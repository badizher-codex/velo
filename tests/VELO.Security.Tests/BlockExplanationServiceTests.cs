using Microsoft.Extensions.Logging.Abstractions;
using VELO.Security.Threats;
using Xunit;

namespace VELO.Security.Tests;

public class BlockExplanationServiceTests
{
    private static BlockExplanationService NewService() =>
        new(NullLogger<BlockExplanationService>.Instance);

    private static BlockEntry NewEntry(BlockKind kind = BlockKind.Tracker, string host = "doubleclick.net") => new()
    {
        Host       = host,
        FullUrl    = $"https://{host}/gampad/ads?iu=demo",
        Kind       = kind,
        SubKind    = "cross-site",
        Source     = BlockSource.GoldenList,
        Confidence = 95,
    };

    [Fact]
    public async Task Explain_ReturnsStaticTemplate_WhenAIAdapterIsOffline()
    {
        // ChatDelegate left null → service short-circuits to static template.
        var svc    = NewService();
        var result = await svc.ExplainAsync(NewEntry());

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("rastreador", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doubleclick.net", result);
    }

    [Fact]
    public async Task Explain_CachesResult_OnSecondCall()
    {
        var svc       = NewService();
        var callCount = 0;
        svc.ChatDelegate = (_, _, _) =>
        {
            callCount++;
            return Task.FromResult($"AI says hi #{callCount}");
        };

        var entry = NewEntry();
        var first  = await svc.ExplainAsync(entry);
        var second = await svc.ExplainAsync(entry);

        // Second call MUST hit the cache: same text, no extra delegate invocation.
        Assert.Equal(first, second);
        Assert.Equal(1, callCount);
        Assert.Equal(1, svc.CacheCount);
    }

    [Fact]
    public async Task Explain_TimesOutAt3Seconds_AndFallsBackToStatic()
    {
        var svc = NewService();
        // Delegate that simply waits past the 3-second budget.
        svc.ChatDelegate = async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return "should never see this";
        };

        var entry = NewEntry(BlockKind.Malware);
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        var text  = await svc.ExplainAsync(entry);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Should bail out near 3s, but took {sw.Elapsed.TotalSeconds:F1}s");
        Assert.DoesNotContain("should never see this", text);
        Assert.Contains("malware", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_PromptIncludesAllEntryFields()
    {
        var entry = new BlockEntry
        {
            Host             = "evil.example",
            FullUrl          = "https://evil.example/payload.js",
            Kind             = BlockKind.Malware,
            SubKind          = "drive-by",
            Source           = BlockSource.AIEngine,
            IsMalwaredexHit  = true,
            Confidence       = 88,
        };

        var prompt = BlockExplanationService.BuildPrompt(entry);

        Assert.Contains("evil.example", prompt);
        Assert.Contains("payload.js",   prompt);
        Assert.Contains("Malware",      prompt);
        Assert.Contains("drive-by",     prompt);
        Assert.Contains("AIEngine",     prompt);
        Assert.Contains("88",           prompt);
        Assert.Contains("yes",          prompt); // Malwaredex hit indicator
    }
}
