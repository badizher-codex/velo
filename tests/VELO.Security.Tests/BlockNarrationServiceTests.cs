using Microsoft.Extensions.Logging.Abstractions;
using VELO.Security.Threats;
using Xunit;

namespace VELO.Security.Tests;

public class BlockNarrationServiceTests
{
    private static (BlockNarrationService Svc, BlockExplanationService Explainer, List<BlockNarrationService.Narration> Emitted)
        Build(string explanationText = "Blocked because tracker.")
    {
        var explainer = new BlockExplanationService(NullLogger<BlockExplanationService>.Instance)
        {
            ChatDelegate = (_, _, _) => Task.FromResult(explanationText),
        };

        var svc = new BlockNarrationService(explainer);
        var emitted = new List<BlockNarrationService.Narration>();
        svc.NarrationReady += n => emitted.Add(n);
        return (svc, explainer, emitted);
    }

    private static Task<BlockNarrationService.Narration?> Consider(
        BlockNarrationService svc,
        string host = "tracker.example",
        string source = "AIEngine",
        string kind = "Tracker",
        bool isMalwaredex = false,
        int confidence = 90)
        => svc.ConsiderAsync(
            tabId: "tab-1",
            host: host,
            kind: kind,
            subKind: "cross-site",
            source: source,
            isMalwaredexHit: isMalwaredex,
            confidence: confidence,
            fullUrl: $"https://{host}/beacon");

    // ── Routine-source filter ────────────────────────────────────────────

    [Fact]
    public async Task QuietSource_Skipped_NoNarration()
    {
        var (svc, _, emitted) = Build();
        var n = await Consider(svc, source: "BLOCKLIST");
        Assert.Null(n);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task QuietSource_ButMalwaredexHit_StillNarrated()
    {
        var (svc, _, emitted) = Build();
        var n = await Consider(svc, source: "BLOCKLIST", isMalwaredex: true);
        Assert.NotNull(n);
        Assert.Single(emitted);
    }

    [Fact]
    public async Task NonQuietSource_AIEngine_IsNarrated()
    {
        var (svc, _, emitted) = Build();
        var n = await Consider(svc, source: "AIEngine");
        Assert.NotNull(n);
        Assert.Single(emitted);
        Assert.Equal("AIEngine", n!.Source);
    }

    // ── Per-host cooldown ───────────────────────────────────────────────

    [Fact]
    public async Task PerHostCooldown_BlocksSecondNarrationForSameHost()
    {
        var (svc, _, emitted) = Build();
        svc.PerHostCooldown = TimeSpan.FromHours(1);

        await Consider(svc, host: "ads.example.com");
        await Consider(svc, host: "ads.example.com");

        Assert.Single(emitted);
    }

    [Fact]
    public async Task PerHostCooldown_DoesNotBlockOtherHosts()
    {
        var (svc, _, emitted) = Build();
        svc.PerHostCooldown = TimeSpan.FromHours(1);

        await Consider(svc, host: "a.com");
        await Consider(svc, host: "b.com");
        await Consider(svc, host: "c.com");

        Assert.Equal(3, emitted.Count);
    }

    // ── Global throttle ─────────────────────────────────────────────────

    [Fact]
    public async Task GlobalThrottle_LimitsNarrationsPerMinute()
    {
        var (svc, _, emitted) = Build();
        svc.MaxNarrationsPerMinute = 3;
        svc.PerHostCooldown        = TimeSpan.Zero; // isolate global throttle

        for (int i = 0; i < 10; i++)
            await Consider(svc, host: $"host{i}.com");

        Assert.Equal(3, emitted.Count);
    }

    [Fact]
    public async Task GlobalThrottle_Disabled_AllowsAll()
    {
        var (svc, _, emitted) = Build();
        svc.MaxNarrationsPerMinute = 0;        // disabled
        svc.PerHostCooldown        = TimeSpan.Zero;

        for (int i = 0; i < 10; i++)
            await Consider(svc, host: $"host{i}.com");

        Assert.Equal(10, emitted.Count);
    }

    // ── Empty inputs ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyHost_NoNarration()
    {
        var (svc, _, emitted) = Build();
        var n = await Consider(svc, host: "");
        Assert.Null(n);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task EmptySource_NoNarration()
    {
        var (svc, _, emitted) = Build();
        var n = await Consider(svc, source: "");
        Assert.Null(n);
        Assert.Empty(emitted);
    }

    // ── Explanation routing ────────────────────────────────────────────

    [Fact]
    public async Task NarrationCarriesExplainerText()
    {
        var (svc, _, emitted) = Build(explanationText: "Custom explanation here.");
        var n = await Consider(svc);
        Assert.NotNull(n);
        Assert.Equal("Custom explanation here.", n!.Text);
        Assert.Equal("Custom explanation here.", emitted[0].Text);
    }

    [Fact]
    public async Task NarrationPreservesTabAndHost()
    {
        var (svc, _, _) = Build();
        var n = await svc.ConsiderAsync(
            tabId: "T-42",
            host: "creep.example",
            kind: "Tracker",
            subKind: "pixel",
            source: "AIEngine",
            isMalwaredexHit: false,
            confidence: 95,
            fullUrl: "https://creep.example/p");
        Assert.NotNull(n);
        Assert.Equal("T-42", n!.TabId);
        Assert.Equal("creep.example", n.Host);
    }

    // ── Reset ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_ClearsCooldownState()
    {
        var (svc, _, emitted) = Build();
        svc.PerHostCooldown = TimeSpan.FromHours(1);

        await Consider(svc, host: "x.com");
        Assert.Single(emitted);

        await Consider(svc, host: "x.com"); // blocked by cooldown
        Assert.Single(emitted);

        svc.Reset();
        await Consider(svc, host: "x.com"); // cooldown gone
        Assert.Equal(2, emitted.Count);
    }

    // ── Subscriber exception isolation ─────────────────────────────────

    [Fact]
    public async Task SubscriberException_DoesNotPropagate()
    {
        var (svc, _, _) = Build();
        svc.NarrationReady += _ => throw new InvalidOperationException("subscriber boom");

        var n = await Consider(svc);
        // Should still return the narration even though the subscriber threw.
        Assert.NotNull(n);
    }

    // ── QuietSources customisation ─────────────────────────────────────

    [Fact]
    public async Task QuietSources_ExtendableAtRuntime()
    {
        var (svc, _, emitted) = Build();
        svc.QuietSources.Add("AIEngine"); // user wants AI verdicts to be silent

        await Consider(svc, source: "AIEngine");
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task QuietSources_ClearedNarratesEverything()
    {
        var (svc, _, emitted) = Build();
        svc.QuietSources.Clear();

        await Consider(svc, source: "BLOCKLIST"); // would normally be quiet
        Assert.Single(emitted);
    }
}
