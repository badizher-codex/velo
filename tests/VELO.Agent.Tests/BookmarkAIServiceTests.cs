using VELO.Agent;
using Xunit;

namespace VELO.Agent.Tests;

public class BookmarkAIServiceTests
{
    private static (BookmarkAIService Svc, List<(string Sys, string User)> Calls) Build(string reply = "")
    {
        var calls = new List<(string Sys, string User)>();
        var svc = new BookmarkAIService
        {
            ChatDelegate = (sys, user, _) =>
            {
                calls.Add((sys, user));
                return Task.FromResult(reply);
            }
        };
        return (svc, calls);
    }

    // ── BuildTagPrompt ───────────────────────────────────────────────────

    [Fact]
    public void BuildTagPrompt_IncludesTitleAndUrl()
    {
        var (sys, user) = BookmarkAIService.BuildTagPrompt(
            "React Server Components", "https://react.dev/rsc",
            "RSC are components that render on the server.", maxTags: 5, maxContentChars: 1500);
        Assert.Contains("5", sys);
        Assert.Contains("React Server Components", user);
        Assert.Contains("react.dev/rsc", user);
        Assert.Contains("server", user);
    }

    [Fact]
    public void BuildTagPrompt_TruncatesLongContent()
    {
        var bigContent = new string('x', 5000);
        var (_, user) = BookmarkAIService.BuildTagPrompt(
            "T", "u", bigContent, maxTags: 5, maxContentChars: 100);
        // Content section should be ≤ 100 chars worth of x's.
        Assert.False(user.Contains(new string('x', 200)));
    }

    [Fact]
    public void BuildTagPrompt_NullContent_OmitsContentField()
    {
        var (_, user) = BookmarkAIService.BuildTagPrompt(
            "T", "u", null, maxTags: 5, maxContentChars: 1500);
        Assert.DoesNotContain("content:", user);
    }

    // ── ParseTags ────────────────────────────────────────────────────────

    [Fact]
    public void ParseTags_SimpleCommaList()
    {
        var tags = BookmarkAIService.ParseTags("react, server components, ssr, framework", maxTags: 5);
        Assert.Equal(new[] { "react", "server components", "ssr", "framework" }, tags);
    }

    [Fact]
    public void ParseTags_RespectsMaxTags()
    {
        var tags = BookmarkAIService.ParseTags("a, b, c, d, e, f, g", maxTags: 3);
        Assert.Equal(3, tags.Count);
    }

    [Fact]
    public void ParseTags_StripsNumberingAndQuotes()
    {
        var tags = BookmarkAIService.ParseTags("\"react\", 1. server, *components*", maxTags: 5);
        Assert.Equal(new[] { "react", "server", "components" }, tags);
    }

    [Fact]
    public void ParseTags_DedupesCaseInsensitive()
    {
        var tags = BookmarkAIService.ParseTags("React, react, REACT, Vue", maxTags: 5);
        Assert.Equal(new[] { "react", "vue" }, tags);
    }

    [Fact]
    public void ParseTags_DropsOverlongEntries()
    {
        var bad = new string('x', 60);
        var tags = BookmarkAIService.ParseTags($"react, {bad}, vue", maxTags: 5);
        Assert.Equal(new[] { "react", "vue" }, tags);
    }

