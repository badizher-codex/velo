using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VELO.Core.Localization;
using VELO.Security;
using VELO.Security.AI.Models;
using VELO.Security.Guards;

namespace VELO.UI.Controls;

public partial class SecurityPanel : UserControl
{
    // ── Public API (unchanged from v1) ───────────────────────────────────────
    public event EventHandler<string>? AllowOnceRequested;
    public event EventHandler<string>? WhitelistRequested;

    // ── State ─────────────────────────────────────────────────────────────────
    private string _currentDomain = "";
    private string _currentLearnMoreUrl = "";
    private string _currentFalsePositiveUrl = "";
    private int _sessionThreatCount = 0;
    private bool _hasActiveWarn = false;

    // Grouping: tracks recent events to collapse duplicates
    private readonly List<(DateTime Time, string Domain, VerdictType Verdict)> _recentEvents = [];

    // Session log (cleared on browser close)
    private readonly List<SessionLogEntry> _sessionLog = [];

    private readonly ExplanationGenerator _explanationGenerator = new();

    // Auto-collapse timer
    private readonly DispatcherTimer _collapseTimer  = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft;

    public SecurityPanel()
    {
        InitializeComponent();
        _collapseTimer.Tick  += (_, _) => { _collapseTimer.Stop(); _countdownTimer.Stop(); CollapseToMiniMode(); };
        _countdownTimer.Tick += (_, _) =>
        {
            _secondsLeft--;
            CountdownLabel.Text = _secondsLeft > 0
                ? string.Format(LocalizationService.Current.T("security.countdown"), _secondsLeft)
                : "";
        };

        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Unloaded += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    /// <summary>
    /// v2.0.5.2 — Pulls every static label from <see cref="LocalizationService"/>
    /// so a language change in Settings updates the panel live. The dynamic
    /// strings (WhatHappenedLabel/WhyBlockedLabel/WhatItMeansLabel) come from
    /// <see cref="ExplanationGenerator"/> using the active language.
    /// </summary>
    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;

        WhatHappenedHeader.Text = L.T("security.what_happened");
        WhyBlockedHeader.Text   = L.T("security.why_blocked");
        WhatItMeansHeader.Text  = L.T("security.what_means");
        LearnMoreLink.Text      = L.T("security.learn_more");

        TechnicalDetails.Header = L.T("security.tech_details");
        TypeHeader.Text         = L.T("security.label.type");
        SourceHeader.Text       = L.T("security.label.source");
        ConfidenceHeader.Text   = L.T("security.label.confidence");
        ScoreHeader.Text        = L.T("security.label.score");

        FalsePositiveLink.Text  = L.T("security.false_positive");
        AllowOnceButton.Content = L.T("security.allow_once");
        WhitelistButton.Content = L.T("security.whitelist");

        MinimizeButton.ToolTip  = L.T("security.minimize");
        CloseButton.ToolTip     = L.T("security.close");

        // Re-apply the verdict label if currently shown (preserves color via brush).
        if (VerdictLabel.Tag is VerdictType v) VerdictLabel.Text = VerdictLabelFor(v);
    }

    private static string VerdictLabelFor(VerdictType v) => v switch
    {
        VerdictType.Block => LocalizationService.Current.T("security.verdict.block"),
        VerdictType.Warn  => LocalizationService.Current.T("security.verdict.warn"),
        _                 => LocalizationService.Current.T("security.verdict.safe"),
    };

    // ── Show (primary entry point — same signature as v1 + overload) ─────────

    public void Show(string domain, AIVerdict verdict)
    {
        var sv = new SecurityVerdict
        {
            Verdict    = verdict.Verdict,
            Confidence = verdict.Confidence,
            Reason     = verdict.Reason,
            ThreatType = verdict.ThreatType,
            Source     = verdict.Source,
        };
        ShowVerdict(domain, sv);
    }

    public void ShowSecurityVerdict(string domain, SecurityVerdict verdict)
        => ShowVerdict(domain, verdict);

