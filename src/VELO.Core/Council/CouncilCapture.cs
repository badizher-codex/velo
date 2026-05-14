namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk A — the kind of payload the user clicked-to-capture from
/// a provider panel. Each kind has its own rendering and synthesis treatment:
///
/// <list type="bullet">
///   <item><see cref="Text"/> — prose / paragraph block. Always extractable, the lowest-common-denominator capture.</item>
///   <item><see cref="Code"/> — fenced code block, preserves language hint and indentation. Synthesis preserves verbatim.</item>
///   <item><see cref="Table"/> — markdown / HTML table. Captured as markdown so the moderator can reason about cell values.</item>
///   <item><see cref="Citation"/> — a numbered citation / footnote / source link with URL + accessed date. Surfaces in synthesis as a footnote.</item>
/// </list>
/// </summary>
public enum CouncilCaptureType
{
    Text = 0,
    Code = 1,
    Table = 2,
    Citation = 3,
}

/// <summary>
/// Phase 4.1 chunk A — a single captured fragment from a provider panel. Created
/// when the user clicks a capture button on the per-panel mini-toolbar (Phase 4.1
/// chunk F). The bridge JS (chunk C) gathers the DOM-rooted content and the
/// bridge C# (chunk E) materialises it into this record.
///
/// Immutable so the orchestrator (chunk B) can pass it around without defensive
/// copies. The <see cref="Id"/> is a stable per-session capture key the UI uses
/// to render highlights and the user uses to remove captures from the synthesis
/// queue.
/// </summary>
/// <param name="Id">Stable per-session capture key (GUID string). Survives reordering.</param>
/// <param name="PanelProvider">Which provider this capture came from.</param>
/// <param name="Type">Capture kind. Affects rendering + synthesis treatment.</param>
/// <param name="Content">The captured payload. Plain text for <see cref="CouncilCaptureType.Text"/>;
/// fenced markdown for <see cref="CouncilCaptureType.Code"/> and <see cref="CouncilCaptureType.Table"/>;
/// JSON-shape <c>{ "text": ..., "url": ..., "accessedAt": ... }</c> for <see cref="CouncilCaptureType.Citation"/>.</param>
/// <param name="SourceUrl">URL the provider panel was on when captured. May be the chat URL or a
/// source link (for citations). Empty string allowed for Local provider (no webview).</param>
/// <param name="CapturedAtUtc">When the user clicked the capture button.</param>
public sealed record CouncilCapture(
    string             Id,
    CouncilProvider    PanelProvider,
    CouncilCaptureType Type,
    string             Content,
    string             SourceUrl,
    DateTime           CapturedAtUtc)
{
    /// <summary>Convenience factory that generates a fresh GUID-backed ID.</summary>
    public static CouncilCapture Create(
        CouncilProvider provider,
        CouncilCaptureType type,
        string content,
        string sourceUrl,
        DateTime? capturedAtUtc = null)
        => new(
            Guid.NewGuid().ToString("N"),
            provider,
            type,
            content ?? throw new ArgumentNullException(nameof(content)),
            sourceUrl ?? "",
            capturedAtUtc ?? DateTime.UtcNow);
}
