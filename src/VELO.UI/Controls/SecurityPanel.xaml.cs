using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VELO.Security.AI.Models;
using VELO.Security.Guards;

namespace VELO.UI.Controls;

public partial class SecurityPanel : UserControl
{
    public event EventHandler<string>? AllowOnceRequested;
    public event EventHandler<string>? WhitelistRequested;

    private string _currentDomain = "";
    private int    _pageThreats   = 0;

    // Auto-collapse timer
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft;

    public SecurityPanel()
    {
        InitializeComponent();
        _collapseTimer.Tick  += (_, _) => { _collapseTimer.Stop(); _countdownTimer.Stop(); CollapseToMiniMode(); };
        _countdownTimer.Tick += (_, _) =>
        {
            _secondsLeft--;
            CountdownLabel.Text = _secondsLeft > 0 ? $"Cerrando en {_secondsLeft}s…" : "";
        };
    }

    public void Show(string domain, AIVerdict verdict)
    {
        _currentDomain = domain;
        _pageThreats++;

        DomainLabel.Text    = domain;
        ReasonLabel.Text    = string.IsNullOrEmpty(verdict.Reason) ? "Sin descripción disponible." : verdict.Reason;
        ThreatTypeLabel.Text = verdict.ThreatType == ThreatType.None ? "—" : verdict.ThreatType.ToString();
        ConfidenceLabel.Text = $"{verdict.Confidence}%";
        SourceLabel.Text     = verdict.Source;

        var (borderColor, labelColor, icon, labelText) = verdict.Verdict == VerdictType.Block
            ? (Color.FromRgb(0xFF, 0x3D, 0x71), Color.FromRgb(0xFF, 0x3D, 0x71), "🔴", "BLOQUEADO")
            : (Color.FromRgb(0xFF, 0xB3, 0x00), Color.FromRgb(0xFF, 0xB3, 0x00), "🟡", "ADVERTENCIA");

        var brush = new SolidColorBrush(borderColor);
        PanelBorder.BorderBrush = brush;
        VerdictIcon.Text         = icon;
        VerdictLabel.Text        = labelText;
        VerdictLabel.Foreground  = new SolidColorBrush(labelColor);

        // Update mini tab badge
        MiniBadge.Background  = brush;
        MiniLabel.Foreground  = new SolidColorBrush(borderColor);
        MiniBadgeCount.Text   = _pageThreats > 99 ? "99+" : _pageThreats.ToString();

        // Show expanded
        Visibility        = Visibility.Visible;
        MiniTab.Visibility     = Visibility.Collapsed;
        PanelBorder.Visibility = Visibility.Visible;

        // Start auto-collapse countdown
        _collapseTimer.Stop();
        _countdownTimer.Stop();
        _secondsLeft = 6;
        CountdownLabel.Text = $"Cerrando en {_secondsLeft}s…";
        _collapseTimer.Start();
        _countdownTimer.Start();
    }

    /// <summary>Fully hide — call on new navigation to reset state.</summary>
    public void Hide()
    {
        _collapseTimer.Stop();
        _countdownTimer.Stop();
        _pageThreats = 0;
        Visibility = Visibility.Collapsed;
        MiniTab.Visibility     = Visibility.Collapsed;
        PanelBorder.Visibility = Visibility.Collapsed;
    }

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

        // Reset auto-collapse timer when user re-opens
        _collapseTimer.Stop();
        _countdownTimer.Stop();
        _secondsLeft = 6;
        CountdownLabel.Text = $"Cerrando en {_secondsLeft}s…";
        _collapseTimer.Start();
        _countdownTimer.Start();
    }

    private void AllowOnce_Click(object sender, RoutedEventArgs e)
    {
        AllowOnceRequested?.Invoke(this, _currentDomain);
        Hide();
    }

    private void Whitelist_Click(object sender, RoutedEventArgs e)
    {
        RequestGuard.AddToWhitelist(_currentDomain);
        WhitelistRequested?.Invoke(this, _currentDomain);
        Hide();
    }
}
