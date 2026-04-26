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

public partial class TabSidebar : UserControl
{
    // ── Public events ─────────────────────────────────────────────────────
    public event EventHandler?              NewTabRequested;
    public event EventHandler<string>?      TabSelected;
    public event EventHandler<string>?      TabCloseRequested;
    public event EventHandler<(string TabId, string ContainerId)>? TabContainerChangeRequested;
    public event EventHandler?              SplitRequested;
    public event EventHandler?              AddWorkspaceRequested;
    public event EventHandler<string>?      WorkspaceSelected;
    /// <summary>Raised when the user drags a tab outside the window. Arg = tab ID.</summary>
    public event EventHandler<string>?      TabTearOffRequested;
    /// <summary>Raised after a workspace is removed. Arg = workspace ID.</summary>
    public event EventHandler<string>?      WorkspaceRemoved;

    // ── Internal state ────────────────────────────────────────────────────
    private readonly ObservableCollection<TabInfo>  _visibleTabs = [];
    private readonly List<TabInfo>                  _allTabs     = [];
    private readonly List<Workspace>                _workspaces  = [];
    private string _activeWorkspaceId = Workspace.Default.Id;
    private string? _activeTabId;

    // ── Collapse state ────────────────────────────────────────────────────
    private bool _isCollapsed;
    private const double ExpandedWidth  = 200;
    private const double CollapsedWidth = 44;

    // ── Drag / tear-off state ─────────────────────────────────────────────
    private Point  _dragStart;
    private bool   _isDragging;
    private static readonly double DragThreshold =
        Math.Max(SystemParameters.MinimumHorizontalDragDistance,
                 SystemParameters.MinimumVerticalDragDistance);

    private static readonly (string Id, string Color)[] ContainerDefs =
    [
        ("none",     "#808080"),
        ("personal", "#00E5FF"),
        ("work",     "#7FFF5F"),
        ("banking",  "#FF3D71"),
        ("shopping", "#FFB300"),
    ];

    /// <summary>Number of workspaces currently registered (used for name/color cycling).</summary>
    public int WorkspaceCount => _workspaces.Count;

