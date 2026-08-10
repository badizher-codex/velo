using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Core.Media;

/// <summary>Outcome of one <see cref="MediaDownloader.DownloadAsync"/> call.</summary>
public sealed record MediaDownloadResult(
    bool    Success,
    string  FilePath,
    long    BytesWritten,
    bool    Resumed,
    string? Error)
{
    public static MediaDownloadResult Fail(string error, long written = 0) =>
        new(false, "", written, false, error);
}

/// <summary>
/// Phase 6 / P2a — fetches one media URL to disk. This is the engine VELO did
/// not have: <c>DownloadManager</c> only keeps a list, and the bytes for
/// page-initiated downloads belong to WebView2. <c>UpdateDownloader</c> owns
/// the same primitives but is welded to the update flow (it derives a
/// SHA256SUMS.txt URL from the release URL), so the pattern is reused here
/// and the class is not.
///
/// Covers the two paths where media has a real URL: a progressive file, and
/// the individual segments of a standards-compliant HLS/DASH stream. It does
/// NOT cover MSE-only delivery — measured in §10 of the analysis, YouTube's
/// SABR transport exposes no per-track URL at all, and that path needs a
/// capture sink rather than a downloader.
///
/// Two things here look like over-engineering and are not; both come from
/// measurements:
///
///   • <b>Browser-realistic headers.</b> OPEN-3 measured a public .mp4
///     answering <b>403 with no User-Agent and 206 with one</b>. Every other
///     HttpClient in VELO identifies itself as "VELO-Browser/…", which is
///     correct for VELO's own APIs and exactly wrong here — it would refuse
///     content that is perfectly downloadable. The real WebView2 UA should be
///     passed in by the caller; the fallback exists so the class is usable
///     without one, not as the preferred path.
///
///   • <b>Resume must handle a server that ignores Range.</b> Asking for
///     <c>bytes=N-</c> and getting <c>200</c> instead of <c>206</c> means the
///     response body starts at zero. Appending it to the existing partial
///     produces a corrupt file that is exactly the right length, which is the
///     worst possible failure — it looks like it worked.
/// </summary>
public sealed class MediaDownloader
{
    /// <summary>
    /// Used only when the caller supplies nothing. Prefer passing
    /// <c>CoreWebView2Settings.UserAgent</c> from the tab that is playing the
    /// media: it is exact, it matches what the site already served, and it
    /// never goes stale as WebView2 updates.
    /// </summary>
    public const string FallbackUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36";

    private const int BufferSize = 64 * 1024;

    private readonly HttpClient _http;
    private readonly ILogger<MediaDownloader> _logger;
    private readonly string _userAgent;

    public MediaDownloader(
        HttpClient? http = null,
        ILogger<MediaDownloader>? logger = null,
        string? userAgent = null)
    {
        _http      = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _logger    = logger ?? NullLogger<MediaDownloader>.Instance;
        _userAgent = string.IsNullOrWhiteSpace(userAgent) ? FallbackUserAgent : userAgent;
    }

    /// <summary>
    /// Streams <paramref name="url"/> to <paramref name="destinationPath"/>.
    ///
    /// Bytes land in a sibling <c>.part</c> file and are renamed into place
    /// only on success, so the destination never holds a half-written file.
    /// A cancelled transfer KEEPS its <c>.part</c> — that is what makes resume
    /// possible — and callers that want the bytes gone should call
    /// <see cref="DiscardPartial"/>.
    ///
    /// Cancellation returns a failed result rather than throwing. This differs
    /// from <c>UpdateDownloader</c> deliberately: an interrupted update is
    /// worthless and gets deleted, while an interrupted media download is a
    /// resume point the user is likely to want back.
    /// </summary>
    /// <param name="refererPageUrl">
    /// The page the media is playing on. Many CDNs hotlink-protect, and the
    /// same 403 OPEN-3 measured for a missing User-Agent also appears for a
    /// missing Referer on some hosts.
    /// </param>
    public async Task<MediaDownloadResult> DownloadAsync(
        string url,
        string destinationPath,
        string? refererPageUrl = null,
        IProgress<(long Received, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))             return MediaDownloadResult.Fail("empty url");
        if (string.IsNullOrWhiteSpace(destinationPath)) return MediaDownloadResult.Fail("empty destination");

