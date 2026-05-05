using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Data.Models;
using VELO.Vault.Security;

namespace VELO.Vault;

/// <summary>
/// Phase 3 / Sprint 5 — Backs the right-click "Autofill" overlay injected
/// into every page. Looks up credentials in the Vault by domain (eTLD+1
/// match plus exact host match), verifies new passwords against HIBP,
/// and provides an idempotent SaveNewCredential path so submitting the
/// same form twice doesn't double-up the Vault.
///
/// HIBP is opt-in via <see cref="HibpEnabled"/>; with the toggle off, no
/// network call is ever made — including no DNS lookup for HIBP.
/// </summary>
public sealed class AutofillService
{
    private readonly VaultService _vault;
    private readonly HibpClient _hibp;
    private readonly ILogger<AutofillService> _logger;

    public AutofillService(
        VaultService vault,
        HibpClient? hibp = null,
        ILogger<AutofillService>? logger = null)
    {
        _vault  = vault;
        _hibp   = hibp ?? new HibpClient();
        _logger = logger ?? NullLogger<AutofillService>.Instance;
    }

    /// <summary>Caller-supplied toggle. When false, <see cref="CheckBreachAsync"/> short-circuits to Clean.</summary>
    public bool HibpEnabled { get; set; } = true;

    /// <summary>
    /// Returns every credential whose stored URL matches the given navigation
    /// host. Match is host-suffix on the eTLD+1 of the navigation host plus a
    /// stored entry that's either equal or a parent of the host. Subdomains
    /// of "google.com" share creds with "google.com" itself, but not with
    /// unrelated sites.
    /// </summary>
    public async Task<IReadOnlyList<AutofillSuggestion>> GetSuggestionsAsync(string navigationHost)
    {
        if (string.IsNullOrEmpty(navigationHost)) return [];
        if (!_vault.IsUnlocked) return [];

        var navEtld = GetEtldPlusOne(navigationHost);

        var all = await _vault.GetAllAsync();
        var matches = all
            .Where(e => MatchesDomain(e.Url, navigationHost, navEtld))
            .Select(e => new AutofillSuggestion(
                Id:       e.Id,
                Username: e.Username,
                SiteName: e.SiteName,
                Url:      e.Url,
                IsExactHostMatch: HostFromUrl(e.Url).Equals(navigationHost, StringComparison.OrdinalIgnoreCase)))
            // Exact host match first so dropdown ordering is intuitive.
            .OrderByDescending(s => s.IsExactHostMatch)
            .ThenBy(s => s.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches;
    }

    /// <summary>
    /// Resolves a suggestion id to the full credential (including password).
    /// Returns null when the entry has been deleted or doesn't belong to the
    /// caller's host — defensive against a stale id from the JS bridge.
    /// </summary>
    public async Task<PasswordEntry?> ResolveCredentialAsync(string id, string navigationHost)
    {
        if (string.IsNullOrEmpty(id) || !_vault.IsUnlocked) return null;
        var all = await _vault.GetAllAsync();
        var navEtld = GetEtldPlusOne(navigationHost);
        return all.FirstOrDefault(e =>
            e.Id == id && MatchesDomain(e.Url, navigationHost, navEtld));
    }

    /// <summary>
    /// Saves a credential captured at form-submit. Idempotent — if the same
    /// (host, username, password) tuple is already in the Vault, returns
    /// <see cref="SaveOutcome.Duplicate"/> without writing.
    /// </summary>
    public async Task<SaveOutcome> SaveNewCredentialAsync(
        string domain, string username, string password, bool autoDetected = true)
    {
        if (!_vault.IsUnlocked) return SaveOutcome.VaultLocked;
        if (string.IsNullOrEmpty(password)) return SaveOutcome.Empty;

        var all = await _vault.GetAllAsync();
        var navHost = NormalizeHost(domain);
        var existing = all.FirstOrDefault(e =>
            HostFromUrl(e.Url).Equals(navHost, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (existing.Password == password) return SaveOutcome.Duplicate;

            // Same host + username but different password → user likely rotated.
            // Update in place rather than queue a second entry.
            existing.Password   = password;
            existing.ModifiedAt = DateTime.UtcNow;
            await _vault.SaveAsync(existing);
            return SaveOutcome.Updated;
        }

        await _vault.SaveAsync(new PasswordEntry
        {
            SiteName = navHost,
            Url      = "https://" + navHost,
            Username = username,
            Password = password,
            Notes    = autoDetected
                ? $"Captured from form submit on {DateTime.Now:yyyy-MM-dd}"
                : null,
        });
        return SaveOutcome.Created;
    }

    /// <summary>
    /// Calls HIBP via k-anonymity. Returns Clean when <see cref="HibpEnabled"/>
    /// is false or the network call fails — never blocks save on a HIBP error.
    /// </summary>
    public async Task<BreachStatus> CheckBreachAsync(string password, CancellationToken ct = default)
    {
        if (!HibpEnabled || string.IsNullOrEmpty(password)) return BreachStatus.Clean;

        try
        {
            var count = await _hibp.GetBreachCountAsync(password, ct).ConfigureAwait(false);
            return new BreachStatus(count, count > 0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HIBP check failed — treating as unknown");
            return BreachStatus.Clean;
        }
    }

    // ── Pure helpers (unit-testable) ──────────────────────────────────

    /// <summary>True when the stored URL's host is the navigation host or one of its parent eTLD+1 ancestors.</summary>
    public static bool MatchesDomain(string storedUrl, string navHost, string navEtldPlusOne)
    {
        if (string.IsNullOrEmpty(storedUrl) || string.IsNullOrEmpty(navHost)) return false;
        var storedHost = HostFromUrl(storedUrl);
        if (string.IsNullOrEmpty(storedHost)) return false;

        // Exact match wins.
        if (storedHost.Equals(navHost, StringComparison.OrdinalIgnoreCase)) return true;

        // eTLD+1 match: stored "google.com" should fill on "mail.google.com" but
        // NOT on "evil-google.com" (different eTLD+1) or on a sibling subdomain
        // like "evil.com" (different eTLD+1 entirely).
        var storedEtld = GetEtldPlusOne(storedHost);
        if (!storedEtld.Equals(navEtldPlusOne, StringComparison.OrdinalIgnoreCase)) return false;

        // The nav host must end with the stored host (so stored="google.com"
        // matches nav="mail.google.com" but stored="evil.google.com" doesn't
        // match nav="mail.google.com" — only equal-or-parent sub-domains
        // share credentials).
        return navHost.EndsWith("." + storedHost, StringComparison.OrdinalIgnoreCase) ||
               storedHost.EndsWith("." + navHost, StringComparison.OrdinalIgnoreCase) ||
               storedHost.Equals(navEtldPlusOne, StringComparison.OrdinalIgnoreCase);
    }

    public static string HostFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try { return new Uri(url).Host.ToLowerInvariant(); }
        catch
        {
            // Stored URLs may be host-only (no scheme) — recover.
            try { return new Uri("https://" + url).Host.ToLowerInvariant(); }
            catch { return url.ToLowerInvariant(); }
        }
    }

    public static string GetEtldPlusOne(string host)
    {
        if (string.IsNullOrEmpty(host)) return host;
        var clean = NormalizeHost(host);
        var parts = clean.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : clean;
    }

    public static string NormalizeHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return "";
        var h = host.ToLowerInvariant();
        if (h.StartsWith("www.")) h = h[4..];
        return h;
    }
}

/// <summary>One row in the autofill dropdown.</summary>
public sealed record AutofillSuggestion(
    string Id,
    string Username,
    string SiteName,
    string Url,
    bool   IsExactHostMatch);

/// <summary>Outcome of <see cref="AutofillService.SaveNewCredentialAsync"/>.</summary>
public enum SaveOutcome
{
    Created,
    Updated,
    Duplicate,
    Empty,
    VaultLocked,
}
