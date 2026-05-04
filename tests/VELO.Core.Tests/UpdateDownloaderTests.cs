using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using VELO.Core.Updates;
using Xunit;

namespace VELO.Core.Tests;

public class UpdateDownloaderTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>HttpMessageHandler stub returning canned (uri → body) pairs.</summary>
    private sealed class StubHandler(Dictionary<string, byte[]> bodies, HttpStatusCode code = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!bodies.TryGetValue(request.RequestUri!.AbsoluteUri, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) });
        }
    }

    /// <summary>Slow handler so cancellation can interrupt mid-flight.</summary>
    private sealed class SlowHandler(Dictionary<string, byte[]> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(200, ct);
            if (!bodies.TryGetValue(request.RequestUri!.AbsoluteUri, out var body))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        }
    }

    private static string Sha256Hex(byte[] data)
    {
        var sb = new StringBuilder();
        foreach (var b in SHA256.HashData(data)) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static UpdateInfo Info(string downloadUrl) => new(
        CurrentVersion: new Version(2, 1, 0),
        LatestVersion : new Version(2, 1, 1),
        ReleaseName   : "v2.1.1",
        ReleaseNotes  : "",
        DownloadUrl   : downloadUrl,
        PublishedAt   : DateTime.UtcNow);

    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "velo-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ── Pure-helper tests (cheap, fast) ──────────────────────────────────

    [Fact]
    public void ParseSha256Sum_FindsHashByFilename()
    {
        var content = """
            ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890  VELO-v2.1.1-Setup.exe
            FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF  VELO-v2.1.1-win-x64-portable.zip
            """;

        var hash = UpdateDownloader.ParseSha256Sum(content, "VELO-v2.1.1-Setup.exe");
        Assert.Equal("ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890", hash);
    }

    [Fact]
    public void ParseSha256Sum_TolerantToBinaryStarPrefix()
    {
        var content = "ABCDEF *VELO-v2.1.1-Setup.exe\n";
        var hash    = UpdateDownloader.ParseSha256Sum(content, "VELO-v2.1.1-Setup.exe");
        Assert.Equal("ABCDEF", hash);
    }

    [Fact]
    public void DeriveSumsUrl_StripsLastPathSegment()
    {
        var url  = "https://github.com/badizher-codex/velo/releases/download/v2.1.1/VELO-v2.1.1-Setup.exe";
        var sums = UpdateDownloader.DeriveSumsUrl(url);
        Assert.Equal("https://github.com/badizher-codex/velo/releases/download/v2.1.1/SHA256SUMS.txt", sums);
    }

    // ── End-to-end tests (per spec § 8.3) ────────────────────────────────

    [Fact]
    public async Task UpdateDownloader_VerifiesHashBeforeExecuting()
    {
        var exeBytes = Encoding.UTF8.GetBytes("THIS IS A FAKE EXE FOR TESTING");
        var hashHex  = Sha256Hex(exeBytes);
        var dlUrl    = "https://example.test/v2.1.1/VELO-v2.1.1-Setup.exe";
        var sumsUrl  = "https://example.test/v2.1.1/SHA256SUMS.txt";
        var sums     = $"{hashHex}  VELO-v2.1.1-Setup.exe\n";

        var handler = new StubHandler(new()
        {
            [dlUrl]   = exeBytes,
            [sumsUrl] = Encoding.UTF8.GetBytes(sums),
        });
        var tempDir = NewTempDir();
        var dl = new UpdateDownloader(new HttpClient(handler), tempDirProvider: () => tempDir);

        try
        {
            var result = await dl.DownloadAndVerifyAsync(Info(dlUrl));

            Assert.True(result.Success, $"Expected success; error: {result.Error}");
            Assert.True(File.Exists(result.FilePath), "File should be on disk after a successful verify");
            Assert.Equal(hashHex, result.ActualHashHex);
            Assert.Equal(hashHex, result.ExpectedHashHex);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task UpdateDownloader_DeletesFileOnHashMismatch()
    {
        var exeBytes  = Encoding.UTF8.GetBytes("DOWNLOADED FILE");
        var dlUrl     = "https://example.test/v2.1.1/VELO-v2.1.1-Setup.exe";
        var sumsUrl   = "https://example.test/v2.1.1/SHA256SUMS.txt";
        var bogusHash = new string('a', 64); // 64 hex chars — definitely wrong
        var sums      = $"{bogusHash}  VELO-v2.1.1-Setup.exe\n";

        var handler = new StubHandler(new()
        {
            [dlUrl]   = exeBytes,
            [sumsUrl] = Encoding.UTF8.GetBytes(sums),
        });
        var tempDir = NewTempDir();
        var dl = new UpdateDownloader(new HttpClient(handler), tempDirProvider: () => tempDir);

        try
        {
            var result = await dl.DownloadAndVerifyAsync(Info(dlUrl));

            Assert.False(result.Success);
            Assert.Equal("hash mismatch", result.Error);
            // The downloaded file must NOT be left around when the hash didn't match.
            var leaked = Directory.GetFiles(tempDir).Where(f => Path.GetFileName(f).Contains("velo-update-")).ToArray();
            Assert.Empty(leaked);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task UpdateDownloader_RespectsUserCancel()
    {
        var dlUrl   = "https://example.test/v2.1.1/VELO-v2.1.1-Setup.exe";
        var sumsUrl = "https://example.test/v2.1.1/SHA256SUMS.txt";

        var handler = new SlowHandler(new()
        {
            [sumsUrl] = Encoding.UTF8.GetBytes("deadbeef  VELO-v2.1.1-Setup.exe\n"),
            [dlUrl]   = new byte[8 * 1024 * 1024], // 8 MB to force a real read loop
        });
        var tempDir = NewTempDir();
        var dl  = new UpdateDownloader(new HttpClient(handler), tempDirProvider: () => tempDir);
        var cts = new CancellationTokenSource();

        try
        {
            var task = dl.DownloadAndVerifyAsync(Info(dlUrl), cts.Token);
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            // No partial file should remain after cancel.
            var leaked = Directory.GetFiles(tempDir).Where(f => Path.GetFileName(f).Contains("velo-update-")).ToArray();
            Assert.Empty(leaked);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
