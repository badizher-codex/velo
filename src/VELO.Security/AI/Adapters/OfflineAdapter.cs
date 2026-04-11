using VELO.Security.AI.Models;

namespace VELO.Security.AI.Adapters;

public class OfflineAdapter : IAIAdapter
{
    public bool IsAvailable => true;
    public string ModeName => "Offline";

    public Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct = default)
    {
        if (context.RiskScore >= 70)
            return Task.FromResult(AIVerdict.Block(
                $"Script sospechoso detectado: {string.Join(", ", context.DetectedPatterns)}",
                source: "OFFLINE"));

        if (context.RiskScore >= 40)
            return Task.FromResult(AIVerdict.Warn(
                $"Script con comportamiento inusual: {string.Join(", ", context.DetectedPatterns)}",
                source: "OFFLINE"));

        return Task.FromResult(AIVerdict.Safe());
    }
}
