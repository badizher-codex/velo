using SQLite;

namespace VELO.Data.Models;

[Table("history")]
public class HistoryEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Url { get; set; } = "";

    public string? Title { get; set; }
    public string? ContainerId { get; set; }

    [Indexed]
    public DateTime VisitedAt { get; set; } = DateTime.UtcNow;

    public int  BlockedCount    { get; set; }
    public int  TrackerCount    { get; set; }
    public int  MalwareCount    { get; set; }
    public bool MonsterCaptured { get; set; }  // Malwaredex — reservado Fase 2

    public byte[]? FaviconBlob { get; set; }
}
