namespace VELO.Security.AI.Models;

public class SecurityVerdict
{
    public VerdictType Verdict { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = "";
    public ThreatType ThreatType { get; set; }
    public string Source { get; set; } = "";   // BLOCKLIST, TLS, HEURISTIC, AI_CLAUDE, AI_OFFLINE
    public bool NeedsAIAnalysis { get; set; }

    public static SecurityVerdict Allow() => new()
    {
        Verdict = VerdictType.Safe,
        Confidence = 100,
        Reason = "",
        Source = "ALLOW"
    };

    public static SecurityVerdict Block(string reason, ThreatType threat, string source = "HEURISTIC") => new()
    {
        Verdict = VerdictType.Block,
        Confidence = 95,
        Reason = reason,
        ThreatType = threat,
        Source = source
    };

    public static SecurityVerdict Warn(string reason, ThreatType threat, string source = "HEURISTIC") => new()
    {
        Verdict = VerdictType.Warn,
        Confidence = 70,
        Reason = reason,
        ThreatType = threat,
        Source = source
    };

    public static SecurityVerdict NeedsAI() => new()
    {
        NeedsAIAnalysis = true,
        Verdict = VerdictType.Safe
    };
}

public class AIVerdict
{
    public VerdictType Verdict { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = "";
    public ThreatType ThreatType { get; set; }
    public string Source { get; set; } = "";
    public bool IsFallback { get; set; }

    public static AIVerdict Safe() => new() { Verdict = VerdictType.Safe, Confidence = 80, Source = "AI" };

    public static AIVerdict Block(string reason, string source = "AI") => new()
    {
        Verdict = VerdictType.Block,
        Confidence = 85,
        Reason = reason,
        Source = source
    };

    public static AIVerdict Warn(string reason, string source = "AI") => new()
    {
        Verdict = VerdictType.Warn,
        Confidence = 70,
        Reason = reason,
        Source = source
    };

    public static AIVerdict Fallback(string reason) => new()
    {
        Verdict = VerdictType.Safe,
        Reason = reason,
        IsFallback = true,
        Source = "FALLBACK"
    };
}
