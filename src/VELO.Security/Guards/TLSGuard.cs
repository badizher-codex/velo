using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VELO.Security.AI.Models;

namespace VELO.Security.Guards;

/// <summary>
/// TLSGuard — validates TLS security for each navigation.
///
/// Checks (in order of execution):
/// 1. QuickCheck (sync, called in NavigationStarting):
///    - HSTS preload: HTTP to an HSTS domain → redirect to HTTPS
/// 2. ServerCertificateErrorDetected (via BrowserTab hook):
///    - Self-signed on public site → WARN
///    - Expired cert → WARN
/// 3. CheckCTLogsAsync (async, non-blocking, 24h cache):
///    - Queries crt.sh — if domain has zero CT entries → WARN
/// </summary>
public class TLSGuard(ILogger<TLSGuard> logger)
{
    private readonly ILogger<TLSGuard> _logger = logger;

    // Shared HttpClient — single instance for CT log queries
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(800),
        DefaultRequestHeaders = { { "User-Agent", "VELO-Browser/2.0" } }
    };

    // CT log result cache: domain → (expiry, hasCtEntries)
    private readonly Dictionary<string, (DateTime Expiry, bool HasEntries)> _ctCache = new();
    private readonly SemaphoreSlim _ctLock = new(1, 1);

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Fired when a TLS issue is detected after navigation has already started.</summary>
    public event Action<string, SecurityVerdict>? ThreatDetected;

    // ── 1. Quick sync check — call in NavigationStarting ──────────────────

    public TlsQuickResult QuickCheck(string uri)
    {
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var url))
                return TlsQuickResult.Allow();

            // HTTP request to HSTS preload domain → upgrade to HTTPS
            if (url.Scheme == "http")
            {
                var host = url.Host.ToLowerInvariant();
                if (IsHSTSPreload(host))
                {
                    var httpsUrl = "https://" + uri[7..]; // strip "http://"
                    _logger.LogDebug("HSTS preload redirect: {Host}", host);
                    return TlsQuickResult.Redirect(httpsUrl);
                }
            }

            return TlsQuickResult.Allow();
        }
        catch
        {
            return TlsQuickResult.Allow();
        }
    }

    // ── 2. CT log check — async, non-blocking, 24h cache ─────────────────

    /// <summary>
    /// Queries crt.sh for the domain. If zero certificates found, fires ThreatDetected.
    /// Never throws — logs and returns silently on timeout or error.
    /// </summary>
    public async Task CheckCTLogsAsync(string domain, string originalUri)
    {
        if (string.IsNullOrEmpty(domain) || IsLocalDomain(domain)) return;

        // Deduplicate: strip leading "www."
        var key = domain.StartsWith("www.") ? domain[4..] : domain;

        // Check cache first (no lock needed for read)
        if (_ctCache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            if (!cached.HasEntries)
                ThreatDetected?.Invoke(originalUri, SecurityVerdict.Warn(
                    $"El dominio '{domain}' no tiene entradas en CT logs públicos (posible CA privada)",
                    ThreatType.MitM, "TLS"));
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            var url = $"https://crt.sh/?q={Uri.EscapeDataString(key)}&output=json";
            var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);

            bool hasEntries = false;
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                // crt.sh returns a JSON array; empty array = no CT entries
                hasEntries = body.TrimStart().StartsWith("[") && body.Length > 10;
            }

            await _ctLock.WaitAsync();
            try { _ctCache[key] = (DateTime.UtcNow.AddHours(24), hasEntries); }
            finally { _ctLock.Release(); }

            if (!hasEntries)
            {
                _logger.LogWarning("CT log check: no entries for {Domain}", domain);
                ThreatDetected?.Invoke(originalUri, SecurityVerdict.Warn(
                    $"El dominio '{domain}' no tiene entradas en CT logs públicos",
                    ThreatType.MitM, "TLS"));
            }
            else
            {
                _logger.LogDebug("CT check OK: {Domain}", domain);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("CT check timeout for {Domain} — skipping", domain);
            // Timeout: mark as OK to avoid blocking on every navigation
            await _ctLock.WaitAsync();
            try { _ctCache[key] = (DateTime.UtcNow.AddMinutes(5), true); }
            finally { _ctLock.Release(); }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CT check failed for {Domain}", domain);
        }
    }

    // ── 3. Certificate error handler ──────────────────────────────────────

    /// <summary>
    /// Call from ServerCertificateErrorDetected. Returns the verdict to apply.
    /// Pass allowSelfSigned=true for localhost/private IPs (dev scenario).
    /// </summary>
    public SecurityVerdict EvaluateCertError(string uri, bool isSelfSigned, bool isExpired, bool isPrivateHost)
    {
        if (isPrivateHost)
            return SecurityVerdict.Allow(); // localhost dev — allow

        if (isSelfSigned)
            return SecurityVerdict.Warn(
                "El sitio usa un certificado autofirmado — la identidad no está verificada por una CA pública",
                ThreatType.MitM, "TLS");

        if (isExpired)
            return SecurityVerdict.Warn(
                "El certificado TLS del sitio ha expirado — la conexión puede no ser segura",
                ThreatType.MitM, "TLS");

        return SecurityVerdict.Warn(
            "Error de certificado TLS — la identidad del servidor no puede verificarse",
            ThreatType.MitM, "TLS");
    }

    // ── HSTS preload list — top domains ──────────────────────────────────

    private static bool IsHSTSPreload(string host)
    {
        // Strip "www." for matching
        var bare = host.StartsWith("www.") ? host[4..] : host;
        return _hstsPreload.Contains(bare) || _hstsPreload.Contains(host);
    }

    private static bool IsLocalDomain(string domain)
        => domain is "localhost" or "127.0.0.1" or "::1"
           || domain.EndsWith(".local")
           || domain.EndsWith(".internal")
           || System.Net.IPAddress.TryParse(domain, out _);

    // Curated HSTS preload list — critical domains
    // Full Chromium list: ~100k entries. We embed the most impactful ~200.
    private static readonly HashSet<string> _hstsPreload = new(StringComparer.OrdinalIgnoreCase)
    {
        // Major platforms
        "google.com", "gmail.com", "youtube.com", "googlevideo.com", "gstatic.com",
        "googleapis.com", "googleusercontent.com", "google.es", "google.com.mx",
        "facebook.com", "instagram.com", "whatsapp.com", "messenger.com",
        "twitter.com", "x.com", "t.co",
        "microsoft.com", "live.com", "outlook.com", "hotmail.com", "office.com",
        "apple.com", "icloud.com", "appleid.apple.com",
        "amazon.com", "amazon.es", "amazon.com.mx", "aws.amazon.com",
        "github.com", "gitlab.com", "bitbucket.org",

        // Banking & payments
        "paypal.com", "stripe.com", "square.com",
        "bankofamerica.com", "chase.com", "wellsfargo.com", "citi.com",
        "santander.com", "bbva.com", "bancomer.com", "hsbc.com",

        // News & media
        "bbc.com", "bbc.co.uk", "nytimes.com", "theguardian.com",
        "elpais.com", "elmundo.es", "abc.es", "lavanguardia.com",

        // Cloud & infra
        "cloudflare.com", "fastly.com", "akamai.com", "netlify.com", "vercel.com",
        "digitalocean.com", "heroku.com", "render.com",

        // Dev tools
        "npmjs.com", "pypi.org", "rubygems.org", "crates.io", "nuget.org",
        "docker.com", "hub.docker.com",
        "stackoverflow.com", "stackexchange.com",

        // Privacy-focused
        "proton.me", "protonmail.com", "signal.org", "tutanota.com",
        "duckduckgo.com", "startpage.com",
        "mozilla.org", "firefox.com", "torproject.org",

        // Shopping
        "ebay.com", "etsy.com", "shopify.com", "aliexpress.com",

        // Social
        "linkedin.com", "reddit.com", "pinterest.com", "tiktok.com",
        "twitch.tv", "discord.com", "slack.com", "zoom.us", "teams.microsoft.com",

        // Other frequently visited
        "wikipedia.org", "wikimedia.org",
        "netflix.com", "spotify.com", "twitch.tv",
        "dropbox.com", "box.com", "onedrive.live.com",
        "wordpress.com", "medium.com", "substack.com",
    };
}

// ── Result types ──────────────────────────────────────────────────────────────

public class TlsQuickResult
{
    public bool ShouldRedirect { get; private init; }
    public string? RedirectUrl  { get; private init; }

    public static TlsQuickResult Allow()                => new() { ShouldRedirect = false };
    public static TlsQuickResult Redirect(string url)   => new() { ShouldRedirect = true, RedirectUrl = url };
}
