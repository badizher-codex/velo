using VELO.Agent;
using Xunit;

namespace VELO.Agent.Tests;

public class TldrServiceTests
{
    private static (TldrService Svc, AIContextActions Actions, List<(string Sys, string User)> Calls)
        Build(string summary = "Three-line summary here.")
    {
        var calls = new List<(string Sys, string User)>();
        var actions = new AIContextActions
        {
            ChatDelegate = (sys, user, _) =>
            {
                calls.Add((sys, user));
                return Task.FromResult(summary);
            }
        };
        return (new TldrService(actions), actions, calls);
    }

    private static string MakeContent(int wordCount)
        => string.Join(' ', Enumerable.Repeat("word", wordCount));

    /// <summary>
    /// Short-but-many-words content for cache tests. The summarizer triggers
    /// map-reduce above 4000 chars; we want pure single-call paths here.
    /// 700 single-letter "words" = ~700 words at ~1400 chars.
    /// </summary>
    private static string MakeShortContent(int wordCount)
        => string.Join(' ', Enumerable.Repeat("a", wordCount));

    // ── Pure helpers ─────────────────────────────────────────────────────

    [Fact]
    public void CountWords_HandlesMultipleSpacesAndNewlines()
    {
        Assert.Equal(0, TldrService.CountWords(""));
        Assert.Equal(0, TldrService.CountWords("   "));
        Assert.Equal(1, TldrService.CountWords("hello"));
        Assert.Equal(3, TldrService.CountWords("  hello   world\nhi  "));
        Assert.Equal(5, TldrService.CountWords("a b c d e"));
    }

    [Fact]
    public void EstimateReadingMinutes_DividesBy225WPM()
    {
        // 450 words = 2 minutes
        Assert.Equal(2, TldrService.EstimateReadingMinutes(MakeContent(450)));
        // 100 words = 0 minutes
        Assert.Equal(0, TldrService.EstimateReadingMinutes(MakeContent(100)));
    }

    [Theory]
    [InlineData("velo://newtab")]
    [InlineData("about:blank")]
    [InlineData("file:///c:/x.html")]
    [InlineData("https://example.com/login")]
    [InlineData("https://x.com/signup")]
    [InlineData("https://x.com/checkout/order")]
    [InlineData("https://x.com/settings")]
    [InlineData("https://x.com/search?q=react")]
    [InlineData("https://x.com/dashboard/home")]
    public void IsBlacklistedUrl_RejectsNonArticlePaths(string url)
    {
        Assert.True(TldrService.IsBlacklistedUrl(url));
    }

    [Theory]
    [InlineData("https://news.example.com/article/123")]
    [InlineData("https://blog.io/2024/05/post-title")]
    [InlineData("https://docs.foo.dev/guide")]
    public void IsBlacklistedUrl_AllowsArticlePaths(string url)
    {
        Assert.False(TldrService.IsBlacklistedUrl(url));
    }

    [Fact]
    public void IsBlacklistedUrl_EmptyIsBlacklisted()
    {
        Assert.True(TldrService.IsBlacklistedUrl(""));
    }

    // ── IsEligible ────────────────────────────────────────────────────────

    [Fact]
    public void IsEligible_AboveThresholds_ReturnsTrue()
    {
        var (svc, _, _) = Build();
        // 900 words ≈ 4 minutes at 225 wpm.
        Assert.True(svc.IsEligible("https://blog.io/post", MakeContent(900)));
    }

    [Fact]
    public void IsEligible_BelowWordCount_ReturnsFalse()
    {
        var (svc, _, _) = Build();
        Assert.False(svc.IsEligible("https://blog.io/post", MakeContent(300)));
    }

    [Fact]
    public void IsEligible_BlacklistedUrl_ReturnsFalse()
    {
        var (svc, _, _) = Build();
        Assert.False(svc.IsEligible("velo://newtab", MakeContent(2000)));
        Assert.False(svc.IsEligible("https://x.com/login", MakeContent(2000)));
    }

    [Fact]
    public void IsEligible_EmptyContent_ReturnsFalse()
    {
        var (svc, _, _) = Build();
        Assert.False(svc.IsEligible("https://blog.io/post", ""));
    }

    [Fact]
    public void IsEligible_HonoursCustomThresholds()
    {
        var (svc, _, _) = Build();
        svc.MinWords = 100;
        svc.MinReadingMinutes = 0;
        Assert.True(svc.IsEligible("https://blog.io/post", MakeContent(150)));
    }

    // ── Cache & GenerateSummaryAsync ─────────────────────────────────────

    [Fact]
    public async Task GenerateSummary_RoundTrips_AndCaches()
    {
        var (svc, _, calls) = Build("This is the summary.");
        // Use short content so SummarizeAsync stays on the single-call path
        // (map-reduce kicks in above 4000 chars and counts each chunk call).
        var content = MakeShortContent(700);

        var first = await svc.GenerateSummaryAsync("https://blog.io/post", content);
        var second = await svc.GenerateSummaryAsync("https://blog.io/post", content);

        Assert.Equal("This is the summary.", first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.Single(calls); // second call hit cache
    }

    [Fact]
    public async Task GenerateSummary_DifferentUrls_GenerateSeparateSummaries()
    {
        var (svc, _, calls) = Build("S");
        var content = MakeShortContent(700);
        await svc.GenerateSummaryAsync("https://a.com/1", content);
        await svc.GenerateSummaryAsync("https://b.com/2", content);
        Assert.Equal(2, calls.Count);
        Assert.Equal(2, svc.CacheCount);
    }

    [Fact]
    public async Task GenerateSummary_EmptyContent_ReturnsEmpty_NoCache()
    {
        var (svc, _, calls) = Build();
        var result = await svc.GenerateSummaryAsync("https://blog.io/p", "");
        Assert.Equal("", result.Text);
        Assert.Empty(calls);
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public async Task GenerateSummary_DoesNotCacheEmptyReply()
    {
        var (svc, _, _) = Build(summary: "");
        await svc.GenerateSummaryAsync("https://blog.io/p", MakeShortContent(700));
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public void TryGetCached_ReturnsNullForUnknownUrl()
    {
        var (svc, _, _) = Build();
        Assert.Null(svc.TryGetCached("https://nope.example"));
    }

    [Fact]
    public async Task ClearCache_ForcesRegeneration()
    {
        var (svc, _, calls) = Build("S");
        var content = MakeShortContent(700);
        await svc.GenerateSummaryAsync("https://blog.io/p", content);
        svc.ClearCache();
        await svc.GenerateSummaryAsync("https://blog.io/p", content);
        Assert.Equal(2, calls.Count);
    }
}
