using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.Navigation;
using VELO.Data.Repositories;
using VELO.UI.Controls;

namespace VELO.App.Controllers;

/// <summary>
/// Phase 3 / Sprint 10b chunk 4 (v2.4.29) — Extracted from
/// MainWindow.BuildCommandResultsAsync. Owns the per-keystroke command-bar
/// search across open tabs, bookmarks, history, built-in commands, and the
/// "navigate to typed input" fallback. Pure data orchestration; no WPF
/// dependency beyond <see cref="CommandResult"/>/<see cref="CommandResultKind"/>
/// which are plain types in <c>VELO.UI.Controls</c>.
///
/// Why the host (MainWindow) keeps the built-in commands list:
/// every built-in command's Action touches private MainWindow state
/// (TabManager, OpenHistory, OpenDownloads, AgentPanelControl, the
/// Settings/Vault dialog flows). Pulling those into the controller
/// would force a back-reference soup. Instead the host supplies them
/// via a <c>Func&lt;IEnumerable&lt;CommandResult&gt;&gt;</c> at
/// construction — same pattern <see cref="KeyboardShortcutsController"/>
/// uses for its binding table.
///
/// Cap (60) and dedup behaviour are preserved 1:1 from the original
/// switch; only the assembly seam changed.
/// </summary>
public sealed class CommandPaletteController
{
    private readonly Func<IEnumerable<TabInfo>> _openTabs;
    private readonly BookmarkRepository _bookmarks;
    private readonly HistoryRepository _history;
    private readonly Func<IEnumerable<CommandResult>> _builtInCommands;
    private readonly ILogger<CommandPaletteController> _logger;

    /// <summary>Maximum total results returned (history truncates first).</summary>
    public int MaxResults { get; init; } = 60;

    /// <summary>Maximum recent-history entries fetched on a blank query.</summary>
    public int RecentHistoryLimit { get; init; } = 30;

    public CommandPaletteController(
        Func<IEnumerable<TabInfo>> openTabsProvider,
        BookmarkRepository bookmarks,
        HistoryRepository history,
        Func<IEnumerable<CommandResult>> builtInCommandsProvider,
        ILogger<CommandPaletteController>? logger = null)
    {
        _openTabs        = openTabsProvider;
        _bookmarks       = bookmarks;
        _history         = history;
        _builtInCommands = builtInCommandsProvider;
        _logger          = logger ?? NullLogger<CommandPaletteController>.Instance;
    }

    /// <summary>
    /// Builds the result list for the given query. Empty query returns
    /// open tabs + recent history (30) + built-in commands + bookmarks
    /// (all). Non-empty query filters every source case-insensitively and
    /// adds a "navigate to typed input" fallback when nothing matched.
    /// Failures in any individual source are logged and skipped — the
    /// other sources still surface.
    /// </summary>
    public async Task<List<CommandResult>> BuildResultsAsync(
        string query, CancellationToken ct = default)
    {
        var q       = (query ?? "").Trim();
        var list    = new List<CommandResult>();
        var isBlank = string.IsNullOrEmpty(q);

        // ── Open tabs ─────────────────────────────────────────────────────
        foreach (var tab in _openTabs())
        {
            if (!isBlank &&
                !Contains(tab.Title, q) &&
                !Contains(tab.Url, q)) continue;

            list.Add(new CommandResult
            {
                Kind     = CommandResultKind.Tab,
                Icon     = tab.IsLoading ? "⏳" : "🌐",
                Title    = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title,
                Subtitle = tab.Url,
                Badge    = "pestaña",
                Tag      = tab.Id,
            });
        }

        // ── Bookmarks ─────────────────────────────────────────────────────
        try
        {
            var bookmarks = await _bookmarks.GetAllAsync();
            foreach (var bm in bookmarks)
            {
                if (!isBlank && !Contains(bm.Title, q) && !Contains(bm.Url, q)) continue;
                list.Add(new CommandResult
                {
                    Kind     = CommandResultKind.Bookmark,
                    Icon     = "⭐",
                    Title    = bm.Title,
                    Subtitle = bm.Url,
                    Badge    = "marcador",
                    Tag      = bm.Url,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CommandPalette: bookmark search failed (showing remaining sources)");
        }

        // ── History ────────────────────────────────────────────────────────
        try
        {
            var entries = isBlank
                ? await _history.GetRecentAsync(RecentHistoryLimit)
                : await _history.SearchAsync(q);

            var seen = new HashSet<string>();
            foreach (var h in entries)
            {
                if (!seen.Add(h.Url)) continue;
                if (list.Count >= MaxResults) break;
                list.Add(new CommandResult
                {
                    Kind     = CommandResultKind.History,
                    Icon     = "🕒",
                    Title    = string.IsNullOrWhiteSpace(h.Title) ? h.Url : h.Title,
                    Subtitle = h.Url,
                    Badge    = "historial",
                    Tag      = h.Url,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CommandPalette: history search failed (showing remaining sources)");
        }

        // ── Built-in commands ─────────────────────────────────────────────
        foreach (var cmd in _builtInCommands())
        {
            if (!isBlank && !Contains(cmd.Title, q)) continue;
            list.Add(cmd);
        }

        // ── Navigate to typed URL / search query ───────────────────────────
        if (!isBlank && list.Count == 0)
        {
            list.Add(new CommandResult
            {
                Kind     = CommandResultKind.Navigate,
                Icon     = "↗",
                Title    = q,
                Subtitle = "Navegar o buscar",
                Badge    = "ir",
                Tag      = q,
            });
        }

        return list;
    }

    private static bool Contains(string? source, string query)
        => source?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
