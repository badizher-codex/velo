using SQLite;

namespace VELO.Data.Models;

/// <summary>
/// v2.4.43 — One row per host (e.g. <c>www.bambulab.com</c>) holding the PNG
/// bytes of that host's favicon. Used by <see cref="Repositories.FaviconRepository"/>
/// so tab favicons survive restarts without re-downloading from WebView2 on
/// every session.
///
/// Schema is intentionally tiny — favicons average ~5-30 KB and we only need
/// (key, blob, when). Eviction is by age: anything older than 30 days gets
/// deleted on cache pressure to keep the DB bounded.
/// </summary>
[Table("favicons")]
public class FaviconEntry
{
    /// <summary>The hostname (lowercased) the favicon belongs to.
    /// Primary key — one entry per host.</summary>
    [PrimaryKey] public string Host { get; set; } = "";

    /// <summary>Raw favicon bytes (typically PNG from WebView2.GetFaviconAsync,
    /// occasionally ICO). Empty array means "no favicon" (negative cache, so we
    /// don't re-request on every nav).</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>When the favicon was last captured. Used by
    /// <c>FaviconRepository.PurgeExpiredAsync</c> for TTL eviction.</summary>
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
