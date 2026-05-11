using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Security.Guards;

/// <summary>
/// Phase 3 / v2.4.25 — Returns the age in days of a domain by querying the
/// authoritative IANA RDAP bootstrap (no third-party intermediary). Used by
/// <see cref="PhishingShield"/> as one more risk signal: fresh domains
/// (≤30 days) on a page that asks for credentials are heavily over-
/// represented in phishing campaigns.
///
/// Privacy stance:
///
///   • <b>Off by default.</b> The user opts in explicitly via
///     <c>SettingKeys.PhishingShieldDomainAgeCheck</c>. When off, the probe
///     is a no-op that returns 0.
///   • <b>Gated by suspicion.</b> The probe is called only after
///     PhishingShield's quick-gate already trips on other signals
///     (suspicious TLD, brand impersonation, broken TLS, login form on a
///     random-looking host). On normal browsing the probe never fires.
///   • <b>Direct IANA bootstrap.</b> We download
///     <c>https://data.iana.org/rdap/dns.json</c> once per 24 h and learn
///     the RDAP server URL for each TLD. Then we query that authoritative
///     server directly — no aggregator, no third-party API that could
///     correlate domains.
///   • <b>Aggressively cached.</b> Result per eTLD+1 with 7-day TTL, so a
///     hostile site repeatedly visited burns one lookup per week.
///
/// Pure-helper API (<see cref="GetEtldPlusOne"/>, <see cref="ParseBootstrap"/>,
/// <see cref="ParseRegistrationDate"/>) lives next to the async surface so
/// unit tests don't need HttpClient. The I/O hook
/// <see cref="HttpGet"/> defaults to a real HttpClient but can be stubbed
/// in tests with a sync function — same pattern BookmarkAIService uses.
/// </summary>
public sealed class DomainAgeProbe
{
    public sealed record Result(string EtldPlusOne, int AgeDays, DateTime FetchedAtUtc);

    /// <summary>
    /// I/O hook. <c>(url, ct) =&gt; response body or null on failure.</c>
    /// Defaults to a real HttpClient with 5 s timeout; replace in tests.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>> HttpGet { get; set; }

    /// <summary>When false, every call returns 0 immediately. Settings-toggled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>How long the TLD→RDAP-server bootstrap map stays cached.</summary>
    public TimeSpan BootstrapTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long an individual domain-age verdict stays cached.</summary>
    public TimeSpan ResultTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The IANA bootstrap URL. Override only in tests.</summary>
    public string BootstrapUrl { get; set; } = "https://data.iana.org/rdap/dns.json";

    private readonly ILogger<DomainAgeProbe> _logger;
    private readonly object _lock = new();

    // TLD (lowercase, no dot) → base RDAP server URL (always ends with /)
    private Dictionary<string, string> _tldToServer = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _bootstrapAt = DateTime.MinValue;

    // eTLD+1 (lowercase) → cached result
    private readonly Dictionary<string, Result> _results = new(StringComparer.OrdinalIgnoreCase);

    public DomainAgeProbe(ILogger<DomainAgeProbe>? logger = null)
    {
        _logger = logger ?? NullLogger<DomainAgeProbe>.Instance;
        // Default real HTTP. Tests overwrite via property.
        HttpGet = DefaultHttpGet;
    }

