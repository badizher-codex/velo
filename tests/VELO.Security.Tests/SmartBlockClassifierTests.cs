using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

public class SmartBlockClassifierTests
{
    private static SmartBlockClassifier Build(
        Func<string, string, CancellationToken, Task<string>>? chat = null,
        double confThreshold = 0.85,
        int maxCallsPerMinute = 0,
        TimeSpan? cacheTtl = null)
    {
        return new SmartBlockClassifier
        {
            ChatDelegate              = chat,
            BlockConfidenceThreshold  = confThreshold,
            MaxCallsPerMinute         = maxCallsPerMinute,
            CacheTtl                  = cacheTtl ?? TimeSpan.FromHours(6),
        };
    }

    // ── Prompt construction ──────────────────────────────────────────────

    [Fact]
    public void BuildPrompt_IncludesAllInputs()
    {
        var (sys, user) = SmartBlockClassifier.BuildPrompt(
            "doubleclick.net", "Script", "news.example.com");
        Assert.Contains("TRACKER", sys);
        Assert.Contains("LEGITIMATE", sys);
        Assert.Contains("VERDICT|CONFIDENCE|REASON", sys);
        Assert.Contains("doubleclick.net", user);
        Assert.Contains("Script", user);
        Assert.Contains("news.example.com", user);
    }

    // ── Reply parsing ────────────────────────────────────────────────────

    [Fact]
    public void ParseReply_BlockAboveThreshold_ReturnsBlock()
    {
        var clf = Build(confThreshold: 0.85);
        var r = clf.ParseReply("BLOCK|0.95|google analytics tracking");
        Assert.Equal(SmartBlockClassifier.Verdict.Block, r.Verdict);
        Assert.Equal(0.95, r.Confidence, 2);
        Assert.Contains("analytics", r.Reason);
    }

