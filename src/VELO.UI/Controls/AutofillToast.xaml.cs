using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VELO.Core.Localization;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 3 / Sprint 5 — Autofill prompt toast. Two modes:
///  • <see cref="Mode.UseSaved"/>: "Use saved password?" with Use / Dismiss.
///  • <see cref="Mode.SaveNew"/>:  "Save this password?" with Save / Dismiss
///    plus optional HIBP breach warning banner.
/// </summary>
public partial class AutofillToast : UserControl
{
    public enum Mode { UseSaved, SaveNew }

    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(12) };

    /// <summary>Raised when the user accepts the primary action (Use / Save).</summary>
    public event EventHandler? Accepted;
    /// <summary>Raised when the user dismisses or the toast times out.</summary>
    public event EventHandler? Dismissed;

    public AutofillToast()
    {
        InitializeComponent();
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); RaiseDismissed(); };
    }

    /// <summary>Shows the toast configured for the given mode.</summary>
    public void Show(Mode mode, string username, string host, int breachCount = 0)
    {
        Dispatcher.Invoke(() =>
        {
            var t = LocalizationService.Current;
            TitleLabel.Text = mode == Mode.UseSaved
                ? t.T("autofill.use_saved")
                : t.T("autofill.save_prompt");

            DetailLabel.Text = string.IsNullOrEmpty(username)
                ? host
                : $"{username} — {host}";

            PrimaryButton.Content = mode == Mode.UseSaved
                ? t.T("autofill.use")
                : t.T("autofill.save");
            DismissButton.Content = t.T("autofill.dismiss");

            if (mode == Mode.SaveNew && breachCount > 0)
            {
                BreachLabel.Text = string.Format(t.T("autofill.breached"), breachCount);
                BreachBanner.Visibility = Visibility.Visible;
            }
            else
            {
                BreachBanner.Visibility = Visibility.Collapsed;
            }

            Visibility = Visibility.Visible;
            _autoHide.Stop();
            PlayFadeIn();
            _autoHide.Start();
        });
    }

    public void Hide()
    {
        Dispatcher.Invoke(() =>
        {
            _autoHide.Stop();
            PlayFadeOut();
        });
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        _autoHide.Stop();
        Accepted?.Invoke(this, EventArgs.Empty);
        PlayFadeOut();
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
