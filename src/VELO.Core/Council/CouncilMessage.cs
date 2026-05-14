namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk A — who emitted a <see cref="CouncilMessage"/>. The set is
/// intentionally tiny: the user, the local moderator, or one of the cloud
/// provider panels. Cloud provider identity is in the message itself (see
/// <see cref="CouncilMessage.SourceProvider"/>) so this enum stays
/// orthogonal to provider count.
/// </summary>
public enum CouncilMessageRole
{
    /// <summary>The master prompt the user typed in the Council Bar (chunk F).</summary>
    User = 0,
    /// <summary>A captured response from one of the four panels (or the local moderator path).</summary>
    Panel = 1,
    /// <summary>The final synthesised reply from the local moderator (qwen3:32b or whatever the user configured).</summary>
    Moderator = 2,
    /// <summary>VELO-emitted system message (errors, status, "panel unavailable" etc).</summary>
    System = 3,
}

/// <summary>
/// Phase 4.1 chunk A — one entry in the linear Council session transcript.
/// A session typically looks like:
/// <code>
///   [User]      "Master prompt"
///   [Panel:Claude]    "Claude's answer"
///   [Panel:ChatGpt]   "ChatGPT's answer"
///   [Panel:Grok]      "Grok's answer"
///   [Panel:Local]     "Ollama's answer"
///   [Moderator]       "Synthesised reply"
/// </code>
/// Subsequent turns append the same shape. Captures (sub-fragments of panel
/// answers) live in <see cref="CouncilCapture"/> and are referenced from
/// <see cref="CapturedRefs"/> when the moderator synthesised only selected
/// fragments rather than the whole panel reply.
/// </summary>
/// <param name="Id">Stable per-session message ID. Used for transcript scroll-restore and export ordering.</param>
/// <param name="Role">Who emitted this message.</param>
/// <param name="SourceProvider">For <see cref="CouncilMessageRole.Panel"/> messages: which provider.
/// Null for User / Moderator / System.</param>
/// <param name="Text">The message body. Markdown allowed (links, code fences, tables).</param>
/// <param name="CapturedRefs">IDs of <see cref="CouncilCapture"/> records this message references.
/// Empty when the message is a full-panel reply (no fragment selection).</param>
/// <param name="EmittedAtUtc">When the message landed in the transcript.</param>
public sealed record CouncilMessage(
    string                Id,
    CouncilMessageRole    Role,
    CouncilProvider?      SourceProvider,
    string                Text,
    IReadOnlyList<string> CapturedRefs,
    DateTime              EmittedAtUtc)
{
    /// <summary>Convenience factory for user prompts.</summary>
    public static CouncilMessage UserPrompt(string text, DateTime? at = null)
        => new(Guid.NewGuid().ToString("N"), CouncilMessageRole.User, null,
               text ?? throw new ArgumentNullException(nameof(text)),
               Array.Empty<string>(), at ?? DateTime.UtcNow);

    /// <summary>Convenience factory for a captured panel reply.</summary>
    public static CouncilMessage PanelReply(
        CouncilProvider provider,
        string text,
        IReadOnlyList<string>? capturedRefs = null,
        DateTime? at = null)
        => new(Guid.NewGuid().ToString("N"), CouncilMessageRole.Panel, provider,
               text ?? throw new ArgumentNullException(nameof(text)),
               capturedRefs ?? Array.Empty<string>(), at ?? DateTime.UtcNow);

    /// <summary>Convenience factory for the moderator synthesis.</summary>
    public static CouncilMessage Synthesis(
        string text,
        IReadOnlyList<string>? capturedRefs = null,
        DateTime? at = null)
        => new(Guid.NewGuid().ToString("N"), CouncilMessageRole.Moderator, null,
               text ?? throw new ArgumentNullException(nameof(text)),
               capturedRefs ?? Array.Empty<string>(), at ?? DateTime.UtcNow);

    /// <summary>Convenience factory for system / status messages.</summary>
    public static CouncilMessage System(string text, DateTime? at = null)
        => new(Guid.NewGuid().ToString("N"), CouncilMessageRole.System, null,
               text ?? throw new ArgumentNullException(nameof(text)),
               Array.Empty<string>(), at ?? DateTime.UtcNow);
}
