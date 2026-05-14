using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Core.AI;

/// <summary>
/// Phase 4.0 (Council Mode — foundations, v2.5.0-pre) — verifies the local
/// Ollama server can serve the moderator model used by Council synthesis.
///
/// Council Mode runs four cloud LLMs (Claude/ChatGPT/Grok) and one local
/// model (Ollama) in parallel as user-facing panels. The synthesis step
/// — combining the four answers into one final response — must run
/// <b>locally</b> for the privacy contract to hold: the four answers
/// (which may contain user-private context) cannot be shipped to a cloud
/// service for moderation. The Phase 4 spec locks <c>qwen3:32b</c> as the
/// moderator model and requires a 16 k token context window.
///
/// This service does not start Ollama, doesn't pull the model and doesn't
/// negotiate version compatibility. It only answers one question:
///   <i>"If a user opens Council Mode right now, will the moderator work?"</i>
///
/// Used by:
///   1. Settings → Council page on first open (chunk H) — display a green
///      checkmark or a red banner explaining what's missing.
///   2. <c>CouncilFirstRunDisclaimer</c> (chunk G) — show the requirement
///      before the user accepts the disclaimer.
///   3. Future: a one-off check the first time the user clicks "Open
///      Council Mode" from the command palette (Phase 4.1).
///
/// Pure HTTP — no WPF dependency. Lives in VELO.Core so it can also be
/// unit-tested with a stubbed HttpClient.
/// </summary>
public sealed class CouncilPreflightService
{
    /// <summary>Moderator model required by the Phase 4 spec.</summary>
    public const string RequiredModel = "qwen3:32b";

    /// <summary>Minimum context window the moderator prompt needs (tokens).</summary>
    public const int MinimumContextSize = 16_384;

    /// <summary>Defaults to <c>http://localhost:11434</c> when the user hasn't customised it.</summary>
    public const string DefaultOllamaEndpoint = "http://localhost:11434";

    /// <summary>Network timeout — Ollama responses are local and should be sub-second.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    private readonly SettingsRepository _settings;
    private readonly ILogger<CouncilPreflightService> _logger;
    private readonly Func<HttpClient> _httpFactory;

    public CouncilPreflightService(
        SettingsRepository settings,
        ILogger<CouncilPreflightService>? logger = null,
        Func<HttpClient>? httpFactory = null)
    {
        _settings    = settings;
        _logger      = logger ?? NullLogger<CouncilPreflightService>.Instance;
        _httpFactory = httpFactory ?? (() => new HttpClient { Timeout = ProbeTimeout });
    }

    /// <summary>
    /// Result of a single pre-flight probe. Built so the UI can render
    /// the three states (ok / model missing / endpoint down) without
    /// having to know the underlying HTTP topology.
    /// </summary>
    /// <param name="IsHealthy">Endpoint reachable AND model present with the required context.</param>
    /// <param name="EndpointReachable">/api/tags responded with 200.</param>
    /// <param name="ModelPresent">qwen3:32b was in the tags list.</param>
    /// <param name="ContextSize">Reported context size from /api/show, or null if not probed.</param>
    /// <param name="Endpoint">The base URL hit (after Settings lookup).</param>
    /// <param name="FailureReason">Localised hint when <see cref="IsHealthy"/> is false.</param>
    public sealed record Result(
        bool   IsHealthy,
        bool   EndpointReachable,
        bool   ModelPresent,
        int?   ContextSize,
        string Endpoint,
        string? FailureReason);

