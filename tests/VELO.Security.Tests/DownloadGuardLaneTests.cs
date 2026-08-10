using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

/// <summary>
/// Phase 6 / P3 — the user-initiated download lane.
///
/// V-6: an HLS download is hundreds of segment requests and Rule 1 blocks the
/// second one inside three seconds. The lane exists so that transfer can run,
/// and the drive-by rule it steps around is doing real work, so most of this
/// file is about what the lane REFUSES. Lesson #44 — every new guard runs in
/// both directions — and this is the one that most needs it: a lane that can
/// be reached by anything other than a user click is a hole in the guard, not
/// a feature.
///
/// The pre-existing behaviour these must not disturb is pinned separately in
/// <see cref="DownloadGuardTests"/>, which was run green against the guard
/// before the lane was added.
/// </summary>
public class DownloadGuardLaneTests
{
    private static DownloadGuard Build() => DownloadGuardTests.Build();
    private static string NewTab()       => DownloadGuardTests.NewTab();

    // ── The reason it exists ─────────────────────────────────────────────

    [Fact]
    public void A_user_initiated_job_runs_hundreds_of_downloads_untouched()
    {
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        for (var i = 0; i < 500; i++)
        {
            var verdict = guard.Evaluate(
                tab, $"https://cdn.example/seg{i}.ts", $"seg{i}.ts", "https://site.example/", job);
            Assert.Equal(DownloadAction.Allow, verdict.Action);
        }
    }

    // ── What it refuses ──────────────────────────────────────────────────

    [Fact]
    public void Without_a_token_the_drive_by_rule_is_exactly_as_strong()
    {
        // Same guard instance, lane wide open, but a burst that does not
        // present the token still dies on the second request.
        var guard = Build();
        var tab   = NewTab();
        guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        guard.Evaluate(tab, "https://evil.example/1.zip", "1.zip", "https://evil.example/");
        var second = guard.Evaluate(tab, "https://evil.example/2.zip", "2.zip", "https://evil.example/");

        Assert.Equal(DownloadAction.Block, second.Action);
    }

    [Fact]
    public void A_token_issued_for_another_tab_does_not_open_this_one()
    {
        var guard    = Build();
        var jobTab   = NewTab();
        var otherTab = NewTab();
        var job = guard.BeginUserInitiatedJob(jobTab, expectedDownloads: 500);

        guard.Evaluate(otherTab, "https://evil.example/1.zip", "1.zip", "https://evil.example/", job);
        var second = guard.Evaluate(otherTab, "https://evil.example/2.zip", "2.zip", "https://evil.example/", job);

        Assert.Equal(DownloadAction.Block, second.Action);
    }

    [Fact]
    public void A_forged_token_does_not_open_the_lane()
    {
        var guard = Build();
        var tab   = NewTab();

        guard.Evaluate(tab, "https://evil.example/1.zip", "1.zip", "https://evil.example/", "not-a-real-token");
        var second = guard.Evaluate(tab, "https://evil.example/2.zip", "2.zip", "https://evil.example/", "not-a-real-token");

        Assert.Equal(DownloadAction.Block, second.Action);
    }

    [Fact]
    public void An_ended_job_closes_the_lane_immediately()
    {
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        guard.Evaluate(tab, "https://cdn.example/seg0.ts", "seg0.ts", "https://site.example/", job);
        guard.EndUserInitiatedJob(job);

        guard.Evaluate(tab, "https://cdn.example/seg1.ts", "seg1.ts", "https://site.example/", job);
        var afterEnd = guard.Evaluate(tab, "https://cdn.example/seg2.ts", "seg2.ts", "https://site.example/", job);

        Assert.Equal(DownloadAction.Block, afterEnd.Action);
    }

    [Fact]
    public void Ending_all_jobs_for_a_tab_closes_its_lanes()
    {
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        guard.EndJobsForTab(tab);

        guard.Evaluate(tab, "https://cdn.example/s0.ts", "s0.ts", "https://site.example/", job);
        var second = guard.Evaluate(tab, "https://cdn.example/s1.ts", "s1.ts", "https://site.example/", job);

        Assert.Equal(DownloadAction.Block, second.Action);
    }

    [Fact]
    public void An_expired_job_closes_the_lane()
    {
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(
            tab, expectedDownloads: 500, maxDuration: TimeSpan.FromMilliseconds(1));

        Thread.Sleep(30);

        guard.Evaluate(tab, "https://evil.example/1.zip", "1.zip", "https://evil.example/", job);
        var second = guard.Evaluate(tab, "https://evil.example/2.zip", "2.zip", "https://evil.example/", job);

        Assert.Equal(DownloadAction.Block, second.Action);
    }

