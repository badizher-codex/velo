using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Core.AI;

/// <summary>
/// Phase 4.0 (Council Mode — foundations) — verifies the local LLM server
/// that will run the moderator/synthesis step is reachable and exposes the
/// configured model.
///
/// Council Mode runs four cloud LLMs (Claude/ChatGPT/Grok) and one local
/// model in parallel as user-facing panels. The synthesis step — combining
/// the four answers into one final response — must run <b>locally</b> for
/// the privacy contract to hold: the four answers (which may contain
/// user-private context) cannot be shipped to a cloud service for moderation.
/// The Phase 4 spec defaults <c>qwen3:32b</c> as the recommended moderator
/// model and asks for a 16 k token context window, but v2.4.40 lets the
/// user override both the backend type (Ollama / LM Studio / generic
/// OpenAI-compatible server) and the model name.
///
/// This service does not start the server, doesn't pull or download a model
/// and doesn't negotiate version compatibility. It only answers one question:
///   <i>"If a user opens Council Mode right now, will the moderator work?"</i>
///
/// Used by:
///   1. Settings → Council page on first open — display a green checkmark or
///      a red banner explaining what's missing.
///   2. <c>CouncilFirstRunDisclaimer</c> — show the requirement before the
///      user accepts the disclaimer.
///   3. Future: a one-off check the first time the user clicks "Open Council
///      Mode" from the command palette (Phase 4.1).
///
/// Pure HTTP — no WPF dependency. Lives in VELO.Core so it can also be
/// unit-tested with a stubbed HttpClient.
/// </summary>
public sealed class CouncilPreflightService
{
    /// <summary>Default moderator model name (overridable via <see cref="SettingKeys.CouncilModeratorModel"/>).</summary>
    public const string DefaultModeratorModel = "qwen3:32b";

    /// <summary>Legacy alias for <see cref="DefaultModeratorModel"/> kept for callers built against v2.4.39.</summary>
    public const string RequiredModel = DefaultModeratorModel;

    /// <summary>Minimum context window the moderator prompt needs (tokens). Only enforced when
    /// the backend reports it — OpenAI-compat servers (LM Studio, llama.cpp) don't expose context
    /// size on /v1/models, so the check is skipped there and the user is trusted.</summary>
    public const int MinimumContextSize = 16_384;

    /// <summary>Defaults to <c>http://localhost:11434</c> (Ollama canonical) when the user hasn't customised it.</summary>
    public const string DefaultOllamaEndpoint = "http://localhost:11434";

    /// <summary>Backend wire format selector. Ollama uses /api/tags + /api/show; LM Studio and the
    /// generic OpenAI-compat path share /v1/models. Stored as a string in Settings so the value
    /// survives across versions even if the enum gets extended.</summary>
    public enum Backend { Ollama, LMStudio, OpenAICompat }

    /// <summary>Network timeout — local responses should be sub-second.</summary>
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
    /// Result of a single pre-flight probe. Built so the UI can render every state
    /// (ok / model missing / endpoint down / context too small) without having to
    /// know the underlying HTTP topology.
    /// </summary>
    /// <param name="IsHealthy">Endpoint reachable AND model present AND (if known) context size sufficient.</param>
    /// <param name="EndpointReachable">The server answered the discovery endpoint with 200.</param>
    /// <param name="ModelPresent">The configured moderator model name was in the server's list.</param>
    /// <param name="ContextSize">Reported context size if the backend exposes it; null otherwise (OpenAI-compat).</param>
    /// <param name="Endpoint">The base URL hit (after Settings lookup).</param>
    /// <param name="BackendType">Which backend wire format was used for the probe.</param>
    /// <param name="ModelName">The moderator model name the probe looked for.</param>
    /// <param name="FailureReason">Localised hint when <see cref="IsHealthy"/> is false.</param>
    public sealed record Result(
        bool   IsHealthy,
        bool   EndpointReachable,
        bool   ModelPresent,
        int?   ContextSize,
        string Endpoint,
        Backend BackendType,
        string ModelName,
        string? FailureReason);

    /// <summary>
    /// Runs the probe. Flow:
    ///   1. Read backend type + endpoint + model name from Settings (with defaults).
    ///   2. Dispatch to <see cref="ProbeOllamaAsync"/> or <see cref="ProbeOpenAICompatAsync"/>.
    ///   3. Compose <see cref="Result"/>.
    ///
    /// Never throws. Caller doesn't need a try/catch — every failure mode reports
    /// back via the Result fields.
    /// </summary>
    public async Task<Result> CheckAsync(CancellationToken ct = default)
    {
        var backend = await ReadBackendAsync();
        var endpoint = (await _settings.GetAsync(
                            SettingKeys.CouncilOllamaEndpoint, DefaultOllamaEndpoint))
                       .TrimEnd('/');
        var model = await _settings.GetAsync(
                        SettingKeys.CouncilModeratorModel, DefaultModeratorModel);
        if (string.IsNullOrWhiteSpace(model)) model = DefaultModeratorModel;

        using var http = _httpFactory();
        if (http.Timeout > ProbeTimeout * 2) http.Timeout = ProbeTimeout;

        return backend switch
        {
            Backend.Ollama => await ProbeOllamaAsync(http, endpoint, model, ct),
            _              => await ProbeOpenAICompatAsync(http, endpoint, model, backend, ct),
        };
    }

