namespace VELO.Agent;

/// <summary>
/// Phase 3 / Sprint 6 — Per-tab agent state. Tracks whether the user has
/// primed the chat with the current page's content (via "Ask about this
/// page"), the cached page text used to build the priming system prompt,
/// and visual separators inserted on tab switch instead of a hard reset.
///
/// This class is pure (no WPF, no I/O). Tests live in
/// <c>PageContextManagerTests</c>.
/// </summary>
public sealed class PageContextManager
{
    private sealed class TabState
    {
        public bool   IsPrimed { get; set; }
        public string Url      { get; set; } = "";
        public string Title    { get; set; } = "";
        public string Content  { get; set; } = "";
    }

    private readonly Dictionary<string, TabState> _states = new();

    /// <summary>Maximum chars of page content kept per tab. Spec § 7.3 caps at ~4000 tokens ≈ 12kB.</summary>
    public int MaxContentChars { get; set; } = 12_000;

    /// <summary>
    /// Primes a tab with the current page's URL, title and extracted content.
    /// Idempotent — repeated calls overwrite the previous priming.
    /// </summary>
    public void Prime(string tabId, string url, string title, string content)
    {
        if (string.IsNullOrEmpty(tabId)) return;
        var clipped = content.Length > MaxContentChars
            ? content[..MaxContentChars]
            : content;
        _states[tabId] = new TabState
        {
            IsPrimed = true,
            Url      = url ?? "",
            Title    = title ?? "",
            Content  = clipped,
        };
    }

    /// <summary>Drops the priming state for a tab (e.g. tab closed).</summary>
    public void Forget(string tabId) => _states.Remove(tabId);

    /// <summary>True when the tab has been primed with page content.</summary>
    public bool IsPrimed(string tabId)
        => _states.TryGetValue(tabId, out var s) && s.IsPrimed;

    /// <summary>Page content cached for this tab, or empty when not primed.</summary>
    public string GetContent(string tabId)
        => _states.TryGetValue(tabId, out var s) ? s.Content : "";

    /// <summary>
    /// Builds the system-prompt prefix to inject on the FIRST message after
    /// priming. Returns empty string when the tab is not primed. The host
    /// is responsible for not resending it on subsequent turns within the
    /// same context — call <see cref="MarkSent"/> after first delivery.
    /// </summary>
    public string BuildSystemPrompt(string tabId)
    {
        if (!_states.TryGetValue(tabId, out var s) || !s.IsPrimed) return "";

        return
            "Eres VeloAgent. El usuario está viendo la siguiente página:\n" +
            $"\nURL: {s.Url}" +
            $"\nTítulo: {s.Title}" +
            "\nContenido extraído (Reader Mode):\n---\n" +
            s.Content +
            "\n---\n\n" +
            "Responde preguntas sobre este contenido. Si la pregunta requiere " +
            "información que no está en el contenido, di \"no lo puedo responder " +
            "solo con esta página\" y sugiere buscar. NUNCA inventes hechos sobre " +
            "la página.";
    }

    /// <summary>
    /// Marks the priming prompt as already delivered so subsequent turns in
    /// the same primed context don't repeat it. The priming itself stays
    /// active — only the system-prompt prefix is suppressed after the first
    /// message.
    /// </summary>
    public void MarkSent(string tabId)
    {
        if (_states.TryGetValue(tabId, out var s)) s.IsPrimed = false;
    }

    /// <summary>
    /// Visual separator inserted into the chat transcript when the active
    /// tab changes. Per spec § 7.3 the chat is NOT reset; the marker just
    /// signals to the user (and to the model when included in next turn)
    /// that the page context shifted.
    /// </summary>
    public static string BuildTabSwitchSeparator(string newUrl)
    {
        var safe = string.IsNullOrEmpty(newUrl) ? "(sin URL)" : newUrl;
        return $"── Contexto cambiado a {safe} ──";
    }
}
