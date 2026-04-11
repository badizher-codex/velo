using Microsoft.Extensions.Logging;
using VELO.Core.Events;
using VELO.Core.Search;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Core.Navigation;

public class NavigationController(
    TabManager tabManager,
    SearchEngineService searchEngine,
    HistoryRepository historyRepo,
    SettingsRepository settings,
    EventBus eventBus,
    ILogger<NavigationController> logger)
{
    private readonly TabManager _tabManager = tabManager;
    private readonly SearchEngineService _searchEngine = searchEngine;
    private readonly HistoryRepository _historyRepo = historyRepo;
    private readonly SettingsRepository _settings = settings;
    private readonly EventBus _eventBus = eventBus;
    private readonly ILogger<NavigationController> _logger = logger;

    public async Task<string> ResolveUrlAsync(string input)
        => await _searchEngine.ResolveInputAsync(input);

    public async Task RecordNavigationAsync(string tabId, string url, string title,
        int blocked = 0, int trackers = 0, int malware = 0, bool monsterCaptured = false)
    {
        _tabManager.UpdateTab(tabId, t =>
        {
            t.Url = url;
            t.Title = title;
            t.IsLoading = false;
        });

        _eventBus.Publish(new NavigationCompletedEvent(tabId, url, title));

        var securityMode = await _settings.GetAsync(SettingKeys.SecurityMode, "Normal");
        var historyEnabled = await _settings.GetBoolAsync(SettingKeys.HistoryEnabled, true);

        if (historyEnabled && securityMode != "Bunker")
        {
            var tab = _tabManager.GetTab(tabId);
            await _historyRepo.SaveAsync(new HistoryEntry
            {
                Url = url,
                Title = title,
                ContainerId = tab?.ContainerId,
                VisitedAt = DateTime.UtcNow,
                BlockedCount = blocked,
                TrackerCount = trackers,
                MalwareCount = malware,
                MonsterCaptured = monsterCaptured
            });
        }

        _logger.LogDebug("Navigation recorded: {Url}", url);
    }

    public async Task ClearDataOnExitAsync()
    {
        var securityMode = await _settings.GetAsync(SettingKeys.SecurityMode, "Normal");
        if (securityMode == "Paranoid" || securityMode == "Bunker")
        {
            await _historyRepo.ClearAllAsync();
            _logger.LogInformation("History cleared on exit (mode: {Mode})", securityMode);
        }
    }
}
