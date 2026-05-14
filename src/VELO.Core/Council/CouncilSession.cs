namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk A — runtime state for a single Council Mode activation.
/// One session spans the lifetime from "user opens Council" to "user closes
/// the 2×2 layout"; multiple turns can happen within one session.
///
/// Owned by <c>CouncilOrchestrator</c> (chunk B) which mutates the panels
/// and transcript as captures land. The session is intentionally not
/// persisted across VELO restarts — Phase 4.1 keeps it in-memory; Phase
/// 4.4 polish may add a save-to-disk flow when the user clicks Export.
/// </summary>
public sealed class CouncilSession
{
    /// <summary>Stable per-session ID. Used in logs and on export filenames.</summary>
    public string Id { get; }

    /// <summary>When the session started.</summary>
    public DateTime StartedAtUtc { get; }

    /// <summary>The four panel slots in panel-index order (0 = top-left → 3 = bottom-right).
    /// Always length 4 even when some providers are disabled — disabled panels have
    /// <see cref="CouncilPanel.IsAvailable"/> = false.</summary>
    public IReadOnlyList<CouncilPanel> Panels { get; }

    /// <summary>Linear transcript: master prompts, panel replies, moderator syntheses,
    /// system messages. New entries appended in emission order. Stable across turns.</summary>
    public IReadOnlyList<CouncilMessage> Transcript => _transcript;
    private readonly List<CouncilMessage> _transcript = new();

    public CouncilSession(IEnumerable<CouncilPanel>? panels = null, DateTime? startedAtUtc = null)
    {
        Id           = Guid.NewGuid().ToString("N");
        StartedAtUtc = startedAtUtc ?? DateTime.UtcNow;

        if (panels is null)
        {
            // Default: one disabled panel per provider in canonical order.
            Panels = CouncilProviderMap.All.Select(p => new CouncilPanel(p)).ToList();
            return;
        }

        var list = panels.ToList();
        if (list.Count != 4)
            throw new ArgumentException(
                $"Council session must have exactly 4 panels, got {list.Count}.",
                nameof(panels));

        // Verify the panels cover the canonical provider set, in canonical order.
        for (int i = 0; i < 4; i++)
        {
            var expected = CouncilProviderMap.All[i];
            if (list[i].Provider != expected)
                throw new ArgumentException(
                    $"Panel at index {i} is {list[i].Provider} but canonical order expects {expected}.",
                    nameof(panels));
        }
        Panels = list;
    }

    /// <summary>Lookup panel by provider. Throws if the provider is somehow not in the session
    /// (impossible with the validated constructor but defensive).</summary>
    public CouncilPanel GetPanel(CouncilProvider provider)
    {
        for (int i = 0; i < Panels.Count; i++)
            if (Panels[i].Provider == provider) return Panels[i];
        throw new InvalidOperationException($"Council session has no panel for {provider}.");
    }

    /// <summary>Appends a message to the transcript. Internal — only the orchestrator emits messages.</summary>
    internal CouncilMessage AppendMessage(CouncilMessage message)
    {
        _transcript.Add(message);
        return message;
    }

    /// <summary>Number of panels currently opted-in + alive.</summary>
    public int AvailablePanelCount => Panels.Count(p => p.IsAvailable);
}
