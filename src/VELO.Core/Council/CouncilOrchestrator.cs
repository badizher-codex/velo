using VELO.Core.AI;

namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk B — coordinates the lifetime and capture flow of one
/// Council Mode session. Owns exactly one <see cref="CouncilSession"/> at
/// a time and routes everything through it:
///
/// <list type="bullet">
///   <item>panel replies → transcript (chunk E feeds these from the bridge);</item>
///   <item>user-clicked captures → the originating panel's capture list (chunk F feeds these);</item>
///   <item>"Send all" master prompt → user message in transcript (chunk F);</item>
///   <item>synthesis request → local <see cref="Synthesizer"/> ChatDelegate → moderator message.</item>
/// </list>
///
/// Backend-agnostic by design: the moderator ChatDelegate is the same one
/// every other AI consumer uses (Sprint 10A's <c>AiChatRouter</c> pattern).
/// In production the host wires it to Custom AI Mode talking to Ollama / LM
/// Studio / generic OpenAI-compat (v2.4.40 made this configurable). In tests
/// it's a fake that returns canned strings.
///
/// Pure C#, no WPF dependency — lives in VELO.Core so it can be unit-tested
/// without a dispatcher.
///
/// Thread-affinity: single-threaded via WPF dispatcher in production.
/// Events fire synchronously on the calling thread.
/// </summary>
public sealed class CouncilOrchestrator
{
    /// <summary>The local moderator's ChatDelegate. Must be set before <see cref="SynthesizeAsync"/>.
    /// Host (MainWindow) typically wires this from <c>AiChatRouter</c> the same way every
    /// other AI service does.</summary>
    public AiChatRouter.ChatDelegate? Synthesizer { get; set; }

    /// <summary>The session being driven right now, or null if Council is not active.</summary>
    public CouncilSession? CurrentSession { get; private set; }

    /// <summary>True when a session is in progress.</summary>
    public bool HasActiveSession => CurrentSession is not null;

    /// <summary>Raised when a user clicks a capture button on a panel toolbar.</summary>
    public event EventHandler<CouncilCapture>? CaptureReceived;

    /// <summary>Raised whenever a new <see cref="CouncilMessage"/> lands in the transcript.</summary>
    public event EventHandler<CouncilMessage>? MessageAppended;

    /// <summary>Raised after the moderator's synthesis lands. Fires AFTER <see cref="MessageAppended"/>
    /// for the same message so subscribers ordering "first transcript, then synthesis-specific UI"
    /// see them in the natural order.</summary>
    public event EventHandler<CouncilMessage>? SynthesisReady;

    /// <summary>
    /// Starts a new session. Panels for providers in <paramref name="enabledProviders"/> are
    /// marked <see cref="CouncilPanel.IsAvailable"/> = true; the rest stay disabled (the 2×2
    /// layout still shows four cells, but disabled cells render an "opt-in" placeholder
    /// instead of a webview — chunk F).
    /// </summary>
    public CouncilSession StartSession(IEnumerable<CouncilProvider> enabledProviders)
    {
        var enabledSet = new HashSet<CouncilProvider>(enabledProviders);
        var panels = CouncilProviderMap.All
            .Select(p => new CouncilPanel(p, isAvailable: enabledSet.Contains(p)))
            .ToList();
        CurrentSession = new CouncilSession(panels);
        return CurrentSession;
    }

    /// <summary>Tears down the current session. Subsequent operations require a fresh
    /// <see cref="StartSession"/>.</summary>
    public void EndSession()
    {
        CurrentSession = null;
    }

    /// <summary>
    /// Records a user-clicked capture against the panel it came from. Raises
    /// <see cref="CaptureReceived"/> synchronously. Throws if no session is active or if
    /// the panel is not available (we should never receive a capture from a disabled panel
    /// — that's a wiring bug worth surfacing).
    /// </summary>
    public CouncilCapture AddCapture(CouncilCapture capture)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        var session = RequireSession();
        var panel   = session.GetPanel(capture.PanelProvider);

        if (!panel.IsAvailable)
            throw new InvalidOperationException(
                $"Cannot add capture: panel {capture.PanelProvider} is not available in this session.");

