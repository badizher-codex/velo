using VELO.Agent;
using Xunit;

namespace VELO.Agent.Tests;

public class AIContextActionsTests
{
    private static AIContextActions NewActions(
        Func<string, string, CancellationToken, Task<string>>? chat = null,
        bool supportsVision = false,
        string adapterName  = "Test")
    {
        var a = new AIContextActions
        {
            SupportsVision = supportsVision,
            AdapterName    = adapterName,
        };
        if (chat != null) a.ChatDelegate = chat;
        return a;
    }

    [Fact]
    public async Task Explain_UsesProvidedChatDelegate_WhenWired()
    {
        // Adapter selection happens in the host (DI). The class just consumes
        // whatever ChatDelegate the host wires — testing that it routes the
        // request and returns the model's reply.
        string? capturedSystem = null, capturedUser = null;
        var actions = NewActions(adapterName: "Local-Ollama",
            chat: (sys, user, _) =>
            {
                capturedSystem = sys;
                capturedUser   = user;
                return Task.FromResult("posit arithmetic is …");
            });

        var result = await actions.ExplainAsync("posit-to-posit floating point", "es");

        Assert.Equal("posit arithmetic is …", result);
        Assert.NotNull(capturedSystem);
        Assert.Contains("Spanish", capturedSystem!);
        Assert.Contains("posit-to-posit", capturedUser!);
    }

    [Fact]
    public async Task Explain_FallsBackToTaggedTemplate_WhenNoChatDelegate()
    {
        // ChatDelegate left null — emulates the "Offline" mode where no
        // backend is configured. Method must NOT throw, must return some
        // string the UI can show.
        var actions = NewActions(adapterName: "Offline");

        var result = await actions.ExplainAsync("photosynthesis", "es");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("photosynthesis", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Summarize_MapReduceKicks_WhenContentExceedsThreshold()
    {
        // Build a long content (3× threshold) made of trivially distinguishable
        // chunks so we can see how many times the chat was called.
        int calls = 0;
        var actions = NewActions(chat: (_, _, _) =>
        {
            calls++;
            return Task.FromResult($"summary#{calls}");
        });
        actions.MapReduceThresholdChars = 100;
        actions.ChunkSizeChars          = 50;

        var content = new string('a', 60) + ". " + new string('b', 60) + ". " + new string('c', 60);
        var result  = await actions.SummarizeAsync(content);

        // Map = N partial summaries (one per chunk). Reduce = 1 final pass.
        Assert.True(calls >= 3, $"expected at least 3 chat calls (≥2 map + 1 reduce), got {calls}");
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task DescribeImage_ReturnsCapabilityError_WhenAdapterLacksVision()
    {
        var actions = NewActions(supportsVision: false);
        var dummyImg = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic

        var result = await actions.DescribeImageAsync(dummyImg);

        Assert.Equal(AIContextActions.VisionUnsupportedMessage, result);
    }

    [Fact]
    public async Task Extract_Links_ParsesCorrectlyFromPlainText()
    {
        var actions = NewActions();
        var text = """
            Check out https://example.com/foo.html and also
            http://test.example.org/path?q=1 . Also email a@b.com please.
            """;

        var links = await actions.ExtractAsync(text, ExtractionKind.Links);

        Assert.Equal(2, links.Count);
        Assert.Contains(links, e => e.Value == "https://example.com/foo.html");
        Assert.Contains(links, e => e.Value == "http://test.example.org/path?q=1");

        // Sanity — Emails extraction picks the email and not the URLs.
        var emails = await actions.ExtractAsync(text, ExtractionKind.Emails);
        Assert.Single(emails);
        Assert.Equal("a@b.com", emails[0].Value);
    }

    [Fact]
    public async Task FactCheck_IncludesDisclaimerInResponse()
    {
        var actions = NewActions(chat: (_, _, _) =>
            Task.FromResult("Verdict: contested. The earth is not flat."));

        var result = await actions.FactCheckAsync("the earth is flat");

        Assert.Contains("Verdict: contested", result);
        Assert.Contains(AIContextActions.Disclaimer, result);
    }

    [Fact]
    public void Translate_DetectsSourceLang_WhenNotSpecified()
    {
        // Bag-of-stopwords heuristic: each language sample lights up its
        // own bucket the most. We don't need perfect accuracy — just
        // distinguishability for the UI hint.
        Assert.Equal("es", AIContextActions.DetectLanguage("El gato está en la casa y la casa es de los abuelos."));
        Assert.Equal("en", AIContextActions.DetectLanguage("The cat is in the house and the house belongs to the grandparents."));
        Assert.Equal("fr", AIContextActions.DetectLanguage("Le chat est dans la maison et la maison est des grands-parents."));
        Assert.Equal("de", AIContextActions.DetectLanguage("Die Katze ist in dem Haus und das Haus ist von den Großeltern."));
    }
}
