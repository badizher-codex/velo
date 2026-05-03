using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VELO.Security.Threats;

/// <summary>
/// Phase 3 / Sprint 1 — One blocked request as displayed in ThreatsPanelV2.
/// Equality is by Id so two events for the same URL still appear as
/// separate entries (we count duplicates inside the Group's Count).
///
/// Explanation is null until the user clicks "Explain"; setting it raises
/// PropertyChanged so the inline expand updates without a full rebuild.
/// </summary>
public class BlockEntry : INotifyPropertyChanged
{
    public string Id              { get; } = Guid.NewGuid().ToString("N");
    public string Host            { get; init; } = "";
    public string FullUrl         { get; init; } = "";
    public BlockKind Kind         { get; init; } = BlockKind.Other;
    public string SubKind         { get; init; } = "";
    public DateTime BlockedAtUtc  { get; init; } = DateTime.UtcNow;
    public BlockSource Source     { get; init; } = BlockSource.RequestGuard;
    public bool IsMalwaredexHit   { get; init; }
    public int Confidence         { get; init; }
    public string TabId           { get; init; } = "";

    private string? _explanation;
    /// <summary>Filled lazily by <see cref="BlockExplanationService"/>.</summary>
    public string? Explanation
    {
        get => _explanation;
        set
        {
            if (_explanation == value) return;
            _explanation = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Explanation)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasExplanation)));
        }
    }
    public bool HasExplanation => !string.IsNullOrEmpty(_explanation);

    public string ShortPath
    {
        get
        {
            try
            {
                var u = new Uri(FullUrl);
                var path = u.AbsolutePath.Length > 40
                    ? u.AbsolutePath[..40] + "…"
                    : u.AbsolutePath;
                return path + (string.IsNullOrEmpty(u.Query) ? "" : "?" + (u.Query.Length > 20 ? u.Query[..20] + "…" : u.Query));
            }
            catch
            {
                return FullUrl.Length > 60 ? FullUrl[..60] + "…" : FullUrl;
            }
        }
    }

    public string TimeLabel => BlockedAtUtc.ToLocalTime().ToString("HH:mm:ss");

    public event PropertyChangedEventHandler? PropertyChanged;
}
