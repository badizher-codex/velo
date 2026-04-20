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
        BackButton.ToolTip      = L.T("nav.back");
        ForwardButton.ToolTip   = L.T("nav.forward");
        ReloadButton.ToolTip    = L.T("nav.reload");
        BookmarkButton.ToolTip  = L.T("nav.bookmark");
        ReaderModeButton.ToolTip = L.T("nav.reader");
        MenuButton.ToolTip      = L.T("nav.menu");
        TlsIndicator.ToolTip    = L.T("nav.secure");
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
        ReloadButton.Content = loading ? "✕" : "↻";
        ReloadButton.ToolTip = loading ? "Detener" : "Recargar";

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
        var (dot, label, tooltip, color) = status switch
        {
            AiStatus.Ready       => ("#00E676", "IA", $"IA activa · {modelName}\nAnalizando amenazas en tiempo real", Color.FromRgb(0x00, 0xE6, 0x76)),
            AiStatus.Connecting  => ("#FFB300", "IA", "IA conectando…",                                              Color.FromRgb(0xFF, 0xB3, 0x00)),
            AiStatus.Error       => ("#F44336", "IA", $"IA no disponible · {modelName}\nRevisa que Ollama esté corriendo: ollama serve", Color.FromRgb(0xF4, 0x43, 0x36)),
            _                    => ("#555566", "IA", "IA offline · Análisis heurístico local activo",               Color.FromRgb(0x55, 0x55, 0x66)),
        };

        AiDot.Fill         = new SolidColorBrush(color);
        AiLabel.Foreground = new SolidColorBrush(color);
        AiLabel.Text       = label;
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
        BookmarkButton.Content   = bookmarked ? "★" : "☆";
        BookmarkButton.Foreground = bookmarked
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB300"))
            : (Brush)FindResource("TextMutedBrush");
        BookmarkButton.ToolTip = bookmarked ? "Eliminar marcador" : "Guardar marcador";
    }
}

public enum TlsStatus { Secure, Insecure, Warning }
