using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Core.Media;

/// <summary>
/// Phase 6 — stateful gate for <c>resources/scripts/media-detect.js</c>. Owns
/// the boolean "should we inject?" answer that
/// <c>BrowserTab.EnsureWebViewInitializedAsync</c> consults.
///
/// Deliberately mirrors <c>YouTubeAdBlocker</c>: the host caches the value at
/// startup, the settings dialog refreshes it through
/// <see cref="SetEnabledAsync"/>, and the BrowserTab reads the cached flag
/// synchronously inside the WebView init path — which is async but must not
/// pay a settings round-trip on every new tab.
///
/// Why this exists at all: detection is read-only and no media bytes ever
/// cross the bridge, but it installs wrappers on
/// <c>MediaSource.addSourceBuffer</c> and <c>SourceBuffer.appendBuffer</c> —
/// the media hot path, and the exact thing that broke playback in YouTube
/// ad-block v0.2. Anything on by default that touches page behaviour needs a
/// switch the user can find; without one, a site where the wrapper misbehaves
/// leaves downgrading as the only recourse.
/// </summary>
public sealed class MediaDetectionGate
{
    /// <summary>Default when the setting has never been written. Detection is
    /// on out of the box — a feature nobody can see is not a safe default,
    /// it is a hidden one.</summary>
    public const string DefaultSettingValue = "yes";

    private readonly SettingsRepository _settings;
    private readonly ILogger<MediaDetectionGate> _logger;
    private bool _isEnabled = true;   // optimistic; RefreshAsync corrects.

    /// <summary>Cached answer, read by BrowserTab on every WebView2 init.</summary>
    public bool IsEnabled => _isEnabled;

    public MediaDetectionGate(
        SettingsRepository settings,
        ILogger<MediaDetectionGate>? logger = null)
    {
        _settings = settings;
        _logger   = logger ?? NullLogger<MediaDetectionGate>.Instance;
    }

    /// <summary>Reads the stored value. Called once at startup; idempotent.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var raw = await _settings.GetAsync(SettingKeys.MediaDetectionEnabled, DefaultSettingValue);
            _isEnabled = string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("MediaDetectionGate refreshed: enabled={Enabled}", _isEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MediaDetectionGate.RefreshAsync failed; keeping default enabled=true");
            _isEnabled = true;
        }
    }

    /// <summary>
    /// Persists the user's choice and updates the cached flag so new tabs pick
    /// it up immediately.
    ///
    /// Existing tabs keep their prior state: script-on-document-created fires
    /// once per WebView lifetime, so turning detection off does not unwrap
    /// pages that are already open — the user has to reload them. Same
    /// limitation as the ad-block toggle, and the settings copy says so rather
    /// than leaving the user to discover it.
    /// </summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        try
        {
            await _settings.SetAsync(SettingKeys.MediaDetectionEnabled, enabled ? "yes" : "no");
            _isEnabled = enabled;
            _logger.LogInformation("MediaDetectionGate setting changed: enabled={Enabled}", enabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MediaDetectionGate.SetEnabledAsync failed; in-memory flag still updated");
            _isEnabled = enabled;   // optimistic — the user clicked a checkbox
        }
    }
}
