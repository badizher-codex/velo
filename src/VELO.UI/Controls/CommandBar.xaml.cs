using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VELO.UI.Controls;

// ── Result model ──────────────────────────────────────────────────────────────

public enum CommandResultKind { Tab, Bookmark, History, Command, Navigate }

public sealed class CommandResult
{
    public CommandResultKind Kind     { get; init; }
    public string            Icon     { get; init; } = "";
    public string            Title    { get; init; } = "";
    public string            Subtitle { get; init; } = "";
    public string            Badge    { get; init; } = "";
    /// <summary>Arbitrary payload (tab ID, URL, Action delegate…).</summary>
    public object?           Tag      { get; init; }

    public Visibility HasSubtitle =>
        string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
}

// ── Control ───────────────────────────────────────────────────────────────────

public partial class CommandBar : UserControl
{
    // ── Events ────────────────────────────────────────────────────────────
    public event EventHandler<CommandResult>? ResultSelected;
    public event EventHandler<string>?        QueryChanged;
    public event EventHandler?                Closed;

    // ── State ─────────────────────────────────────────────────────────────
    private readonly ObservableCollection<CommandResult> _results = [];
    private bool _suppressQueryEvent;

    public CommandBar()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void Show(IEnumerable<CommandResult> initialResults)
    {
        _suppressQueryEvent = true;
        SearchBox.Text = "";
        PlaceholderText.Visibility = Visibility.Visible;
        _suppressQueryEvent = false;

        SetResults(initialResults);
        Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(() => SearchBox.Focus(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void SetResults(IEnumerable<CommandResult> results)
    {
        _results.Clear();
        foreach (var r in results)
            _results.Add(r);

        if (_results.Count > 0)
            ResultsList.SelectedIndex = 0;
    }

    // ── Input handlers ────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility =
            string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        if (!_suppressQueryEvent)
            QueryChanged?.Invoke(this, SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                CommitSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)   { CommitSelection(); e.Handled = true; }
        if (e.Key == Key.Escape)  { Hide();            e.Handled = true; }
    }

    private void ResultsList_DoubleClick(object sender, MouseButtonEventArgs e)
        => CommitSelection();

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { /* selection already tracked via SelectedItem */ }

    private void Backdrop_Click(object sender, MouseButtonEventArgs e)
        => Hide();

    private void Palette_StopBubble(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    // ── Helpers ───────────────────────────────────────────────────────────

    private void MoveSelection(int delta)
    {
        if (_results.Count == 0) return;
        var next = (ResultsList.SelectedIndex + delta + _results.Count) % _results.Count;
        ResultsList.SelectedIndex = next;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void CommitSelection()
    {
        if (ResultsList.SelectedItem is CommandResult result)
        {
            Hide();
            ResultSelected?.Invoke(this, result);
        }
    }
}
