using System.IO;
using VELO.Import.Models;

namespace VELO.Import.Detectors;

/// <summary>
/// Phase 3 / Sprint 4 — Detects a Firefox install by looking for any
/// <c>*.default*</c> profile under <c>%APPDATA%\Mozilla\Firefox\Profiles\</c>.
/// Firefox stores everything (bookmarks, history, passwords) inside that
/// profile folder; the path is what importers consume.
/// </summary>
public sealed class FirefoxDetector : IBrowserDetector
{
    public string Name => "Firefox";

    public Task<DetectedBrowser?> DetectAsync(CancellationToken ct = default)
    {
        var appData     = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesDir = Path.Combine(appData, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profilesDir)) return Task.FromResult<DetectedBrowser?>(null);

        // Firefox profile names are like "abc12345.default-release".
        // The "default" profile is usually the largest one; pick the most
        // recently modified to handle multi-profile installs.
        var profile = Directory.GetDirectories(profilesDir)
            .Where(d => Path.GetFileName(d).Contains("default", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
            .FirstOrDefault();

        if (profile == null) return Task.FromResult<DetectedBrowser?>(null);

        // Sanity check: places.sqlite is the bookmarks+history DB. Without
        // it the profile is corrupt or the path matched something stale.
        if (!File.Exists(Path.Combine(profile, "places.sqlite")))
            return Task.FromResult<DetectedBrowser?>(null);

        return Task.FromResult<DetectedBrowser?>(new DetectedBrowser(
            BrowserKind.Firefox,
            "Firefox",
            Path.GetFileName(profile),
            profile));
    }
}
