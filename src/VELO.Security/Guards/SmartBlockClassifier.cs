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
///
/// 5. <b>Degrades quietly when the model is down (v2.4.62 P2-B).</b> Field
///    logs showed ~30 warnings-with-stack-traces for a <i>single</i> host in
///    200 ms: LM Studio wasn't listening, every call queued behind
///    DirectChatAdapter's in-flight lock until its own 10 s timeout fired,
///    and nothing deduplicated the concurrent requests for the same host.
///    Three mechanisms keep that from happening: <see cref="MaxConcurrentCalls"/>
///    (bail, don't queue), in-flight deduplication (one call per host at a
///    time) and a circuit breaker (<see cref="FailureThreshold"/> consecutive
///    failures → stop calling for <see cref="CircuitOpenDuration"/>).
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

    /// <summary>v2.4.62 — Classifications allowed to be in flight at once. Excess
    /// requests return Allow immediately instead of queueing behind the adapter's
    /// in-flight lock until their own timeout fires. 0 disables the cap.</summary>
    public int MaxConcurrentCalls { get; set; } = 2;

    /// <summary>v2.4.62 — Consecutive failures that open the circuit. 0 disables.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>v2.4.62 — How long the circuit stays open before we try again.</summary>
    public TimeSpan CircuitOpenDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>v2.4.62 — How long a failed classification is remembered, so a
    /// host that just timed out isn't re-attempted on every page load.</summary>
    public TimeSpan FailureCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, (Result R, DateTime At, bool Failed)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<Result>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recentCalls = new();
    private readonly ILogger<SmartBlockClassifier> _logger;
    private readonly object _lock = new();
    private int _consecutiveFailures;
    private DateTime _circuitOpenUntil = DateTime.MinValue;

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
    public Task<Result> ClassifyAsync(
        string host,
        string resourceType,
        string referrerHost,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return Task.FromResult(new Result(Verdict.Allow, 0, "empty host", FromCache: false));

        if (ChatDelegate is null)
        {
            return Task.FromResult(new Result(Verdict.Allow, 0,
                "smartblock disabled (no chat adapter wired)",
                FromCache: false));
        }

        lock (_lock)
        {
            // Cache lookup is the hot path — most page loads should hit it.
            if (TryGetCachedLocked(host, out var cached))
                return Task.FromResult(cached! with { FromCache = true });

            // v2.4.62 — Circuit open: the model isn't answering, so don't pay the
            // timeout again on every request until it has had a chance to recover.
            if (DateTime.UtcNow < _circuitOpenUntil)
                return Task.FromResult(new Result(Verdict.Allow, 0, "classifier circuit open", FromCache: false));

            // v2.4.62 — Same host already being classified: join that call instead
            // of issuing a second one. A single page loading 30 assets from one CDN
            // used to fire 30 identical classifications.
            if (_inFlight.TryGetValue(host, out var pending))
                return pending;

            if (MaxConcurrentCalls > 0 && _inFlight.Count >= MaxConcurrentCalls)
                return Task.FromResult(new Result(Verdict.Allow, 0, "classifier busy", FromCache: false));

            // Budget check — never call the model more than N times per minute.
            if (!ConsumeBudgetLocked())
            {
                _logger.LogDebug("SmartBlock budget exhausted; allowing {Host} without classification", host);
                return Task.FromResult(new Result(Verdict.Allow, 0,
                    "classifier budget exhausted",
                    FromCache: false));
            }

            var task = RunClassificationAsync(host, resourceType, referrerHost, ct);
            // Only track it while it's actually running; a synchronously-completed
            // task (test doubles, immediate failure) is already done here.
            if (!task.IsCompleted) _inFlight[host] = task;
            return task;
        }
    }

    /// <summary>
    /// Runs one classification. Never throws: a failing model must degrade to
    /// Allow, and callers (including the ones joined via in-flight dedup) get a
    /// Result instead of an exception.
    /// </summary>
    private async Task<Result> RunClassificationAsync(
        string host, string resourceType, string referrerHost, CancellationToken ct)
    {
        try
        {
            var (system, user) = BuildPrompt(host, resourceType, referrerHost);
            var reply  = await ChatDelegate!(system, user, ct).ConfigureAwait(false);
            var result = ParseReply(reply);

            _logger.LogDebug("SmartBlock classified {Host} → {Verdict} (conf {Conf:F2}): {Reason}",
                host, result.Verdict, result.Confidence, result.Reason);

            lock (_lock)
            {
                _cache[host] = (result, DateTime.UtcNow, Failed: false);
                _consecutiveFailures = 0;
            }
            return result;
        }
        catch (Exception ex)
        {
            var result = new Result(Verdict.Allow, 0, $"classifier error: {ex.Message}", FromCache: false);
            bool circuitJustOpened;

            lock (_lock)
            {
                // Negative cache: don't re-attempt this host until the TTL expires.
                _cache[host] = (result, DateTime.UtcNow, Failed: true);
                _consecutiveFailures++;
                circuitJustOpened = FailureThreshold > 0
                                 && _consecutiveFailures == FailureThreshold;
                if (circuitJustOpened)
                    _circuitOpenUntil = DateTime.UtcNow + CircuitOpenDuration;
            }

            // v2.4.62 — One Warning when the circuit trips, Debug for the rest.
            // Cancellations never carry a stack trace: a timeout under load is
            // expected, and the old code logged a full trace for every one.
            if (circuitJustOpened)
            {
                _logger.LogWarning(
                    "SmartBlock disabled for {Minutes:F0} min after {Count} consecutive failures (last: {Host} — {Error})",
                    CircuitOpenDuration.TotalMinutes, _consecutiveFailures, host, ex.Message);
            }
            else if (ex is OperationCanceledException)
            {
                _logger.LogDebug("SmartBlock timed out for {Host}; allowing", host);
            }
            else
            {
                _logger.LogDebug(ex, "SmartBlock classifier failed for {Host}; allowing", host);
            }

            return result;
        }
        finally
        {
            lock (_lock) { _inFlight.Remove(host); }
        }
    }

    private bool TryGetCachedLocked(string host, out Result? result)
    {
        result = null;
        if (!_cache.TryGetValue(host, out var entry)) return false;

        var ttl = entry.Failed ? FailureCacheTtl : CacheTtl;
        if ((DateTime.UtcNow - entry.At) >= ttl)
        {
            _cache.Remove(host);
            return false;
        }

        result = entry.R;
        return true;
    }

    /// <summary>
    /// Sync cache lookup — returns the cached verdict for <paramref name="host"/>
    /// when one exists within the TTL, otherwise null. Used by
    /// <c>RequestGuard.Evaluate</c> (which is sync) to consult prior async
    /// classifications without re-invoking the model. v2.4.22.
    /// </summary>
    public Result? TryGetCachedVerdict(string host)
    {
        if (string.IsNullOrEmpty(host)) return null;
        lock (_lock)
        {
            return TryGetCachedLocked(host, out var entry)
                ? entry! with { FromCache = true }
                : null;
        }
    }

    /// <summary>Test helper — clears the cache and resets the circuit breaker.</summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _recentCalls.Clear();
            _consecutiveFailures = 0;
            _circuitOpenUntil = DateTime.MinValue;
        }
    }

    /// <summary>v2.4.62 — True while the classifier is backing off after repeated failures.</summary>
    public bool IsCircuitOpen
    {
        get { lock (_lock) return DateTime.UtcNow < _circuitOpenUntil; }
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

    /// <summary>Caller must hold <c>_lock</c>.</summary>
    private bool ConsumeBudgetLocked()
    {
        if (MaxCallsPerMinute <= 0) return true;

        var now = DateTime.UtcNow;

        // Drop entries older than 1 minute.
        while (_recentCalls.Count > 0 && (now - _recentCalls.Peek()) > TimeSpan.FromMinutes(1))
            _recentCalls.Dequeue();

        if (_recentCalls.Count >= MaxCallsPerMinute) return false;
        _recentCalls.Enqueue(now);
        return true;
    }
}
