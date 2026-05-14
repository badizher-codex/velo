using VELO.Core.AI;
using VELO.Core.Council;
using Xunit;

namespace VELO.Core.Tests.Council;

/// <summary>
/// Phase 4.1 chunk B — covers session lifecycle, capture coordination, transcript
/// emission and synthesis routing. The orchestrator is single-threaded by design;
/// these tests run synchronously and inspect state after each call.
/// </summary>
public class CouncilOrchestratorTests
{
    /// <summary>Fake ChatDelegate the orchestrator wires as its synthesiser.
    /// Records the prompts it was called with so the test can assert on them.</summary>
    private sealed class FakeSynthesizer
    {
        public string Reply { get; set; } = "synthesised";
        public string? LastSystemPrompt { get; private set; }
        public string? LastUserPrompt { get; private set; }
        public int CallCount { get; private set; }
        public Exception? ThrowOnNext { get; set; }

        public AiChatRouter.ChatDelegate Delegate => (sys, usr, ct) =>
        {
            LastSystemPrompt = sys;
            LastUserPrompt   = usr;
            CallCount++;
            if (ThrowOnNext is not null)
            {
                var e = ThrowOnNext;
                ThrowOnNext = null;
                throw e;
            }
            return Task.FromResult(Reply);
        };
    }

    // ── Session lifecycle ───────────────────────────────────────────────

    [Fact]
    public void StartSession_marksEnabledProvidersAvailable()
    {
        var orch = new CouncilOrchestrator();
        var session = orch.StartSession(new[] { CouncilProvider.Claude, CouncilProvider.Local });

        Assert.Same(session, orch.CurrentSession);
        Assert.True(orch.HasActiveSession);
        Assert.True(session.GetPanel(CouncilProvider.Claude).IsAvailable);
        Assert.False(session.GetPanel(CouncilProvider.ChatGpt).IsAvailable);
        Assert.False(session.GetPanel(CouncilProvider.Grok).IsAvailable);
        Assert.True(session.GetPanel(CouncilProvider.Local).IsAvailable);
        Assert.Equal(2, session.AvailablePanelCount);
    }

    [Fact]
    public void EndSession_clearsCurrentSession()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(CouncilProviderMap.All);
        orch.EndSession();

