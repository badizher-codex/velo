using Microsoft.Extensions.Logging;
using SQLite;
using VELO.Data.Models;

namespace VELO.Data;

public class VeloDatabase : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly ILogger<VeloDatabase> _logger;

    public SQLiteAsyncConnection Connection => _db;

    public VeloDatabase(ILogger<VeloDatabase> logger)
    {
        _logger = logger;

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VELO");
        Directory.CreateDirectory(appDataPath);

        var dbPath = Path.Combine(appDataPath, "velo.db");

        // SQLCipher connection — password is set after unlock
        // For initial setup we use an empty password; the vault uses a separate key derived from master password
        var options = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: null);
        _db = new SQLiteAsyncConnection(options);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _db.CreateTableAsync<AppSettings>();
            await _db.CreateTableAsync<HistoryEntry>();
            // Add new columns to existing history table if missing (ALTER TABLE ignores duplicates via catch)
            try { await _db.ExecuteAsync("ALTER TABLE history ADD COLUMN BlockedCount INTEGER DEFAULT 0"); } catch { }
            try { await _db.ExecuteAsync("ALTER TABLE history ADD COLUMN TrackerCount INTEGER DEFAULT 0"); } catch { }
            try { await _db.ExecuteAsync("ALTER TABLE history ADD COLUMN MalwareCount INTEGER DEFAULT 0"); } catch { }
            try { await _db.ExecuteAsync("ALTER TABLE history ADD COLUMN MonsterCaptured INTEGER DEFAULT 0"); } catch { }
            await _db.CreateTableAsync<Bookmark>();
            await _db.CreateTableAsync<PasswordEntry>();
            await _db.CreateTableAsync<CachedVerdict>();
            await _db.CreateTableAsync<Container>();
            await _db.CreateTableAsync<MalwaredexEntry>();

            await SeedDefaultContainersAsync();

            _logger.LogInformation("Database initialized at {Path}", _db.DatabasePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    private async Task SeedDefaultContainersAsync()
    {
        var existing = await _db.Table<Container>().CountAsync();
        if (existing > 0) return;

        await _db.InsertOrReplaceAsync(Container.Personal);
        await _db.InsertOrReplaceAsync(Container.Work);
        await _db.InsertOrReplaceAsync(Container.Banking);
        await _db.InsertOrReplaceAsync(Container.Shopping);
        await _db.InsertOrReplaceAsync(Container.None);
    }

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
