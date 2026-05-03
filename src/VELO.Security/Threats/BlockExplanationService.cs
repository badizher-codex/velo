using Microsoft.Extensions.Logging;

namespace VELO.Security.Threats;

/// <summary>
/// Phase 3 / Sprint 1 — Generates human-readable explanations for one
/// <see cref="BlockEntry"/>. Calls the host-supplied LLM delegate when set;
/// always honours a 3-second timeout and falls back to static templates so
/// the user gets *something* even when the AI is offline / slow / missing.
///
/// Architectural note: the service does NOT depend on VELO.Agent so that
/// VELO.Security stays self-contained. The host (MainWindow) wires
/// <see cref="ChatDelegate"/> to AgentLauncher's chat path.
/// </summary>
public class BlockExplanationService(ILogger<BlockExplanationService> logger)
{
    private readonly ILogger<BlockExplanationService> _logger = logger;
    private readonly Dictionary<string, (string Text, DateTime CachedAtUtc)> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// (systemPrompt, userPrompt, ct) → reply text. Wired by the host to the
    /// best available agent backend (LLamaSharp/Ollama/Claude). Null until
    /// wiring runs — service then short-circuits to static templates.
    /// </summary>
    public Func<string, string, CancellationToken, Task<string>>? ChatDelegate { get; set; }

    public async Task<string> ExplainAsync(BlockEntry entry, CancellationToken ct = default)
    {
        var key = CacheKey(entry);

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var hit) && (DateTime.UtcNow - hit.CachedAtUtc) < CacheTtl)
                return hit.Text;
        }

        // Try the LLM with a hard timeout. If anything goes wrong (no
        // delegate, timeout, exception) fall back to the static template.
        if (ChatDelegate != null)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(AiTimeout);

                var system = "You are a privacy expert explaining to a technical but non-specialist user. Reply in Spanish unless the prompt language differs. Two to three sentences. No invented technical claims.";
                var prompt = BuildPrompt(entry);
                var text   = await ChatDelegate(system, prompt, timeoutCts.Token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Store(key, text);
                    return text;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug("Explain: AI timed out after {Sec}s — falling back to static template", AiTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Explain: AI error — falling back to static template");
            }
        }

        var fallback = LookupStaticTemplate(entry);
        Store(key, fallback);
        return fallback;
    }

    /// <summary>Exposed for tests — returns the prompt string passed to the model.</summary>
    public static string BuildPrompt(BlockEntry entry)
        => $"""
           Domain: {entry.Host}
           URL: {entry.FullUrl}
           Category: {entry.Kind}
           Subtype: {entry.SubKind}
           Block source: {entry.Source}
           Confidence: {entry.Confidence}%
           Malwaredex hit: {(entry.IsMalwaredexHit ? "yes" : "no")}

           Explain in 2-3 sentences why this request was blocked. If it is a
           well-known tracker (Google Analytics, Facebook Pixel, …), mention
           what data it typically captures. If it is a fingerprint, name the
           technique. If malware, emphasise the risk avoided. Do not invent
           capabilities — if unsure, say "this domain appears in {entry.Source}
           lists".
           """;

    /// <summary>Public for tests — picks a localised static template by kind+subkind.</summary>
    public static string LookupStaticTemplate(BlockEntry entry)
    {
        // Curated short-form fallbacks — no LLM needed. Keep these sync with
        // the live ExplanationTemplates table so the UI never reads "no info".
        var src = entry.Source switch
        {
            BlockSource.GoldenList    => "Golden List",
            BlockSource.Malwaredex    => "Malwaredex local",
            BlockSource.AIEngine      => "análisis IA",
            BlockSource.UserRule      => "regla del usuario",
            BlockSource.StaticList    => "lista estática integrada",
            BlockSource.RequestGuard  => "RequestGuard",
            BlockSource.DownloadGuard => "DownloadGuard",
            _ => entry.Source.ToString(),
        };

        return entry.Kind switch
        {
            BlockKind.Tracker     => $"Bloqueo de rastreador: '{entry.Host}' aparece en {src}. Suele recopilar tu actividad entre sitios sin tu consentimiento.",
            BlockKind.Malware     => $"Bloqueo de malware: '{entry.Host}' aparece en {src} como sitio malicioso confirmado. Visitarlo podría haber instalado software dañino.",
            BlockKind.Ads         => $"Bloqueo de publicidad: '{entry.Host}' está clasificado como red publicitaria por {src}.",
            BlockKind.Fingerprint => $"Bloqueo de fingerprint: '{entry.Host}' intentó identificar tu navegador con técnicas detectadas por {src}.",
            BlockKind.Script      => $"Bloqueo de script: '{entry.Host}' sirvió un script con patrón sospechoso según {src}.",
            BlockKind.Social      => $"Bloqueo de widget social: '{entry.Host}' carga botones de redes sociales que rastrean visitas. Detectado por {src}.",
            _                     => $"Solicitud bloqueada por {src}. Categoría: {entry.Kind}. URL: {entry.ShortPath}.",
        };
    }

    private static string CacheKey(BlockEntry entry) => $"{entry.Host}|{entry.Kind}|{entry.SubKind}";

    private void Store(string key, string text)
    {
        lock (_lock) _cache[key] = (text, DateTime.UtcNow);
    }

    /// <summary>Test helper — number of cached explanations.</summary>
    public int CacheCount { get { lock (_lock) return _cache.Count; } }

    /// <summary>Test helper — wipe cache between scenarios.</summary>
    public void ClearCache() { lock (_lock) _cache.Clear(); }
}
