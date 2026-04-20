using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VELO.Core.Sessions;

namespace VELO.UI.Controls;

/// <summary>
/// Non-intrusive 3-second toast shown bottom-right when a tab is closed.
/// Host must position this control absolutely within the main window grid.
/// </summary>
public partial class PrivacyReceiptToast : UserControl
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public PrivacyReceiptToast()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); PlayFadeOut(); };
    }

    public void Show(TabSession session)
    {
        // Must run on UI thread
        Dispatcher.Invoke(() =>
        {
            DomainLabel.Text      = session.Domain;
            TrackerCount.Text     = session.TrackersBlocked.ToString();
            AdCount.Text          = session.AdsBlocked.ToString();
            FingerprintCount.Text = session.FingerprintBlocked.ToString();

            Visibility        = Visibility.Visible;
            IsHitTestVisible  = false;

            _hideTimer.Stop();
            PlayFadeIn();
            _hideTimer.Start();
        });
    }

    private void PlayFadeIn()
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, anim);
    }

    private void PlayFadeOut()
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
        anim.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, anim);
    }
}
