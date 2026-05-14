using VELO.Data.Models;

namespace VELO.Data.Repositories;

/// <summary>
/// v2.4.43 — Persistent favicon cache keyed by hostname. Lets the
/// <c>TabSidebar</c> show real site icons across VELO restarts without paying
/// the WebView2 favicon-download round-trip on every nav. Empty Data is a
/// negative-cache marker so sites without a favicon don't get re-queried on
/// every tab open.
///
/// Pure data access — no I/O assumptions, no event surface. Host normalisation
/// happens here (lowercased + leading <c>www.</c> stripped) so callers can pass
/// whatever <c>Uri.Host</c> hands them.
/// </summary>
public class FaviconRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    /// <summary>Default eviction window: drop anything older than 30 days.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(30);

    /// <summary>Fetches the cached bytes for <paramref name="host"/> within
    /// <paramref name="ttl"/>. Returns null when the cache is missing, expired,
    /// or holds an empty (negative-cache) entry — caller treats null as "no
    /// favicon, please re-fetch from WebView2".</summary>
    public async Task<byte[]?> GetFreshAsync(string host, TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(host)) return null;
        var key = Normalise(host);
        var cutoff = DateTime.UtcNow - (ttl ?? DefaultTtl);

        var row = await _db.Connection.Table<FaviconEntry>()
            .Where(f => f.Host == key)
            .FirstOrDefaultAsync();

        if (row is null) return null;
        if (row.FetchedAtUtc < cutoff) return null; // expired
        if (row.Data.Length == 0)      return null; // negative-cache marker

        return row.Data;
    }

    /// <summary>Persists the favicon bytes for a host. Pass an empty array to
    /// record a negative cache entry (host has no favicon, don't ask again
    /// soon).</summary>
    public async Task SaveAsync(string host, byte[] data)
    {
        if (string.IsNullOrEmpty(host)) return;
        var entry = new FaviconEntry
        {
            Host          = Normalise(host),
            Data          = data ?? Array.Empty<byte>(),
            FetchedAtUtc  = DateTime.UtcNow,
        };
        await _db.Connection.InsertOrReplaceAsync(entry);
    }

    /// <summary>Drops cache entries older than <paramref name="ttl"/>.
    /// Returns the number of rows removed.</summary>
    public async Task<int> PurgeExpiredAsync(TimeSpan? ttl = null)
    {
        var cutoff = DateTime.UtcNow - (ttl ?? DefaultTtl);
        return await _db.Connection.Table<FaviconEntry>()
            .DeleteAsync(f => f.FetchedAtUtc < cutoff);
    }

    /// <summary>Removes a single host's entry. Useful for explicit "Clear browsing data"
    /// flows that want to scrub favicons too.</summary>
    public async Task DeleteAsync(string host)
    {
        if (string.IsNullOrEmpty(host)) return;
        var key = Normalise(host);
        await _db.Connection.Table<FaviconEntry>()
            .DeleteAsync(f => f.Host == key);
    }

    /// <summary>Lowercases and strips a leading <c>www.</c> so callers don't have to
    /// pre-normalise. Idempotent. <c>"WWW.Bambu.com" → "bambu.com"</c>.</summary>
    public static string Normalise(string host)
    {
        if (string.IsNullOrEmpty(host)) return "";
        var h = host.Trim().ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }
}
