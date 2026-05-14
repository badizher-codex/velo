using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk D — loads the bundled per-provider <see cref="CouncilAdapter"/>
/// JSON files from <c>resources/council/adapters/</c> on construction and
/// exposes a typed lookup by <see cref="CouncilProvider"/>. The registry is
/// a singleton: every Council panel resolves its adapter from the same
/// instance.
///
/// Bundled-only in v0.1 (Phase 4.1). Phase 4.4 spec covers a remote refresh
/// mechanism (clone of <c>GoldenListUpdater</c>) so users can pull updated
/// selectors when a provider's DOM drifts without waiting for a VELO release.
/// Phase 4.1 ships with the selectors hand-curated on 2026-05-14.
///
/// Fail-soft: malformed JSON, missing files, or invalid shapes are logged
/// and the entry is left out of the registry; <see cref="TryGet"/> returns
/// null for that provider so callers degrade gracefully (panel renders an
/// "adapter unavailable" placeholder instead of crashing).
/// </summary>
public sealed class CouncilAdaptersRegistry
{
    /// <summary>Path under the VELO install root holding the bundled adapter JSONs.</summary>
    public const string DefaultRelativePath = @"resources\council\adapters";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<CouncilProvider, CouncilAdapter> _adapters = new();
    private readonly ILogger<CouncilAdaptersRegistry> _logger;

    public CouncilAdaptersRegistry(
        string? overrideFolder = null,
        ILogger<CouncilAdaptersRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<CouncilAdaptersRegistry>.Instance;

        var folder = overrideFolder ?? Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
        Load(folder);
    }

    /// <summary>All providers that have a valid adapter loaded.</summary>
    public IReadOnlyCollection<CouncilProvider> Available => _adapters.Keys;

    /// <summary>Returns the adapter for <paramref name="provider"/> or null
    /// when no valid adapter was loaded (file missing, malformed, schema
    /// invalid).</summary>
    public CouncilAdapter? TryGet(CouncilProvider provider)
        => _adapters.TryGetValue(provider, out var a) ? a : null;

    /// <summary>Returns the adapter as a JSON string suitable for handing to
    /// <c>__veloCouncil.setAdapter(json)</c> via ExecuteScriptAsync. Null
    /// when no adapter is loaded for the provider.</summary>
    public string? GetAdapterJson(CouncilProvider provider)
    {
        var adapter = TryGet(provider);
        return adapter is null ? null : JsonSerializer.Serialize(adapter, JsonOpts);
    }

    /// <summary>Test helper — replaces an in-memory adapter without touching
    /// the filesystem. Lets unit tests exercise the registry surface without
    /// staging temp folders for every assertion.</summary>
    internal void SetForTest(CouncilProvider provider, CouncilAdapter adapter)
        => _adapters[provider] = adapter;

    /// <summary>Test helper — clears all loaded adapters.</summary>
    internal void ClearForTest() => _adapters.Clear();

    private void Load(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _logger.LogWarning(
                "Council adapter folder {Folder} does not exist; registry is empty.",
                folder);
            return;
        }

        // Filename → provider mapping. Matches the JSON files in
        // resources/council/adapters/ verbatim.
        var fileMap = new (string FileName, CouncilProvider Provider)[]
        {
            ("claude.json",  CouncilProvider.Claude),
            ("chatgpt.json", CouncilProvider.ChatGpt),
            ("grok.json",    CouncilProvider.Grok),
            ("local.json",   CouncilProvider.Local),
        };

        foreach (var (fileName, provider) in fileMap)
        {
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
            {
                _logger.LogWarning(
                    "Council adapter file {Path} missing; provider {Provider} will be unavailable.",
                    path, provider);
                continue;
            }

            try
            {
                var json    = File.ReadAllText(path);
                var adapter = JsonSerializer.Deserialize<CouncilAdapter>(json, JsonOpts);
                if (adapter is null || !adapter.IsValid)
                {
                    _logger.LogWarning(
                        "Council adapter at {Path} parsed but is invalid (missing required fields); skipped.",
                        path);
                    continue;
                }
                _adapters[provider] = adapter;
                _logger.LogInformation(
                    "Council adapter loaded: {Provider} = {AdapterName} {Version}",
                    provider, adapter.Name, adapter.Version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to load Council adapter from {Path}; provider {Provider} will be unavailable.",
                    path, provider);
            }
        }
    }
}
