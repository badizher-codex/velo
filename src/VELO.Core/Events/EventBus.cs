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

// ── Fase 3 events ────────────────────────────────────────────────────────────
/// <summary>
/// Fase 3 / Sprint 1 — Rich event published whenever a request is blocked
/// (RequestGuard, DownloadGuard, AISecurityEngine, GoldenList). Consumed by
/// ThreatsPanelV2 to render the live per-tab block list. Existing
/// SecurityVerdictEvent stays for back-compat callers; this one carries
/// enough context for grouping by host + AI explanation.
/// Source values mirror BlockSource enum: GoldenList | Malwaredex | AIEngine
/// | UserRule | StaticList | RequestGuard | DownloadGuard.
/// </summary>
public record BlockedRequestEvent(
    string TabId,
    string Host,
    string FullUrl,
    string Kind,        // "Tracker" | "Malware" | "Ads" | "Fingerprint" | "Script" | "Social" | "Other"
    string SubKind,     // "cross-site" | "fingerprint" | "pixel" | …
    string Source,
    bool   IsMalwaredexHit,
    int    Confidence,
    DateTime BlockedAtUtc) : IEvent;
