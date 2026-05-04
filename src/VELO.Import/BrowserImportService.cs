using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Import.Detectors;
using VELO.Import.Importers;
using VELO.Import.Models;
using VELO.Vault;

namespace VELO.Import;

/// <summary>
/// Phase 3 / Sprint 4 — Top-level orchestrator. Detects installed browsers,
/// runs the per-section importers and writes through to VELO's repos
/// (BookmarkRepository / HistoryRepository / VaultService).
///
/// Detectors and Importers are pure I/O classes; tests instantiate them
/// directly with synthetic profile dirs and don't need this orchestrator.
/// </summary>
public sealed class BrowserImportService
{
    private readonly BookmarkRepository _bookmarks;
    private readonly HistoryRepository  _history;
    private readonly VaultService       _vault;
    private readonly ILogger<BrowserImportService> _logger;
    private readonly IReadOnlyList<IBrowserDetector> _detectors;

    public BrowserImportService(
        BookmarkRepository bookmarks,
        HistoryRepository  history,
        VaultService       vault,
        ILogger<BrowserImportService>?  logger    = null,
        IReadOnlyList<IBrowserDetector>? detectors = null)
    {
        _bookmarks = bookmarks;
        _history   = history;
        _vault     = vault;
        _logger    = logger ?? NullLogger<BrowserImportService>.Instance;
        _detectors = detectors ??
        [
            new ChromeDetector(),
            new EdgeDetector(),
            new BraveDetector(),
            new VivaldiDetector(),
            new OperaDetector(),
            new FirefoxDetector(),
        ];
    }

    /// <summary>Runs every detector and returns the ones that found a profile.</summary>
    public async Task<IReadOnlyList<DetectedBrowser>> DetectInstalledAsync(CancellationToken ct = default)
    {
        var tasks = _detectors.Select(d => d.DetectAsync(ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    /// <summary>
    /// Imports each section requested by <paramref name="opts"/>. Errors in
    /// one section don't abort the others — every result is recorded and
    /// surfaced through <see cref="ImportResult.Warnings"/> /
    /// <see cref="ImportResult.Errors"/>.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        DetectedBrowser browser,
        ImportOptions   opts,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ImportResult();

        // ── Bookmarks ─────────────────────────────────────────────────
        if (opts.Bookmarks)
        {
            try
            {
                var importer = new BookmarksImporter();
                var rows     = importer.Import(browser);
                foreach (var b in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    await _bookmarks.SaveAsync(new Bookmark
                    {
                        Url    = b.Url,
                        Title  = b.Title,
                        Folder = string.IsNullOrEmpty(b.FolderPath) ? "root" : b.FolderPath,
                    });
                    result.BookmarksImported++;
                }
                _logger.LogInformation("Imported {Count} bookmarks from {Browser}",
                    result.BookmarksImported, browser.Kind);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Errors.Add($"Bookmarks: {ex.Message}");
                _logger.LogWarning(ex, "Bookmarks import failed");
            }
            progress?.Report(25);
        }

        // ── History ───────────────────────────────────────────────────
        if (opts.History)
        {
            try
            {
                var importer = new HistoryImporter();
                var rows     = importer.Import(browser, opts);
                foreach (var h in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    await _history.SaveAsync(new HistoryEntry
                    {
                        Url       = h.Url,
                        Title     = h.Title,
                        VisitedAt = h.VisitedUtc,
                    });
                    result.HistoryImported++;
                }
                _logger.LogInformation("Imported {Count} history rows from {Browser}",
                    result.HistoryImported, browser.Kind);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Errors.Add($"History: {ex.Message}");
                _logger.LogWarning(ex, "History import failed");
            }
            progress?.Report(50);
        }

        // ── Passwords ─────────────────────────────────────────────────
        if (opts.Passwords)
        {
            try
            {
                if (!_vault.IsUnlocked)
                {
                    result.Warnings.Add("Vault is locked. Unlock it first to import passwords.");
                }
                else
                {
                    var importer = new PasswordImporter();
                    var pr = importer.Import(browser);
                    foreach (var w in pr.Warnings) result.Warnings.Add($"Passwords: {w}");
                    foreach (var c in pr.Credentials)
                    {
                        ct.ThrowIfCancellationRequested();
                        var siteName = TryHost(c.Url, fallback: c.Url);
                        await _vault.SaveAsync(new PasswordEntry
                        {
                            SiteName = siteName,
                            Url      = c.Url,
                            Username = c.Username,
                            Password = c.Password,
                            Notes    = $"Imported from {browser.DisplayName} on {DateTime.Now:yyyy-MM-dd}",
                        });
                        result.PasswordsImported++;
                    }
                    _logger.LogInformation("Imported {Count} passwords from {Browser}",
                        result.PasswordsImported, browser.Kind);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Errors.Add($"Passwords: {ex.Message}");
                _logger.LogWarning(ex, "Password import failed");
            }
            progress?.Report(85);
        }

        // ── Cookies / Search engines ──────────────────────────────────
        if (opts.Cookies)
            result.Warnings.Add("Cookie import is intentionally disabled (privacy + tracker risk).");
        if (opts.SearchEngines)
            result.Warnings.Add("Search engine import is not yet implemented.");

        progress?.Report(100);
        return result;
    }

    private static string TryHost(string url, string fallback)
    {
        try { return new Uri(url).Host; } catch { return fallback; }
    }
}
