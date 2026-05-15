using VELO.Core.Council;
using Xunit;

namespace VELO.Core.Tests.Council;

/// <summary>
/// Phase 4.1 chunk E (v2.4.46) — End-to-end test of the WebMessage → orchestrator
/// pipeline. Drives the same path the production runtime will follow:
///   page-side __veloCouncil emits chrome.webview.postMessage(json)
///     → host's BrowserTab.OnWebMessageReceived parses via CouncilBridgeParser
///       → BrowserTab raises CouncilBridgeMessageReceived
///         → MainWindow.OnCouncilBridgeMessage routes into the orchestrator
///           → orchestrator state mutates + raises its own events
///
/// We can't unit-test the WebView2/WPF half (would need an STA dispatcher + a
/// real BrowserTab instance), but the parser + orchestrator pair is the
/// non-cosmetic core of the dispatch. A failure here means the runtime
/// wiring is broken; tests for the JS side live in <c>WiringSmokeTests</c>
/// (script content sanity) + manual verification (chunk G/H).
/// </summary>
public class CouncilDispatchIntegrationTests
{
    /// <summary>Mirrors what MainWindow.OnCouncilBridgeMessage does at runtime —
    /// kept here so the test exercises the same dispatch logic without
    /// pulling in WPF.</summary>
    private static void DispatchToOrchestrator(
        CouncilOrchestrator orch, CouncilBridgeMessage msg)
    {
        if (!orch.HasActiveSession) return;
        switch (msg)
        {
            case CouncilCaptureMessage cap:
                orch.AddCapture(CouncilCapture.Create(
                    cap.Provider, cap.CaptureType, cap.Content, cap.SourceUrl));
                break;
            case CouncilReplyDetectedMessage reply:
                orch.RecordPanelReply(reply.Provider, reply.Text);
                break;
            // CouncilBridgeErrorMessage logged-only in production; no state mutation.
        }
    }

    private static CouncilOrchestrator BuildOrchWithSession(params CouncilProvider[] enabled)
    {
        var o = new CouncilOrchestrator();
        o.StartSession(enabled);
        return o;
    }

    [Fact]
    public void CaptureWebMessage_landsAsCaptureOnMatchingPanel()
    {
        var orch = BuildOrchWithSession(CouncilProvider.Claude);
        const string json =
            """{"type":"council/capture","captureType":"text","content":"hello","sourceUrl":"https://claude.ai/x"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Claude);
        Assert.NotNull(msg);
        DispatchToOrchestrator(orch, msg!);

        var captures = orch.CurrentSession!.GetPanel(CouncilProvider.Claude).Captures;
        Assert.Single(captures);
        Assert.Equal(CouncilCaptureType.Text, captures[0].Type);
        Assert.Equal("hello", captures[0].Content);
        Assert.Equal("https://claude.ai/x", captures[0].SourceUrl);
    }

    [Fact]
    public void ReplyDetectedWebMessage_updatesPanelLatestReplyAndAppendsTranscript()
    {
        var orch = BuildOrchWithSession(CouncilProvider.Grok);
        const string json =
            """{"type":"council/replyDetected","text":"final answer","sourceUrl":"https://grok.com/c/abc"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Grok);
        Assert.NotNull(msg);
        DispatchToOrchestrator(orch, msg!);

        var panel = orch.CurrentSession!.GetPanel(CouncilProvider.Grok);
        Assert.Equal("final answer", panel.LatestReply);
        Assert.Single(orch.CurrentSession.Transcript);
        Assert.Equal(CouncilMessageRole.Panel, orch.CurrentSession.Transcript[0].Role);
        Assert.Equal(CouncilProvider.Grok, orch.CurrentSession.Transcript[0].SourceProvider);
    }

    [Fact]
    public void DispatchWithoutActiveSession_isNoOp()
    {
        var orch = new CouncilOrchestrator(); // session NOT started
        const string json = """{"type":"council/capture","captureType":"text","content":"x"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Claude);
        Assert.NotNull(msg);
        DispatchToOrchestrator(orch, msg!);

        Assert.False(orch.HasActiveSession);
    }

    [Fact]
    public void MultipleCaptures_inOrder_landAsSequentialEntries()
    {
        // Simulates the user clicking capture-text twice in succession on
        // the same panel. Each WebMessage produces an independent capture
        // record in chronological order.
        var orch = BuildOrchWithSession(CouncilProvider.Local);
        string[] payloads = {
            """{"type":"council/capture","captureType":"text","content":"first"}""",
            """{"type":"council/capture","captureType":"code","content":"console.log('x')"}""",
            """{"type":"council/capture","captureType":"text","content":"third"}""",
        };

        foreach (var p in payloads)
        {
            var m = CouncilBridgeParser.Parse(p, CouncilProvider.Local);
            DispatchToOrchestrator(orch, m!);
        }

        var captures = orch.CurrentSession!.GetPanel(CouncilProvider.Local).Captures;
        Assert.Equal(3, captures.Count);
        Assert.Equal("first",                captures[0].Content);
        Assert.Equal("console.log('x')",     captures[1].Content);
        Assert.Equal(CouncilCaptureType.Code, captures[1].Type);
        Assert.Equal("third",                captures[2].Content);
    }

    [Fact]
    public void RoutingRespectsProviderStampedOnMessage()
    {
        // A capture parsed with provider=ChatGpt must land on the ChatGpt panel
        // regardless of which tab fired the WebMessage. This guards against
        // a future regression where a router shortcut accidentally swaps
        // the provider field.
        var orch = BuildOrchWithSession(CouncilProvider.ChatGpt, CouncilProvider.Grok);
        const string json = """{"type":"council/capture","captureType":"text","content":"chatgpt-only"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.ChatGpt);
        DispatchToOrchestrator(orch, msg!);

        Assert.Single(orch.CurrentSession!.GetPanel(CouncilProvider.ChatGpt).Captures);
        Assert.Empty (orch.CurrentSession.GetPanel(CouncilProvider.Grok).Captures);
    }

    [Fact]
    public void BridgeErrorMessage_doesNotMutateOrchestratorState()
    {
        var orch = BuildOrchWithSession(CouncilProvider.Claude);
        const string json = """{"type":"council/error","message":"setAdapter failed"}""";

        var msg = CouncilBridgeParser.Parse(json, CouncilProvider.Claude);
        Assert.IsType<CouncilBridgeErrorMessage>(msg);
        DispatchToOrchestrator(orch, msg!);

        // Error path is log-only — no captures, no transcript entries.
        Assert.Empty(orch.CurrentSession!.GetPanel(CouncilProvider.Claude).Captures);
        Assert.Empty(orch.CurrentSession.Transcript);
    }
}
