using VELO.Data.Repositories;
using VELO.Data.Models;

namespace VELO.Core.Search;

public class SearchEngineService(SettingsRepository settings)
{
    private readonly SettingsRepository _settings = settings;

    private static readonly Dictionary<string, string> KnownEngines = new()
    {
        ["DuckDuckGo"]  = "https://duckduckgo.com/?q={query}",
        ["BraveSearch"] = "https://search.brave.com/search?q={query}",
        ["SearxNG"]     = "https://searx.be/search?q={query}",
    };

    public async Task<string> BuildSearchUrlAsync(string query)
    {
        var engine = await _settings.GetAsync(SettingKeys.SearchEngine, "DuckDuckGo");
        string template;

        if (engine == "Custom")
            template = await _settings.GetAsync(SettingKeys.SearchCustomUrl, KnownEngines["DuckDuckGo"]);
        else
            template = KnownEngines.GetValueOrDefault(engine, KnownEngines["DuckDuckGo"]);

        return template.Replace("{query}", Uri.EscapeDataString(query));
    }

    /// <summary>
    /// v2.4.64 — Schemes the omnibox passes through untouched. Before this, only
    /// http/https/velo were recognised, so anything else got "https://" glued to
    /// the front: typing a local file produced
    /// <c>https://file:///C:/…</c> and VELO simply could not open local files.
    /// <para>
    /// Deliberately excluded: <c>data:</c> and <c>javascript:</c> (typing those in
    /// an address bar is a classic self-XSS / phishing vector — Chromium blocks
    /// top-level navigation to them too) and the OS handoff schemes covered by
    /// AS-3's denylist.
    /// </para>
    /// </summary>
    private static readonly string[] PassthroughSchemes =
    [
        "http://", "https://", "velo://", "file://", "ftp://",
        "about:", "edge://", "chrome://", "view-source:",
    ];

    private static bool HasPassthroughScheme(string input)
        => PassthroughSchemes.Any(s => input.StartsWith(s, StringComparison.OrdinalIgnoreCase));

    public static bool IsSearchQuery(string input)
    {
        input = input.Trim();
        if (HasPassthroughScheme(input))
            return false;
        if (input.Contains('.') && !input.Contains(' '))
            return false;
        return true;
    }

    public async Task<string> ResolveInputAsync(string input)
    {
        input = input.Trim();

        if (IsSearchQuery(input))
            return await BuildSearchUrlAsync(input);

        if (!HasPassthroughScheme(input))
            return "https://" + input;

        return input;
    }
}
