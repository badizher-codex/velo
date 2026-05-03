using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VELO.Agent;
using VELO.Agent.Adapters;
using VELO.Core.Localization;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Security.AI;
using VELO.Security.AI.Adapters;
using VELO.Security.GoldenList;
using VELO.Security.Rules;
using VELO.Security.AI.Models;

namespace VELO.App.Startup;

public class AppBootstrapper(IServiceProvider services)
{
    private readonly IServiceProvider _services = services;
    private ILogger<AppBootstrapper>? _logger;

    public async Task InitializeAsync()
    {
        _logger = _services.GetRequiredService<ILogger<AppBootstrapper>>();
        _logger.LogInformation("VELO starting up");

        // 1. Database
        var db = _services.GetRequiredService<VeloDatabase>();
        await db.InitializeAsync();

        // 1b (v2.0.5.1). One-shot heal pass on the Malwaredex table — older
        // builds could insert duplicate (ThreatType, SubType) rows under load,
        // and the Malwaredex window threw an ArgumentException on those.
        // Collapses duplicates into a single row, summing TotalSeen.
        try
        {
            var malwaredex = _services.GetRequiredService<MalwaredexRepository>();
            var removed = await malwaredex.DeduplicateAsync();
            if (removed > 0)
                _logger.LogInformation("Malwaredex: deduped {Count} legacy duplicate row(s)", removed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malwaredex dedupe pass failed — non-fatal");
        }

        // 2. Blocklists — try AppData cache first (fast), then bundled fallback
        var blocklist = _services.GetRequiredService<BlocklistManager>();
        await blocklist.LoadCachedAsync();
        if (blocklist.DomainCount == 0)
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            await blocklist.LoadBundledAsync(Path.Combine(appDir, "resources"));
        }

        // 2c (v2.0.5.12). Restore the persistent security whitelist so
        // "Whitelist always" survives across VELO restarts. Both guards
        // re-populate from the same source-of-truth list in settings.
        try
        {
            var saved = await _services.GetRequiredService<SettingsRepository>()
                .GetAsync(SettingKeys.SecurityWhitelist, "");
            if (!string.IsNullOrWhiteSpace(saved))
            {
                foreach (var host in saved.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var h = host.Trim();
                    if (h.Length == 0) continue;
                    VELO.Security.Guards.RequestGuard.AddToWhitelist(h);
                    VELO.Security.Guards.DownloadGuard.Whitelist(h);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restore security whitelist failed — non-fatal");
        }

        // 3. Load saved language
        var savedLang = await _services.GetRequiredService<SettingsRepository>()
            .GetAsync(SettingKeys.Language, "es");
        LocalizationService.Current.SetLanguage(savedLang);

        // 4. Configure AI adapter from saved settings
        await ConfigureAIAdapterAsync();

        // 5. Configure VeloAgent adapters from saved settings
        await ConfigureAgentAdaptersAsync();

        // 4. Update blocklists in background (never blocks startup)
        var settings = _services.GetRequiredService<SettingsRepository>();
        var lastUpdate = await settings.GetAsync(SettingKeys.BlocklistsLastUpdate);
        DateTime? lastUpdateDate = lastUpdate != null && DateTime.TryParse(lastUpdate, out var dt) ? dt : null;

        // Persist the update date so we don't re-download every startup
        blocklist.UpdateSucceeded += date =>
            _ = Task.Run(() => settings.SetAsync(SettingKeys.BlocklistsLastUpdate, date.ToString("O")));

        _ = Task.Run(async () =>
        {
            try
            {
                if (lastUpdateDate == null || DateTime.UtcNow - lastUpdateDate > TimeSpan.FromDays(7))
                    await blocklist.UpdateAsync(lastUpdateDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blocklist update failed on startup — continuing with cached list");
            }
        });

        // Warn if the GoldenList is older than 7 days — this means the remote CDN
        // update has been failing silently and Shield Score accuracy is reduced.
        var goldenList = _services.GetRequiredService<GoldenListService>();
        if (goldenList.IsStale)
        {
            _logger.LogWarning(
                "GoldenList is stale ({Age}). Shield accuracy may be reduced. Check network / remote CDN.",
                goldenList.StalenessDescription);
        }

        _logger.LogInformation("Bootstrapping complete");
    }

    public async Task ConfigureAIAdapterAsync()
    {
        var settings = _services.GetRequiredService<SettingsRepository>();
        var engine   = _services.GetRequiredService<AISecurityEngine>();
        var aiMode   = await settings.GetAsync(SettingKeys.AiMode, "Offline");
        var logger   = _services.GetRequiredService<ILogger<OllamaAdapter>>();

        IAIAdapter adapter = aiMode switch
        {
            // "Custom" = LLM local via Ollama (OpenAI-compatible endpoint)
            "Ollama" or "Custom" => BuildOllamaAdapter(
                                           await settings.GetAsync(SettingKeys.AiCustomEndpoint, "http://localhost:11434"),
                                           await settings.GetAsync(SettingKeys.AiClaudeModel, "llama3"),
                                           logger),
            "Claude" => BuildClaudeAdapter(await settings.GetAsync(SettingKeys.AiApiKey, ""),
                                            await settings.GetAsync(SettingKeys.AiClaudeModel, "claude-haiku-4-5-20251001"),
                                            _services.GetRequiredService<ILogger<ClaudeAdapter>>()),
            _        => new OfflineAdapter()
        };

        engine.SetAdapter(adapter);
        _logger?.LogInformation("AI adapter: {Mode}", adapter.ModeName);
    }

    private static OllamaAdapter BuildOllamaAdapter(string endpoint, string model, ILogger<OllamaAdapter> logger)
        => new(endpoint, model, logger);

    private static IAIAdapter BuildClaudeAdapter(string apiKey, string model, ILogger<ClaudeAdapter> logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new OfflineAdapter();
        return new ClaudeAdapter(apiKey, model, logger);
    }

    /// <summary>
    /// Rebuilds the VeloAgent adapter list from current settings and pushes it to AgentLauncher.
    /// Call this on startup and whenever the user saves AI settings.
    /// </summary>
    public async Task ConfigureAgentAdaptersAsync()
    {
        var settings = _services.GetRequiredService<SettingsRepository>();
        var launcher = _services.GetRequiredService<AgentLauncher>();
        var adapters = new List<IAgentAdapter>();

        // LLamaSharp — available when a GGUF model file is present on disk
        adapters.Add(_services.GetRequiredService<LLamaSharpAdapter>());

        // Ollama / LM Studio — available when AI mode is Custom/Ollama and a model is configured
        var aiMode = await settings.GetAsync(SettingKeys.AiMode, "Offline");
        if (aiMode is "Ollama" or "Custom")
        {
            var endpoint = await settings.GetAsync(SettingKeys.AiCustomEndpoint, "http://localhost:11434");
            var model    = await settings.GetAsync(SettingKeys.AiClaudeModel, "");
            if (!string.IsNullOrWhiteSpace(model))
            {
                var ollamaLogger = _services.GetRequiredService<ILogger<OllamaAgentAdapter>>();
                adapters.Add(new OllamaAgentAdapter(endpoint, model, ollamaLogger));
                _logger?.LogInformation("VeloAgent: OllamaAgentAdapter configured → {Endpoint} / {Model}",
                    endpoint, model);
            }
        }

        launcher.UpdateAdapters(adapters);
    }

    public Task<bool> IsOnboardingCompletedAsync(SettingsRepository settings)
        => settings.GetBoolAsync(SettingKeys.OnboardingCompleted, false);
}
