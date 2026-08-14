using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / P4 — what the media panel offers, and what it refuses.
///
/// These live in Core rather than in the panel's code-behind on purpose: the
/// interesting part of P4 is a set of decisions (offer this, refuse that, say
/// why), and decisions stuck to a WPF control cannot be tested. Lesson #55.
/// </summary>
public class MediaInventoryOfferTests
{
    private static MediaPageReport PageWith(params MediaTrack[] tracks) =>
        new("https://site.example/watch", tracks, new DrmSignals(), []);

    private static MediaTrack Track(TrackKind kind, string codecs, long bytes = 1_048_576) =>
        new(0, kind == TrackKind.Audio ? "audio/webm" : "video/mp4", kind, codecs, "isobmff", 10, bytes, false);

    // ── The refusal ──────────────────────────────────────────────────────

    [Fact]
    public void Protected_content_offers_nothing_and_says_so()
    {
        // §5: protected media is declined, not attempted. Downloading it would
        // produce encrypted, unplayable bytes — a bug report waiting to happen.
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals(
            "https://cdn.example/movie.mp4", "video/mp4", ContentLength: "900000"));
        inventory.ApplyPageReport(new MediaPageReport(
            "https://site.example/watch",
            [Track(TrackKind.Video, "avc1")],
            new DrmSignals(KeySystemsProbed: 1, KeySystemsResolved: 1, MediaKeysAttached: 1),
            []));

        var offers = inventory.BuildOffers();

