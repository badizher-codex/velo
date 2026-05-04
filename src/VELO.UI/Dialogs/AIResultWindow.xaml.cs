using System.Windows;
using System.Windows.Media;

namespace VELO.UI.Dialogs;

public partial class AIResultWindow : Window
{
    /// <summary>Async producer that knows how to (re)run the action. Receives
    /// a CancellationToken and returns the model's reply text.</summary>
    public Func<CancellationToken, Task<string>>? Generator { get; set; }

    /// <summary>Raised when the user clicks "Preguntar seguimiento" — host
    /// opens the agent panel pre-loaded with the result + source as context.</summary>
    public event EventHandler<(string ActionLabel, string Source, string Result)>? FollowUpRequested;

    private CancellationTokenSource? _cts;

    public AIResultWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configures and opens the window. Returns immediately; the result
    /// streams in via the Generator callback.
    /// </summary>
    public void Show(
        string actionLabel,
        string sourcePreview,
        string adapterName,
        bool isCloud,
        Func<CancellationToken, Task<string>> generator)
    {
        ActionLabel.Text = actionLabel;
        AdapterChipText.Text = (isCloud ? "🌐 " : "🖥 ") + adapterName;

        // Cloud invocations get an amber chip + tooltip (per spec § 3.5):
        // makes "this query left your device" loud-and-clear.
        if (isCloud)
        {
            AdapterChip.Background  = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x10));
            AdapterChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x29));
            AdapterChipText.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x29));
            AdapterChip.ToolTip = "Esta consulta salió de tu dispositivo. Puedes desactivar la nube en Settings → IA.";
        }
        else
        {
            AdapterChip.Background  = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x20));
            AdapterChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xB5, 0x4F));
            AdapterChipText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xB5, 0x4F));
            AdapterChip.ToolTip = "Procesado localmente. Tus datos no salieron del dispositivo.";
        }

        if (!string.IsNullOrWhiteSpace(sourcePreview))
        {
            SourceLabel.Text       = "Contexto:";
            SourceText.Text        = sourcePreview.Length > 400
                ? sourcePreview[..400] + "…"
                : sourcePreview;
            SourceText.Visibility  = Visibility.Visible;
        }
        else
        {
            SourceLabel.Text = "";
            SourceText.Visibility = Visibility.Collapsed;
        }

        Generator = generator;
        Show();
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        if (Generator == null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        LoadingPanel.Visibility = Visibility.Visible;
        ResultText.Visibility   = Visibility.Collapsed;

        try
        {
            var text = await Generator(ct);
            if (ct.IsCancellationRequested) return;
            ResultText.Text         = text;
            ResultText.Visibility   = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ResultText.Text         = "Error: " + ex.Message;
            ResultText.Foreground   = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
            ResultText.Visibility   = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultText.Text))
            Clipboard.SetText(ResultText.Text);
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e) => _ = RunAsync();

    private void FollowUp_Click(object sender, RoutedEventArgs e)
    {
        FollowUpRequested?.Invoke(this,
            (ActionLabel.Text, SourceText.Text, ResultText.Text));
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
