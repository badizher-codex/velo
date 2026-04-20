using SQLite;

namespace VELO.Data.Models;

[Table("privacy_stats")]
public class PrivacyStats
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, Indexed]
    public string TabId { get; set; } = "";

    [NotNull]
    public string Domain { get; set; } = "";

    public string Url { get; set; } = "";

    public int TrackersBlocked     { get; set; }
    public int AdsBlocked          { get; set; }
    public int FingerprintBlocked  { get; set; }
    public int RequestsTotal       { get; set; }
    public int RequestsBlocked     { get; set; }

    public bool IsGoldenList       { get; set; }
    public int  ShieldScore        { get; set; }   // numeric -100..+100

    public DateTime SessionStart   { get; set; } = DateTime.UtcNow;
    public DateTime SessionEnd     { get; set; } = DateTime.UtcNow;
    public int DurationSeconds     { get; set; }
}
