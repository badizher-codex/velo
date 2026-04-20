using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.Events;
using VELO.Security.Rules;
using Xunit;

namespace VELO.Security.Tests;

public class BlocklistManagerTests
{
    private static BlocklistManager Build()
    {
        var bus    = new EventBus();
        var logger = NullLogger<BlocklistManager>.Instance;
        return new BlocklistManager(bus, logger);
    }

    // ── IsBlocked — exact match ───────────────────────────────────────────────

    [Fact]
    public void IsBlocked_ReturnsFalse_WhenListIsEmpty()
    {
        var mgr = Build();
        Assert.False(mgr.IsBlocked("tracker.example.com"));
    }

    // ── ParseContent — ABP format ─────────────────────────────────────────────
    // We test ParseContent indirectly through LoadBundledAsync using temp files.

    [Fact]
    public async Task LoadBundled_AbpFormat_BlocksMatchingDomain()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        await File.WriteAllTextAsync(
            Path.Combine(listDir, "test.txt"),
            "! comment line\n||doubleclick.net^\n||googletagmanager.com^\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.True(mgr.IsBlocked("doubleclick.net"));
        Assert.True(mgr.IsBlocked("googletagmanager.com"));
        Assert.Equal(2, mgr.DomainCount);

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadBundled_AbpFormat_IgnoresCommentLines()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        await File.WriteAllTextAsync(
            Path.Combine(listDir, "test.txt"),
            "! This is a comment\n# Also a comment\n||real-tracker.net^\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.False(mgr.IsBlocked("This"));
        Assert.True(mgr.IsBlocked("real-tracker.net"));

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadBundled_AbpFormat_IgnoresUrlPatterns_WithSlash()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        // Lines with path segments are NOT pure-domain entries — should be skipped
        await File.WriteAllTextAsync(
            Path.Combine(listDir, "test.txt"),
            "||example.com/ads/banner^\n||domain.net^\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.False(mgr.IsBlocked("example.com"));
        Assert.True(mgr.IsBlocked("domain.net"));

        Directory.Delete(tempDir, recursive: true);
    }

    // ── ParseContent — Hosts format ───────────────────────────────────────────

    [Fact]
    public async Task LoadBundled_HostsFormat_ParsesZeroZeroZeroZeroEntries()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        // .hosts extension triggers hosts-format parser
        await File.WriteAllTextAsync(
            Path.Combine(listDir, "hosts.hosts"),
            "# comment\n0.0.0.0 ads.tracker.io\n127.0.0.1 malware.biz\n0.0.0.0 localhost\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.True(mgr.IsBlocked("ads.tracker.io"));
        Assert.True(mgr.IsBlocked("malware.biz"));

        Directory.Delete(tempDir, recursive: true);
    }

    // ── IsBlocked — subdomain inheritance ────────────────────────────────────

    [Fact]
    public async Task IsBlocked_ReturnsTrueForSubdomain_WhenParentIsBlocked()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        await File.WriteAllTextAsync(
            Path.Combine(listDir, "test.txt"),
            "||doubleclick.net^\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.True(mgr.IsBlocked("sub.doubleclick.net"));
        Assert.True(mgr.IsBlocked("deep.sub.doubleclick.net"));
        Assert.False(mgr.IsBlocked("notdoubleclick.net"));

        Directory.Delete(tempDir, recursive: true);
    }

    // ── IsBlocked — case insensitivity ────────────────────────────────────────

    [Fact]
    public async Task IsBlocked_IsCaseInsensitive()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        await File.WriteAllTextAsync(
            Path.Combine(listDir, "test.txt"),
            "||Tracker.EXAMPLE.COM^\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.True(mgr.IsBlocked("tracker.example.com"));
        Assert.True(mgr.IsBlocked("TRACKER.EXAMPLE.COM"));

        Directory.Delete(tempDir, recursive: true);
    }

    // ── DomainCount ──────────────────────────────────────────────────────────

    [Fact]
    public void DomainCount_IsZero_OnFreshInstance()
    {
        var mgr = Build();
        Assert.Equal(0, mgr.DomainCount);
    }

    [Fact]
    public async Task DomainCount_ReflectsLoadedDomains()
    {
        var mgr     = Build();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var listDir = Path.Combine(tempDir, "blocklists");
        Directory.CreateDirectory(listDir);

        await File.WriteAllTextAsync(
            Path.Combine(listDir, "test.txt"),
            "||a.com^\n||b.com^\n||c.com^\n");

        await mgr.LoadBundledAsync(tempDir);

        Assert.Equal(3, mgr.DomainCount);

        Directory.Delete(tempDir, recursive: true);
    }
}
