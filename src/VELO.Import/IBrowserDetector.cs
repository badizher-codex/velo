using VELO.Import.Models;

namespace VELO.Import;

/// <summary>
/// Phase 3 / Sprint 4 — Inspects a known on-disk location and returns a
/// <see cref="DetectedBrowser"/> if the browser is installed for the
/// current user. Implementations are stateless; the orchestrator runs
/// them in parallel.
/// </summary>
public interface IBrowserDetector
{
    string Name { get; }
    Task<DetectedBrowser?> DetectAsync(CancellationToken ct = default);
}
