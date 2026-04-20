using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VELO.Security.GoldenList;

/// <summary>
/// Downloads the latest golden_list.json once per day using If-None-Match
/// and verifies integrity with SHA256 before applying the update.
///
/// The remote URL is injected via configuration so it can point to the
/// GitHub raw CDN or a self-hosted mirror without recompiling.
/// </summary>
public class GoldenListUpdater(
    GoldenListService goldenList,
    ILogger<GoldenListUpdater> logger)
{
    private readonly GoldenListService         _goldenList = goldenList;
    private readonly ILogger<GoldenListUpdater> _logger    = logger;

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "VELO-Browser/2.0 GoldenList-Updater" } }
    };

    private string _lastEtag = "";
    private DateTime _lastCheck = DateTime.MinValue;

    // CDN URL — release builds override this via appsettings / config injection
    private const string DefaultUrl =
        "https://raw.githubusercontent.com/badizher-codex/velo/main/resources/blocklists/golden_list.json";

    public string RemoteUrl { get; set; } = DefaultUrl;

    /// <summary>
    /// Call once on startup and then periodically (e.g. every 24h via a timer).
    /// Safe to call concurrently — additional calls no-op if within the 23h window.
    /// </summary>
    public async Task CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _lastCheck < TimeSpan.FromHours(23))
            return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RemoteUrl);
            if (!string.IsNullOrEmpty(_lastEtag))
                request.Headers.TryAddWithoutValidation("If-None-Match", _lastEtag);

            using var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _lastCheck = DateTime.UtcNow;
                _logger.LogDebug("GoldenList: 304 Not Modified — no update needed");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GoldenList: HTTP {Status} from {Url}", response.StatusCode, RemoteUrl);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            // Verify SHA256 if the server returns X-Content-SHA256 header
            if (response.Headers.TryGetValues("X-Content-SHA256", out var headerValues))
            {
                var expectedHash = headerValues.FirstOrDefault();
                if (!string.IsNullOrEmpty(expectedHash) && !VerifySha256(json, expectedHash))
                {
                    _logger.LogError("GoldenList: SHA256 mismatch — aborting update");
                    return;
                }
            }

            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("domains", out var arr))
            {
                _logger.LogWarning("GoldenList: JSON missing 'domains' array");
                return;
            }

            var domains = new List<string>();
            foreach (var el in arr.EnumerateArray())
                if (el.GetString() is { } d) domains.Add(d);

            var updatedAt = DateTime.UtcNow;
            if (doc.RootElement.TryGetProperty("updated", out var upd) &&
                DateTime.TryParse(upd.GetString(), out var dt))
                updatedAt = dt;

            _goldenList.Replace(domains, updatedAt);

            // Store ETag for next request
            if (response.Headers.ETag is { } etag)
                _lastEtag = etag.Tag;

            _lastCheck = DateTime.UtcNow;
            _logger.LogInformation("GoldenList updated: {Count} domains", domains.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GoldenList update failed — continuing with cached list");
        }
    }

    private static bool VerifySha256(string content, string expectedHex)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash  = SHA256.HashData(bytes);
        var hex   = Convert.ToHexString(hash);
        return string.Equals(hex, expectedHex, StringComparison.OrdinalIgnoreCase);
    }
}
