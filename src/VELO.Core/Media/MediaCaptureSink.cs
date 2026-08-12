using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Core.Media;

/// <summary>Why a capture stopped.</summary>
public enum CaptureOutcome { Completed, Cancelled, Failed }

/// <summary>Outcome of one capture.</summary>
public sealed record MediaCaptureResult(
    CaptureOutcome Outcome,
    string         FilePath,
    long           Bytes,
    int            Chunks,
    string?        Error);

/// <summary>
/// Phase 6 / P2b — writes the bytes a page appends to a SourceBuffer.
///
/// This is the half of the feature the network layer cannot do. §10 measured
/// that YouTube exposes no per-track URL at all — one <c>videoplayback</c>
/// endpoint, SABR, and the tracks only exist demultiplexed inside the page —
/// so for those streams the only place the audio and the video exist as
/// separate byte sequences is at <c>appendBuffer</c>.
///
/// Gate P2b-0 settled the transport: the page→host bridge drains 90–122 MB/s
/// against a ~3 MB/s requirement and does not disturb playback, so the chunks
/// arrive as base64 over <c>postMessage</c> and no second channel is needed.
///
/// Three properties this type is responsible for:
///
///   • <b>Order is the file.</b> Chunks carry a sequence number and are
///     written only in order. postMessage preserves ordering, so a gap means
///     something is wrong rather than something to paper over — the capture
///     fails loudly instead of writing a file with a hole.
///   • <b>Nothing accumulates.</b> Bytes are decoded and written as they
///     arrive; the sink holds one chunk at a time. The page must not buffer
///     either — §10 measured 91 MB in 40 s on a single track.
///   • <b>The destination never holds a partial.</b> Same <c>.part</c> then
///     rename as the downloader, so an interrupted capture cannot be mistaken
///     for a finished file.
/// </summary>
public sealed class MediaCaptureSink : IDisposable
{
    private readonly ILogger<MediaCaptureSink> _logger;
    private readonly object _lock = new();

    private FileStream? _stream;
    private string _destination = "";
    private string _partPath    = "";
    private int    _expectedSeq;
    private long   _bytes;
    private int    _chunks;
    private string? _error;

    /// <summary>Identifies the capture the page is feeding. Empty when idle.</summary>
    public string CaptureId { get; private set; } = "";

    /// <summary>True between <see cref="Begin"/> and <see cref="Finish"/>.</summary>
    public bool IsCapturing { get { lock (_lock) return _stream is not null; } }

    /// <summary>Bytes written so far.</summary>
    public long Bytes { get { lock (_lock) return _bytes; } }

    public MediaCaptureSink(ILogger<MediaCaptureSink>? logger = null)
        => _logger = logger ?? NullLogger<MediaCaptureSink>.Instance;

    /// <summary>
    /// Opens a capture. Any capture already running is abandoned — one sink
    /// writes one file, and a second Begin means the first will never finish.
    /// </summary>
    public void Begin(string captureId, string destinationPath)
    {
        lock (_lock)
        {
            AbortLocked();

            _destination = destinationPath;
            _partPath    = destinationPath + ".part";
            _expectedSeq = 0;
            _bytes       = 0;
            _chunks      = 0;
            _error       = null;
            CaptureId    = captureId;

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            _stream = new FileStream(_partPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
            _logger.LogInformation("Media capture started: {Id} → {Path}", captureId, destinationPath);
        }
    }

    /// <summary>
    /// Writes one chunk. Returns false when the chunk does not belong to the
    /// running capture, arrives out of order, or cannot be decoded — in which
    /// case the capture is marked failed and stops accepting data.
    /// </summary>
    public bool Write(string captureId, int sequence, string base64)
    {
        lock (_lock)
        {
            if (_stream is null) return false;
            if (!string.Equals(captureId, CaptureId, StringComparison.Ordinal)) return false;

            if (sequence != _expectedSeq)
            {
                // postMessage preserves order, so this is not a race to
                // tolerate — writing anyway would produce a file whose middle
                // is silently wrong, which is the failure mode this whole
                // phase keeps trying to avoid.
                Fail($"chunk out of order: expected {_expectedSeq}, got {sequence}");
                return false;
            }

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException ex) { Fail("undecodable chunk: " + ex.Message); return false; }

            try
            {
                _stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return false;
            }

            _expectedSeq++;
            _chunks++;
            _bytes += bytes.Length;
            return true;
        }
    }

    /// <summary>
    /// Ends the capture. On success the <c>.part</c> is renamed into place; a
    /// failed or empty capture leaves nothing behind, because a zero-byte file
    /// named after a video is worse than no file.
    /// </summary>
    public MediaCaptureResult Finish(bool cancelled = false)
    {
        lock (_lock)
        {
            if (_stream is null)
                return new MediaCaptureResult(CaptureOutcome.Failed, "", 0, 0, "no capture running");

            var bytes  = _bytes;
            var chunks = _chunks;
            var error  = _error;

            _stream.Dispose();
            _stream = null;

            if (error is not null || cancelled || bytes == 0)
            {
                TryDelete(_partPath);
                var outcome = error is not null ? CaptureOutcome.Failed : CaptureOutcome.Cancelled;
                _logger.LogInformation("Media capture ended without a file: {Outcome} {Error}", outcome, error);
                return new MediaCaptureResult(outcome, "", bytes, chunks,
                    error ?? (bytes == 0 ? "nothing was captured" : "cancelled"));
            }

            try
            {
                if (File.Exists(_destination)) File.Delete(_destination);
                File.Move(_partPath, _destination);
            }
            catch (Exception ex)
            {
                TryDelete(_partPath);
                return new MediaCaptureResult(CaptureOutcome.Failed, "", bytes, chunks, ex.Message);
            }

            _logger.LogInformation("Media capture complete: {Bytes} bytes in {Chunks} chunks → {Path}",
                bytes, chunks, _destination);

            return new MediaCaptureResult(CaptureOutcome.Completed, _destination, bytes, chunks, null);
        }
    }

    private void Fail(string message)
    {
        _error = message;
        _logger.LogWarning("Media capture failed: {Message}", message);
    }

    private void AbortLocked()
    {
        if (_stream is null) return;
        _stream.Dispose();
        _stream = null;
        TryDelete(_partPath);
    }

    private static void TryDelete(string path)
    {
        try { if (path.Length > 0 && File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        lock (_lock) AbortLocked();
    }
}
