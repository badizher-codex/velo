using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VELO.Agent.Models;

namespace VELO.Agent.Adapters;

/// <summary>
/// Agent backend that uses an already-running Ollama instance.
/// Preferred over LLamaSharp when Ollama is available — faster cold start,
/// supports larger models, and users may already have it configured.
/// </summary>
public class OllamaAgentAdapter : IAgentAdapter
{
    private readonly string                    _endpoint;
    private readonly string                    _model;
    private readonly HttpClient                _http;
    private readonly ILogger<OllamaAgentAdapter> _logger;

    private const int TimeoutSeconds = 120;  // thinking models can be slow

    public bool   IsAvailable => !string.IsNullOrEmpty(_model);
    public string BackendName => $"Ollama Agent ({_model})";

    public OllamaAgentAdapter(string endpoint, string model, ILogger<OllamaAgentAdapter> logger)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model    = model;
        _logger   = logger;
        _http     = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
    }

    public async Task<AgentResponse> ChatAsync(
        string userPrompt,
        AgentContext context,
        CancellationToken ct = default)
    {
        try
        {
            var userMessage = AgentPromptBuilder.BuildUserMessage(userPrompt, context);

            var messages = new List<object>
            {
                new { role = "system", content = AgentPromptBuilder.SystemPrompt },
            };

            // Inject conversation history
            foreach (var (role, content) in context.History)
                messages.Add(new { role, content });

            messages.Add(new { role = "user", content = userMessage });

            var body = JsonSerializer.Serialize(new
            {
                model       = _model,
                stream      = false,
                temperature = 0.3,
                top_p       = 0.9,
                // 2048: thinking models need ~400-800 tokens to reason + ~200 for the reply.
                // 1024 is too tight for complex/safety-sensitive queries.
                max_tokens  = 2048,
                messages    = messages.ToArray()
            });

            // OpenAI-compatible endpoint — works with Ollama, LM Studio, llama.cpp server, etc.
            var response = await _http.PostAsync(
                $"{_endpoint}/v1/chat/completions",
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var doc  = JsonDocument.Parse(json);

            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            // Standard content field
            var raw = message.GetProperty("content").GetString() ?? "";

            // Thinking models (Qwen3, DeepSeek-R1, etc.) with forced-on reasoning write their
            // final JSON inside reasoning_content while content stays empty.
            // Extract the last valid JSON block from the reasoning instead of dumping it all.
            if (string.IsNullOrWhiteSpace(raw) &&
                message.TryGetProperty("reasoning_content", out var reasoningEl))
            {
                var reasoning = reasoningEl.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(reasoning))
                {
                    raw = ExtractJsonFromReasoning(reasoning);
                    if (!string.IsNullOrWhiteSpace(raw))
                        _logger.LogInformation("OllamaAgent: extracted JSON from reasoning_content ({Len} chars)", raw.Length);
                    else
                    {
                        _logger.LogWarning("OllamaAgent: content empty and no JSON found in reasoning_content — token limit too low?");
                        // Return a friendly message instead of empty/crash
                        raw = """{"reply":"El modelo agotó su límite de tokens razonando. Intenta una pregunta más corta o usa un modelo más pequeño.","actions":[]}""";
                    }
                }
            }

            _logger.LogDebug("OllamaAgent raw: {Raw}", raw[..Math.Min(raw.Length, 200)]);
            return AgentResponseParser.Parse(raw);
        }
        catch (OperationCanceledException)
        {
            return AgentResponse.Error("Ollama no respondió a tiempo.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OllamaAgent error");
            return AgentResponse.Error($"Error de Ollama: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans reasoning_content backwards for the last REAL JSON object containing "reply".
    /// Thinking models write the final answer inside the reasoning block; we extract it
    /// without dumping the whole chain-of-thought. Skips schema/template placeholders.
    /// </summary>
    private static string ExtractJsonFromReasoning(string reasoning)
    {
        int searchFrom = reasoning.Length - 1;
        int maxAttempts = 15;

        while (maxAttempts-- > 0 && searchFrom >= 0)
        {
            int replyIdx = reasoning.LastIndexOf("\"reply\"", searchFrom, StringComparison.Ordinal);
            if (replyIdx < 0) break;

            // Walk backwards to find the opening brace
            int start = -1;
            for (int i = replyIdx - 1; i >= 0; i--)
            {
                if (reasoning[i] == '{') { start = i; break; }
            }
            if (start < 0) { searchFrom = replyIdx - 1; continue; }

            // Walk forward to find the matching closing brace
            int depth = 0, end = -1;
            for (int i = start; i < reasoning.Length; i++)
            {
                if      (reasoning[i] == '{') depth++;
                else if (reasoning[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
            }
            if (end < 0) { searchFrom = start - 1; continue; }

            var candidate = reasoning[start..(end + 1)];

            // Skip schema templates — they contain "..." placeholder values
            if (candidate.Contains("\"...\"") || candidate.Contains(": \"...\","))
            { searchFrom = start - 1; continue; }

            // Try to parse as valid JSON
            string? parsed = null;
            try { JsonDocument.Parse(candidate); parsed = candidate; }
            catch
            {
                // Models sometimes add inline comments: "actions": [] (reason for no action)
                var cleaned = Regex.Replace(candidate, @"\]\s*\([^)]*\)", "]",
                    RegexOptions.None, TimeSpan.FromSeconds(1));
                try { JsonDocument.Parse(cleaned); parsed = cleaned; }
                catch { /* still invalid */ }
            }

            if (parsed != null)
            {
                // Validate the reply value isn't a placeholder
                try
                {
                    var doc      = JsonDocument.Parse(parsed);
                    var replyVal = doc.RootElement.GetProperty("reply").GetString() ?? "";
                    if (replyVal.TrimStart().StartsWith("Tu respuesta") ||
                        replyVal == "..." || replyVal.Length < 2)
                    { searchFrom = start - 1; continue; }
                }
                catch { searchFrom = start - 1; continue; }

                return parsed;
            }

            searchFrom = start - 1;
        }

        return "";
    }
}
