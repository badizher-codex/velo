using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VELO.UI.Utilities;

/// <summary>
/// Removes known tracking parameters from URLs without touching the path or fragment.
/// Parameter list is loaded from resources/url_cleaner_params.json.
/// </summary>
public class UrlCleaner(ILogger<UrlCleaner> logger)
{
    private readonly ILogger<UrlCleaner> _logger = logger;
    private HashSet<string> _trackingParams = [];

    public async Task LoadParamsAsync(string resourcesPath)
    {
        var jsonPath = Path.Combine(resourcesPath, "url_cleaner_params.json");
        if (!File.Exists(jsonPath))
        {
            _logger.LogWarning("url_cleaner_params.json not found at {Path}", jsonPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(jsonPath);
            var doc  = JsonDocument.Parse(json);
            var set  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var category in doc.RootElement.EnumerateObject())
                foreach (var param in category.Value.EnumerateArray())
                    if (param.GetString() is { } p)
                        set.Add(p);

            _trackingParams = set;
            _logger.LogInformation("UrlCleaner loaded {Count} tracking params", set.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load url_cleaner_params.json");
        }
    }

    /// <summary>
    /// Returns a clean version of the URL with all tracking query params removed.
    /// Path and fragment are never touched.
    /// </summary>
    public string Clean(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return rawUrl;

        if (string.IsNullOrEmpty(uri.Query))
            return rawUrl;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var keysToRemove = query.AllKeys
            .Where(k => k is not null && _trackingParams.Contains(k))
            .ToList();

        if (keysToRemove.Count == 0)
            return rawUrl;

        foreach (var key in keysToRemove)
            query.Remove(key);

        var newQuery = query.Count > 0 ? "?" + query : "";
        var fragment = uri.Fragment; // preserves #anchor

        var builder = new UriBuilder(uri)
        {
            Query    = newQuery.TrimStart('?'),
            Fragment = fragment.TrimStart('#'),
        };

        return builder.Uri.AbsoluteUri;
    }

    public bool IsLoaded => _trackingParams.Count > 0;
}