    [Fact]
    public void A_job_that_exceeds_its_budget_falls_back_to_the_burst_rule()
    {
        // A lane with no ceiling is a permanent hole. The budget is what makes
        // this a lane rather than an off switch.
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 3);

        for (var i = 0; i < 3; i++)
            Assert.Equal(DownloadAction.Allow,
                guard.Evaluate(tab, $"https://cdn.example/s{i}.ts", $"s{i}.ts", "https://site.example/", job).Action);

        // Budget spent. The tracker never saw the lane requests, so this one is
        // the first it knows about — allowed…
        Assert.Equal(DownloadAction.Allow,
            guard.Evaluate(tab, "https://cdn.example/s3.ts", "s3.ts", "https://site.example/", job).Action);
        // …and the next is a burst again.
        Assert.Equal(DownloadAction.Block,
            guard.Evaluate(tab, "https://cdn.example/s4.ts", "s4.ts", "https://site.example/", job).Action);
    }

    // ── Scope: the lane bypasses ONE rule ────────────────────────────────

    [Fact]
    public void The_lane_bypasses_the_burst_rule_and_nothing_else()
    {
        // The most important negative here. The lane says "the user asked for
        // this transfer", not "anything goes" — a cross-origin executable
        // inside an open lane must still be blocked.
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        var verdict = guard.Evaluate(
            tab, "https://cdn.evil.example/payload.exe", "payload.exe", "https://site.example/watch", job);

        Assert.Equal(DownloadAction.Block, verdict.Action);
    }

    [Fact]
    public void A_dangerous_extension_inside_a_lane_still_warns()
    {
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        var verdict = guard.Evaluate(
            tab, "https://site.example/tool.exe", "tool.exe", "https://site.example/watch", job);

        Assert.Equal(DownloadAction.Warn, verdict.Action);
    }

    // ── The tracker keeps measuring what it was built to measure ─────────

    [Fact]
    public void Lane_downloads_do_not_pollute_the_drive_by_counter()
    {
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 300);

        for (var i = 0; i < 300; i++)
            guard.Evaluate(tab, $"https://cdn.example/s{i}.ts", $"s{i}.ts", "https://site.example/", job);

        // An ordinary download right afterwards is the first the tracker saw.
        Assert.Equal(DownloadAction.Allow,
            guard.Evaluate(tab, "https://site.example/notes.pdf", "notes.pdf", "https://site.example/").Action);
        // And the drive-by rule is still armed behind it.
        Assert.Equal(DownloadAction.Block,
            guard.Evaluate(tab, "https://site.example/x.zip", "x.zip", "https://site.example/").Action);
    }

    [Fact]
    public void A_drive_by_during_an_open_lane_is_still_detected()
    {
        // Because lane requests are not counted, page-initiated downloads
        // arriving WHILE a capture runs are still measured on their own — the
        // property that makes "don't pollute the counter" safe rather than a
        // blind spot.
        var guard = Build();
        var tab   = NewTab();
        var job   = guard.BeginUserInitiatedJob(tab, expectedDownloads: 500);

        guard.Evaluate(tab, "https://cdn.example/s0.ts", "s0.ts", "https://site.example/", job);
        guard.Evaluate(tab, "https://evil.example/1.zip", "1.zip", "https://site.example/");
        guard.Evaluate(tab, "https://cdn.example/s1.ts", "s1.ts", "https://site.example/", job);
        var secondDriveBy = guard.Evaluate(tab, "https://evil.example/2.zip", "2.zip", "https://site.example/");

        Assert.Equal(DownloadAction.Block, secondDriveBy.Action);
    }

    // ── The token itself ─────────────────────────────────────────────────

    [Fact]
    public void Tokens_are_distinct_and_long_enough_to_be_unguessable()
    {
        var guard = Build();
        var a = guard.BeginUserInitiatedJob(NewTab(), expectedDownloads: 1);
        var b = guard.BeginUserInitiatedJob(NewTab(), expectedDownloads: 1);

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 32, $"token is {a.Length} chars — too short to be unguessable");
    }

    [Fact]
    public void Ending_an_unknown_or_null_token_is_harmless()
    {
        var guard = Build();
        guard.EndUserInitiatedJob(null);
        guard.EndUserInitiatedJob("");
        guard.EndUserInitiatedJob("nonsense");
        // No throw is the assertion.
    }
}
