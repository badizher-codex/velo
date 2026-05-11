namespace VELO.Security.AI.Models;

public class ThreatContext
{
    public string Domain { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string Referrer { get; set; } = "";
    public string? ScriptHash { get; set; }

    // First 500 chars only, never full script
    public string? ScriptSnippet { get; set; }

    public List<string> DetectedPatterns { get; set; } = [];
    public int RiskScore { get; set; }
    public TLSInfo? TLSInfo { get; set; }
    public Dictionary<string, string> ResponseHeaders { get; set; } = [];

    /// <summary>
    /// v2.4.24 — True when the originating page contains a password input
    /// (detected by autofill.js). Surfaced to PhishingShield so the model
    /// can use "this page is asking for credentials" as a risk amplifier.
    /// Defaults to false so callers that don't set it preserve old behavior.
    /// </summary>
    public bool HasLoginForm { get; set; }

    /// <summary>
    /// v2.4.26 — The current page's &lt;title&gt; as last reported by
    /// WebView2.DocumentTitleChanged. Surfaced to PhishingShield so the
    /// model can spot brand-named titles served from non-brand hosts
    /// ("PayPal — Sign in" on paypal-secure.xyz). Empty when the page
    /// has not produced a title yet or when the caller has not wired
    /// the field.
    /// </summary>
    public string PageTitle { get; set; } = "";
}

public class TLSInfo
{
    public string Protocol { get; set; } = "";
    public bool IsSelfSigned { get; set; }
    public bool IsInCtLogs { get; set; }
}
