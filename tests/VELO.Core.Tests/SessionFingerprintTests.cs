using VELO.Core.Sessions;
using Xunit;

namespace VELO.Core.Tests;

public class SessionFingerprintTests
{
    private static WindowSnapshot Window(
        double left = 100, double top = 50, double width = 1200, double height = 800,
        bool isMaximised = false, string activeTabId = "tab-1",
        params (string Id, string Url, string Title, string Container, string Workspace)[] tabs)
    {
        return new WindowSnapshot
        {
            Left        = left,
            Top         = top,
            Width       = width,
            Height      = height,
            IsMaximised = isMaximised,
            ActiveTabId = activeTabId,
            Tabs = tabs.Select(t => new TabSnapshot
            {
                Id          = t.Id,
                Url         = t.Url,
                Title       = t.Title,
                ContainerId = t.Container,
                WorkspaceId = t.Workspace,
            }).ToList(),
        };
    }

    // ── Determinism ──────────────────────────────────────────────────────

    [Fact]
    public void Compute_SameInputs_ProduceIdenticalFingerprint()
    {
        var w = Window(tabs: new[]
        {
            ("tab-1", "https://example.com", "Example", "personal", "default"),
            ("tab-2", "https://github.com",  "GitHub",  "work",     "work"),
        });
        var a = SessionFingerprint.Compute(w, cleanShutdown: false);
        var b = SessionFingerprint.Compute(w, cleanShutdown: false);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_EmptyWindow_DoesNotThrow()
    {
        var fp = SessionFingerprint.Compute(Window(), cleanShutdown: false);
        Assert.NotEmpty(fp);
        Assert.StartsWith("0|tab-1|", fp);
    }

    // ── Sensitivity ──────────────────────────────────────────────────────

    [Fact]
    public void Compute_CleanShutdownChange_DifferentFingerprint()
    {
        var w = Window();
        Assert.NotEqual(
            SessionFingerprint.Compute(w, cleanShutdown: false),
            SessionFingerprint.Compute(w, cleanShutdown: true));
    }

    [Fact]
    public void Compute_ActiveTabChange_DifferentFingerprint()
    {
        var w1 = Window(activeTabId: "tab-1");
        var w2 = Window(activeTabId: "tab-2");
        Assert.NotEqual(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }

    [Fact]
    public void Compute_BoundsChange_DifferentFingerprint()
    {
        Assert.NotEqual(
            SessionFingerprint.Compute(Window(width: 1200), cleanShutdown: false),
            SessionFingerprint.Compute(Window(width: 1400), cleanShutdown: false));
    }

    [Fact]
    public void Compute_MaximisedFlagChange_DifferentFingerprint()
    {
        Assert.NotEqual(
            SessionFingerprint.Compute(Window(isMaximised: false), cleanShutdown: false),
            SessionFingerprint.Compute(Window(isMaximised: true),  cleanShutdown: false));
    }

    [Fact]
    public void Compute_TabUrlChange_DifferentFingerprint()
    {
        var w1 = Window(tabs: new[] { ("t1", "https://a.com", "T", "c", "w") });
        var w2 = Window(tabs: new[] { ("t1", "https://b.com", "T", "c", "w") });
        Assert.NotEqual(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }

    [Fact]
    public void Compute_TabTitleChange_DifferentFingerprint()
    {
        var w1 = Window(tabs: new[] { ("t1", "u", "Old Title", "c", "w") });
        var w2 = Window(tabs: new[] { ("t1", "u", "New Title", "c", "w") });
        Assert.NotEqual(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }

    [Fact]
    public void Compute_TabContainerChange_DifferentFingerprint()
    {
        var w1 = Window(tabs: new[] { ("t1", "u", "T", "personal", "w") });
        var w2 = Window(tabs: new[] { ("t1", "u", "T", "banking",  "w") });
        Assert.NotEqual(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }

    [Fact]
    public void Compute_TabOrderingChange_DifferentFingerprint()
    {
        var t1 = ("t1", "u1", "T1", "c", "w");
        var t2 = ("t2", "u2", "T2", "c", "w");
        Assert.NotEqual(
            SessionFingerprint.Compute(Window(tabs: new[] { t1, t2 }), cleanShutdown: false),
            SessionFingerprint.Compute(Window(tabs: new[] { t2, t1 }), cleanShutdown: false));
    }

    [Fact]
    public void Compute_AddingTab_DifferentFingerprint()
    {
        var w1 = Window(tabs: new[] { ("t1", "u", "T", "c", "w") });
        var w2 = Window(tabs: new[]
        {
            ("t1", "u", "T", "c", "w"),
            ("t2", "u2", "T2", "c", "w")
        });
        Assert.NotEqual(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }

    // ── Insensitivity ────────────────────────────────────────────────────

    [Fact]
    public void Compute_ScrollYChange_SameFingerprint()
    {
        // ScrollY is intentionally OUT of the fingerprint so scrolling
        // doesn't trigger a write 100 times per minute. Verifies the
        // fingerprint shape stays narrow.
        var w1 = new WindowSnapshot
        {
            Width = 1200, Height = 800, ActiveTabId = "t1",
            Tabs = [ new TabSnapshot { Id = "t1", Url = "u", Title = "T", ScrollY = 0 } ],
        };
        var w2 = new WindowSnapshot
        {
            Width = 1200, Height = 800, ActiveTabId = "t1",
            Tabs = [ new TabSnapshot { Id = "t1", Url = "u", Title = "T", ScrollY = 9999 } ],
        };
        Assert.Equal(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }

    [Fact]
    public void Compute_LastActiveAtUtcChange_SameFingerprint()
    {
        var w1 = new WindowSnapshot
        {
            ActiveTabId = "t1",
            Tabs = [ new TabSnapshot { Id = "t1", LastActiveAtUtc = DateTime.UtcNow.AddHours(-1) } ],
        };
        var w2 = new WindowSnapshot
        {
            ActiveTabId = "t1",
            Tabs = [ new TabSnapshot { Id = "t1", LastActiveAtUtc = DateTime.UtcNow } ],
        };
        Assert.Equal(
            SessionFingerprint.Compute(w1, cleanShutdown: false),
            SessionFingerprint.Compute(w2, cleanShutdown: false));
    }
}
