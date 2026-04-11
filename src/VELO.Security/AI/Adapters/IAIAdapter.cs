using VELO.Security.AI.Models;

namespace VELO.Security.AI.Adapters;

public interface IAIAdapter
{
    bool IsAvailable { get; }
    string ModeName { get; }
    Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct = default);
}