    private async Task<Backend> ReadBackendAsync()
    {
        var raw = await _settings.GetAsync(SettingKeys.CouncilBackendType, nameof(Backend.Ollama));
        return Enum.TryParse<Backend>(raw, ignoreCase: true, out var parsed) ? parsed : Backend.Ollama;
    }

    // ── Ollama path (/api/tags + /api/show) ──────────────────────────────

    private async Task<Result> ProbeOllamaAsync(
        HttpClient http, string endpoint, string model, CancellationToken ct)
    {
        List<string> models;
        try
        {
            using var tagsResp = await http.GetAsync($"{endpoint}/api/tags", ct);
            if (!tagsResp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Council pre-flight: /api/tags returned {Status} at {Endpoint}",
                    (int)tagsResp.StatusCode, endpoint);
                return new Result(false, false, false, null, endpoint, Backend.Ollama, model,
                    $"Ollama respondió HTTP {(int)tagsResp.StatusCode}. ¿Está corriendo y exponiendo /api/tags?");
            }

            var tagsJson = await tagsResp.Content.ReadAsStringAsync(ct);
            models = ExtractOllamaModelNames(tagsJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Council pre-flight: /api/tags unreachable at {Endpoint}", endpoint);
            return new Result(false, false, false, null, endpoint, Backend.Ollama, model,
                $"No se pudo conectar a Ollama en {endpoint}. ¿Está corriendo?");
        }

        if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            return new Result(false, true, false, null, endpoint, Backend.Ollama, model,
                $"Modelo '{model}' no instalado. Ejecutá: ollama pull {model}");
        }

        int? contextSize = null;
        try
        {
            var showPayload = JsonSerializer.Serialize(new { name = model });
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
                model);
        }

        if (contextSize is { } size && size < MinimumContextSize)
        {
            return new Result(false, true, true, contextSize, endpoint, Backend.Ollama, model,
                $"Modelo '{model}' instalado pero su contexto ({size} tokens) está por debajo del mínimo recomendado ({MinimumContextSize}).");
        }

        return new Result(true, true, true, contextSize, endpoint, Backend.Ollama, model, null);
    }

    // ── OpenAI-compat path (/v1/models) ──────────────────────────────────

    private async Task<Result> ProbeOpenAICompatAsync(
        HttpClient http, string endpoint, string model, Backend backend, CancellationToken ct)
    {
        var serverLabel = backend == Backend.LMStudio ? "LM Studio" : "el servidor";
        List<string> models;
        try
        {
            using var resp = await http.GetAsync($"{endpoint}/v1/models", ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Council pre-flight: /v1/models returned {Status} at {Endpoint}",
                    (int)resp.StatusCode, endpoint);
                return new Result(false, false, false, null, endpoint, backend, model,
                    $"{serverLabel} respondió HTTP {(int)resp.StatusCode} en /v1/models.");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            models = ExtractOpenAIModelNames(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Council pre-flight: /v1/models unreachable at {Endpoint}", endpoint);
            return new Result(false, false, false, null, endpoint, backend, model,
                $"No se pudo conectar a {serverLabel} en {endpoint}. ¿Está corriendo y con el server local habilitado?");
        }

        // Be lenient with model name matching for OpenAI-compat servers — LM Studio
        // appends format suffixes (e.g. "qwen3.6-35b-a3b" exact, but UD/quantization
        // variants exist). Try exact case-insensitive first, then a contains match.
        var modelPresent =
            models.Contains(model, StringComparer.OrdinalIgnoreCase) ||
            models.Any(m => m.Contains(model, StringComparison.OrdinalIgnoreCase));

        if (!modelPresent)
        {
            var hint = backend == Backend.LMStudio
                ? $"Cargá '{model}' en LM Studio (Models → Load) o ajustá el nombre del modelo en Settings."
                : $"El modelo '{model}' no aparece en /v1/models. Cargalo en tu servidor o ajustá el nombre del modelo en Settings.";
            return new Result(false, true, false, null, endpoint, backend, model, hint);
        }

        // OpenAI-compat /v1/models doesn't expose context size. We trust the user to
        // have loaded a sufficient model.
        return new Result(true, true, true, null, endpoint, backend, model, null);
    }

    // ── JSON helpers (kept private so the public surface stays minimal) ──

    private static List<string> ExtractOllamaModelNames(string tagsJson)
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

    private static List<string> ExtractOpenAIModelNames(string json)
    {
        // OpenAI /v1/models shape: { "data": [ { "id": "model-name", ... }, ... ] }
        // LM Studio matches this shape exactly. llama.cpp server too.
        var names = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataEl.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl) &&
                        idEl.ValueKind == JsonValueKind.String)
                    {
                        names.Add(idEl.GetString() ?? "");
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
            // Ollama /api/show returns model parameters under several keys depending on
            // version. Look at parameters.num_ctx, options.num_ctx, and
            // model_info.{*}.context_length in that order. Earliest match wins.
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
