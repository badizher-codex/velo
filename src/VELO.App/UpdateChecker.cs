using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VELO.Core.Updates;

namespace VELO.App;

/// <summary>
/// Polls GitHub Releases for a newer version of VELO and raises an event when one is found.
///
/// Security model:
///   • HTTPS only (enforced by HttpClient).
///   • Does NOT download or install anything silently — only signals the UI to show a prompt.
///   • The user must explicitly click "Download" for any file transfer to begin.
///   • Verify Authenticode + SHA256 on the downloaded file before launching it (done by the installer).
/// </summary>
public sealed class UpdateChecker(ILogger<UpdateChecker> logger)
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/badizher-codex/velo/releases/latest";

    private static readonly Version CurrentVersion =
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(2, 0, 0);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            { "User-Agent", $"VELO-Browser/{CurrentVersion} UpdateChecker" },
            { "Accept",     "application/vnd.github+json" },
            { "X-GitHub-Api-Version", "2022-11-28" },
        }
    };

    /// <summary>
    /// Raised on the thread-pool when a newer release is available.
    /// Subscribers are responsible for marshalling to the UI thread.
    /// </summary>
    public event Action<UpdateInfo>? UpdateAvailable;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a background loop that polls for updates every <paramref name="intervalHours"/> hours.
    /// Returns immediately; the loop runs for the lifetime of the app.
    /// </summary>
    public void StartBackgroundCheck(int intervalHours = 24, CancellationToken ct = default)
        => _ = RunLoopAsync(intervalHours, ct);

    /// <summary>Performs a single check and returns the result (null = up-to-date or error).</summary>
    public async Task<UpdateInfo?> CheckOnceAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GithubRelease>(LatestReleaseUrl, ct);
            if (release == null) return null;

            // Strip leading 'v' from tag (e.g. "v2.1.0" → "2.1.0")
            var tagVersion = release.TagName?.TrimStart('v');
            if (!Version.TryParse(tagVersion, out var remoteVersion)) return null;
            if (remoteVersion <= CurrentVersion) return null;

            logger.LogInformation(
                "Update available: {Current} → {Remote}", CurrentVersion, remoteVersion);

            // Find the Setup .exe asset
            var setupAsset = release.Assets?
                .FirstOrDefault(a => a.Name?.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) == true);

            return new UpdateInfo(
                CurrentVersion : CurrentVersion,
                LatestVersion  : remoteVersion,
                ReleaseName    : release.Name    ?? release.TagName ?? "",
                ReleaseNotes   : release.Body    ?? "",
                DownloadUrl    : setupAsset?.BrowserDownloadUrl ?? release.HtmlUrl ?? "",
                PublishedAt    : release.PublishedAt ?? DateTime.UtcNow);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Update check failed — will retry later");
            return null;
        }
    }

    // ── Background loop ───────────────────────────────────────────────────

    private async Task RunLoopAsync(int intervalHours, CancellationToken ct)
    {
        // Wait a few minutes after startup before first check
        await SafeDelay(TimeSpan.FromMinutes(3), ct);

        while (!ct.IsCancellationRequested)
        {
            var info = await CheckOnceAsync(ct);
            if (info != null)
                UpdateAvailable?.Invoke(info);

            await SafeDelay(TimeSpan.FromHours(intervalHours), ct);
        }
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }

    // ── GitHub API DTO ────────────────────────────────────────────────────

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")]    public string?              TagName      { get; init; }
        [JsonPropertyName("name")]        public string?              Name         { get; init; }
        [JsonPropertyName("body")]        public string?              Body         { get; init; }
        [JsonPropertyName("html_url")]    public string?              HtmlUrl      { get; init; }
        [JsonPropertyName("published_at")] public DateTime?           PublishedAt  { get; init; }
        [JsonPropertyName("assets")]      public GithubAsset[]?       Assets       { get; init; }
    }

    private sealed class GithubAsset
    {
        [JsonPropertyName("name")]                  public string? Name                 { get; init; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl   { get; init; }
    }
}

// UpdateInfo lives in VELO.Core/Updates so the UpdateDownloader unit tests
// can reference it without pulling in VELO.App (the WinExe).
