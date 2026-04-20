using Moq;
using VELO.Security;
using VELO.Security.AI.Models;
using VELO.Security.GoldenList;
using VELO.Security.Models;
using Xunit;

namespace VELO.Security.Tests;

public class SafetyScorerTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SafetyScorer BuildScorer(bool isGolden = false)
    {
        var golden = new Mock<IGoldenList>();
        golden.Setup(g => g.IsGolden(It.IsAny<string>())).Returns(isGolden);
        return new SafetyScorer(golden.Object);
    }

    private static SafetyContext HttpsCtx(
        TLSStatus tls = TLSStatus.Valid,
        bool isGolden = false,
        bool whitelisted = false,
        int trackers = 0,
        int fingerprints = 0,
        AIVerdict? ai = null,
        IReadOnlyList<SecurityVerdict>? verdicts = null) => new()
    {
        Uri                      = new Uri("https://example.com"),
        SessionVerdicts           = verdicts ?? Array.Empty<SecurityVerdict>(),
        TLSStatus                 = tls,
        AIVerdict                 = ai,
        IsGoldenList              = isGolden,
        IsWhitelistedByUser       = whitelisted,
        TrackersBlockedCount      = trackers,
        FingerprintAttemptsBlocked = fingerprints,
    };

    // ── Short-circuit: Block verdict → Red ────────────────────────────────────

    [Fact]
    public void BlockVerdict_ReturnsRed_Immediately()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(verdicts: [SecurityVerdict.Block("malware", ThreatType.Malware)]);

        var result = scorer.Compute(ctx);

        Assert.Equal(SafetyLevel.Red, result.Level);
        Assert.Equal(-100, result.NumericScore);
        Assert.NotNull(result.ShortCircuitReason);
    }

    [Fact]
    public void HttpSite_ReturnsRed()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.Http);

        var result = scorer.Compute(ctx);

        Assert.Equal(SafetyLevel.Red, result.Level);
        Assert.Contains("HTTPS", result.ShortCircuitReason!);
    }

    [Fact]
    public void ExpiredCertificate_ReturnsRed()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.Expired);

        var result = scorer.Compute(ctx);

        Assert.Equal(SafetyLevel.Red, result.Level);
    }

    // ── Golden List ────────────────────────────────────────────────────────────

    [Fact]
    public void GoldenList_WithValidTLS_ReturnsGold()
    {
        var scorer = BuildScorer(isGolden: true);
        var ctx = HttpsCtx(isGolden: true, tls: TLSStatus.Valid);

        var result = scorer.Compute(ctx);

        Assert.Equal(SafetyLevel.Gold, result.Level);
        Assert.True(result.NumericScore >= 40);
    }

    [Fact]
    public void GoldenList_WithBlockVerdict_StillReturnsRed()
    {
        var scorer = BuildScorer(isGolden: true);
        var ctx = HttpsCtx(
            isGolden: true,
            verdicts: [SecurityVerdict.Block("threat", ThreatType.Malware)]);

        var result = scorer.Compute(ctx);

        Assert.Equal(SafetyLevel.Red, result.Level);
    }

    // ── TLS signals ───────────────────────────────────────────────────────────

    [Fact]
    public void SelfSignedCert_ReducesScore_AndYieldsNegativeReason()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.SelfSigned);

        var result = scorer.Compute(ctx);

        Assert.True(result.NumericScore < 0);
        Assert.Contains(result.ReasonsNegative, r => r.Contains("auto-firmado"));
    }

    [Fact]
    public void ValidTLS_AddsPositiveReason()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.Valid);

        var result = scorer.Compute(ctx);

        Assert.Contains(result.ReasonsPositive, r => r.Contains("TLS"));
    }

    // ── AI verdicts ───────────────────────────────────────────────────────────

    [Fact]
    public void AIBlock_PenalizesScore_And_AddsNegativeReason()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(
            tls: TLSStatus.Valid,
            ai: AIVerdict.Block("phishing detected"));

        var result = scorer.Compute(ctx);

        Assert.Contains(result.ReasonsNegative, r => r.Contains("phishing detected"));
    }

    [Fact]
    public void AISafe_AddsPositiveReason()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.Valid, ai: AIVerdict.Safe());

        var result = scorer.Compute(ctx);

        Assert.Contains(result.ReasonsPositive, r => r.Contains("segura"));
    }

    [Fact]
    public void AIWarn_AddsNegativeReason()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.Valid, ai: AIVerdict.Warn("suspicious JS"));

        var result = scorer.Compute(ctx);

        Assert.Contains(result.ReasonsNegative, r => r.Contains("suspicious JS"));
    }

    // ── Trackers & fingerprint ────────────────────────────────────────────────

    [Fact]
    public void TrackersBlocked_AddsPositiveBonus_CappedAt15()
    {
        var scorer = BuildScorer();
        var ctx5   = HttpsCtx(tls: TLSStatus.Valid, trackers: 5);
        var ctx100 = HttpsCtx(tls: TLSStatus.Valid, trackers: 100);

        var r5   = scorer.Compute(ctx5);
        var r100 = scorer.Compute(ctx100);

        Assert.True(r5.NumericScore > 0);
        // Bonus is capped at 15 for trackers
        Assert.True(r100.NumericScore <= r5.NumericScore + 10);
    }

    [Fact]
    public void FingerprintAttempts_PenalizeScore()
    {
        var scorer  = BuildScorer();
        var ctxNone = HttpsCtx(tls: TLSStatus.Valid, fingerprints: 0);
        var ctxMany = HttpsCtx(tls: TLSStatus.Valid, fingerprints: 5);

        var rNone = scorer.Compute(ctxNone);
        var rMany = scorer.Compute(ctxMany);

        Assert.True(rNone.NumericScore > rMany.NumericScore);
        Assert.Contains(rMany.ReasonsNegative, r => r.Contains("fingerprinting"));
    }

    // ── Score bucketing ───────────────────────────────────────────────────────

    [Fact]
    public void WhitelistedUser_AddsBonus_And_CanReachGreen()
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: TLSStatus.Valid, whitelisted: true);

        var result = scorer.Compute(ctx);

        Assert.True(result.NumericScore >= 20);
        Assert.Equal(SafetyLevel.Green, result.Level);
    }

    [Fact]
    public void ScoreIsClampedBetween_Minus100_And_Plus100()
    {
        var scorer = BuildScorer(isGolden: true);
        var ctx = HttpsCtx(
            isGolden: true,
            whitelisted: true,
            tls: TLSStatus.Valid,
            trackers: 1000,
            ai: AIVerdict.Safe());

        var result = scorer.Compute(ctx);

        Assert.InRange(result.NumericScore, -100, 100);
    }

    [Theory]
    [InlineData(TLSStatus.Valid, SafetyLevel.Green)]
    [InlineData(TLSStatus.SelfSigned, SafetyLevel.Yellow)]
    [InlineData(TLSStatus.Error, SafetyLevel.Yellow)]
    public void TLSOnly_MapsToExpectedLevel(TLSStatus tls, SafetyLevel expected)
    {
        var scorer = BuildScorer();
        var ctx = HttpsCtx(tls: tls);

        var result = scorer.Compute(ctx);

        Assert.Equal(expected, result.Level);
    }

    // ── Result fields ─────────────────────────────────────────────────────────

    [Fact]
    public void Result_ComputedAt_IsRecent()
    {
        var scorer = BuildScorer();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = scorer.Compute(HttpsCtx());

        Assert.True(result.ComputedAt >= before);
    }

    [Fact]
    public void NormalResult_HasNullShortCircuitReason()
    {
        var scorer = BuildScorer();
        var result = scorer.Compute(HttpsCtx(tls: TLSStatus.Valid));

        Assert.Null(result.ShortCircuitReason);
    }
}
