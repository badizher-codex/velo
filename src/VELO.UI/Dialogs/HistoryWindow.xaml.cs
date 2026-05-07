using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VELO.Core.Localization;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.UI.Dialogs;

public partial class HistoryWindow : Window
{
    private readonly HistoryRepository _repo;
    private List<HistoryEntry> _all = [];

    public event EventHandler<string>? NavigationRequested;

    public HistoryWindow(HistoryRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;

        // v2.4.5 — Wire BOTH Loaded and Activated. v2.4.4 only wired
        // Activated assuming it fired on first show, but in WPF that event
        // is tied to "window becomes the active window" — and ShowDialog()
        // doesn't always activate the dialog if the parent's UI is in an
        // unusual focus state, leaving the dialog blank. Loaded guarantees
        // a first read; Activated keeps subsequent focus-returns fresh.
        // LoadAsync itself dedupes via _isLoading so the first focus event
        // immediately after Loaded is a no-op.
        Loaded    += async (_, _) => await LoadAsync();
        Activated += async (_, _) => await LoadAsync();
    }

    private bool _isLoading;

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        Title = L.T("title.history");
        HeaderLabel.Text  = L.T("history.title");
        SearchBox.Tag     = L.T("history.search");
        ClearAllBtn.Content = L.T("history.clearall");
        Render(_all);
    }

    private async Task LoadAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _all = await _repo.GetRecentAsync(500);
            // Defensive trace so the next time we see "0 entries" the
            // user can grep DebugView / VS Output for the actual count.
            System.Diagnostics.Trace.WriteLine(
                $"[VELO] HistoryWindow loaded {_all.Count} entries");
            Render(_all);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Render(List<HistoryEntry> entries)
    {
        HistoryList.ItemsSource = entries.ToList();
        var L = LocalizationService.Current;
        var word = entries.Count == 1 ? L.T("history.entry") : L.T("history.entries");
        CountLabel.Text = $"{entries.Count} {word}";
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            Render(_all);
            return;
        }
        var results = await _repo.SearchAsync(q);
        Render(results);
    }

    private void Entry_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string url)
        {
            NavigationRequested?.Invoke(this, url);
            Close();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            await _repo.DeleteAsync(id);
            _all.RemoveAll(h => h.Id == id);
            Render(_all);
        }
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var L = LocalizationService.Current;
        var r = MessageBox.Show(L.T("history.confirm.clear"), L.T("history.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        await _repo.ClearAllAsync();
        _all.Clear();
        Render(_all);
    }
}
