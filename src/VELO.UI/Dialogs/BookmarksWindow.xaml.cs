using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VELO.Core.Localization;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.UI.Dialogs;

public partial class BookmarksWindow : Window
{
    private readonly BookmarkRepository _repo;
    private List<Bookmark> _all = [];

    public event EventHandler<string>? NavigationRequested;

    public BookmarksWindow(BookmarkRepository repo)
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
        Title = L.T("title.bookmarks");
        HeaderLabel.Text = L.T("bookmarks.title");
    }

    private async Task LoadAsync()
    {
        _all = await _repo.GetAllAsync();
        Render(_all);
    }

    private void Render(IEnumerable<Bookmark> items)
    {
        BookmarkList.Children.Clear();

        foreach (var b in items)
            BookmarkList.Children.Add(BuildCard(b));

        if (!BookmarkList.Children.Cast<UIElement>().Any())
            BookmarkList.Children.Add(new TextBlock
            {
                Text = LocalizationService.Current.T("bookmarks.empty"),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
    }

    private Border BuildCard(Bookmark b)
    {
        var card = new Border
        {
            Background      = (Brush)FindResource("BackgroundLightBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 0, 0, 6),
            Padding         = new Thickness(14, 10, 14, 10),
            Cursor          = System.Windows.Input.Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Info
        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text       = b.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new TextBlock
        {
            Text       = b.Url,
            FontSize   = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin     = new Thickness(0, 2, 0, 0)
        });

        // v2.4.18 — AI tag chips (Sprint 9B). Read-only first pass; click-to-filter
        // is a follow-up.
        if (!string.IsNullOrWhiteSpace(b.Tags))
            info.Children.Add(BuildTagChips(b.Tags));

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Delete button
        var del = new Button
        {
            Content  = "🗑",
            Padding  = new Thickness(6, 4, 6, 4),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        del.Click += async (_, _) =>
        {
            await _repo.DeleteAsync(b.Id);
            await LoadAsync();
        };
        Grid.SetColumn(del, 1);
        grid.Children.Add(del);

        card.Child = grid;

        // Click to navigate
        card.MouseLeftButtonUp += (_, _) =>
        {
            NavigationRequested?.Invoke(this, b.Url);
            Close();
        };

        return card;
    }

    private WrapPanel BuildTagChips(string csv)
    {
        var chipsPanel = new WrapPanel
        {
            Margin = new Thickness(0, 6, 0, 0)
        };

        var tags = csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var tag in tags.Take(5))
        {
            var chip = new Border
            {
                Background      = (Brush)FindResource("BackgroundDarkBrush"),
                BorderBrush     = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(7, 1, 7, 2),
                Margin          = new Thickness(0, 0, 4, 0),
                Child = new TextBlock
                {
                    Text       = tag,
                    FontSize   = 10,
                    Foreground = (Brush)FindResource("TextMutedBrush")
                }
            };
            chipsPanel.Children.Add(chip);
        }

        return chipsPanel;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(b =>
                b.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Url.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Tags.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Render(filtered);
    }
}
