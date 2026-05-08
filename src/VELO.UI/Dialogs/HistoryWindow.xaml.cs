using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VELO.Core.Localization;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.UI.Dialogs;

/// <summary>
/// v2.4.10 — Complete rewrite of the v2.4.x bug train (see project_phase3_state.md).
/// v2.4.11 — Re-added security badges + 👾 monster + i18n via the
/// <see cref="HistoryEntryRowView"/> wrapper. Localised strings are
/// pre-computed when each row is constructed; the DataTemplate binds to
/// plain CLR properties so a missing key surfaces as the literal key
/// instead of a blank row (the failure mode that hid the original
/// bug for seven hotfixes).
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryRepository _repo;
    private readonly ObservableCollection<HistoryEntryRowView> _items = new();
    private List<HistoryEntry> _allCached = new();

    /// <summary>Raised when the user clicks an entry to navigate to its URL.</summary>
    public event EventHandler<string>? NavigationRequested;

    public HistoryWindow(HistoryRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        HistoryList.ItemsSource = _items;

        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;

        Loaded += async (_, _) => await ReloadAsync();
    }

    // ── Localisation ────────────────────────────────────────────────────

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        Title             = L.T("title.history");
        HeaderLabel.Text  = L.T("history.title");
        SearchBox.Tag     = L.T("history.search");
        ClearButton.Content = L.T("history.clearall");
        ReloadButton.ToolTip = L.T("history.reload");
        // If a reload already happened, rebuild the views so badge text
        // re-localises. Cheap — at most 500 record allocations.
        if (_allCached.Count > 0) ApplyToCollection(_allCached);
    }

    // ── Reload ──────────────────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        var L = LocalizationService.Current;
        StatusLabel.Text = L.T("history.loading");
        DiagLabel.Text = "";
        try
        {
            var rows = await _repo.GetRecentAsync(500);
            _allCached = rows;
            ApplyToCollection(rows);
            StatusLabel.Text = FormatCount(rows.Count);
            DiagLabel.Text = $"loaded {rows.Count} from DB";
            Serilog.Log.Information(
                "HistoryWindow.ReloadAsync OK: {Count} entries; ItemsControl now has {ItemsCount} items",
                rows.Count, _items.Count);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            DiagLabel.Text = "";
            Serilog.Log.Error(ex, "HistoryWindow.ReloadAsync failed");
        }
    }

    private void ApplyToCollection(IReadOnlyList<HistoryEntry> rows)
    {
        _items.Clear();
        foreach (var row in rows) _items.Add(HistoryEntryRowView.From(row));
    }

    private static string FormatCount(int count)
    {
        var L = LocalizationService.Current;
        var word = count == 1 ? L.T("history.entry") : L.T("history.entries");
        return $"{count} {word}";
    }

    // ── Search ──────────────────────────────────────────────────────────

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            ApplyToCollection(_allCached);
            StatusLabel.Text = FormatCount(_allCached.Count);
            return;
        }

        try
        {
            var rows = await _repo.SearchAsync(q);
            ApplyToCollection(rows);
            var L = LocalizationService.Current;
            StatusLabel.Text = string.Format(L.T("history.matches"), rows.Count, q);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Search failed: {ex.Message}";
            Serilog.Log.Error(ex, "HistoryWindow.SearchAsync failed");
        }
    }

    // ── Buttons ─────────────────────────────────────────────────────────

    private async void Reload_Click(object sender, RoutedEventArgs e)
        => await ReloadAsync();

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var L = LocalizationService.Current;
        var r = MessageBox.Show(this,
            L.T("history.confirm.clear"),
            L.T("history.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            await _repo.ClearAllAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Clear failed: {ex.Message}";
            Serilog.Log.Error(ex, "HistoryWindow.Clear failed");
        }
    }

    // ── Per-entry interactions ──────────────────────────────────────────

    private void Entry_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string url && !string.IsNullOrEmpty(url))
        {
            NavigationRequested?.Invoke(this, url);
            Close();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int id) return;
        try
        {
            await _repo.DeleteAsync(id);
            _allCached.RemoveAll(h => h.Id == id);
            ApplyToCollection(_allCached);
            StatusLabel.Text = FormatCount(_allCached.Count);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Delete failed: {ex.Message}";
            Serilog.Log.Error(ex, "HistoryWindow.Delete failed for id {Id}", id);
        }
        e.Handled = true;
    }
}
