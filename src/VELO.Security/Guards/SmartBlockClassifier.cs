using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Security.Guards;

/// <summary>
/// Phase 3 / Sprint 8A — Local-LLM classifier for sub-resource requests
/// that don't match any static blocklist entry. The static
/// <see cref="RequestGuard"/> handles first-pass (EasyPrivacy + Golden List
/// + heuristics); this classifier is the second pass that catches
/// previously-unknown trackers, ad networks and analytics endpoints.
///
/// Design constraints (matters when this runs on every page-load):
///
/// 1. <b>Async, never blocks the request.</b> First request to an unknown
///    host: allow + queue for classification. Subsequent requests to the
///    same host within the cache TTL get the cached verdict instantly.
///    Per-host cache means a single classification covers all future
///    requests from that domain in this session.
///
/// 2. <b>Confidence-gated.</b> Only Block at confidence ≥
///    <see cref="BlockConfidenceThreshold"/>. Below that the verdict is
///    Allow + reason logged so the user (or a future heuristic) can
///    review. False positives erode trust faster than a missed tracker
///    erodes privacy.
///
/// 3. <b>Budgeted.</b> The classifier honors a per-minute call budget
///    via <see cref="MaxCallsPerMinute"/> so a page that loads 200
///    third-party domains doesn't melt the local model. Excess calls
///    return Allow + budget-exhausted reason.
///
/// 4. <b>Pure (no I/O beyond the chat delegate).</b> Tests live in
///    <c>SmartBlockClassifierTests</c>.
/// </summary>
public sealed class SmartBlockClassifier
{
    public enum Verdict { Allow, Block }

    public sealed record Result(
        Verdict Verdict,
        double  Confidence,
        string  Reason,
        bool    FromCache);

    /// <summary>Adapter delegate: <c>(systemPrompt, userPrompt, ct) =&gt; reply</c>.</summary>
    public Func<string, string, CancellationToken, Task<string>>? ChatDelegate { get; set; }

    /// <summary>Confidence at or above which the classifier returns Block. 0.0 .. 1.0.</summary>
    public double BlockConfidenceThreshold { get; set; } = 0.85;

    /// <summary>How long a host's verdict stays cached before we re-classify.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Per-minute classification budget. 0 disables (unlimited).</summary>
    public int MaxCallsPerMinute { get; set; } = 30;

