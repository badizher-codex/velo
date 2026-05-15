using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Security.Guards;

/// <summary>
/// v2.4.53 — Stateful gate for the YouTube ad-block script. Owns the
/// boolean "should we inject?" answer that <c>BrowserTab.EnsureWebViewInitializedAsync</c>
/// consults before adding <c>resources/scripts/youtube-adblock.js</c> to the
/// WebView2 document-create pipeline.
///
/// Pattern mirrored from <see cref="SmartBlockClassifier"/> / <see cref="PhishingShield"/>:
/// the host caches an <see cref="IsEnabled"/> value at startup, refreshes it
/// on a setting change via <see cref="SetEnabledAsync"/> from the settings
/// dialog, and the BrowserTab reads the cached flag synchronously inside
/// the WebView init path (which itself is async but cannot await an extra
/// settings round-trip without slowing every new tab).
///
/// Lives in VELO.Security so the unit tests (next to SmartBlock /
/// PhishingShield) can exercise the same settings round-trip without
/// pulling WPF in.
///
/// Privacy contract: the script never sends data anywhere. CSS injection +
/// DOM observers all happen page-side; nothing leaves the user's machine.
/// The "ad-block" verdict is binary (script on / off) — there is no
/// per-host allowlist for v0.1 because the script's host gate is
/// "is this youtube.com?" not "is this host ad-allowed?".
/// </summary>
public sealed class YouTubeAdBlocker
{
    /// <summary>Default state when the setting hasn't been written yet.
    /// "yes" = privacy-first behaviour out of the box.</summary>
    public const string DefaultSettingValue = "yes";

    private readonly SettingsRepository _settings;
    private readonly ILogger<YouTubeAdBlocker> _logger;
    private bool _isEnabled = true; // optimistic default — RefreshAsync corrects.

    /// <summary>Cached "should we inject the script?" answer. Read by
    /// BrowserTab on every WebView2 init. Refreshed by the settings dialog
    /// via <see cref="SetEnabledAsync"/>; refreshed at startup via
    /// <see cref="RefreshAsync"/>.</summary>
    public bool IsEnabled => _isEnabled;

    public YouTubeAdBlocker(
        SettingsRepository settings,
        ILogger<YouTubeAdBlocker>? logger = null)
    {
        _settings = settings;
        _logger   = logger ?? NullLogger<YouTubeAdBlocker>.Instance;
    }

    /// <summary>Reads the setting from storage and updates <see cref="IsEnabled"/>.
    /// Called once at startup by the bootstrap path. Safe to call multiple
    /// times — idempotent.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var raw = await _settings.GetAsync(SettingKeys.YouTubeAdsBlocked, DefaultSettingValue);
            _isEnabled = string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("YouTubeAdBlocker refreshed: enabled={Enabled}", _isEnabled);
        }
        catch (Exception ex)
        {
            // Best-effort — on failure assume the safe default.
            _logger.LogWarning(ex, "YouTubeAdBlocker.RefreshAsync failed; keeping default enabled=true");
            _isEnabled = true;
        }
    }

    /// <summary>Persists the user's choice from the Settings dialog AND
    /// updates the cached flag so new tabs pick it up immediately. Existing
    /// tabs keep their prior injection state (script-on-document-created
    /// fires only once per webview lifetime) — toggling off doesn't unload
    /// the script from already-loaded YouTube tabs, but the user can
    /// refresh to apply.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        try
        {
            await _settings.SetAsync(SettingKeys.YouTubeAdsBlocked, enabled ? "yes" : "no");
            _isEnabled = enabled;
            _logger.LogInformation("YouTubeAdBlocker setting changed: enabled={Enabled}", enabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTubeAdBlocker.SetEnabledAsync failed; in-memory flag still updated");
            _isEnabled = enabled; // optimistic — the user clicked a checkbox
        }
    }
}
