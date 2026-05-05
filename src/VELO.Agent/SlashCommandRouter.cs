using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Agent;

/// <summary>
/// Phase 3 / Sprint 6 — Parses VeloAgent slash commands like
/// <c>/tldr</c>, <c>/traducir en</c>, <c>/preguntas 5</c> and dispatches
/// them to <see cref="AIContextActions"/> against the active page content.
/// Unknown or malformed commands return <c>null</c> so the caller falls
/// back to general chat. The router is pure (no WPF, no I/O beyond the
/// adapter delegate) so it's directly unit-testable.
/// </summary>
public sealed class SlashCommandRouter
{
    /// <summary>Function that returns the active page's plain-text content (Reader-Mode-extracted).</summary>
    public Func<string>? PageContentProvider { get; set; }

    /// <summary>Default target language for <c>/traducir</c> when none supplied (UI locale).</summary>
    public string DefaultTranslateLang { get; set; } = "es";

    private readonly AIContextActions _actions;
    private readonly ILogger<SlashCommandRouter> _logger;

    public SlashCommandRouter(AIContextActions actions, ILogger<SlashCommandRouter>? logger = null)
    {
        _actions = actions;
        _logger  = logger ?? NullLogger<SlashCommandRouter>.Instance;
    }

    /// <summary>
    /// True when <paramref name="input"/> begins with a recognised slash
    /// command. Used by the chat input to render command suggestions
    /// without committing to dispatch.
    /// </summary>
    public static bool IsSlashCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();
        return t.Length > 1 && t[0] == '/';
    }

    /// <summary>
    /// Parses <paramref name="input"/> as a slash command and dispatches.
    /// Returns the response text, or <c>null</c> when no recognised
    /// command was found (caller should treat as free-form chat).
    /// </summary>
    public async Task<string?> TryDispatchAsync(string input, CancellationToken ct = default)
    {
        var (cmd, args) = ParseInput(input);
        if (cmd == null) return null;

        var content = PageContentProvider?.Invoke() ?? "";

        switch (cmd)
        {
            case "/tldr":
            case "/resumen":
            {
                var lines = ParseInt(args.FirstOrDefault(), fallback: 5);
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                return await _actions.SummarizeAsync(content, lines, ct).ConfigureAwait(false);
            }

            case "/explicar":
            case "/eli5":
            {
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                return await _actions.SimplifyAsync(content, ct).ConfigureAwait(false);
            }

            case "/preguntas":
            case "/questions":
            {
                var n = ParseInt(args.FirstOrDefault(), fallback: 5);
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                return await GenerateQuestionsAsync(content, n, ct).ConfigureAwait(false);
            }

            case "/traducir":
            case "/translate":
            {
                var lang = !string.IsNullOrWhiteSpace(args.FirstOrDefault())
                    ? args[0].Trim().ToLowerInvariant()
                    : DefaultTranslateLang;
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                return await _actions.TranslateAsync(content, lang, ct).ConfigureAwait(false);
            }

            case "/buscar":
            case "/find":
            {
                var query = string.Join(' ', args).Trim();
                if (string.IsNullOrEmpty(query))
                    return "Uso: /buscar <texto>";
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                return SearchInPage(content, query);
            }

            case "/extraer":
            case "/extract":
            {
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                var kindArg = (args.FirstOrDefault() ?? "").ToLowerInvariant();
                ExtractionKind? kind = kindArg switch
                {
                    "emails"  or "email"            => ExtractionKind.Emails,
                    "links"   or "urls"   or "url"  => ExtractionKind.Links,
                    "phones"  or "phone"  or "tel"  => ExtractionKind.Phones,
                    _ => null,
                };
                if (kind is null)
                    return "Uso: /extraer emails | links | phones";
                var items = await _actions.ExtractAsync(content, kind.Value, ct).ConfigureAwait(false);
                return items.Count == 0
                    ? $"No se encontraron {kindArg} en la página."
                    : string.Join("\n", items.Select(i => $"• {i.Value}"));
            }

            case "/analizar":
            case "/analyze":
            {
                if (string.IsNullOrWhiteSpace(content)) return EmptyPage();
                return await AnalyzeAsync(content, ct).ConfigureAwait(false);
            }

            case "/help":
            case "/ayuda":
                return HelpText;

            default:
                _logger.LogDebug("Unknown slash command {Cmd}", cmd);
                return null; // caller falls back to free-form chat
        }
    }

    // ── Helpers (public-static where it helps tests) ───────────────────────

    /// <summary>Splits a chat-input line into (command, args[]) — null command when not a slash command.</summary>
    public static (string? Cmd, string[] Args) ParseInput(string raw)
    {
        if (!IsSlashCommand(raw)) return (null, []);

        var trimmed = raw.TrimStart();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
            return (trimmed.ToLowerInvariant(), []);

        var cmd  = trimmed[..firstSpace].ToLowerInvariant();
        var rest = trimmed[(firstSpace + 1)..].Trim();
        if (string.IsNullOrEmpty(rest)) return (cmd, []);

        // Split the rest by whitespace — simple, good enough for our 1-2 arg commands.
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (cmd, parts);
    }

    private static int ParseInt(string? s, int fallback)
        => int.TryParse(s, out var n) && n > 0 ? n : fallback;

    private static string EmptyPage()
        => "No hay contenido legible en la página activa. Intenta abrir el modo lectura primero.";

    /// <summary>Case-insensitive substring search with ±60 char context windows.</summary>
    public static string SearchInPage(string content, string query)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(query)) return "";

        var hits = new List<string>();
        int idx = 0;
        while (idx < content.Length && hits.Count < 5)
        {
            var found = content.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            var winStart = Math.Max(0, found - 60);
            var winEnd   = Math.Min(content.Length, found + query.Length + 60);
            var snippet  = content[winStart..winEnd]
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();
            hits.Add($"…{snippet}…");
            idx = found + query.Length;
        }
        return hits.Count == 0
            ? $"No se encontró \"{query}\" en la página."
            : $"{hits.Count} coincidencia(s):\n" + string.Join("\n\n", hits);
    }

    private async Task<string> GenerateQuestionsAsync(string content, int n, CancellationToken ct)
    {
        if (_actions.ChatDelegate is null) return EmptyPage();
        var snippet = content.Length > 4000 ? content[..4000] : content;
        var prompt = $"Genera exactamente {n} preguntas de comprensión sobre el siguiente texto. " +
                     "Numéralas 1, 2, 3… No respondas, solo formula las preguntas.\n\n" + snippet;
        return await _actions.ChatDelegate("", prompt, ct).ConfigureAwait(false);
    }

    private async Task<string> AnalyzeAsync(string content, CancellationToken ct)
    {
        if (_actions.ChatDelegate is null) return EmptyPage();
        var snippet = content.Length > 4000 ? content[..4000] : content;
        var prompt = "Analiza el siguiente texto desde tres ángulos: (1) Sesgo o punto de vista, " +
                     "(2) Tono y registro, (3) Credibilidad de las fuentes citadas. " +
                     "Sé conciso (3-4 frases por punto).\n\n" + snippet;
        return await _actions.ChatDelegate("", prompt, ct).ConfigureAwait(false);
    }

    /// <summary>Localised in <see cref="LocalizationService"/> via key <c>agent.help</c>.</summary>
    public const string HelpText =
        """
        Comandos disponibles:
          /tldr [n]            Resumen de la página activa (n líneas, def. 5)
          /explicar            Explicación en lenguaje simple
          /preguntas [n]       Genera n preguntas de comprensión
          /traducir <lang>     Traduce la página (ej. /traducir en)
          /buscar <texto>      Busca texto en la página
          /extraer <tipo>      tipo = emails | links | phones | code
          /analizar            Sesgo, tono y credibilidad de fuentes
          /help                Esta ayuda
        """;
}
