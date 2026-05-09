using Microsoft.Extensions.Logging;
using VELO.Security.AI.Adapters;
using VELO.Security.AI.Models;
using VELO.Security.Guards;

namespace VELO.Security.AI;

public class AISecurityEngine(
    SecurityCache cache,
    LocalRuleEngine localRules,
    IAIAdapter aiAdapter,
    ILogger<AISecurityEngine> logger,
    PhishingShield? phishingShield = null)
{
    private readonly SecurityCache _cache = cache;
    private readonly LocalRuleEngine _localRules = localRules;
    private IAIAdapter _aiAdapter = aiAdapter;
    private readonly ILogger<AISecurityEngine> _logger = logger;
    // v2.4.22 — Sprint 8B wire. PhishingShield runs between local rules
    // and the cloud/local AI adapter as a fast early-out for high-
    // confidence phishing pages. Optional dependency so test fixtures
    // and pre-Sprint-8 wiring keep working.
    private readonly PhishingShield? _phishingShield = phishingShield;

    public void SetAdapter(IAIAdapter adapter)
    {
        _aiAdapter = adapter;
        _logger.LogInformation("AI adapter switched to: {Mode}", adapter.ModeName);
    }

    public async Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct = default)
    {
        // 1. Check cache
        var cached = await _cache.GetAsync(context);
        if (cached != null)
        {
            _logger.LogDebug("Cache HIT for {Domain}", context.Domain);
            return cached;
        }

        // 2. Local rules (deterministic, fast)
        var localVerdict = _localRules.Evaluate(context);
        if (localVerdict != null)
        {
            _logger.LogDebug("Local rule verdict for {Domain}: {Verdict}", context.Domain, localVerdict.Verdict);
            await _cache.SetAsync(context, localVerdict);
            return localVerdict;
        }

        // 3 (v2.4.22). PhishingShield — local LLM phishing/impersonation check.
        //               Only runs when there are real heuristic flags (suspicious
        //               TLD, brand impersonation, random hostname, broken TLS) —
        //               otherwise it returns Safe immediately and we proceed.
        if (_phishingShield != null)
        {
            var signals = BuildPhishingSignals(context);
            var phish = await _phishingShield.EvaluateAsync(signals, ct);
            if (phish.Verdict == PhishingShield.Verdict.Phishing)
            {
                _logger.LogWarning("PhishingShield blocked {Domain}: {Reason} (conf {Conf:F2})",
                    context.Domain, phish.Reason, phish.Confidence);
                var blockVerdict = AIVerdict.Block(
                    $"PhishingShield: {phish.Reason}",
                    source: "PhishingShield");
                blockVerdict.ThreatType = ThreatType.Phishing;
                blockVerdict.Confidence = (int)Math.Round(phish.Confidence * 100);
                blockVerdict.Host       = context.Domain;
                await _cache.SetAsync(context, blockVerdict);
                return blockVerdict;
            }
        }

        // 4. Only call AI if score warrants it
        if (context.RiskScore < 40)
        {
            var safe = AIVerdict.Safe();
            await _cache.SetAsync(context, safe);
            return safe;
        }

        // 5. AI Adapter
        _logger.LogDebug("Sending {Domain} to AI adapter ({Mode})", context.Domain, _aiAdapter.ModeName);
        var verdict = await _aiAdapter.AnalyzeAsync(context, ct);
        await _cache.SetAsync(context, verdict);
        return verdict;
    }

    public string CurrentMode => _aiAdapter.ModeName;

    /// <summary>
    /// v2.4.22 — Builds <see cref="PhishingShield.Signals"/> from a
    /// <see cref="ThreatContext"/>. The shield's quick gate already
    /// short-circuits to Safe when no flag is set and no login form is
    /// present, so the cost of building this is paid only on suspicious
    /// pages. HasLoginForm/DomainAgeDays default to false/0 because
    /// neither is currently surfaced by RequestGuard's pipeline; future
    /// work can populate them from a DOM probe + RDAP cache.
    /// </summary>
    private static PhishingShield.Signals BuildPhishingSignals(ThreatContext context)
    {
        var host = context.Domain ?? "";
        var tls  = context.TLSInfo;
        return new PhishingShield.Signals(
            Host:                       host,
            PageTitle:                  "",
            HasLoginForm:               false,
            TlsValid:                   tls is null || !tls.IsSelfSigned,
            IsSelfSigned:               tls?.IsSelfSigned ?? false,
            LooksLikeBrandImpersonation: RequestGuard.LooksLikeBrandImpersonation(host),
            LooksRandomGenerated:        RequestGuard.LooksRandomGenerated(host),
            HasSuspiciousTld:            RequestGuard.HasSuspiciousTld(host),
            DomainAgeDays:              0);
    }
}
