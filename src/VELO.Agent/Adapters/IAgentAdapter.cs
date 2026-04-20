using VELO.Agent.Models;

namespace VELO.Agent.Adapters;

/// <summary>
/// Contract for all agent backends (LLamaSharp local, Ollama, Claude API).
/// Separate from IAIAdapter — the agent has a different prompt structure
/// and returns structured actions, not just threat verdicts.
/// </summary>
public interface IAgentAdapter
{
    bool   IsAvailable { get; }
    string BackendName { get; }

    Task<AgentResponse> ChatAsync(
        string       userPrompt,
        AgentContext context,
        CancellationToken ct = default);
}
