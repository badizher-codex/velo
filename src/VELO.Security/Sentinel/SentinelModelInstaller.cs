using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Security.Sentinel;

/// <summary>
/// S-D — the download channel for the Sentinel model
/// (<c>PLAN_VELO_IA_SEGURIDAD.md</c> §3). The installer deliberately does not
/// ship the model: a 67 MB blob inside the Setup is more AV surface for no
/// benefit, and the model is versioned independently of the app so a
/// <c>model-vN+1</c> with a compatible schema can be adopted without an app
/// release.
///
/// Security model, mirroring <c>UpdateDownloader</c>:
///   • HTTPS only, GitHub Releases only.
///   • <b>Never automatic.</b> Nothing here runs unless the user clicks the
///     button in Settings → AI, same privacy gate as <c>updates.auto_check</c>.
///   • The manifest is fetched and validated FIRST — schema range and version —
///     so an incompatible model costs a kilobyte instead of 67 MB.
///   • Every file is SHA256-verified against the manifest before it is allowed
///     anywhere near the model directory.
///   • Staged in a sibling temp directory and moved into place only once every
///     hash matches, so a failed or cancelled download can never leave a
///     half-installed version for <c>LocateModelDirectory</c> to find.
/// </summary>
public sealed class SentinelModelInstaller
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/badizher-codex/velo/releases";

    /// <summary>Release tags that carry a model: <c>model-v1</c>, <c>model-v2</c>, …</summary>
    private static readonly Regex ModelTag = new(@"^model-v(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<SentinelModelInstaller> _logger;
    private readonly string _modelRoot;

    public SentinelModelInstaller(
        HttpClient? http = null,
        ILogger<SentinelModelInstaller>? logger = null,
        string? modelRoot = null)
    {
        _http = http ?? CreateDefaultClient();
        _logger = logger ?? NullLogger<SentinelModelInstaller>.Instance;
        _modelRoot = modelRoot ?? SentinelClassifier.DefaultModelRoot();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "VELO-Browser SentinelModelInstaller");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    /// <summary>A model release that can be installed.</summary>
    public sealed record Available(
        int Version,
        string Tag,
        string ManifestUrl,
        string ModelUrl,
        string TokenizerUrl);

    public enum Phase { Manifest, Model, Tokenizer, Verifying, Installing }

    public sealed record Progress(Phase Phase, long BytesRead, long TotalBytes)
    {
        public double Fraction => TotalBytes > 0 ? (double)BytesRead / TotalBytes : 0;
    }

    public sealed record Result(bool Success, int Version, string? Directory, string? Error)
    {
        public static Result Fail(string error) => new(false, 0, null, error);
    }

    // ── Discovery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Highest <c>model-vN</c> release newer than what is installed, or null
    /// when up to date / offline / nothing published. Never throws: this is
    /// called from a button handler and the answer to any failure is "no
    /// update offered".
    /// </summary>
    public async Task<Available?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var releases = await _http.GetFromJsonAsync<GithubRelease[]>(ReleasesUrl, ct).ConfigureAwait(false);
            if (releases is null) return null;

            var installed = InstalledVersion();

            Available? best = null;
            foreach (var release in releases)
            {
                var match = ModelTag.Match(release.TagName ?? "");
                if (!match.Success) continue;
                if (!int.TryParse(match.Groups[1].Value, out var version)) continue;
                if (version <= installed) continue;
                if (best is not null && version <= best.Version) continue;

                string? Asset(string name) => release.Assets?
                    .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?.BrowserDownloadUrl;

                var manifest  = Asset(SentinelClassifier.ManifestFileName);
                var model     = Asset(SentinelClassifier.ModelFileName);
                var tokenizer = Asset(SentinelClassifier.TokenizerFileName);

                // A release missing any of the three is not installable. Skip
                // it rather than offering a download that cannot complete.
                if (manifest is null || model is null || tokenizer is null)
                {
                    _logger.LogDebug("Sentinel release {Tag} skipped: incomplete assets", release.TagName);
                    continue;
                }

                best = new Available(version, release.TagName!, manifest, model, tokenizer);
            }

            if (best is not null)
                _logger.LogInformation("Sentinel model available: {Tag} (installed v{Installed})", best.Tag, installed);

            return best;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sentinel model check failed");
            return null;
        }
    }

    /// <summary>Highest complete version already on disk, 0 when none.</summary>
    public int InstalledVersion()
    {
        var dir = SentinelClassifier.LocateModelDirectory(_modelRoot);
        if (dir is null) return 0;
        try { return SentinelManifest.FromFile(Path.Combine(dir, SentinelClassifier.ManifestFileName)).Version; }
        catch { return 0; }
    }

    // ── Download + verify + install ───────────────────────────────────────

    /// <summary>
    /// Fetches, verifies and installs <paramref name="available"/>. Returns a
    /// Result rather than throwing (except on cancellation) — the caller is a
    /// settings dialog, and every failure mode here ends the same way: the
    /// previously installed model, if any, is untouched.
    /// </summary>
    public async Task<Result> InstallAsync(
        Available available,
        IProgress<Progress>? progress = null,
        CancellationToken ct = default)
    {
        var staging = Path.Combine(_modelRoot, $".staging-v{available.Version}");

        try
        {
            Directory.CreateDirectory(_modelRoot);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            // 1. Manifest first — cheap, and it decides whether the rest is
            //    even worth downloading.
            progress?.Report(new Progress(Phase.Manifest, 0, 0));
            var manifestJson = await _http.GetStringAsync(available.ManifestUrl, ct).ConfigureAwait(false);
            var manifest = SentinelManifest.FromJson(manifestJson);

            if (!manifest.IsSchemaSupported)
            {
                return Result.Fail(
                    $"model schema {manifest.Schema} is outside this build's supported range " +
                    $"{SentinelManifest.MinSupportedSchema}-{SentinelManifest.MaxSupportedSchema}; update VELO first");
            }

            if (manifest.Version != available.Version)
            {
                // The tag and the manifest disagreeing means the release was
                // assembled wrong. Refuse rather than install something whose
                // identity we cannot state.
                return Result.Fail(
                    $"release tag says v{available.Version} but the manifest says v{manifest.Version}");
            }

            await File.WriteAllTextAsync(
                Path.Combine(staging, SentinelClassifier.ManifestFileName), manifestJson, ct).ConfigureAwait(false);

            // 2. The two payload files, each verified against the manifest.
            foreach (var (fileName, url, phase) in new[]
            {
                (SentinelClassifier.ModelFileName,     available.ModelUrl,     Phase.Model),
                (SentinelClassifier.TokenizerFileName, available.TokenizerUrl, Phase.Tokenizer),
            })
            {
                var expected = ExpectedHash(manifestJson, fileName);
                if (expected is null)
                    return Result.Fail($"manifest has no sha256 for {fileName}");

                var path = Path.Combine(staging, fileName);
                await DownloadAsync(url, path, phase, progress, ct).ConfigureAwait(false);

                progress?.Report(new Progress(Phase.Verifying, 0, 0));
                var actual = await Sha256HexAsync(path, ct).ConfigureAwait(false);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Sentinel model {File} hash mismatch: expected {Expected}, got {Actual}",
                        fileName, expected, actual);
                    return Result.Fail($"{fileName} failed SHA256 verification");
                }
            }

            // 3. Everything verified — publish atomically. The version
            //    directory only ever appears complete.
            progress?.Report(new Progress(Phase.Installing, 0, 0));
            var target = Path.Combine(_modelRoot, $"v{available.Version}");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);

            _logger.LogInformation(
                "Sentinel model v{Version} installed and verified → {Dir}", available.Version, target);
            return new Result(true, available.Version, target, null);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(staging);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sentinel model install failed");
            TryDeleteDirectory(staging);
            return Result.Fail(ex.Message);
        }
        finally
        {
            // Nothing may survive under .staging-*: LocateModelDirectory would
            // not pick it (the name doesn't parse as a version) but leaving 67 MB
            // of debris behind is its own bug.
            TryDeleteDirectory(staging);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Reads <c>files.&lt;name&gt;.sha256</c> out of the raw manifest JSON.</summary>
    internal static string? ExpectedHash(string manifestJson, string fileName)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("files", out var files)) return null;
            if (!files.TryGetProperty(fileName, out var entry)) return null;
            return entry.TryGetProperty("sha256", out var hash) ? hash.GetString() : null;
        }
        catch { return null; }
    }

    private async Task DownloadAsync(
        string url, string path, Phase phase, IProgress<Progress>? progress, CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var target = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            progress?.Report(new Progress(phase, read, total));
        }
    }

    internal static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not clean staging directory {Path}", path); }
    }

    // ── GitHub API DTO ────────────────────────────────────────────────────

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; init; }
        [JsonPropertyName("assets")]   public GithubAsset[]? Assets { get; init; }
    }

    private sealed class GithubAsset
    {
        [JsonPropertyName("name")]                 public string? Name { get; init; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
    }
}
