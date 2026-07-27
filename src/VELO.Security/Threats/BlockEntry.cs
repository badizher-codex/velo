using System.ComponentModel;
using System.Runtime.CompilerServices;
using VELO.Core.Localization;

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

    /// <summary>
    /// v2.4.63 — Generic, always-visible reason, localised to the UI language.
    /// The panel used to show only "blocked" plus three buttons: the *why* was
    /// behind the Explain button, which calls the local model — so with no model
    /// running the user never got an answer at all. This needs no AI: it states
    /// what this category of request typically does, and where the verdict came
    /// from. Explain still exists for a domain-specific answer.
    /// </summary>
    public string WhyBlocked
    {
        get
        {
            var L      = LocalizationService.Current;
            var why    = L.T(WhyKey(Kind));
            var source = L.T(SourceKey(Source));
            return $"{why} {string.Format(L.T("threatspanel.why.detected"), source)}";
        }
    }

    public static string WhyKey(BlockKind kind) => kind switch
    {
        BlockKind.Tracker     => "threatspanel.why.tracker",
        BlockKind.Malware     => "threatspanel.why.malware",
        BlockKind.Ads         => "threatspanel.why.ads",
        BlockKind.Fingerprint => "threatspanel.why.fingerprint",
        BlockKind.Script      => "threatspanel.why.script",
        BlockKind.Social      => "threatspanel.why.social",
        _                     => "threatspanel.why.other",
    };

    public static string SourceKey(BlockSource source) => source switch
    {
        BlockSource.GoldenList    => "threatspanel.source.goldenlist",
        BlockSource.Malwaredex    => "threatspanel.source.malwaredex",
        BlockSource.AIEngine      => "threatspanel.source.aiengine",
        BlockSource.UserRule      => "threatspanel.source.userrule",
        BlockSource.StaticList    => "threatspanel.source.staticlist",
        BlockSource.DownloadGuard => "threatspanel.source.downloadguard",
        _                         => "threatspanel.source.requestguard",
    };

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