        var partPath  = destinationPath + ".part";
        var alreadyOnDisk = PartialLength(partPath);
        var resuming  = alreadyOnDisk > 0;
        var written   = alreadyOnDisk;

        try
        {
            // GetDirectoryName returns "" (not null) for a bare filename, and
            // CreateDirectory("") throws.
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var request = BuildRequest(url, refererPageUrl, alreadyOnDisk);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            // A server that ignores Range answers 200 and starts the body at
            // byte zero. Appending that to the partial yields a corrupt file of
            // exactly the expected length — start over instead.
            if (resuming && response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation(
                    "Range ignored by {Host}; restarting the transfer from zero",
                    SafeHost(url));
                TryDelete(partPath);
                alreadyOnDisk = 0;
                written       = 0;
                resuming      = false;
            }

            // The partial is already at or past the full length. Nothing left
            // to fetch, and the bytes we have are the whole file.
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && alreadyOnDisk > 0)
            {
                Promote(partPath, destinationPath);
                return new MediaDownloadResult(true, destinationPath, alreadyOnDisk, true, null);
            }

            if (!response.IsSuccessStatusCode)
                return MediaDownloadResult.Fail(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", written);

            var total = ResolveTotal(response, alreadyOnDisk);

            using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var target = new FileStream(
                       partPath,
                       resuming ? FileMode.Append : FileMode.Create,
                       FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    written += read;
                    progress?.Report((written, total));
                }
            }

