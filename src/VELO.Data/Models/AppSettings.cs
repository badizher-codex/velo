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
}
