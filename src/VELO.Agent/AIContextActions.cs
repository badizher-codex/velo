using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VELO.Agent;

public enum ExtractionKind { Links, Emails, Phones }

public record Extraction(ExtractionKind Kind, string Value);

/// <summary>
/// Phase 3 / Sprint 1D — Context-menu AI actions. Backs the 🤖 IA submenu
/// added in Sprint 1E. Each method invokes the host-supplied
/// <see cref="ChatDelegate"/> with a tailored prompt and returns plain text;
/// <see cref="ExtractAsync"/> is regex-only (no LLM round-trip).
///
/// Architectural note: same pattern as BlockExplanationService — the class
/// does NOT pick an adapter itself. The host wires <see cref="ChatDelegate"/>
/// to whatever backend is active (local LLamaSharp, Ollama, Claude). That
/// keeps this assembly free from adapter-specific code paths.
/// </summary>
public class AIContextActions(ILogger<AIContextActions>? logger = null)
{
    private readonly ILogger<AIContextActions>? _logger = logger;

    /// <summary>
    /// (systemPrompt, userPrompt, ct) → reply text.
    /// Set by the host on app start. When null, methods return a tagged
    /// fallback string so the UI shows something instead of crashing.
    /// </summary>
    public Func<string, string, CancellationToken, Task<string>>? ChatDelegate { get; set; }

    /// <summary>True if the configured adapter can ingest image bytes.</summary>
    public bool SupportsVision { get; set; }

    /// <summary>Display name of the active adapter, used by AIResultWindow chip.</summary>
    public string AdapterName { get; set; } = "Offline";

    /// <summary>Above this character count, SummarizeAsync splits + recurses.</summary>
    public int MapReduceThresholdChars { get; set; } = 4000;

    /// <summary>Chunk size when splitting for map-reduce.</summary>
    public int ChunkSizeChars { get; set; } = 2000;

    // ── 1. Explain ─────────────────────────────────────────────────────

    public Task<string> ExplainAsync(string text, string targetLang, CancellationToken ct = default)
    {
        var system =
            "You are an expert tutor. Explain the term or concept in 3-4 sentences " +
            "for someone who understands tech but isn't a specialist. If the input is " +
            "a long phrase, identify the central concept and explain that. Reply in " +
            $"{LanguageName(targetLang)}. Don't invent — say \"I have no reliable " +
            "information about X\" if unsure.";
        return Chat(system, $"Text: \"{text}\"", ct, fallback: $"[Explain offline] {text}");
    }

    // ── 2. Summarize (with map-reduce) ─────────────────────────────────

    public async Task<string> SummarizeAsync(string content, int maxLines = 3, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";

        // Map-reduce when too long: split, summarise each chunk, then summarise
        // the joined summaries. Keeps small models from blowing the context window.
        if (content.Length > MapReduceThresholdChars)
        {
            var chunks       = SplitIntoChunks(content, ChunkSizeChars);
            var partials     = new List<string>();
            foreach (var c in chunks)
            {
                ct.ThrowIfCancellationRequested();
                var partial = await SummarizeOnceAsync(c, maxLines: 5, ct).ConfigureAwait(false);
                partials.Add(partial);
            }
            var joined = string.Join("\n\n", partials);
            return await SummarizeOnceAsync(joined, maxLines, ct).ConfigureAwait(false);
        }

        return await SummarizeOnceAsync(content, maxLines, ct).ConfigureAwait(false);
    }

    private Task<string> SummarizeOnceAsync(string content, int maxLines, CancellationToken ct)
    {
        var system =
            $"Summarise the following content in {maxLines} lines or fewer. Use " +
            "bullet points if helpful. Keep names, dates and numbers intact. Don't " +
            "invent details that aren't present.";
        return Chat(system, content, ct, fallback: TruncatePreview(content, 200));
    }

