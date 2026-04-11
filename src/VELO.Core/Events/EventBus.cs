namespace VELO.Core.Events;

public interface IEvent { }

public class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<T>(Action<T> handler) where T : IEvent
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list))
        {
            list = [];
            _handlers[type] = list;
        }
        list.Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler) where T : IEvent
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }

    public void Publish<T>(T @event) where T : IEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) return;
        foreach (var handler in list.ToList())
            ((Action<T>)handler)(@event);
    }
}

// Events
public record TabCreatedEvent(string TabId) : IEvent;
public record TabClosedEvent(string TabId) : IEvent;
public record TabActivatedEvent(string TabId) : IEvent;
public record NavigationStartedEvent(string TabId, string Url) : IEvent;
public record NavigationCompletedEvent(string TabId, string Url, string Title) : IEvent;
public record SecurityVerdictEvent(string TabId, string Domain, string Verdict, string? Reason) : IEvent;
public record BlocklistUpdateFailedEvent(DateTime? LastSuccessfulUpdate) : IEvent;
public record UpdateAvailableEvent(Version NewVersion, string? ReleaseNotes) : IEvent;
public record DownloadStartedEvent(string FileName, string Url) : IEvent;
