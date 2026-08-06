using VELO.Core.Sessions;
using Xunit;

namespace VELO.Core.Tests.Sessions;

/// <summary>
/// Guards for the restore path, written after the maintainer reported that
/// "it says 3 tabs and opens 2" — a bug that had been shipping for a long time
/// because nothing anywhere compared what the snapshot promised against what
/// restore produced. The prompt and the log both reported the snapshot count,
/// so they agreed with each other and disagreed with reality.
/// </summary>
public class SessionRestorePlannerTests
{
    private static SessionSnapshot Snapshot(int tabs, string activeId = "", int maxAgeSpreadMinutes = 0)
    {
        var list = new List<TabSnapshot>();
        for (var i = 0; i < tabs; i++)
        {
            list.Add(new TabSnapshot
            {
                Id  = $"tab{i}",
                Url = $"https://example.com/{i}",
                // Later tabs look more recently used, so cap tests are decidable.
                LastActiveAtUtc = DateTime.UtcNow.AddMinutes(-(tabs - i) * maxAgeSpreadMinutes),
            });
        }
        return new SessionSnapshot
        {
            Windows = [new WindowSnapshot { Tabs = list, ActiveTabId = activeId }]
        };
    }

    // ── The reported bug ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(12)]
    public void Plan_Opens_Every_Tab_The_Snapshot_Promised(int count)
    {
        var snap = Snapshot(count);

        var plan = SessionRestorePlanner.Plan(snap);

        // The whole bug in one assertion: restore used to swallow the first
        // entry, so this was count - 1 for every count above 1. It passed at
        // count == 1, which is why it survived.
        Assert.Equal(snap.TotalTabs, plan.Tabs.Count);
        Assert.Equal(0, plan.DroppedByCap);
    }

    [Fact]
    public void Plan_Keeps_The_Snapshot_Order()
    {
        var plan = SessionRestorePlanner.Plan(Snapshot(4));

        Assert.Equal(
            ["https://example.com/0", "https://example.com/1", "https://example.com/2", "https://example.com/3"],
            plan.Tabs.Select(t => t.Url));
    }

    // ── Launch URL ───────────────────────────────────────────────────────

    [Fact]
    public void A_Launch_Url_Survives_A_Restore()
    {
        // Cold start as the default browser: VELO is closed, the user clicks a
        // link elsewhere. The session must come back AND the link must open.
        var plan = SessionRestorePlanner.Plan(Snapshot(3), "https://makerworld.com/en/models/42");

        Assert.Equal(3, plan.Tabs.Count);
        Assert.Equal("https://makerworld.com/en/models/42", plan.LaunchUrl);
    }

    [Theory]
    [InlineData("velo://newtab")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Normal_Start_Adds_No_Extra_Tab(string launchUrl)
    {
        var plan = SessionRestorePlanner.Plan(Snapshot(3), launchUrl);

        Assert.Equal(3, plan.Tabs.Count);
        Assert.Null(plan.LaunchUrl);
    }

    [Fact]
    public void A_Launch_Url_Survives_An_Empty_Snapshot()
    {
        var plan = SessionRestorePlanner.Plan(null, "https://makerworld.com/");

        Assert.Empty(plan.Tabs);
        Assert.Equal("https://makerworld.com/", plan.LaunchUrl);
    }

    // ── Active tab ───────────────────────────────────────────────────────

    [Fact]
    public void The_Tab_You_Were_On_Comes_Back_Focused()
    {
        var plan = SessionRestorePlanner.Plan(Snapshot(4, activeId: "tab2"));

        Assert.Equal(2, plan.ActiveIndex);
    }

    [Fact]
    public void An_Unknown_Active_Id_Is_Reported_As_None()
    {
        var plan = SessionRestorePlanner.Plan(Snapshot(4, activeId: "gone"));

        Assert.Equal(-1, plan.ActiveIndex);
    }

    // ── Cap ──────────────────────────────────────────────────────────────

    [Fact]
    public void Over_The_Cap_The_Least_Recent_Are_Dropped()
    {
        var snap = Snapshot(35, maxAgeSpreadMinutes: 1);

        var plan = SessionRestorePlanner.Plan(snap, maxTabs: 30);

        Assert.Equal(30, plan.Tabs.Count);
        Assert.Equal(5, plan.DroppedByCap);
        // tab0..tab4 are the oldest by construction.
        Assert.DoesNotContain(plan.Tabs, t => t.Id == "tab0");
        Assert.Contains(plan.Tabs, t => t.Id == "tab34");
    }

    [Fact]
    public void The_Cap_Does_Not_Reshuffle_The_Tab_Strip()
    {
        // Selecting by recency and opening in recency order would silently
        // reorder the sidebar, which is the kind of thing users notice and
        // can't explain.
        var plan = SessionRestorePlanner.Plan(Snapshot(35, maxAgeSpreadMinutes: 1), maxTabs: 30);

        var ordinals = plan.Tabs.Select(t => int.Parse(t.Id["tab".Length..])).ToList();
        Assert.Equal(ordinals.OrderBy(n => n), ordinals);
    }

    // ── Degenerate input ─────────────────────────────────────────────────

    [Fact]
    public void A_Null_Snapshot_Plans_Nothing()
    {
        var plan = SessionRestorePlanner.Plan(null);

        Assert.Empty(plan.Tabs);
        Assert.Null(plan.LaunchUrl);
        Assert.Equal(-1, plan.ActiveIndex);
    }

    [Fact]
    public void A_Snapshot_With_No_Windows_Plans_Nothing()
    {
        var plan = SessionRestorePlanner.Plan(new SessionSnapshot());

        Assert.Empty(plan.Tabs);
    }
}
