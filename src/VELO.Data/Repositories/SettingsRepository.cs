using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class SettingsRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    public async Task<string?> GetAsync(string key)
    {
        var row = await _db.Connection.Table<AppSettings>()
            .Where(s => s.Key == key)
            .FirstOrDefaultAsync();
        return row?.Value;
    }

    public async Task<string> GetAsync(string key, string defaultValue)
        => await GetAsync(key) ?? defaultValue;

    public async Task SetAsync(string key, string value)
    {
        await _db.Connection.InsertOrReplaceAsync(new AppSettings
        {
            Key = key,
            Value = value,
            ModifiedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var val = await GetAsync(key);
        return val == null ? defaultValue : val == "true";
    }

    public Task SetBoolAsync(string key, bool value)
        => SetAsync(key, value ? "true" : "false");

    public async Task<int> GetIntAsync(string key, int defaultValue = 0)
    {
        var val = await GetAsync(key);
        return val != null && int.TryParse(val, out var i) ? i : defaultValue;
    }

    public Task SetIntAsync(string key, int value)
        => SetAsync(key, value.ToString());
}