        Assert.False(orch.HasActiveSession);
        Assert.Null(orch.CurrentSession);
    }

    // ── Capture flow ────────────────────────────────────────────────────

    [Fact]
    public void AddCapture_appendsToPanelAndRaisesEvent()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(new[] { CouncilProvider.Claude });
        CouncilCapture? observed = null;
        orch.CaptureReceived += (_, c) => observed = c;

        var cap = CouncilCapture.Create(
            CouncilProvider.Claude, CouncilCaptureType.Text, "hola", "https://claude.ai/x");
        orch.AddCapture(cap);

        Assert.Single(orch.CurrentSession!.GetPanel(CouncilProvider.Claude).Captures);
        Assert.Same(cap, observed);
    }

    [Fact]
    public void AddCapture_throwsWhenNoActiveSession()
    {
        var orch = new CouncilOrchestrator();
        var cap = CouncilCapture.Create(
            CouncilProvider.Claude, CouncilCaptureType.Text, "x", "https://claude.ai");

        Assert.Throws<InvalidOperationException>(() => orch.AddCapture(cap));
    }

    [Fact]
    public void AddCapture_throwsWhenPanelNotAvailable()
    {
        var orch = new CouncilOrchestrator();
        // Only enable Claude — Grok stays disabled.
        orch.StartSession(new[] { CouncilProvider.Claude });
        var cap = CouncilCapture.Create(
            CouncilProvider.Grok, CouncilCaptureType.Text, "x", "https://grok.com");

        Assert.Throws<InvalidOperationException>(() => orch.AddCapture(cap));
    }

    [Fact]
    public void RemoveCapture_findsAndRemovesAcrossPanels()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(CouncilProviderMap.All);
        var cap = orch.AddCapture(CouncilCapture.Create(
            CouncilProvider.Grok, CouncilCaptureType.Code, "code", "https://grok.com"));

        Assert.True(orch.RemoveCapture(cap.Id));
        Assert.Empty(orch.CurrentSession!.GetPanel(CouncilProvider.Grok).Captures);
    }

    [Fact]
    public void RemoveCapture_returnsFalseForUnknownId()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(CouncilProviderMap.All);
        Assert.False(orch.RemoveCapture("missing"));
    }

    // ── Transcript ──────────────────────────────────────────────────────

    [Fact]
    public void AppendUserPrompt_appendsAndRaisesMessageAppended()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(CouncilProviderMap.All);
        CouncilMessage? observed = null;
        orch.MessageAppended += (_, m) => observed = m;

        orch.AppendUserPrompt("¿Qué es VELO?");

        Assert.Single(orch.CurrentSession!.Transcript);
        Assert.Equal(CouncilMessageRole.User, observed!.Role);
        Assert.Equal("¿Qué es VELO?", observed.Text);
    }

    [Fact]
    public void RecordPanelReply_setsLatestReplyAndAppendsTranscript()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(new[] { CouncilProvider.Claude });

        orch.RecordPanelReply(CouncilProvider.Claude, "Claude's answer");

        var panel = orch.CurrentSession!.GetPanel(CouncilProvider.Claude);
        Assert.Equal("Claude's answer", panel.LatestReply);
        Assert.Single(orch.CurrentSession.Transcript);
        Assert.Equal(CouncilMessageRole.Panel, orch.CurrentSession.Transcript[0].Role);
        Assert.Equal(CouncilProvider.Claude, orch.CurrentSession.Transcript[0].SourceProvider);
    }

    [Fact]
    public void AppendSystemMessage_appendsWithSystemRole()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(CouncilProviderMap.All);

        orch.AppendSystemMessage("Panel Grok no disponible");

        Assert.Equal(CouncilMessageRole.System, orch.CurrentSession!.Transcript[0].Role);
    }

    // ── Synthesis ───────────────────────────────────────────────────────

    [Fact]
    public async Task SynthesizeAsync_callsSynthesizerWithCombinedPanelReplies()
    {
        var orch = new CouncilOrchestrator();
        var fake = new FakeSynthesizer { Reply = "the synthesis" };
        orch.Synthesizer = fake.Delegate;
        orch.StartSession(new[] { CouncilProvider.Claude, CouncilProvider.Grok });
        orch.RecordPanelReply(CouncilProvider.Claude, "Claude says A");
        orch.RecordPanelReply(CouncilProvider.Grok,   "Grok says B");

        var msg = await orch.SynthesizeAsync("master prompt here");

        Assert.Equal(1, fake.CallCount);
        Assert.NotNull(fake.LastUserPrompt);
        Assert.Contains("master prompt here", fake.LastUserPrompt);
        Assert.Contains("## Claude", fake.LastUserPrompt);
        Assert.Contains("Claude says A", fake.LastUserPrompt);
        Assert.Contains("## Grok", fake.LastUserPrompt);
        Assert.Contains("Grok says B", fake.LastUserPrompt);
        // ChatGpt + Local were not enabled, so they must NOT be in the prompt.
        Assert.DoesNotContain("## ChatGpt", fake.LastUserPrompt);
        Assert.DoesNotContain("## Local",   fake.LastUserPrompt);

        Assert.Equal("the synthesis", msg.Text);
        Assert.Equal(CouncilMessageRole.Moderator, msg.Role);
    }

    [Fact]
    public async Task SynthesizeAsync_throwsWhenNoActiveSession()
    {
        var orch = new CouncilOrchestrator();
        orch.Synthesizer = new FakeSynthesizer().Delegate;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orch.SynthesizeAsync("x"));
    }

    [Fact]
    public async Task SynthesizeAsync_throwsWhenNoSynthesizerConfigured()
    {
        var orch = new CouncilOrchestrator();
        orch.StartSession(CouncilProviderMap.All);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orch.SynthesizeAsync("x"));
    }

    [Fact]
    public async Task SynthesizeAsync_synthesisReadyFiresAfterMessageAppended()
    {
        var orch = new CouncilOrchestrator();
        orch.Synthesizer = new FakeSynthesizer { Reply = "synth" }.Delegate;
        orch.StartSession(new[] { CouncilProvider.Claude });
        orch.RecordPanelReply(CouncilProvider.Claude, "reply");

        var order = new List<string>();
        orch.MessageAppended += (_, m) =>
        {
            if (m.Role == CouncilMessageRole.Moderator) order.Add("appended");
        };
        orch.SynthesisReady += (_, _) => order.Add("ready");

        await orch.SynthesizeAsync("master");

        // MessageAppended must fire BEFORE SynthesisReady so subscribers who render the
        // transcript first see the row in place before synthesis-specific UI runs.
        Assert.Equal(new[] { "appended", "ready" }, order);
    }

    [Fact]
    public async Task SynthesizeAsync_logsSystemErrorAndRethrowsOnSynthesiserFailure()
    {
        var orch = new CouncilOrchestrator();
        var fake = new FakeSynthesizer { ThrowOnNext = new InvalidOperationException("kaboom") };
        orch.Synthesizer = fake.Delegate;
        orch.StartSession(new[] { CouncilProvider.Claude });
        orch.RecordPanelReply(CouncilProvider.Claude, "reply");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orch.SynthesizeAsync("master"));

        // The transcript must contain the user-visible system error.
        Assert.Contains(orch.CurrentSession!.Transcript,
            m => m.Role == CouncilMessageRole.System && m.Text.Contains("kaboom"));
    }

    [Fact]
    public async Task SynthesizeAsync_propagatesCancellation_withoutSystemMessage()
    {
        var orch = new CouncilOrchestrator();
        var fake = new FakeSynthesizer { ThrowOnNext = new OperationCanceledException() };
        orch.Synthesizer = fake.Delegate;
        orch.StartSession(new[] { CouncilProvider.Claude });
        orch.RecordPanelReply(CouncilProvider.Claude, "reply");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => orch.SynthesizeAsync("master"));

        // Cancellation is not a failure — no system error row should be emitted.
        Assert.DoesNotContain(orch.CurrentSession!.Transcript,
            m => m.Role == CouncilMessageRole.System);
    }

    [Fact]
    public void BuildPanelReplyBlock_skipsUnavailableAndEmptyPanels()
    {
        var session = new CouncilSession();
        session.GetPanel(CouncilProvider.Claude).IsAvailable = true;
        session.GetPanel(CouncilProvider.Claude).LatestReply = "alpha";
        session.GetPanel(CouncilProvider.ChatGpt).IsAvailable = true;    // No reply yet — must be skipped.
        session.GetPanel(CouncilProvider.Grok).IsAvailable = false;
        session.GetPanel(CouncilProvider.Grok).LatestReply = "should-not-appear";
        session.GetPanel(CouncilProvider.Local).IsAvailable = true;
        session.GetPanel(CouncilProvider.Local).LatestReply = "omega";

        var block = CouncilOrchestrator.BuildPanelReplyBlock(session);

        Assert.Contains("## Claude", block);
        Assert.Contains("alpha", block);
        Assert.DoesNotContain("## ChatGpt", block);
        Assert.DoesNotContain("## Grok", block);
        Assert.DoesNotContain("should-not-appear", block);
        Assert.Contains("## Local", block);
        Assert.Contains("omega", block);
    }
}
