namespace VELO.Core.Updates;

/// <summary>
/// Phase 3 / Sprint 2 — Common record describing a release available for
/// download. Lives in VELO.Core (not VELO.App) so the test project can
/// reference it without trying to load the WinExe.
/// </summary>
public sealed record UpdateInfo(
    Version  CurrentVersion,
    Version  LatestVersion,
    string   ReleaseName,
    string   ReleaseNotes,
    string   DownloadUrl,
    DateTime PublishedAt);
