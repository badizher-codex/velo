using System.Windows.Controls;
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
/// simultaneously. The two layouts are independent: activating Council does
/// not deactivate the 2-pane split, and vice versa — only one can be active
/// at a time but the state machine lives in the host. This controller owns
/// only the Council-specific path.
///
/// API design notes:
///
///   • <see cref="PanelCount"/> is locked to 4 by spec. Future Council
///     variants (3-pane, 6-pane) would extend the controller, not parameterise
///     this constant.
///   • <see cref="ActivateAsync"/> takes a list of tab IDs (one per panel).
///     The caller — MainWindow — is responsible for resolving those IDs to
///     real <c>BrowserTab</c> instances and for tab restore/persist semantics.
///   • Layout activation toggles the visual tree of <c>BrowserContent</c>
///     (a <see cref="Grid"/> passed at construction) into a 2 rows × 2 cols
///     arrangement with a vertical <see cref="GridSplitter"/> and a horizontal
///     splitter in a "+" pattern.
///
/// <b>Phase 4.0 chunk A:</b> scaffold only. Public surface, no behaviour.
/// Chunk B implements <see cref="ActivateAsync"/> / <see cref="DeactivateAsync"/>
/// / <see cref="RefreshLayout"/>.
/// </summary>
public sealed class CouncilLayoutController
{
    /// <summary>Number of panels in a Council session. Locked to 4 by spec.</summary>
    public const int PanelCount = 4;

    private readonly Grid _browserContent;
    private readonly ILogger<CouncilLayoutController> _logger;
    private readonly List<string?> _panelTabIds = new() { null, null, null, null };

    /// <summary>True while the 2×2 layout is active in BrowserContent.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Tab IDs currently assigned to each panel (index 0 = top-left,
    /// 1 = top-right, 2 = bottom-left, 3 = bottom-right). Null when no
    /// tab is assigned to a slot (e.g. the layout activated but a slot's
    /// tab hasn't been hydrated yet).
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
    /// Phase 4.0 chunk A — stub. Future chunks will:
    ///   1. Validate that exactly <see cref="PanelCount"/> tab IDs were passed.
    ///   2. Rebuild <c>BrowserContent.ColumnDefinitions</c>/<c>RowDefinitions</c>
    ///      to a 2×2 grid + two <see cref="GridSplitter"/> seams.
    ///   3. Move each provided <c>BrowserTab</c> visual into the matching
    ///      Grid cell (the visual tree is host-owned, the controller only
    ///      manipulates Grid.SetRow / Grid.SetColumn).
    ///   4. Set <see cref="IsActive"/> = true and raise <see cref="LayoutActivated"/>.
    /// </summary>
    public Task<bool> ActivateAsync(IReadOnlyList<string> tabIds, CancellationToken ct = default)
    {
        _ = ct;
        _logger.LogInformation(
            "CouncilLayoutController.ActivateAsync called with {Count} tab(s) — stub, no-op until Phase 4.0 chunk B",
            tabIds?.Count ?? 0);
        return Task.FromResult(false);
    }

    /// <summary>
    /// Phase 4.0 chunk A — stub. Future chunks will:
    ///   1. Restore <c>BrowserContent</c>'s grid to single-cell.
    ///   2. Move panel tabs back to their pre-Council arrangement (or hide
    ///      them per maintainer setting).
    ///   3. Clear <see cref="PanelTabIds"/>.
    ///   4. Set <see cref="IsActive"/> = false and raise <see cref="LayoutDeactivated"/>.
    /// </summary>
    public Task DeactivateAsync()
    {
        _logger.LogInformation(
            "CouncilLayoutController.DeactivateAsync called — stub, no-op until Phase 4.0 chunk B");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Phase 4.0 chunk A — stub. Future chunks will re-apply <c>Grid.SetRow</c>
    /// / <c>Grid.SetColumn</c> for all currently-assigned panel tabs (used
    /// when a tab inside a Council panel navigates and its visual needs
    /// re-attachment, mirroring the existing <c>RefreshSplitLayout</c> in
    /// MainWindow for the 2-pane case).
    /// </summary>
    public void RefreshLayout()
    {
        _logger.LogDebug("CouncilLayoutController.RefreshLayout called — stub");
    }
}
