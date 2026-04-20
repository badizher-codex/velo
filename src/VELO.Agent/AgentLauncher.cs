using Microsoft.Extensions.Logging;
using VELO.Agent.Adapters;
using VELO.Agent.Models;
using VELO.Core.Events;

namespace VELO.Agent;

/// <summary>
/// Orchestrates a conversation turn:
///   1. Picks the best available backend (LLamaSharp → Ollama → offline fallback).
///   2. Calls ChatAsync with context.
///   3. Sends proposed actions to AgentActionSandbox for user approval.
///   4. Publishes AgentActionProposedEvent for each action.
///
/// Callers subscribe to Sandbox.ActionApproved / ActionRejected to react.
/// </summary>
public class AgentLauncher(
    IEnumerable<IAgentAdapter> adapters,
    AgentActionSandbox         sandbox,
    EventBus                   eventBus,
    ILogger<AgentLauncher>     logger)
{
    private readonly List<IAgentAdapter>   _adapters = adapters.ToList();
    private readonly AgentActionSandbox    _sandbox  = sandbox;
    private readonly EventBus              _eventBus = eventBus;
    private readonly ILogger<AgentLauncher> _logger  = logger;

    // Conversation history per session (tabId → turns)
    private readonly Dictionary<string, List<(string Role, string Content)>> _history = new();

    public event Action<AgentResponse>? ResponseReady;

    /// <summary>
    /// Send a user prompt from the given tab. Non-blocking — fires ResponseReady when done.
    /// </summary>
    public void SendAsync(string tabId, string userPrompt, AgentContext context)
    {
        // Attach history to context
        var history = GetOrCreateHistory(tabId);
        var ctxWithHistory = new AgentContext
        {
            CurrentUrl      = context.CurrentUrl,
            CurrentDomain   = context.CurrentDomain,
            PageTitle       = context.PageTitle,
            PageTextSnippet = context.PageTextSnippet,
            ContainerId     = context.ContainerId,
            OpenTabCount    = context.OpenTabCount,
            History         = history.AsReadOnly(),
        };

        _ = Task.Run(async () =>
        {
            var response = await RunAsync(userPrompt, ctxWithHistory);

            // Append to history
            history.Add(("user",      userPrompt));
            history.Add(("assistant", response.ReplyText));

            // Trim history to last 20 turns (40 entries)
            while (history.Count > 40) history.RemoveAt(0);

            ResponseReady?.Invoke(response);

            // Send each action to sandbox
            foreach (var action in response.Actions)
            {
                _sandbox.Propose(tabId, action);
                _eventBus.Publish(new AgentActionProposedEvent(action.Type.ToString(),
                    System.Text.Json.JsonSerializer.Serialize(action)));
            }
        });
    }

    public void ClearHistory(string tabId) => _history.Remove(tabId);

    /// <summary>
    /// Replaces the adapter list at runtime — called by AppBootstrapper after settings change.
    /// Thread-safe for single-writer (UI thread) scenarios.
    /// </summary>
    public void UpdateAdapters(IEnumerable<IAgentAdapter> adapters)
    {
        _adapters.Clear();
        _adapters.AddRange(adapters);
        _logger.LogInformation("AgentLauncher: adapters updated → [{Names}]",
            string.Join(", ", _adapters.Select(a => $"{a.BackendName}(avail={a.IsAvailable})")));
    }

    // ── Private ─────────────────────────────────────────────────────────────

    private async Task<AgentResponse> RunAsync(string prompt, AgentContext ctx)
    {
        var backend = _adapters.FirstOrDefault(a => a.IsAvailable);
        if (backend == null)
        {
            _logger.LogWarning("AgentLauncher: no adapter available, returning offline fallback");
            return AgentResponse.TextOnly(
                "No hay ningún modelo de IA disponible. " +
                "Descarga un modelo GGUF o inicia Ollama para usar VeloAgent.");
        }

        _logger.LogDebug("AgentLauncher: using backend '{Backend}' for prompt: {Prompt}",
            backend.BackendName, prompt[..Math.Min(prompt.Length, 80)]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        return await backend.ChatAsync(prompt, ctx, cts.Token);
    }

    private List<(string Role, string Content)> GetOrCreateHistory(string tabId)
    {
        if (!_history.TryGetValue(tabId, out var h))
        {
            h = new List<(string, string)>();
            _history[tabId] = h;
        }
        return h;
    }
}
