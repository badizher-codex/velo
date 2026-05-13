using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.App.Controllers;

/// <summary>
/// Phase 4.0 (Council Mode — foundations, v2.5.0-pre) — owns the 2×2 split-view
/// layout for Council sessions. Peer to <see cref="BrowserTabHost"/>,
/// <see cref="SessionPersistenceController"/>, <see cref="CommandPaletteController"/>
/// and <see cref="KeyboardShortcutsController"/>.
///
/// Today VELO supports a 2-pane horizontal split (managed inline in
/// <c>MainWindow.xaml.cs</c> via <c>_isSplitMode</c> / <c>_primaryTabId</c> /
/// <c>_splitTabId</c>). Council Mode needs FOUR panels arranged 2×2 so the
/// user can paste the same master prompt into Claude / ChatGPT / Grok / Ollama
/// simultaneously. The two layouts are mutually exclusive — host must
/// deactivate the 2-pane split before activating Council, and vice versa.
///
/// Grid topology when active:
/// <code>
///     +--------+---+--------+
///     | Panel0 | V | Panel1 |   Row 0 (*) — top half
///     +--------+---+--------+
///     |   H    | + |   H    |   Row 1 (4 px) — horizontal splitter
///     +--------+---+--------+
///     | Panel2 | V | Panel3 |   Row 2 (*) — bottom half
///     +--------+---+--------+
///       Col 0    Col 1   Col 2
///        (*)    (4 px)    (*)
/// </code>
///
/// Mirrors the inline <c>ActivateSplit</c> / <c>DeactivateSplit</c>
/// pattern in MainWindow for the 2-pane case. The controller owns Grid
/// structure + splitter lifecycle; tab placement (Grid.SetRow /
/// Grid.SetColumn / Visibility) lives in the host because the host owns
/// the <c>_browserTabs</c> dictionary.
/// </summary>
public sealed class CouncilLayoutController
{
    /// <summary>Number of panels in a Council session. Locked to 4 by spec.</summary>
    public const int PanelCount = 4;

    private readonly Grid _browserContent;
    private readonly ILogger<CouncilLayoutController> _logger;
    private readonly List<string?> _panelTabIds = new() { null, null, null, null };

    // Splitter lifetime — references kept so DeactivateAsync can remove them.
    private GridSplitter? _verticalSplitter;
    private GridSplitter? _horizontalSplitter;

    /// <summary>True while the 2×2 layout is active in BrowserContent.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Tab IDs currently assigned to each panel (index 0 = top-left,
    /// 1 = top-right, 2 = bottom-left, 3 = bottom-right). Null when no
    /// tab is assigned to a slot.
    /// </summary>
    public IReadOnlyList<string?> PanelTabIds => _panelTabIds;

    /// <summary>Raised when <see cref="ActivateAsync"/> finishes successfully.</summary>
    public event Action? LayoutActivated;

    /// <summary>Raised when <see cref="DeactivateAsync"/> finishes.</summary>
    public event Action? LayoutDeactivated;

    public CouncilLayoutController(
        Grid browserContent,
        ILogger<CouncilLayoutController>? logger = null)
    {
        _browserContent = browserContent ?? throw new ArgumentNullException(nameof(browserContent));
        _logger = logger ?? NullLogger<CouncilLayoutController>.Instance;
    }

    /// <summary>
    /// Translates a panel index (0..<see cref="PanelCount"/>-1) into
    /// (Grid.Row, Grid.Column) coordinates. The host calls this when
    /// placing <c>BrowserTab</c> visuals into the activated layout.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="panelIndex"/> is outside <c>[0,3]</c>.
    /// </exception>
    public static (int Row, int Column) GetPanelCell(int panelIndex) => panelIndex switch
    {
        0 => (0, 0),  // top-left
        1 => (0, 2),  // top-right
        2 => (2, 0),  // bottom-left
        3 => (2, 2),  // bottom-right
        _ => throw new ArgumentOutOfRangeException(
                 nameof(panelIndex),
                 $"Council panel index must be in [0,{PanelCount - 1}], got {panelIndex}.")
    };

