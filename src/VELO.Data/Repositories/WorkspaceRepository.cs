using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class WorkspaceRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    /// <summary>Returns all saved workspaces ordered by SortOrder.</summary>
    public async Task<List<WorkspaceEntry>> GetAllAsync()
        => await _db.Connection
            .Table<WorkspaceEntry>()
            .OrderBy(w => w.SortOrder)
            .ToListAsync();

    /// <summary>Inserts or updates a workspace.</summary>
    public async Task SaveAsync(WorkspaceEntry entry)
        => await _db.Connection.InsertOrReplaceAsync(entry);

    /// <summary>Persists the full ordered list atomically (used after reorder).</summary>
    public async Task SaveAllAsync(IEnumerable<WorkspaceEntry> entries)
    {
        var order = 0;
        foreach (var e in entries)
        {
            e.SortOrder = order++;
            await _db.Connection.InsertOrReplaceAsync(e);
        }
    }

    public async Task DeleteAsync(string workspaceId)
        => await _db.Connection.DeleteAsync<WorkspaceEntry>(workspaceId);

    public async Task<bool> AnyAsync()
        => await _db.Connection.Table<WorkspaceEntry>().CountAsync() > 0;
}
