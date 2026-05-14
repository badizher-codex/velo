using System.Text.Json;
using System.Text.Json.Serialization;

namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk D — Provider-specific DOM selectors used by
/// <c>council-bridge.js</c> to drive a panel (paste prompt, click send,
/// capture latest reply). Mirrors the JSON shape stored under
/// <c>resources/council/adapters/{name}.json</c>.
///
/// Versioning: <see cref="Version"/> is a hand-stamped token like
/// <c>v1-2026-05-14</c> bumped whenever the maintainer adjusts selectors.
/// Diagnostics surface the version so bug reports about "Council can't
/// capture from Claude any more" can be matched to a specific selector set.
///
/// Serialised back to JSON when the host hands the adapter to the JS bridge
/// via <c>__veloCouncil.setAdapter(json)</c>. Field names use camelCase to
/// match the JS surface verbatim.
/// </summary>
public sealed class CouncilAdapter
{
    [JsonPropertyName("name")]              public string Name              { get; set; } = "";
    [JsonPropertyName("displayName")]       public string DisplayName       { get; set; } = "";
    [JsonPropertyName("version")]           public string Version           { get; set; } = "";
    [JsonPropertyName("homeUrl")]           public string HomeUrl           { get; set; } = "";

    /// <summary>CSS selector for the prompt input. Commonly a textarea, but
    /// some providers use contenteditable divs (Claude's ProseMirror).</summary>
    [JsonPropertyName("composer")]          public string Composer          { get; set; } = "";

    /// <summary>CSS selector for the send/submit button. Comma-separated
    /// fallback list allowed.</summary>
    [JsonPropertyName("sendButton")]        public string SendButton        { get; set; } = "";

    /// <summary>CSS selector that lists assistant response bubbles in
    /// document order; the bridge picks the last one.</summary>
    [JsonPropertyName("responseContainer")] public string ResponseContainer { get; set; } = "";

    /// <summary>CSS selector for code blocks inside a response. Optional —
    /// defaults to <c>pre code, pre</c> on the JS side when empty.</summary>
    [JsonPropertyName("codeBlock")]         public string CodeBlock         { get; set; } = "";

    /// <summary>CSS selector for tables inside a response. Optional.</summary>
    [JsonPropertyName("table")]             public string Table             { get; set; } = "";

    /// <summary>CSS selector for citation/source links inside a response.
    /// Optional.</summary>
    [JsonPropertyName("citation")]          public string Citation          { get; set; } = "";

    /// <summary>Free-form maintainer note about why specific selectors were
    /// chosen. Surfaced only in adapter-registry diagnostics; the JS bridge
    /// ignores it.</summary>
    [JsonPropertyName("notes")]             public string Notes             { get; set; } = "";

    /// <summary>True when the adapter has the minimum fields the bridge
    /// requires to drive a panel: composer + sendButton + responseContainer.
    /// Everything else falls back to JS defaults.</summary>
    [JsonIgnore]
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Composer) &&
        !string.IsNullOrWhiteSpace(SendButton) &&
        !string.IsNullOrWhiteSpace(ResponseContainer);
}
