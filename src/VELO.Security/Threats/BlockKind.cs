namespace VELO.Security.Threats;

/// <summary>
/// Phase 3 / Sprint 1 — Coarse classification of a blocked request, used by
/// ThreatsPanelV2 grouping and by BlockExplanationService template lookup.
/// Keep these stable — they're persisted in event payloads.
/// </summary>
public enum BlockKind
{
    Tracker,
    Malware,
    Ads,
    Fingerprint,
    Script,
    Social,
    Other,
}

/// <summary>Origin of the block decision — informs UI badge colour and trust.</summary>
public enum BlockSource
{
    /// <summary>Curated VELO Golden List — high-confidence well-known trackers.</summary>
    GoldenList,
    /// <summary>Captured to local Malwaredex — user has a per-host count.</summary>
    Malwaredex,
    /// <summary>Live AI verdict (Claude / Ollama / LLamaSharp).</summary>
    AIEngine,
    /// <summary>User-defined rule (Settings → Privacy → Custom rules).</summary>
    UserRule,
    /// <summary>Hard-coded static list inside the binary (last-resort safety net).</summary>
    StaticList,
    /// <summary>RequestGuard heuristic (blocklist match, suspicious params, …).</summary>
    RequestGuard,
    /// <summary>DownloadGuard heuristic (drive-by, cross-origin exec, …).</summary>
    DownloadGuard,
}