    /// <summary>Test helper — splits text into chunks at sentence-ish boundaries.</summary>
    public static IReadOnlyList<string> SplitIntoChunks(string content, int chunkSize)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var result = new List<string>();
        int i = 0;
        while (i < content.Length)
        {
            int len = Math.Min(chunkSize, content.Length - i);
            // Try to break on a paragraph or sentence boundary near the end of
            // the chunk so we don't slice mid-word.
            if (i + len < content.Length)
            {
                int breakAt = content.LastIndexOfAny(['.', '\n', '!', '?'], i + len - 1, Math.Min(200, len));
                if (breakAt > i) len = breakAt - i + 1;
            }
            result.Add(content.Substring(i, len).Trim());
            i += len;
        }
        return result;
    }

    // ── 3. Translate ───────────────────────────────────────────────────

    public Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct = default)
    {
        var sourceLang = DetectLanguage(text);
        var system =
            $"Translate the text to {LanguageName(targetLang)}. Source language is " +
            $"{LanguageName(sourceLang)} (auto-detected). Keep proper nouns, code, " +
            "URLs and numbers as-is. Output only the translation, no preamble.";
        return Chat(system, text, ct, fallback: $"[Translate offline] {text}");
    }

    /// <summary>Test helper — returns the detected source-language code (es/en/de/fr/pt/it/unknown).</summary>
    public static string DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "unknown";
        var sample = text.ToLowerInvariant();

        // Tiny stopword-frequency heuristic. Good enough for the panel's
        // language indicator — production-grade detection would use ICU.
        var hits = new Dictionary<string, int>
        {
            ["es"] = CountWords(sample, ["el", "la", "de", "que", "y", "en", "un", "una", "los", "las", "es", "del", "para", "por", "con"]),
            ["en"] = CountWords(sample, ["the", "and", "of", "to", "a", "in", "is", "it", "you", "that", "for", "with", "on", "this"]),
            ["fr"] = CountWords(sample, ["le", "la", "de", "et", "à", "les", "que", "des", "est", "un", "une", "pour", "avec", "sur"]),
            ["de"] = CountWords(sample, ["der", "die", "das", "und", "ist", "ein", "eine", "den", "von", "zu", "mit", "auf", "für"]),
            ["pt"] = CountWords(sample, ["o", "a", "de", "que", "e", "do", "da", "em", "um", "uma", "para", "com", "não", "no"]),
            ["it"] = CountWords(sample, ["il", "la", "di", "che", "e", "un", "una", "in", "per", "con", "non", "del", "della"]),
        };

        var top = hits.OrderByDescending(kv => kv.Value).First();
        return top.Value > 0 ? top.Key : "unknown";
    }

    private static int CountWords(string text, string[] words)
    {
        int n = 0;
        foreach (var w in words)
        {
            int idx = 0;
            while ((idx = text.IndexOf(w, idx, StringComparison.Ordinal)) >= 0)
            {
                bool leftOk  = idx == 0 || !char.IsLetter(text[idx - 1]);
                bool rightOk = idx + w.Length == text.Length || !char.IsLetter(text[idx + w.Length]);
                if (leftOk && rightOk) n++;
                idx += w.Length;
            }
        }
        return n;
    }

    // ── 4. FactCheck ───────────────────────────────────────────────────

    public async Task<string> FactCheckAsync(string claim, CancellationToken ct = default)
    {
        var system =
            "You assess the truthfulness of a claim. Reply with: 1) a short verdict " +
            "(supported/contested/likely false/insufficient info), 2) one or two " +
            "sentences of reasoning. Be conservative — say \"insufficient info\" if " +
            "you don't have reliable knowledge. Reply in the same language as the claim.";
        var body = await Chat(system, $"Claim: \"{claim}\"", ct, fallback: $"[FactCheck offline] {claim}").ConfigureAwait(false);

        // Per § 3.4 always include the disclaimer so users don't take a model
        // verdict as authoritative. Static so localisation stays in the host.
        return body + "\n\n" + Disclaimer;
    }

    public const string Disclaimer =
        "⚠ Verificación generada por IA. No es asesoramiento legal, médico ni financiero. " +
        "Cruza con fuentes confiables antes de actuar. (AI-generated fact-check; not professional advice.)";

    // ── 5. Define ──────────────────────────────────────────────────────

    public Task<string> DefineAsync(string term, CancellationToken ct = default)
    {
        var system =
            "Define the given term in 1-2 sentences plus one concrete example. Reply " +
            "in the same language as the term. Don't invent — say \"unknown term\" if " +
            "you don't have reliable knowledge.";
        return Chat(system, term, ct, fallback: $"[Define offline] {term}");
    }

    // ── 6. Simplify (ELI5) ─────────────────────────────────────────────

    public Task<string> SimplifyAsync(string text, CancellationToken ct = default)
    {
        var system =
            "Rewrite the text as if explaining to a 5-year-old. Use everyday words, " +
            "concrete analogies, no jargon. Keep it short (3-5 sentences max). Reply " +
            "in the same language as the input.";
        return Chat(system, text, ct, fallback: $"[Simplify offline] {text}");
    }

    // ── 7. Extract (regex-only, no LLM) ────────────────────────────────

    public Task<IReadOnlyList<Extraction>> ExtractAsync(string text, ExtractionKind kind, CancellationToken ct = default)
    {
        IReadOnlyList<Extraction> result = kind switch
        {
            ExtractionKind.Links  => ExtractLinks(text),
            ExtractionKind.Emails => ExtractEmails(text),
            ExtractionKind.Phones => ExtractPhones(text),
            _ => Array.Empty<Extraction>(),
        };
        return Task.FromResult(result);
    }

    private static readonly Regex _linkRx  = new(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _emailRx = new(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Phone heuristic: international (+CC ...) or 7+ digits with optional separators.
    // Loose by design — false positives in code are tolerable; missing numbers is worse.
    private static readonly Regex _phoneRx = new(@"(?:\+\d{1,3}[\s\-.]?)?(?:\(?\d{2,4}\)?[\s\-.]?){2,}\d{2,4}", RegexOptions.Compiled);

    private static List<Extraction> ExtractLinks(string text)
        => _linkRx.Matches(text).Select(m => new Extraction(ExtractionKind.Links, m.Value.TrimEnd('.', ',', ')'))).ToList();

    private static List<Extraction> ExtractEmails(string text)
        => _emailRx.Matches(text).Select(m => new Extraction(ExtractionKind.Emails, m.Value)).ToList();

    private static List<Extraction> ExtractPhones(string text)
        => _phoneRx.Matches(text)
            .Select(m => m.Value.Trim())
            .Where(v => v.Count(char.IsDigit) >= 7)
            .Select(v => new Extraction(ExtractionKind.Phones, v))
            .ToList();

    // ── 8. DescribeImage ───────────────────────────────────────────────

    public Task<string> DescribeImageAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (!SupportsVision)
            return Task.FromResult(VisionUnsupportedMessage);
        if (imageBytes.Length > 5 * 1024 * 1024)
            return Task.FromResult("[Image too large — limit is 5 MB]");

        // The actual image-to-text path lives in the host; without a vision
        // adapter we can't synthesise a description. Return a placeholder so
        // the UI shows something useful.
        var system = "Describe the image in 2-3 sentences. Identify any text, main " +
                     "objects, and context. Don't invent details.";
        var b64    = Convert.ToBase64String(imageBytes);
        return Chat(system, $"[image_base64 length={b64.Length}]", ct,
                    fallback: "[DescribeImage offline]");
    }

    public const string VisionUnsupportedMessage =
        "Tu adaptador IA actual no soporta imágenes. Activa Claude en Settings o usa un modelo local con visión (llava, moondream).";

    // ── 9. OCR ─────────────────────────────────────────────────────────

    public Task<string> OcrAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (!SupportsVision)
            return Task.FromResult(VisionUnsupportedMessage);

        var system = "Extract any visible text from the image, preserving line breaks. " +
                     "Output only the extracted text, no commentary.";
        return Chat(system, $"[image_base64 length={imageBytes.Length}]", ct,
                    fallback: "[OCR offline]");
    }

    // ── 10. DeepSearch ─────────────────────────────────────────────────

    public Task<string> DeepSearchAsync(string query, CancellationToken ct = default)
    {
        // MVP: ask the model for a structured search outline. Full DDG-results +
        // scrape-and-merge flow is a Sprint-1E follow-up since it needs
        // network calls + UI presentation.
        var system =
            "The user wants a deep dive on the query. Produce: 1) a 1-sentence " +
            "framing of what they likely want; 2) 3-5 sub-questions worth searching; " +
            "3) suggested keywords or operators. Don't return URLs you don't know.";
        return Chat(system, query, ct, fallback: $"[DeepSearch offline] {query}");
    }

    // ── Internals ──────────────────────────────────────────────────────

    private async Task<string> Chat(string system, string user, CancellationToken ct, string fallback)
    {
        if (ChatDelegate == null)
        {
            _logger?.LogDebug("AIContextActions: no ChatDelegate wired — returning fallback");
            return fallback;
        }
        try
        {
            return await ChatDelegate(system, user, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AIContextActions: chat failed");
            return fallback;
        }
    }

    private static string TruncatePreview(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static string LanguageName(string code) => code switch
    {
        "es" => "Spanish",
        "en" => "English",
        "fr" => "French",
        "de" => "German",
        "pt" => "Portuguese",
        "it" => "Italian",
        "zh" => "Chinese",
        "ru" => "Russian",
        "ja" => "Japanese",
        _    => "the user's language",
    };
}
