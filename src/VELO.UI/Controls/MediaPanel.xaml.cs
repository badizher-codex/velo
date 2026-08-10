using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VELO.Core.Media;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 6 / P4 — lists what VELO found playing on the page.
///
/// The decisions (what is offered, what is refused, and why) are NOT here:
/// they come from <see cref="MediaInventory.BuildOffers"/>, which is a pure
/// function with tests. This control only renders them. That split is why the
/// DRM refusal and the "no unactionable row without a reason" rule are
/// verifiable at all.
/// </summary>
public partial class MediaPanel : UserControl
{
    /// <summary>Raised when the user clicks Download on a row.</summary>
    public event EventHandler<MediaOffer>? DownloadRequested;

    public MediaPanel() => InitializeComponent();

    /// <summary>Renders an inventory snapshot. Safe to call repeatedly.</summary>
    public void Show(IReadOnlyList<MediaOffer> offers)
    {
        OffersList.ItemsSource = offers.Select(o => new OfferRow(o)).ToList();

        var actionable = offers.Count(o => o.CanDownload);
        CountLabel.Text = offers.Count switch
        {
            0 => "nothing found",
            _ => actionable > 0 ? $"{offers.Count} found · {actionable} available" : $"{offers.Count} found",
        };
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OfferRow row })
            DownloadRequested?.Invoke(this, row.Offer);
    }

    /// <summary>
    /// View wrapper. Exists so the DataTemplate binds to plain properties
    /// instead of carrying converters for three cosmetic decisions.
    /// </summary>
    private sealed class OfferRow(MediaOffer offer)
    {
        public MediaOffer Offer => offer;

        public string Title  => offer.Title;
        public string Detail => offer.Detail;
        public string? BlockedReason => offer.BlockedReason;

        public string Icon => offer.Kind switch
        {
            MediaOfferKind.Protected       => "🔒",
            MediaOfferKind.ProgressiveFile => "🎬",
            MediaOfferKind.AudioTrack      => "🎵",
            MediaOfferKind.VideoTrack      => "🎞️",
            _                              => "📃",
        };

        public Visibility ActionVisibility =>
            offer.CanDownload ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ReasonVisibility =>
            string.IsNullOrWhiteSpace(offer.BlockedReason) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// A refusal reads as a warning; a "not yet" reads as ordinary muted
        /// text. Both are role tokens, so both follow the theme.
        /// </summary>
        public Brush ReasonBrush =>
            (Brush)Application.Current.FindResource(
                offer.Kind == MediaOfferKind.Protected
                    ? "StatusWarningTextBrush"
                    : "TextMutedBrush");
    }
}
