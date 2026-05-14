using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VELO.Agent;
using VELO.Agent.Adapters;
using VELO.Core;
using VELO.Core.Containers;
using VELO.Core.Downloads;
using VELO.Core.Events;
using VELO.Core.Navigation;
using VELO.Core.Search;
using VELO.Core.Sessions;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.DNS;
using VELO.DNS.Providers;
using VELO.Security;
using VELO.Security.AI;
using VELO.Security.AI.Adapters;
using VELO.Security.GoldenList;
using VELO.Security.Guards;
using VELO.Security.CookieWall;
using VELO.Security.Rules;
using VELO.Vault;

namespace VELO.App.Startup;

public static class DependencyConfig
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Resolve user-data root (supports portable mode via DataLocation)
        var appDataPath = DataLocation.GetUserDataPath();

        // Logging — Serilog to file only, never remote
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "velo-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .MinimumLevel.Information()
            .CreateLogger();

        services.AddLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSerilog(Log.Logger);
        });

        // Data — pass portable-aware data folder to DB constructor
        services.AddSingleton<VeloDatabase>(sp =>
            new VeloDatabase(
                sp.GetRequiredService<ILogger<VeloDatabase>>(),
                appDataPath));
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<HistoryRepository>();
        services.AddSingleton<BookmarkRepository>();
        services.AddSingleton<PasswordRepository>();
        services.AddSingleton<SecurityCacheRepository>();
        services.AddSingleton<ContainerRepository>();
        services.AddSingleton<MalwaredexRepository>();
        services.AddSingleton<WorkspaceRepository>();

        // Bootstrapper (singleton so MainWindow can call ConfigureAIAdapterAsync after settings change)
        services.AddSingleton<AppBootstrapper>();

        // Core
        services.AddSingleton<EventBus>();
        services.AddSingleton<TabManager>();
        services.AddSingleton<SearchEngineService>();
        services.AddSingleton<NavigationController>();
        services.AddSingleton<DownloadManager>();

        // Security
        services.AddSingleton<BlocklistManager>();
        services.AddSingleton<TLSGuard>();
        services.AddSingleton<DownloadGuard>();
        services.AddSingleton<RequestGuard>();
        services.AddSingleton<SecurityCache>();
        services.AddSingleton<LocalRuleEngine>();

        services.AddSingleton<CookieWallBypassEngine>();

        // AI adapter — resolved at runtime based on settings
        services.AddSingleton<IAIAdapter, OfflineAdapter>();  // default
        services.AddSingleton<AISecurityEngine>();

        // Phase 3 / Sprint 1 — Threats Panel v3 stack.
        services.AddSingleton<VELO.Security.Threats.BlockExplanationService>();
        services.AddSingleton<VELO.Security.Threats.ThreatsPanelViewModel>();

        // Phase 3 / Sprint 3 — Session restore service.
        services.AddSingleton<VELO.Core.Sessions.SessionService>();

        // Phase 3 / Sprint 4 — Browser import (Chrome/Edge/Firefox).
        services.AddSingleton<VELO.Import.BrowserImportService>();

        // Phase 3 / Sprint 1E — Context Menu IA stack. AIContextMenuBuilder
        // composes ContextMenuBuilder (Phase 2 menu) so we register the inner
        // builder too. Phase 2 had ContextMenuBuilder declared but unwired —
        // Sprint 1E activates it through the AI variant.
        services.AddSingleton<VELO.UI.Utilities.UrlCleaner>();
        services.AddSingleton<VELO.UI.Controls.ContextMenuBuilder>();
        services.AddSingleton<VELO.Agent.AIContextActions>();
        services.AddSingleton<VELO.UI.Controls.AIContextMenuBuilder>();

        // DNS
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IDoHProvider, Quad9Provider>();
        services.AddSingleton<DoHResolver>();

        // Vault
        services.AddSingleton<VaultService>();

        // Phase 3 / Sprint 5 — Password autofill + HIBP breach check
        services.AddSingleton<VELO.Vault.Security.HibpClient>();
        services.AddSingleton<VELO.Vault.AutofillService>();

        // Phase 3 / Sprint 6 — VeloAgent v2 contextual: slash commands +
        // per-tab page priming.
        services.AddSingleton<VELO.Agent.SlashCommandRouter>();
        services.AddSingleton<VELO.Agent.PageContextManager>();

        // Phase 3 / Sprint 8 — AI Privacy Shield:
        //   • SmartBlockClassifier — second-pass tracker classification
        //     for sub-resources missed by the static blocklist.
        //   • PhishingShield — local-LLM phishing/impersonation analysis
        //     using URL + TLS + login-form signals.
        //   • BlockNarrationService — proactive human-readable explanations
        //     for "interesting" blocks (off by default; toggled by setting).
        // ChatDelegate wiring happens in MainWindow once the AI adapter is
        // resolved (same pattern as BlockExplanationService's ChatDelegate).
        services.AddSingleton<VELO.Security.Guards.SmartBlockClassifier>(sp =>
        {
            // v2.4.42 — bump cache TTL 6h → 24h. Trackers rarely change host within
            // a single day, and the local model fan-out from v2.4.41 was too costly
            // to re-classify every 6h. Combined with the DirectChatAdapter semaphore
            // this keeps the local LLM idle most of the time.
            var c = new VELO.Security.Guards.SmartBlockClassifier(
                sp.GetService<Microsoft.Extensions.Logging.ILogger<VELO.Security.Guards.SmartBlockClassifier>>());
            c.CacheTtl = TimeSpan.FromHours(24);
            return c;
        });
        services.AddSingleton<VELO.Security.Guards.PhishingShield>();
        services.AddSingleton<VELO.Security.Threats.BlockNarrationService>();
        // v2.4.42 — DirectChatAdapter: stateless one-shot OpenAI-compat for internal
        // AI services. Replaces the WireAgentChat path that piled every classifier
        // call onto AgentLauncher's shared "__ai__" history bucket.
        services.AddSingleton<VELO.Core.AI.DirectChatAdapter>();
        // v2.4.25 — IANA RDAP probe for domain-age PhishingShield signal.
        // Disabled by default (privacy). MainWindow flips Enabled based on
        // the user setting at startup and after Save in SettingsWindow.
        services.AddSingleton<VELO.Security.Guards.DomainAgeProbe>();

        // Phase 3 / Sprint 9 — AI Productivity Pack:
        //   • CodeActions      — right-click code-block actions (Explain,
        //     Translate, Debug, Optimize, Comment, AddErrorHandling)
        //   • BookmarkAIService — auto-tag on save + semantic rerank on search
        //   • TldrService       — eligibility check + on-demand TL;DR
        // ChatDelegates wired in MainWindow once the AI adapter resolves.
        services.AddSingleton<VELO.Agent.CodeActions>();
        services.AddSingleton<VELO.Agent.BookmarkAIService>();
        services.AddSingleton<VELO.Agent.TldrService>();

        // v2.4.23 — Clipboard history (Sprint 9D follow-up to v2.4.16 paste).
        // In-memory ring buffer; the polling timer that feeds it lives in
        // MainWindow and only ticks while the user has the feature enabled.
        services.AddSingleton<VELO.Core.Clipboard.ClipboardHistory>();

        // v2.1.5.1 — Per-site shields allowlist (relax fingerprint/WebRTC on
        // anti-bot login endpoints so users can sign in to homedepot, banks,
        // etc. without false "wrong password" rejections).
        services.AddSingleton<VELO.Security.Guards.ShieldsAllowlist>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var allow = new VELO.Security.Guards.ShieldsAllowlist(
                loggerFactory.CreateLogger<VELO.Security.Guards.ShieldsAllowlist>());
            allow.Load(userDataPath: appDataPath);
            return allow;
        });

        // Containers (Fase 2)
        services.AddSingleton<ContainerExpiryService>();

        // Privacy Receipt (Fase 2)
        services.AddSingleton<PrivacyReceiptService>();

        // GoldenList (Fase 2)
        services.AddSingleton<GoldenListService>();
        services.AddSingleton<IGoldenList>(sp => sp.GetRequiredService<GoldenListService>());
        services.AddSingleton<GoldenListUpdater>();

        // Safety Scorer — Shield Score engine (Fase 2)
        services.AddSingleton<SafetyScorer>();

        // Auto-updater (Sprint 7)
        services.AddSingleton<UpdateChecker>();

        // PasteGuard (Fase 2 / Sprint 3)
        services.AddSingleton<PasteGuard>();

        // Agent (Fase 2 / Sprint 4)
        services.AddSingleton<AgentActionSandbox>();
        services.AddSingleton<AgentActionExecutor>();

        // Agent adapters — registered in priority order (LLamaSharp → Ollama → none)
        // LLamaSharp is registered but not loaded until a model path is configured
        services.AddSingleton<LLamaSharpAdapter>(sp =>
        {
            var modelPath = Path.Combine(appDataPath, "models", "agent.gguf");
            return new LLamaSharpAdapter(modelPath,
                sp.GetRequiredService<ILogger<LLamaSharpAdapter>>());
        });
        services.AddSingleton<IEnumerable<IAgentAdapter>>(sp =>
        {
            var adapters = new List<IAgentAdapter>
            {
                sp.GetRequiredService<LLamaSharpAdapter>(),
            };
            return adapters;
        });
        services.AddSingleton<AgentLauncher>();

        return services.BuildServiceProvider();
    }
}
