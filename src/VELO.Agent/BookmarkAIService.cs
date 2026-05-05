using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Agent;

/// <summary>
/// Phase 3 / Sprint 9B — AI-assisted bookmark organisation. Two surfaces:
///
/// <list type="bullet">
/// <item><b>Auto-tag on save</b>: when the user bookmarks a page, this
/// service derives 3-5 short topical tags from the page title + URL +
/// (optional) extracted content. Tags get stored alongside the bookmark
/// and turn into facet filters / semantic anchors later.</item>
///
/// <item><b>Semantic rerank on search</b>: the user's existing lexical
/// search returns the top-N candidates by token match; this service then
/// asks the model to reorder them by intent ("find that article about
/// React Server Components I bookmarked"). Cheap, model-capable, and
/// requires no embedding infrastructure.</item>
/// </list>
///
/// Design notes:
/// • Pure (no I/O beyond ChatDelegate). DB I/O lives in BookmarkRepository.
/// • Failure-safe: tag generation falls back to empty list, rerank falls
///   back to the input order. Never throws on the user's hot path.
/// • Token-budgeted: candidates are clipped to <see cref="MaxRerankCandidates"/>
///   before being sent to the model.
/// </summary>
public sealed class BookmarkAIService
{
    public sealed record Candidate(string Id, string Title, string Url, string? Tags);

    /// <summary>(systemPrompt, userPrompt, ct) → reply.</summary>
    public Func<string, string, CancellationToken, Task<string>>? ChatDelegate { get; set; }

    /// <summary>Maximum tags returned by <see cref="GenerateTagsAsync"/>. Caps model creativity.</summary>
    public int MaxTags { get; set; } = 5;

    /// <summary>Maximum chars of page content forwarded to the tagger. Keeps small models fast.</summary>
    public int MaxContentChars { get; set; } = 1500;

    /// <summary>Maximum candidates passed to the rerank model. Above this, fall back to lexical order.</summary>
    public int MaxRerankCandidates { get; set; } = 20;

    private readonly ILogger<BookmarkAIService> _logger;

    public BookmarkAIService(ILogger<BookmarkAIService>? logger = null)
    {
        _logger = logger ?? NullLogger<BookmarkAIService>.Instance;
    }

