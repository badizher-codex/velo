using VELO.Vault;
using Xunit;

namespace VELO.Vault.Tests;

/// <summary>
/// Pure-helper tests for <see cref="AutofillService"/>. The instance-level
/// behaviour (GetSuggestionsAsync / SaveNewCredentialAsync) is covered
/// indirectly here through <see cref="AutofillService.MatchesDomain"/>,
/// which is the single predicate those methods use for filtering. Full
/// integration-level coverage will land alongside the WebView2 wiring
/// once an in-memory VaultService fixture exists.
/// </summary>
public class AutofillServiceTests
{
    // ── Spec § 5.7 #1 — credentials returned for the exact host ──────────

    [Fact]
    public void MatchesDomain_ReturnsTrue_ForExactHost()
    {
        Assert.True(AutofillService.MatchesDomain(
            storedUrl:       "https://github.com/login",
            navHost:         "github.com",
            navEtldPlusOne:  "github.com"));
    }

    [Fact]
    public void MatchesDomain_ReturnsTrue_ForExactHost_HostOnlyStoredUrl()
    {
        // Some legacy entries are stored without the scheme.
        Assert.True(AutofillService.MatchesDomain(
            storedUrl:       "github.com",
            navHost:         "github.com",
            navEtldPlusOne:  "github.com"));
    }

    // ── Spec § 5.7 #2 — subdomain policy ─────────────────────────────────

    [Fact]
    public void MatchesDomain_AllowsSubdomain_OfStoredHost()
    {
        // Stored google.com → fill on mail.google.com.
        Assert.True(AutofillService.MatchesDomain(
            storedUrl:       "https://google.com",
            navHost:         "mail.google.com",
            navEtldPlusOne:  "google.com"));
    }

    [Fact]
    public void MatchesDomain_AllowsParentDomain_FromStoredSubdomain()
    {
        // Stored mail.google.com → fill on google.com (sibling promotion).
        Assert.True(AutofillService.MatchesDomain(
            storedUrl:       "https://mail.google.com",
            navHost:         "google.com",
            navEtldPlusOne:  "google.com"));
    }

    // ── Spec § 5.7 #3 — cross-domain leakage MUST be blocked ─────────────

    [Fact]
    public void MatchesDomain_BlocksUnrelatedDomain()
    {
        Assert.False(AutofillService.MatchesDomain(
            storedUrl:       "https://google.com",
            navHost:         "bing.com",
            navEtldPlusOne:  "bing.com"));
    }

    [Fact]
    public void MatchesDomain_BlocksLookalikeDomain()
    {
        // "evil-google.com" has eTLD+1 = "evil-google.com", not "google.com".
        Assert.False(AutofillService.MatchesDomain(
            storedUrl:       "https://google.com",
            navHost:         "evil-google.com",
            navEtldPlusOne:  "evil-google.com"));
    }

    [Fact]
    public void MatchesDomain_BlocksSiblingSubdomain_OnSameEtldPlusOne()
    {
        // Stored evil.google.com → MUST NOT fill on mail.google.com.
        // Same eTLD+1 but stored is not equal-or-parent of nav.
        Assert.False(AutofillService.MatchesDomain(
            storedUrl:       "https://evil.google.com",
            navHost:         "mail.google.com",
            navEtldPlusOne:  "google.com"));
    }

    [Fact]
    public void MatchesDomain_HandlesEmptyOrNullInputs()
    {
        Assert.False(AutofillService.MatchesDomain("", "github.com",  "github.com"));
        Assert.False(AutofillService.MatchesDomain("https://github.com", "", ""));
    }

    // ── Helper: HostFromUrl ──────────────────────────────────────────────

    [Fact]
    public void HostFromUrl_ExtractsHost_FromFullUrl()
    {
        Assert.Equal("github.com",      AutofillService.HostFromUrl("https://github.com/path?q=1"));
        Assert.Equal("mail.google.com", AutofillService.HostFromUrl("HTTPS://Mail.Google.COM/inbox"));
    }

    [Fact]
    public void HostFromUrl_RecoversHost_FromSchemelessUrl()
    {
        Assert.Equal("github.com", AutofillService.HostFromUrl("github.com"));
    }

    // ── Helper: GetEtldPlusOne ───────────────────────────────────────────

    [Fact]
    public void GetEtldPlusOne_ReturnsLastTwoLabels()
    {
        Assert.Equal("google.com", AutofillService.GetEtldPlusOne("mail.google.com"));
        Assert.Equal("google.com", AutofillService.GetEtldPlusOne("a.b.c.google.com"));
        Assert.Equal("google.com", AutofillService.GetEtldPlusOne("google.com"));
    }

    [Fact]
    public void GetEtldPlusOne_StripsWwwPrefix()
    {
        Assert.Equal("google.com", AutofillService.GetEtldPlusOne("www.google.com"));
    }

    // ── Helper: NormalizeHost ────────────────────────────────────────────

    [Fact]
    public void NormalizeHost_LowercasesAndStripsWww()
    {
        Assert.Equal("github.com", AutofillService.NormalizeHost("WWW.GitHub.COM"));
        Assert.Equal("github.com", AutofillService.NormalizeHost("github.com"));
        Assert.Equal("",           AutofillService.NormalizeHost(""));
    }

    // ── Spec § 5.7 #4 — SaveNewCredential dedup logic via DedupKey shape ─
    // The full SaveNewCredentialAsync test needs a VaultService instance
    // (DB-backed) and lives in the integration-test pass. The static
    // helpers above cover everything that doesn't require I/O.
}
