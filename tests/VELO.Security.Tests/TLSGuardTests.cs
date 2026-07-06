using Microsoft.Extensions.Logging.Abstractions;
using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

public class TLSGuardTests
{
    // ── CT log check opt-in (v2.4.59 QW-3 / decision #3) ─────────────────

    [Fact]
    public void CtLogCheck_DefaultsToDisabled()
    {
        // Privacy contract: the crt.sh lookup leaks every visited domain to a
        // third party, so it must be OFF unless the user explicitly opts in.
        var guard = new TLSGuard(NullLogger<TLSGuard>.Instance);
        Assert.False(guard.CtLogCheckEnabled);
    }

    [Fact]
    public async Task CheckCTLogs_WhenDisabled_NeverFiresThreatDetected()
    {
        // With the opt-in off this must return immediately without firing. If
        // someone removes the Enabled guard, the crt.sh query for a nonexistent
        // domain returns an empty CT set and ThreatDetected fires, failing this.
        var guard = new TLSGuard(NullLogger<TLSGuard>.Instance);
        var fired = false;
        guard.ThreatDetected += (_, _) => fired = true;

        await guard.CheckCTLogsAsync(
            "definitely-not-in-ct-logs.invalid",
            "https://definitely-not-in-ct-logs.invalid/");

        Assert.False(fired);
    }

    // ── EvaluateCertError (v2.4.59 AS-2 / v2.4.60 A4) ────────────────────

    [Fact]
    public void EvaluateCertError_PrivateHost_Allows()
    {
        var guard = new TLSGuard(NullLogger<TLSGuard>.Instance);
        var v = guard.EvaluateCertError("https://localhost/", isSelfSigned: true, isExpired: false, isPrivateHost: true);
        Assert.Equal(VELO.Security.AI.Models.VerdictType.Safe, v.Verdict);
    }

    [Theory]
    [InlineData(true,  false)] // self-signed
    [InlineData(false, true)]  // expired
    [InlineData(false, false)] // other cert error
    public void EvaluateCertError_PublicHost_Blocks(bool selfSigned, bool expired)
    {
        // Since v2.4.59 the navigation is hard-cancelled; the verdict must say
        // Block (v2.4.60 A4 — Warn contradicted the actual action).
        var guard = new TLSGuard(NullLogger<TLSGuard>.Instance);
        var v = guard.EvaluateCertError("https://expired.badssl.com/", selfSigned, expired, isPrivateHost: false);
        Assert.Equal(VELO.Security.AI.Models.VerdictType.Block, v.Verdict);
    }
}
