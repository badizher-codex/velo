using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VELO.Core.Localization;
using VELO.Security.Models;

namespace VELO.UI.Controls;

public partial class UrlBar : UserControl
{
    public event EventHandler<string>? NavigationRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? ForwardRequested;
    public event EventHandler? ReloadRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? MenuRequested;
    public event EventHandler? BookmarkRequested;
    public event EventHandler? ZoomResetRequested;
    public event EventHandler? ReaderModeRequested;
    public event EventHandler? ShieldScoreClicked;
    /// <summary>Fired when the user clicks the 🤖 IA indicator — caller opens VeloAgent chat.</summary>
    public event EventHandler? AgentChatRequested;
    /// <summary>v2.4.12 — Fired when the user clicks the TL;DR badge in the URL bar.</summary>
    public event EventHandler? TldrRequested;

    private bool _isLoading;
    private bool _isBookmarked;

    public UrlBar()
    {
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Unloaded += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        BackButton.ToolTip       = L.T("nav.back");
        ForwardButton.ToolTip    = L.T("nav.forward");
        ReloadButton.ToolTip     = _isLoading ? L.T("urlbar.stop") : L.T("nav.reload");
        BookmarkButton.ToolTip   = _isBookmarked ? L.T("urlbar.bookmark.remove") : L.T("urlbar.bookmark.add");
        ReaderModeButton.ToolTip = L.T("nav.reader");
        MenuButton.ToolTip       = L.T("nav.menu");
        TlsIndicator.ToolTip     = L.T("nav.secure");
        ZoomIndicator.ToolTip    = L.T("urlbar.zoom.reset");
    }

    public void FocusUrlBar()
    {
        UrlField.Focus();
        UrlField.SelectAll();
    }

    public void SetUrl(string url)
    {
        if (!UrlField.IsFocused)
            UrlField.Text = url == "velo://newtab" ? "" : url;
    }

    public void SetLoading(bool loading)
    {
        _isLoading = loading;
        var L = LocalizationService.Current;
        // v2.4.35 — Segoe Fluent Icons:  = Cancel (X),  = Refresh
        ReloadButton.Content = loading ? "" : "";
        ReloadButton.ToolTip = loading ? L.T("urlbar.stop") : L.T("nav.reload");

        if (loading)
        {
            LoadingCanvas.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation
            {
                From = -LoadingRect.Width,
                To   = ActualWidth + LoadingRect.Width,
                Duration = TimeSpan.FromSeconds(1.4),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            LoadingRect.BeginAnimation(Canvas.LeftProperty, anim);
        }
        else
        {
            LoadingRect.BeginAnimation(Canvas.LeftProperty, null);
            LoadingCanvas.Visibility = Visibility.Collapsed;
        }
    }

    public void SetCanGoBack(bool can) => BackButton.IsEnabled = can;
    public void SetCanGoForward(bool can) => ForwardButton.IsEnabled = can;

    /// <summary>v2.4.12 — Toggle TL;DR badge visibility based on TldrService.IsEligible.</summary>
    public void SetTldrAvailable(bool available)
        => TldrBadge.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

    public void SetTlsStatus(TlsStatus status)
    {
        TlsIndicator.Text = status switch
        {
            TlsStatus.Secure  => "🔒",
            TlsStatus.Insecure => "🔓",
            TlsStatus.Warning  => "⚠️",
            _                  => "🔒"
        };
    }

    public enum AiStatus { Offline, Connecting, Ready, Error }

    public void SetAiStatus(AiStatus status, string modelName = "")
    {
        var L = LocalizationService.Current;
        // v2.4.35 — paint resolves from the Phase 5 badge tokens instead of
        // hardcoded #00E676 / #FFB300 / #F44336 / #555566. Same semantic
        // mapping (ready=green, connecting=amber, error=red, offline=muted)
        // but the values are now coherent with the rest of the dark palette.
        var brushKey = status switch
        {
            AiStatus.Ready      => "BadgeGreenBrush",
            AiStatus.Connecting => "BadgeAmberBrush",
            AiStatus.Error      => "BadgeRedBrush",
            _                   => "TextMutedBrush",
        };
        var tooltip = status switch
        {
            AiStatus.Ready      => string.Format(L.T("urlbar.ai.ready"), modelName),
            AiStatus.Connecting => L.T("urlbar.ai.connecting"),
            AiStatus.Error      => string.Format(L.T("urlbar.ai.error"), modelName),
            _                   => L.T("urlbar.ai.offline"),
        };

        var brush = (Brush)FindResource(brushKey);
        AiDot.Fill         = brush;
        AiLabel.Foreground = brush;
        AiLabel.Text       = "IA";
        AiRobot.Opacity    = status == AiStatus.Ready ? 1.0 : 0.4;

        // Update tooltip (it's on the parent StackPanel)
        ToolTipService.SetToolTip(AiIndicatorPanel, tooltip);
    }

    private void AiIndicator_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AgentChatRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    // Keep backward compat for any callers
    public void SetAiMode(string mode) { }

    public void SetContainer(string containerId, string color)
    {
        if (containerId == "none")
        {
            ContainerIndicator.Visibility = Visibility.Collapsed;
            return;
        }
        ContainerIndicator.Visibility = Visibility.Visible;
        ContainerIndicator.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
    }

    private void UrlField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var input = UrlField.Text.Trim();
            if (!string.IsNullOrEmpty(input))
                NavigationRequested?.Invoke(this, input);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
            Keyboard.ClearFocus();
    }

