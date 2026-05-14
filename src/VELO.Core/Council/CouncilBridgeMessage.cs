using System.Text.Json;

namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk C — incoming WebMessage shapes emitted by
/// <c>council-bridge.js</c>. The page-side script wraps every event in a
/// flat JSON object with a <c>type</c> discriminator; the parser here
/// converts those raw payloads into strongly-typed records the orchestrator
/// (chunk B) can consume.
///
/// Stays in VELO.Core so the parser is unit-testable without WebView2.
/// </summary>
public abstract record CouncilBridgeMessage
{
    /// <summary>Provider this message originated from. Stamped by the C# side
    /// when the message is parsed — the JS bridge doesn't know about
    /// <see cref="CouncilProvider"/>, only the host does (it knows which
    /// BrowserTab fired the WebMessage event).</summary>
    public CouncilProvider Provider { get; init; }

    /// <summary>URL the panel was on when the bridge emitted the message.
    /// Useful for citation building and for distinguishing chat pages from
    /// settings/redirect pages.</summary>
    public string SourceUrl { get; init; } = "";
}

/// <summary>
/// User-clicked capture in the per-panel mini-toolbar. <c>Content</c> is
/// already extracted by the JS bridge (plain text for Text, markdown-ish
/// table flattening for Table, JSON array string for Citation).
/// </summary>
public sealed record CouncilCaptureMessage : CouncilBridgeMessage
{
    public CouncilCaptureType CaptureType { get; init; }
    public string             Content     { get; init; } = "";
}

/// <summary>
/// MutationObserver in the bridge detected the latest assistant response
/// stopped changing for ~1.5 s — likely stream-complete. The host can mark
/// the panel ready for synthesis or refresh a "captured" badge.
/// </summary>
public sealed record CouncilReplyDetectedMessage : CouncilBridgeMessage
{
    public string Text { get; init; } = "";
}

/// <summary>
/// Diagnostic emitted by the bridge when an internal step failed (adapter
/// JSON parse error, selector returns null unexpectedly, etc).
/// </summary>
public sealed record CouncilBridgeErrorMessage : CouncilBridgeMessage
{
    public string ErrorText { get; init; } = "";
}

/// <summary>
/// Parses the flat <c>chrome.webview.postMessage</c> payload emitted by
/// <c>council-bridge.js</c> into a typed <see cref="CouncilBridgeMessage"/>.
/// Returns null when the payload doesn't look like a Council message (so the
/// caller's <c>WebMessageReceived</c> handler can hand it off to the next
/// branch in its switch).
/// </summary>
public static class CouncilBridgeParser
{
    /// <summary>The discriminator prefix every Council bridge payload uses.
    /// Letting the caller fast-fail on non-Council messages avoids paying the
    /// JSON parse cost on every WebMessageReceived event from the page.</summary>
    public const string TypePrefix = "council/";

    /// <summary>Attempt to parse <paramref name="json"/> as a Council bridge
    /// message. <paramref name="provider"/> is supplied by the host because
    /// it owns the mapping <c>tab → provider</c>.</summary>
    public static CouncilBridgeMessage? Parse(string json, CouncilProvider provider)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("type", out var typeEl) ||
                typeEl.ValueKind != JsonValueKind.String)
                return null;

            var type = typeEl.GetString() ?? "";
            if (!type.StartsWith(TypePrefix, StringComparison.Ordinal)) return null;

            var sourceUrl = ReadStringOrEmpty(root, "sourceUrl");

            return type switch
            {
                "council/capture"        => ParseCapture(root, provider, sourceUrl),
                "council/replyDetected"  => new CouncilReplyDetectedMessage
                {
                    Provider  = provider,
                    SourceUrl = sourceUrl,
                    Text      = ReadStringOrEmpty(root, "text"),
                },
                "council/error"          => new CouncilBridgeErrorMessage
                {
                    Provider  = provider,
                    SourceUrl = sourceUrl,
                    ErrorText = ReadStringOrEmpty(root, "message"),
                },
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CouncilCaptureMessage? ParseCapture(
        JsonElement root, CouncilProvider provider, string sourceUrl)
    {
        var captureTypeRaw = ReadStringOrEmpty(root, "captureType");
        var captureType = captureTypeRaw.ToLowerInvariant() switch
        {
            "text"     => CouncilCaptureType.Text,
            "code"     => CouncilCaptureType.Code,
            "table"    => CouncilCaptureType.Table,
            "citation" => CouncilCaptureType.Citation,
            _          => (CouncilCaptureType?)null,
        };
        if (captureType is null) return null;

        return new CouncilCaptureMessage
        {
            Provider    = provider,
            SourceUrl   = sourceUrl,
            CaptureType = captureType.Value,
            Content     = ReadStringOrEmpty(root, "content"),
        };
    }

    private static string ReadStringOrEmpty(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        return "";
    }
}
