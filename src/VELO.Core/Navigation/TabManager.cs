using Microsoft.Extensions.Logging;
using VELO.Core.Events;

namespace VELO.Core.Navigation;

public class TabManager(EventBus eventBus, ILogger<TabManager> logger)
{
    private readonly EventBus _eventBus = eventBus;
    private readonly ILogger<TabManager> _logger = logger;
    private readonly List<TabInfo> _tabs = [];
    private string? _activeTabId;

    public IReadOnlyList<TabInfo> Tabs => _tabs.AsReadOnly();

    public TabInfo? ActiveTab => _tabs.FirstOrDefault(t => t.Id == _activeTabId);

    public TabInfo CreateTab(string url = "velo://newtab", string containerId = "none")
    {
        var tab = new TabInfo { Url = url, ContainerId = containerId };
        _tabs.Add(tab);
        _logger.LogDebug("Tab created: {TabId}", tab.Id);
        _eventBus.Publish(new TabCreatedEvent(tab.Id));
        ActivateTab(tab.Id);
        return tab;
    }

    public void CloseTab(string tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _logger.LogDebug("Tab closed: {TabId}", tabId);
        _eventBus.Publish(new TabClosedEvent(tabId));

        if (_activeTabId == tabId)
        {
            if (_tabs.Count == 0)
                CreateTab();
            else
            {
                var nextIndex = Math.Max(0, Math.Min(index, _tabs.Count - 1));
                ActivateTab(_tabs[nextIndex].Id);
            }
        }
    }

    public void ActivateTab(string tabId)
    {
        if (!_tabs.Any(t => t.Id == tabId)) return;
        _activeTabId = tabId;
        _eventBus.Publish(new TabActivatedEvent(tabId));
    }

    public TabInfo? GetTab(string tabId)
        => _tabs.FirstOrDefault(t => t.Id == tabId);

    public void UpdateTab(string tabId, Action<TabInfo> update)
    {
        var tab = GetTab(tabId);
        if (tab != null) update(tab);
    }
}
