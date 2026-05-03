using VELO.Core.Events;
using VELO.Security.Threats;
using Xunit;

namespace VELO.Security.Tests;

public class ThreatsPanelViewModelTests
{
    private static ThreatsPanelViewModel NewVm(EventBus bus, TimeSpan? debounce = null)
        => new(bus, logger: null, debounce: debounce ?? TimeSpan.Zero);

    private static BlockedRequestEvent Block(
        string tabId,
        string host,
        bool malwaredex = false,
        string kind = "Tracker",
        DateTime? at = null) =>
        new(tabId, host, $"https://{host}/some/path", kind, "cross-site",
            "GoldenList", malwaredex, 95, at ?? DateTime.UtcNow);

    [Fact]
    public void GroupByHost_SortsMalwaredexHitsFirst()
    {
        var bus = new EventBus();
        var vm  = NewVm(bus);
        vm.CurrentTabId = "T1";

        // Two non-Malwaredex hosts with high counts, one Malwaredex host with one hit.
        for (int i = 0; i < 5; i++) bus.Publish(Block("T1", "doubleclick.net"));
        for (int i = 0; i < 3; i++) bus.Publish(Block("T1", "facebook.com"));
        bus.Publish(Block("T1", "evil.example", malwaredex: true, kind: "Malware"));

        vm.RecomputeNow();

        Assert.True(vm.Groups.Count >= 1);
        Assert.Equal("evil.example", vm.Groups[0].Host);
        Assert.True(vm.Groups[0].IsMalwaredexHit);
    }

    [Fact]
    public void GroupByHost_SortsByCountDescending_WithinSameClass()
    {
        var bus = new EventBus();
        var vm  = NewVm(bus);
        vm.CurrentTabId = "T1";

        for (int i = 0; i < 4; i++) bus.Publish(Block("T1", "small.example"));
        for (int i = 0; i < 9; i++) bus.Publish(Block("T1", "big.example"));
        for (int i = 0; i < 6; i++) bus.Publish(Block("T1", "mid.example"));

        vm.RecomputeNow();

        var hosts = vm.Groups.Select(g => g.Host).ToList();
        Assert.Equal(new[] { "big.example", "mid.example", "small.example" }, hosts);
    }

    [Fact]
    public async Task Debounce_BatchesMultipleVerdicts_IntoSingleUIUpdate()
    {
        var bus = new EventBus();
        var vm  = NewVm(bus, debounce: TimeSpan.FromMilliseconds(80));
        vm.CurrentTabId = "T1";

        var uiUpdates = 0;
        vm.InvokeOnUi = action => { uiUpdates++; action(); };

        // 12 events fired faster than the debounce window — should collapse
        // to a single UI recompute.
        for (int i = 0; i < 12; i++) bus.Publish(Block("T1", $"host{i}.example"));

        await Task.Delay(250);

        Assert.Equal(1, uiUpdates);
        Assert.Equal(12, vm.Groups.Count);
    }

    [Fact]
    public void TabChange_ClearsBlocksForOldTabId()
    {
        var bus = new EventBus();
        var vm  = NewVm(bus);
        vm.CurrentTabId = "T1";

        bus.Publish(Block("T1", "a.example"));
        bus.Publish(Block("T1", "b.example"));
        vm.RecomputeNow();
        Assert.Equal(2, vm.Groups.Count);

        // Switching to T2 must hide T1's data — but NOT delete it (closing the
        // tab does that). Coming back to T1 must restore the same groups.
        vm.CurrentTabId = "T2";
        vm.RecomputeNow();
        Assert.Empty(vm.Groups);

        vm.CurrentTabId = "T1";
        vm.RecomputeNow();
        Assert.Equal(2, vm.Groups.Count);

        // Closing T1 truly drops the data.
        bus.Publish(new TabClosedEvent("T1"));
        vm.RecomputeNow();
        Assert.Empty(vm.Groups);
    }
}
