using Microsoft.Extensions.Logging;
using VELO.Agent.Models;
using VELO.Core.Events;

namespace VELO.Agent;

/// <summary>
/// Holds proposed agent actions pending user approval.
/// Each action must be explicitly approved or rejected before execution.
///
/// The Chat Panel UI listens to ActionProposed and renders approve/reject buttons.
/// Action executors (TabManager, WebView2) listen to ActionApproved.
/// </summary>
public class AgentActionSandbox(EventBus eventBus, ILogger<AgentActionSandbox> logger)
{
    private readonly EventBus                    _eventBus = eventBus;
    private readonly ILogger<AgentActionSandbox> _logger   = logger;

    // Pending actions: actionId → (tabId, action)
    private readonly Dictionary<string, (string TabId, AgentAction Action)> _pending = new();

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired when a new action is queued for the user to review.</summary>
    public event Action<string, AgentAction>? ActionProposed;   // (tabId, action)

    /// <summary>Fired when the user approves an action — executor should run it.</summary>
    public event Action<string, AgentAction>? ActionApproved;   // (tabId, action)

    /// <summary>Fired when the user rejects an action.</summary>
    public event Action<string, AgentAction>? ActionRejected;   // (tabId, action)

    // ── API ──────────────────────────────────────────────────────────────────

    public void Propose(string tabId, AgentAction action)
    {
        _pending[action.Id] = (tabId, action);
        _logger.LogDebug("Sandbox: proposed action [{Id}] {Type} for tab {TabId}",
            action.Id, action.Type, tabId);
        ActionProposed?.Invoke(tabId, action);
    }

    public void Approve(string actionId)
    {
        if (!_pending.Remove(actionId, out var entry)) return;
        var (tabId, action) = entry;

        _logger.LogInformation("Sandbox: user approved [{Id}] {Type}", actionId, action.Type);
        _eventBus.Publish(new AgentActionExecutedEvent(action.Type.ToString(), Success: true));
        ActionApproved?.Invoke(tabId, action);
    }

    public void Reject(string actionId)
    {
        if (!_pending.Remove(actionId, out var entry)) return;
        var (tabId, action) = entry;

        _logger.LogInformation("Sandbox: user rejected [{Id}] {Type}", actionId, action.Type);
        _eventBus.Publish(new AgentActionExecutedEvent(action.Type.ToString(), Success: false));
        ActionRejected?.Invoke(tabId, action);
    }

    /// <summary>Reject all pending actions for a tab (e.g. on tab close).</summary>
    public void RejectAll(string tabId)
    {
        var ids = _pending
            .Where(kv => kv.Value.TabId == tabId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in ids) Reject(id);
    }

    public IReadOnlyList<(string TabId, AgentAction Action)> PendingFor(string tabId)
        => _pending.Values.Where(e => e.TabId == tabId).ToList().AsReadOnly();
}
