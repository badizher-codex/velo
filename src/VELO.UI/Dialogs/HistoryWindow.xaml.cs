using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.UI.Dialogs;

/// <summary>
/// v2.4.10 — Complete rewrite. Previous incarnation (Phase 2 + v2.4.4-v2.4.9
/// hotfix accretion) had at least three layered defects that none of the
/// hotfixes individually fixed:
///   • Constructor's ApplyLanguage() called Render(_all) before _all was
///     populated, leaving CountLabel pinned at "0 entries"
///   • Loaded/Activated dual wiring + _isLoading dedupe didn't recover
///     when the captured-context continuation didn't return to UI thread
///   • DataTemplate bound to localisation keys via Source='history.badge.X'
///     which the LocalizeKeyConverter handled silently — failures rendered
///     as empty rows that the user perceived as "missing entries"
/// We replace all of that with the simplest WPF pattern that works:
/// ItemsControl.ItemsSource ← ObservableCollection&lt;HistoryEntry&gt;.
/// One reload path. Synchronous to the user via the existing
/// .GetAwaiter().GetResult() inside HistoryRepository (the underlying
/// SQLite query is &lt;10ms for 500 rows). No converter chain. No
/// localisation in the template (added back later if needed).
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryRepository _repo;
    private readonly ObservableCollection<HistoryEntry> _items = new();
    private List<HistoryEntry> _allCached = new();

    /// <summary>Raised when the user clicks an entry to navigate to its URL.</summary>
    public event EventHandler<string>? NavigationRequested;

    public HistoryWindow(HistoryRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        HistoryList.ItemsSource = _items;
        Loaded += async (_, _) => await ReloadAsync();
    }

    // ── Reload ──────────────────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        StatusLabel.Text = "Loading…";
        DiagLabel.Text = "";
        try
        {
            var rows = await _repo.GetRecentAsync(500);
            _allCached = rows;
            ApplyToCollection(rows);
            StatusLabel.Text = $"{rows.Count} entries";
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

    /// <summary>
    /// Replaces the visible collection in-place. ObservableCollection
    /// raises CollectionChanged for each Clear/Add so the ItemsControl
    /// rebuilds its visual children correctly. Faster + more reliable
    /// than ItemsSource = newList.
    /// </summary>
    private void ApplyToCollection(IReadOnlyList<HistoryEntry> rows)
    {
        _items.Clear();
        foreach (var row in rows) _items.Add(row);
    }

    // ── Search ──────────────────────────────────────────────────────────

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            ApplyToCollection(_allCached);
            StatusLabel.Text = $"{_allCached.Count} entries";
            return;
        }

        try
        {
            var rows = await _repo.SearchAsync(q);
            ApplyToCollection(rows);
            StatusLabel.Text = $"{rows.Count} matches for \"{q}\"";
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
        var r = MessageBox.Show(this,
            "Clear all browsing history?",
            "Confirm",
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
            StatusLabel.Text = $"{_allCached.Count} entries";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Delete failed: {ex.Message}";
            Serilog.Log.Error(ex, "HistoryWindow.Delete failed for id {Id}", id);
        }
        e.Handled = true; // prevent the row click from firing
    }
}
