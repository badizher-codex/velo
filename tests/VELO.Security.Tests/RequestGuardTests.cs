using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.Events;
using VELO.Security.AI.Models;
using VELO.Security.Guards;
using VELO.Security.Rules;
using VELO.Security.Sentinel;
using Xunit;

namespace VELO.Security.Tests;

// v2.4.62 P2-A — Regression cover for the first-party false positive: VELO was
// blocking primevideo.com's own /detail/, /movie and /collection routes as
// "trackers" while the user browsed primevideo.com.
public class RequestGuardTests
{
    private static RequestGuard Build(
        SmartBlockClassifier? smartBlock = null,
        SentinelClassifier? sentinel = null)
    {
        var blocklist = new BlocklistManager(new EventBus(), NullLogger<BlocklistManager>.Instance);
        return new RequestGuard(blocklist, NullLogger<RequestGuard>.Instance, smartBlock, sentinel);
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

    // ── S-C — VELO Sentinel inside the verdict pipeline ───────────────────

    /// <summary>A classifier with a seeded verdict and no model on disk — the
    /// guard's handling of each action is what's under test, not inference.</summary>
    private static SentinelClassifier SentinelWith(
        SentinelResult verdict, string host, SentinelMode mode)
    {
        var sentinel = new SentinelClassifier(
            modelRoot: Path.Combine(Path.GetTempPath(), "velo-sentinel-tests", Guid.NewGuid().ToString("N")))
        {
            Mode = mode,
        };
        sentinel.SeedVerdict(host, verdict);
        return sentinel;
    }

    private static SentinelResult Blocked(SentinelLabel label = SentinelLabel.Phishing) =>
        new(label, 0.97, SentinelAction.Block, $"sentinel classified host as {label}");

    [Fact]
    public void Evaluate_AppliesSentinelBlockInEnforceMode()
    {
        using var sentinel = SentinelWith(Blocked(), "fresh-lookalike.top", SentinelMode.Enforce);
        var guard = Build(sentinel: sentinel);

        var verdict = guard.Evaluate("https://fresh-lookalike.top/login", "https://mail.google.com/", "Document");

        Assert.Equal(VerdictType.Block, verdict.Verdict);
        Assert.Equal("SENTINEL",        verdict.Source);
        Assert.Equal(ThreatType.Phishing, verdict.ThreatType);
        Assert.Contains("sentinel", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DoesNotApplySentinelBlockInShadowMode()
    {
        // The S-E contract: one release where the classifier only records. If
        // this ever passes a block through, shadow mode isn't shadow.
        using var sentinel = SentinelWith(Blocked(), "fresh-lookalike.top", SentinelMode.Shadow);
        var guard = Build(sentinel: sentinel);

        var verdict = guard.Evaluate("https://fresh-lookalike.top/login", "https://mail.google.com/", "Document");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    [Fact]
    public void Evaluate_DoesNotBlockOnASentinelFlag()
    {
        // FLAG feeds PhishingShield and nothing else — it must never reach a
        // request verdict, in either mode.
        var flag = new SentinelResult(SentinelLabel.Phishing, 0.62, SentinelAction.Flag, "below threshold");
        using var sentinel = SentinelWith(flag, "maybe-phish.example", SentinelMode.Enforce);
        var guard = Build(sentinel: sentinel);

        var verdict = guard.Evaluate("https://maybe-phish.example/", "https://www.google.com/", "Document");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    [Fact]
    public async Task Evaluate_LetsTheBlocklistWinOverSentinel()
    {
        // Position in the pipeline: the exact list is checked first, so its
        // attribution is what the user sees even when both would block.
        var listDir = Path.Combine(Path.GetTempPath(), "velo-blocklist-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(listDir, "blocklists"));
        await File.WriteAllTextAsync(
            Path.Combine(listDir, "blocklists", "test.hosts"), "0.0.0.0 doubleclick.net\n");

        try
        {
            var blocklist = new BlocklistManager(new EventBus(), NullLogger<BlocklistManager>.Instance);
            await blocklist.LoadBundledAsync(listDir);
            Assert.True(blocklist.IsBlocked("doubleclick.net"), "fixture blocklist failed to load");

            using var sentinel = SentinelWith(Blocked(SentinelLabel.Tracker), "doubleclick.net", SentinelMode.Enforce);
            var guard = new RequestGuard(blocklist, NullLogger<RequestGuard>.Instance, null, sentinel);

            var verdict = guard.Evaluate("https://doubleclick.net/pixel", "https://news.example/", "Script");

            Assert.Equal(VerdictType.Block, verdict.Verdict);
            Assert.Equal("BLOCKLIST", verdict.Source);
        }
        finally
        {
            try { Directory.Delete(listDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Evaluate_TrustedHostsAreNeverReachedBySentinel()
    {
        // Rule 1b returns before the classifier. This is what keeps model-v1's
        // cdn.jsdelivr.net → "ad" p=0.92 from breaking half the web's scripts.
        using var sentinel = SentinelWith(Blocked(SentinelLabel.Ad), "cdn.jsdelivr.net", SentinelMode.Enforce);
        var guard = Build(sentinel: sentinel);

        var verdict = guard.Evaluate("https://cdn.jsdelivr.net/npm/x.js", "https://news.example/", "Script");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    // ── v2.4.69 — Sentinel is scoped like SmartBlock (first shadow session) ──

    [Fact]
    public void Evaluate_NeverAppliesSentinelToFirstPartyRequests()
    {
        // The failure this exists for: 75 hosts from one real browsing session
        // and the model wanted to block cart-mf.cinepolis.com, myaccount.ea.com,
        // stories.duolingo.com — a site's own app subdomains while the user was
        // on that site. A site is never its own tracker.
        using var sentinel = SentinelWith(Blocked(SentinelLabel.Tracker), "cart-mf.cinepolis.com", SentinelMode.Enforce);
        var guard = Build(sentinel: sentinel);

        var verdict = guard.Evaluate(
            "https://cart-mf.cinepolis.com/api/cart",
            "https://www.cinepolis.com/compra",
            "XmlHttpRequest");

        Assert.Equal(VerdictType.Safe, verdict.Verdict);
    }

    [Fact]
    public void Evaluate_AppliesSentinelToThirdPartySubresources()
    {
        using var sentinel = SentinelWith(Blocked(SentinelLabel.Tracker), "metrics.tracker.net", SentinelMode.Enforce);
        var guard = Build(sentinel: sentinel);

        var verdict = guard.Evaluate("https://metrics.tracker.net/t.js", "https://news.example/", "Script");

        Assert.Equal(VerdictType.Block, verdict.Verdict);
        Assert.Equal("SENTINEL", verdict.Source);
    }

    [Fact]
    public void Evaluate_MainFrameNavigationOnlyBlocksOnPhishing()
    {
        // Navigating TO a tracker or ad domain is the user deciding to go
        // there. Phishing is the one label that justifies cancelling a
        // top-level navigation — and it is the reason Sentinel exists.
        using var tracker = SentinelWith(Blocked(SentinelLabel.Tracker), "analytics.example", SentinelMode.Enforce);
        Assert.Equal(VerdictType.Safe,
            Build(sentinel: tracker).Evaluate("https://analytics.example/", "https://www.google.com/", "Document").Verdict);

        using var phishing = SentinelWith(Blocked(SentinelLabel.Phishing), "paypa1-verify.top", SentinelMode.Enforce);
        Assert.Equal(VerdictType.Block,
            Build(sentinel: phishing).Evaluate("https://paypa1-verify.top/", "https://mail.google.com/", "Document").Verdict);
    }

    [Fact]
    public void Evaluate_WithoutASentinelBehavesExactlyAsBefore()
    {
        // The dependency is optional everywhere; a build with no classifier
        // wired must produce the pre-S-C verdicts unchanged.
        var guard = Build();

        Assert.Equal(VerdictType.Safe,
            guard.Evaluate("https://example.com/app.js", "https://example.com/", "Script").Verdict);
    }
}
