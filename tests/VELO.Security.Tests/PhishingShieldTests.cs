using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

public class PhishingShieldTests
{
    private static PhishingShield Build(
        Func<string, string, CancellationToken, Task<string>>? chat = null,
        double phishThreshold = 0.80,
        double suspThreshold  = 0.55)
    {
        return new PhishingShield
        {
            ChatDelegate                  = chat,
            PhishingConfidenceThreshold   = phishThreshold,
            SuspiciousConfidenceThreshold = suspThreshold,
        };
    }

    private static PhishingShield.Signals SignalsRiskyLogin(string host = "evil-paypal.top") =>
        new(
            Host:                          host,
            PageTitle:                     "Sign in to PayPal",
            HasLoginForm:                  true,
            TlsValid:                      false,
            IsSelfSigned:                  true,
            LooksLikeBrandImpersonation:   true,
            LooksRandomGenerated:          false,
            HasSuspiciousTld:              true,
            DomainAgeDays:                 5);

    private static PhishingShield.Signals SignalsCleanGoogle() =>
        new(
            Host:                          "google.com",
            PageTitle:                     "Google",
            HasLoginForm:                  false,
            TlsValid:                      true,
            IsSelfSigned:                  false,
            LooksLikeBrandImpersonation:   false,
            LooksRandomGenerated:          false,
            HasSuspiciousTld:              false,
            DomainAgeDays:                 0);

    // ── Quick gate ────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_NoSuspiciousFlags_NoLoginForm_ShortCircuitsToSafe()
    {
        var calls = 0;
        var shield = Build(chat: (_, _, _) =>
        {
            calls++;
            return Task.FromResult("PHISHING|0.99|whatever");
        });

        var result = await shield.EvaluateAsync(SignalsCleanGoogle());

        Assert.Equal(PhishingShield.Verdict.Safe, result.Verdict);
        Assert.Equal(0, calls); // model never called
    }

    [Fact]
    public async Task EvaluateAsync_LoginFormAlone_TriggersModelCall()
    {
        var calls = 0;
        var shield = Build(chat: (_, _, _) =>
        {
            calls++;
            return Task.FromResult("SAFE|0.95|legitimate sign-in page");
        });

        var s = SignalsCleanGoogle() with { HasLoginForm = true };
        await shield.EvaluateAsync(s);

        Assert.Equal(1, calls);
    }

    // ── Prompt construction ──────────────────────────────────────────────

    [Fact]
    public void BuildPrompt_IncludesAllRedFlags()
    {
        var (sys, user) = PhishingShield.BuildPrompt(SignalsRiskyLogin(), maxTitleChars: 120);

        Assert.Contains("PHISHING", sys);
        Assert.Contains("SAFE", sys);
        Assert.Contains("VERDICT|CONFIDENCE|REASON", sys);

        Assert.Contains("evil-paypal.top", user);
        Assert.Contains("Sign in to PayPal", user);
        Assert.Contains("login form present", user);
        Assert.Contains("TLS invalid", user);
        Assert.Contains("brand-like host", user);
        Assert.Contains("suspicious TLD", user);
        Assert.Contains("5d old", user);
    }

    [Fact]
    public void BuildPrompt_TruncatesLongTitles()
    {
        var s = SignalsRiskyLogin() with { PageTitle = new string('x', 500) };
        var (_, user) = PhishingShield.BuildPrompt(s, maxTitleChars: 100);
        // Title line shows the truncated form with the ellipsis marker.
        Assert.Contains("…", user);
        Assert.DoesNotContain(new string('x', 200), user);
    }

    [Fact]
    public void BuildPrompt_NoFlags_StillEmitsFlagLine()
    {
        var s = SignalsCleanGoogle() with { HasLoginForm = true };
        var (_, user) = PhishingShield.BuildPrompt(s, maxTitleChars: 100);
        Assert.Contains("login form present", user);
    }

    // ── Reply parsing & confidence gating ────────────────────────────────

    [Fact]
    public void ParseReply_PhishingAboveThreshold_ReturnsPhishing()
    {
        var shield = Build(phishThreshold: 0.80, suspThreshold: 0.55);
        var r = shield.ParseReply("PHISHING|0.92|paypal-themed on fresh .top domain");
        Assert.Equal(PhishingShield.Verdict.Phishing, r.Verdict);
        Assert.Equal(0.92, r.Confidence, 2);
        Assert.Contains("paypal", r.Reason);
    }

