using System.Windows;
using VELO.Core.Localization;
using VELO.Data.Models;

namespace VELO.UI.Dialogs;

/// <summary>
/// v2.4.11 — Wrapper around <see cref="HistoryEntry"/> that pre-computes
/// every value the HistoryWindow row template needs to render. Created
/// fresh on every reload, so a language change followed by a reload picks
/// up the new strings.
///
/// Why a wrapper instead of binding through converters: the previous
/// HistoryWindow (Phase 2) bound badge text through a chain of static-source
/// bindings (<c>Source='history.badge.X'</c>) routed through
/// LocalizeKeyConverter. When that chain failed for any reason — missing
/// resource, converter exception, attribute typo — the row simply rendered
/// blank with no error surface. That contributed to the v2.4.x History
/// "0 entries" mystery (see project_phase3_state.md).
///
/// In this rewrite, every binding target is a plain CLR property on this
/// record. No converters in the template. No source-static bindings. If
/// the localisation key doesn't resolve, you see the literal key, which
/// is debuggable. If the property is null, you see empty, which is
/// debuggable. The DataTemplate becomes infallible by construction.
/// </summary>
public sealed record HistoryEntryRowView(
    int      Id,
    string   Url,
    string   Title,
    string   ContainerId,
    DateTime VisitedAt,
    int      BlockedCount,
    int      TrackerCount,
    int      MalwareCount,
    bool     MonsterCaptured)
{
    // ── Tracker / blocked / malware badges ───────────────────────────────

    public Visibility BlockedBadgeVisibility => BlockedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TrackerBadgeVisibility => TrackerCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MalwareBadgeVisibility => MalwareCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string BlockedBadgeText => $"{BlockedCount} {LocalizationService.Current.T("history.badge.blocked")}";
    public string TrackerBadgeText => $"{TrackerCount} {LocalizationService.Current.T("history.badge.trackers")}";
    public string MalwareBadgeText => $"{MalwareCount} {LocalizationService.Current.T("history.badge.malware")}";

    // ── 'No threats' badge — visible only when nothing was blocked ───────

    public Visibility NoThreatsVisibility =>
        (BlockedCount == 0 && TrackerCount == 0 && MalwareCount == 0)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string NoThreatsText => LocalizationService.Current.T("history.no_threats");

    // ── 👾 Monster-captured indicator (Phase 2 — Malwaredex) ─────────────

    public Visibility MonsterVisibility => MonsterCaptured ? Visibility.Visible : Visibility.Collapsed;

    // ── Factory ─────────────────────────────────────────────────────────

    public static HistoryEntryRowView From(HistoryEntry e) => new(
        Id:              e.Id,
        Url:             e.Url,
        Title:           string.IsNullOrEmpty(e.Title) ? e.Url : e.Title,
        ContainerId:     e.ContainerId ?? "",
        VisitedAt:       e.VisitedAt,
        BlockedCount:    e.BlockedCount,
        TrackerCount:    e.TrackerCount,
        MalwareCount:    e.MalwareCount,
        MonsterCaptured: e.MonsterCaptured);
}
