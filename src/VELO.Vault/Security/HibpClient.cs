using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Vault.Security;

/// <summary>
/// Phase 3 / Sprint 5 — Have-I-Been-Pwned passwords client using k-anonymity.
///
/// SHA1 the password locally, send only the first 5 hex chars of the hash to
/// <c>https://api.pwnedpasswords.com/range/{prefix}</c>. The API responds with
/// every (suffix, count) tuple whose hash starts with that prefix. We compare
/// the suffix against ours locally and return the count.
/// The plaintext password never leaves the device. Even the full hash never
/// leaves the device.
///
/// Spec reference: § 5.6, "k-anonymity".
/// </summary>
public sealed class HibpClient
{
    private const string ApiBase = "https://api.pwnedpasswords.com/range/";
    private const int    PrefixLen = 5;

    private readonly HttpClient _http;
    private readonly ILogger<HibpClient> _logger;

    public HibpClient(HttpClient? http = null, ILogger<HibpClient>? logger = null)
    {
        _http = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
            DefaultRequestHeaders =
            {
                // HIBP requires a non-empty UA.
                { "User-Agent",       "VELO-Browser-HIBP-Client" },
                { "Add-Padding",      "true" },     // randomise response size to prevent traffic-analysis fingerprinting
            }
        };
        _logger = logger ?? NullLogger<HibpClient>.Instance;
    }

    /// <summary>
    /// Returns the number of times this exact password appears in HIBP's
    /// breach corpus. 0 means not seen. Throws on network/parse failure —
    /// callers are expected to treat that as "unknown" rather than "safe".
    /// </summary>
    public async Task<int> GetBreachCountAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        var (prefix, suffix) = Sha1PrefixSuffix(password);
        var resp = await _http.GetStringAsync(ApiBase + prefix, ct).ConfigureAwait(false);
        return ParseRangeResponse(resp, suffix);
    }

    // ── Pure helpers (unit-testable) ──────────────────────────────────

    /// <summary>SHA1 the input and split into (5-char prefix, 35-char suffix), all uppercase hex.</summary>
    public static (string Prefix, string Suffix) Sha1PrefixSuffix(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
#pragma warning disable CA5350 // SHA-1 is the protocol HIBP requires; not used for security here.
        var hash  = SHA1.HashData(bytes);
#pragma warning restore CA5350
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("X2"));
        var hex = sb.ToString();
        return (hex[..PrefixLen], hex[PrefixLen..]);
    }

    /// <summary>
    /// Parses the HIBP range API response (line-separated <c>SUFFIX:COUNT</c>)
    /// and returns the count for <paramref name="ourSuffix"/>, or 0 if absent.
    /// Comparison is case-insensitive — HIBP returns uppercase but be safe.
    /// </summary>
    public static int ParseRangeResponse(string responseBody, string ourSuffix)
    {
        if (string.IsNullOrEmpty(responseBody)) return 0;

        // Walk lines without splitting allocations.
        int start = 0;
        while (start < responseBody.Length)
        {
            int end = responseBody.IndexOf('\n', start);
            if (end < 0) end = responseBody.Length;

            var line = responseBody.AsSpan(start, end - start).TrimEnd('\r').TrimEnd();
            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                var suffix = line[..colon];
                if (suffix.Equals(ourSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var countStr = line[(colon + 1)..].Trim();
                    if (int.TryParse(countStr, out var count)) return count;
                }
            }
            start = end + 1;
        }
        return 0;
    }
}

/// <summary>Outcome of a HIBP check, returned by <see cref="AutofillService.CheckBreachAsync"/>.</summary>
public sealed record BreachStatus(int Count, bool Pwned)
{
    public static BreachStatus Clean => new(0, false);
}
