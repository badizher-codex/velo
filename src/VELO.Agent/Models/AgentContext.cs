namespace VELO.Agent.Models;

/// <summary>
/// Snapshot of the browser state passed to the agent alongside the user prompt.
/// </summary>
public class AgentContext
{
    public string  CurrentUrl     { get; init; } = "";
    public string  CurrentDomain  { get; init; } = "";
    public string  PageTitle      { get; init; } = "";
    public string? PageTextSnippet { get; init; }  // first ~1000 chars of visible text
    public string  ContainerId    { get; init; } = "none";
    public int     OpenTabCount   { get; init; }

    // Conversation history for multi-turn
    public IReadOnlyList<(string Role, string Content)> History { get; init; }
        = Array.Empty<(string, string)>();
}
