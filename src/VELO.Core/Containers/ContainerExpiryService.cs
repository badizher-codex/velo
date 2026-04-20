using Microsoft.Extensions.Logging;
using VELO.Core.Events;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Core.Containers;

/// <summary>
/// Polls every 30 seconds for containers whose ExpiresAt has passed
/// and triggers destruction: purge WebView2 data, delete from DB, fire event.
/// </summary>
public class ContainerExpiryService(
    ContainerRepository repository,
    EventBus eventBus,
    ILogger<ContainerExpiryService> logger)
{
    private readonly ContainerRepository             _repository = repository;
    private readonly EventBus                        _eventBus   = eventBus;
    private readonly ILogger<ContainerExpiryService> _logger     = logger;

    private System.Threading.Timer? _timer;

    // Injected by the host (MainWindow/App) to perform WebView2 data purge
    public Func<string, Task>? OnDestroyContainer { get; set; }

    public void Start()
    {
        _timer = new System.Threading.Timer(
            async _ => await CheckExpiryAsync(),
            null,
            dueTime: TimeSpan.FromSeconds(10),
            period:  TimeSpan.FromSeconds(30));

        _logger.LogDebug("ContainerExpiryService started");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task CheckExpiryAsync()
    {
        try
        {
            var all = await _repository.GetAllAsync();
            foreach (var c in all.Where(c => c.IsExpired))
                await DestroyAsync(c);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking container expiry");
        }
    }

    public async Task DestroyAsync(Container container)
    {
        _logger.LogInformation("Destroying expired container '{Name}' ({Id})", container.Name, container.Id);

        // 1. Purge WebView2 user data (cookies, cache, storage)
        if (OnDestroyContainer is { } purge)
        {
            try { await purge(container.Id); }
            catch (Exception ex) { _logger.LogWarning(ex, "WebView2 purge failed for container {Id}", container.Id); }
        }

        // 2. Remove from DB
        try { await _repository.DeleteAsync(container.Id); }
        catch (Exception ex) { _logger.LogWarning(ex, "DB delete failed for container {Id}", container.Id); }

        // 3. Notify the rest of the app
        _eventBus.Publish(new ContainerDestroyedEvent(container.Id, container.Name));
    }

    /// <summary>
    /// Creates a temporary container with the given TTL, saves it, and returns it.
    /// </summary>
    public async Task<Container> CreateTemporaryAsync(string name, string color, TimeSpan ttl)
    {
        var c = Container.Temporary(name, color, ttl);
        await _repository.SaveAsync(c);
        _logger.LogInformation("Temporary container '{Name}' created, expires in {TTL}", name, ttl);
        return c;
    }
}
