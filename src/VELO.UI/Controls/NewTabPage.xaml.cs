using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VELO.Core.Localization;

namespace VELO.UI.Controls;

public partial class NewTabPage : UserControl
{
    private readonly DispatcherTimer _clockTimer;

    public event EventHandler<string>? NavigationRequested;

    public NewTabPage()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        UpdateSearchPlaceholder();

        LocalizationService.Current.LanguageChanged += UpdateSearchPlaceholder;
        Unloaded += (_, _) => LocalizationService.Current.LanguageChanged -= UpdateSearchPlaceholder;
    }

    private void UpdateSearchPlaceholder()
        => SearchBox.Tag = LocalizationService.Current.T("newtab.search");

    private void UpdateClock()
        => ClockText.Text = DateTime.Now.ToString("HH:mm");

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            NavigationRequested?.Invoke(this, SearchBox.Text.Trim());
            SearchBox.Clear();
        }
    }

    public new void Focus()
    {
        SearchBox.Focus();
        // Dispatcher delay ensures focus lands after WebView2 finishes its own focus handling
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }
}
