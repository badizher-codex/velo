using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / P2a-2.
///
/// The fixtures are the real manifests from the stream this phase has been
/// measuring all along (test-streams.mux.dev), captured verbatim — tag order,
/// attribute order, relative URIs and all. Written against the spec's tidy
/// examples instead, two of the tests below would not exist: nothing in a
/// textbook example warns you that CODECS contains a comma, or that every URI
/// in the file is relative.
/// </summary>
public class HlsManifestParserTests
{
    private const string MasterUrl = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8";

    private const string RealMaster = """
        #EXTM3U
        #EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=2149280,CODECS="mp4a.40.2,avc1.64001f",RESOLUTION=1280x720,NAME="720"
        url_0/193039199_mp4_h264_aac_hd_7.m3u8
        #EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=246440,CODECS="mp4a.40.5,avc1.42000d",RESOLUTION=320x184,NAME="240"
        url_2/193039199_mp4_h264_aac_ld_7.m3u8
        #EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=460560,CODECS="mp4a.40.5,avc1.420016",RESOLUTION=512x288,NAME="380"
        url_4/193039199_mp4_h264_aac_7.m3u8
        #EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=836280,CODECS="mp4a.40.2,avc1.64001f",RESOLUTION=848x480,NAME="480"
        url_6/193039199_mp4_h264_aac_hq_7.m3u8
        #EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=6221600,CODECS="mp4a.40.2,avc1.640028",RESOLUTION=1920x1080,NAME="1080"
        url_8/193039199_mp4_h264_aac_fhd_7.m3u8
        """;

    private const string VariantUrl =
        "https://test-streams.mux.dev/x36xhzz/url_0/193039199_mp4_h264_aac_hd_7.m3u8";

    private const string RealVariant = """
        #EXTM3U
        #EXT-X-VERSION:3
        #EXT-X-PLAYLIST-TYPE:VOD
        #EXT-X-TARGETDURATION:11
        #EXTINF:10.000,
        url_462/193039199_mp4_h264_aac_hd_7.ts
        #EXTINF:10.000,
        url_463/193039199_mp4_h264_aac_hd_7.ts
        #EXTINF:10.000,
        url_464/193039199_mp4_h264_aac_hd_7.ts
        #EXT-X-ENDLIST
        """;

    // ── Which kind of playlist is this ───────────────────────────────────

    [Fact]
    public void Master_and_media_playlists_are_told_apart_by_their_tags()
    {
        // Both are .m3u8 and both were served as audio/mpegurl, so neither the
        // name nor the Content-Type can decide this.
        Assert.True(HlsManifestParser.IsMaster(RealMaster));
        Assert.False(HlsManifestParser.IsMaster(RealVariant));
    }

    // ── Master ───────────────────────────────────────────────────────────

    [Fact]
    public void Every_variant_is_read_with_its_attributes()
    {
        var variants = HlsManifestParser.ParseMaster(RealMaster, MasterUrl);

        Assert.Equal(5, variants.Count);

        var best = variants[0];
        Assert.Equal(6221600, best.Bandwidth);          // highest first
        Assert.Equal("1920x1080", best.Resolution);
        Assert.Equal("1080", best.Name);
    }

    [Fact]
    public void A_codecs_attribute_containing_a_comma_survives()
    {
        // CODECS="mp4a.40.2,avc1.64001f" — splitting the attribute list on
        // commas cuts this in half and yields "mp4a.40.2. Measured, not
        // hypothetical: every variant in the real master looks like this.
        var variants = HlsManifestParser.ParseMaster(RealMaster, MasterUrl);

        Assert.Equal("mp4a.40.2,avc1.640028", variants[0].Codecs);
    }

    [Fact]
    public void Relative_variant_uris_are_resolved_against_the_manifest()
    {
        // Every URI in the real master is relative. Left unresolved they are
        // unfetchable, and the failure would be a 404 far from the cause.
        var variants = HlsManifestParser.ParseMaster(RealMaster, MasterUrl);

        Assert.All(variants, v => Assert.StartsWith("https://test-streams.mux.dev/x36xhzz/url_", v.Url));
        Assert.Contains(variants, v =>
            v.Url == "https://test-streams.mux.dev/x36xhzz/url_8/193039199_mp4_h264_aac_fhd_7.m3u8");
    }

