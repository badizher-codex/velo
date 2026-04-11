using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VELO.Security.AI.Models;

namespace VELO.Security.AI.Adapters;

public class OllamaAdapter : IAIAdapter
{
    private const int TIMEOUT_SECONDS = 30; // qwen3:8b needs time for first inference

    private readonly string _endpoint;
    private readonly string _model;
    private readonly HttpClient _http;
    private readonly ILogger<OllamaAdapter> _logger;

    private const string SYSTEM_PROMPT =
        "Eres un analizador de seguridad web. " +
        "Responde ÚNICAMENTE con JSON válido en una sola línea, sin texto adicional ni markdown: " +
        "{\"verdict\":\"SAFE\"|\"WARN\"|\"BLOCK\",\"confidence\":0-100,\"reason\":\"max 2 oraciones en español\",\"threat_type\":\"Phishing\"|\"Malware\"|\"KnownTracker\"|null}";

    public OllamaAdapter(string endpoint, string model, ILogger<OllamaAdapter> logger)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model    = model;
        _logger   = logger;
        _http     = new HttpClient { Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS) };
    }

    public bool IsAvailable => !string.IsNullOrEmpty(_model);
    public string ModeName => $"Ollama ({_model})";

    public async Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct = default)
    {
        try
        {
            // Use Ollama's native /api/chat endpoint — supports "think":false for qwen3
            var body = JsonSerializer.Serialize(new
            {
                model   = _model,
                stream  = false,
                think   = false,            // disables qwen3 thinking mode — fast response
                options = new { temperature = 0, num_predict = 120 },
                messages = new[]
                {
                    new { role = "system", content = SYSTEM_PROMPT },
                    new { role = "user",   content = BuildPrompt(context) }
                }
            });

            var response = await _http.PostAsync(
                $"{_endpoint}/api/chat",   // native API, not /v1/ compat layer
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var doc  = JsonDocument.Parse(json);

            // Native /api/chat response: { "message": { "content": "..." } }
            var text = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            _logger.LogInformation("Ollama [{Model}] → {Domain}: {Response}",
                _model, context.Domain, text.Trim());

            return VerdictParser.Parse(text, "AI_OLLAMA");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ollama timeout for {Domain}", context.Domain);
            return AIVerdict.Fallback("Ollama no respondió a tiempo");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama analysis failed for {Domain}", context.Domain);
            return AIVerdict.Fallback("Ollama no disponible, usando análisis local");
        }
    }

    private static string BuildPrompt(ThreatContext ctx)
        => $"Analiza este dominio:\nDominio: {ctx.Domain}\nTipo de recurso: {ctx.ResourceType}\nScore de riesgo: {ctx.RiskScore}/100";
}