    // ── Auto-tag ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates 3-5 short topical tags for a bookmark. Returns an empty
    /// list when no adapter is wired or the model reply is unparseable —
    /// the caller saves the bookmark either way.
    /// </summary>
    public async Task<IReadOnlyList<string>> GenerateTagsAsync(
        string title,
        string url,
        string? content = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url))
            return Array.Empty<string>();
        if (ChatDelegate is null) return Array.Empty<string>();

        var (sys, user) = BuildTagPrompt(title, url, content, MaxTags, MaxContentChars);
        try
        {
            var reply = await ChatDelegate(sys, user, ct).ConfigureAwait(false);
            return ParseTags(reply, MaxTags);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tag generation failed for {Url}", url);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Builds the (system, user) prompt for tag generation. The system
    /// prompt asks for a single line of comma-separated tags so parsing
    /// is trivial across small and large models.
    /// </summary>
    public static (string System, string User) BuildTagPrompt(
        string title, string url, string? content, int maxTags, int maxContentChars)
    {
        var system =
            $"Generate {maxTags} short topical tags for a bookmarked page. " +
            "Output ONE LINE of comma-separated tags. Tags must be 1-3 words " +
            "each, lowercase, no markdown, no numbering, no preamble. Prefer " +
            "concrete topics ('react server components') over generic words " +
            "('web', 'tech'). If unsure, return an empty line.";

        var snippet = content ?? "";
        if (snippet.Length > maxContentChars) snippet = snippet[..maxContentChars];

        var user =
            $"title: {title}\n" +
            $"url: {url}\n" +
            (string.IsNullOrEmpty(snippet) ? "" : $"content: {snippet}");

        return (system, user);
    }

    /// <summary>
    /// Parses a comma-separated tag line. Robust to surrounding whitespace,
    /// numeric prefixes, markdown bullets, and quoted entries.
    /// </summary>
    public static IReadOnlyList<string> ParseTags(string reply, int maxTags)
    {
        if (string.IsNullOrWhiteSpace(reply)) return Array.Empty<string>();

        // Pick the first non-empty line that contains a comma OR is the only line.
        var line = reply
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('`', '*', '-'))
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrEmpty(line)) return Array.Empty<string>();

        var parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var tags = new List<string>(maxTags);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in parts)
        {
            var clean = raw
                .Trim('"', '\'', '*', '`', '-', '•', '·', '#')
                .Trim();

            // Strip leading "1." or "2)" numbering, common in small models.
            int i = 0;
            while (i < clean.Length && (char.IsDigit(clean[i]) || clean[i] == '.' || clean[i] == ')')) i++;
            clean = clean[i..].TrimStart();

            if (clean.Length == 0 || clean.Length > 40) continue;
            clean = clean.ToLowerInvariant();

            if (seen.Add(clean))
            {
                tags.Add(clean);
                if (tags.Count >= maxTags) break;
            }
        }
        return tags;
    }

    // ── Semantic rerank ──────────────────────────────────────────────────

    /// <summary>
    /// Reorders <paramref name="candidates"/> by relevance to <paramref name="query"/>
    /// using a single model call. Falls back to the original order when
    /// no adapter is wired, the candidate count is over budget, or the
    /// model reply can't be parsed. Never throws.
    /// </summary>
    public async Task<IReadOnlyList<Candidate>> RerankAsync(
        string query,
        IReadOnlyList<Candidate> candidates,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
            return candidates;
        if (ChatDelegate is null || candidates.Count > MaxRerankCandidates)
            return candidates;

        var (sys, user) = BuildRerankPrompt(query, candidates);
        try
        {
            var reply = await ChatDelegate(sys, user, ct).ConfigureAwait(false);
            var ordering = ParseRerankReply(reply, candidates.Count);
            if (ordering.Count == 0) return candidates;
            return ApplyOrdering(candidates, ordering);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rerank failed for query '{Query}'", query);
            return candidates;
        }
    }

    /// <summary>
    /// Builds the rerank prompt. Each candidate is numbered 1..N so the
    /// model can return an ordering as a comma-separated list of indices.
    /// </summary>
    public static (string System, string User) BuildRerankPrompt(
        string query, IReadOnlyList<Candidate> candidates)
    {
        const string system =
            "You rerank bookmarked pages by how well each matches a search " +
            "intent. Output ONE LINE: comma-separated indices (1-based) in " +
            "order of best-match-first. Include every input index exactly " +
            "once. No preamble, no explanation, no markdown.";

        var sb = new System.Text.StringBuilder();
        sb.Append("query: ").Append(query).Append('\n').Append('\n');
        sb.Append("candidates:\n");
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.Append(i + 1).Append(". ");
            sb.Append(string.IsNullOrEmpty(c.Title) ? c.Url : c.Title);
            if (!string.IsNullOrWhiteSpace(c.Tags))
                sb.Append(" [").Append(c.Tags).Append(']');
            sb.Append('\n');
        }
        return (system, sb.ToString());
    }

    /// <summary>
    /// Parses a model reply containing 1-based indices into the candidate
    /// list. Tolerates surrounding text. Returns empty list when the
    /// parse can't recover a complete permutation (caller falls back).
    /// </summary>
    public static IReadOnlyList<int> ParseRerankReply(string reply, int candidateCount)
    {
        if (string.IsNullOrWhiteSpace(reply) || candidateCount == 0)
            return Array.Empty<int>();

        // Find the first line that looks like a comma- or space-separated
        // list of integers. Models sometimes prepend "Sure, here you go:".
        var line = reply
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('`', '*'))
            .FirstOrDefault(l => l.Any(char.IsDigit));

        if (string.IsNullOrEmpty(line)) return Array.Empty<int>();

        var nums = new List<int>();
        var seen = new HashSet<int>();
        var token = new System.Text.StringBuilder();
        void Flush()
        {
            if (token.Length == 0) return;
            if (int.TryParse(token.ToString(), out var n) && n >= 1 && n <= candidateCount && seen.Add(n))
                nums.Add(n);
            token.Clear();
        }
        foreach (var ch in line)
        {
            if (char.IsDigit(ch)) token.Append(ch);
            else                  Flush();
        }
        Flush();

        // Require at least half the candidates to be parsable; otherwise
        // we don't trust the model's intent — caller falls back.
        return nums.Count >= Math.Max(1, candidateCount / 2)
            ? nums
            : Array.Empty<int>();
    }

    private static IReadOnlyList<Candidate> ApplyOrdering(
        IReadOnlyList<Candidate> candidates, IReadOnlyList<int> ordering)
    {
        var result = new List<Candidate>(candidates.Count);
        foreach (var idx in ordering)
            result.Add(candidates[idx - 1]);

        // Append any candidates the model omitted in original order so the
        // user never loses a result silently.
        var seen = new HashSet<int>(ordering);
        for (int i = 0; i < candidates.Count; i++)
            if (!seen.Contains(i + 1))
                result.Add(candidates[i]);

        return result;
    }
}