    private readonly Dictionary<string, (Result R, DateTime At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recentCalls = new();
    private readonly ILogger<SmartBlockClassifier> _logger;
    private readonly object _lock = new();

    public SmartBlockClassifier(ILogger<SmartBlockClassifier>? logger = null)
    {
        _logger = logger ?? NullLogger<SmartBlockClassifier>.Instance;
    }

    /// <summary>
    /// Returns the classifier verdict for <paramref name="host"/>. When the
    /// host is in cache, returns immediately (FromCache=true). Otherwise
    /// invokes <see cref="ChatDelegate"/> with a tracker-classification
    /// prompt. When the chat delegate is null or the budget is exhausted,
    /// returns Allow with an explanatory reason and does not cache the
    /// verdict.
    /// </summary>
    public async Task<Result> ClassifyAsync(
        string host,
        string resourceType,
        string referrerHost,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new Result(Verdict.Allow, 0, "empty host", FromCache: false);

        // Cache lookup is the hot path — most page loads should hit it.
        lock (_lock)
        {
            if (_cache.TryGetValue(host, out var entry) &&
                (DateTime.UtcNow - entry.At) < CacheTtl)
            {
                return entry.R with { FromCache = true };
            }
        }

        if (ChatDelegate is null)
        {
            return new Result(Verdict.Allow, 0,
                "smartblock disabled (no chat adapter wired)",
                FromCache: false);
        }

        // Budget check — never call the model more than N times per minute.
        if (!ConsumeBudget())
        {
            _logger.LogDebug("SmartBlock budget exhausted; allowing {Host} without classification", host);
            return new Result(Verdict.Allow, 0,
                "classifier budget exhausted",
                FromCache: false);
        }

        Result result;
        try
        {
            var (system, user) = BuildPrompt(host, resourceType, referrerHost);
            var reply = await ChatDelegate(system, user, ct).ConfigureAwait(false);
            result = ParseReply(reply);
            _logger.LogDebug("SmartBlock classified {Host} → {Verdict} (conf {Conf:F2}): {Reason}",
                host, result.Verdict, result.Confidence, result.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SmartBlock classifier failed for {Host}; allowing", host);
            return new Result(Verdict.Allow, 0, $"classifier error: {ex.Message}", FromCache: false);
        }

        // Cache only verdicts the model actually produced.
        lock (_lock) { _cache[host] = (result, DateTime.UtcNow); }
        return result;
    }

    /// <summary>Test helper — clears the cache.</summary>
    public void ClearCache()
    {
        lock (_lock) { _cache.Clear(); _recentCalls.Clear(); }
    }

    /// <summary>Test helper — returns the current cache size.</summary>
    public int CacheCount
    {
        get { lock (_lock) return _cache.Count; }
    }

    // ── Pure helpers (public-static where useful for tests) ────────────────

    /// <summary>
    /// Builds the (system, user) prompt pair sent to the LLM. The system
    /// prompt is intentionally compact so a small local model (1-3B) can
    /// follow it reliably. The expected reply format is one line:
    /// <c>VERDICT|CONFIDENCE|REASON</c>.
    /// </summary>
    public static (string System, string User) BuildPrompt(
        string host, string resourceType, string referrerHost)
    {
        const string system =
            "You classify network requests as TRACKER or LEGITIMATE. " +
            "Trackers include: analytics, ad networks, beacon/pixel servers, " +
            "session-replay, fingerprinting scripts, third-party data brokers. " +
            "Legitimate includes: CDNs serving the page's own assets, payment " +
            "processors, embedded media, fonts, the site's own subdomains. " +
            "Reply on ONE line in the format VERDICT|CONFIDENCE|REASON where " +
            "VERDICT is BLOCK or ALLOW, CONFIDENCE is a number 0.0-1.0, and " +
            "REASON is a 5-12 word phrase. No preamble, no markdown.";

        var user =
            $"host: {host}\n" +
            $"resource: {resourceType}\n" +
            $"referrer: {referrerHost}";

        return (system, user);
    }

    /// <summary>
    /// Parses a model reply of shape <c>VERDICT|CONFIDENCE|REASON</c>.
    /// Robust to whitespace, casing and markdown leakage. Defaults to
    /// Allow with confidence 0 when parsing fails — better to under-block
    /// than over-block on a malformed reply.
    /// </summary>
    public Result ParseReply(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return new Result(Verdict.Allow, 0, "empty model reply", FromCache: false);

        // Take the first non-empty line — small models sometimes prepend a thought.
        var line = reply
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('`', '*'))
            .FirstOrDefault(l => l.Contains('|'));

        if (string.IsNullOrEmpty(line))
            return new Result(Verdict.Allow, 0, "no pipe-separated line in reply", FromCache: false);

        var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return new Result(Verdict.Allow, 0, "malformed reply", FromCache: false);

        var verdictWord = parts[0].ToUpperInvariant();
        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var conf))
            conf = 0;
        conf = Math.Clamp(conf, 0, 1);

        var reason = parts.Length >= 3 ? parts[2] : "";

        // Confidence-gate: only Block at threshold-or-above. Below threshold
        // we Allow even when the model said BLOCK — caller can still log it.
        var verdict = verdictWord.StartsWith("BLOCK") && conf >= BlockConfidenceThreshold
            ? Verdict.Block
            : Verdict.Allow;

        return new Result(verdict, conf, reason, FromCache: false);
    }

    private bool ConsumeBudget()
    {
        if (MaxCallsPerMinute <= 0) return true;

        var now = DateTime.UtcNow;
        lock (_lock)
        {
            // Drop entries older than 1 minute.
            while (_recentCalls.Count > 0 && (now - _recentCalls.Peek()) > TimeSpan.FromMinutes(1))
                _recentCalls.Dequeue();

            if (_recentCalls.Count >= MaxCallsPerMinute) return false;
            _recentCalls.Enqueue(now);
            return true;
        }
    }
}
