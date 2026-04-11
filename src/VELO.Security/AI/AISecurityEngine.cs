using Microsoft.Extensions.Logging;
using VELO.Security.AI.Adapters;
using VELO.Security.AI.Models;

namespace VELO.Security.AI;

public class AISecurityEngine(
    SecurityCache cache,
    LocalRuleEngine localRules,
    IAIAdapter aiAdapter,
    ILogger<AISecurityEngine> logger)
{
    private readonly SecurityCache _cache = cache;
    private readonly LocalRuleEngine _localRules = localRules;
    private IAIAdapter _aiAdapter = aiAdapter;
    private readonly ILogger<AISecurityEngine> _logger = logger;

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

        // 3. Only call AI if score warrants it
        if (context.RiskScore < 40)
        {
            var safe = AIVerdict.Safe();
            await _cache.SetAsync(context, safe);
            return safe;
        }

        // 4. AI Adapter
        _logger.LogDebug("Sending {Domain} to AI adapter ({Mode})", context.Domain, _aiAdapter.ModeName);
        var verdict = await _aiAdapter.AnalyzeAsync(context, ct);
        await _cache.SetAsync(context, verdict);
        return verdict;
    }

    public string CurrentMode => _aiAdapter.ModeName;
}
