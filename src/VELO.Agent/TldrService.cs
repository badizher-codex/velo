using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Agent;

/// <summary>
/// Phase 3 / Sprint 9C — Decides which pages deserve a "TL;DR available"
/// badge in the URL bar and produces the summary on demand. Two-phase so
/// the cheap eligibility check runs on every navigation, but the model
/// call only fires when the user actually asks for it.
///
/// Eligibility (pure, instant):
///   • word count above <see cref="MinWords"/> (default 600)
///   • reading time above <see cref="MinReadingMinutes"/> (default 4)
///   • not on a non-article page (login, search results, settings, etc.)
///
/// Summary (LLM, on click):
///   • reuses <see cref="AIContextActions.SummarizeAsync"/> with map-reduce
///   • cached per-URL so re-visits don't re-run the model
///
/// Pure (no I/O beyond ChatDelegate via AIContextActions). UI plumbing
/// (the badge in the URL bar + click handler) is layered on top.
/// </summary>
public sealed class TldrService
{
    public sealed record CachedSummary(string Url, string Text, DateTime AtUtc);

    /// <summary>Below this word count, no badge. Tunable per locale.</summary>
    public int MinWords { get; set; } = 600;

    /// <summary>Below this reading time (minutes, ~225 wpm), no badge.</summary>
    public int MinReadingMinutes { get; set; } = 4;

    /// <summary>Default summary line count (passed to <see cref="AIContextActions.SummarizeAsync"/>).</summary>
    public int SummaryLines { get; set; } = 5;

    /// <summary>Cache TTL — same URL stays summarised this long before re-running.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

    private readonly AIContextActions _actions;
    private readonly Dictionary<string, CachedSummary> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly ILogger<TldrService> _logger;

    public TldrService(AIContextActions actions, ILogger<TldrService>? logger = null)
    {
        _actions = actions;
        _logger  = logger ?? NullLogger<TldrService>.Instance;
    }

    /// <summary>
    /// Cheap eligibility check — does this page deserve a TL;DR badge?
    /// Pure, deterministic, instant. Runs on every navigation.
    /// </summary>
    public bool IsEligible(string url, string content)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(content)) return false;
        if (IsBlacklistedUrl(url)) return false;

        var words = CountWords(content);
        if (words < MinWords) return false;

        var minutes = EstimateReadingMinutes(content);
        return minutes >= MinReadingMinutes;
    }

    /// <summary>
    /// Returns a cached summary for <paramref name="url"/> when one exists
    /// within the TTL window. Returns null otherwise — caller should
    /// invoke <see cref="GenerateSummaryAsync"/> on demand.
    /// </summary>
    public CachedSummary? TryGetCached(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        lock (_lock)
        {
            if (_cache.TryGetValue(url, out var entry) &&
                (DateTime.UtcNow - entry.AtUtc) < CacheTtl)
                return entry;
        }
        return null;
    }

    /// <summary>
    /// Generates (or returns cached) summary text for the given URL +
    /// content. Routes through <see cref="AIContextActions.SummarizeAsync"/>
    /// which already implements map-reduce for long documents. Caches the
    /// result keyed on URL so badge re-clicks within the TTL hit cache.
    /// </summary>
    public async Task<CachedSummary> GenerateSummaryAsync(
        string url, string content, CancellationToken ct = default)
    {
        var cached = TryGetCached(url);
        if (cached != null) return cached;

        if (string.IsNullOrWhiteSpace(content))
            return new CachedSummary(url, "", DateTime.UtcNow);

        try
        {
            var text = await _actions.SummarizeAsync(content, SummaryLines, ct).ConfigureAwait(false);
            var entry = new CachedSummary(url, text ?? "", DateTime.UtcNow);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lock (_lock) { _cache[url] = entry; }
            }
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TL;DR generation failed for {Url}", url);
            return new CachedSummary(url, "", DateTime.UtcNow);
        }
    }

    /// <summary>Test helper.</summary>
    public void ClearCache() { lock (_lock) _cache.Clear(); }

    /// <summary>Test helper.</summary>
    public int CacheCount { get { lock (_lock) return _cache.Count; } }

    // ── Pure helpers ──────────────────────────────────────────────────────

    /// <summary>Counts whitespace-separated words. Cheap, allocation-light.</summary>
    public static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        int count = 0;
        bool inWord = false;
        foreach (var ch in content)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (inWord) { count++; inWord = false; }
            }
            else
            {
                inWord = true;
            }
        }
        if (inWord) count++;
        return count;
    }

    /// <summary>Average adult reading speed ≈ 225 WPM. Returns minutes (rounded down).</summary>
    public static int EstimateReadingMinutes(string content) => CountWords(content) / 225;

    /// <summary>
    /// True for non-article URLs where a TL;DR badge would be confusing
    /// (login, search results, settings, dashboards). Conservative — only
    /// matches obvious patterns; the word-count gate catches the rest.
    /// </summary>
    public static bool IsBlacklistedUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return true;

        // Internal pages
        if (url.StartsWith("velo://", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.StartsWith("about:",  StringComparison.OrdinalIgnoreCase)) return true;
        if (url.StartsWith("file:",   StringComparison.OrdinalIgnoreCase)) return true;

        // Login / settings paths — common shapes across many sites
        var lower = url.ToLowerInvariant();
        if (lower.Contains("/login")    || lower.Contains("/signin"))   return true;
        if (lower.Contains("/signup")   || lower.Contains("/register")) return true;
        if (lower.Contains("/checkout") || lower.Contains("/cart"))     return true;
        if (lower.Contains("/settings") || lower.Contains("/preferences")) return true;
        if (lower.Contains("/search?")  || lower.Contains("/search/"))  return true;
        if (lower.Contains("/dashboard")) return true;

        return false;
    }
}
