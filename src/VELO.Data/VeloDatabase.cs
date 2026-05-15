using Microsoft.Extensions.Logging;
using SQLite;
using VELO.Data.Models;

namespace VELO.Data;

public class VeloDatabase : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly ILogger<VeloDatabase> _logger;

    public SQLiteAsyncConnection Connection => _db;

    /// <param name="dataFolderPath">
    /// Root user-data folder resolved by <c>DataLocation.GetUserDataPath()</c>.
    /// When empty/null, falls back to <c>%LocalAppData%\VELO\</c> (non-portable default).
    /// </param>
    public VeloDatabase(ILogger<VeloDatabase> logger, string? dataFolderPath = null)
    {
        _logger = logger;

        var folder = string.IsNullOrEmpty(dataFolderPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VELO")
            : dataFolderPath;

        Directory.CreateDirectory(folder);

        var dbPath = Path.Combine(folder, "velo.db");

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
            // v2.4.18 — Sprint 9B: Tags column added for BookmarkAIService auto-tag.
            try { await _db.ExecuteAsync("ALTER TABLE bookmarks ADD COLUMN Tags TEXT NOT NULL DEFAULT ''"); } catch { }
            await _db.CreateTableAsync<PasswordEntry>();
            await _db.CreateTableAsync<CachedVerdict>();
            await _db.CreateTableAsync<Container>();
            try { await _db.ExecuteAsync("ALTER TABLE containers ADD COLUMN ExpiresAt TEXT"); }     catch { }
            try { await _db.ExecuteAsync("ALTER TABLE containers ADD COLUMN IsBankingMode INTEGER DEFAULT 0"); } catch { }
            await _db.CreateTableAsync<MalwaredexEntry>();
            await _db.CreateTableAsync<PrivacyStats>();
            await _db.CreateTableAsync<WorkspaceEntry>();
            // v2.4.43 — favicon cache (host PK, byte[] data, fetched_at).
            await _db.CreateTableAsync<FaviconEntry>();

            await SeedDefaultContainersAsync();
            await SeedDefaultWorkspaceAsync();

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
        // Phase 4.0 chunk E — Council containers seed in two modes:
        //   1. Brand-new database (existing == 0): seed the v2.4 set
        //      (Personal/Work/Banking/Shopping/None) AND the four Council
        //      slots in one batch.
        //   2. Existing v2.4.x database (existing > 0): users who upgrade
        //      from a pre-Council install still need the four council-*
        //      containers, so we top-up just the new IDs without touching
        //      anything the user might have customised.
        var existing = await _db.Table<Container>().CountAsync();

        if (existing == 0)
        {
            await _db.InsertOrReplaceAsync(Container.Personal);
            await _db.InsertOrReplaceAsync(Container.Work);
            await _db.InsertOrReplaceAsync(Container.Banking);
            await _db.InsertOrReplaceAsync(Container.Shopping);
            await _db.InsertOrReplaceAsync(Container.None);
        }

        // Top-up Council containers regardless. InsertOrReplaceAsync is
        // idempotent on the primary key, so re-seeding on every launch is
        // cheap; this also self-heals if the user accidentally deletes
        // one of the four Council rows.
        await _db.InsertOrReplaceAsync(Container.CouncilClaude);
        await _db.InsertOrReplaceAsync(Container.CouncilChatGpt);
        await _db.InsertOrReplaceAsync(Container.CouncilGrok);
        await _db.InsertOrReplaceAsync(Container.CouncilOllama);
    }

    private async Task SeedDefaultWorkspaceAsync()
    {
        // v2.4.51 — migration for users created pre-Phase-5: if the default
        // workspace still has the original cyan colour and nobody has renamed
        // it, swap to AccentPurpleLight so it stops clashing with the v2.4.32+
        // purple palette. Tested condition is intentionally strict ("default"
        // id + "#00E5FF" colour + "Principal" name) so a user who customised
        // the colour OR renamed the workspace keeps their choice.
        try
        {
            await _db.ExecuteAsync(
                "UPDATE workspaces SET Color = '#FF8A6BFF' " +
                "WHERE Id = 'default' AND Color = '#00E5FF' AND Name = 'Principal'");
        }
        catch { /* table may not exist yet on first install — handled below */ }

        // Only seed when table is brand-new (no rows yet)
        var count = await _db.Table<WorkspaceEntry>().CountAsync();
        if (count > 0) return;

        await _db.InsertOrReplaceAsync(new WorkspaceEntry
        {
            Id        = "default",
            Name      = "Principal",
            // v2.4.51 — AccentPurpleLight (matches Phase 5 palette).
            Color     = "#FF8A6BFF",
            SortOrder = 0,
        });
    }

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
