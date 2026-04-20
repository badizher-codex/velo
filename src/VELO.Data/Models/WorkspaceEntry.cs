using SQLite;

namespace VELO.Data.Models;

[Table("workspaces")]
public class WorkspaceEntry
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [NotNull]
    public string Name { get; set; } = "Workspace";

    [NotNull]
    public string Color { get; set; } = "#808080";

    /// <summary>Display order in the workspace strip (ascending).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
