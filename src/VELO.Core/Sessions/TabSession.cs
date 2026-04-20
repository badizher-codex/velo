namespace VELO.Core.Sessions;

/// <summary>
/// Tracks per-tab privacy stats for the current navigation session.
/// Reset on each new navigation (NavigationCompleted).
/// </summary>
public class TabSession
{
    public string TabId           { get; init; } = "";
    public string Domain          { get; set; }  = "";
    public string Url             { get; set; }  = "";

    public int TrackersBlocked    { get; set; }
    public int AdsBlocked         { get; set; }
    public int FingerprintBlocked { get; set; }
    public int RequestsTotal      { get; set; }
    public int RequestsBlocked    { get; set; }

    public bool IsGoldenList      { get; set; }
    public int  ShieldScore       { get; set; }

    public DateTime SessionStart  { get; init; } = DateTime.UtcNow;

    public int DurationSeconds => (int)(DateTime.UtcNow - SessionStart).TotalSeconds;
}
