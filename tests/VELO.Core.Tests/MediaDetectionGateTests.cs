using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.Media;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 — the media-detection opt-out. Same shape as
/// <c>YouTubeAdBlockerTests</c>: the setting round-trip and the IsEnabled
/// cache. Script injection itself is BrowserTab + WebView2 and is verified at
/// runtime, not here.
///
/// The switch exists because detection wraps MediaSource/SourceBuffer — the
/// media hot path — and anything on by default that touches page behaviour
/// needs a recourse that is not "downgrade".
/// </summary>
public class MediaDetectionGateTests
{
    private static async Task<(MediaDetectionGate Gate, SettingsRepository Settings)> BuildAsync()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-media-gate-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        var settings = new SettingsRepository(db);
        return (new MediaDetectionGate(settings), settings);
    }

    [Fact]
    public void Default_is_enabled()
    {
        // A feature nobody can see is not a safe default, it is a hidden one —
        // that was the whole problem with the VELO_MEDIA_PROBE gate this
        // replaced. Pinned so turning it off by default becomes a deliberate,
        // visible change.
        Assert.Equal("yes", MediaDetectionGate.DefaultSettingValue);
    }

    [Fact]
    public void IsEnabled_is_optimistically_true_before_the_first_refresh()
    {
        // BrowserTab reads IsEnabled inside EnsureWebViewInitializedAsync,
        // which can run before the bootstrap RefreshAsync finishes. The cached
        // default has to match the persisted default or the first tab of the
        // session silently loses detection.
        var gate = new MediaDetectionGate(
            new SettingsRepository(new VeloDatabase(
                NullLogger<VeloDatabase>.Instance,
                Path.Combine(Path.GetTempPath(), "velo-media-gate-" + Guid.NewGuid().ToString("N")))));

        Assert.True(gate.IsEnabled);
    }

    [Fact]
    public async Task Refresh_with_nothing_stored_yields_the_default()
    {
        var (gate, _) = await BuildAsync();
        await gate.RefreshAsync();
        Assert.True(gate.IsEnabled);
    }

    [Fact]
    public async Task Turning_it_off_persists_and_survives_a_refresh()
    {
        var (gate, settings) = await BuildAsync();

        await gate.SetEnabledAsync(false);

        Assert.False(gate.IsEnabled);                                   // cache updated at once
        Assert.Equal("no", await settings.GetAsync(SettingKeys.MediaDetectionEnabled, "yes"));

        await gate.RefreshAsync();                                       // and it is not lost
        Assert.False(gate.IsEnabled);
    }

    [Fact]
    public async Task Turning_it_back_on_persists_too()
    {
        var (gate, settings) = await BuildAsync();

        await gate.SetEnabledAsync(false);
        await gate.SetEnabledAsync(true);

        Assert.True(gate.IsEnabled);
        Assert.Equal("yes", await settings.GetAsync(SettingKeys.MediaDetectionEnabled, "no"));
    }

    [Fact]
    public async Task A_stored_value_other_than_yes_reads_as_disabled()
    {
        // The convention is the "yes"/"no" string the rest of these settings
        // use. Anything else is not a third state — it is off, which is the
        // safe reading for a page-side wrapper.
        var (gate, settings) = await BuildAsync();

        await settings.SetAsync(SettingKeys.MediaDetectionEnabled, "nonsense");
        await gate.RefreshAsync();

        Assert.False(gate.IsEnabled);
    }
}
