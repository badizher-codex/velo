using System.Collections.Concurrent;
using System.IO;
using System.Text;
using VELO.Core;

namespace VELO.UI.Utilities;

/// <summary>
/// TEMPORARY — Phase 6 / P1 Gate 0 instrumentation. DELETE once the
/// Content-Type table is recorded in docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md.
///
/// Why this exists: P0 concluded that media must be classified by the
/// response's Content-Type, but it never measured a single one — P0 only
/// instrumented <c>WebResourceRequested</c>, which carries the request and
/// therefore no response headers. "Classify by Content-Type" is a conclusion
/// by elimination (URL extensions matched nothing; ResourceContext reported
/// YouTube's video stream as XmlHttpRequest), not a measurement. Lesson #40
/// says measure the hypothesis before coding against it, so this writes down
/// what the servers actually answer before the classifier is written.
///
/// Off unless <c>VELO_MEDIA_PROBE=1</c> is set in the environment, so a
/// normal run pays one static bool check per response and nothing else.
/// Output: <c>%LOCALAPPDATA%\VELO\logs\media-probe.tsv</c>, tab-separated so
/// it drops straight into a table.
///
/// Removal is one file plus the single call in BrowserTab.Events.cs
/// (OnWebResourceResponseReceived) — the hook itself stays, that is what
/// Gate 1's classifier hangs off.
/// </summary>
public static class MediaProbeLog
{
    /// <summary>
    /// Read once at type-load. Flipping the variable needs an app restart —
    /// deliberate: the probe is a measurement session, not a live toggle.
    /// </summary>
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("VELO_MEDIA_PROBE") == "1";

    // No resourceContext column: CoreWebView2WebResourceResponseReceivedEventArgs
    // exposes only Request and Response, so P0's "the stream arrives tagged
    // XmlHttpRequest" cannot be re-confirmed on this event. contentRange takes
    // its place and is worth more here — on a 206 it carries the full asset
    // size, which a Range-driven endpoint never reports in Content-Length.
    private const string Header =
        "timestamp\ttabId\tstatus\tcontentType\tcontentLength\trangeRequest\tcontentRange\turi";

    private static readonly ConcurrentQueue<string> _pending = new();
    private static readonly object _fileLock = new();
    private static System.Threading.Timer? _flushTimer;
    private static string? _path;
    private static string? _pagePath;

    /// <summary>
    /// Records one response. Called from the WebView2 UI thread, so it only
    /// enqueues — the file write happens on the timer thread.
    /// </summary>
    public static void Record(
        string tabId,
        string uri,
        int    statusCode,
        string contentType,
        string contentLength,
        string rangeRequest,
        string contentRange)
    {
        if (!Enabled) return;

        EnsureStarted();

        // Tabs are stripped, not escaped: a header value containing a tab
        // would silently shift every later column of the table.
        static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        _pending.Enqueue(string.Join('\t',
            DateTime.Now.ToString("HH:mm:ss.fff"),
            Clean(tabId),
            statusCode.ToString(),
            Clean(contentType),
            Clean(contentLength),
            Clean(rangeRequest),
            Clean(contentRange),
            Clean(uri)));
    }

    /// <summary>
    /// Records one page-side report from media-detect.js. Written to a
    /// separate file: the payload is a JSON blob per line, which would wreck
    /// the tab-separated network table.
    /// </summary>
    public static void RecordPage(string tabId, string host, string json)
    {
        if (!Enabled) return;

        EnsureStarted();

        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff}\t{tabId}\t{host}\t" +
                       json.Replace('\n', ' ').Replace('\r', ' ');
            lock (_fileLock)
                File.AppendAllText(_pagePath!, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* the probe must never take the browser down */ }
    }

    // ── Gate P2b-0 — bridge throughput bench (TEMPORARY) ─────────────────
    //
    // Times how fast the host can drain page→host messages carrying base64
    // payloads. The page reports how long IT took to hand them off; what
    // matters more is the interval between the first and last arrival here,
    // because that is the UI thread actually doing the work.

    private static long     _benchBytes;
    private static int      _benchChunks;
    private static DateTime _benchFirst;
    private static DateTime _benchLast;
    private static double   _benchEncodeMs;
    private static int      _benchChunkBytes;

    public static void BenchStart(int chunkBytes, int chunks, double encodeMs)
    {
        _benchBytes      = 0;
        _benchChunks     = 0;
        _benchFirst      = default;
        _benchEncodeMs   = encodeMs;
        _benchChunkBytes = chunkBytes;
        RecordPage("bench", "", $"BENCH start chunkBytes={chunkBytes} chunks={chunks} encodeMs={encodeMs:F1}");
    }

    public static void BenchChunk(int base64Length)
    {
        if (_benchFirst == default) _benchFirst = DateTime.UtcNow;
        _benchLast = DateTime.UtcNow;
        _benchChunks++;
        _benchBytes += base64Length;
    }

    public static void BenchEnd(double pagePostMs)
    {
        if (_benchChunks == 0) { RecordPage("bench", "", "BENCH end — no chunks arrived"); return; }

        var hostMs = (_benchLast - _benchFirst).TotalMilliseconds;

        // Payload bytes are what a sink would actually be moving; wire bytes
        // are what the bridge carried, base64 inflation included.
        var payloadBytes = (long)_benchChunks * _benchChunkBytes;
        var wireMBs      = hostMs > 0 ? _benchBytes / 1048576.0 / (hostMs / 1000.0) : 0;
        var payloadMBs   = hostMs > 0 ? payloadBytes / 1048576.0 / (hostMs / 1000.0) : 0;

        RecordPage("bench", "",
            $"BENCH end chunks={_benchChunks} wireBytes={_benchBytes} payloadBytes={payloadBytes} " +
            $"hostDrainMs={hostMs:F0} pagePostMs={pagePostMs:F0} encodeMs={_benchEncodeMs:F1} " +
            $"wireMB/s={wireMBs:F2} payloadMB/s={payloadMBs:F2}");
    }

    private static void EnsureStarted()
    {
        if (_flushTimer != null) return;

        lock (_fileLock)
        {
            if (_flushTimer != null) return;

            try
            {
                var dir = DataLocation.SubPath("logs");
                _path     = Path.Combine(dir, "media-probe.tsv");
                _pagePath = Path.Combine(dir, "media-probe-page.tsv");
                if (!File.Exists(_path))
                    File.WriteAllText(_path, Header + Environment.NewLine, Encoding.UTF8);
                if (!File.Exists(_pagePath))
                    File.WriteAllText(_pagePath, "timestamp\ttabId\thost\tpayload" + Environment.NewLine, Encoding.UTF8);

                // Session marker — a probe run that captures nothing must be
                // distinguishable from a probe that never started.
                File.AppendAllText(
                    _path,
                    $"# session start {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch { /* the probe must never take the browser down */ }

            _flushTimer = new System.Threading.Timer(_ => Flush(), null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>Writes everything buffered. Safe to call at any time.</summary>
    public static void Flush()
    {
        if (_path is null || _pending.IsEmpty) return;

        var batch = new StringBuilder();
        while (_pending.TryDequeue(out var line))
            batch.AppendLine(line);

        if (batch.Length == 0) return;

        try
        {
            lock (_fileLock)
                File.AppendAllText(_path, batch.ToString(), Encoding.UTF8);
        }
        catch { /* see above */ }
    }
}
