using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VELO.Core.Downloads;
using VELO.Core.Events;
using VELO.Core.Navigation;
using VELO.Core.Search;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.DNS;
using VELO.DNS.Providers;
using VELO.Security.AI;
using VELO.Security.AI.Adapters;
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

        // Logging — Serilog to file only, never remote
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VELO");
        Directory.CreateDirectory(appDataPath);

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

        // Data
        services.AddSingleton<VeloDatabase>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<HistoryRepository>();
        services.AddSingleton<BookmarkRepository>();
        services.AddSingleton<PasswordRepository>();
        services.AddSingleton<SecurityCacheRepository>();
        services.AddSingleton<ContainerRepository>();
        services.AddSingleton<MalwaredexRepository>();

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

        // DNS
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IDoHProvider, Quad9Provider>();
        services.AddSingleton<DoHResolver>();

        // Vault
        services.AddSingleton<VaultService>();

        return services.BuildServiceProvider();
    }
}