        panel.AddCapture(capture);
        CaptureReceived?.Invoke(this, capture);
        return capture;
    }

    /// <summary>Removes a capture by ID, searching all panels. Returns true if found.</summary>
    public bool RemoveCapture(string captureId)
    {
        if (string.IsNullOrEmpty(captureId)) return false;
        if (CurrentSession is null) return false;
        foreach (var panel in CurrentSession.Panels)
            if (panel.RemoveCapture(captureId)) return true;
        return false;
    }

    /// <summary>Appends a user-typed master prompt to the transcript.</summary>
    public CouncilMessage AppendUserPrompt(string text)
    {
        var session = RequireSession();
        var msg = CouncilMessage.UserPrompt(text);
        session.AppendMessage(msg);
        MessageAppended?.Invoke(this, msg);
        return msg;
    }

    /// <summary>
    /// Records what one of the panels replied for the current turn. Updates
    /// <see cref="CouncilPanel.LatestReply"/> (used as the synthesis input) and appends
    /// the message to the transcript.
    /// </summary>
    public CouncilMessage RecordPanelReply(
        CouncilProvider provider,
        string text,
        IReadOnlyList<string>? capturedRefs = null)
    {
        var session = RequireSession();
        var panel   = session.GetPanel(provider);

        panel.LatestReply = text ?? "";
        var msg = CouncilMessage.PanelReply(provider, panel.LatestReply, capturedRefs);
        session.AppendMessage(msg);
        MessageAppended?.Invoke(this, msg);
        return msg;
    }

    /// <summary>Records a system message ("Panel Grok no disponible", "Sesión finalizada", etc).</summary>
    public CouncilMessage AppendSystemMessage(string text)
    {
        var session = RequireSession();
        var msg = CouncilMessage.System(text);
        session.AppendMessage(msg);
        MessageAppended?.Invoke(this, msg);
        return msg;
    }

    /// <summary>
    /// Calls the local moderator with the master prompt + every available panel's latest reply.
    /// Awaits the synthesis text, appends it as a <see cref="CouncilMessageRole.Moderator"/>
    /// message, and raises <see cref="SynthesisReady"/>. Cancellation propagates verbatim.
    /// Other exceptions are surfaced both as a System message in the transcript AND re-thrown
    /// to the caller (so the Council Bar can render an error toast).
    /// </summary>
    public async Task<CouncilMessage> SynthesizeAsync(string masterPrompt, CancellationToken ct = default)
    {
        var session = RequireSession();
        if (Synthesizer is null)
            throw new InvalidOperationException(
                "Synthesizer ChatDelegate not configured. Wire it from AiChatRouter at startup.");

        var combined = BuildPanelReplyBlock(session);
        var userPrompt = $"Master prompt:\n{masterPrompt}\n\n---\n\nPanel replies:\n\n{combined}";

        string synthesisText;
        try
        {
            synthesisText = await Synthesizer.Invoke(SynthesisSystemPrompt, userPrompt, ct)
                             .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var err = CouncilMessage.System($"Síntesis falló: {ex.Message}");
            session.AppendMessage(err);
            MessageAppended?.Invoke(this, err);
            throw;
        }

        var synthesis = CouncilMessage.Synthesis(synthesisText ?? "");
        session.AppendMessage(synthesis);
        MessageAppended?.Invoke(this, synthesis);
        SynthesisReady?.Invoke(this, synthesis);
        return synthesis;
    }

    private CouncilSession RequireSession()
    {
        if (CurrentSession is null)
            throw new InvalidOperationException(
                "No active Council session. Call StartSession first.");
        return CurrentSession;
    }

    /// <summary>
    /// Composes the panel-replies block fed to the synthesis prompt. Iterates panels in
    /// canonical order so the moderator sees the same shape every turn. Skips panels that
    /// are unavailable or have no reply yet.
    /// </summary>
    internal static string BuildPanelReplyBlock(CouncilSession session)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var panel in session.Panels)
        {
            if (!panel.IsAvailable) continue;
            if (string.IsNullOrWhiteSpace(panel.LatestReply)) continue;
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.Append("## ").AppendLine(panel.Provider.ToString());
            sb.Append(panel.LatestReply);
        }
        return sb.ToString();
    }

    /// <summary>
    /// System prompt for the moderator. The synthesis directive is intentionally short
    /// (the model only has 16 k context, and we may feed it ~12 k of panel replies).
    /// Reply-language directive matches the rest of VELO's AI services (lesson #10).
    /// </summary>
    internal const string SynthesisSystemPrompt = """
        You are the Council moderator. The user asked a question; up to four parallel AI assistants answered. Your job is to synthesise a single accurate, well-grounded reply that:
          - notes where the assistants agree;
          - highlights disagreements explicitly, attributing each side;
          - flags any claims that look hallucinated or unsupported;
          - cites the source assistant in parentheses when you quote it (e.g. "(Claude)", "(ChatGpt)", "(Grok)", "(Local)").
        Reply in the same language as the user's master prompt. Do not add a preamble — start directly with the synthesis.
        """;
}