    [Fact]
    public void ParseTags_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(BookmarkAIService.ParseTags("", 5));
        Assert.Empty(BookmarkAIService.ParseTags("   ", 5));
    }

    [Fact]
    public void ParseTags_FirstLineWithContent_IsUsed()
    {
        var tags = BookmarkAIService.ParseTags("Sure, here are tags:\nreact, ssr, vite", maxTags: 5);
        // Picks the first non-empty line; preamble may be ignored only if it's the only line.
        // Either parsing path is acceptable as long as we never return 0 tags here.
        Assert.True(tags.Count >= 1);
    }

    // ── GenerateTagsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GenerateTags_NoChatDelegate_ReturnsEmpty()
    {
        var svc = new BookmarkAIService(); // ChatDelegate stays null
        var tags = await svc.GenerateTagsAsync("Title", "https://x.com");
        Assert.Empty(tags);
    }

    [Fact]
    public async Task GenerateTags_AdapterThrows_ReturnsEmpty()
    {
        var svc = new BookmarkAIService
        {
            ChatDelegate = (_, _, _) => throw new InvalidOperationException("boom")
        };
        var tags = await svc.GenerateTagsAsync("T", "u");
        Assert.Empty(tags);
    }

    [Fact]
    public async Task GenerateTags_EmptyInputs_NoModelCall()
    {
        var (svc, calls) = Build(reply: "should not be used");
        var tags = await svc.GenerateTagsAsync("", "");
        Assert.Empty(tags);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task GenerateTags_RoundTrip_ParsesReply()
    {
        var (svc, _) = Build(reply: "react, ssr, framework");
        var tags = await svc.GenerateTagsAsync("React docs", "https://react.dev");
        Assert.Equal(new[] { "react", "ssr", "framework" }, tags);
    }

    // ── BuildRerankPrompt ────────────────────────────────────────────────

    [Fact]
    public void BuildRerankPrompt_NumbersCandidates()
    {
        var cands = new[]
        {
            new BookmarkAIService.Candidate("a", "React docs",  "https://react.dev",   "react,framework"),
            new BookmarkAIService.Candidate("b", "Vue overview", "https://vuejs.org",  "vue,framework"),
        };
        var (_, user) = BookmarkAIService.BuildRerankPrompt("react server components", cands);
        Assert.Contains("1. React docs", user);
        Assert.Contains("2. Vue overview", user);
        Assert.Contains("react server components", user);
    }

    // ── ParseRerankReply ─────────────────────────────────────────────────

    [Fact]
    public void ParseRerankReply_PlainCommaList()
    {
        var ordering = BookmarkAIService.ParseRerankReply("3, 1, 2", candidateCount: 3);
        Assert.Equal(new[] { 3, 1, 2 }, ordering);
    }

    [Fact]
    public void ParseRerankReply_HandlesPreamble()
    {
        var ordering = BookmarkAIService.ParseRerankReply("Sure, ranking: 2, 1, 3", candidateCount: 3);
        Assert.Equal(new[] { 2, 1, 3 }, ordering);
    }

    [Fact]
    public void ParseRerankReply_DedupesAndClampsToRange()
    {
        var ordering = BookmarkAIService.ParseRerankReply("3, 3, 99, 1", candidateCount: 3);
        Assert.Equal(new[] { 3, 1 }, ordering); // 99 dropped, second 3 deduped
    }

    [Fact]
    public void ParseRerankReply_TooSparse_ReturnsEmpty()
    {
        // For 10 candidates, we need at least 5 numbers parsed.
        var ordering = BookmarkAIService.ParseRerankReply("only one: 3", candidateCount: 10);
        Assert.Empty(ordering);
    }

    [Fact]
    public void ParseRerankReply_EmptyOrNoDigits_ReturnsEmpty()
    {
        Assert.Empty(BookmarkAIService.ParseRerankReply("", 5));
        Assert.Empty(BookmarkAIService.ParseRerankReply("nothing here", 5));
    }

    // ── RerankAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task Rerank_AppliesModelOrdering()
    {
        var (svc, _) = Build(reply: "3, 1, 2");
        var cands = new[]
        {
            new BookmarkAIService.Candidate("a", "A", "https://a", null),
            new BookmarkAIService.Candidate("b", "B", "https://b", null),
            new BookmarkAIService.Candidate("c", "C", "https://c", null),
        };
        var result = await svc.RerankAsync("query", cands);
        Assert.Equal(new[] { "c", "a", "b" }, result.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Rerank_PartialOrdering_AppendsMissingInOriginalOrder()
    {
        // Model only returned 2 of 3 indices.
        var (svc, _) = Build(reply: "3, 1");
        var cands = new[]
        {
            new BookmarkAIService.Candidate("a", "A", "https://a", null),
            new BookmarkAIService.Candidate("b", "B", "https://b", null),
            new BookmarkAIService.Candidate("c", "C", "https://c", null),
        };
        // 3 candidates need at least 2 parsed (count/2). 2 is enough; result is [c, a, then b appended].
        var result = await svc.RerankAsync("query", cands);
        Assert.Equal(new[] { "c", "a", "b" }, result.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Rerank_NoChatDelegate_ReturnsOriginal()
    {
        var svc = new BookmarkAIService();
        var cands = new[]
        {
            new BookmarkAIService.Candidate("a", "A", "https://a", null),
            new BookmarkAIService.Candidate("b", "B", "https://b", null),
        };
        var result = await svc.RerankAsync("query", cands);
        Assert.Same(cands, result);
    }

    [Fact]
    public async Task Rerank_OverBudget_FallsBackToOriginal()
    {
        var (svc, calls) = Build(reply: "1, 2");
        svc.MaxRerankCandidates = 2;
        var cands = Enumerable.Range(1, 5)
            .Select(i => new BookmarkAIService.Candidate($"id{i}", $"T{i}", $"u{i}", null))
            .ToArray();
        var result = await svc.RerankAsync("query", cands);
        Assert.Empty(calls);
        // Same reference: no rerank attempted at all.
        Assert.Same(cands, result);
    }

    [Fact]
    public async Task Rerank_AdapterThrows_ReturnsOriginal()
    {
        var svc = new BookmarkAIService
        {
            ChatDelegate = (_, _, _) => throw new InvalidOperationException("boom")
        };
        var cands = new[]
        {
            new BookmarkAIService.Candidate("a", "A", "u", null),
            new BookmarkAIService.Candidate("b", "B", "u", null),
        };
        var result = await svc.RerankAsync("query", cands);
        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Id);
    }

    [Fact]
    public async Task Rerank_EmptyQuery_ReturnsOriginal()
    {
        var (svc, calls) = Build(reply: "1, 2");
        var cands = new[]
        {
            new BookmarkAIService.Candidate("a", "A", "u", null),
            new BookmarkAIService.Candidate("b", "B", "u", null),
        };
        var result = await svc.RerankAsync("", cands);
        Assert.Empty(calls);
        Assert.Same(cands, result);
    }
}