            Promote(partPath, destinationPath);
            _logger.LogInformation("Media download complete: {Bytes} bytes → {Path}", written, destinationPath);
            return new MediaDownloadResult(true, destinationPath, written, resuming, null);
        }
        catch (OperationCanceledException)
        {
            // The .part survives on purpose — see the summary.
            return new MediaDownloadResult(false, "", written, resuming, "cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media download failed: {Url}", SafeHost(url));
            return new MediaDownloadResult(false, "", written, resuming, ex.Message);
        }
    }

    /// <summary>
    /// Phase 6 / P2a-2 — fetches an ordered list of segments and concatenates
    /// them into one file.
    ///
    /// Byte-wise concatenation is correct for MPEG-TS, and that is measured
    /// rather than assumed: two real segments from the reference stream are
    /// each an exact multiple of 188 bytes, and joined they give 32 436
    /// packets with **zero** bad sync bytes. That is OPEN-4, open since P0,
    /// answered — for TS. For fMP4 the initialisation segment must lead, once,
    /// which is what <paramref name="initSegmentUrl"/> is for.
    ///
    /// Order is the file. Segments are fetched sequentially on purpose: a
    /// parallel fetch would be faster and would also let a reordered
    /// completion corrupt the output silently, and this is not a speed
    /// contest.
    ///
    /// Resume works at segment granularity — a partial run leaves a .part and
    /// the count of whole segments already written, so a retry skips those and
    /// continues. A segment half-written when cancellation landed is dropped,
    /// because appending the rest of it afterwards would splice garbage into
    /// the middle.
    /// </summary>
    public async Task<MediaDownloadResult> DownloadSegmentsAsync(
        IReadOnlyList<string> segmentUrls,
        string destinationPath,
        string? initSegmentUrl = null,
        string? refererPageUrl = null,
        IProgress<(int SegmentsDone, int SegmentsTotal, long Bytes)>? progress = null,
        CancellationToken ct = default)
    {
        if (segmentUrls is null || segmentUrls.Count == 0)
            return MediaDownloadResult.Fail("no segments");
        if (string.IsNullOrWhiteSpace(destinationPath))
            return MediaDownloadResult.Fail("empty destination");

        var partPath = destinationPath + ".part";
        var written  = 0L;
        var done     = 0;

        try
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // A fresh run every time. Resuming a segmented job needs the index
            // of the last WHOLE segment, which nothing persists yet — see the
            // note in the analysis doc. Starting over is the honest behaviour
            // until it does; silently appending to an unknown offset is not.
            TryDelete(partPath);

            var urls = new List<string>(segmentUrls.Count + 1);
            if (!string.IsNullOrWhiteSpace(initSegmentUrl)) urls.Add(initSegmentUrl);
            urls.AddRange(segmentUrls);

            string? failure = null;

            using (var target = new FileStream(
                       partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                foreach (var url in urls)
                {
                    ct.ThrowIfCancellationRequested();

                    using var request  = BuildRequest(url, refererPageUrl, 0);
                    using var response = await _http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        // One bad segment means a hole in the middle of the
                        // file. Stop rather than produce something that plays
                        // until it doesn't. The partial is cleaned up after
                        // the stream closes — returning from inside the using
                        // left a .part behind, which a test caught.
                        failure = $"segment {done + 1}/{urls.Count} failed: HTTP {(int)response.StatusCode}";
                        break;
                    }

                    using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    var buffer = new byte[BufferSize];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        written += read;
                    }

                    done++;
                    progress?.Report((done, urls.Count, written));
                }
            }

            if (failure is not null)
            {
                TryDelete(partPath);
                return MediaDownloadResult.Fail(failure, written);
            }

            Promote(partPath, destinationPath);
            _logger.LogInformation(
                "Segmented download complete: {Segments} segments, {Bytes} bytes → {Path}",
                done, written, destinationPath);

            return new MediaDownloadResult(true, destinationPath, written, false, null);
        }
        catch (OperationCanceledException)
        {
            // No resume point is kept: the last segment is probably half
            // written, and a half segment spliced into the middle is worse
            // than starting again.
            TryDelete(partPath);
            return new MediaDownloadResult(false, "", written, false, "cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Segmented download failed after {Segments} segments", done);
            TryDelete(partPath);
            return new MediaDownloadResult(false, "", written, false, ex.Message);
        }
    }

    /// <summary>
    /// Fetches a manifest as text, with the same browser-realistic headers as
    /// a media fetch. Manifests sit behind the same hotlink protection their
    /// segments do, so a plain HttpClient would hit the 403 OPEN-3 measured.
    /// Returns null on any failure rather than throwing.
    /// </summary>
    public async Task<string?> GetTextAsync(string url, string? refererPageUrl = null, CancellationToken ct = default)
    {
        try
        {
            using var request  = BuildRequest(url, refererPageUrl, 0);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Throws away a resume point the user no longer wants.</summary>
    public static void DiscardPartial(string destinationPath) =>
        TryDelete(destinationPath + ".part");

    /// <summary>Bytes already on disk for this destination, 0 when none.</summary>
    public static long PartialLength(string partOrDestinationPath)
    {
        var path = partOrDestinationPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
            ? partOrDestinationPath
            : partOrDestinationPath + ".part";
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private HttpRequestMessage BuildRequest(string url, string? referer, long resumeFrom)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Measured requirement, not decoration: the same file answered 403
        // without this header and 206 with it.
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        if (!string.IsNullOrWhiteSpace(referer))
            request.Headers.TryAddWithoutValidation("Referer", referer);

        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        return request;
    }

    /// <summary>
    /// Full resource size. Content-Range carries it on a partial response;
    /// Content-Length carries only the slice being sent, so on a resume it has
    /// to be added to what is already on disk. Returns 0 when unknown — the
    /// measured vnd.yt-ump responses had neither header.
    /// </summary>
    private static long ResolveTotal(HttpResponseMessage response, long alreadyOnDisk)
    {
        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange?.Length is > 0) return contentRange.Length.Value;

        var length = response.Content.Headers.ContentLength;
        return length is > 0 ? length.Value + alreadyOnDisk : 0;
    }

    private static void Promote(string partPath, string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        File.Move(partPath, destinationPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private static string SafeHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";
}
