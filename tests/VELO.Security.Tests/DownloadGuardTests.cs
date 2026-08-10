using Microsoft.Extensions.Logging.Abstractions;
using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

/// <summary>
/// Characterization tests for <see cref="DownloadGuard"/>, written before P3
/// opened a lane through it.
///
/// The guard shipped with **no tests at all** — 273 security tests and none
/// covering the rule that blocks the second download in three seconds. There
/// was therefore no way to make the claim P3 depends on ("the drive-by rule
/// stays exactly as strong for everything else") mean anything. These pin the
/// behaviour as it existed before the lane, and were run green against the
/// unmodified guard first. The lane tests live in DownloadGuardLaneTests.cs,
/// deliberately separate: a characterization suite that only ever ran against
/// the changed code proves nothing about what the change preserved.
///
/// Note on isolation: the user-override sets on DownloadGuard are **static**,
/// so they outlive an instance and would leak between tests. Nothing here
/// writes to them. Fixing that staticness is out of P3's scope.
/// </summary>
public class DownloadGuardTests
{
    internal static DownloadGuard Build() => new(NullLogger<DownloadGuard>.Instance);

    internal static string NewTab() => "tab-" + Guid.NewGuid().ToString("N");

    // ── Rule 1 — the burst rule, exactly as it behaves today ─────────────

    [Fact]
    public void First_download_in_a_tab_is_allowed()
    {
        var verdict = Build().Evaluate(NewTab(), "https://example.com/a.zip", "a.zip", "https://example.com/");
        Assert.Equal(DownloadAction.Allow, verdict.Action);
    }

    [Fact]
    public void Second_download_within_the_burst_window_is_blocked()
    {
        // This is V-6, the rule that kills any segmented download: the SECOND
        // download inside 3 s is already a burst.
        var guard = Build();
        var tab   = NewTab();

        var first  = guard.Evaluate(tab, "https://example.com/a.zip", "a.zip", "https://example.com/");
        var second = guard.Evaluate(tab, "https://example.com/b.zip", "b.zip", "https://example.com/");

        Assert.Equal(DownloadAction.Allow, first.Action);
        Assert.Equal(DownloadAction.Block, second.Action);
    }

    [Fact]
    public void Burst_tracking_is_per_tab_not_global()
    {
        var guard = Build();

        var a = guard.Evaluate(NewTab(), "https://example.com/a.zip", "a.zip", "https://example.com/");
        var b = guard.Evaluate(NewTab(), "https://example.com/b.zip", "b.zip", "https://example.com/");

        Assert.Equal(DownloadAction.Allow, a.Action);
        Assert.Equal(DownloadAction.Allow, b.Action);
    }

    [Fact]
    public void ResetBurst_clears_the_counter_for_that_tab()
    {
        var guard = Build();
        var tab   = NewTab();

        guard.Evaluate(tab, "https://example.com/a.zip", "a.zip", "https://example.com/");
        guard.ResetBurst(tab);
        var afterReset = guard.Evaluate(tab, "https://example.com/b.zip", "b.zip", "https://example.com/");

        Assert.Equal(DownloadAction.Allow, afterReset.Action);
    }

    // ── Rules 2-4 — extensions and origin ────────────────────────────────

    [Fact]
    public void Cross_origin_executable_is_blocked()
    {
        var verdict = Build().Evaluate(
            NewTab(), "https://cdn.evil.example/setup.exe", "setup.exe", "https://news.example/article");

        Assert.Equal(DownloadAction.Block, verdict.Action);
    }

    [Fact]
    public void Same_origin_executable_only_warns()
    {
        var verdict = Build().Evaluate(
            NewTab(), "https://vendor.example/setup.exe", "setup.exe", "https://vendor.example/downloads");

        Assert.Equal(DownloadAction.Warn, verdict.Action);
    }

    [Fact]
    public void Subdomains_of_the_same_site_are_not_cross_origin()
    {
        var verdict = Build().Evaluate(
            NewTab(), "https://downloads.vendor.example/setup.exe", "setup.exe", "https://vendor.example/");

        Assert.Equal(DownloadAction.Warn, verdict.Action);   // warn, not block
    }

    [Fact]
    public void A_download_with_no_parent_page_is_not_treated_as_cross_origin()
    {
        // v2.0.5.5 — VELO launched directly with a download URL by another
        // program has no page, and that is not a drive-by by definition.
        var verdict = Build().Evaluate(NewTab(), "https://vendor.example/setup.exe", "setup.exe", "");

        Assert.Equal(DownloadAction.Warn, verdict.Action);
    }

    [Theory]
    [InlineData("holiday.jpg")]
    [InlineData("report.pdf")]
    [InlineData("album.mp3")]
    [InlineData("movie.mp4")]
    public void Safe_extensions_are_allowed(string fileName)
    {
        var verdict = Build().Evaluate(
            NewTab(), $"https://cdn.example/{fileName}", fileName, "https://site.example/");

        Assert.Equal(DownloadAction.Allow, verdict.Action);
    }

    [Fact]
    public void An_unknown_extension_is_allowed()
    {
        // HLS segments are .ts, which is in neither list.
        var verdict = Build().Evaluate(
            NewTab(), "https://cdn.example/segment.ts", "segment.ts", "https://site.example/");

        Assert.Equal(DownloadAction.Allow, verdict.Action);
    }
}
