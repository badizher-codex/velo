namespace VELO.Agent.Models;

/// <summary>
/// The full response from the agent for a single user turn:
/// a text reply (always present) plus zero or more proposed actions.
/// </summary>
public record AgentResponse(
    string                   ReplyText,
    IReadOnlyList<AgentAction> Actions)
{
    public bool HasActions => Actions.Count > 0;

    public static AgentResponse TextOnly(string text)
        => new(text, Array.Empty<AgentAction>());

    public static AgentResponse Error(string message)
        => new($"Error: {message}", Array.Empty<AgentAction>());
}
