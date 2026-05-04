using System.Text.Json.Serialization;

namespace VELO.Core.Sessions;

/// <summary>
/// Phase 3 / Sprint 3 — Top-level snapshot persisted to disk so VELO can
/// restore the previous session at next launch (clean shutdown) or recover
/// from a crash (unclean shutdown).
///
/// Schema is versioned so future fields can be added without breaking the
/// loader; <see cref="SessionService"/> upgrades unknown versions to a
/// "no snapshot available" result rather than failing the whole boot.
/// </summary>
public sealed record SessionSnapshot
{
    /// <summary>Schema version. Bump when fields change shape.</summary>
    public int Version { get; init; } = 1;

    public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>True when the snapshot was written during a clean shutdown
    /// (Window_Closing). False during periodic 30-s heartbeats — used to
    /// detect crash recovery on next launch.</summary>
    public bool WasCleanShutdown { get; init; }

    public List<WindowSnapshot> Windows { get; init; } = [];

    /// <summary>Total number of safe tabs across all windows. Cached so the
    /// restore prompt can show the count without rehydrating everything.</summary>
    [JsonIgnore]
    public int TotalTabs => Windows.Sum(w => w.Tabs.Count);
}

public sealed record WindowSnapshot
{
    public double Left   { get; init; }
    public double Top    { get; init; }
    public double Width  { get; init; }
    public double Height { get; init; }
    public bool   IsMaximised { get; init; }

    public string ActiveTabId { get; init; } = "";
    public List<TabSnapshot> Tabs { get; init; } = [];
    public List<WorkspaceSnapshot> Workspaces { get; init; } = [];
}

public sealed record TabSnapshot
{
    public string Id           { get; init; } = "";
    public string Url          { get; init; } = "";
    public string Title        { get; init; } = "";
    public string ContainerId  { get; init; } = "none";
    public string WorkspaceId  { get; init; } = "default";
    public int    ScrollY      { get; init; }
    public DateTime LastActiveAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed record WorkspaceSnapshot
{
    public string Id   { get; init; } = "";
    public string Name { get; init; } = "";
    public string ColorHex { get; init; } = "#808080";
}
