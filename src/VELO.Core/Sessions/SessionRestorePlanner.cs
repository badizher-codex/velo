namespace VELO.Core.Sessions;

/// <summary>
/// What a session restore should open, decided before anything touches the UI.
///
/// The decision used to live inline in <c>MainWindow.RestoreSnapshot</c>, mixed
/// with WPF calls, which made it untestable — and it was wrong in four separate
/// ways for years without anything noticing. Pure input → output here so
/// <c>SessionRestorePlannerTests</c> can pin each rule down.
/// </summary>
public sealed record SessionRestorePlan
{
    /// <summary>Tabs to open, in the order they appeared in the snapshot.</summary>
    public IReadOnlyList<TabSnapshot> Tabs { get; init; } = [];

    /// <summary>Index into <see cref="Tabs"/> of the tab that was active when
    /// the snapshot was taken, or -1 when it can't be identified.</summary>
    public int ActiveIndex { get; init; } = -1;

    /// <summary>A URL the process was launched with that isn't the default new
    /// tab — someone clicked a link in another app. Opened after the restored
    /// tabs so it ends up focused. Null when VELO was started normally.</summary>
    public string? LaunchUrl { get; init; }

    /// <summary>How many tabs the cap discarded. Non-zero is worth logging.</summary>
    public int DroppedByCap { get; init; }
}

public static class SessionRestorePlanner
{
    /// <summary>
    /// v2.1.4 — Maximum tabs restored eagerly. Above this the oldest are
    /// dropped. Proper lazy hydration with placeholder rows (spec § 6.4) is
    /// still parked; this cap keeps RAM sane in the meantime.
    /// </summary>
    public const int MaxTabs = 30;

    public const string NewTabUrl = "velo://newtab";

    /// <param name="snapshot">Snapshot to restore. Only the first window is
    /// considered — tear-off windows are session-only by design.</param>
    /// <param name="launchUrl">The URL the process was started with, or
    /// <see cref="NewTabUrl"/> when it was started normally.</param>
    public static SessionRestorePlan Plan(
        SessionSnapshot? snapshot,
        string launchUrl = NewTabUrl,
        int maxTabs = MaxTabs)
    {
        var extra = string.IsNullOrWhiteSpace(launchUrl) || launchUrl == NewTabUrl
            ? null
            : launchUrl;

        if (snapshot is null || snapshot.Windows.Count == 0)
            return new SessionRestorePlan { LaunchUrl = extra };

        var window = snapshot.Windows[0];
        var all    = window.Tabs;

        var kept    = all;
        var dropped = 0;
        if (all.Count > maxTabs)
        {
            // Keep the most recently used, but put them back in the user's
            // original order afterwards: selecting by recency and opening in
            // recency order would silently reshuffle the tab strip.
            var keepIds = all
                .OrderByDescending(t => t.LastActiveAtUtc)
                .Take(maxTabs)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.Ordinal);

            kept    = all.Where(t => keepIds.Contains(t.Id)).ToList();
            dropped = all.Count - kept.Count;
        }

        // Snapshot ids are regenerated on restore (each TabInfo gets a fresh
        // one), so the caller has to map this back by position, not by id.
        var activeIndex = string.IsNullOrEmpty(window.ActiveTabId)
            ? -1
            : IndexOfId(kept, window.ActiveTabId);

        return new SessionRestorePlan
        {
            Tabs         = kept,
            ActiveIndex  = activeIndex,
            LaunchUrl    = extra,
            DroppedByCap = dropped,
        };
    }

    private static int IndexOfId(IReadOnlyList<TabSnapshot> tabs, string id)
    {
        for (var i = 0; i < tabs.Count; i++)
            if (string.Equals(tabs[i].Id, id, StringComparison.Ordinal))
                return i;
        return -1;
    }
}
