namespace VELO.Core.Navigation;

/// <summary>
/// v2.4.52 — Cross-window tab transfer payload. Serialised as JSON into a
/// WPF <see cref="System.Windows.DataObject"/> when the user drags a tab
/// out of one VELO window's sidebar; the target window deserialises it on
/// drop and re-creates the tab there (re-joining the workspace).
///
/// Designed as a value-record so cross-process OLE drag-drop marshals it
/// safely: the wire format is a string (JSON), every field is a primitive
/// or another record, no object identity matters across the process boundary.
///
/// The companion <see cref="TabSnapshot"/> (v2.4.49) is embedded so the
/// re-joined tab restores scroll position the same way a fresh tear-off
/// does. Future fields (form state, video time) land additively without
/// breaking the wire format.
/// </summary>
/// <param name="SourceSidebarId">
///   GUID assigned to each <c>TabSidebar</c> instance at construction. The
///   target compares it against its own <c>_sidebarId</c> on drop — when
///   they match (drop inside the same window), the drop is rejected so
///   v0.1 doesn't accidentally trigger a re-join on a local reorder gesture.
///   Equivalent of "is this drag from somebody else" without needing a
///   cross-process IPC channel.
/// </param>
/// <param name="TabId">Tab id from the source window — kept for diagnostics
/// only in v0.1. Future work could re-use this when pin-state migrates
/// across the transfer.</param>
/// <param name="Url">Navigation URL. The target window calls
/// <c>TabManager.CreateTab(Url, ContainerId)</c> with this verbatim.</param>
/// <param name="Title">Best-effort title the source last knew about so the
/// re-joined tab's TabInfo seed isn't "Nueva pestaña" while the WebView2
/// re-fetches and re-parses the page.</param>
/// <param name="ContainerId">Source container ("personal" / "work" / etc).
/// Preserved so the re-joined tab keeps its container assignment in the
/// target window's TabManager.</param>
/// <param name="Snapshot">Optional <see cref="TabSnapshot"/> with the
/// scroll position captured pre-drag. Null when the source couldn't
/// capture (page not loaded yet) — in that case the re-join lands at
/// the top of the page like any normal navigation.</param>
public sealed record TabTransferPayload(
    string        SourceSidebarId,
    string        TabId,
    string        Url,
    string        Title,
    string        ContainerId,
    TabSnapshot?  Snapshot)
{
    /// <summary>OLE DataObject format identifier used by both source and
    /// target sides of the drag. Choosing a VELO-prefixed format means our
    /// drag isn't accidentally consumed by other apps (Notepad accepting
    /// the Text fallback, for example).</summary>
    public const string DataFormat = "VELO.Tab.Transfer";
}
