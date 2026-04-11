using SQLite;

namespace VELO.Data.Models;

[Table("security_cache")]
public class CachedVerdict
{
    [PrimaryKey]
    public string CacheKey { get; set; } = "";

    [NotNull, Indexed]
    public string Domain { get; set; } = "";

    [NotNull]
    public string Verdict { get; set; } = "";   // SAFE, WARN, BLOCK

    public int Confidence { get; set; }
    public string? Reason { get; set; }
    public string? ThreatType { get; set; }
    public string? Source { get; set; }         // AI_CLAUDE, OFFLINE, BLOCKLIST, TLS

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public DateTime ExpiresAt { get; set; }
}
