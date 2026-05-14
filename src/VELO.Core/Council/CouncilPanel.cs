namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk A — runtime state for one of the four panels in a Council
/// session. Owns the WebView2 tab association (by <see cref="TabId"/>), the
/// captures that originated from this panel, and an availability flag.
///
/// Mutable on purpose: the orchestrator (chunk B) updates captures and reply
/// text as the user clicks capture buttons and the panel finishes streaming.
/// Concurrent access is single-threaded via the WPF dispatcher — Council
/// activates only on the UI thread.
/// </summary>
public sealed class CouncilPanel
{
    /// <summary>The provider this panel represents.</summary>
    public CouncilProvider Provider { get; }

    /// <summary>VELO container ID for cookie/storage isolation.</summary>
    public string ContainerId { get; }

    /// <summary>The BrowserTab ID hosting this panel's webview. Empty until the
    /// orchestrator binds the tab in <c>ActivateAsync</c>.</summary>
    public string TabId { get; internal set; } = "";

    /// <summary>Current URL the panel's webview is on. Updated by the bridge
    /// (chunk E) on navigation events.</summary>
    public string CurrentUrl { get; internal set; }

    /// <summary>True when the panel is opted-in (user enabled this provider) and
    /// the panel webview is alive. False when the user disabled the provider
    /// in Settings or the panel hit an unrecoverable error.</summary>
    public bool IsAvailable { get; internal set; }

    /// <summary>Latest captured response text. Empty before any capture happens.</summary>
    public string LatestReply { get; internal set; } = "";

    /// <summary>All captures originated from this panel in chronological order.</summary>
    public IReadOnlyList<CouncilCapture> Captures => _captures;
    private readonly List<CouncilCapture> _captures = new();

    public CouncilPanel(CouncilProvider provider, bool isAvailable = false)
    {
        Provider     = provider;
        ContainerId  = CouncilProviderMap.ToContainerId(provider);
        CurrentUrl   = CouncilProviderMap.DefaultHomeUrl(provider);
        IsAvailable  = isAvailable;
    }

    /// <summary>Adds a capture to this panel's history. Returns the same capture for chaining.</summary>
    internal CouncilCapture AddCapture(CouncilCapture capture)
    {
        if (capture.PanelProvider != Provider)
            throw new ArgumentException(
                $"Capture provider {capture.PanelProvider} does not match panel provider {Provider}",
                nameof(capture));
        _captures.Add(capture);
        return capture;
    }

    /// <summary>Removes a capture by ID. Returns true if found and removed.</summary>
    internal bool RemoveCapture(string captureId)
    {
        var idx = _captures.FindIndex(c => c.Id == captureId);
        if (idx < 0) return false;
        _captures.RemoveAt(idx);
        return true;
    }

    /// <summary>Clears all captures (e.g. when starting a new turn).</summary>
    internal void ClearCaptures() => _captures.Clear();
}
