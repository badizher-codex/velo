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

// ── Fase 2 events ────────────────────────────────────────────────────────────
public record FingerprintBlockedEvent(string TabId, string Domain, string Technique) : IEvent;
public record ShieldLevelChangedEvent(string TabId, string OldLevel, string NewLevel, int NumericScore) : IEvent;
public record ContainerDestroyedEvent(string ContainerId, string Name) : IEvent;
public record WorkspaceChangedEvent(string OldWorkspaceId, string NewWorkspaceId) : IEvent;
public record AgentActionProposedEvent(string ActionType, string ActionJson) : IEvent;
public record AgentActionExecutedEvent(string ActionType, bool Success) : IEvent;
public record PasteGuardTriggeredEvent(string TabId, string Domain, string SignalType) : IEvent;
public record PrivacyReceiptReadyEvent(string TabId, string Domain, int TrackersBlocked,
    int ScriptsAnalyzed, int ScriptsWarn, int ScriptsBlock, int CookiesRejected,
    int FingerprintAttempts, long BytesNotDownloaded, string FinalShieldLevel) : IEvent;
public record GlanceOpenedEvent(string Uri) : IEvent;
public record GlanceClosedEvent(string Uri) : IEvent;