    private void ShowVerdict(string domain, SecurityVerdict verdict)
    {
        _currentDomain = domain;

        // Append to session log
        _sessionLog.Add(new SessionLogEntry(DateTime.Now, domain, verdict.Verdict, verdict.ThreatType, verdict.Reason));

        // Check for grouping (>5 same type+domain in <30s)
        PruneRecentEvents();
        _recentEvents.Add((DateTime.Now, domain, verdict.Verdict));
        int similarCount = CountSimilarRecent(domain, verdict.Verdict);

        // Increment session counters
        _sessionThreatCount++;
        if (verdict.Verdict == VerdictType.Warn) _hasActiveWarn = true;

        // Generate v2 explanation in the active UI language
        var explanation = _explanationGenerator.Generate(verdict, LocalizationService.Current.Language);
        _currentLearnMoreUrl = explanation.LearnMoreUrl ?? "";

        // Build false-positive GitHub URL (no personal data)
        _currentFalsePositiveUrl = BuildFalsePositiveUrl(domain, verdict);

        // v2.0.5.5 — DownloadGuard already writes a precise, user-facing reason
        // (cross-origin exec, burst attack, dangerous-extension warning…). Using
        // the generic Malware template was misleading — it cited "VELO's Malwaredex
        // database" when the real block came from a cross-origin heuristic. When
        // the source is DownloadGuard, surface the verdict's own Reason instead.
        bool useVerdictReason = verdict.Source == "DownloadGuard"
                                && !string.IsNullOrWhiteSpace(verdict.Reason);

        // Populate UI
        DomainLabel.Text       = $"📍 {domain}";
        WhatHappenedLabel.Text = similarCount > 5
            ? string.Format(LocalizationService.Current.T("security.events_grouped"), similarCount, domain)
            : useVerdictReason ? verdict.Reason : explanation.WhatHappened;
        WhyBlockedLabel.Text   = useVerdictReason ? "" : explanation.WhyBlocked;
        WhatItMeansLabel.Text  = useVerdictReason ? "" : explanation.WhatItMeans;
        WhyBlockedHeader.Visibility   = useVerdictReason ? Visibility.Collapsed : Visibility.Visible;
        WhyBlockedLabel.Visibility    = useVerdictReason ? Visibility.Collapsed : Visibility.Visible;
        WhatItMeansHeader.Visibility  = useVerdictReason ? Visibility.Collapsed : Visibility.Visible;
        WhatItMeansLabel.Visibility   = useVerdictReason ? Visibility.Collapsed : Visibility.Visible;

        LearnMoreLink.Visibility  = string.IsNullOrEmpty(_currentLearnMoreUrl)
            ? Visibility.Collapsed : Visibility.Visible;

        // Technical details
        ThreatTypeLabel.Text = verdict.ThreatType == ThreatType.None ? "—" : verdict.ThreatType.ToString();
        SourceLabel.Text     = verdict.Source;
        ConfidenceLabel.Text = $"{verdict.Confidence}%";
        ScoreLabel.Text      = "—";

        // Header colors
        ApplyVerdictStyle(verdict.Verdict);

        // Update chip
        UpdateChip();

        // Show expanded panel
        Visibility             = Visibility.Visible;
        MiniTab.Visibility     = Visibility.Collapsed;
        PanelBorder.Visibility = Visibility.Visible;

        // Restart auto-collapse
        _collapseTimer.Stop();
        _countdownTimer.Stop();
        _secondsLeft = 6;
        CountdownLabel.Text = string.Format(LocalizationService.Current.T("security.countdown"), _secondsLeft);
        _collapseTimer.Start();
        _countdownTimer.Start();
    }

    // ── Hide (v1-compatible) ─────────────────────────────────────────────────

    public void Hide()
    {
        _collapseTimer.Stop();
        _countdownTimer.Stop();
        _sessionThreatCount = 0;
        _hasActiveWarn      = false;
        _recentEvents.Clear();
        _sessionLog.Clear();
        Visibility             = Visibility.Collapsed;
        MiniTab.Visibility     = Visibility.Collapsed;
        PanelBorder.Visibility = Visibility.Collapsed;
    }

    // ── Session log access ───────────────────────────────────────────────────

    public IReadOnlyList<SessionLogEntry> GetSessionLog() => _sessionLog.AsReadOnly();

    // ── Private helpers ──────────────────────────────────────────────────────

