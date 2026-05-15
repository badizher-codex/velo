using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

/// <summary>
/// v2.4.53 — Coverage for the YouTube ad-block opt-out gate. Tests the
/// setting round-trip + IsEnabled cache behaviour. The actual script
/// injection is BrowserTab + WebView2 runtime — verified manually by the
/// maintainer, not in this suite.
/// </summary>
public class YouTubeAdBlockerTests
{
    private static async Task<(YouTubeAdBlocker Svc, SettingsRepository Settings)> BuildAsync()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        var settings = new SettingsRepository(db);
        var svc = new YouTubeAdBlocker(settings, logger: null);
        return (svc, settings);
    }

    [Fact]
    public void DefaultSettingValue_isYes()
    {
        // Out-of-the-box experience is "ads blocked" — privacy-first. Tests
        // pin this so a future "tune-down for legal reasons" can't be a
        // silent change.
        Assert.Equal("yes", YouTubeAdBlocker.DefaultSettingValue);
    }

    [Fact]
    public void IsEnabled_optimisticTrueBeforeRefresh()
    {
        // BrowserTab consults IsEnabled inside EnsureWebViewInitializedAsync,
        // which can run before the app's bootstrap RefreshAsync completes.
        // The cached default must match the persisted default ("yes") so the
        // first tab doesn't fall through to "ads not blocked" by accident.
        var svc = new YouTubeAdBlocker(
            new SettingsRepository(new VeloDatabase(
                NullLogger<VeloDatabase>.Instance,
                Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N")))));
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public async Task RefreshAsync_readsYesFromSettings_setsEnabledTrue()
    {
        var (svc, settings) = await BuildAsync();
        await settings.SetAsync(SettingKeys.YouTubeAdsBlocked, "yes");

        await svc.RefreshAsync();

        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public async Task RefreshAsync_readsNoFromSettings_setsEnabledFalse()
    {
        var (svc, settings) = await BuildAsync();
        await settings.SetAsync(SettingKeys.YouTubeAdsBlocked, "no");

        await svc.RefreshAsync();

        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public async Task RefreshAsync_caseInsensitive_acceptsYES()
    {
        // The string convention is lowercase but a future migration / hand-
        // edited settings.json could produce "YES" / "Yes". Be lenient.
        var (svc, settings) = await BuildAsync();
        await settings.SetAsync(SettingKeys.YouTubeAdsBlocked, "YES");

        await svc.RefreshAsync();

        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public async Task RefreshAsync_missingSetting_fallsBackToDefaultYes()
    {
        // First install: the row doesn't exist in the settings table yet.
        // Helper assumes "yes" so the user gets ad-block out of the box.
        var (svc, _) = await BuildAsync();
        // Note: no SetAsync — the key never gets written.

        await svc.RefreshAsync();

        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public async Task SetEnabledAsync_writesStringYesAndUpdatesCache()
    {
        var (svc, settings) = await BuildAsync();

        await svc.SetEnabledAsync(true);

        Assert.True(svc.IsEnabled);
        var raw = await settings.GetAsync(SettingKeys.YouTubeAdsBlocked, "");
        Assert.Equal("yes", raw);
    }

    [Fact]
    public async Task SetEnabledAsync_writesStringNoAndUpdatesCache()
    {
        var (svc, settings) = await BuildAsync();

        await svc.SetEnabledAsync(false);

        Assert.False(svc.IsEnabled);
        var raw = await settings.GetAsync(SettingKeys.YouTubeAdsBlocked, "");
        Assert.Equal("no", raw);
    }
}
