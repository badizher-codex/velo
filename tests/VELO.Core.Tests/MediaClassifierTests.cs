using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / P1 Gate 1.
///
/// Every string in this file was measured, not invented — they come from the
/// captures recorded in docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md §9–§10. That
/// matters: the first version of this classifier was going to be written
/// against the types the formats are *supposed* to use, and every one of the
/// four traps below would have shipped.
///
/// The negative tests come first on purpose. Three of the four failures the
/// measurement found are false POSITIVES — a rule that happily reports media
/// where there is none — and a suite that only proves the happy path would
/// have gone green on all of them (lesson #44).
/// </summary>
public class MediaClassifierTests
{
    // ── Negatives — the four traps, each measured ────────────────────────

    [Fact]
    public void Font_served_as_octet_stream_is_not_media()
    {
        // Measured on cdnjs during the hls.js capture. HLS segments answer
        // application/octet-stream too, so a rule that treats the type as
        // media would have put Font Awesome in the download list.
        var font = new ResponseSignals(
            "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.1/webfonts/fa-solid-900.woff2",
            "application/octet-stream; charset=utf-8",
            ContentLength: "156496");

        Assert.Equal(MediaClass.NotMedia, MediaClassifier.ClassifyResponse(font));
    }

    [Fact]
    public void Hls_segment_is_media_only_because_a_manifest_named_it()
    {
        // The same type as the font above, and 13 MB of real video. Nothing in
        // the response tells them apart — only provenance does.
        var segment = new ResponseSignals(
            "https://test-streams.mux.dev/x36xhzz/url_8/url_591/193039199_mp4_h264_aac_fhd_7.ts",
            "application/octet-stream",
            ContentLength: "13184440");

        Assert.Equal(MediaClass.NotMedia, MediaClassifier.ClassifyResponse(segment));
        Assert.Equal(MediaClass.Segment,
            MediaClassifier.ClassifyResponse(segment, referencedByManifest: true));
    }

    [Fact]
    public void YouTube_stream_is_not_recognisable_on_the_network_layer()
    {
        // application/vnd.yt-ump is in no registry, and the URL carries no
        // itag, mime or range parameter. This asserts the honest answer: the
        // network layer cannot see it, and the page layer is what covers it.
        var ump = new ResponseSignals(
            "https://rr10---sn-0opoxu-j8we.googlevideo.com/videoplayback?expire=1786397306&sabr=1&rn=4",
            "application/vnd.yt-ump");

        Assert.Equal(MediaClass.NotMedia, MediaClassifier.ClassifyResponse(ump));
    }

    [Fact]
    public void Url_extension_never_decides()
    {
        // P0's finding, pinned: 370 requests contained zero media extensions,
        // and an .mp4 in a URL says nothing about what came back.
        var htmlAtMp4Url = new ResponseSignals(
            "https://example.com/watch/movie.mp4",
            "text/html; charset=utf-8");

        Assert.Equal(MediaClass.NotMedia, MediaClassifier.ClassifyResponse(htmlAtMp4Url));
    }

    [Fact]
    public void Hls_manifest_is_a_manifest_even_when_it_calls_itself_audio()
    {
        // Measured: test-streams.mux.dev serves both the master and the
        // variant playlist of an H.264+AAC VIDEO stream as "audio/mpegurl".
        // Classified as an audio track, this offers an audio-only download of
        // a video — a wrong file, silently.
        foreach (var type in new[]
        {
            "audio/mpegurl",
            "audio/x-mpegurl",
            "application/x-mpegurl",
            "application/vnd.apple.mpegurl",
        })
        {
            var manifest = new ResponseSignals("https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8", type);
            Assert.Equal(MediaClass.HlsManifest, MediaClassifier.ClassifyResponse(manifest));
            // And it is not a track, in the other direction.
            Assert.Equal(TrackKind.Unknown, MediaClassifier.ClassifyTrack(type));
        }
    }

    [Fact]
    public void UI_sound_effects_are_not_user_content()
    {
        // The four beeps YouTube ships (open/success/failure/no_input.mp3),
        // 6.1-7.0 KB each. These are the same false positives P0 found under
        // ResourceContext == Media, and a plain audio/* rule finds them again.
        foreach (var bytes in new long[] { 6167, 6529, 6636, 6953 })
        {
            var beep = new ResponseSignals(
                "https://www.youtube.com/s/search/audio/open.mp3",
                "audio/mpeg",
                ContentLength: bytes.ToString());

            var cls = MediaClassifier.ClassifyResponse(beep);
            Assert.Equal(MediaClass.ProgressiveMedia, cls);   // it IS an audio file…
            Assert.False(MediaClassifier.IsUserContent(cls, MediaClassifier.ResolveTotalBytes(beep)));
        }                                                     // …but not content.
    }

    // ── Positives ────────────────────────────────────────────────────────

    [Fact]
    public void Progressive_video_is_the_one_case_headers_settle_alone()
    {
        var mp4 = new ResponseSignals(
            "https://www.w3schools.com/html/mov_bbb.mp4",
            "video/mp4",
            ContentLength: "788493",
            ContentRange:  "bytes 0-788492/788493");

        var cls = MediaClassifier.ClassifyResponse(mp4);
        Assert.Equal(MediaClass.ProgressiveMedia, cls);
        Assert.Equal(788493, MediaClassifier.ResolveTotalBytes(mp4));
        Assert.True(MediaClassifier.IsUserContent(cls, 788493));
    }

