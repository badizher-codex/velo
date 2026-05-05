using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Security.Guards;

/// <summary>
/// Phase 3 / Sprint 8B — Local-LLM phishing detector. Combines signals
/// the page itself emits (URL shape, host, TLS validity, presence of a
/// login form, page title) and asks a local model whether the page is
/// likely a phishing impersonation. The model's reasoning is surfaced
/// to the user so the verdict is auditable, not opaque.
///
/// Why local LLM over signature-based (Google Safe Browsing / SmartScreen):
///
/// • <b>Zero-day phishing.</b> Attackers register fresh domains in bulk
///   and signature lists lag by hours-to-days. A model that sees the
///   page CONTENT can flag e.g. "this is a paypal-themed page hosted on
///   a 2-day-old .top domain with no valid TLS" without ever needing a
///   signature update.
/// • <b>Privacy.</b> Vendor lookups leak the URL the user is visiting.
///   A local model never sends anything off-device.
/// • <b>Explainability.</b> The user sees the reasoning, not just a
///   binary block. Builds trust.
///
/// Signals we DO send to the model:
///   - host
///   - URL path/query length (no contents)
///   - TLS validity flags
///   - page title (truncated to 120 chars)
///   - whether a password field is present
///   - heuristic flags from <see cref="RequestGuard"/>
///
/// Signals we DO NOT send:
///   - the password field's name or autocomplete attribute (could leak)
///   - the user's username, if they typed it
///   - cookies, localStorage, referrer headers
///
/// The classifier is pure (no I/O beyond ChatDelegate). Tests in
/// <c>PhishingShieldTests</c>.
/// </summary>
public sealed class PhishingShield
{
    public enum Verdict { Safe, Suspicious, Phishing }

    public sealed record Signals(
        string Host,
        string PageTitle,
        bool   HasLoginForm,
        bool   TlsValid,
        bool   IsSelfSigned,
        bool   LooksLikeBrandImpersonation,
        bool   LooksRandomGenerated,
        bool   HasSuspiciousTld,
        int    DomainAgeDays);

    public sealed record Result(
        Verdict Verdict,
        double  Confidence,
        string  Reason,
        bool    FromCache);

    /// <summary>Adapter delegate. <c>(systemPrompt, userPrompt, ct) =&gt; reply</c>.</summary>
    public Func<string, string, CancellationToken, Task<string>>? ChatDelegate { get; set; }

    /// <summary>Confidence floor for a Phishing verdict; below this, downgrade to Suspicious.</summary>
    public double PhishingConfidenceThreshold { get; set; } = 0.80;

    /// <summary>Confidence floor for a Suspicious verdict; below this, returns Safe.</summary>
    public double SuspiciousConfidenceThreshold { get; set; } = 0.55;

    /// <summary>Maximum chars of page title we forward to the model — prevents prompt-stuffing.</summary>
    public int MaxTitleChars { get; set; } = 120;

    /// <summary>Cache TTL per host. Phishing sites get taken down fast; 30 min is a balance.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, (Result R, DateTime At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PhishingShield> _logger;
    private readonly object _lock = new();

    public PhishingShield(ILogger<PhishingShield>? logger = null)
    {
        _logger = logger ?? NullLogger<PhishingShield>.Instance;
    }

    /// <summary>
    /// Returns a phishing verdict for the page described by <paramref name="signals"/>.
    /// Cache-first; falls back to the chat adapter only when no recent
    /// classification exists. When ChatDelegate is null, returns
    /// <see cref="Verdict.Safe"/> so the rest of the security stack
    /// (RequestGuard, blocklist, TLSGuard) keeps working.
    /// </summary>
    public async Task<Result> EvaluateAsync(Signals signals, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(signals.Host))
            return new Result(Verdict.Safe, 0, "empty host", FromCache: false);

        // Quick gate: if we have NO suspicious heuristic and TLS is valid,
        // don't bother the model. This is the 99% path.
        var anySuspiciousHeuristic =
            signals.LooksLikeBrandImpersonation ||
            signals.LooksRandomGenerated ||
            signals.HasSuspiciousTld ||
            !signals.TlsValid ||
            signals.IsSelfSigned ||
            (signals.DomainAgeDays > 0 && signals.DomainAgeDays < 30);

        if (!anySuspiciousHeuristic && !signals.HasLoginForm)
            return new Result(Verdict.Safe, 0, "no risk signals", FromCache: false);

