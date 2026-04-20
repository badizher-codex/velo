using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
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
        TimestampText.Text = $"Actualizado: {data.AnalyzedAt.ToLocalTime():HH:mm:ss}";

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
        TlsPanel.Children.Clear();
        AddRow(TlsPanel, d.TlsStatusIcon, "Estado TLS", d.TlsStatusLabel);
        AddRow(TlsPanel, "ℹ️", "Indicador de URL", d.TlsStatusLabel == "Seguro (HTTPS)" ? "Candado cerrado 🔒" : "Advertencia ⚠️");
    }

    private void PopulateBlocks(SecurityInspectorData d)
    {
        BlocksPanel.Children.Clear();
        AddRow(BlocksPanel, d.TrackersBlocked > 0 ? "🚫" : "✅", "Rastreadores bloqueados",
               d.TrackersBlocked == 0 ? "Ninguno detectado" : d.TrackersBlocked.ToString());
        AddRow(BlocksPanel, d.ScriptsBlocked > 0 ? "🚫" : "✅", "Scripts sospechosos",
               d.ScriptsBlocked == 0 ? "Ninguno" : d.ScriptsBlocked.ToString());
        AddRow(BlocksPanel, d.MalwareBlocked > 0 ? "🔴" : "✅", "Malware / phishing",
               d.MalwareBlocked == 0 ? "No detectado" : d.MalwareBlocked.ToString());
    }

    private void PopulateAi(SecurityInspectorData d)
    {
        AiPanel.Children.Clear();
        if (d.AiVerdictLabel == "Sin análisis")
        {
            AddRow(AiPanel, "⏳", "Estado", "Sin análisis disponible para esta sesión");
            return;
        }
        AddRow(AiPanel, d.AiVerdictIcon, "Veredicto", d.AiVerdictLabel);
        if (d.AiConfidence > 0)
            AddRow(AiPanel, "📊", "Confianza", $"{d.AiConfidence}%");
        if (!string.IsNullOrEmpty(d.AiEngine))
            AddRow(AiPanel, "🤖", "Motor", d.AiEngine);
        if (!string.IsNullOrEmpty(d.AiReason))
            AddRow(AiPanel, "📝", "Razón", d.AiReason, wrap: true);
    }

    private void PopulateFingerprint(SecurityInspectorData d)
    {
        FingerprintPanel.Children.Clear();
        if (d.FingerprintActive)
        {
            AddRow(FingerprintPanel, "✅", "Estado",   $"Activa — Nivel: {d.FingerprintLevel}");
            AddRow(FingerprintPanel, "✅", "Canvas",   "Ruido aleatorio inyectado");
            AddRow(FingerprintPanel, "✅", "WebGL",    "Renderer falso activo");
            AddRow(FingerprintPanel, "✅", "AudioCtx", "Ruido en AudioContext");
            AddRow(FingerprintPanel, "✅", "WebRTC",   "IPs locales ocultas");
        }
        else
        {
            AddRow(FingerprintPanel, "⚠️", "Estado", "Desactivada en Configuración");
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

    private static string ShieldLabel(SafetyLevel level) => level switch
    {
        SafetyLevel.Gold      => "Excelente (Gold)",
        SafetyLevel.Green     => "Seguro (Verde)",
        SafetyLevel.Yellow    => "Precaución (Amarillo)",
        SafetyLevel.Red       => "Peligro (Rojo)",
        _                     => "Analizando…",
    };

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    // ── Action handlers ──────────────────────────────────────────────────

    private void DevTools_Click(object sender, RoutedEventArgs e)
        => OpenDevToolsRequested?.Invoke();

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var original = btn.Content;
            btn.IsEnabled = false;
            btn.Content   = "⏳ Escaneando…";
            ForceScanRequested?.Invoke();
            await Task.Delay(800);
            btn.Content   = "✓ Actualizado";
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
            Title            = "Exportar análisis de seguridad",
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