    [Fact]
    public void Dash_manifest_is_recognised()
    {
        Assert.Equal(MediaClass.DashManifest, MediaClassifier.ClassifyResponse(
            new ResponseSignals("https://example.com/stream.mpd", "application/dash+xml")));
    }

    [Theory]
    [InlineData("VIDEO/MP4", MediaClass.ProgressiveMedia)]
    [InlineData("video/mp4; charset=utf-8", MediaClass.ProgressiveMedia)]
    [InlineData("  audio/mpegurl  ", MediaClass.HlsManifest)]
    [InlineData("", MediaClass.NotMedia)]
    public void Content_type_is_normalised_before_matching(string contentType, MediaClass expected)
    {
        // The captured headers carry charset parameters and mixed case at
        // random; matching the raw string would miss half of them.
        Assert.Equal(expected, MediaClassifier.ClassifyResponse(
            new ResponseSignals("https://example.com/x", contentType)));
    }

    // ── Total size ───────────────────────────────────────────────────────

    [Fact]
    public void Total_size_prefers_content_range_over_content_length()
    {
        // A ranged response reports only the slice in Content-Length. Reading
        // that as the total under-reports a partial fetch badly.
        var partial = new ResponseSignals("https://example.com/v.mp4", "video/mp4",
            ContentLength: "1024", ContentRange: "bytes 0-1023/788493");

        Assert.Equal(788493, MediaClassifier.ResolveTotalBytes(partial));
    }

    [Fact]
    public void Unknown_total_in_content_range_falls_back_to_content_length()
    {
        var openEnded = new ResponseSignals("https://example.com/v.mp4", "video/mp4",
            ContentLength: "1024", ContentRange: "bytes 0-1023/*");

        Assert.Equal(1024, MediaClassifier.ResolveTotalBytes(openEnded));
    }

    [Fact]
    public void Missing_size_headers_yield_zero_not_a_guess()
    {
        // Measured: every vnd.yt-ump response arrived with neither header.
        Assert.Equal(0, MediaClassifier.ResolveTotalBytes(
            new ResponseSignals("https://example.com/x", "application/vnd.yt-ump")));
    }

    // ── Tracks — the audio/video split the feature is built on ───────────

    [Theory]
    [InlineData("audio/webm; codecs=\"opus\"",      TrackKind.Audio, "opus")]
    [InlineData("video/mp4; codecs=\"av01.0.08M.08\"", TrackKind.Video, "av01.0.08M.08")]
    [InlineData("audio/mp4;codecs=mp4a.40.2",       TrackKind.Audio, "mp4a.40.2")]
    [InlineData("video/mp4;codecs=avc1.64001f",     TrackKind.Video, "avc1.64001f")]
    public void SourceBuffer_mime_yields_track_kind_and_codecs(
        string mime, TrackKind expectedKind, string expectedCodecs)
    {
        // All four measured live: the first two on YouTube, the last two on
        // the hls.js demo. Note the quoting and spacing differ between sites —
        // that is why parsing is not a split on '"'.
        Assert.Equal(expectedKind,   MediaClassifier.ClassifyTrack(mime));
        Assert.Equal(expectedCodecs, MediaClassifier.ExtractCodecs(mime));
    }

    [Fact]
    public void Missing_codecs_returns_empty_rather_than_guessing()
    {
        Assert.Equal("", MediaClassifier.ExtractCodecs("video/mp4"));
        Assert.Equal("", MediaClassifier.ExtractCodecs(""));
        Assert.Equal("", MediaClassifier.ExtractCodecs(null));
    }

    // ── DRM — negative first, it is the rule most likely to be wrong ─────

    [Fact]
    public void Probing_thirteen_key_systems_is_not_protected_content()
    {
        // bitmovin's demo probes 13 key systems on load with nothing protected
        // playing (P0). A rule keyed on requestMediaKeySystemAccess refuses
        // downloads on any site that feature-detects — which is most of them.
        var probingOnly = new DrmSignals(KeySystemsProbed: 13, KeySystemsResolved: 13);

        Assert.False(MediaClassifier.IsProtected(probingOnly));
    }

    [Fact]
    public void Free_content_measured_clean_on_every_counter()
    {
        // Both free sources in the Gate 0.5 capture: no EME at all, and no
        // encryption boxes in either initialisation segment.
        Assert.False(MediaClassifier.IsProtected(new DrmSignals()));
    }

    [Theory]
    [InlineData(1, 0, false, false)]   // keys actually attached
    [InlineData(0, 1, false, false)]   // an encrypted event fired
    [InlineData(0, 0, true,  false)]   // pssh in the init segment
    [InlineData(0, 0, false, true)]    // sinf in the init segment
    public void Actual_use_is_protected(int setMediaKeys, int encrypted, bool pssh, bool sinf)
    {
        var used = new DrmSignals(
            KeySystemsProbed: 1,
            KeySystemsResolved: 1,
            SetMediaKeysCalls: setMediaKeys,
            EncryptedEvents: encrypted,
            InitSegmentHasPssh: pssh,
            InitSegmentHasSinf: sinf);

        Assert.True(MediaClassifier.IsProtected(used));
    }
}