    private void UrlField_GotFocus(object sender, RoutedEventArgs e)
        => UrlField.SelectAll();

    private void UrlField_LostFocus(object sender, RoutedEventArgs e)
    {
        // Could restore display URL here
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
        => ForwardRequested?.Invoke(this, EventArgs.Empty);

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) StopRequested?.Invoke(this, EventArgs.Empty);
        else            ReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
        => MenuRequested?.Invoke(this, EventArgs.Empty);

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        => BookmarkRequested?.Invoke(this, EventArgs.Empty);

    private void TldrBadge_Click(object sender, RoutedEventArgs e)
        => TldrRequested?.Invoke(this, EventArgs.Empty);

    private void ReaderModeButton_Click(object sender, RoutedEventArgs e)
        => ReaderModeRequested?.Invoke(this, EventArgs.Empty);

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
        => ZoomResetRequested?.Invoke(this, EventArgs.Empty);

    public void SetZoom(double factor)
    {
        if (Math.Abs(factor - 1.0) < 0.01)
        {
            ZoomIndicator.Visibility = Visibility.Collapsed;
        }
        else
        {
            ZoomIndicator.Content    = $"{(int)Math.Round(factor * 100)}%";
            ZoomIndicator.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Shows the reader mode button only on real web pages (not newtab).</summary>
    public void SetReaderModeAvailable(bool available)
        => ReaderModeButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

    public void UpdateShieldScore(SafetyResult result) => ShieldBadge.Update(result);
    public void SetShieldAnalyzing() => ShieldBadge.SetAnalyzing();

    private void ShieldBadge_Click(object sender, EventArgs e)
        => ShieldScoreClicked?.Invoke(this, EventArgs.Empty);

    public void SetBookmarked(bool bookmarked)
    {
        _isBookmarked = bookmarked;
        BookmarkButton.Content    = bookmarked ? "★" : "☆";
        // v2.4.35 — amber for the active state via the Phase 5 badge token
        // (warmer/golder than the v2.4 #FFB300, still unambiguously "saved").
        BookmarkButton.Foreground = bookmarked
            ? (Brush)FindResource("BadgeAmberBrush")
            : (Brush)FindResource("TextMutedBrush");
        var L = LocalizationService.Current;
        BookmarkButton.ToolTip    = bookmarked ? L.T("urlbar.bookmark.remove") : L.T("urlbar.bookmark.add");
    }
}

public enum TlsStatus { Secure, Insecure, Warning }