    [Fact]
    public void ParseReply_BlockBelowThreshold_DowngradesToAllow()
    {
        var clf = Build(confThreshold: 0.85);
        var r = clf.ParseReply("BLOCK|0.60|might be tracker");
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, r.Verdict); // confidence-gated
        Assert.Equal(0.60, r.Confidence, 2);
    }

    [Fact]
    public void ParseReply_Allow_AlwaysAllows()
    {
        var clf = Build();
        var r = clf.ParseReply("ALLOW|0.99|first-party CDN");
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, r.Verdict);
    }

    [Fact]
    public void ParseReply_HandlesWhitespaceAndCase()
    {
        var clf = Build(confThreshold: 0.5);
        var r = clf.ParseReply("  block | 0.9 | trailing whitespace tracker  ");
        Assert.Equal(SmartBlockClassifier.Verdict.Block, r.Verdict);
    }

    [Fact]
    public void ParseReply_StripsMarkdownFences()
    {
        var clf = Build(confThreshold: 0.5);
        var r = clf.ParseReply("```\nBLOCK|0.9|tracker\n```");
        Assert.Equal(SmartBlockClassifier.Verdict.Block, r.Verdict);
    }

    [Fact]
    public void ParseReply_SkipsThoughtsAndPicksFirstPipeLine()
    {
        var clf = Build(confThreshold: 0.5);
        var reply = "Let me think about this.\nBLOCK|0.95|known ad network";
        var r = clf.ParseReply(reply);
        Assert.Equal(SmartBlockClassifier.Verdict.Block, r.Verdict);
    }

    [Fact]
    public void ParseReply_MalformedInput_DefaultsToAllow()
    {
        var clf = Build();
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, clf.ParseReply("").Verdict);
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, clf.ParseReply("not even close").Verdict);
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, clf.ParseReply("BLOCK").Verdict);
    }

    [Fact]
    public void ParseReply_ClampsConfidenceToZeroToOne()
    {
        var clf = Build();
        var hi = clf.ParseReply("BLOCK|7.5|huge");
        var lo = clf.ParseReply("BLOCK|-0.3|negative");
        Assert.True(hi.Confidence <= 1.0);
        Assert.True(lo.Confidence >= 0.0);
    }

    // ── No adapter ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_NoChatDelegate_ReturnsAllow_NoCacheWrite()
    {
        var clf = Build(chat: null);
        var r = await clf.ClassifyAsync("unknown.com", "Script", "site.com");
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, r.Verdict);
        Assert.False(r.FromCache);
        Assert.Equal(0, clf.CacheCount); // never cached
    }

    // ── Empty host ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_EmptyHost_ReturnsAllowImmediately()
    {
        var calls = 0;
        var clf = Build(chat: (_, _, _) => { calls++; return Task.FromResult("BLOCK|0.99|x"); });
        var r = await clf.ClassifyAsync("", "Script", "site.com");
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, r.Verdict);
        Assert.Equal(0, calls); // no model call
    }

    // ── Cache behaviour ──────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_Caches_Verdict_PerHost()
    {
        var calls = 0;
        var clf = Build(chat: (_, _, _) =>
        {
            calls++;
            return Task.FromResult("BLOCK|0.95|known tracker");
        });

        var first = await clf.ClassifyAsync("doubleclick.net", "Script", "site.com");
        Assert.False(first.FromCache);
        Assert.Equal(SmartBlockClassifier.Verdict.Block, first.Verdict);

        var second = await clf.ClassifyAsync("doubleclick.net", "Script", "site.com");
        Assert.True(second.FromCache);
        Assert.Equal(SmartBlockClassifier.Verdict.Block, second.Verdict);

        Assert.Equal(1, calls); // only one model call total
    }

    [Fact]
    public async Task ClassifyAsync_DifferentHosts_GetSeparateClassifications()
    {
        var calls = 0;
        var clf = Build(chat: (_, user, _) =>
        {
            calls++;
            var verdict = user.Contains("good.com") ? "ALLOW|0.95|first-party" : "BLOCK|0.95|tracker";
            return Task.FromResult(verdict);
        });

        var bad = await clf.ClassifyAsync("ads.tracker.io", "Script", "site.com");
        var ok  = await clf.ClassifyAsync("good.com",       "Script", "site.com");

        Assert.Equal(SmartBlockClassifier.Verdict.Block, bad.Verdict);
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, ok.Verdict);
        Assert.Equal(2, calls);
    }

    // ── Budget enforcement ───────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_RespectsCallBudget()
    {
        var calls = 0;
        var clf = Build(
            chat: (_, _, _) => { calls++; return Task.FromResult("BLOCK|0.95|x"); },
            maxCallsPerMinute: 2);

        // Three different hosts so cache doesn't short-circuit.
        await clf.ClassifyAsync("a.com", "Script", "site.com");
        await clf.ClassifyAsync("b.com", "Script", "site.com");
        var third = await clf.ClassifyAsync("c.com", "Script", "site.com");

        Assert.Equal(2, calls); // budget caps at 2
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, third.Verdict);
        Assert.Contains("budget", third.Reason);
    }

    // ── Failure handling ────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_AdapterThrows_ReturnsAllow()
    {
        var clf = Build(chat: (_, _, _) => throw new InvalidOperationException("model crashed"));
        var r = await clf.ClassifyAsync("oops.com", "Script", "site.com");
        Assert.Equal(SmartBlockClassifier.Verdict.Allow, r.Verdict);
        Assert.Contains("classifier error", r.Reason);
    }

    // ── v2.4.62 P2-B — degrading quietly when the model is down ──────────

    // Field log 2026-07-27: ~30 warnings for one host in 200 ms. Every failure was
    // re-attempted on the next request because failures were never remembered.
    [Fact]
    public async Task ClassifyAsync_RemembersFailures_InsteadOfRetryingEveryRequest()
    {
        var calls = 0;
        var clf = Build(chat: (_, _, _) => { calls++; throw new HttpRequestException("connection refused"); });
        clf.FailureThreshold = 0; // isolate the negative cache from the circuit breaker

        await clf.ClassifyAsync("cdn.example.com", "Script", "site.com");
        await clf.ClassifyAsync("cdn.example.com", "Script", "site.com");
        await clf.ClassifyAsync("cdn.example.com", "Script", "site.com");

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClassifyAsync_FailureCacheExpires_AndRetries()
    {
        var calls = 0;
        var clf = Build(chat: (_, _, _) => { calls++; throw new HttpRequestException("connection refused"); });
        clf.FailureThreshold = 0;
        clf.FailureCacheTtl  = TimeSpan.Zero;

        await clf.ClassifyAsync("cdn.example.com", "Script", "site.com");
        await clf.ClassifyAsync("cdn.example.com", "Script", "site.com");

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ClassifyAsync_OpensCircuitAfterConsecutiveFailures()
    {
        var calls = 0;
        var clf = Build(chat: (_, _, _) => { calls++; throw new HttpRequestException("connection refused"); });
        clf.FailureThreshold = 3;
        clf.FailureCacheTtl  = TimeSpan.Zero; // force a real call attempt per host

        foreach (var host in new[] { "a.com", "b.com", "c.com", "d.com", "e.com" })
            await clf.ClassifyAsync(host, "Script", "site.com");

        Assert.True(clf.IsCircuitOpen);
        Assert.Equal(3, calls); // stopped calling after the threshold
    }

    [Fact]
    public async Task ClassifyAsync_CircuitOpen_ReturnsAllowWithoutCalling()
    {
        var clf = Build(chat: (_, _, _) => throw new HttpRequestException("connection refused"));
        clf.FailureThreshold = 1;
        clf.FailureCacheTtl  = TimeSpan.Zero;

        await clf.ClassifyAsync("a.com", "Script", "site.com");
        var r = await clf.ClassifyAsync("b.com", "Script", "site.com");

        Assert.Equal(SmartBlockClassifier.Verdict.Allow, r.Verdict);
        Assert.Contains("circuit open", r.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_SuccessResetsTheFailureStreak()
    {
        var fail = true;
        var clf = Build(chat: (_, _, _) => fail
            ? throw new HttpRequestException("connection refused")
            : Task.FromResult("ALLOW|0.9|fine"));
        clf.FailureThreshold = 2;
        clf.FailureCacheTtl  = TimeSpan.Zero;

        await clf.ClassifyAsync("a.com", "Script", "site.com");
        fail = false;
        await clf.ClassifyAsync("b.com", "Script", "site.com");
        fail = true;
        await clf.ClassifyAsync("c.com", "Script", "site.com");

        Assert.False(clf.IsCircuitOpen);
    }

    // One page loading 30 assets from one CDN used to fire 30 identical calls.
    [Fact]
    public async Task ClassifyAsync_DeduplicatesConcurrentCallsForTheSameHost()
    {
        var calls = 0;
        var gate  = new TaskCompletionSource<string>();
        var clf   = Build(chat: (_, _, _) => { Interlocked.Increment(ref calls); return gate.Task; });

        var pending = Enumerable.Range(0, 20)
            .Select(_ => clf.ClassifyAsync("cdn.example.com", "Script", "site.com"))
            .ToArray();

        gate.SetResult("BLOCK|0.95|tracker");
        var results = await Task.WhenAll(pending);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal(SmartBlockClassifier.Verdict.Block, r.Verdict));
    }

    [Fact]
    public async Task ClassifyAsync_CapsConcurrentCallsAcrossHosts()
    {
        var calls = 0;
        var gate  = new TaskCompletionSource<string>();
        var clf   = Build(chat: (_, _, _) => { Interlocked.Increment(ref calls); return gate.Task; });
        clf.MaxConcurrentCalls = 2;

        var pending = new[] { "a.com", "b.com", "c.com", "d.com" }
            .Select(h => clf.ClassifyAsync(h, "Script", "site.com"))
            .ToArray();

        gate.SetResult("ALLOW|0.9|fine");
        var results = await Task.WhenAll(pending);

        Assert.Equal(2, calls);
        Assert.Equal(2, results.Count(r => r.Reason == "classifier busy"));
    }

    // ── ClearCache ──────────────────────────────────────────────────────

    [Fact]
    public async Task ClearCache_ForcesRetClassification()
    {
        var calls = 0;
        var clf = Build(chat: (_, _, _) =>
        {
            calls++;
            return Task.FromResult("BLOCK|0.95|x");
        });

        await clf.ClassifyAsync("foo.com", "Script", "site.com");
        clf.ClearCache();
        await clf.ClassifyAsync("foo.com", "Script", "site.com");

        Assert.Equal(2, calls); // cache cleared between calls
    }
}
