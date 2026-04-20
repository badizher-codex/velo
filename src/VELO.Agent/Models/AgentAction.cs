namespace VELO.Agent.Models;

/// <summary>
/// Represents a single action proposed by the agent.
/// Immutable after creation — the sandbox presents it to the user as-is.
/// </summary>
public record AgentAction(
    AgentActionType Type,
    string          Description,    // Human-readable, shown in the approval UI
    string?         Url      = null,
    string?         Selector = null,
    string?         Value    = null,
    string?         Text     = null)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    public override string ToString() => $"[{Type}] {Description}";
}
