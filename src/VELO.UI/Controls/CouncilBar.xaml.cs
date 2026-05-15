using System.Windows;
using System.Windows.Controls;
using VELO.Core.Council;

namespace VELO.UI.Controls;

/// <summary>
/// Phase 4.1 chunk F (v2.4.47) — Council Bar code-behind.
///
/// The control is intentionally framework-light: all state lives in a
/// <see cref="CouncilBarViewModel"/> bound via DataContext. The class only
/// owns:
///   • a focus-helper that drops the caret in the prompt box on activation
///     so the user can type immediately after opening Council Mode;
///   • the <see cref="SendRequested"/> event that the host (MainWindow when
///     activation lands in chunk G) subscribes to and translates into
///     <c>orchestrator.SynthesizeAsync(...)</c>.
///
/// The Send button's enable state, the badge visibility and the status copy
/// are all bound through the VM — the code-behind does NOT mirror those
/// states locally. That keeps the unit tests of the VM (chunk F) sufficient
/// for the bar's behavioural surface.
/// </summary>
public partial class CouncilBar : UserControl
{
    /// <summary>Raised when the user clicks the Send-all button while
    /// <see cref="CouncilBarViewModel.IsSendEnabled"/> is true.
    /// Arg = the prompt text the bar handed the orchestrator. The host
    /// is responsible for the actual fan-out + synthesis call.</summary>
    public event EventHandler<string>? SendRequested;

    public CouncilBar()
    {
        InitializeComponent();
    }

    /// <summary>v2.4.47 — convenience for the host to bring the bar up and
    /// focus the prompt box in a single call when Council Mode activates.
    /// </summary>
    public void ShowAndFocus(CouncilBarViewModel viewModel)
    {
        DataContext = viewModel;
        Visibility  = Visibility.Visible;
        // Defer focus until the layout pass so the textbox is actually visible.
        Dispatcher.BeginInvoke(new Action(() => PromptBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>v2.4.47 — convenience for the host to tear down the bar
    /// when Council Mode deactivates.</summary>
    public void HideAndReset()
    {
        Visibility  = Visibility.Collapsed;
        DataContext = null;
    }

    private void OnSendAllClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CouncilBarViewModel vm) return;
        if (!vm.IsSendEnabled) return; // double-click / race guard

        var prompt = vm.PromptText.Trim();
        SendRequested?.Invoke(this, prompt);
    }
}
