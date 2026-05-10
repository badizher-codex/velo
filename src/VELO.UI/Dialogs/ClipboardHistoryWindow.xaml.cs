using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VELO.Core.Clipboard;
using VELO.Core.Localization;

namespace VELO.UI.Dialogs;

/// <summary>
/// Phase 3 / Sprint 9D (v2.4.23) — Dialog over <see cref="ClipboardHistory"/>.
/// Opened with Ctrl+Shift+V. Click a row → restore that text to the
/// Windows clipboard and close. Double-click → same + raise the
/// <see cref="PasteRequested"/> event so the host can inject into the
/// active editable.
/// </summary>
public partial class ClipboardHistoryWindow : Window
{
    private readonly ClipboardHistory _history;
    private List<ClipboardHistory.Entry> _all = [];

    /// <summary>Raised when the user double-clicks an entry — host pastes into the active tab.</summary>
    public event EventHandler<string>? PasteRequested;

    public ClipboardHistoryWindow(ClipboardHistory history)
    {
        _history = history;
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        _history.EntryAdded += OnEntryAdded;
        Closed += (_, _) =>
        {
            LocalizationService.Current.LanguageChanged -= ApplyLanguage;
            _history.EntryAdded -= OnEntryAdded;
        };
        Loaded += (_, _) => Reload();
    }

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        Title             = L.T("clipboard.title");
        HeaderLabel.Text  = L.T("clipboard.title");
        HintLabel.Text    = L.T("clipboard.hint");
        ClearAllButton.Content = L.T("clipboard.clear_all");
    }

    private void OnEntryAdded(ClipboardHistory.Entry _)
    {
        // Reload on UI thread; the service may raise on any thread.
        Dispatcher.InvokeAsync(Reload);
    }

    private void Reload()
    {
        _all = _history.GetAll().ToList();
        Render(_all);
    }

    private void Render(IEnumerable<ClipboardHistory.Entry> items)
    {
        EntryList.Children.Clear();

        int index = 0;
        foreach (var entry in items)
        {
            EntryList.Children.Add(BuildCard(entry, index));
            index++;
        }

        if (EntryList.Children.Count == 0)
            EntryList.Children.Add(new TextBlock
            {
                Text       = LocalizationService.Current.T("clipboard.empty"),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin     = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
    }

    private Border BuildCard(ClipboardHistory.Entry entry, int index)
    {
        var card = new Border
        {
            Background      = (Brush)FindResource("BackgroundLightBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 0, 0, 6),
            Padding         = new Thickness(14, 10, 14, 10),
            Cursor          = System.Windows.Input.Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Preview text (single-line, truncated to fit card)
        var info = new StackPanel();
        var preview = entry.Text.Replace('\n', ' ').Replace('\r', ' ');
        if (preview.Length > 120) preview = preview[..120] + "…";

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (entry.LooksLikePassword)
        {
            header.Children.Add(new TextBlock
            {
                Text       = "🔒 ",
                FontSize   = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        header.Children.Add(new TextBlock
        {
            Text       = preview,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        info.Children.Add(header);

        info.Children.Add(new TextBlock
        {
            Text       = entry.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
            FontSize   = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin     = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Delete button
        var del = new Button
        {
            Content  = "🗑",
            Padding  = new Thickness(6, 4, 6, 4),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        del.Click += (_, _) =>
        {
            _history.RemoveAt(index);
            Reload();
        };
        Grid.SetColumn(del, 1);
        grid.Children.Add(del);

        card.Child = grid;

        // Single-click → copy to clipboard, close.
        // Double-click → copy + paste into active editable via PasteRequested.
        card.MouseLeftButtonUp += (_, e) =>
        {
            try { System.Windows.Clipboard.SetText(entry.Text); } catch { /* clipboard sometimes locked */ }
            if (e.ClickCount >= 2)
                PasteRequested?.Invoke(this, entry.Text);
            Close();
        };

        return card;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(en => en.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Render(filtered);
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        Reload();
    }
}
