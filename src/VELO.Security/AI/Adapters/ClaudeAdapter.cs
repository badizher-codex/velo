using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using VELO.Security.AI.Models;

namespace VELO.Security.AI.Adapters;

public class ClaudeAdapter : IAIAdapter
{
    private const string DEFAULT_MODEL = "claude-sonnet-4-20250514";
    private const int MAX_TOKENS = 300;
    private const int TIMEOUT_SECONDS = 3;

    private const string SYSTEM_PROMPT = """
        Eres un analizador de seguridad web. Recibes contexto técnico sobre una request de red y debes determinar si es una amenaza.

        RESPONDE ÚNICAMENTE con JSON válido, sin texto adicional, sin markdown:
        {
          "verdict": "SAFE" | "WARN" | "BLOCK",
          "confidence": 0-100,
          "reason": "Explicación breve en español (máximo 2 oraciones)",
          "threat_type": "Tracker" | "Malware" | "Phishing" | "DataExfiltration" | "Miner" | "Fingerprinting" | "MitM" | "Other" | null
        }

        Criterios:
        - BLOCK: amenaza clara, riesgo alto para el usuario
        - WARN: comportamiento sospechoso pero no definitivamente malicioso
        - SAFE: request legítima, sin indicadores de amenaza
        """;

    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<ClaudeAdapter> _logger;

    public ClaudeAdapter(string apiKey, string model, ILogger<ClaudeAdapter> logger)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrEmpty(model) ? DEFAULT_MODEL : model;
        _logger = logger;
    }

    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
    public string ModeName => "Claude";

    public async Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

        try
        {
            var client = new AnthropicClient(_apiKey);
            var prompt = BuildPrompt(context);

            var response = await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model = _model,
                    MaxTokens = MAX_TOKENS,
                    System = [new SystemMessage(SYSTEM_PROMPT)],
                    Messages = [new Message(RoleType.User, prompt)]
                },
                timeoutCts.Token);

            var text = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";
            return ParseVerdict(text);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Claude timeout for domain {Domain}, falling back to offline", context.Domain);
            return AIVerdict.Fallback("IA no disponible, usando análisis local");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude analysis failed for {Domain}", context.Domain);
            return AIVerdict.Fallback("Error en análisis IA, usando análisis local");
        }
    }

    private static string BuildPrompt(ThreatContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Dominio: {ctx.Domain}");
        sb.AppendLine($"Tipo de recurso: {ctx.ResourceType}");
        sb.AppendLine($"Origen (referrer): {ctx.Referrer}");
        sb.AppendLine($"Risk score heurístico: {ctx.RiskScore}/100");

        if (ctx.DetectedPatterns.Count > 0)
            sb.AppendLine($"Patrones detectados: {string.Join(", ", ctx.DetectedPatterns)}");

        if (!string.IsNullOrEmpty(ctx.ScriptSnippet))
            sb.AppendLine($"Snippet del script (primeros 500 chars):\n{ctx.ScriptSnippet}");

        if (ctx.TLSInfo != null)
        {
            sb.AppendLine($"TLS: {ctx.TLSInfo.Protocol}");
            if (ctx.TLSInfo.IsSelfSigned) sb.AppendLine("ADVERTENCIA: Certificado autofirmado");
        }

        return sb.ToString();
    }

    private static AIVerdict ParseVerdict(string json)
        => VerdictParser.Parse(json, "AI_CLAUDE");
}
