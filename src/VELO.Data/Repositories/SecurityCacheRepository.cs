using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class SecurityCacheRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    public async Task<CachedVerdict?> GetByKeyAsync(string key)
        => await _db.Connection.Table<CachedVerdict>()
            .Where(c => c.CacheKey == key)
            .FirstOrDefaultAsync();

    public async Task SaveAsync(CachedVerdict verdict)
        => await _db.Connection.InsertOrReplaceAsync(verdict);

    public async Task DeleteAsync(string key)
        => await _db.Connection.Table<CachedVerdict>()
            .DeleteAsync(c => c.CacheKey == key);

    public async Task PurgeExpiredAsync()
        => await _db.Connection.Table<CachedVerdict>()
            .DeleteAsync(c => c.ExpiresAt < DateTime.UtcNow);
}
