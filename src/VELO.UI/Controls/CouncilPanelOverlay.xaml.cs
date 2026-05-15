using System.Windows;
using System.Windows.Controls;
using VELO.Core.Council;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 4.1 chunk F (v2.4.47) — Per-panel mini-toolbar code-behind.
///
/// The overlay floats over a single 2×2 layout cell and emits a
/// <see cref="CaptureRequested"/> event when the user clicks one of the
/// four capture buttons. The host (BrowserTabHost or MainWindow when
/// chunk G wires activation) listens, invokes the appropriate
/// <c>__veloCouncil.captureXxx()</c> on the matching BrowserTab via
/// ExecuteScriptAsync, materialises the returned text into a
/// <see cref="CouncilCapture"/>, and hands it to
/// <c>CouncilOrchestrator.AddCapture</c>.
///
/// This control does NOT call into WebView2 itself — it stays
/// framework-light so the same overlay can be reused if Phase 4.4 ever
/// adds a tear-off Council mini-window. All WebView access lives in the
/// host event handler.
/// </summary>
public partial class CouncilPanelOverlay : UserControl
{
    /// <summary>The provider this overlay was bound to via
    /// <see cref="SetProvider"/>. Null when the control is in its blank
    /// default state.</summary>
    public CouncilProvider? Provider { get; private set; }

    /// <summary>Raised when the user clicks one of the four capture buttons.
    /// Args = (provider, captureType). Provider comes from the last
    /// <see cref="SetProvider"/> call; captureType from the button clicked.
    /// The host translates this into an ExecuteScriptAsync call against the
    /// matching panel's <c>__veloCouncil</c> bridge.</summary>
    public event EventHandler<(CouncilProvider Provider, CouncilCaptureType Type)>? CaptureRequested;

    public CouncilPanelOverlay()
    {
        InitializeComponent();
    }

    /// <summary>Bind the overlay to a provider. Updates the chip label so
    /// the user can see which slot the toolbar is acting on. Idempotent.</summary>
    public void SetProvider(CouncilProvider provider)
    {
        Provider = provider;
        ProviderLabel.Text = provider switch
        {
            CouncilProvider.Claude  => "Claude",
            CouncilProvider.ChatGpt => "ChatGPT",
            CouncilProvider.Grok    => "Grok",
            CouncilProvider.Local   => "Local",
            _                       => provider.ToString(),
        };
        // No state change for visibility — the host owns when the overlay
        // appears (it doesn't show until Council Mode is active).
    }

    /// <summary>Convenience to bring the overlay up after <see cref="SetProvider"/>.</summary>
    public void Show() => Visibility = Visibility.Visible;

    /// <summary>Tear-down used when Council Mode deactivates.</summary>
    public void HideAndReset()
    {
        Visibility = Visibility.Collapsed;
        Provider   = null;
    }

    private void OnCaptureTextClick(object sender, RoutedEventArgs e)
        => Raise(CouncilCaptureType.Text);

    private void OnCaptureCodeClick(object sender, RoutedEventArgs e)
        => Raise(CouncilCaptureType.Code);

    private void OnCaptureTableClick(object sender, RoutedEventArgs e)
        => Raise(CouncilCaptureType.Table);

    private void OnCaptureCitationClick(object sender, RoutedEventArgs e)
        => Raise(CouncilCaptureType.Citation);

    private void Raise(CouncilCaptureType type)
    {
        if (Provider is not { } prov) return; // not bound — no-op
        CaptureRequested?.Invoke(this, (prov, type));
    }
}
