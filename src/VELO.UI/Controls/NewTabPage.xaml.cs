using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VELO.Core.Localization;
using VELO.Data.Repositories;

namespace VELO.UI.Controls;

public partial class NewTabPage : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private bool _sitesLoaded;
    private (int Trackers, int Blocked, int Sites) _lastStats;

    public event EventHandler<string>? NavigationRequested;

    // Tile dimensions
    private const double TileSize   = 88;
    private const double TileRadius = 10;
    private const double TileMargin = 8;

    // 8 tiles max — 2 rows × 4 cols
    private const int MaxTilesPerRow = 4;
    private const int MaxTiles       = 8;

    // Accent colours cycling for letter tiles
    private static readonly string[] TileColors =
    [
        "#FF00BCD4", "#FF7C4DFF", "#FF4CAF50", "#FFFF9800",
        "#FFE91E63", "#FF2196F3", "#FFFF5722", "#FF009688",
    ];

    public NewTabPage()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        UpdateSearchPlaceholder();

        LocalizationService.Current.LanguageChanged += UpdateSearchPlaceholder;
        LocalizationService.Current.LanguageChanged += () => UpdateStats(_lastStats.Trackers, _lastStats.Blocked, _lastStats.Sites);
        Unloaded += (_, _) =>
        {
            LocalizationService.Current.LanguageChanged -= UpdateSearchPlaceholder;
            _clockTimer.Stop();
        };
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads top sites and lifetime stats from the history DB.
    /// Safe to call multiple times (only runs once per page lifetime).
    /// </summary>
    public async Task LoadTopSitesAsync(HistoryRepository repo)
    {
        if (_sitesLoaded) return;
        _sitesLoaded = true;

        try
        {
            var topSites = await repo.GetTopSitesAsync(MaxTiles);
            var stats    = await repo.GetLifetimeStatsAsync();

            Dispatcher.Invoke(() =>
            {
                PopulateTiles(topSites);
                UpdateStats(stats.TotalTrackers, stats.TotalBlocked, stats.TotalSites);
            });
        }
        catch
        {
            // Best-effort — never block the newtab from showing
        }
    }

    public new void Focus()
    {
        SearchBox.Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }

    // ── Clock / placeholder ──────────────────────────────────────────────

    private void UpdateSearchPlaceholder()
        => SearchBox.Tag = LocalizationService.Current.T("newtab.search");

    private void UpdateClock()
        => ClockText.Text = DateTime.Now.ToString("HH:mm");

    // ── Tile population ──────────────────────────────────────────────────

    private void PopulateTiles(List<TopSiteEntry> sites)
    {
        TopSitesRow1.Children.Clear();
        TopSitesRow2.Children.Clear();

        if (sites.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        for (int i = 0; i < Math.Min(sites.Count, MaxTiles); i++)
        {
            var tile = BuildTile(sites[i], i);
            if (i < MaxTilesPerRow)
                TopSitesRow1.Children.Add(tile);
            else
                TopSitesRow2.Children.Add(tile);
        }
    }

    private Border BuildTile(TopSiteEntry site, int index)
    {
        var accentHex = TileColors[index % TileColors.Length];
        var accent    = (Color)ColorConverter.ConvertFromString(accentHex);
        var letter    = GetInitial(site.Host);

        // Letter circle
        var circle = new Border
        {
            Width           = 44,
            Height          = 44,
            CornerRadius    = new CornerRadius(22),
            Background      = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1.5),
            Margin          = new Thickness(0, 0, 0, 6),
            Child           = new TextBlock
            {
                Text                = letter,
                FontSize            = 18,
                FontWeight          = FontWeights.SemiBold,
                Foreground          = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            }
        };

        // Label (truncated host)
        var label = new TextBlock
        {
            Text                = TruncateHost(site.Host),
            FontSize            = 11,
            Foreground          = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis,
            MaxWidth            = TileSize - 4,
        };

        // Tile container
        var tileContent = new StackPanel
        {
            Width               = TileSize,
            Height              = TileSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        tileContent.Children.Add(circle);
        tileContent.Children.Add(label);

        var tile = new Border
        {
            Width           = TileSize,
            Height          = TileSize,
            CornerRadius    = new CornerRadius(TileRadius),
            Background      = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(TileMargin),
            Cursor          = Cursors.Hand,
            Child           = tileContent,
            Tag             = site.Url,
            ToolTip         = $"{site.Title}\n{site.Url}\n{site.VisitCount} visita(s)",
        };

        // Hover highlight
        tile.MouseEnter += (_, _) =>
        {
            tile.Background  = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B));
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.Background  = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
            tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        };

        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (tile.Tag is string url)
                NavigationRequested?.Invoke(this, url);
        };

        return tile;
    }

    private void UpdateStats(int trackers, int blocked, int sites)
    {
        _lastStats = (trackers, blocked, sites);

        if (trackers == 0 && blocked == 0)
        {
            StatsText.Text = LocalizationService.Current.T("newtab.stats.empty");
            return;
        }

        var parts = new List<string>();
        if (trackers > 0) parts.Add($"{trackers} rastreadores bloqueados");
        if (blocked > 0 && blocked != trackers) parts.Add($"{blocked} requests bloqueados");
        if (sites > 0) parts.Add($"{sites} sitios visitados");

        StatsText.Text = string.Join(" · ", parts) + " en total";
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetInitial(string host)
    {
        // Strip "www." prefix, take first letter uppercase
        var bare = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        return bare.Length > 0 ? bare[0].ToString().ToUpperInvariant() : "?";
    }

    private static string TruncateHost(string host)
    {
        var bare = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        // Remove TLD for very short display
        var dot = bare.LastIndexOf('.');
        return dot > 0 && bare.Length > 12 ? bare[..dot] : bare;
    }

    // ── Search ───────────────────────────────────────────────────────────

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            NavigationRequested?.Invoke(this, SearchBox.Text.Trim());
            SearchBox.Clear();
        }
    }
}
