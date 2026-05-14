using SQLite;

namespace VELO.Data.Models;

public class AppSettings
{
    [PrimaryKey]
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public static class SettingKeys
{
    public const string SecurityMode            = "security.mode";
    public const string FingerprintLevel        = "privacy.fingerprint_level";
    public const string WebRtcMode              = "privacy.webrtc_mode";
    public const string ClearOnExit             = "privacy.clear_on_exit";
    public const string HistoryEnabled          = "privacy.history_enabled";
    public const string ConsentInjector         = "cookiewall.consent_injector";
    public const string DomExtractor            = "cookiewall.dom_extractor";
    public const string CacheFetcher            = "cookiewall.cache_fetcher";
    public const string AiMode                  = "ai.mode";
    public const string AiApiKey               = "ai.api_key";
    public const string AiCustomEndpoint        = "ai.custom_endpoint";
    public const string AiClaudeModel           = "ai.claude_model";
    public const string DnsProvider             = "dns.provider";
    public const string DnsCustomUrl            = "dns.custom_url";
    public const string DnsNextDnsConfigId      = "dns.nextdns_config_id";
    public const string SearchEngine            = "search.engine";
    public const string SearchCustomUrl         = "search.custom_url";
    public const string VaultAutoLockMinutes    = "vault.auto_lock_minutes";
    public const string VaultMasterPasswordHash = "vault.master_password_hash";
    public const string VaultSalt              = "vault.salt";
    public const string UpdateLastCheck         = "update.last_check";
    public const string BlocklistsLastUpdate    = "update.blocklists_last_update";
    public const string OnboardingCompleted     = "onboarding.completed";
    public const string Language               = "ui.language";
    /// <summary>v2.0.5.12 — Comma-separated list of hosts the user has whitelisted via SecurityPanel.</summary>
    public const string SecurityWhitelist       = "security.whitelist_hosts";
    /// <summary>v2.1.2 — When true, restore the previous session on every clean launch without prompting.
    /// Default false: ask the user the first time they encounter a clean snapshot.</summary>
    public const string SessionRestoreAlways    = "session.restore_always";
    /// <summary>v2.1.2 — Set to "yes" once the user has answered the first restore prompt so
    /// we don't keep asking on every launch.</summary>
    public const string SessionRestoreAsked     = "session.restore_asked";
    /// <summary>v2.4.18 — When true, BookmarkAIService generates tags on save. Default true.</summary>
    public const string BookmarkAutoTag         = "ai.bookmark_autotag";
    /// <summary>v2.4.23 — When true, the clipboard polling history captures recent copies. Default false (privacy).</summary>
    public const string ClipboardHistoryEnabled = "clipboard.history_enabled";
    /// <summary>v2.4.25 — When true, PhishingShield queries IANA RDAP for domain age on suspicious pages. Default false (privacy: leaks the suspect domain to its RDAP server).</summary>
    public const string PhishingShieldDomainAgeCheck = "phishing.domain_age_check";

    // ── Council Mode (Phase 4.0) ─────────────────────────────────────────
    /// <summary>Phase 4.0 — set to "yes" once the user has accepted the first-run disclaimer
    /// (acknowledges that the master prompt is sent to each enabled provider).</summary>
    public const string CouncilDisclaimerAccepted = "council.disclaimer_accepted";
    /// <summary>Phase 4.0 — per-provider opt-in. Each defaults true once disclaimer is accepted;
    /// user can disable a provider from Settings → Council to skip its panel.</summary>
    public const string CouncilEnabledClaude      = "council.enabled.claude";
    public const string CouncilEnabledChatGpt     = "council.enabled.chatgpt";
    public const string CouncilEnabledGrok        = "council.enabled.grok";
    public const string CouncilEnabledOllama      = "council.enabled.ollama";
    /// <summary>v2.4.39 — Council-specific synthesis endpoint. Default: http://localhost:11434
    /// (Ollama canonical). Independent from <see cref="AiCustomEndpoint"/> (Custom AI Mode).
    /// Renamed semantically in v2.4.40 to mean "the local LLM server endpoint Council probes",
    /// regardless of backend type — the key kept its name to avoid wiping users' v2.4.39
    /// configuration on upgrade.</summary>
    public const string CouncilOllamaEndpoint     = "council.ollama_endpoint";
    /// <summary>v2.4.40 — Which local LLM server flavour Council should talk to. One of:
    /// "Ollama" (uses /api/tags + /api/show), "LMStudio" (uses /v1/models, OpenAI-compatible),
    /// "OpenAICompat" (generic /v1/models, same wire format as LMStudio). Defaults to "Ollama"
    /// so users who upgrade from v2.4.38/4.39 see the same probe behaviour as before.</summary>
    public const string CouncilBackendType        = "council.backend_type";
    /// <summary>v2.4.40 — Moderator model name. Previously hard-coded to "qwen3:32b" in
    /// <c>CouncilPreflightService.RequiredModel</c>. Surfaced as a setting so users running
    /// LM Studio (e.g. qwen3.6-35b-a3b) or any non-default Ollama tag don't have to install
    /// the exact spec model. Default: "qwen3:32b" to keep parity with the Phase 4 spec.</summary>
    public const string CouncilModeratorModel     = "council.moderator_model";
}
