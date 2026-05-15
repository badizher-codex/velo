namespace VELO.Core.Navigation;

/// <summary>
/// v2.4.49 — Captured state of a tab at the moment it's about to be torn off
/// into a new window. Carries the data the host needs to make the post-tearoff
/// page feel like a continuation rather than a reload.
///
/// WebView2 (Microsoft.Web.WebView2.Wpf) does not support reparenting a control
/// between two WPF Window hosts without destroying its CoreWebView2 backing.
/// VELO's tear-off model (privacy-isolated services per window) forces the
/// new window to re-navigate from scratch. The HTTP cache + cookies +
/// localStorage are shared (same user-data folder under <c>Profile/</c>) so
/// the re-fetch is a cache hit, but the DOM re-renders from zero — scripts
/// re-run, scroll position is lost, in-flight WebSocket connections die.
///
/// This snapshot preserves what's cheaply recoverable via JavaScript:
/// <list type="bullet">
///   <item><see cref="ScrollX"/> + <see cref="ScrollY"/> — the page's scroll
///         position at the moment of tear-off, so a long article doesn't
///         jump back to the top after the move.</item>
/// </list>
///
/// Future fields (form values, selection, zoom factor) can land additively
/// without breaking the constructor surface. Kept as a record so callers
/// can construct it from JSON parse results without ceremony.
/// </summary>
/// <param name="ScrollX">Horizontal scroll offset in CSS pixels. Defaults to 0.</param>
/// <param name="ScrollY">Vertical scroll offset in CSS pixels. Defaults to 0.</param>
public sealed record TabSnapshot(double ScrollX = 0, double ScrollY = 0)
{
    /// <summary>Empty snapshot used when capture fails or when the caller has
    /// nothing to preserve. Restore on this is a no-op.</summary>
    public static readonly TabSnapshot Empty = new();

    /// <summary>True when the snapshot has any non-default content worth
    /// restoring. Lets the receiver skip the ExecuteScriptAsync round-trip
    /// when the snapshot is empty.</summary>
    public bool HasContent => ScrollX != 0 || ScrollY != 0;
}
