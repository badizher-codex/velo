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
        Loaded += async (_, _) => await LoadAsync();
    }

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
        _all = await _repo.GetRecentAsync(500);
        Render(_all);
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
