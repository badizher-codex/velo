namespace VELO.Core.AI;

/// <summary>
/// Phase 3 / Sprint 10 — Single point of coordination for the
/// <c>ChatDelegate</c> that VELO's AI services need from the host. Sprints
/// 1, 5, 6, 8 and 9 each added a new AI-capable service (AIContextActions,
/// BlockExplanationService, SmartBlockClassifier, PhishingShield,
/// CodeActions, BookmarkAIService, …) and each one ended up with its own
/// 2-3-line wiring block in MainWindow. That block was a magnet for
/// add-a-service-forget-to-wire bugs.
///
/// This router lets MainWindow declare the wiring once at startup with
/// a fluent <see cref="Register"/> chain, then call <see cref="WireAll"/>
/// to push the host's chat delegate to every registered consumer. Adding
/// a new AI service in a future sprint is one extra <c>.Register(...)</c>
/// call instead of finding the right corner of MainWindow.
///
/// Pure (no DI, no I/O). Tests live in <c>AiChatRouterTests</c>.
/// </summary>
public sealed class AiChatRouter
{
    /// <summary>The host-supplied chat delegate. Same shape as every consumer expects.</summary>
    public delegate Task<string> ChatDelegate(string systemPrompt, string userPrompt, CancellationToken ct);

    /// <summary>Setter callback that knows how to install the delegate on a single consumer.</summary>
    public delegate void Setter(ChatDelegate chat);

    private readonly List<Setter> _setters = new();

    /// <summary>The most recently wired delegate, or null if <see cref="WireAll"/> hasn't run yet.</summary>
    public ChatDelegate? Current { get; private set; }

    /// <summary>Number of registered consumers.</summary>
    public int Count => _setters.Count;

    /// <summary>
    /// Adds a consumer's setter to the registry. Typically the caller
    /// passes a lambda like <c>d =&gt; svc.ChatDelegate = d</c>. Returns
    /// <c>this</c> for fluent chaining.
    /// </summary>
    public AiChatRouter Register(Setter setter)
    {
        ArgumentNullException.ThrowIfNull(setter);
        _setters.Add(setter);
        return this;
    }

    /// <summary>
    /// Pushes <paramref name="chat"/> to every registered consumer.
    /// Idempotent — calling it again replaces the delegate everywhere
    /// (used when the user switches AI adapter at runtime). A failing
    /// setter does not abort the rest; the exception is swallowed and
    /// reported via <paramref name="onError"/> when supplied.
    /// </summary>
    public void WireAll(ChatDelegate chat, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(chat);
        Current = chat;
        foreach (var setter in _setters)
        {
            try { setter(chat); }
            catch (Exception ex) { onError?.Invoke(ex); }
        }
    }

    /// <summary>
    /// Re-pushes the last-known delegate to every registered consumer.
    /// Useful when a setter fails the first time (e.g. service not yet
    /// resolved) and the caller wants to retry without reconstructing
    /// the lambda. No-op when <see cref="WireAll"/> hasn't run yet.
    /// </summary>
    public void RewireExisting(Action<Exception>? onError = null)
    {
        if (Current is null) return;
        WireAll(Current, onError);
    }
}