    /// <summary>
    /// Transitions <c>BrowserContent</c> into the 2×2 grid layout. Builds
    /// 3 columns × 3 rows (with a 4 px splitter row/column at index 1) and
    /// inserts the two <see cref="GridSplitter"/> seams. Stores the four
    /// tab IDs assigned to the panels.
    /// <br/>
    /// Tab placement (setting <c>Grid.SetRow</c> / <c>Grid.SetColumn</c> on
    /// the <c>BrowserTab</c> visuals and managing Visibility) is the host's
    /// responsibility — use <see cref="GetPanelCell"/> for the coordinates.
    /// </summary>
    /// <returns>
    /// True if the layout transitioned successfully. False if the layout
    /// was already active, or if <paramref name="tabIds"/> does not
    /// contain exactly <see cref="PanelCount"/> entries.
    /// </returns>
    public Task<bool> ActivateAsync(IReadOnlyList<string> tabIds, CancellationToken ct = default)
    {
        _ = ct;

        if (IsActive)
        {
            _logger.LogWarning("Council layout already active; ActivateAsync skipped.");
            return Task.FromResult(false);
        }

        if (tabIds is null || tabIds.Count != PanelCount)
        {
            _logger.LogWarning(
                "Council ActivateAsync requires exactly {Expected} tab IDs, got {Got}.",
                PanelCount, tabIds?.Count ?? 0);
            return Task.FromResult(false);
        }

        // Reset Grid structure to a 3×3 (content/splitter/content × content/splitter/content).
        // The host is responsible for any cleanup of pre-existing column/row defs.
        _browserContent.ColumnDefinitions.Clear();
        _browserContent.RowDefinitions.Clear();

        _browserContent.ColumnDefinitions.Add(new ColumnDefinition());                                   // col 0: panel column
        _browserContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });       // col 1: vertical splitter
        _browserContent.ColumnDefinitions.Add(new ColumnDefinition());                                   // col 2: panel column

        _browserContent.RowDefinitions.Add(new RowDefinition());                                         // row 0: panel row
        _browserContent.RowDefinitions.Add(new RowDefinition    { Height = new GridLength(4) });        // row 1: horizontal splitter
        _browserContent.RowDefinitions.Add(new RowDefinition());                                         // row 2: panel row

        // Splitter Background matches the v2.4 inline-split styling
        // (#FF2A2A3A — same colour the existing _panesSplitter uses). Keeping
        // them visually consistent for now; Phase 5 palette migration can
        // happen as a follow-up if maintainer wants the seams brighter.
        var seamBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3A));

        _verticalSplitter = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Background          = seamBrush,
            ResizeBehavior      = GridResizeBehavior.PreviousAndNext,
            ResizeDirection     = GridResizeDirection.Columns,
        };
        Grid.SetColumn(_verticalSplitter, 1);
        Grid.SetRow(_verticalSplitter, 0);
        Grid.SetRowSpan(_verticalSplitter, 3);

        _horizontalSplitter = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Background          = seamBrush,
            ResizeBehavior      = GridResizeBehavior.PreviousAndNext,
            ResizeDirection     = GridResizeDirection.Rows,
        };
        Grid.SetRow(_horizontalSplitter, 1);
        Grid.SetColumn(_horizontalSplitter, 0);
        Grid.SetColumnSpan(_horizontalSplitter, 3);

        // Insert at index 0 so any existing BrowserTab children sit above
        // the splitter seams in z-order (matches the v2.4 ActivateSplit
        // ordering — host re-adds tab visuals on top via OnTabActivated).
        _browserContent.Children.Insert(0, _verticalSplitter);
        _browserContent.Children.Insert(0, _horizontalSplitter);

        // Persist the tab assignment so the host can drive placement +
        // future RefreshLayout calls from a single source of truth.
        for (int i = 0; i < PanelCount; i++)
            _panelTabIds[i] = tabIds[i];

        IsActive = true;
        _logger.LogInformation(
            "Council layout activated with tabs [{Tabs}]",
            string.Join(", ", _panelTabIds));

        LayoutActivated?.Invoke();
        return Task.FromResult(true);
    }

    /// <summary>
    /// Restores <c>BrowserContent</c> to a single-cell layout (1 row × 1 col,
    /// both <c>*</c>), removes the splitter seams and clears the panel
    /// assignment. The host is responsible for re-collapsing tab visuals to
    /// the single-active-tab mode (matches the existing
    /// <c>DeactivateSplit</c> contract).
    /// </summary>
    public Task DeactivateAsync()
    {
        if (!IsActive)
        {
            _logger.LogDebug("Council layout not active; DeactivateAsync skipped.");
            return Task.CompletedTask;
        }

        if (_verticalSplitter != null)
        {
            _browserContent.Children.Remove(_verticalSplitter);
            _verticalSplitter = null;
        }
        if (_horizontalSplitter != null)
        {
            _browserContent.Children.Remove(_horizontalSplitter);
            _horizontalSplitter = null;
        }

        _browserContent.ColumnDefinitions.Clear();
        _browserContent.RowDefinitions.Clear();

        // Reset Grid.SetRow / Grid.SetColumn / Grid.SetRowSpan / Grid.SetColumnSpan
        // on every direct child so leftover attached values don't leak into
        // the next layout (1×1 default or the 2-pane split).
        foreach (var child in _browserContent.Children)
        {
            if (child is UIElement el)
            {
                Grid.SetRow(el, 0);
                Grid.SetColumn(el, 0);
                Grid.SetRowSpan(el, 1);
                Grid.SetColumnSpan(el, 1);
            }
        }

        for (int i = 0; i < PanelCount; i++)
            _panelTabIds[i] = null;

        IsActive = false;
        _logger.LogInformation("Council layout deactivated; BrowserContent collapsed to single cell.");

        LayoutDeactivated?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Re-applies <c>Grid.SetRow</c> / <c>Grid.SetColumn</c> for the panel
    /// visuals owned by the host. Today this is a stub — Phase 4.0 chunk C
    /// will wire it up against <c>MainWindow._browserTabs</c>. Mirrors the
    /// existing inline <c>RefreshSplitLayout</c> for the 2-pane case.
    /// </summary>
    public void RefreshLayout()
    {
        _logger.LogDebug(
            "CouncilLayoutController.RefreshLayout (stub for chunk C) — IsActive={IsActive}",
            IsActive);
    }
}
