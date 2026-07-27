using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.Events;
using VELO.Security.AI.Models;
using VELO.Security.Guards;
using VELO.Security.Rules;
using Xunit;

namespace VELO.Security.Tests;

// v2.4.62 P2-A — Regression cover for the first-party false positive: VELO was
// blocking primevideo.com's own /detail/, /movie and /collection routes as
// "trackers" while the user browsed primevideo.com.
public class RequestGuardTests
{
    private static RequestGuard Build(SmartBlockClassifier? smartBlock = null)
    {
        var blocklist = new BlocklistManager(new EventBus(), NullLogger<BlocklistManager>.Instance);
        return new RequestGuard(blocklist, NullLogger<RequestGuard>.Instance, smartBlock);
    }

    // ── IsFirstParty ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("www.primevideo.com", "https://www.primevideo.com/storefront")]
    [InlineData("m.media-amazon.com", "https://media-amazon.com/")]
    [InlineData("api.example.com",    "https://www.example.com/page?q=1")]
    public void IsFirstParty_TrueForSameRegistrableRoot(string host, string referrer)
        => Assert.True(RequestGuard.IsFirstParty(host, referrer));

    [Theory]
    [InlineData("doubleclick.net",  "https://www.primevideo.com/storefront")]
    [InlineData("evil.example.org", "https://www.example.com/")]
    public void IsFirstParty_FalseForDifferentRoot(string host, string referrer)
        => Assert.False(RequestGuard.IsFirstParty(host, referrer));

    [Theory]
    [InlineData("www.example.com", "")]
    [InlineData("www.example.com", null)]
    [InlineData("www.example.com", "not a url")]
    [InlineData("", "https://www.example.com/")]
    public void IsFirstParty_FalseWithoutUsableReferrer(string host, string? referrer)
        => Assert.False(RequestGuard.IsFirstParty(host, referrer));

    // Naive last-two-labels would make every *.co.uk site first-party to every
    // other one — and first-party grants a heuristics bypass.
    [Fact]
    public void IsFirstParty_HonoursSecondLevelPublicSuffixes()
    {
        Assert.False(RequestGuard.IsFirstParty("tracker.co.uk", "https://www.bbc.co.uk/news"));
        Assert.True(RequestGuard.IsFirstParty("news.bbc.co.uk", "https://www.bbc.co.uk/news"));
    }

    // ── First-party bypasses the heuristic tracker rules ─────────────────

    // The shape that regressed: a catalogue URL whose query carries a long opaque
    // blob, requested while browsing the same site. Sending data to yourself is not
    // exfiltration, so the length heuristic must not fire on it.
    [Fact]
    public void Evaluate_AllowsFirstPartyUrlWithLongOpaqueParams()
    {
        var guard = Build();
        var uri = "https://www.primevideo.com/detail/0RGRY4MBDL1IT45CW7N0QMIIYB" +
                  "?jic=" + new string('A', 240) + "&ref_=atv_hm_hom_c_ywRzEs_awns_5_2";

        var verdict = guard.Evaluate(uri, "https://www.primevideo.com/storefront", "Document");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    [Fact]
    public void Evaluate_StillWarnsOnThirdPartyExfiltrationParams()
    {
        var guard = Build();
        var uri = "https://collector.example.net/c?payload=" + new string('a', 260);

        var verdict = guard.Evaluate(uri, "https://www.primevideo.com/storefront", "XmlHttpRequest");

        Assert.Equal(VerdictType.Warn, verdict.Verdict);
        Assert.Equal(ThreatType.DataExfiltration, verdict.ThreatType);
    }

    [Fact]
    public void Evaluate_AllowsFirstPartyBeaconPath_ButBlocksThirdParty()
    {
        var guard = Build();

        var first = guard.Evaluate(
            "https://www.example.com/log?event=play", "https://www.example.com/watch", "Fetch");
        var third = guard.Evaluate(
            "https://ads.tracker.net/log?event=play", "https://www.example.com/watch", "Fetch");

        Assert.Equal(VerdictType.Safe,  first.Verdict);
        Assert.Equal(VerdictType.Block, third.Verdict);
    }

    // ── Local / private targets (v2.4.64) ────────────────────────────────
    // The old rule 3 blocked localhost, 0.0.0.0 and *.local unconditionally as
    // "DNS rebinding", so VELO could not open a dev server or a NAS at all.

    [Theory]
    [InlineData("http://localhost:8765/page.html")]
    [InlineData("http://127.0.0.1:8765/page.html")]
    [InlineData("http://nas.local/admin")]
    [InlineData("http://192.168.1.10/")]
    public void Evaluate_AllowsUserNavigationToLocalTargets(string uri)
    {
        // A typed navigation has no referrer.
        var verdict = Build().Evaluate(uri, "", "Document");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    [Fact]
    public void Evaluate_AllowsLocalPageLoadingLocalResources()
    {
        var verdict = Build().Evaluate(
            "http://localhost:8765/app.js", "http://localhost:8765/index.html", "Script");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    [Theory]
    [InlineData("http://localhost:8765/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://printer.local/status")]
    public void Evaluate_StillBlocksPublicPagesReachingIntoTheLocalNetwork(string uri)
    {
        var verdict = Build().Evaluate(uri, "https://evil.example.com/page", "XmlHttpRequest");

        Assert.Equal(VerdictType.Block, verdict.Verdict);
        Assert.Equal(ThreatType.SSRF, verdict.ThreatType);
    }

    // ── SmartBlock verdicts are scoped to third-party sub-resources ───────

    private static SmartBlockClassifier ClassifierThatBlocks()
    {
        var classifier = new SmartBlockClassifier
        {
            MaxCallsPerMinute        = 0,
            BlockConfidenceThreshold = 0.5,
            ChatDelegate             = (_, _, _) => Task.FromResult("BLOCK|0.99|looks like analytics"),
        };
        return classifier;
    }

    [Fact]
    public async Task Evaluate_NeverBlocksMainFrameNavigationOnASmartBlockVerdict()
    {
        var classifier = ClassifierThatBlocks();
        await classifier.ClassifyAsync("www.primevideo.com", "XmlHttpRequest", "other.example");
        var guard = Build(classifier);

        var nav = guard.Evaluate("https://www.primevideo.com/movie", "https://www.google.com/", "Document");

        Assert.Equal(VerdictType.Safe, nav.Verdict);
    }

    [Fact]
    public async Task Evaluate_IgnoresSmartBlockVerdictForFirstPartySubresources()
    {
        var classifier = ClassifierThatBlocks();
        await classifier.ClassifyAsync("www.example.com", "Script", "other.example");
        var guard = Build(classifier);

        var sub = guard.Evaluate("https://www.example.com/app.js", "https://www.example.com/", "Script");

        Assert.Equal(VerdictType.Safe, sub.Verdict);
    }

    [Fact]
    public async Task Evaluate_AppliesSmartBlockVerdictToThirdPartySubresources()
    {
        var classifier = ClassifierThatBlocks();
        await classifier.ClassifyAsync("metrics.tracker.net", "Script", "www.example.com");
        var guard = Build(classifier);

        var sub = guard.Evaluate("https://metrics.tracker.net/t.js", "https://www.example.com/", "Script");

        Assert.Equal(VerdictType.Block, sub.Verdict);
        Assert.Equal("SmartBlock", sub.Source);
    }
}
