using VELO.Core.Navigation;
using Xunit;

namespace VELO.Core.Tests.Navigation;

/// <summary>
/// v2.4.49 — coverage for the tear-off scroll-restore snapshot. Tiny surface;
/// tests pin the contract the host depends on: Empty is a true no-op marker,
/// HasContent gates the round-trip, equality is value-based.
/// </summary>
public class TabSnapshotTests
{
    [Fact]
    public void Empty_hasZeroScrollAndNoContent()
    {
        var s = TabSnapshot.Empty;
        Assert.Equal(0, s.ScrollX);
        Assert.Equal(0, s.ScrollY);
        Assert.False(s.HasContent);
    }

    [Fact]
    public void DefaultConstructor_matchesEmpty()
    {
        // Records get a default ctor for free; the host uses it as the
        // fallback when JSON parsing fails. Should match Empty exactly.
        var s = new TabSnapshot();
        Assert.Equal(TabSnapshot.Empty, s);
        Assert.False(s.HasContent);
    }

    [Fact]
    public void HasContent_isTrue_whenAnyAxisIsNonZero()
    {
        Assert.True(new TabSnapshot(ScrollX: 100, ScrollY: 0).HasContent);
        Assert.True(new TabSnapshot(ScrollX: 0,   ScrollY: 250).HasContent);
        Assert.True(new TabSnapshot(ScrollX: 42,  ScrollY: 1024).HasContent);
    }

    [Fact]
    public void HasContent_isFalse_whenBothAxesAreZero()
    {
        Assert.False(new TabSnapshot(ScrollX: 0, ScrollY: 0).HasContent);
    }

    [Fact]
    public void RecordEquality_isValueBased()
    {
        var a = new TabSnapshot(ScrollX: 10, ScrollY: 200);
        var b = new TabSnapshot(ScrollX: 10, ScrollY: 200);
        var c = new TabSnapshot(ScrollX: 10, ScrollY: 201);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void NegativeScroll_isAllowed_notClamped()
    {
        // Some pages report negative scroll positions briefly during
        // overscroll / rubber-banding. The snapshot must round-trip
        // those values verbatim — the JS restore on the other end
        // accepts them and the browser clamps as needed.
        var s = new TabSnapshot(ScrollX: -5, ScrollY: -3);
        Assert.Equal(-5, s.ScrollX);
        Assert.Equal(-3, s.ScrollY);
        Assert.True(s.HasContent);
    }

    [Fact]
    public void FractionalScroll_isPreserved()
    {
        // DPI-scaled pages can produce sub-pixel scroll values. Don't
        // truncate — restore them as the browser handed them to us.
        var s = new TabSnapshot(ScrollX: 12.5, ScrollY: 408.75);
        Assert.Equal(12.5,   s.ScrollX);
        Assert.Equal(408.75, s.ScrollY);
    }
}
