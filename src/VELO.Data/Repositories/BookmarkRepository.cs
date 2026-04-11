using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class BookmarkRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    public async Task<List<Bookmark>> GetAllAsync()
        => await _db.Connection.Table<Bookmark>()
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

    public async Task<List<Bookmark>> GetRecentAsync(int limit = 8)
        => await _db.Connection.Table<Bookmark>()
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .ToListAsync();

    public async Task SaveAsync(Bookmark bookmark)
        => await _db.Connection.InsertOrReplaceAsync(bookmark);

    public async Task DeleteAsync(string id)
        => await _db.Connection.Table<Bookmark>()
            .DeleteAsync(b => b.Id == id);
}
