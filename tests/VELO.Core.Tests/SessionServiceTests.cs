using System.IO;
using VELO.Core.Sessions;
using Xunit;

namespace VELO.Core.Tests;

public class SessionServiceTests
{
    private static (SessionService Svc, string TempFile) NewService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"velo-session-test-{Guid.NewGuid():N}.json");
        return (new SessionService(path), path);
    }

    private static SessionSnapshot WithTabs(params TabSnapshot[] tabs) => new()
    {
        Version          = 1,
        SavedAtUtc       = DateTime.UtcNow,
        WasCleanShutdown = true,
        Windows =
        [
            new WindowSnapshot
            {
                Left = 0, Top = 0, Width = 1280, Height = 720,
                ActiveTabId = tabs.FirstOrDefault()?.Id ?? "",
                Tabs = tabs.ToList(),
            }
        ],
    };

    private static TabSnapshot Tab(string id, string container = "none", string url = "https://example.com") =>
        new() { Id = id, Url = url, Title = $"Tab {id}", ContainerId = container };

    // ── Spec § 6.5 tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_IncludesAllOpenTabs()
    {
        var (svc, file) = NewService();
        try
        {
            var snap = WithTabs(Tab("a"), Tab("b"), Tab("c"));
            await svc.SnapshotAsync(snap);

            var loaded = await svc.LoadLastAsync();
            Assert.NotNull(loaded);
            Assert.Equal(3, loaded!.TotalTabs);
            Assert.Equal(new[] { "a", "b", "c" },
                loaded.Windows[0].Tabs.Select(t => t.Id).ToArray());
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public async Task Snapshot_OmitsBankingContainerTabs()
    {
        var (svc, file) = NewService();
        try
        {
            var snap = WithTabs(
                Tab("personal", container: "personal"),
                Tab("bankA",    container: "banking"),
                Tab("bankB",    container: "Banking"),  // case-insensitive
                Tab("work",     container: "work"));
            await svc.SnapshotAsync(snap);

            var loaded = await svc.LoadLastAsync();
            Assert.NotNull(loaded);
            var ids = loaded!.Windows[0].Tabs.Select(t => t.Id).ToArray();
            Assert.DoesNotContain("bankA", ids);
            Assert.DoesNotContain("bankB", ids);
            Assert.Equal(new[] { "personal", "work" }, ids);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public async Task Snapshot_OmitsTemporalContainerTabs()
    {
        var (svc, file) = NewService();
        try
        {
            var snap = WithTabs(
                Tab("keep",  container: "personal"),
                Tab("temp1", container: "temporal-abc123"),
                Tab("temp2", container: "Temp-feed"));
            await svc.SnapshotAsync(snap);

            var loaded = await svc.LoadLastAsync();
            Assert.NotNull(loaded);
            Assert.Single(loaded!.Windows[0].Tabs);
            Assert.Equal("keep", loaded.Windows[0].Tabs[0].Id);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public async Task LoadLast_ReturnsNull_WhenFileMissing()
    {
        // Path that definitely doesn't exist on this machine.
        var path = Path.Combine(Path.GetTempPath(), $"velo-session-missing-{Guid.NewGuid():N}.json");
        var svc  = new SessionService(path);

        var result = await svc.LoadLastAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadLast_ReturnsNull_WhenFileCorrupt()
    {
        var (svc, file) = NewService();
        try
        {
            await File.WriteAllTextAsync(file, "{ this is not valid JSON :::: ");
            var result = await svc.LoadLastAsync();
            // Best-effort recovery: corrupt file is treated as no snapshot.
            Assert.Null(result);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public async Task LoadLast_DetectsUncleanShutdown()
    {
        var (svc, file) = NewService();
        try
        {
            // Heartbeat snapshot: clean=false. If app exits without writing
            // the final clean snapshot, the loader sees clean=false on next
            // launch — which is the cue for the crash-recovery prompt.
            var snap = new SessionSnapshot
            {
                Version          = 1,
                WasCleanShutdown = false,
                Windows          = [new WindowSnapshot { Tabs = [Tab("a")] }],
            };
            await svc.SnapshotAsync(snap);

            var loaded = await svc.LoadLastAsync();
            Assert.NotNull(loaded);
            Assert.False(loaded!.WasCleanShutdown);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    // ── Bonus tests ────────────────────────────────────────────────────

    [Fact]
    public async Task Clear_DeletesTheFile()
    {
        var (svc, file) = NewService();
        await svc.SnapshotAsync(WithTabs(Tab("a")));
        Assert.True(File.Exists(file));

        await svc.ClearAsync();
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Snapshot_WithOnlyBankingTabs_DropsTheWindow()
    {
        var (svc, file) = NewService();
        try
        {
            // If every tab in a window is banking, the window itself should
            // not survive the snapshot — restoring an empty window is useless.
            var snap = WithTabs(Tab("b1", container: "banking"), Tab("b2", container: "banking"));
            await svc.SnapshotAsync(snap);

            var loaded = await svc.LoadLastAsync();
            Assert.NotNull(loaded);
            Assert.Empty(loaded!.Windows);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public async Task LoadLast_IgnoresFutureSchemaVersion()
    {
        var (svc, file) = NewService();
        try
        {
            // Hand-craft a JSON with a higher Version than we support so the
            // loader knows to skip rather than misinterpret unknown fields.
            await File.WriteAllTextAsync(file, """
                {
                  "Version": 999,
                  "SavedAtUtc": "2030-01-01T00:00:00Z",
                  "WasCleanShutdown": true,
                  "Windows": []
                }
                """);
            var result = await svc.LoadLastAsync();
            Assert.Null(result);
        }
        finally { try { File.Delete(file); } catch { } }
    }
}