    /// <summary>
    /// Runs the probe. Order:
    ///   1. Read the Ollama endpoint from Settings (<see cref="SettingKeys.CouncilOllamaEndpoint"/>),
    ///      defaulting to <see cref="DefaultOllamaEndpoint"/>. Council uses its own setting
    ///      because Custom AI Mode (<c>SettingKeys.AiCustomEndpoint</c>) may legitimately point
    ///      at LM Studio or another non-Ollama OpenAI-compatible server.
    ///   2. GET /api/tags → if it fails or returns non-200, report
    ///      <c>EndpointReachable=false</c> and stop.
    ///   3. Look for <see cref="RequiredModel"/> in the tags response.
    ///   4. POST /api/show with the model name → read num_ctx if present.
    ///   5. Compose <see cref="Result"/>.
    ///
    /// Never throws. Caller doesn't need a try/catch — every failure mode
    /// reports back via the Result fields.
    /// </summary>
    public async Task<Result> CheckAsync(CancellationToken ct = default)
    {
        var endpoint = (await _settings.GetAsync(
                            SettingKeys.CouncilOllamaEndpoint, DefaultOllamaEndpoint))
                       .TrimEnd('/');

        using var http = _httpFactory();
        if (http.Timeout > ProbeTimeout * 2) http.Timeout = ProbeTimeout;

        bool endpointReachable;
        List<string> models;
        try
        {
            using var tagsResp = await http.GetAsync($"{endpoint}/api/tags", ct);
            endpointReachable = tagsResp.IsSuccessStatusCode;
            if (!endpointReachable)
            {
                _logger.LogWarning(
                    "Council pre-flight: /api/tags returned {Status} at {Endpoint}",
                    (int)tagsResp.StatusCode, endpoint);
                return new Result(false, false, false, null, endpoint,
                    $"Ollama respondió HTTP {(int)tagsResp.StatusCode}.");
            }

            var tagsJson = await tagsResp.Content.ReadAsStringAsync(ct);
            models = ExtractModelNames(tagsJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Council pre-flight: /api/tags unreachable at {Endpoint}", endpoint);
            return new Result(false, false, false, null, endpoint,
                $"No se pudo conectar a Ollama en {endpoint}. ¿Está corriendo?");
        }

        var modelPresent = models.Contains(RequiredModel, StringComparer.OrdinalIgnoreCase);
        if (!modelPresent)
        {
            return new Result(false, true, false, null, endpoint,
                $"Modelo '{RequiredModel}' no instalado. Ejecutá: ollama pull {RequiredModel}");
        }

        int? contextSize = null;
        try
        {
            var showPayload = JsonSerializer.Serialize(new { name = RequiredModel });
            using var showResp = await http.PostAsync(
                $"{endpoint}/api/show",
                new StringContent(showPayload, System.Text.Encoding.UTF8, "application/json"),
                ct);
            if (showResp.IsSuccessStatusCode)
            {
                var showJson = await showResp.Content.ReadAsStringAsync(ct);
                contextSize = ExtractContextSize(showJson);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: model is present, we just don't know the exact context.
            _logger.LogDebug(ex,
                "Council pre-flight: /api/show failed for {Model}; treating ContextSize as unknown.",
                RequiredModel);
        }

        if (contextSize is { } size && size < MinimumContextSize)
        {
            return new Result(false, true, true, contextSize, endpoint,
                $"Modelo '{RequiredModel}' instalado pero su contexto ({size} tokens) está por debajo del mínimo requerido ({MinimumContextSize}).");
        }

        return new Result(true, true, true, contextSize, endpoint, null);
    }

    // ── JSON helpers (kept private so the public surface stays minimal) ──

    private static List<string> ExtractModelNames(string tagsJson)
    {
        var names = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(tagsJson);
            if (doc.RootElement.TryGetProperty("models", out var modelsEl) &&
                modelsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelsEl.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameEl) &&
                        nameEl.ValueKind == JsonValueKind.String)
                    {
                        names.Add(nameEl.GetString() ?? "");
                    }
                }
            }
        }
        catch
        {
            // Malformed JSON — treat as no models reported.
        }
        return names;
    }

    private static int? ExtractContextSize(string showJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(showJson);
            // Ollama /api/show returns model parameters under several keys
            // depending on version. Look at parameters.num_ctx, options.num_ctx,
            // and model_info.{*}.context_length in that order. Earliest match wins.
            if (TryReadInt(doc.RootElement, "parameters", "num_ctx", out var p)) return p;
            if (TryReadInt(doc.RootElement, "options",    "num_ctx", out var o)) return o;

            if (doc.RootElement.TryGetProperty("model_info", out var info) &&
                info.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in info.EnumerateObject())
                {
                    if (prop.Name.EndsWith(".context_length", StringComparison.Ordinal) &&
                        prop.Value.TryGetInt32(out var ctx))
                    {
                        return ctx;
                    }
                }
            }
        }
        catch
        {
            // Malformed JSON or unexpected shape — return null so callers know.
        }
        return null;
    }

    private static bool TryReadInt(JsonElement root, string parent, string child, out int value)
    {
        value = 0;
        if (root.TryGetProperty(parent, out var p) &&
            p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty(child, out var c) &&
            c.TryGetInt32(out var v))
        {
            value = v;
            return true;
        }
        return false;
    }
}
