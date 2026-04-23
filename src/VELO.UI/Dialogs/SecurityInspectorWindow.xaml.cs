using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using VELO.Core.Localization;
using VELO.Security.Models;

namespace VELO.UI.Dialogs;

/// <summary>
/// Snapshot of a tab's security state at a point in time, passed to the inspector window.
/// </summary>
public sealed record SecurityInspectorData(
    string   Url,
    string   Domain,
    SafetyLevel ShieldLevel,
    int      ShieldScore,
    string[] ReasonsPositive,
    string[] ReasonsNegative,
    string   TlsStatusLabel,
    string   TlsStatusIcon,
    int      TrackersBlocked,
    int      ScriptsBlocked,
    int      MalwareBlocked,
    string   AiVerdictLabel,
    string   AiVerdictIcon,
    int      AiConfidence,
    string   AiReason,
    string   AiEngine,
    bool     FingerprintActive,
    string   FingerprintLevel,
    DateTime AnalyzedAt);

/// <summary>
/// Standalone VELO Security Inspector window — shows security details for the active tab.
/// Not modal; can stay open while the user browses.
/// </summary>
public partial class SecurityInspectorWindow : Window
{
    private SecurityInspectorData? _data;

    // Callbacks wired by MainWindow so the inspector can request actions
    public Action? OpenDevToolsRequested { get; set; }
    public Action? ForceScanRequested    { get; set; }

    public SecurityInspectorWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        TlsSectionTitle.Text          = L.T("inspector.section.tls");
        BlocksSectionTitle.Text       = L.T("inspector.section.blocks");
        AiSectionTitle.Text           = L.T("inspector.section.ai");
        FpSectionTitle.Text           = L.T("inspector.section.fingerprint");
        ScoreSectionTitle.Text        = L.T("inspector.section.score");
        DevToolsButton.Content        = L.T("inspector.btn.devtools");
        ExportButton.Content          = L.T("inspector.btn.export");
        RescanButton.Content          = L.T("inspector.btn.rescan");
        // Re-render panels if data is available
        if (_data != null) Refresh(_data);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Refreshes all sections with the provided snapshot.</summary>
    public void Refresh(SecurityInspectorData data)
    {
        _data = data;

        // Window title
        Title = $"VELO Security Inspector — {data.Domain}";

        // Header
        DomainText.Text    = data.Domain;
        UrlText.Text       = data.Url;
        TimestampText.Text = $"{LocalizationService.Current.T("inspector.updated")} {data.AnalyzedAt.ToLocalTime():HH:mm:ss}";

        // Shield badge
        var (badgeBg, badgeBorder, levelFg) = ShieldColors(data.ShieldLevel);
        ShieldBadgeBorder.Background  = Brush(badgeBg);
        ShieldBadgeBorder.BorderBrush = Brush(badgeBorder);
        ShieldIcon.Text               = ShieldIcon_(data.ShieldLevel);
        ShieldLevelText.Text          = ShieldLabel(data.ShieldLevel);
        ShieldLevelText.Foreground    = Brush(levelFg);
        ShieldScoreText.Text          = $"Score: {data.ShieldScore:+#;-#;0}";

        // TLS section
        PopulateTls(data);

        // Blocks section
        PopulateBlocks(data);

        // AI section
        PopulateAi(data);

        // Fingerprint section
        PopulateFingerprint(data);

        // Reasons
        PopulateReasons(data);
    }

    // ── Section builders ─────────────────────────────────────────────────

    private void PopulateTls(SecurityInspectorData d)
    {
        var L = LocalizationService.Current;
        TlsPanel.Children.Clear();
        AddRow(TlsPanel, d.TlsStatusIcon, L.T("inspector.tls.status"), d.TlsStatusLabel);
        var isSecure = d.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        AddRow(TlsPanel, "ℹ️", L.T("inspector.tls.indicator"), isSecure ? L.T("inspector.tls.lock") : L.T("inspector.tls.warning"));
    }

