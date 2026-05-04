using System.IO;
using VELO.Import.Models;

namespace VELO.Import.Detectors;

/// <summary>
/// Phase 3 / Sprint 4 — Generic detector for any Chromium-derived browser
/// (Chrome / Edge / Brave / Vivaldi / Opera). They all share the same
/// User-Data layout under different vendor folders, so subclasses just
/// supply (Kind, DisplayName, vendor relative path).
/// </summary>
public abstract class ChromiumDetectorBase : IBrowserDetector
{
    public abstract string Name { get; }
    protected abstract BrowserKind Kind { get; }
    protected abstract string DisplayName { get; }
    /// <summary>Relative path under %LOCALAPPDATA%, e.g. <c>Google\Chrome\User Data</c>.</summary>
    protected abstract string UserDataRelative { get; }

    /// <summary>
    /// Test seam — production reads the Windows known folder; tests inject
    /// a temp directory so detection can be verified without touching the
    /// real Chrome / Edge install.
    /// </summary>
    protected virtual string LocalAppDataRoot =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public virtual Task<DetectedBrowser?> DetectAsync(CancellationToken ct = default)
    {
        var localAppData = LocalAppDataRoot;
        var userData     = Path.Combine(localAppData, UserDataRelative);
        if (!Directory.Exists(userData)) return Task.FromResult<DetectedBrowser?>(null);

        // Default profile is always at "Default"; some users have multiple
        // profiles ("Profile 1", "Profile 2"…). MVP: pick Default if it
        // exists, else the first profile dir we find.
        var defaultProfile = Path.Combine(userData, "Default");
        string profilePath, profileName;
        if (Directory.Exists(defaultProfile))
        {
            profilePath = defaultProfile;
            profileName = "Default";
        }
        else
        {
            var first = Directory.GetDirectories(userData)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));
            if (first == null) return Task.FromResult<DetectedBrowser?>(null);
            profilePath = first;
            profileName = Path.GetFileName(first);
        }

        // Sanity: the profile must contain at least one of the standard files.
        if (!File.Exists(Path.Combine(profilePath, "Bookmarks")) &&
            !File.Exists(Path.Combine(profilePath, "History")))
            return Task.FromResult<DetectedBrowser?>(null);

        return Task.FromResult<DetectedBrowser?>(
            new DetectedBrowser(Kind, DisplayName, profileName, profilePath));
    }
}

public sealed class ChromeDetector : ChromiumDetectorBase
{
    public override string Name           => "Chrome";
    protected override BrowserKind Kind   => BrowserKind.Chrome;
    protected override string DisplayName => "Google Chrome";
    protected override string UserDataRelative => @"Google\Chrome\User Data";
}

public sealed class EdgeDetector : ChromiumDetectorBase
{
    public override string Name           => "Edge";
    protected override BrowserKind Kind   => BrowserKind.Edge;
    protected override string DisplayName => "Microsoft Edge";
    protected override string UserDataRelative => @"Microsoft\Edge\User Data";
}

public sealed class BraveDetector : ChromiumDetectorBase
{
    public override string Name           => "Brave";
    protected override BrowserKind Kind   => BrowserKind.Brave;
    protected override string DisplayName => "Brave";
    protected override string UserDataRelative => @"BraveSoftware\Brave-Browser\User Data";
}

public sealed class VivaldiDetector : ChromiumDetectorBase
{
    public override string Name           => "Vivaldi";
    protected override BrowserKind Kind   => BrowserKind.Vivaldi;
    protected override string DisplayName => "Vivaldi";
    protected override string UserDataRelative => @"Vivaldi\User Data";
}

public sealed class OperaDetector : ChromiumDetectorBase
{
    public override string Name           => "Opera";
    protected override BrowserKind Kind   => BrowserKind.Opera;
    protected override string DisplayName => "Opera";
    protected override string UserDataRelative => @"Opera Software\Opera Stable";

    // Opera flattens — it doesn't put a Default folder under User Data.
    // Override is unnecessary because we fall through to "first dir starting
    // with Profile " when Default missing; Opera Stable already IS the profile.
}
