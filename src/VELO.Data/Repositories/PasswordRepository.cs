using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class PasswordRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    public async Task<List<PasswordEntry>> GetAllAsync()
        => await _db.Connection.Table<PasswordEntry>()
            .OrderByDescending(p => p.ModifiedAt)
            .ToListAsync();

    public async Task<PasswordEntry?> FindByUrlAsync(string url)
    {
        var host = new Uri(url).Host;
        return await _db.Connection.Table<PasswordEntry>()
            .Where(p => p.Url.Contains(host))
            .FirstOrDefaultAsync();
    }

    public async Task SaveAsync(PasswordEntry entry)
    {
        entry.ModifiedAt = DateTime.UtcNow;
        await _db.Connection.InsertOrReplaceAsync(entry);
    }

    public async Task DeleteAsync(string id)
        => await _db.Connection.Table<PasswordEntry>()
            .DeleteAsync(p => p.Id == id);
}
