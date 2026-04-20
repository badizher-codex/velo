using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class HistoryRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    public async Task SaveAsync(HistoryEntry entry)
        => await _db.Connection.InsertAsync(entry);

    public async Task<List<HistoryEntry>> GetRecentAsync(int limit = 100)
        => await _db.Connection.Table<HistoryEntry>()
            .OrderByDescending(h => h.VisitedAt)
            .Take(limit)
            .ToListAsync();

    public async Task<List<HistoryEntry>> SearchAsync(string query)
        => await _db.Connection.Table<HistoryEntry>()
            .Where(h => h.Url.Contains(query) || (h.Title != null && h.Title.Contains(query)))
            .OrderByDescending(h => h.VisitedAt)
            .Take(50)
            .ToListAsync();

    public async Task DeleteAsync(int id)
        => await _db.Connection.DeleteAsync<HistoryEntry>(id);

    public async Task ClearAllAsync()
        => await _db.Connection.DeleteAllAsync<HistoryEntry>();

    public async Task DeleteOlderThanAsync(int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        await _db.Connection.Table<HistoryEntry>()
            .DeleteAsync(h => h.VisitedAt < cutoff);
    }

    /// <summary>
    /// Returns up to <paramref name="limit"/> most-visited distinct origins,
    /// grouped by host. Each entry carries the most-recent URL, title, visit count,
    /// and the total trackers blocked across all visits to that host.
    /// </summary>
    public async Task<List<TopSiteEntry>> GetTopSitesAsync(int limit = 8)
    {
        var all = await _db.Connection.Table<HistoryEntry>()
            .Where(h => h.Url != null && h.Url.StartsWith("http"))
            .OrderByDescending(h => h.VisitedAt)
            .Take(500)
            .ToListAsync();

        return all
            .GroupBy(h => TryGetHost(h.Url))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .Select(g =>
            {
                var best = g.OrderByDescending(h => h.VisitedAt).First();
                return new TopSiteEntry(
                    Url:              best.Url,
                    Host:             g.Key,
                    Title:            string.IsNullOrWhiteSpace(best.Title) ? g.Key : best.Title!,
                    VisitCount:       g.Count(),
                    TrackersBlocked:  g.Sum(h => h.TrackerCount));
            })
            .ToList();
    }

    /// <summary>Returns lifetime totals across all history entries.</summary>
    public async Task<(int TotalTrackers, int TotalBlocked, int TotalSites)> GetLifetimeStatsAsync()
    {
        var all = await _db.Connection.Table<HistoryEntry>().ToListAsync();
        return (all.Sum(h => h.TrackerCount),
                all.Sum(h => h.BlockedCount),
                all.Select(h => TryGetHost(h.Url)).Where(h => !string.IsNullOrEmpty(h)).Distinct().Count());
    }

    private static string TryGetHost(string url)
    {
        try { return new Uri(url).Host.ToLowerInvariant(); }
        catch { return ""; }
    }
}

public record TopSiteEntry(
    string Url,
    string Host,
    string Title,
    int    VisitCount,
    int    TrackersBlocked);
