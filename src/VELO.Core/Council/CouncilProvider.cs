namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk A — the four built-in Council provider slots. Each slot
/// maps 1:1 to a Council container ID (seeded by Phase 4.0 chunk E in
/// <c>VeloDatabase</c>) and to a default home URL.
///
/// The fourth slot is named <see cref="Local"/> rather than "Ollama" because
/// v2.4.40 made the local moderator backend configurable — LM Studio, Ollama
/// or any OpenAI-compatible server all map to this single slot. The container
/// ID stays <c>council-ollama</c> for back-compat with rows already seeded in
/// existing users' databases.
/// </summary>
public enum CouncilProvider
{
    Claude = 0,
    ChatGpt = 1,
    Grok = 2,
    Local = 3,
}

/// <summary>
/// Bidirectional mapping between <see cref="CouncilProvider"/> and the
/// canonical container ID, plus the home URL Council navigates each panel
/// to when a session starts.
///
/// The container IDs MUST match <c>CouncilContainerPolicy.CouncilContainerIds</c>
/// order so panel index ↔ provider ↔ container resolve consistently.
/// </summary>
public static class CouncilProviderMap
{
    /// <summary>Container ID for the provider's panel. Matches the seeded row.</summary>
    public static string ToContainerId(CouncilProvider provider) => provider switch
    {
        CouncilProvider.Claude  => "council-claude",
        CouncilProvider.ChatGpt => "council-chatgpt",
        CouncilProvider.Grok    => "council-grok",
        CouncilProvider.Local   => "council-ollama",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider"),
    };

    /// <summary>
    /// Default home URL Council navigates a panel to when a session starts. For
    /// the cloud providers this is the chat surface the adapter scrapes; for
    /// <see cref="CouncilProvider.Local"/> it's <c>about:blank</c> because the
    /// local moderator runs in-process via ChatDelegate, not as a webview.
    /// </summary>
    public static string DefaultHomeUrl(CouncilProvider provider) => provider switch
    {
        CouncilProvider.Claude  => "https://claude.ai/new",
        CouncilProvider.ChatGpt => "https://chat.openai.com/",
        CouncilProvider.Grok    => "https://grok.com/",
        CouncilProvider.Local   => "about:blank",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider"),
    };

    /// <summary>Returns the provider for a Council container ID, or null if it's not a Council slot.</summary>
    public static CouncilProvider? FromContainerId(string? containerId) => containerId switch
    {
        "council-claude"  => CouncilProvider.Claude,
        "council-chatgpt" => CouncilProvider.ChatGpt,
        "council-grok"    => CouncilProvider.Grok,
        "council-ollama"  => CouncilProvider.Local,
        _ => null,
    };

    /// <summary>The setting key that gates whether the user has opted into this provider.</summary>
    public static string EnabledSettingKey(CouncilProvider provider) => provider switch
    {
        CouncilProvider.Claude  => "council.enabled.claude",
        CouncilProvider.ChatGpt => "council.enabled.chatgpt",
        CouncilProvider.Grok    => "council.enabled.grok",
        CouncilProvider.Local   => "council.enabled.ollama",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider"),
    };

    /// <summary>All providers in panel-index order (0 = top-left through 3 = bottom-right).</summary>
    public static IReadOnlyList<CouncilProvider> All { get; } = new[]
    {
        CouncilProvider.Claude,
        CouncilProvider.ChatGpt,
        CouncilProvider.Grok,
        CouncilProvider.Local,
    };
}
