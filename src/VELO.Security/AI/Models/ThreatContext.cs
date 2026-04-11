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
}

public class TLSInfo
{
    public string Protocol { get; set; } = "";
    public bool IsSelfSigned { get; set; }
    public bool IsInCtLogs { get; set; }
}