        // Cache lookup keyed on host + has-login-form. We don't include the
        // title because legit sites change titles (loading states) and we
        // don't want to thrash the model.
        var cacheKey = $"{signals.Host}|{signals.HasLoginForm}";
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry) &&
                (DateTime.UtcNow - entry.At) < CacheTtl)
            {
                return entry.R with { FromCache = true };
            }
        }

        if (ChatDelegate is null)
        {
            return new Result(Verdict.Safe, 0,
                "phishing shield disabled (no chat adapter)",
                FromCache: false);
        }

        Result result;
        try
        {
            var (sys, user) = BuildPrompt(signals, MaxTitleChars);
            var reply = await ChatDelegate(sys, user, ct).ConfigureAwait(false);
            result = ParseReply(reply);
            _logger.LogDebug("PhishingShield {Host}: {Verdict} ({Conf:F2}) — {Reason}",
                signals.Host, result.Verdict, result.Confidence, result.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PhishingShield evaluation failed for {Host}", signals.Host);
            return new Result(Verdict.Safe, 0, $"shield error: {ex.Message}", FromCache: false);
        }

        lock (_lock) { _cache[cacheKey] = (result, DateTime.UtcNow); }
        return result;
    }

    /// <summary>Test helper.</summary>
    public void ClearCache()
    {
        lock (_lock) { _cache.Clear(); }
    }

    /// <summary>Test helper.</summary>
    public int CacheCount
    {
        get { lock (_lock) return _cache.Count; }
    }

    // ── Pure helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the (system, user) prompt pair. The system prompt asks for
    /// a single-line reply: <c>VERDICT|CONFIDENCE|REASON</c> where VERDICT
    /// is SAFE | SUSPICIOUS | PHISHING.
    /// </summary>
    public static (string System, string User) BuildPrompt(Signals s, int maxTitleChars)
    {
        const string system =
            "You evaluate web pages for phishing/impersonation risk. " +
            "PHISHING = clearly impersonating a known brand or login (e.g. paypal " +
            "themed page on a fresh non-paypal domain with broken TLS). " +
            "SUSPICIOUS = some red flags but inconclusive. " +
            "SAFE = no concerning signals. " +
            "Reply on ONE line: VERDICT|CONFIDENCE|REASON. " +
            "VERDICT: SAFE | SUSPICIOUS | PHISHING. " +
            "CONFIDENCE: 0.0-1.0. REASON: 5-15 words. No preamble, no markdown.";

        var title = (s.PageTitle ?? "").Replace('\n', ' ').Replace('\r', ' ');
        if (title.Length > maxTitleChars) title = title[..maxTitleChars] + "…";

        var flags = new List<string>();
        if (s.HasLoginForm)               flags.Add("login form present");
        if (!s.TlsValid)                  flags.Add("TLS invalid");
        if (s.IsSelfSigned)               flags.Add("TLS self-signed");
        if (s.LooksLikeBrandImpersonation) flags.Add("brand-like host");
        if (s.LooksRandomGenerated)       flags.Add("random-looking host");
        if (s.HasSuspiciousTld)           flags.Add("suspicious TLD");
        if (s.DomainAgeDays > 0 && s.DomainAgeDays < 30)
            flags.Add($"domain {s.DomainAgeDays}d old");

        var flagLine = flags.Count == 0 ? "(none)" : string.Join(", ", flags);

        var user =
            $"host: {s.Host}\n" +
            $"title: {title}\n" +
            $"flags: {flagLine}";

        return (system, user);
    }

    /// <summary>
    /// Parses a model reply of shape <c>VERDICT|CONFIDENCE|REASON</c>.
    /// Confidence-gates downgrades: PHISHING below threshold becomes
    /// SUSPICIOUS; SUSPICIOUS below threshold becomes SAFE. Defaults to
    /// SAFE on malformed replies — under-blocking is safer than
    /// over-blocking on a misparsed response.
    /// </summary>
    public Result ParseReply(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return new Result(Verdict.Safe, 0, "empty reply", FromCache: false);

        var line = reply
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('`', '*'))
            .FirstOrDefault(l => l.Contains('|'));

        if (string.IsNullOrEmpty(line))
            return new Result(Verdict.Safe, 0, "no pipe-separated line", FromCache: false);

        var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return new Result(Verdict.Safe, 0, "malformed reply", FromCache: false);

        var verdictWord = parts[0].ToUpperInvariant();
        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var conf))
            conf = 0;
        conf = Math.Clamp(conf, 0, 1);

        var reason = parts.Length >= 3 ? parts[2] : "";

        Verdict v;
        if (verdictWord.StartsWith("PHISH") && conf >= PhishingConfidenceThreshold)
            v = Verdict.Phishing;
        else if ((verdictWord.StartsWith("PHISH") || verdictWord.StartsWith("SUSP"))
                 && conf >= SuspiciousConfidenceThreshold)
            v = Verdict.Suspicious;
        else
            v = Verdict.Safe;

        return new Result(v, conf, reason, FromCache: false);
    }
}
