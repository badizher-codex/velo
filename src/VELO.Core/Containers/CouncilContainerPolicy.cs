using VELO.Data.Models;

namespace VELO.Core.Containers;

/// <summary>
/// Phase 4.0 (Council Mode — foundations, v2.5.0-pre) — policy hub for the
/// four built-in Council containers. Peer to <see cref="BankingContainerPolicy"/>.
///
/// Council Mode runs four parallel LLM panels (Claude / ChatGPT / Grok /
/// Ollama). Each panel lives in its own VELO Container so cookies/storage
/// stay isolated — the user can have the same Google account signed into
/// Claude but never to Grok. Container names follow the <c>council-*</c>
/// convention seeded once in <c>VeloDatabase</c> (Phase 4.0 chunk E).
///
/// The single rule encoded here today is the <b>fingerprint level
/// override</b>: VELO's default fingerprint protection ("Aggressive") spoofs
/// canvas, WebGL, fonts, navigator and webrtc surfaces aggressively. That
/// looks suspicious to Cloudflare's anti-bot pipelines that Claude/ChatGPT/
/// Grok sit behind — they intermittently throw the user a captcha or a hard
/// block. Council containers therefore drop to "Standard" (a softer noise
/// profile that still resists basic fingerprinting but doesn't trip the
/// gatekeepers). Banking mode keeps its strict policy untouched.
///
/// Lookup is by hardcoded container ID — these are seeded by VELO itself,
/// not user-creatable. If the user ever renames or deletes one of the
/// four built-in containers, the override silently stops applying and
/// the panel reverts to the global fingerprint level (which may surface
/// the anti-bot collision; better than silently lying about isolation).
/// </summary>
public static class CouncilContainerPolicy
{
    /// <summary>
    /// Canonical container IDs for the four Council panels. Order matches
    /// the panel index used by <c>CouncilLayoutController.PanelTabIds</c>
    /// (0 = top-left through 3 = bottom-right) so the same index resolves
    /// both panel cell and container identity.
    /// </summary>
    public static readonly IReadOnlyList<string> CouncilContainerIds = new[]
    {
        "council-claude",
        "council-chatgpt",
        "council-grok",
        "council-ollama",
    };

    /// <summary>The fingerprint level applied to all Council containers.</summary>
    public const string CouncilFingerprintLevel = "Standard";

    /// <summary>True if the container ID matches any of the four Council slots.</summary>
    public static bool Applies(string? containerId) =>
        !string.IsNullOrEmpty(containerId) &&
        containerId!.StartsWith("council-", StringComparison.Ordinal) &&
        CouncilContainerIds.Contains(containerId);

    /// <summary>True if the resolved container is a Council slot.</summary>
    public static bool Applies(Container? container) =>
        container is not null && Applies(container.Id);

    /// <summary>
    /// Returns the fingerprint level the caller (<c>BrowserTabHost</c> via
    /// <c>MainWindow.OnTabCreated</c>) should pass into
    /// <c>BrowserTab.Initialize</c> for a tab living in
    /// <paramref name="containerId"/>. Council slots downgrade to
    /// <see cref="CouncilFingerprintLevel"/>; every other container keeps
    /// the user's global preference unchanged.
    /// </summary>
    public static string ResolveFingerprintLevel(string? containerId, string globalLevel) =>
        Applies(containerId) ? CouncilFingerprintLevel : globalLevel;
}