    /// <summary>
    /// Returns the age in days of <paramref name="host"/>'s eTLD+1, or 0 when
    /// disabled / not in cache / lookup failed. Never throws.
    /// </summary>
    public async Task<int> GetDomainAgeDaysAsync(string host, CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        if (string.IsNullOrWhiteSpace(host)) return 0;

        var etld1 = GetEtldPlusOne(host);
        if (string.IsNullOrEmpty(etld1)) return 0;

        // Result cache hot path.
        lock (_lock)
        {
            if (_results.TryGetValue(etld1, out var hit) &&
                (DateTime.UtcNow - hit.FetchedAtUtc) < ResultTtl)
            {
                return hit.AgeDays;
            }
        }

        try
        {
            var tld = etld1.Split('.').Last();
            var serverBase = await GetRdapServerForTldAsync(tld, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(serverBase)) return 0;

            var url = serverBase.TrimEnd('/') + "/domain/" + Uri.EscapeDataString(etld1);
            var body = await HttpGet(url, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return 0;

            var regDate = ParseRegistrationDate(body);
            if (regDate is null) return 0;

            var ageDays = (int)Math.Max(0, (DateTime.UtcNow - regDate.Value).TotalDays);
            var result  = new Result(etld1, ageDays, DateTime.UtcNow);

            lock (_lock) { _results[etld1] = result; }
            _logger.LogDebug("DomainAgeProbe {Etld1}: {Age} days", etld1, ageDays);
            return ageDays;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DomainAgeProbe lookup failed for {Host}", host);
            return 0;
        }
    }

    /// <summary>Test helper.</summary>
    public int ResultCacheCount { get { lock (_lock) return _results.Count; } }

    /// <summary>Test helper.</summary>
    public int BootstrapEntryCount { get { lock (_lock) return _tldToServer.Count; } }

    /// <summary>Test helper.</summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _results.Clear();
            _tldToServer.Clear();
            _bootstrapAt = DateTime.MinValue;
        }
    }

    // ── Bootstrap ────────────────────────────────────────────────────────

    private async Task<string?> GetRdapServerForTldAsync(string tld, CancellationToken ct)
    {
        // Try cache first.
        lock (_lock)
        {
            if ((DateTime.UtcNow - _bootstrapAt) < BootstrapTtl &&
                _tldToServer.TryGetValue(tld, out var cached))
                return cached;
        }

        // Load fresh bootstrap.
        var body = await HttpGet(BootstrapUrl, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(body)) return null;

        var map = ParseBootstrap(body);
        lock (_lock)
        {
            _tldToServer = map;
            _bootstrapAt = DateTime.UtcNow;
            return map.TryGetValue(tld, out var server) ? server : null;
        }
    }

    // ── Pure helpers (public for test coverage) ───────────────────────────

    /// <summary>
    /// Returns the eTLD+1 of <paramref name="host"/>: the last two labels,
    /// lowercased. Strips a leading "www." for consistency. Returns empty
    /// for IPs, single-label hosts, or empty input. Does NOT consult the
    /// public-suffix list — simplified for the phishing-shield use case
    /// where false-positives on multi-label ccTLDs (co.uk) lean toward
    /// "no lookup performed" rather than incorrect blocking.
    /// </summary>
    public static string GetEtldPlusOne(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        var h = host.ToLowerInvariant().Trim();
        if (h.StartsWith("www.")) h = h[4..];

        // Reject IPv4 literals (cheap detection).
        if (h.Count(c => c == '.') == 3 && h.All(c => char.IsDigit(c) || c == '.'))
            return "";
        // Reject IPv6 literals.
        if (h.Contains(':')) return "";

        var parts = h.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return "";
        return $"{parts[^2]}.{parts[^1]}";
    }

    /// <summary>
    /// Parses the IANA RDAP bootstrap JSON
    /// (<c>https://data.iana.org/rdap/dns.json</c>) into a TLD → server-URL
    /// map. The bootstrap structure is:
    /// <code>
    /// {
    ///   "services": [
    ///     [ ["xyz","top",...], ["https://rdap.centralnic.com/"] ],
    ///     ...
    ///   ]
    /// }
    /// </code>
    /// Each "service" entry is a pair of arrays: TLDs covered, then
    /// server URLs. We take the first URL of each pair (servers are
    /// listed in preference order). Unknown structures are skipped.
    /// </summary>
    public static Dictionary<string, string> ParseBootstrap(string bootstrapJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(bootstrapJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(bootstrapJson);
            if (!doc.RootElement.TryGetProperty("services", out var services)) return result;
            if (services.ValueKind != JsonValueKind.Array) return result;

            foreach (var entry in services.EnumerateArray())
            {
                try
                {
                    if (entry.ValueKind != JsonValueKind.Array) continue;
                    var pair = entry.EnumerateArray().ToArray();
                    if (pair.Length < 2) continue;
                    if (pair[0].ValueKind != JsonValueKind.Array) continue;
                    if (pair[1].ValueKind != JsonValueKind.Array) continue;

                    // First URL is the preferred server. Use explicit loop so
                    // an empty server array doesn't poison sibling entries.
                    string? firstServer = null;
                    foreach (var s in pair[1].EnumerateArray())
                    {
                        if (s.ValueKind != JsonValueKind.String) continue;
                        firstServer = s.GetString();
                        if (!string.IsNullOrEmpty(firstServer)) break;
                    }
                    if (string.IsNullOrEmpty(firstServer)) continue;
                    if (!firstServer.EndsWith('/')) firstServer += "/";

                    foreach (var tldElement in pair[0].EnumerateArray())
                    {
                        if (tldElement.ValueKind != JsonValueKind.String) continue;
                        var tld = tldElement.GetString();
                        if (!string.IsNullOrWhiteSpace(tld))
                            result[tld] = firstServer;
                    }
                }
                catch { /* skip just this entry, keep parsing siblings */ }
            }
        }
        catch { /* malformed JSON — return whatever we managed to parse */ }

        return result;
    }

    /// <summary>
    /// Returns the "registration" event date from an RDAP domain response,
    /// or null if missing/unparseable. Format spec: RFC 7483 §4.5. The
    /// response carries an <c>events</c> array; we want the entry with
    /// <c>eventAction == "registration"</c>.
    /// </summary>
    public static DateTime? ParseRegistrationDate(string rdapJson)
    {
        if (string.IsNullOrWhiteSpace(rdapJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(rdapJson);
            if (!doc.RootElement.TryGetProperty("events", out var events)) return null;
            if (events.ValueKind != JsonValueKind.Array) return null;

            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("eventAction", out var action)) continue;
                if (!string.Equals(action.GetString(), "registration",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ev.TryGetProperty("eventDate", out var dateEl)) continue;
                var raw = dateEl.GetString();
                if (string.IsNullOrEmpty(raw)) continue;

                if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal |
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                    return parsed;
            }
        }
        catch { /* malformed JSON */ }

        return null;
    }

    // ── Default HTTP implementation ───────────────────────────────────────

    private static readonly HttpClient _defaultClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static async Task<string?> DefaultHttpGet(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _defaultClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
