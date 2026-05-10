using VELO.Core.Clipboard;
using Xunit;

namespace VELO.Core.Tests;

public class ClipboardHistoryTests
{
    [Fact]
    public void TryAdd_AcceptsNewText_RaisesEntryAdded()
    {
        var h = new ClipboardHistory();
        ClipboardHistory.Entry? raised = null;
        h.EntryAdded += e => raised = e;

        var ok = h.TryAdd("hello world");

        Assert.True(ok);
        Assert.Equal(1, h.Count);
        Assert.NotNull(raised);
        Assert.Equal("hello world", raised!.Text);
        Assert.False(raised.LooksLikePassword);
    }

    [Fact]
    public void TryAdd_RejectsEmptyAndWhitespace()
    {
        var h = new ClipboardHistory();
        Assert.False(h.TryAdd(""));
        Assert.False(h.TryAdd("   "));
        Assert.False(h.TryAdd("\t\n"));
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void TryAdd_DedupesConsecutiveIdenticalCaptures()
    {
        var h = new ClipboardHistory();
        Assert.True(h.TryAdd("same"));
        Assert.False(h.TryAdd("same")); // identical → rejected
        Assert.True(h.TryAdd("different"));
        Assert.False(h.TryAdd("different"));
        Assert.Equal(2, h.Count);
    }

    [Fact]
    public void TryAdd_AcceptsRepeatAfterIntervening()
    {
        var h = new ClipboardHistory();
        Assert.True(h.TryAdd("a"));
        Assert.True(h.TryAdd("b"));
        Assert.True(h.TryAdd("a")); // not consecutive — allowed back at top
        Assert.Equal(3, h.Count);
    }

    [Fact]
    public void TryAdd_EvictsOldestWhenAtCap()
    {
        var h = new ClipboardHistory { MaxEntries = 3 };
        h.TryAdd("oldest");
        h.TryAdd("middle");
        h.TryAdd("newest");
        h.TryAdd("overflow"); // pushes oldest out

        var all = h.GetAll().Select(e => e.Text).ToList();
        Assert.Equal(new[] { "overflow", "newest", "middle" }, all);
        Assert.Equal(3, h.Count);
    }

    [Fact]
    public void GetAll_ReturnsNewestFirst()
    {
        var h = new ClipboardHistory();
        h.TryAdd("one");
        h.TryAdd("two");
        h.TryAdd("three");

        var ordered = h.GetAll().Select(e => e.Text).ToList();
        Assert.Equal(new[] { "three", "two", "one" }, ordered);
    }

    [Fact]
    public void RemoveAt_RemovesByIndex()
    {
        var h = new ClipboardHistory();
        h.TryAdd("one");
        h.TryAdd("two");
        h.TryAdd("three");

        Assert.True(h.RemoveAt(1)); // removes "two"
        var ordered = h.GetAll().Select(e => e.Text).ToList();
        Assert.Equal(new[] { "three", "one" }, ordered);
    }

    [Fact]
    public void RemoveAt_OutOfRange_ReturnsFalse()
    {
        var h = new ClipboardHistory();
        h.TryAdd("only");
        Assert.False(h.RemoveAt(5));
        Assert.False(h.RemoveAt(-1));
        Assert.Equal(1, h.Count);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var h = new ClipboardHistory();
        h.TryAdd("a");
        h.TryAdd("b");
        h.Clear();
        Assert.Equal(0, h.Count);
        Assert.Empty(h.GetAll());
    }

    [Fact]
    public void LooksLikePassword_DetectsTypicalGenerated()
    {
        // Length 12-64, no whitespace, mix of ≥3 classes
        Assert.True(ClipboardHistory.LooksLikePassword("Abcd1234!xyz")); // upper+lower+digit+symbol
        Assert.True(ClipboardHistory.LooksLikePassword("Tr0ub4dor&3xyz")); // mixed
        Assert.True(ClipboardHistory.LooksLikePassword("Xkcd1234Abcd")); // upper+lower+digit (3 classes)
    }

    [Fact]
    public void LooksLikePassword_RejectsTooShort()
    {
        Assert.False(ClipboardHistory.LooksLikePassword("A1!a"));
        Assert.False(ClipboardHistory.LooksLikePassword("Ab12!"));
    }

    [Fact]
    public void LooksLikePassword_RejectsTooLong()
    {
        var huge = new string('A', 80) + "1!a";
        Assert.False(ClipboardHistory.LooksLikePassword(huge));
    }

    [Fact]
    public void LooksLikePassword_RejectsWhitespace()
    {
        Assert.False(ClipboardHistory.LooksLikePassword("Hello World 12!"));
    }

    [Fact]
    public void LooksLikePassword_RejectsSingleClass()
    {
        // All lowercase letters — only 1 class
        Assert.False(ClipboardHistory.LooksLikePassword("abcdefghijklm"));
        // All digits — only 1 class
        Assert.False(ClipboardHistory.LooksLikePassword("1234567890123"));
    }

    [Fact]
    public void LooksLikePassword_RejectsTwoClasses()
    {
        // Upper + lower, no digit / symbol — common prose
        Assert.False(ClipboardHistory.LooksLikePassword("HelloWorldCase"));
    }

    [Fact]
    public void TryAdd_TagsPasswordEntries()
    {
        var h = new ClipboardHistory();
        h.TryAdd("ordinary text here");
        h.TryAdd("Abcd1234!xyz");
        var entries = h.GetAll();
        Assert.False(entries[1].LooksLikePassword); // "ordinary text here"
        Assert.True(entries[0].LooksLikePassword);  // "Abcd1234!xyz"
    }

    [Fact]
    public void TryAdd_IsThreadSafe()
    {
        var h = new ClipboardHistory { MaxEntries = 200 };
        Parallel.For(0, 200, i =>
        {
            h.TryAdd($"entry-{i}");
        });
        Assert.True(h.Count > 0 && h.Count <= 200);
    }
}
