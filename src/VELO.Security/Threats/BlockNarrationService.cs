using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Security.Threats;

/// <summary>
/// Phase 3 / Sprint 8C — Decides which blocks deserve a proactive
/// human-readable narration ("VELO blocked Google Analytics — they
/// were tracking your scroll position to retarget you elsewhere") and
/// surfaces the result via <see cref="NarrationReady"/> for the UI to
/// render as a discrete toast. The actual explanation text comes from
/// <see cref="BlockExplanationService"/>; this class is the policy
/// layer that decides WHEN to narrate.
///
/// Why a separate service?
///
/// • <b>Most blocks are routine.</b> Every page-load racks up dozens of
///   static-blocklist hits (DoubleClick, Facebook Pixel, etc.). If we
///   narrated every one the user would drown in toasts. The policy here
///   skips routine sources and only narrates "interesting" blocks — AI
///   verdicts, Malwaredex hits, RequestGuard heuristics, phishing.
///
/// • <b>Throttle is mandatory.</b> Per-host cooldown prevents a single
///   tracker from triggering ten toasts as the page loads its assets.
///   A global per-minute cap prevents narration spam if a page is
///   particularly hostile.
///
/// • <b>Settings opt-in.</b> The UI layer (MainWindow) checks a setting
///   before subscribing to <see cref="NarrationReady"/>. Off by default
///   so v2.3.0 ships without changing default UX.
///
/// Pure (no I/O beyond the explanation service). Tests in
/// <c>BlockNarrationServiceTests</c>.
/// </summary>
public sealed class BlockNarrationService
{
    public sealed record Narration(
        string TabId,
        string Host,
        string Kind,
        string Source,
        string Text);

    /// <summary>
    /// Sources we DON'T narrate by default — routine block-list noise.
    /// Override via <see cref="QuietSources"/> if you want to suppress
    /// more, or clear the set to narrate everything.
    /// </summary>
    public HashSet<string> QuietSources { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "BLOCKLIST",
        "EasyPrivacy",
        "GoldenList",
        "StaticList",
    };

    /// <summary>Per-host cooldown — once we narrate a host, we don't again until this elapses.</summary>
    public TimeSpan PerHostCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum narrations per minute (global). 0 disables.</summary>
    public int MaxNarrationsPerMinute { get; set; } = 6;

    /// <summary>Raised when a narration is ready for the UI to display.</summary>
    public event Action<Narration>? NarrationReady;

    private readonly BlockExplanationService _explainer;
    private readonly ILogger<BlockNarrationService> _logger;
    private readonly Dictionary<string, DateTime> _lastNarrationByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recentNarrations = new();
    private readonly object _lock = new();

    public BlockNarrationService(
        BlockExplanationService explainer,
        ILogger<BlockNarrationService>? logger = null)
    {
        _explainer = explainer;
        _logger    = logger ?? NullLogger<BlockNarrationService>.Instance;
    }

    /// <summary>
    /// Considers a block for narration. Returns the narration if it was
    /// emitted (and raises <see cref="NarrationReady"/>), or null when
    /// the policy declined (routine source, cooldown, throttle, or
    /// empty inputs).
    /// </summary>
    public async Task<Narration?> ConsiderAsync(
        string tabId,
        string host,
        string kind,
        string subKind,
        string source,
        bool   isMalwaredexHit,
        int    confidence,
        string fullUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(source))
            return null;

        // 1. Routine-source filter. Narrate only "interesting" blocks.
        //    Malwaredex hits jump the gate even when source is quiet —
        //    confirmed-malware blocks are always worth surfacing.
        if (!isMalwaredexHit && QuietSources.Contains(source))
            return null;

        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            // 2. Per-host cooldown.
            if (_lastNarrationByHost.TryGetValue(host, out var lastAt) &&
                (now - lastAt) < PerHostCooldown)
            {
                return null;
            }

            // 3. Global per-minute throttle.
            if (MaxNarrationsPerMinute > 0)
            {
                while (_recentNarrations.Count > 0 &&
                       (now - _recentNarrations.Peek()) > TimeSpan.FromMinutes(1))
                    _recentNarrations.Dequeue();

                if (_recentNarrations.Count >= MaxNarrationsPerMinute)
                {
                    _logger.LogDebug("Narration throttled — {Count}/min cap reached", MaxNarrationsPerMinute);
                    return null;
                }
            }

            // Reserve our slot before the await so concurrent callers don't
            // all squeeze through the gate at once.
            _lastNarrationByHost[host] = now;
            _recentNarrations.Enqueue(now);
        }

        // 4. Explanation. The explainer caches per (host, kind, subkind),
        //    so repeated misses on the same domain don't burn the model.
        var entry = BuildEntry(host, kind, subKind, source, isMalwaredexHit, confidence, fullUrl);
        string text;
        try
        {
            text = await _explainer.ExplainAsync(entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Narration explainer failed for {Host}", host);
            return null;
        }

        if (string.IsNullOrWhiteSpace(text)) return null;

        var narration = new Narration(tabId, host, kind, source, text.Trim());
        try { NarrationReady?.Invoke(narration); }
        catch (Exception ex) { _logger.LogWarning(ex, "Narration subscriber threw"); }

        return narration;
    }

    /// <summary>Test helper.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastNarrationByHost.Clear();
            _recentNarrations.Clear();
        }
    }

    /// <summary>Test helper.</summary>
    public int RecentNarrationCount
    {
        get { lock (_lock) return _recentNarrations.Count; }
    }

    private static BlockEntry BuildEntry(
        string host, string kind, string subKind, string source,
        bool isMalwaredexHit, int confidence, string fullUrl)
    {
        // Map string kind → enum; default to Other for forward-compat.
        var kindEnum = Enum.TryParse<BlockKind>(kind, ignoreCase: true, out var k) ? k : BlockKind.Other;
        var sourceEnum = Enum.TryParse<BlockSource>(source, ignoreCase: true, out var s) ? s : BlockSource.RequestGuard;

        return new BlockEntry
        {
            Host            = host,
            FullUrl         = fullUrl,
            Kind            = kindEnum,
            SubKind         = subKind,
            Source          = sourceEnum,
            IsMalwaredexHit = isMalwaredexHit,
            Confidence      = confidence,
        };
    }
}