    // ── Media playlist ───────────────────────────────────────────────────

    [Fact]
    public void Segments_come_out_in_playback_order_fully_qualified()
    {
        var playlist = HlsManifestParser.ParseMediaPlaylist(RealVariant, VariantUrl);

        Assert.Equal(3, playlist.SegmentUrls.Count);
        Assert.Equal("https://test-streams.mux.dev/x36xhzz/url_0/url_462/193039199_mp4_h264_aac_hd_7.ts",
                     playlist.SegmentUrls[0]);
        // Order is the file — 462, 463, 464 and not any other permutation.
        Assert.Contains("url_463", playlist.SegmentUrls[1]);
        Assert.Contains("url_464", playlist.SegmentUrls[2]);
    }

    [Fact]
    public void Durations_are_summed_with_the_invariant_culture()
    {
        // "#EXTINF:10.000," is ten seconds. Parsed under a Spanish locale
        // without InvariantCulture it is ten thousand, and the estimate the
        // panel shows becomes nonsense.
        var playlist = HlsManifestParser.ParseMediaPlaylist(RealVariant, VariantUrl);

        Assert.Equal(30.0, playlist.TotalSeconds, precision: 3);
    }

    [Fact]
    public void An_endlist_marks_the_playlist_complete()
    {
        Assert.True(HlsManifestParser.ParseMediaPlaylist(RealVariant, VariantUrl).IsComplete);

        // Without it the stream is live: the segment list is a window, not the
        // asset, and downloading it would capture an arbitrary slice.
        var live = RealVariant.Replace("#EXT-X-ENDLIST", "");
        Assert.False(HlsManifestParser.ParseMediaPlaylist(live, VariantUrl).IsComplete);
    }

    [Fact]
    public void An_fmp4_playlist_yields_its_initialisation_segment()
    {
        // Not in the measured stream — that one is MPEG-TS — but Gate 0.5
        // measured fMP4 arriving at the MSE layer, so the case is real. The
        // init segment must lead the file exactly once.
        const string fmp4 = """
            #EXTM3U
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:4.000,
            seg1.m4s
            #EXTINF:4.000,
            seg2.m4s
            #EXT-X-ENDLIST
            """;

        var playlist = HlsManifestParser.ParseMediaPlaylist(fmp4, "https://cdn.example/v/stream.m3u8");

        Assert.Equal("https://cdn.example/v/init.mp4", playlist.InitSegmentUrl);
        Assert.Equal(2, playlist.SegmentUrls.Count);
    }

    [Fact]
    public void A_playlist_with_absolute_uris_is_left_alone()
    {
        const string absolute = """
            #EXTM3U
            #EXTINF:6.000,
            https://other.example/a.ts
            #EXT-X-ENDLIST
            """;

        var playlist = HlsManifestParser.ParseMediaPlaylist(absolute, "https://cdn.example/v/stream.m3u8");

        Assert.Equal("https://other.example/a.ts", Assert.Single(playlist.SegmentUrls));
    }

    // ── Degenerate input ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#EXTM3U")]
    public void Empty_or_contentless_input_yields_nothing_rather_than_throwing(string content)
    {
        Assert.Empty(HlsManifestParser.ParseMaster(content, MasterUrl));

        var playlist = HlsManifestParser.ParseMediaPlaylist(content, VariantUrl);
        Assert.Empty(playlist.SegmentUrls);
        Assert.False(playlist.IsComplete);
    }

    [Fact]
    public void An_EXTINF_with_no_uri_after_it_is_skipped()
    {
        // Truncated downloads happen, and a trailing #EXTINF with nothing
        // following must not become an empty URL in the segment list.
        const string truncated = """
            #EXTM3U
            #EXTINF:10.000,
            a.ts
            #EXTINF:10.000,
            """;

        var playlist = HlsManifestParser.ParseMediaPlaylist(truncated, VariantUrl);

        Assert.Single(playlist.SegmentUrls);
    }
}