    private void PopulateBlocks(SecurityInspectorData d)
    {
        var L = LocalizationService.Current;
        BlocksPanel.Children.Clear();
        AddRow(BlocksPanel, d.TrackersBlocked > 0 ? "🚫" : "✅", L.T("inspector.blocks.trackers"),
               d.TrackersBlocked == 0 ? L.T("inspector.blocks.none") : d.TrackersBlocked.ToString());
        AddRow(BlocksPanel, d.ScriptsBlocked > 0 ? "🚫" : "✅", L.T("inspector.blocks.scripts"),
               d.ScriptsBlocked == 0 ? L.T("inspector.blocks.nonescript") : d.ScriptsBlocked.ToString());
        AddRow(BlocksPanel, d.MalwareBlocked > 0 ? "🔴" : "✅", L.T("inspector.blocks.malware"),
               d.MalwareBlocked == 0 ? L.T("inspector.blocks.nomalware") : d.MalwareBlocked.ToString());
    }

    private void PopulateAi(SecurityInspectorData d)
    {
        var L = LocalizationService.Current;
        AiPanel.Children.Clear();
        if (d.AiVerdictLabel == "Sin análisis")
        {
            AddRow(AiPanel, "⏳", L.T("inspector.ai.status.label"), L.T("inspector.ai.noanalysis"));
            return;
        }
        AddRow(AiPanel, d.AiVerdictIcon, L.T("inspector.ai.verdict"), d.AiVerdictLabel);
        if (d.AiConfidence > 0)
            AddRow(AiPanel, "📊", L.T("inspector.ai.confidence"), $"{d.AiConfidence}%");
        if (!string.IsNullOrEmpty(d.AiEngine))
            AddRow(AiPanel, "🤖", L.T("inspector.ai.engine"), d.AiEngine);
        if (!string.IsNullOrEmpty(d.AiReason))
            AddRow(AiPanel, "📝", L.T("inspector.ai.reason"), d.AiReason, wrap: true);
    }

    private void PopulateFingerprint(SecurityInspectorData d)
    {
        var L = LocalizationService.Current;
        FingerprintPanel.Children.Clear();
        if (d.FingerprintActive)
        {
            AddRow(FingerprintPanel, "✅", L.T("inspector.fp.status"),   $"{L.T("inspector.fp.active")} {d.FingerprintLevel}");
            AddRow(FingerprintPanel, "✅", "Canvas",                      L.T("inspector.fp.canvas.value"));
            AddRow(FingerprintPanel, "✅", "WebGL",                       L.T("inspector.fp.webgl.value"));
            AddRow(FingerprintPanel, "✅", "AudioCtx",                    L.T("inspector.fp.audio.value"));
            AddRow(FingerprintPanel, "✅", "WebRTC",                      L.T("inspector.fp.webrtc.value"));
        }
        else
        {
            AddRow(FingerprintPanel, "⚠️", L.T("inspector.fp.status"), L.T("inspector.fp.inactive"));
        }
    }

    private void PopulateReasons(SecurityInspectorData d)
    {
        var hasReasons = d.ReasonsPositive.Length > 0 || d.ReasonsNegative.Length > 0;
        ReasonsSection.Visibility = hasReasons ? Visibility.Visible : Visibility.Collapsed;
        if (!hasReasons) return;

        ReasonsPanel.Children.Clear();
        foreach (var r in d.ReasonsPositive)
            AddRow(ReasonsPanel, "✅", "", r, foreground: "#FF2EB54F", wrap: true);
        foreach (var r in d.ReasonsNegative)
            AddRow(ReasonsPanel, "⚠️", "", r, foreground: "#FFF0B429", wrap: true);
    }

    // ── Row factory ──────────────────────────────────────────────────────

    private static void AddRow(Panel parent, string icon, string label, string value,
        bool wrap = false, string? foreground = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

        sp.Children.Add(new TextBlock
        {
            Text = icon + " ",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 4, 0)
        });

        if (!string.IsNullOrEmpty(label))
        {
            sp.Children.Add(new TextBlock
            {
                Text = label + ": ",
                FontSize = 13,
                Foreground = Brush("#FFB0B0B0"),
                MinWidth = 150,
                VerticalAlignment = VerticalAlignment.Top
            });
        }

