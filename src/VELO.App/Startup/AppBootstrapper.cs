using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VELO.Core.Localization;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Security.AI;
using VELO.Security.AI.Adapters;
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

        // 2. Blocklists — try AppData cache first (fast), then bundled fallback
        var blocklist = _services.GetRequiredService<BlocklistManager>();
        await blocklist.LoadCachedAsync();
        if (blocklist.DomainCount == 0)
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            await blocklist.LoadBundledAsync(Path.Combine(appDir, "resources"));
        }

        // 3. Load saved language
        var savedLang = await _services.GetRequiredService<SettingsRepository>()
            .GetAsync(SettingKeys.Language, "es");
        LocalizationService.Current.SetLanguage(savedLang);

        // 4. Configure AI adapter from saved settings
        await ConfigureAIAdapterAsync();

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
            catch { /* Silencioso — blocklists opcionales */ }
        });

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

    public Task<bool> IsOnboardingCompletedAsync(SettingsRepository settings)
        => settings.GetBoolAsync(SettingKeys.OnboardingCompleted, false);
}
