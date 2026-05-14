using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Core.AI;

/// <summary>
/// v2.4.42 — Stateless, one-shot OpenAI-compatible chat adapter for
/// VELO's internal AI services (<c>SmartBlockClassifier</c>,
/// <c>PhishingShield</c>, <c>CodeActions</c>, <c>BookmarkAIService</c>,
/// <c>AIContextActions</c>, and Phase 4.1's Council moderator).
///
/// <b>What problem this solves.</b>
/// Up to v2.4.41 every internal AI service routed through
/// <c>MainWindow.WireAgentChat → AgentLauncher.SendAsync(tabId="__ai__", …)</c>.
/// That path was designed for the VeloAgent chat panel — it keeps a per-tabId
/// conversation history in <c>_history["__ai__"]</c>. Because every internal
/// caller used the same tabId, all of them accumulated history into a single
/// shared bucket, and the system prompt was concatenated into the user
/// message (<c>$"{system}\n\n{user}"</c>) rather than sent as a separate
/// <c>role:"system"</c> entry. Result: each navigation appended ~2 entries,
/// every subsequent inference replayed an ever-growing log of irrelevant
/// past prompts, the local model's context filled up, latency spiraled,
/// timeouts started, and the model could end up reading its own prior
/// classifications as if they were the current request.
///
/// <b>How this adapter fixes it.</b>
/// <list type="bullet">
///   <item>Stateless: every call composes the payload from <i>only</i> the
///         (system, user) it was handed. No history, no shared bucket.</item>
///   <item>Proper roles: system goes in <c>messages[0]</c> with role "system",
///         user in <c>messages[1]</c> with role "user". Matches the OpenAI
///         chat-completions spec exactly; LM Studio / Ollama / llama.cpp
///         server / vllm all accept this shape.</item>
///   <item>Single global concurrency permit (<see cref="_concurrency"/>):
///         even when SmartBlock + PhishingShield + BookmarkAI fan out from
///         the same page-load, the local model receives exactly one in-flight
///         request at a time. Tail latency is capped instead of blowing up
///         under parallel pressure.</item>
///   <item>Fail-soft: any HTTP failure / parse failure / non-success status
///         returns an empty string. Caller services (SmartBlock, PhishingShield,
///         …) treat empty replies as "no verdict" and degrade to Allow / Safe
///         per their existing fallback logic. Cancellation propagates verbatim.</item>
/// </list>
///
/// <b>Backend coverage.</b>
/// This adapter only handles <c>AI Mode = Custom</c> (Ollama / LM Studio /
/// any OpenAI-compatible local server). For <c>AI Mode = Claude</c> the
/// adapter currently returns empty — security path for Claude was always
/// handled directly via <c>VELO.Security.AI.Adapters.ClaudeAdapter</c> in
/// <c>AISecurityEngine</c>, not via this delegate, so nothing regresses.
/// For <c>AI Mode = Offline</c> the adapter returns empty (caller fail-soft).
///
/// <b>The VeloAgent chat panel is NOT routed through this adapter.</b>
/// The panel's conversational chat (slash commands, agent actions, page
/// priming) still goes through <c>AgentLauncher</c> because there history
/// is intentional. Only the always-on internal classifiers/analysers switch
/// to this stateless path.
/// </summary>
public sealed class DirectChatAdapter
{
    /// <summary>Per-call HTTP timeout. Local inference is usually sub-second on small
    /// models but jumps to 5-20 s on 30B+ class models — 30 s is a comfortable upper
    /// bound that still surfaces a real hang quickly instead of waiting forever.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default endpoint when the user hasn't customised <c>SettingKeys.AiCustomEndpoint</c>.</summary>
    public const string DefaultEndpoint = "http://localhost:11434";

    /// <summary>Default model name when the user hasn't customised <c>SettingKeys.AiClaudeModel</c>
    /// (yes, the key is misnamed — it carries the Custom-mode model too, dating back to Phase 1).</summary>
    public const string DefaultModel = "qwen3:32b";

    private readonly SettingsRepository _settings;
    private readonly Func<HttpClient> _httpFactory;
    private readonly ILogger<DirectChatAdapter> _logger;

