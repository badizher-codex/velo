namespace VELO.Security.AI.Models;

public enum VerdictType
{
    Safe,
    Warn,
    Block
}

public enum ThreatType
{
    None,
    KnownTracker,
    Malware,
    Phishing,
    DataExfiltration,
    Miner,
    Fingerprinting,
    MitM,
    DnsRebinding,
    SSRF,
    MixedContent,
    ContainerViolation,
    Tracker,
    Other
}
