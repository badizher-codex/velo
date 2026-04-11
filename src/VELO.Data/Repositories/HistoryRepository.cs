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
}
