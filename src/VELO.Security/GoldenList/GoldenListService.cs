using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VELO.Security.GoldenList;

/// <summary>
/// Loads golden_list.json from resources and answers IsGolden queries.
/// Subdomains are matched: "mail.proton.me" matches entry "proton.me".
/// </summary>
public class GoldenListService(ILogger<GoldenListService> logger) : IGoldenList
{
    private readonly ILogger<GoldenListService> _logger = logger;
    private HashSet<string> _domains = [];

    public int Count => _domains.Count;
    public DateTime LastUpdated { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// True if the blocklist hasn't refreshed in >7 days. Shield accuracy may
    /// be reduced; consumers should surface this to the user.
    /// </summary>
    public bool IsStale => LastUpdated != DateTime.MinValue &&
                           DateTime.UtcNow - LastUpdated > TimeSpan.FromDays(7);

    /// <summary>Human-readable age (e.g. "3 days ago"); empty if never updated.</summary>
    public string StalenessDescription
    {
        get
        {
            if (LastUpdated == DateTime.MinValue) return "";
            var age = DateTime.UtcNow - LastUpdated;
            if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d ago";
            if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h ago";
            return "recently";
        }
    }

    public async Task LoadAsync(string resourcesPath)
    {
        var path = Path.Combine(resourcesPath, "blocklists", "golden_list.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("golden_list.json not found at {Path}", path);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var doc  = JsonDocument.Parse(json);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("domains", out var arr))
                foreach (var el in arr.EnumerateArray())
                    if (el.GetString() is { } d)
                        set.Add(d.TrimStart('.').ToLowerInvariant());

            if (doc.RootElement.TryGetProperty("updated", out var upd) &&
                DateTime.TryParse(upd.GetString(), out var dt))
                LastUpdated = dt;

            _domains = set;
            _logger.LogInformation("GoldenList loaded {Count} domains (updated {Date})", set.Count, LastUpdated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load golden_list.json");
        }
    }

    public bool IsGolden(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var d = domain.ToLowerInvariant().TrimStart('.');

        // Exact match first, then subdomain walk
        if (_domains.Contains(d)) return true;

        // Walk up: "mail.proton.me" → "proton.me" → "me"
        var idx = d.IndexOf('.');
        while (idx >= 0 && idx < d.Length - 1)
        {
            var parent = d[(idx + 1)..];
            if (_domains.Contains(parent)) return true;
            idx = d.IndexOf('.', idx + 1);
        }

        return false;
    }

    /// <summary>
    /// Replace domain set from an already-parsed list (used by GoldenListUpdater).
    /// </summary>
    public void Replace(IEnumerable<string> domains, DateTime updatedAt)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in domains)
            set.Add(d.TrimStart('.').ToLowerInvariant());
        _domains    = set;
        LastUpdated = updatedAt;
        _logger.LogInformation("GoldenList replaced: {Count} domains (updated {Date})", set.Count, updatedAt);
    }
}