    private void ApplyVerdictStyle(VerdictType verdict)
    {
        var (borderHex, icon) = verdict switch
        {
            VerdictType.Block => ("#E53E3E", "🔴"),
            VerdictType.Warn  => ("#F0B429", "⚠️"),
            _                 => ("#2EB54F", "✅"),
        };

        var color = (Color)ColorConverter.ConvertFromString(borderHex);
        var brush = new SolidColorBrush(color);

        PanelBorder.BorderBrush = brush;
        VerdictIcon.Text        = icon;
        VerdictLabel.Text       = VerdictLabelFor(verdict);
        VerdictLabel.Foreground = brush;
        VerdictLabel.Tag        = verdict; // remembered so ApplyLanguage can re-localize
    }

    private void UpdateChip()
    {
        var color = _hasActiveWarn
            ? (Color)ColorConverter.ConvertFromString("#F0B429")
            : (Color)ColorConverter.ConvertFromString("#E53E3E");

        MiniBadge.Background   = new SolidColorBrush(color);
        MiniBadgeCount.Text    = _sessionThreatCount > 99 ? "99+" : _sessionThreatCount.ToString();
        MiniIcon.Text          = _hasActiveWarn ? "⚠️" : "🔴";
        MiniWarnIndicator.Visibility = _hasActiveWarn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PruneRecentEvents()
    {
        var cutoff = DateTime.Now.AddSeconds(-30);
        _recentEvents.RemoveAll(e => e.Time < cutoff);
    }

    private int CountSimilarRecent(string domain, VerdictType verdict)
        => _recentEvents.Count(e => e.Domain == domain && e.Verdict == verdict);

    private static string BuildFalsePositiveUrl(string domain, SecurityVerdict verdict)
    {
        // Encodes domain + threat type only — no URL, no query params, no personal data
        var title = Uri.EscapeDataString($"False positive: {domain} ({verdict.ThreatType})");
        var body  = Uri.EscapeDataString(
            $"**Domain:** {domain}\n" +
            $"**Threat type:** {verdict.ThreatType}\n" +
            $"**Source:** {verdict.Source}\n" +
            $"**VELO version:** (please check Help → About)\n\n" +
            $"**Why do you think this is a false positive?**\n\n(Please describe here)");

        return $"https://github.com/badizher-codex/velo/issues/new" +
               $"?template=false_positive.yml&title={title}&body={body}";
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void CollapseToMiniMode()
    {
        PanelBorder.Visibility = Visibility.Collapsed;
        MiniTab.Visibility     = Visibility.Visible;
        Visibility             = Visibility.Visible;
        CountdownLabel.Text    = "";
    }

    private void CollapseToMini_Click(object sender, RoutedEventArgs e)
    {
        _collapseTimer.Stop();
        _countdownTimer.Stop();
        CollapseToMiniMode();
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => Hide();

    private void MiniTab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        MiniTab.Visibility     = Visibility.Collapsed;
        PanelBorder.Visibility = Visibility.Visible;

        _collapseTimer.Stop();
        _countdownTimer.Stop();
        _secondsLeft = 6;
        CountdownLabel.Text = $"Cerrando en {_secondsLeft}s…";
        _collapseTimer.Start();
        _countdownTimer.Start();
    }

    private void AllowOnce_Click(object sender, RoutedEventArgs e)
    {
        // v2.0.5.4 — Plumb the override into BOTH guards. Previously only
        // RequestGuard saw it, so downloads continued to hit DownloadGuard.Block.
        DownloadGuard.AllowOnce(_currentDomain);
        AllowOnceRequested?.Invoke(this, _currentDomain);
        Hide();
    }

    private void Whitelist_Click(object sender, RoutedEventArgs e)
    {
        RequestGuard.AddToWhitelist(_currentDomain);
        DownloadGuard.Whitelist(_currentDomain);
        WhitelistRequested?.Invoke(this, _currentDomain);
        Hide();
    }

    private void LearnMore_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentLearnMoreUrl)) return;
        // velo://docs/threats/* — handled by MainWindow navigation
        LearnMoreRequested?.Invoke(this, _currentLearnMoreUrl);
    }

    private void FalsePositive_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFalsePositiveUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = _currentFalsePositiveUrl,
                UseShellExecute = true
            });
        }
        catch { /* silencioso si el navegador predeterminado no está disponible */ }
    }

    // ── Additional events (Fase 2) ───────────────────────────────────────────
    public event EventHandler<string>? LearnMoreRequested;
}

// ── Session log entry ────────────────────────────────────────────────────────

public record SessionLogEntry(
    DateTime Timestamp,
    string Domain,
    VerdictType Verdict,
    ThreatType ThreatType,
    string Reason);
