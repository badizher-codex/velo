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

    public static bool IsSearchQuery(string input)
    {
        input = input.Trim();
        if (input.StartsWith("http://") || input.StartsWith("https://") || input.StartsWith("velo://"))
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

        if (!input.StartsWith("http://") && !input.StartsWith("https://") && !input.StartsWith("velo://"))
            return "https://" + input;

        return input;
    }
}