    [Fact]
    public void ParseReply_PhishingBelowPhishThreshold_DowngradesToSuspicious()
    {
        var shield = Build(phishThreshold: 0.80, suspThreshold: 0.55);
        var r = shield.ParseReply("PHISHING|0.65|some red flags");
        Assert.Equal(PhishingShield.Verdict.Suspicious, r.Verdict); // 0.65 in [0.55, 0.80)
    }

    [Fact]
    public void ParseReply_PhishingBelowAllThresholds_DowngradesToSafe()
    {
        var shield = Build(phishThreshold: 0.80, suspThreshold: 0.55);
        var r = shield.ParseReply("PHISHING|0.40|maybe");
        Assert.Equal(PhishingShield.Verdict.Safe, r.Verdict);
    }

    [Fact]
    public void ParseReply_Suspicious_RequiresSuspThreshold()
    {
        var shield = Build(phishThreshold: 0.80, suspThreshold: 0.55);
        Assert.Equal(PhishingShield.Verdict.Suspicious,
            shield.ParseReply("SUSPICIOUS|0.60|odd").Verdict);
        Assert.Equal(PhishingShield.Verdict.Safe,
            shield.ParseReply("SUSPICIOUS|0.30|barely").Verdict);
    }

    [Fact]
    public void ParseReply_Safe_AlwaysSafe()
    {
        var shield = Build();
        var r = shield.ParseReply("SAFE|0.99|known good site");
        Assert.Equal(PhishingShield.Verdict.Safe, r.Verdict);
    }

    [Fact]
    public void ParseReply_HandlesWhitespaceAndCase()
    {
        var shield = Build(phishThreshold: 0.5, suspThreshold: 0.3);
        var r = shield.ParseReply("  phishing | 0.9 | tracker  ");
        Assert.Equal(PhishingShield.Verdict.Phishing, r.Verdict);
    }

    [Fact]
    public void ParseReply_StripsMarkdownFences()
    {
        var shield = Build(phishThreshold: 0.5, suspThreshold: 0.3);
        var r = shield.ParseReply("```\nPHISHING|0.9|x\n```");
        Assert.Equal(PhishingShield.Verdict.Phishing, r.Verdict);
    }

    [Fact]
    public void ParseReply_Malformed_DefaultsToSafe()
    {
        var shield = Build();
        Assert.Equal(PhishingShield.Verdict.Safe, shield.ParseReply("").Verdict);
        Assert.Equal(PhishingShield.Verdict.Safe, shield.ParseReply("nonsense").Verdict);
        Assert.Equal(PhishingShield.Verdict.Safe, shield.ParseReply("PHISHING").Verdict);
    }

    // ── Cache behaviour ──────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_CachesPerHostAndLoginFormFlag()
    {
        var calls = 0;
        var shield = Build(chat: (_, _, _) =>
        {
            calls++;
            return Task.FromResult("PHISHING|0.95|fresh fake paypal");
        });

        var s = SignalsRiskyLogin();
        var first  = await shield.EvaluateAsync(s);
        var second = await shield.EvaluateAsync(s);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EvaluateAsync_NoChatDelegate_ReturnsSafe_NoCache()
    {
        var shield = Build(chat: null);
        var r = await shield.EvaluateAsync(SignalsRiskyLogin());
        Assert.Equal(PhishingShield.Verdict.Safe, r.Verdict);
        Assert.Equal(0, shield.CacheCount);
    }

    [Fact]
    public async Task EvaluateAsync_AdapterThrows_ReturnsSafe_NoCache()
    {
        var shield = Build(chat: (_, _, _) => throw new InvalidOperationException("boom"));
        var r = await shield.EvaluateAsync(SignalsRiskyLogin());
        Assert.Equal(PhishingShield.Verdict.Safe, r.Verdict);
        Assert.Contains("shield error", r.Reason);
        Assert.Equal(0, shield.CacheCount);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyHost_ReturnsSafeImmediately()
    {
        var calls = 0;
        var shield = Build(chat: (_, _, _) => { calls++; return Task.FromResult("PHISHING|0.99|x"); });
        var r = await shield.EvaluateAsync(SignalsRiskyLogin() with { Host = "" });
        Assert.Equal(PhishingShield.Verdict.Safe, r.Verdict);
        Assert.Equal(0, calls);
    }
}
