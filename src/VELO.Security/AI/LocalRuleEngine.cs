using VELO.Security.AI.Models;

namespace VELO.Security.AI;

public class LocalRuleEngine
{
    private static readonly string[] MinerPatterns =
    [
        "coinhive", "cryptonight", "monero", "stratum+tcp",
        "minero", "webminerpool", "coinimp", "jsecoin"
    ];

    private static readonly string[] PhishingPatterns =
    [
        "paypa1", "g00gle", "arnazon", "micros0ft", "bankofamerica-secure"
    ];

    public AIVerdict? Evaluate(ThreatContext context)
    {
        // Crypto miner detection by domain
        foreach (var pattern in MinerPatterns)
        {
            if (context.Domain.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return new AIVerdict
                {
                    Verdict = VerdictType.Block,
                    Confidence = 95,
                    Reason = $"Dominio asociado a minería de criptomonedas: {pattern}",
                    ThreatType = ThreatType.Miner,
                    Source = "LOCAL_RULES"
                };
        }

        // Phishing patterns in domain
        foreach (var pattern in PhishingPatterns)
        {
            if (context.Domain.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return new AIVerdict
                {
                    Verdict = VerdictType.Block,
                    Confidence = 90,
                    Reason = $"Dominio con patrón de phishing: {context.Domain}",
                    ThreatType = ThreatType.Phishing,
                    Source = "LOCAL_RULES"
                };
        }

        // Script patterns detected by ScriptGuard — if score is decisive
        if (context.DetectedPatterns.Contains("eval_dynamic") && context.RiskScore >= 80)
            return new AIVerdict
            {
                Verdict = VerdictType.Block,
                Confidence = 85,
                Reason = "Script con eval() dinámico y score de riesgo alto",
                ThreatType = ThreatType.Malware,
                Source = "LOCAL_RULES"
            };

        // Not deterministic — let AI decide
        return null;
    }
}