    /// <summary>Highlights the split button when split view is active.</summary>
    public void SetSplitActive(bool active)
    {
        SplitBtn.Background = active
            ? new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xE5, 0xFF))
            : Brushes.Transparent;
        SplitBtn.Foreground = active
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x77));
    }

    public TabSidebar()
    {
        InitializeComponent();
        TabList.ItemsSource          = _visibleTabs;
        CollapsedTabList.ItemsSource = _visibleTabs;
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Unloaded += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    // ── Language ──────────────────────────────────────────────────────────

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        NewTabBtn.Content         = _isCollapsed ? "+" : L.T("sidebar.newtab");
        NewTabBtn.ToolTip         = L.T("sidebar.newtab.tooltip");
        TopNewTabBtn.Content      = _isCollapsed ? "+" : L.T("sidebar.newtab");
        TopNewTabBtn.ToolTip      = L.T("sidebar.newtab.tooltip");
        SplitBtn.ToolTip          = L.T("sidebar.split.tooltip");
        AddWorkspaceBtn.ToolTip   = L.T("sidebar.workspace.tooltip");
        CollapseBtn.ToolTip       = _isCollapsed ? L.T("sidebar.expand.tooltip") : L.T("sidebar.collapse.tooltip");
        System.Windows.Automation.AutomationProperties.SetName(SidebarRoot, L.T("sidebar.aria"));
    }

    // ── Workspace management ──────────────────────────────────────────────

    public void AddWorkspace(Workspace ws)
    {
        if (_workspaces.Any(w => w.Id == ws.Id)) return;
        _workspaces.Add(ws);
        RebuildWorkspaceStrip();
    }

    public void RemoveWorkspace(string workspaceId)
    {
        var ws = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (ws == null) return;
        _workspaces.Remove(ws);

        if (_activeWorkspaceId == workspaceId)
            _activeWorkspaceId = _workspaces.FirstOrDefault()?.Id ?? Workspace.Default.Id;

        RebuildWorkspaceStrip();
        RefreshVisibleTabs();
        WorkspaceRemoved?.Invoke(this, workspaceId);
    }

    public void SetActiveWorkspace(string workspaceId)
    {
        _activeWorkspaceId = workspaceId;
        foreach (var ws in _workspaces)
            ws.IsActive = ws.Id == workspaceId;
        RebuildWorkspaceStrip();
        RefreshVisibleTabs();
    }

    private void RebuildWorkspaceStrip()
    {
        WorkspaceStrip.Children.Clear();

        foreach (var ws in _workspaces)
        {
            var isActive = ws.Id == _activeWorkspaceId;

            Color fill;
            try   { fill = (Color)ColorConverter.ConvertFromString(ws.Color); }
            catch { fill = Colors.Gray; }

            var pill = new Border
            {
                CornerRadius  = new CornerRadius(4),
                Padding       = new Thickness(8, 3, 8, 3),
                Margin        = new Thickness(0, 0, 4, 0),
                Background    = isActive
                                    ? new SolidColorBrush(fill) { Opacity = 0.25 }
                                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush   = new SolidColorBrush(fill),
                BorderThickness = new Thickness(1),
                Cursor        = Cursors.Hand,
                Tag           = ws.Id,
                ToolTip       = ws.Name,
            };

            var label = new TextBlock
            {
                Text       = ws.Name,
                FontSize   = 11,
                Foreground = isActive
                                 ? new SolidColorBrush(fill)
                                 : new SolidColorBrush(Color.FromArgb(0xBB, fill.R, fill.G, fill.B)),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            };

            pill.Child = label;
            pill.MouseLeftButtonDown += WorkspacePill_Click;

            // Right-click context menu (only show Delete when more than one workspace exists)
            if (_workspaces.Count > 1)
            {
                var menu       = new ContextMenu();
                var deleteItem = new MenuItem { Header = "Eliminar workspace" };
                var capturedId = ws.Id;
                deleteItem.Click += (_, _) => RemoveWorkspace(capturedId);
                menu.Items.Add(deleteItem);
                pill.ContextMenu = menu;
            }

            WorkspaceStrip.Children.Add(pill);
        }
    }

    private void WorkspacePill_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string wsId)
        {
            SetActiveWorkspace(wsId);
            WorkspaceSelected?.Invoke(this, wsId);
        }
    }

    // ── Tab management ────────────────────────────────────────────────────

    public void AddTab(TabInfo tab)
    {
        _allTabs.Add(tab);
        if (tab.WorkspaceId == _activeWorkspaceId)
        {
            _visibleTabs.Add(tab);
            SetActiveTab(tab.Id);
        }
    }

    public void RemoveTab(string tabId)
    {
        _allTabs.RemoveAll(t => t.Id == tabId);
        var visible = _visibleTabs.FirstOrDefault(t => t.Id == tabId);
        if (visible != null) _visibleTabs.Remove(visible);
    }

    public void SetActiveTab(string tabId)
    {
        _activeTabId = tabId;
        foreach (var tab in _allTabs)
            tab.IsActive = tab.Id == tabId;
    }

    public void UpdateTab(TabInfo updated)
    {
        var idx = _allTabs.FindIndex(t => t.Id == updated.Id);
        if (idx < 0) return;
        _allTabs[idx] = updated;

        var vIdx = _visibleTabs.ToList().FindIndex(t => t.Id == updated.Id);
        if (vIdx >= 0) _visibleTabs[vIdx] = updated;
    }

    /// <summary>Re-evaluates which tabs belong to the active workspace.</summary>
    private void RefreshVisibleTabs()
    {
        _visibleTabs.Clear();
        foreach (var tab in _allTabs.Where(t => t.WorkspaceId == _activeWorkspaceId))
            _visibleTabs.Add(tab);
    }

    /// <summary>Moves a tab to a different workspace and refreshes the list.</summary>
    public void MoveTabToWorkspace(string tabId, string workspaceId)
    {
        var tab = _allTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;
        tab.WorkspaceId = workspaceId;
        RefreshVisibleTabs();
    }

    // ── Click handlers ────────────────────────────────────────────────────

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tabId)
        {
            _dragStart  = e.GetPosition(this);
            _isDragging = false;
            TabSelected?.Invoke(this, tabId);
        }
    }

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tabId) return;
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

        var pos   = e.GetPosition(this);
        var delta = pos - _dragStart;

        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

        // Threshold exceeded → start drag-drop
        _isDragging = true;

        var data   = new DataObject(DataFormats.Text, tabId);
        var result = DragDrop.DoDragDrop(border, data, DragDropEffects.Move);

        _isDragging = false;

        // If the drop landed outside any registered target, the effect is None → tear off
        if (result == DragDropEffects.None)
            TabTearOffRequested?.Invoke(this, tabId);
    }

    private void Tab_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tabId) return;

        var menu = new ContextMenu();
        var L    = LocalizationService.Current;

        // ── Container section ──────────────────────────────────────────
        var containerHeader = new MenuItem { Header = L.T("sidebar.container.assign"), IsEnabled = false };
        menu.Items.Add(containerHeader);

        foreach (var (id, color) in ContainerDefs)
        {
            Color c;
            try   { c = (Color)ColorConverter.ConvertFromString(color); }
            catch { c = Colors.Gray; }

            var dot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush(c),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = L.T($"sidebar.container.{id}");
            var text  = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(dot);
            panel.Children.Add(text);

            var item = new MenuItem { Header = panel };
            var capturedId = id;
            item.Click += (_, _) => TabContainerChangeRequested?.Invoke(this, (tabId, capturedId));
            menu.Items.Add(item);
        }

        // ── Workspace section (only if more than one workspace) ────────
        if (_workspaces.Count > 1)
        {
            menu.Items.Add(new Separator());
            var wsHeader = new MenuItem { Header = L.T("sidebar.container.moveto"), IsEnabled = false };
            menu.Items.Add(wsHeader);

            foreach (var ws in _workspaces.Where(w => w.Id != _activeWorkspaceId))
            {
                var wsItem = new MenuItem { Header = ws.Name };
                var capturedWsId = ws.Id;
                wsItem.Click += (_, _) => MoveTabToWorkspace(tabId, capturedWsId);
                menu.Items.Add(wsItem);
            }
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

    private void Split_Click(object sender, RoutedEventArgs e)
        => SplitRequested?.Invoke(this, EventArgs.Empty);

    private void AddWorkspace_Click(object sender, RoutedEventArgs e)
        => AddWorkspaceRequested?.Invoke(this, EventArgs.Empty);

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;

        Width = _isCollapsed ? CollapsedWidth : ExpandedWidth;

        // Show/hide sections — workspace strip + bottom action buttons hide;
        // the tab list swaps to its compact icon-only variant (v2.0.5).
        WorkspaceStripBorder.Visibility       = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TabListScroller.Visibility            = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedTabListScroller.Visibility   = _isCollapsed ? Visibility.Visible    : Visibility.Collapsed;
        SplitBtn.Visibility                   = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        AddWorkspaceBtn.Visibility            = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // Arrow flips direction
        CollapseBtn.Content = _isCollapsed ? "▶" : "◀";

        // Update language-aware texts
        ApplyLanguage();
    }
}