    /// <summary>Global concurrency cap — one model call in-flight at a time. Caller services
    /// queue rather than fan out. Local LLMs do not benefit from parallel requests; on consumer
    /// hardware they degrade catastrophically (queueing inside the runtime, VRAM thrashing,
    /// stall on every concurrent context switch).</summary>
    private readonly SemaphoreSlim _concurrency = new(initialCount: 1, maxCount: 1);

    public DirectChatAdapter(
        SettingsRepository settings,
        ILogger<DirectChatAdapter>? logger = null,
        Func<HttpClient>? httpFactory = null)
    {
        _settings    = settings;
        _logger      = logger ?? NullLogger<DirectChatAdapter>.Instance;
        _httpFactory = httpFactory ?? (() => new HttpClient { Timeout = CallTimeout });
    }

    /// <summary>
    /// Sends a single (system, user) prompt to the configured local AI backend and
    /// returns the assistant's reply text. Signature matches
    /// <c>AiChatRouter.ChatDelegate</c> verbatim so it can be wired wherever a
    /// ChatDelegate is expected.
    ///
    /// Returns an empty string when:
    /// <list type="bullet">
    ///   <item>AI Mode is Offline or Claude (Claude has its own security-engine path).</item>
    ///   <item>The server is unreachable, returns non-2xx, or sends a malformed reply.</item>
    /// </list>
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> fires.
    /// </summary>
    public async Task<string> SendAsync(string system, string user, CancellationToken ct)
    {
        var aiMode = await _settings.GetAsync(SettingKeys.AiMode, "Offline").ConfigureAwait(false);
        if (!string.Equals(aiMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            // Claude path is handled directly by AISecurityEngine; Offline path has no AI.
            return "";
        }

        var endpoint = (await _settings.GetAsync(SettingKeys.AiCustomEndpoint, DefaultEndpoint)
                            .ConfigureAwait(false))
                       .TrimEnd('/');
        var modelRaw = await _settings.GetAsync(SettingKeys.AiClaudeModel, "").ConfigureAwait(false);
        var model    = string.IsNullOrWhiteSpace(modelRaw) ? DefaultModel : modelRaw;

        await _concurrency.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var http = _httpFactory();
            if (http.Timeout > CallTimeout * 2) http.Timeout = CallTimeout;

            var payload = new
            {
                model,
                stream      = false,
                temperature = 0.3,
                // 512 is enough for SmartBlock's one-line verdict, PhishingShield's JSON
                // verdict, code-action snippets and bookmark tag lists. VeloAgent panel
                // does NOT route through here so 2048-token replies are not needed.
                max_tokens  = 512,
                messages    = new[]
                {
                    new { role = "system", content = system ?? "" },
                    new { role = "user",   content = user   ?? "" },
                },
            };
            var body = JsonSerializer.Serialize(payload);

            using var resp = await http.PostAsync(
                $"{endpoint}/v1/chat/completions",
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var status = (int)resp.StatusCode;
                _logger.LogWarning(
                    "DirectChat: HTTP {Status} from {Endpoint}/v1/chat/completions (model={Model})",
                    status, endpoint, model);
                return "";
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractAssistantText(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectChat: call to {Endpoint} failed", endpoint);
            return "";
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>Pulls the first choice's <c>message.content</c> string from an OpenAI-compat
    /// chat-completions response. Returns empty on any parse failure (fail-soft per the
    /// adapter contract).</summary>
    internal static string ExtractAssistantText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
                return "";

            var first = choices[0];
            if (!first.TryGetProperty("message", out var msg)) return "";
            if (!msg.TryGetProperty("content",   out var content)) return "";
            if (content.ValueKind != JsonValueKind.String) return "";

            return content.GetString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Diagnostic: how many callers are currently waiting on the semaphore. 0 means
    /// the model is idle or the in-flight call is the only one. Used by tests + future
    /// telemetry; never gated on in production logic.</summary>
    public int CurrentQueueDepth => 1 - _concurrency.CurrentCount;
}