        var only = Assert.Single(offers);
        Assert.Equal(MediaOfferKind.Protected, only.Kind);
        Assert.False(only.CanDownload);
        Assert.NotNull(only.BlockedReason);
        // Even the progressive file that WAS downloadable is withheld — the
        // page is playing protected content and we decline wholesale.
        Assert.DoesNotContain(offers, o => o.Kind == MediaOfferKind.ProgressiveFile);
    }

    [Fact]
    public void Merely_probing_key_systems_does_not_suppress_the_offers()
    {
        // bitmovin probes 13 key systems on load with nothing protected
        // playing. Treating that as protection would empty the panel on any
        // site that feature-detects.
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals(
            "https://cdn.example/movie.mp4", "video/mp4", ContentLength: "900000"));
        inventory.ApplyPageReport(new MediaPageReport(
            "https://site.example/watch", [],
            new DrmSignals(KeySystemsProbed: 13, KeySystemsResolved: 13), []));

        var offers = inventory.BuildOffers();

        Assert.Single(offers);
        Assert.Equal(MediaOfferKind.ProgressiveFile, offers[0].Kind);
        Assert.True(offers[0].CanDownload);
    }

    [Fact]
    public void Leaving_protected_content_brings_the_offers_back()
    {
        // The second direction of the refusal, and the one that was broken.
        // §21, measured on Prime Video: /storefront and /movie — catalogue
        // pages playing nothing — kept reporting protected after a protected
        // title had played, because the page-side EME counters were cumulative
        // per document and an SPA route change raises no navigation, so
        // Media.Reset() never ran. Since protected content short-circuits
        // BuildOffers entirely, the user lost every offer for the rest of the
        // session on that site.
        //
        // Guards get run both ways (#44): the case above proves it refuses,
        // this one proves it stops refusing.
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals(
            "https://cdn.example/movie.mp4", "video/mp4", ContentLength: "900000"));

        inventory.ApplyPageReport(new MediaPageReport(
            "https://site.example/watch",
            [Track(TrackKind.Video, "avc1")],
            new DrmSignals(KeySystemsProbed: 5, KeySystemsResolved: 3, MediaKeysAttached: 1),
            []));
        Assert.True(inventory.IsProtected);
        Assert.Equal(MediaOfferKind.Protected, Assert.Single(inventory.BuildOffers()).Kind);

        // Same document, next thing on screen: a clean trailer. Capability
        // stays high — those five key systems really were probed — but nothing
        // protected is attached to the live element any more.
        inventory.ApplyPageReport(new MediaPageReport(
            "https://site.example/storefront",
            [Track(TrackKind.Video, "avc1")],
            new DrmSignals(KeySystemsProbed: 5, KeySystemsResolved: 3),
            []));

        Assert.False(inventory.IsProtected);
        var offers = inventory.BuildOffers();
        Assert.DoesNotContain(offers, o => o.Kind == MediaOfferKind.Protected);
        Assert.Contains(offers, o => o.Kind == MediaOfferKind.ProgressiveFile && o.CanDownload);
        Assert.Contains(offers, o => o.Kind == MediaOfferKind.VideoTrack);
    }

    // ── Every unactionable row explains itself ───────────────────────────

    [Fact]
    public void No_row_is_ever_unactionable_without_a_reason()
    {
        // The rule P4 exists to enforce: never a disabled control with no
        // explanation. Asserted across every kind the panel can produce.
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals(
            "https://cdn.example/movie.mp4", "video/mp4", ContentLength: "900000"));
        inventory.RecordResponse(new ResponseSignals(
            "https://cdn.example/master.m3u8", "audio/mpegurl", ContentLength: "752"));
        inventory.ApplyPageReport(PageWith(
            Track(TrackKind.Audio, "opus"), Track(TrackKind.Video, "av01.0.08M.08")));

        var offers = inventory.BuildOffers();

        Assert.NotEmpty(offers);
        foreach (var offer in offers)
        {
            if (offer.CanDownload) Assert.Null(offer.BlockedReason);
            else Assert.False(string.IsNullOrWhiteSpace(offer.BlockedReason),
                              $"{offer.Kind} '{offer.Title}' is not actionable and gives no reason");
        }
    }

    // ── The audio / video choice, which is the point of the feature ──────

    [Fact]
    public void Audio_and_video_appear_as_separate_rows_with_their_codecs()
    {
        // Measured live on YouTube: two SourceBuffers, Opus and AV1. This is
        // the "choose audio or video" the request asked for, surfaced.
        var inventory = new MediaInventory();
        inventory.ApplyPageReport(PageWith(
            Track(TrackKind.Audio, "opus"),
            Track(TrackKind.Video, "av01.0.08M.08")));

        var offers = inventory.BuildOffers();

        Assert.Equal(2, offers.Count);
        Assert.Contains(offers, o => o.Kind == MediaOfferKind.AudioTrack && o.Detail.Contains("opus"));
        Assert.Contains(offers, o => o.Kind == MediaOfferKind.VideoTrack && o.Detail.Contains("av01"));
    }

    [Fact]
    public void Adaptive_tracks_are_capturable_and_carry_the_mime_to_save_as()
    {
        // Capturable since P2b. The MIME rides along because it is the only
        // thing that says which container the captured bytes are — audio/webm
        // and video/mp4 were both measured on the same YouTube page.
        var inventory = new MediaInventory();
        inventory.ApplyPageReport(PageWith(Track(TrackKind.Audio, "opus")));

        var offer = Assert.Single(inventory.BuildOffers());

        Assert.True(offer.CanDownload);
        Assert.Null(offer.BlockedReason);
        Assert.Equal("audio/webm", offer.TrackMime);
    }

    // ── The path that works today ────────────────────────────────────────

    [Fact]
    public void A_progressive_file_is_offered_for_real()
    {
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals(
            "https://www.w3schools.com/html/mov_bbb.mp4", "video/mp4",
            ContentLength: "788493", ContentRange: "bytes 0-788492/788493"));

        var offer = Assert.Single(inventory.BuildOffers());

        Assert.Equal(MediaOfferKind.ProgressiveFile, offer.Kind);
        Assert.True(offer.CanDownload);
        Assert.Null(offer.BlockedReason);
        Assert.Equal("mov_bbb.mp4", offer.Title);
        Assert.Contains("video/mp4", offer.Detail);
        Assert.Contains("770 KB", offer.Detail);
    }

    [Fact]
    public void Larger_files_are_offered_first()
    {
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals("https://cdn.example/small.mp4", "video/mp4", "200000"));
        inventory.RecordResponse(new ResponseSignals("https://cdn.example/big.mp4",   "video/mp4", "9000000"));

        var offers = inventory.BuildOffers();

        Assert.Equal("big.mp4", offers[0].Title);
    }

    [Fact]
    public void An_empty_inventory_offers_nothing()
    {
        Assert.Empty(new MediaInventory().BuildOffers());
        Assert.True(new MediaInventory().IsEmpty);
    }

    [Fact]
    public void Navigating_away_clears_what_the_panel_would_show()
    {
        var inventory = new MediaInventory();
        inventory.RecordResponse(new ResponseSignals("https://cdn.example/a.mp4", "video/mp4", "900000"));
        inventory.ApplyPageReport(PageWith(Track(TrackKind.Audio, "opus")));

        inventory.Reset();

        Assert.Empty(inventory.BuildOffers());
    }

    // ── Titles ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://cdn.example/path/movie.mp4",     "movie.mp4")]
    [InlineData("https://cdn.example/movie%20two.mp4",    "movie two.mp4")]
    [InlineData("https://cdn.example/",                   "cdn.example")]
    [InlineData("not a url",                              "not a url")]
    public void File_names_come_from_the_url_or_fall_back_to_the_host(string url, string expected)
    {
        Assert.Equal(expected, MediaInventory.FileNameFor(url));
    }
}
