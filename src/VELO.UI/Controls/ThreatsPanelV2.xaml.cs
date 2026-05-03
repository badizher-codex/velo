using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VELO.Core.Localization;
using VELO.Security.Threats;

namespace VELO.UI.Controls;

public partial class ThreatsPanelV2 : UserControl
{
    private ThreatsPanelViewModel? _vm;
    private BlockExplanationService? _explainer;

    /// <summary>Raised when the user wants to whitelist a host (currentDomain → entry.Host).</summary>
    public event EventHandler<BlockEntry>? AllowRequested;
    /// <summary>Raised when the user reports a false-negative; host should pre-load Malwaredex.</summary>
    public event EventHandler<BlockEntry>? ReportRequested;
    /// <summary>Raised when the user closes the panel via the ✕ button.</summary>
    public event EventHandler? CloseRequested;

    public ThreatsPanelV2()
    {
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Unloaded += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    /// <summary>
    /// Wires the panel to the DI-registered ViewModel + explanation service.
    /// Called once by the host after construction.
    /// </summary>
    public void SetServices(ThreatsPanelViewModel vm, BlockExplanationService explainer)
    {
        _vm        = vm;
        _explainer = explainer;
        // ObservableCollection mutations from the VM happen on whatever thread
        // ScheduleRecompute runs on; route them to the WPF dispatcher so WPF
        // never sees a non-UI-thread mutation.
        _vm.InvokeOnUi = action => Dispatcher.Invoke(action);
        GroupsList.ItemsSource = _vm.Groups;
        _vm.PropertyChanged += (_, _) => Dispatcher.Invoke(UpdateHeaderAndSummary);
        UpdateHeaderAndSummary();
    }

    private void ApplyLanguage()
    {
        // Header text reused on every recompute via UpdateHeaderAndSummary,
        // but seed something visible on first paint.
        UpdateHeaderAndSummary();
    }

    private void UpdateHeaderAndSummary()
    {
        var L = LocalizationService.Current;
        int total = _vm?.TotalBlocks ?? 0;
        HeaderTitle.Text = total == 1
            ? string.Format(L.T("threatspanel.header.one"), total)
            : string.Format(L.T("threatspanel.header.many"), total);

        if (total == 0)
        {
            SummaryText.Text = L.T("threatspanel.summary.empty");
            return;
        }

        var groups = _vm?.Groups ?? new System.Collections.ObjectModel.ObservableCollection<BlockGroup>();
        var trackers     = groups.SelectMany(g => g.Entries).Count(e => e.Kind == BlockKind.Tracker);
        var malware      = groups.SelectMany(g => g.Entries).Count(e => e.Kind == BlockKind.Malware);
        var ads          = groups.SelectMany(g => g.Entries).Count(e => e.Kind == BlockKind.Ads);
        var fingerprint  = groups.SelectMany(g => g.Entries).Count(e => e.Kind == BlockKind.Fingerprint);

        var parts = new List<string>();
        if (trackers > 0)    parts.Add(string.Format(L.T("threatspanel.summary.trackers"),    trackers));
        if (malware > 0)     parts.Add(string.Format(L.T("threatspanel.summary.malware"),     malware));
        if (ads > 0)         parts.Add(string.Format(L.T("threatspanel.summary.ads"),         ads));
        if (fingerprint > 0) parts.Add(string.Format(L.T("threatspanel.summary.fingerprint"), fingerprint));
        SummaryText.Text = string.Join(" · ", parts);
    }

    // ── Click handlers ─────────────────────────────────────────────────

    private void GroupHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BlockGroup g)
            g.IsExpanded = !g.IsExpanded;
    }

    private async void Explain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not BlockEntry entry || _explainer == null) return;

        // Provide immediate feedback while the (potentially slow) AI call runs.
        var L = LocalizationService.Current;
        entry.Explanation = L.T("threatspanel.explain.loading");
        try
        {
            var text = await _explainer.ExplainAsync(entry);
            entry.Explanation = text;
        }
        catch (Exception ex)
        {
            entry.Explanation = string.Format(L.T("threatspanel.explain.error"), ex.Message);
        }
    }

    private void Allow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BlockEntry entry)
            AllowRequested?.Invoke(this, entry);
    }

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BlockEntry entry)
            ReportRequested?.Invoke(this, entry);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _vm?.Reset();
        UpdateHeaderAndSummary();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try
        {
            var payload = new
            {
                exported_at = DateTime.UtcNow.ToString("O"),
                tab_id      = _vm.CurrentTabId,
                total_blocks = _vm.TotalBlocks,
                by_host     = _vm.Groups.Select(g => new
                {
                    host  = g.Host,
                    count = g.Count,
                    entries = g.Entries.Select(en => new
                    {
                        url        = en.FullUrl,
                        kind       = en.Kind.ToString(),
                        sub_kind   = en.SubKind,
                        blocked_at = en.BlockedAtUtc.ToString("O"),
                        source     = en.Source.ToString(),
                    }).ToList(),
                }).ToList(),
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                $"velo-session-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.WriteAllText(path, json);
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                string.Format(LocalizationService.Current.T("threatspanel.export.ok"), path),
                "VELO", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                string.Format(LocalizationService.Current.T("threatspanel.export.error"), ex.Message),
                "VELO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
