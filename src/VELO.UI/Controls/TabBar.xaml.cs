using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VELO.Core.Localization;
using VELO.Core.Navigation;

namespace VELO.UI.Controls;

public partial class TabBar : UserControl
{
    public event EventHandler? NewTabRequested;
    public event EventHandler<string>? TabSelected;
    public event EventHandler<string>? TabCloseRequested;
    public event EventHandler<(string TabId, string ContainerId)>? TabContainerChangeRequested;

    private readonly ObservableCollection<TabInfo> _tabs = [];
    private string? _activeTabId;

    private static readonly (string Id, string Label, string Color)[] Containers =
    [
        ("none",     "Sin container", "#808080"),
        ("personal", "Personal",      "#00E5FF"),
        ("work",     "Trabajo",       "#7FFF5F"),
        ("banking",  "Banca",         "#FF3D71"),
        ("shopping", "Compras",       "#FFB300"),
    ];

    public TabBar()
    {
        InitializeComponent();
        TabList.ItemsSource = _tabs;
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Unloaded += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        NewTabBtn.ToolTip = LocalizationService.Current.T("newtab.title");
    }

    public void AddTab(TabInfo tab)
    {
        _tabs.Add(tab);
        SetActiveTab(tab.Id);
    }

    public void RemoveTab(string tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null) _tabs.Remove(tab);
    }

    public void SetActiveTab(string tabId)
    {
        _activeTabId = tabId;
        foreach (var tab in _tabs)
            tab.IsActive = tab.Id == tabId;
    }

    public void UpdateTab(TabInfo updated)
    {
        // With INotifyPropertyChanged on TabInfo this is mostly a no-op,
        // but kept for callers that pass a replaced object.
        var existing = _tabs.FirstOrDefault(t => t.Id == updated.Id);
        if (existing == null) return;
        var index = _tabs.IndexOf(existing);
        _tabs[index] = updated;
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tabId)
            TabSelected?.Invoke(this, tabId);
    }

    private void Tab_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tabId) return;

        var menu = new ContextMenu();

        foreach (var (id, label, color) in Containers)
        {
            var dot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(dot);
            panel.Children.Add(text);

            var item = new MenuItem { Header = panel };
            var capturedId = id;
            item.Click += (_, _) => TabContainerChangeRequested?.Invoke(this, (tabId, capturedId));
            menu.Items.Add(item);

            if (id == "none") menu.Items.Add(new Separator());
        }

        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabId)
            TabCloseRequested?.Invoke(this, tabId);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
        => NewTabRequested?.Invoke(this, EventArgs.Empty);
}
