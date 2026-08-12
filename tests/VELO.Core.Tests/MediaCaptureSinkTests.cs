using System.IO;
using System.Text;
using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / P2b — the MSE capture sink.
///
/// This is the half of the feature the network layer cannot do: §10 measured
/// that YouTube exposes no per-track URL at all, so for those streams the
/// audio and the video only exist as separate byte sequences inside the page.
///
/// The negatives matter more than the happy path here. A capture that writes
/// a file with a silently wrong middle is the worst outcome — it looks like it
/// worked, and only fails later on someone else's player.
/// </summary>
public class MediaCaptureSinkTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "velo-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    // ── The happy path ───────────────────────────────────────────────────

    [Fact]
    public void Chunks_are_written_in_order_and_promoted_on_finish()
    {
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "audio.webm");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        Assert.True(sink.Write("cap1", 0, B64("hello ")));
        Assert.True(sink.Write("cap1", 1, B64("brave ")));
        Assert.True(sink.Write("cap1", 2, B64("world")));
        var result = sink.Finish();

        Assert.Equal(CaptureOutcome.Completed, result.Outcome);
        Assert.Equal(dest, result.FilePath);
        Assert.Equal(3, result.Chunks);
        Assert.Equal("hello brave world", File.ReadAllText(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void The_destination_holds_nothing_until_the_capture_finishes()
    {
        // A capture runs for as long as the video plays. If the destination
        // existed throughout, a half-written file would be indistinguishable
        // from a finished one for the whole of that time.
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        sink.Write("cap1", 0, B64("partial"));

        Assert.False(File.Exists(dest));
        Assert.True(File.Exists(dest + ".part"));
        Assert.True(sink.IsCapturing);

        sink.Finish();
        Assert.True(File.Exists(dest));
    }

    // ── The negatives ────────────────────────────────────────────────────

    [Fact]
    public void An_out_of_order_chunk_fails_the_capture_instead_of_writing_a_hole()
    {
        // postMessage preserves ordering, so a gap is not a race to tolerate —
        // it means something is wrong. Writing chunk 2 where chunk 1 belongs
        // produces a file whose middle is silently incorrect.
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        Assert.True(sink.Write("cap1", 0, B64("first")));
        Assert.False(sink.Write("cap1", 2, B64("third")));   // 1 is missing

        var result = sink.Finish();

        Assert.Equal(CaptureOutcome.Failed, result.Outcome);
        Assert.Contains("out of order", result.Error!);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void Chunks_from_another_capture_are_ignored()
    {
        // A page that reloads mid-capture, or a second tab, must not splice
        // its bytes into this file.
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        sink.Write("cap1", 0, B64("mine"));
        Assert.False(sink.Write("cap2", 1, B64("theirs")));

        var result = sink.Finish();

        Assert.Equal(CaptureOutcome.Completed, result.Outcome);
        Assert.Equal("mine", File.ReadAllText(dest));
    }

    [Fact]
    public void An_undecodable_chunk_fails_the_capture()
    {
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        sink.Write("cap1", 0, B64("good"));
        Assert.False(sink.Write("cap1", 1, "!!! not base64 !!!"));

        var result = sink.Finish();

        Assert.Equal(CaptureOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Cancelling_leaves_no_file()
    {
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        sink.Write("cap1", 0, B64("some bytes"));
        var result = sink.Finish(cancelled: true);

        Assert.Equal(CaptureOutcome.Cancelled, result.Outcome);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void A_capture_that_received_nothing_produces_no_file()
    {
        // Arming a capture and never playing is the likeliest way to get here.
        // A zero-byte file named after a video is worse than no file.
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", dest);
        var result = sink.Finish();

        Assert.Equal(CaptureOutcome.Cancelled, result.Outcome);
        Assert.Equal("nothing was captured", result.Error);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Writing_with_no_capture_running_is_refused_rather_than_throwing()
    {
        // The page can post whatever it likes, including after a capture ends.
        using var sink = new MediaCaptureSink();

        Assert.False(sink.Write("cap1", 0, B64("stray")));
        Assert.Equal(CaptureOutcome.Failed, sink.Finish().Outcome);
    }

    [Fact]
    public void Beginning_a_second_capture_abandons_the_first_without_leaving_a_partial()
    {
        var dir   = NewTempDir();
        var first = Path.Combine(dir, "first.mp4");
        var second = Path.Combine(dir, "second.mp4");
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", first);
        sink.Write("cap1", 0, B64("abandoned"));

        sink.Begin("cap2", second);
        sink.Write("cap2", 0, B64("kept"));
        var result = sink.Finish();

        Assert.Equal(CaptureOutcome.Completed, result.Outcome);
        Assert.Equal("kept", File.ReadAllText(second));
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(first + ".part"));
    }

    [Fact]
    public void Sequence_numbering_restarts_with_each_capture()
    {
        var dir = NewTempDir();
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", Path.Combine(dir, "a.mp4"));
        sink.Write("cap1", 0, B64("a"));
        sink.Finish();

        sink.Begin("cap2", Path.Combine(dir, "b.mp4"));
        Assert.True(sink.Write("cap2", 0, B64("b")));   // 0 again, not 1
        Assert.Equal(CaptureOutcome.Completed, sink.Finish().Outcome);
    }

    [Fact]
    public void Disposing_mid_capture_leaves_nothing_behind()
    {
        var dir  = NewTempDir();
        var dest = Path.Combine(dir, "video.mp4");

        using (var sink = new MediaCaptureSink())
        {
            sink.Begin("cap1", dest);
            sink.Write("cap1", 0, B64("interrupted"));
        }

        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void Bytes_are_reported_as_they_arrive()
    {
        // The panel shows progress from this; a capture has no known total, so
        // the running byte count is all there is to show.
        var dir = NewTempDir();
        using var sink = new MediaCaptureSink();

        sink.Begin("cap1", Path.Combine(dir, "v.mp4"));
        sink.Write("cap1", 0, Convert.ToBase64String(new byte[1000]));
        sink.Write("cap1", 1, Convert.ToBase64String(new byte[2500]));

        Assert.Equal(3500, sink.Bytes);
        sink.Finish();
    }
}
