using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using VELO.Core.Events;

namespace VELO.Security.Threats;

/// <summary>
/// Phase 3 / Sprint 1 — ViewModel backing ThreatsPanelV2. Subscribes to
/// <see cref="BlockedRequestEvent"/> and <see cref="TabActivatedEvent"/>
/// from the EventBus and produces a per-tab grouped view. Per spec § 2.5
/// the recompute is debounced (default 250 ms) so a page load that yields
/// 50 verdicts in a burst doesn't thrash the UI.
///
/// The ViewModel is UI-framework agnostic — debounce uses a Timer that
/// invokes a delegate the host installs (defaults to in-process direct
/// call, which is what tests use). The WPF host wires it to the
/// dispatcher so changes land on the UI thread.
/// </summary>
public class ThreatsPanelViewModel : INotifyPropertyChanged
{
    private readonly EventBus _eventBus;
    private readonly ILogger<ThreatsPanelViewModel>? _logger;
    private readonly TimeSpan _debounce;

    // Per-tab raw blocks (insertion order)
    private readonly Dictionary<string, List<BlockEntry>> _tabBlocks = [];

    // Currently displayed grouped view — bound by the UserControl
    public ObservableCollection<BlockGroup> Groups { get; } = [];

    private string _currentTabId = "";
    public string CurrentTabId
    {
        get => _currentTabId;
        set
        {
            if (_currentTabId == value) return;
            _currentTabId = value;
            ScheduleRecompute();
        }
    }

    public int TotalBlocks => _tabBlocks.TryGetValue(_currentTabId, out var list) ? list.Count : 0;

    /// <summary>Test seam — replaced by the WPF host with Dispatcher.BeginInvoke.</summary>
    public Action<Action> InvokeOnUi { get; set; } = a => a();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThreatsPanelViewModel(
        EventBus eventBus,
        ILogger<ThreatsPanelViewModel>? logger = null,
        TimeSpan? debounce = null)
    {
        _eventBus = eventBus;
        _logger   = logger;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);

        _eventBus.Subscribe<BlockedRequestEvent>(OnBlocked);
        _eventBus.Subscribe<TabActivatedEvent>(OnTabActivated);
        _eventBus.Subscribe<TabClosedEvent>(OnTabClosed);
    }

    // ── Event handlers ─────────────────────────────────────────────────

    private void OnBlocked(BlockedRequestEvent e)
    {
        if (string.IsNullOrEmpty(e.TabId)) return;

        if (!_tabBlocks.TryGetValue(e.TabId, out var list))
        {
            list = [];
            _tabBlocks[e.TabId] = list;
        }

        var entry = new BlockEntry
        {
            Host             = e.Host,
            FullUrl          = e.FullUrl,
            Kind             = ParseKind(e.Kind),
            SubKind          = e.SubKind,
            BlockedAtUtc     = e.BlockedAtUtc,
            Source           = ParseSource(e.Source),
            IsMalwaredexHit  = e.IsMalwaredexHit,
            Confidence       = e.Confidence,
            TabId            = e.TabId,
        };
        list.Add(entry);

        if (e.TabId == _currentTabId)
            ScheduleRecompute();
    }

    private void OnTabActivated(TabActivatedEvent e) => CurrentTabId = e.TabId;

    private void OnTabClosed(TabClosedEvent e)
    {
        if (_tabBlocks.Remove(e.TabId) && e.TabId == _currentTabId)
            ScheduleRecompute();
    }

    // ── Public API for the host ────────────────────────────────────────

    public IReadOnlyList<BlockEntry> GetBlocksForTab(string tabId)
        => _tabBlocks.TryGetValue(tabId, out var list) ? list : [];

    public IReadOnlyList<BlockEntry> GetBlocksForHost(string host)
    {
        if (!_tabBlocks.TryGetValue(_currentTabId, out var list)) return [];
        return list.Where(e => string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Test helper — clears all per-tab state.</summary>
    public void Reset()
    {
        _tabBlocks.Clear();
        Groups.Clear();
    }

    /// <summary>
    /// Runs the recompute immediately, bypassing debounce, and cancels any
    /// in-flight scheduled task so a stale wakeup can't repopulate Groups
    /// between this call and a subsequent assertion.
    /// </summary>
    public void RecomputeNow()
    {
        _pendingCts?.Cancel();
        Recompute();
    }

    // ── Debounced recompute ────────────────────────────────────────────

    private CancellationTokenSource? _pendingCts;

    private void ScheduleRecompute()
    {
        // debounce == 0 → run inline. Removes the threadpool race that made
        // the tab-change test flaky and keeps unit tests deterministic.
        if (_debounce <= TimeSpan.Zero)
        {
            _pendingCts?.Cancel();
            InvokeOnUi(Recompute);
            return;
        }

        // Cancel any in-flight debounce window and start a new one. The
        // last call within _debounce wins. Token is captured so a fast
        // follow-up doesn't accidentally fire two recomputes.
        _pendingCts?.Cancel();
        _pendingCts = new CancellationTokenSource();
        var ct = _pendingCts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_debounce, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            InvokeOnUi(Recompute);
        });
    }

    private void Recompute()
    {
        Groups.Clear();
        if (!_tabBlocks.TryGetValue(_currentTabId, out var blocks) || blocks.Count == 0)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBlocks)));
            return;
        }

        // Group by host (case-insensitive).
        var byHost = blocks
            .GroupBy(b => string.IsNullOrEmpty(b.Host) ? "system" : b.Host,
                     StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var grp = new BlockGroup { Host = g.Key };
                foreach (var entry in g) grp.Add(entry);
                return grp;
            })
            // Sort: Malwaredex hits first, then by Count desc, then by latest.
            .OrderByDescending(g => g.IsMalwaredexHit)
            .ThenByDescending(g => g.Count)
            .ThenByDescending(g => g.LatestUtc)
            .ToList();

        // Default expansion: only the first group.
        for (int i = 0; i < byHost.Count; i++)
            byHost[i].IsExpanded = i == 0;

        foreach (var g in byHost) Groups.Add(g);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBlocks)));
    }

    // ── Parse helpers ──────────────────────────────────────────────────

    private static BlockKind ParseKind(string raw) =>
        Enum.TryParse<BlockKind>(raw, ignoreCase: true, out var v) ? v : BlockKind.Other;

    private static BlockSource ParseSource(string raw) =>
        Enum.TryParse<BlockSource>(raw, ignoreCase: true, out var v) ? v : BlockSource.RequestGuard;
}
