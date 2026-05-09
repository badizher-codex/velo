using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VELO.Core.Localization;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 3 / Sprint 8C — Discrete toast that surfaces a single narration
/// from <c>BlockNarrationService</c>. Auto-hides after 10 s and stacks
/// politely (the service's own per-host cooldown + per-minute throttle
/// already prevent toast spam, so the UI side just shows whatever lands
/// here).
/// </summary>
public partial class BlockNarrationToast : UserControl
{
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(10) };

    /// <summary>Raised when the user dismisses the toast or it times out.</summary>
    public event EventHandler? Dismissed;

    public BlockNarrationToast()
    {
        InitializeComponent();
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); RaiseDismissed(); };
    }

    /// <summary>
    /// Renders a narration on the UI thread and starts the auto-hide timer.
    /// </summary>
    public void ShowNarration(string host, string source, string body)
    {
        Dispatcher.Invoke(() =>
        {
            var t = LocalizationService.Current;
            TitleLabel.Text = t.T("narration.title");
            HostLabel.Text  = string.IsNullOrEmpty(source)
                ? host
                : $"{host} — {source}";
            BodyLabel.Text  = body ?? "";
            DismissButton.Content = t.T("narration.dismiss");

            Visibility = Visibility.Visible;
            _autoHide.Stop();
            PlayFadeIn();
            _autoHide.Start();
        });
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        _autoHide.Stop();
        RaiseDismissed();
    }

    private void RaiseDismissed()
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
        PlayFadeOut();
    }

    private void PlayFadeIn()
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        BeginAnimation(OpacityProperty, anim);
    }

    private void PlayFadeOut()
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        anim.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, anim);
    }
}
