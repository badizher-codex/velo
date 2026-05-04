using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Core.Updates;

/// <summary>
/// Phase 3 / Sprint 2 — Downloads + verifies the next VELO installer against
/// the SHA256SUMS.txt published with each GitHub Release. The verification
/// step exists so VELO can ship without an Authenticode certificate (the
/// installer trips Windows SmartScreen, but a verified hash still proves the
/// binary matches what CI built).
///
/// Flow (per spec § 8.2):
///   1. Download <c>VELO-v{ver}-Setup.exe</c> → %TEMP%\velo-update-{ver}.exe
///   2. Download <c>SHA256SUMS.txt</c> from the same release URL parent.
///   3. Parse the line whose path matches the asset name.
///   4. Compute SHA256 of the downloaded .exe.
///   5. If match → return <see cref="DownloadVerifyResult.Success"/> = true.
///      If mismatch → delete the file and return Success = false with a reason.
///
/// Lives in VELO.Core (not VELO.App) so the unit tests can reference it
/// without pulling in the WinExe entry point.
/// </summary>
public sealed class UpdateDownloader
{
    private readonly HttpClient _http;
    private readonly ILogger<UpdateDownloader> _logger;
    private readonly Func<string> _tempDirProvider;

    public UpdateDownloader(
        HttpClient?                http            = null,
        ILogger<UpdateDownloader>? logger          = null,
        Func<string>?              tempDirProvider = null)
    {
        _http   = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _logger = logger ?? NullLogger<UpdateDownloader>.Instance;
        _tempDirProvider = tempDirProvider ?? Path.GetTempPath;
    }

    /// <summary>
    /// Downloads the installer + sums file, computes the hash, verifies match.
    /// On any failure (network / parse / mismatch / cancellation) the partial
    /// .exe is deleted before returning so we never leave junk in %TEMP%.
    /// </summary>
    public async Task<DownloadVerifyResult> DownloadAndVerifyAsync(
        UpdateInfo info, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.DownloadUrl))
            return DownloadVerifyResult.Fail("missing download URL");

        var sumsUrl = DeriveSumsUrl(info.DownloadUrl);
        if (string.IsNullOrEmpty(sumsUrl))
            return DownloadVerifyResult.Fail("could not derive SHA256SUMS.txt URL");

        var fileName  = Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
        var localPath = Path.Combine(_tempDirProvider(),
                                     $"velo-update-{info.LatestVersion}.exe");

        try
        {
            // 1. Fetch the SHA256SUMS.txt first — if this fails we don't
            //    waste bandwidth on the .exe.
            ct.ThrowIfCancellationRequested();
            var sumsContent = await _http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
            var expected   = ParseSha256Sum(sumsContent, fileName);
            if (string.IsNullOrEmpty(expected))
                return DownloadVerifyResult.Fail($"hash for '{fileName}' not in SHA256SUMS.txt");

            // 2. Download the installer to %TEMP%.
            ct.ThrowIfCancellationRequested();
            using (var resp = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var dst = File.Create(localPath);
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            // 3. Compute SHA256.
            ct.ThrowIfCancellationRequested();
            string actual;
            using (var fs = File.OpenRead(localPath))
                actual = await ComputeSha256HexAsync(fs, ct).ConfigureAwait(false);

            // 4. Compare (case-insensitive — PowerShell emits uppercase).
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Update hash mismatch: expected {Expected}, got {Actual}", expected, actual);
                TryDelete(localPath);
                return new DownloadVerifyResult(false, "", actual, expected, "hash mismatch");
            }

            _logger.LogInformation("Update verified: {File} → SHA256 {Hash}", fileName, actual);
            return new DownloadVerifyResult(true, localPath, actual, expected, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(localPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update download failed");
            TryDelete(localPath);
            return DownloadVerifyResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Launches the verified installer with Inno Setup's silent flags and
    /// returns true on successful start. Caller is expected to call
    /// <c>System.Windows.Application.Shutdown()</c> afterwards.
    /// </summary>
    public bool ExecuteInstaller(string verifiedExePath)
    {
        if (string.IsNullOrEmpty(verifiedExePath) || !File.Exists(verifiedExePath))
        {
            _logger.LogWarning("Installer path missing: {Path}", verifiedExePath);
            return false;
        }
        try
        {
            // /SILENT  → no wizard pages
            // /CLOSEAPPLICATIONS → ask Inno to close VELO if it's running.
            // UseShellExecute=true so the UAC prompt elevates as needed.
            Process.Start(new ProcessStartInfo
            {
                FileName        = verifiedExePath,
                Arguments       = "/SILENT /CLOSEAPPLICATIONS",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Installer launch failed: {Path}", verifiedExePath);
            return false;
        }
    }

    // ── Pure helpers (unit-testable) ─────────────────────────────────────

    /// <summary>
    /// Parses one line out of the standard PowerShell-emitted SHA256SUMS.txt
    /// (format: <c>HEXHASH  filename</c>, two spaces). Returns the hex hash
    /// (case unchanged) or null when the filename doesn't appear.
    /// </summary>
    public static string? ParseSha256Sum(string content, string fileName)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(fileName)) return null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0) continue;

            // Accept either "HASH  file" (two spaces, sha256sum default) or
            // "HASH *file" (binary mode marker). Some emitters use a single
            // space — be permissive.
            int sep = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == ' ' || line[i] == '\t')
                {
                    sep = i;
                    break;
                }
            }
            if (sep <= 0 || sep == line.Length - 1) continue;

            var hash    = line[..sep];
            var rest    = line[(sep + 1)..].TrimStart(' ', '*', '\t');
            // Match by basename — release tooling sometimes prefixes a path.
            var basename = Path.GetFileName(rest);
            if (string.Equals(basename, fileName, StringComparison.OrdinalIgnoreCase))
                return hash;
        }
        return null;
    }

    /// <summary>SHA256 of a stream, returned as lowercase hex.</summary>
    public static async Task<string> ComputeSha256HexAsync(Stream stream, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        var sb   = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Same release as the .exe — replace the trailing filename in the URL
    /// with <c>SHA256SUMS.txt</c>. Returns null on a malformed URL.
    /// </summary>
    public static string? DeriveSumsUrl(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var u)) return null;
        var lastSlash = u.AbsoluteUri.LastIndexOf('/');
        if (lastSlash < 0) return null;
        return u.AbsoluteUri[..(lastSlash + 1)] + "SHA256SUMS.txt";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}

/// <summary>Outcome of <see cref="UpdateDownloader.DownloadAndVerifyAsync"/>.</summary>
public sealed record DownloadVerifyResult(
    bool   Success,
    string FilePath,
    string ActualHashHex,
    string? ExpectedHashHex,
    string? Error)
{
    public static DownloadVerifyResult Fail(string error) =>
        new(false, "", "", null, error);
}
