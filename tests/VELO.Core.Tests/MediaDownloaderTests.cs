using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / P2a.
///
/// Two of these tests exist because of things that were MEASURED rather than
/// assumed (see docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md):
///
///   • OPEN-3 — the same public .mp4 answered 403 with no User-Agent and 206
///     with one. A downloader identifying itself as "VELO-Browser/…", which
///     every other HttpClient in the codebase does, refuses content that is
///     perfectly downloadable.
///   • A server that ignores a Range request answers 200 with the body from
///     byte zero. Appending that to a partial produces a corrupt file of
///     exactly the right length — a failure that looks like success, which is
///     why it gets an explicit test rather than trust.
/// </summary>
public class MediaDownloaderTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────

    private static byte[] Payload(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < size; i++) data[i] = (byte)(i % 251);   // prime → no accidental alignment
        return data;
    }

    /// <summary>Serves a body, honouring Range unless told not to.</summary>
    private sealed class MediaHandler(byte[] body, bool honourRange = true) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            Requests++;

            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;

            if (from is > 0 && honourRange)
            {
                if (from >= body.Length)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));

                var slice = body[(int)from.Value..];
                var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(slice),
                };
                partial.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(from.Value, body.Length - 1, body.Length);
                return Task.FromResult(partial);
            }

            // Either no Range asked, or a server that ignores it: full body, 200.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }

    /// <summary>Refuses anything without a browser User-Agent, as measured.</summary>
    private sealed class HotlinkProtectedHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var agent = request.Headers.UserAgent.ToString();
            var looksLikeABrowser = agent.Contains("Mozilla", StringComparison.Ordinal);

            return Task.FromResult(looksLikeABrowser
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }

    /// <summary>Trickles the body so cancellation lands mid-transfer.</summary>
    private sealed class TricklingHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new TricklingStream(body)),
            };
            response.Content.Headers.ContentLength = body.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class TricklingStream(byte[] data) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(40, ct);
            if (_position >= data.Length) return 0;

            var count = Math.Min(4096, Math.Min(buffer.Length, data.Length - _position));
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "velo-media-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Reports on the calling thread instead of posting to a synchronization
    /// context. Progress&lt;T&gt; was the first choice and it made two tests
    /// flaky under the full-solution run: its callbacks are posted, so they
    /// arrive late and — because Post gives no ordering guarantee — the "last"
    /// value seen can be an earlier one. Nothing here needs the marshalling
    /// Progress&lt;T&gt; exists to provide.
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private const string Url = "https://cdn.example.com/movie.mp4";

    // ── The happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task Downloads_bytes_identical_to_the_source()
    {
        var body = Payload(300_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        var downloader = new MediaDownloader(new HttpClient(new MediaHandler(body)));
        var result = await downloader.DownloadAsync(Url, dest);

        Assert.True(result.Success, result.Error);
        Assert.Equal(body.Length, result.BytesWritten);
        Assert.Equal(body, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".part"));   // nothing left behind
    }

    [Fact]
    public async Task Progress_reaches_the_full_size()
    {
        var body = Payload(200_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        (long Received, long Total) last = (0, 0);
        var downloader = new MediaDownloader(new HttpClient(new MediaHandler(body)));
        await downloader.DownloadAsync(Url, dest,
            progress: new SyncProgress<(long Received, long Total)>(p => last = p));

        // Reported synchronously during the copy loop, so by the time the
        // download has awaited to completion this is final. No polling.
        Assert.Equal(body.Length, last.Received);
        Assert.Equal(body.Length, last.Total);
    }

    // ── Headers — the measured requirement ───────────────────────────────

    [Fact]
    public async Task Sends_browser_realistic_headers()
    {
        var body    = Payload(1_000);
        var handler = new MediaHandler(body);
        var dir     = NewTempDir();

        var downloader = new MediaDownloader(new HttpClient(handler));
        await downloader.DownloadAsync(Url, Path.Combine(dir, "m.mp4"),
            refererPageUrl: "https://example.com/watch");

        var sent = handler.LastRequest!;
        Assert.Contains("Mozilla", sent.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://example.com/watch", sent.Headers.Referrer?.ToString());
    }

    [Fact]
    public async Task Hotlink_protected_content_downloads_because_of_the_user_agent()
    {
        // OPEN-3, reproduced: 403 without a browser UA, 200/206 with one. This
        // is the direction that matters — a VELO-branded UA fails here, and
        // that would look like "the site blocks downloads" rather than a bug.
        var body = Payload(50_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "m.mp4");

        var downloader = new MediaDownloader(new HttpClient(new HotlinkProtectedHandler(body)));
        var result = await downloader.DownloadAsync(Url, dest);

        Assert.True(result.Success, result.Error);
        Assert.Equal(body, await File.ReadAllBytesAsync(dest));
    }

    [Fact]
    public async Task A_velo_branded_user_agent_is_refused_which_is_why_the_default_is_a_browser_one()
    {
        // The negative half of the test above: proves the handler really is
        // gating on the header, so the pass above is not a coincidence.
        var dir = NewTempDir();
        var downloader = new MediaDownloader(
            new HttpClient(new HotlinkProtectedHandler(Payload(50_000))),
            userAgent: "VELO-Browser/2.5.0");

        var result = await downloader.DownloadAsync(Url, Path.Combine(dir, "m.mp4"));

        Assert.False(result.Success);
        Assert.Contains("403", result.Error!, StringComparison.Ordinal);
    }

    // ── Cancel and resume ────────────────────────────────────────────────

    [Fact]
    public async Task Cancelling_leaves_no_file_at_the_destination_but_keeps_the_resume_point()
    {
        var body = Payload(400_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        using var cts = new CancellationTokenSource();
        var downloader = new MediaDownloader(new HttpClient(new TricklingHandler(body)));

        // Cancel once bytes have DEMONSTRABLY been read, not after a fixed
        // delay. The first version slept 200 ms and failed once under the
        // full-solution run, where six test assemblies compete for the
        // scheduler. Progress fires per read, so it is a real signal — unlike
        // the .part length, which stays 0 until the 64 KB FileStream buffer
        // flushes on dispose.
        var received = 0L;
        var task = downloader.DownloadAsync(Url, dest,
            progress: new SyncProgress<(long Received, long Total)>(p => Interlocked.Exchange(ref received, p.Received)),
            ct: cts.Token);

        for (var i = 0; i < 300 && Interlocked.Read(ref received) == 0; i++) await Task.Delay(10);
        Assert.True(Interlocked.Read(ref received) > 0, "no bytes were read before cancelling");

        cts.Cancel();
        var result = await task;

        Assert.False(result.Success);
        Assert.Equal("cancelled", result.Error);
        Assert.False(File.Exists(dest));               // never a half file in view
        Assert.True(File.Exists(dest + ".part"));      // …but the bytes survive
        Assert.True(MediaDownloader.PartialLength(dest) > 0);
    }

    [Fact]
    public async Task Resuming_completes_the_file_and_matches_the_source()
    {
        var body = Payload(300_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        // Pretend an earlier attempt got the first 100 KB.
        await File.WriteAllBytesAsync(dest + ".part", body[..100_000]);

        var handler = new MediaHandler(body);
        var result  = await new MediaDownloader(new HttpClient(handler)).DownloadAsync(Url, dest);

        Assert.True(result.Success, result.Error);
        Assert.True(result.Resumed);
        Assert.Equal(body.Length, result.BytesWritten);
        Assert.Equal(body, await File.ReadAllBytesAsync(dest));
        Assert.Equal(100_000, handler.LastRequest!.Headers.Range!.Ranges.First().From);
    }

    [Fact]
    public async Task A_server_that_ignores_Range_restarts_instead_of_corrupting_the_file()
    {
        // The failure this prevents is the nastiest kind: appending a
        // from-zero body to a partial gives a file of EXACTLY the expected
        // length whose middle is garbage. Length checks and "it downloaded
        // fine" both pass; only playback fails, later, on the user's machine.
        var body = Payload(300_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        await File.WriteAllBytesAsync(dest + ".part", body[..100_000]);

        var result = await new MediaDownloader(
            new HttpClient(new MediaHandler(body, honourRange: false))).DownloadAsync(Url, dest);

        Assert.True(result.Success, result.Error);
        Assert.False(result.Resumed);                              // it started over
        Assert.Equal(body.Length, result.BytesWritten);            // not 400_000
        Assert.Equal(body, await File.ReadAllBytesAsync(dest));    // and it is correct
    }

    [Fact]
    public async Task A_partial_that_is_already_complete_is_promoted_not_refetched()
    {
        var body = Payload(120_000);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        await File.WriteAllBytesAsync(dest + ".part", body);   // the whole thing

        var result = await new MediaDownloader(new HttpClient(new MediaHandler(body))).DownloadAsync(Url, dest);

        Assert.True(result.Success, result.Error);
        Assert.Equal(body, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task DiscardPartial_throws_the_resume_point_away()
    {
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");
        await File.WriteAllBytesAsync(dest + ".part", Payload(1_000));

        MediaDownloader.DiscardPartial(dest);

        Assert.Equal(0, MediaDownloader.PartialLength(dest));
    }

    // ── Failures ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_failed_request_produces_no_destination_file()
    {
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "movie.mp4");

        var downloader = new MediaDownloader(new HttpClient(new HotlinkProtectedHandler(Payload(10)))
            , userAgent: "not-a-browser");
        var result = await downloader.DownloadAsync(Url, dest);

        Assert.False(result.Success);
        Assert.False(File.Exists(dest));
    }

    // ── P2a-2 — segmented downloads ──────────────────────────────────────

    /// <summary>Serves a distinct body per URL, and can be told to fail one.</summary>
    private sealed class SegmentHandler(Dictionary<string, byte[]> bodies, string? failUrl = null) : HttpMessageHandler
    {
        public List<string> RequestedInOrder { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            RequestedInOrder.Add(url);

            if (url == failUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(bodies.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (Dictionary<string, byte[]> Bodies, List<string> Urls, byte[] Joined) BuildSegments(int count)
    {
        var bodies = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var urls   = new List<string>();
        var joined = new List<byte>();

        for (var i = 0; i < count; i++)
        {
            var url  = $"https://cdn.example/seg{i}.ts";
            var body = Enumerable.Range(0, 1000).Select(_ => (byte)i).ToArray();
            bodies[url] = body;
            urls.Add(url);
            joined.AddRange(body);
        }

        return (bodies, urls, [.. joined]);
    }

    [Fact]
    public async Task Segments_are_concatenated_in_playlist_order()
    {
        // Order is the file. Each segment here is filled with its own index,
        // so a reordered fetch produces different bytes and this fails.
        var (bodies, urls, joined) = BuildSegments(6);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "stream.ts");

        var handler = new SegmentHandler(bodies);
        var result  = await new MediaDownloader(new HttpClient(handler))
            .DownloadSegmentsAsync(urls, dest);

        Assert.True(result.Success, result.Error);
        Assert.Equal(joined, await File.ReadAllBytesAsync(dest));
        Assert.Equal(urls, handler.RequestedInOrder);
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task The_initialisation_segment_leads_the_file_exactly_once()
    {
        // fMP4: the init segment is not part of the playlist's segment list
        // and must be written first, once. Prepending it twice, or appending
        // it, gives a file no player will open.
        var (bodies, urls, joined) = BuildSegments(3);
        var initUrl = "https://cdn.example/init.mp4";
        var init    = new byte[] { 9, 9, 9, 9 };
        bodies[initUrl] = init;

        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "stream.mp4");

        var handler = new SegmentHandler(bodies);
        var result  = await new MediaDownloader(new HttpClient(handler))
            .DownloadSegmentsAsync(urls, dest, initSegmentUrl: initUrl);

        Assert.True(result.Success, result.Error);
        Assert.Equal(init.Concat(joined), await File.ReadAllBytesAsync(dest));
        Assert.Equal(initUrl, handler.RequestedInOrder[0]);
        Assert.Single(handler.RequestedInOrder.Where(u => u == initUrl));
    }

    [Fact]
    public async Task One_bad_segment_stops_the_job_and_leaves_no_file()
    {
        // A hole in the middle produces a file that plays until it doesn't —
        // the worst outcome, because it looks like it worked.
        var (bodies, urls, _) = BuildSegments(6);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "stream.ts");

        var result = await new MediaDownloader(new HttpClient(new SegmentHandler(bodies, failUrl: urls[3])))
            .DownloadSegmentsAsync(urls, dest);

        Assert.False(result.Success);
        Assert.Contains("segment 4/6", result.Error!);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Progress_counts_segments_not_just_bytes()
    {
        var (bodies, urls, joined) = BuildSegments(5);
        var dir = NewTempDir();

        (int Done, int Total, long Bytes) last = (0, 0, 0);
        await new MediaDownloader(new HttpClient(new SegmentHandler(bodies)))
            .DownloadSegmentsAsync(urls, Path.Combine(dir, "s.ts"),
                progress: new SyncProgress<(int, int, long)>(p => last = p));

        Assert.Equal(5, last.Done);
        Assert.Equal(5, last.Total);
        Assert.Equal(joined.Length, last.Bytes);
    }

    [Fact]
    public async Task Cancelling_a_segmented_job_keeps_no_partial()
    {
        // Unlike a single-URL transfer, no resume point is kept: the segment
        // in flight is probably half written, and splicing the rest of it in
        // later would corrupt the middle of the file.
        var (bodies, urls, _) = BuildSegments(40);
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "stream.ts");

        using var cts = new CancellationTokenSource();
        var done = 0;
        var task = new MediaDownloader(new HttpClient(new SegmentHandler(bodies)))
            .DownloadSegmentsAsync(urls, dest,
                progress: new SyncProgress<(int Done, int Total, long Bytes)>(p =>
                {
                    Interlocked.Exchange(ref done, p.Done);
                    if (p.Done >= 3) cts.Cancel();
                }),
                ct: cts.Token);

        var result = await task;

        Assert.False(result.Success);
        Assert.Equal("cancelled", result.Error);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task An_empty_segment_list_fails_cleanly()
    {
        var result = await new MediaDownloader(new HttpClient(new SegmentHandler([])))
            .DownloadSegmentsAsync([], Path.Combine(NewTempDir(), "s.ts"));

        Assert.False(result.Success);
        Assert.Equal("no segments", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_input_fails_without_touching_the_disk(string url)
    {
        var result = await new MediaDownloader(new HttpClient(new MediaHandler(Payload(10))))
            .DownloadAsync(url, Path.Combine(NewTempDir(), "m.mp4"));

        Assert.False(result.Success);
        Assert.Equal("empty url", result.Error);
    }
}
