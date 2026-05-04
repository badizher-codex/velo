using System.IO;
using SQLite;
using VELO.Import.Models;

namespace VELO.Import.Importers;

/// <summary>
/// Phase 3 / Sprint 4 — Reads recent history from a Chromium or Firefox
/// profile. Returns at most <see cref="ImportOptions.HistoryMaxItems"/>
/// rows (newest first), filtered by <see cref="ImportOptions.HistoryMaxDays"/>.
///
/// Timestamps:
///   • Chromium stores microseconds since 1601-01-01 UTC (Windows FILETIME
///     epoch). We convert via <see cref="ChromiumTimeToUtc"/>.
///   • Firefox stores microseconds since 1970-01-01 UTC (UNIX epoch in µs).
/// </summary>
public sealed class HistoryImporter
{
    public IReadOnlyList<ImportedHistory> Import(DetectedBrowser browser, ImportOptions opts)
    {
        var path = browser.IsChromium
            ? Path.Combine(browser.ProfilePath, "History")
            : Path.Combine(browser.ProfilePath, "places.sqlite");
        if (!File.Exists(path)) return [];

        // Copy to temp so we can read while the source browser is running
        // (its SQLite WAL lock stays on the original).
        var tempPath = Path.Combine(Path.GetTempPath(), $"velo-import-history-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.Copy(path, tempPath, overwrite: true);
            using var conn = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadOnly);

            return browser.IsChromium ? ReadChromium(conn, opts) : ReadFirefox(conn, opts);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ── Chromium ───────────────────────────────────────────────────────

    private static IReadOnlyList<ImportedHistory> ReadChromium(SQLiteConnection conn, ImportOptions opts)
    {
        // Chromium last_visit_time = µs since 1601-01-01 UTC.
        var minTimeChromium = UtcToChromiumTime(DateTime.UtcNow.AddDays(-opts.HistoryMaxDays));

        var rows = conn.Query<UrlsRow>(@"
            SELECT url, COALESCE(title,'') AS title, last_visit_time
            FROM urls
            WHERE last_visit_time >= ?
            ORDER BY last_visit_time DESC
            LIMIT ?", minTimeChromium, opts.HistoryMaxItems);

        var result = new List<ImportedHistory>(rows.Count);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.url)) continue;
            result.Add(new ImportedHistory(r.url, r.title, ChromiumTimeToUtc(r.last_visit_time)));
        }
        return result;
    }

    private sealed class UrlsRow
    {
        public string url             { get; set; } = "";
        public string title           { get; set; } = "";
        public long   last_visit_time { get; set; }
    }

    // ── Firefox ────────────────────────────────────────────────────────

    private static IReadOnlyList<ImportedHistory> ReadFirefox(SQLiteConnection conn, ImportOptions opts)
    {
        // Firefox last_visit_date = µs since 1970-01-01 UTC.
        var minTimeFirefox = (DateTime.UtcNow.AddDays(-opts.HistoryMaxDays) - new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc))
                                 .Ticks / 10L;  // Ticks (100 ns) → µs

        var rows = conn.Query<MozHistoryRow>(@"
            SELECT url, COALESCE(title,'') AS title, last_visit_date
            FROM moz_places
            WHERE last_visit_date IS NOT NULL
              AND last_visit_date >= ?
              AND url NOT LIKE 'place:%'
            ORDER BY last_visit_date DESC
            LIMIT ?", minTimeFirefox, opts.HistoryMaxItems);

        var result = new List<ImportedHistory>(rows.Count);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.url)) continue;
            var visited = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddTicks(r.last_visit_date * 10L);
            result.Add(new ImportedHistory(r.url, r.title, visited));
        }
        return result;
    }

    private sealed class MozHistoryRow
    {
        public string url             { get; set; } = "";
        public string title           { get; set; } = "";
        public long   last_visit_date { get; set; }
    }

    // ── Time conversion helpers (public so tests can hit them directly) ─

    /// <summary>
    /// Converts a Chromium <c>last_visit_time</c> (µs since 1601-01-01 UTC,
    /// i.e. the Windows FILETIME epoch) to a UTC DateTime.
    /// </summary>
    public static DateTime ChromiumTimeToUtc(long microsSince1601)
    {
        if (microsSince1601 == 0) return DateTime.UnixEpoch;
        // FILETIME is 100-ns ticks → microseconds × 10 = ticks.
        var ticks = microsSince1601 * 10L;
        return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticks);
    }

    /// <summary>Inverse of <see cref="ChromiumTimeToUtc"/> — used for query bounds.</summary>
    public static long UtcToChromiumTime(DateTime utc)
    {
        var diff = utc.ToUniversalTime() - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return diff.Ticks / 10L;
    }
}

/// <summary>One history row produced by the importer.</summary>
public sealed record ImportedHistory(string Url, string Title, DateTime VisitedUtc);
