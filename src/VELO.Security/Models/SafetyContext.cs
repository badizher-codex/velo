using VELO.Security.AI.Models;

namespace VELO.Security.Models;

public class SafetyContext
{
    public required Uri                              Uri                { get; init; }
    public required IReadOnlyList<SecurityVerdict>  SessionVerdicts    { get; init; }
    public TLSStatus                                TLSStatus          { get; init; } = TLSStatus.Unknown;
    public AIVerdict?                               AIVerdict          { get; init; }
    public bool                                     IsWhitelistedByUser { get; init; }
    public bool                                     IsGoldenList       { get; init; }
    public int                                      TrackersBlockedCount { get; init; }
    public int                                      FingerprintAttemptsBlocked { get; init; }
}