        sp.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 13,
            Foreground = foreground != null ? Brush(foreground) : Brush("#FFFFFFFF"),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = 380,
            VerticalAlignment = VerticalAlignment.Top
        });

        parent.Children.Add(sp);
    }

    // ── Colour helpers ───────────────────────────────────────────────────

    private static (string Bg, string Border, string Fg) ShieldColors(SafetyLevel level) => level switch
    {
        SafetyLevel.Gold      => ("#FF2A2208", "#FFD4AF37", "#FFD4AF37"),
        SafetyLevel.Green     => ("#FF0E2410", "#FF2EB54F", "#FF2EB54F"),
        SafetyLevel.Yellow    => ("#FF2A2008", "#FFF0B429", "#FFF0B429"),
        SafetyLevel.Red       => ("#FF2A0808", "#FFE53E3E", "#FFE53E3E"),
        _                     => ("#FF1A1A1A", "#FF555555", "#FF888888"),
    };

    private static string ShieldIcon_(SafetyLevel level) => level switch
    {
        SafetyLevel.Gold      => "🥇",
        SafetyLevel.Green     => "🛡",
        SafetyLevel.Yellow    => "⚠",
        SafetyLevel.Red       => "🔴",
        _                     => "⏳",
    };

    private static string ShieldLabel(SafetyLevel level)
    {
        var L = LocalizationService.Current;
        return level switch
        {
            SafetyLevel.Gold      => L.T("inspector.shield.gold"),
            SafetyLevel.Green     => L.T("inspector.shield.green"),
            SafetyLevel.Yellow    => L.T("inspector.shield.yellow"),
            SafetyLevel.Red       => L.T("inspector.shield.red"),
            _                     => L.T("inspector.shield.analyzing"),
        };
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    // ── Action handlers ──────────────────────────────────────────────────

    private void DevTools_Click(object sender, RoutedEventArgs e)
        => OpenDevToolsRequested?.Invoke();

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var L        = LocalizationService.Current;
            var original = btn.Content;
            btn.IsEnabled = false;
            btn.Content   = L.T("inspector.btn.scanning");
            ForceScanRequested?.Invoke();
            await Task.Delay(800);
            btn.Content   = L.T("inspector.btn.updated");
            await Task.Delay(1200);
            btn.Content   = original;
            btn.IsEnabled = true;
        }
        else
        {
            ForceScanRequested?.Invoke();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null) return;

        var dialog = new SaveFileDialog
        {
            Title            = LocalizationService.Current.T("inspector.export.title"),
            Filter           = "JSON (*.json)|*.json",
            FileName         = $"velo-security-{_data.Domain}-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt       = ".json",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var export = new
            {
                schema_version = 1,
                exported_at    = _data.AnalyzedAt.ToString("o"),
                domain         = _data.Domain,
                url            = _data.Url,
                shield = new
                {
                    level        = _data.ShieldLevel.ToString().ToLowerInvariant(),
                    score        = _data.ShieldScore,
                    reasons_positive = _data.ReasonsPositive,
                    reasons_negative = _data.ReasonsNegative,
                },
                tls = new
                {
                    status = _data.TlsStatusLabel,
                },
                blocks_this_session = new
                {
                    trackers = _data.TrackersBlocked,
                    scripts  = _data.ScriptsBlocked,
                    malware  = _data.MalwareBlocked,
                },
                ai_analysis = new
                {
                    verdict    = _data.AiVerdictLabel,
                    confidence = _data.AiConfidence,
                    engine     = _data.AiEngine,
                    reason     = _data.AiReason,
                },
                fingerprint_protection = new
                {
                    active = _data.FingerprintActive,
                    level  = _data.FingerprintLevel,
                },
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);

            MessageBox.Show($"Análisis exportado a:\n{dialog.FileName}",
                "VELO Security Inspector", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al exportar:\n{ex.Message}",
                "VELO Security Inspector", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
