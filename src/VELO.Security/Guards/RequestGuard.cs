using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VELO.Security.AI.Models;
using VELO.Security.Rules;
using VELO.Security.Sentinel;

namespace VELO.Security.Guards;

public class RequestGuard(
    BlocklistManager blocklist,
    ILogger<RequestGuard> logger,
    SmartBlockClassifier? smartBlock = null,
    SentinelClassifier? sentinel = null)
{
    private readonly BlocklistManager _blocklist = blocklist;
    private readonly ILogger<RequestGuard> _logger = logger;
    // S-C — VELO Sentinel sits behind the exact blocklists (they're faster and
    // have no false-positive surface) and in front of the optional HTTP path.
    // Evaluate is sync and inference costs ~9 ms, so this reads the classifier's
    // cache and queues a prefetch on a miss — the next request to the same host
    // gets the verdict. Same shape as SmartBlockClassifier above, for the same
    // reason: nothing may sit on the request path waiting for a model.
    private readonly SentinelClassifier? _sentinel = sentinel;
    // v2.4.22 — Sprint 8A wire. SmartBlockClassifier is async by design,
    // so we don't await here — sync Evaluate consults the classifier's
    // existing cache for previously-seen hosts. The async classification
    // is kicked off from the call-site (BrowserTab.OnWebResourceRequested)
    // for hosts not yet classified, which populates the cache so the
    // next request to the same host gets the verdict instantly.
    private readonly SmartBlockClassifier? _smartBlock = smartBlock;

    private static readonly HashSet<string> _userWhitelist = [];

    private static readonly Regex _trackingBeaconPattern = new(
        @"\.(gif|png)\?.*utm_|/beacon\?|/pixel\?|/track\?|1x1\.gif|/log\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Trusted hosting / CDN domains — skip AI and suspicious-params checks entirely.
    // These domains use long AWS S3 pre-signed URLs that would otherwise trigger false positives.
    // Also exposed publicly so MalwaredexRepository can purge historical false-positive entries.
    public static readonly HashSet<string> TrustedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // GitHub and its asset CDN — also covers any *.github.io project page
        // because TrustedHosts is checked against the eTLD+1 (GetRootDomain).
        "github.com", "www.github.com", "github.io",
        "githubusercontent.com", "objects.githubusercontent.com",
        "codeload.github.com", "github-releases.githubusercontent.com",
        "raw.githubusercontent.com", "avatars.githubusercontent.com",
        // Microsoft / NuGet
        "microsoft.com", "www.microsoft.com", "nuget.org", "api.nuget.org",
        // Package registries
        "npmjs.com", "registry.npmjs.org", "pypi.org", "files.pythonhosted.org",
        // Generic trusted CDNs
        "cloudflare.com", "cdn.cloudflare.com",
        "fastly.net", "akamai.net", "akamaized.net",
        // S-C — public script/asset CDNs. Same class as the entries above and
        // they belong here regardless of Sentinel, but the S-C runtime check is
        // what surfaced the gap: model-v1 calls cdn.jsdelivr.net an ad at
        // p=0.92 (a CDN's traffic pattern looks like an ad network's from the
        // host alone), and half the web loads its scripts from these.
        "jsdelivr.net", "cdnjs.cloudflare.com", "unpkg.com",
        "bootstrapcdn.com", "jquery.com", "gstatic.com",
    };

    // AWS S3 / CDN signing parameter names — long by design, never a sign of exfiltration
    private static readonly HashSet<string> _signingParamNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-Amz-Signature", "X-Amz-Credential", "X-Amz-Security-Token",
        "X-Amz-Algorithm", "X-Amz-Date", "X-Amz-Expires",
        "X-Amz-SignedHeaders", "X-Goog-Signature", "X-Goog-Credential",
        "Signature", "Policy", "Key-Pair-Id", "token", "access_token",
        "response-content-disposition", "response-content-type",
    };

    // TLDs commonly abused for phishing/malware (free or unregulated)
    private static readonly HashSet<string> _suspiciousTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "xyz", "tk", "ml", "ga", "cf", "gq", "top", "work", "loan",
        "click", "download", "zip", "mov", "cam", "live", "fun",
        "icu", "buzz", "cyou", "cfd", "sbs", "bar", "monster"
    };

    // Well-known brands — homograph detection
    private static readonly string[] _brandKeywords =
    [
        "paypal", "google", "microsoft", "apple", "amazon", "facebook",
        "instagram", "twitter", "netflix", "bank", "secure", "login",
        "account", "verify", "update", "confirm"
    ];

    // v2.0.5 — Extensions that almost always indicate a file download.
    // RequestGuard skips heuristic blocking for these so that DownloadGuard
    // (which sees the actual response mimetype) gets the final say. Without
    // this bypass, any URL on a "suspicious" host that happens to point at an
    // installer / archive / 3D-print model is killed before WebView2 can fire
    // DownloadStarting — which is how Bambu Studio updates and MakerWorld STL
    // downloads were getting silently dropped.
    private static readonly HashSet<string> _downloadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".dmg", ".pkg", ".deb", ".rpm",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz",
        ".iso", ".img",
        ".stl", ".3mf", ".obj", ".step", ".stp", ".gcode", ".bgcode",
        ".pdf", ".epub", ".mobi",
        ".mp3", ".mp4", ".m4a", ".mkv", ".webm", ".avi", ".mov", ".flac", ".wav",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".apk", ".ipa", ".jar"
    };

    private static bool LooksLikeDownload(Uri url)
    {
        var path = url.AbsolutePath;
        var dotIdx = path.LastIndexOf('.');
        if (dotIdx <= 0 || dotIdx == path.Length - 1) return false;
        var ext = path[dotIdx..];
        return _downloadExtensions.Contains(ext);
    }

    /// <summary>
    /// Evaluates a request. Every non-Allow verdict is logged with the rule that
    /// produced it — v2.4.62: the primevideo.com false positive took a full
    /// session to attribute because only two of the nine rules logged anything
    /// (lesson #7: a guard that can't say why it blocked can't be debugged).
    /// </summary>
    public SecurityVerdict Evaluate(string uri, string? referrer, string resourceType)
    {
        var verdict = EvaluateCore(uri, referrer, resourceType);

        if (verdict.Verdict != VerdictType.Safe)
        {
            _logger.LogInformation(
                "RequestGuard {Verdict} [{Source}] {Uri} (type {Type}, referrer {Referrer}): {Reason}",
                verdict.Verdict, verdict.Source, uri, resourceType, referrer ?? "", verdict.Reason);
        }

        return verdict;
    }

    private SecurityVerdict EvaluateCore(string uri, string? referrer, string resourceType)
    {
        Uri? url;
        try { url = new Uri(uri); }
        catch { return SecurityVerdict.Allow(); }

        var host = url.Host.ToLowerInvariant();

        // v2.4.62 P2-A. Main-frame vs sub-resource, and first-party vs third-party.
        // Both gate the heuristic tracker rules below. WebView2 reports main-frame
        // and iframe navigation alike as "Document"; the NavGuard call-site passes
        // "Document" too.
        var isMainFrame = resourceType.Equals("Document", StringComparison.OrdinalIgnoreCase)
                       || resourceType.Equals("Other",    StringComparison.OrdinalIgnoreCase);
        var isFirstParty = IsFirstParty(host, referrer);

        // 1. User whitelist
        if (_userWhitelist.Contains(host))
            return SecurityVerdict.Allow();

        // 1a (v2.0.5). Document-type navigation that resolves to a binary download.
        //              Let it through so DownloadGuard can decide on real content-type.
        if (resourceType == "Document" && LooksLikeDownload(url))
        {
            _logger.LogDebug("RequestGuard bypass: download extension detected → {Uri}", uri);
            return SecurityVerdict.Allow();
        }

        // 1b. Trusted CDN / hosting domains — skip AI and suspicious-param checks entirely.
        //     These domains use AWS S3 pre-signed URLs with long params by design.
        if (TrustedHosts.Contains(host) || TrustedHosts.Contains(GetRootDomain(host)))
            return SecurityVerdict.Allow();

        // 2. Blocklist (O(1)). Applies to first-party too — the list is exact-match,
        //    so there's no false-positive surface, and a site that IS a tracker
        //    shouldn't get a pass for loading its own resources.
        if (_blocklist.IsBlocked(host))
            return SecurityVerdict.Block("Dominio en blocklist de rastreadores conocidos", ThreatType.KnownTracker, "BLOCKLIST");

        // 2b (S-C, scoped in v2.4.69). VELO Sentinel — the embedded classifier,
        //     for the tail the exact lists never saw (fresh lookalikes, zero-day
        //     phishing). AFTER the blocklist: an exact match is cheaper and
        //     cannot be wrong, so it wins. BEFORE the heuristics and SmartBlock:
        //     this is the offline always-on path, the HTTP one stays opt-in.
        //
        //     The scope below came out of the first real shadow session. 75
        //     hosts from ordinary browsing, and the model wanted to block
        //     cart-mf.cinepolis.com, myaccount.ea.com, stories.duolingo.com,
        //     merchantpool1.linkedin.com — a site's own app subdomains, while
        //     the user was on that site. A site is never its own tracker; the
        //     same conclusion v2.4.62 P2-A reached for SmartBlock, which this
        //     rule should have inherited from the start and did not.
        //
        //     Main-frame is scoped differently rather than skipped. Sentinel
        //     exists for zero-day phishing, and that arrives as a top-level
        //     navigation — but AISecurityEngine (the other Sentinel call-site)
        //     is only reached when RequestGuard already raised NeedsAI, i.e.
        //     when a heuristic fired, which is exactly the tail Sentinel is
        //     supposed to cover on its own. So main-frame keeps the classifier,
        //     restricted to Phishing: navigating TO a tracker or ad domain is
        //     the user deciding to go there, not a threat to cancel.
        if (_sentinel is not null && !isFirstParty)
        {
            var sentinelVerdict = _sentinel.TryGetCachedVerdict(host);
            if (sentinelVerdict is null)
            {
                // Context travels into the one-time verdict log so the shadow
                // record says what kind of request produced it — without it the
                // log cannot distinguish a third-party beacon from a main-frame
                // navigation, and S-E has to guess.
                _sentinel.Prefetch(host, isMainFrame ? $"main-frame {resourceType}" : $"third-party {resourceType}");
            }
            else if (sentinelVerdict.Action == SentinelAction.Block &&
                     _sentinel.Mode == SentinelMode.Enforce &&
                     (!isMainFrame || sentinelVerdict.Label == SentinelLabel.Phishing))
            {
                return SecurityVerdict.Block(sentinelVerdict.Reason, ToThreatType(sentinelVerdict.Label), "SENTINEL");
            }
        }

        // 3+4 (v2.4.64). Local / private targets — SSRF and DNS rebinding.
        //
        // These used to be two rules, and the first one blocked `localhost`,
        // `0.0.0.0` and any `*.local` host UNCONDITIONALLY as "DNS rebinding".
        // That is not what DNS rebinding is (a *public* name resolving to a
        // private address), and it meant VELO refused to open http://localhost
        // at all — a dev server, a local dashboard, a NAS at nas.local.
        //
        // The real signal is who is asking: a public page reaching for a private
        // address is SSRF/rebinding; the user typing localhost (no referrer), or
        // a local page loading its own assets, is ordinary work.
        if (IsLocalOrPrivateTarget(host) && !string.IsNullOrEmpty(referrer) && !IsLocalPage(referrer))
            return SecurityVerdict.Block(
                "Página externa pidiendo un recurso local o de red privada (SSRF / DNS rebinding)",
                ThreatType.SSRF, "LOCALTARGET");

        // 5. Suspicious URL params — third-party only (v2.4.62 P2-A). A site's own
        //    URLs routinely carry long opaque state: primevideo.com/detail/<id>?jic=
        //    <base64 blob>&ref_=... is a catalogue page, not exfiltration. Sending
        //    data to *yourself* is not exfiltration by definition.
        if (!isFirstParty && HasSuspiciousUrlParams(url))
            return SecurityVerdict.Warn("URL contiene parámetros sospechosos de exfiltración", ThreatType.DataExfiltration, "PARAMS");

        // 6. Tracking beacons — third-party only (v2.4.62 P2-A). The pattern
        //    (/log?, /track?, /pixel?) matches plenty of first-party app endpoints.
        if (!isFirstParty && _trackingBeaconPattern.IsMatch(uri))
            return SecurityVerdict.Block("Tracking beacon detectado", ThreatType.Tracker, "BEACON");

        // 7. Mixed content (HTTP request from HTTPS page)
        if (uri.StartsWith("http://") && referrer?.StartsWith("https://") == true)
            return SecurityVerdict.Warn("Contenido mixto HTTP desde página HTTPS", ThreatType.MixedContent);

        // 8. Only send to AI if the domain looks genuinely suspicious:
        //    - suspicious TLD, brand impersonation, or random-generated hostname
        //    - only for main-frame navigation (WebView2 calls this "Document")
        //    - not for sub-resources (Image, Script, Stylesheet, Font, etc.)
        if (isMainFrame && (HasSuspiciousTld(host) || LooksLikeBrandImpersonation(host) || LooksRandomGenerated(host)))
            return SecurityVerdict.NeedsAI();

        // 9 (v2.4.22). SmartBlock second-pass — async classifier verdict from a
        //              previous request to this host. Caller (BrowserTab) fires
        //              the async classification when the cache misses; on the
        //              next request we read its verdict here.
        //
        // v2.4.62 P2-A — sub-resources only, third-party only. The classifier is
        // specified for sub-resources (see its class doc) and the call-site only
        // ever queues those, so applying its verdict to a top-level navigation
        // was asymmetric: one XHR to www.primevideo.com getting classified as a
        // tracker would then cancel every /detail/ page-load on that host.
        // A small local model returning BLOCK for a first-party host must never
        // be able to make the site unreachable.
        if (!isMainFrame && !isFirstParty)
        {
            var smartVerdict = _smartBlock?.TryGetCachedVerdict(host);
            if (smartVerdict?.Verdict == SmartBlockClassifier.Verdict.Block)
            {
                return SecurityVerdict.Block(
                    $"SmartBlock: {smartVerdict.Reason}",
                    ThreatType.Tracker,
                    "SmartBlock");
            }
        }

        return SecurityVerdict.Allow();
    }

    /// <summary>S-C — maps a Sentinel label onto the ThreatType vocabulary the
    /// threats panel and Malwaredex already speak. Ads and trackers land on the
    /// same bucket on purpose: to the user, an ad network IS a tracker.</summary>
    internal static ThreatType ToThreatType(SentinelLabel label) => label switch
    {
        SentinelLabel.Phishing => ThreatType.Phishing,
        SentinelLabel.Tracker  => ThreatType.Tracker,
        SentinelLabel.Ad       => ThreatType.Tracker,
        _                      => ThreatType.Other,
    };

    /// <summary>
    /// v2.4.62 P2-A — true when the request host and the page that issued it share
    /// a registrable root (www.primevideo.com ↔ primevideo.com). A site is never
    /// its own third-party tracker, so the heuristic tracker rules (suspicious
    /// params, beacon pattern, SmartBlock) don't apply to its own requests. An
    /// absent referrer is treated as third-party — same behaviour as before.
    /// </summary>
    public static bool IsFirstParty(string host, string? referrer)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrWhiteSpace(referrer)) return false;
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var refUri)) return false;

        var refHost = refUri.Host.ToLowerInvariant();
        if (refHost.Length == 0) return false;

        return GetRootDomain(host).Equals(GetRootDomain(refHost), StringComparison.OrdinalIgnoreCase);
    }

    public static void AddToWhitelist(string host) => _userWhitelist.Add(host.ToLowerInvariant());
    public static void RemoveFromWhitelist(string host) => _userWhitelist.Remove(host.ToLowerInvariant());
    /// <summary>v2.4.60 A4 — exposed so the TLS cert-error handler can honour a
    /// "Whitelist always" override the user granted from the security panel.</summary>
    public static bool IsUserWhitelisted(string host) => _userWhitelist.Contains(host.ToLowerInvariant());

    /// <summary>v2.4.22 — exposed so AISecurityEngine can populate PhishingShield signals.</summary>
    public static bool HasSuspiciousTld(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2 && _suspiciousTlds.Contains(parts[^1]);
    }

    /// <summary>v2.4.22 — exposed so AISecurityEngine can populate PhishingShield signals.</summary>
    public static bool LooksLikeBrandImpersonation(string host)
    {
        // e.g. paypa1.com, g00gle-login.net, microsoft-secure.xyz
        var lower = host.Replace("-", "").Replace(".", "");
        foreach (var brand in _brandKeywords)
        {
            if (lower.Contains(brand))
            {
                // Allowed: exact known-good domains like google.com, paypal.com
                if (host == $"{brand}.com" || host == $"www.{brand}.com") return false;
                return true;
            }
        }
        return false;
    }

    /// <summary>v2.4.22 — exposed so AISecurityEngine can populate PhishingShield signals.</summary>
    public static bool LooksRandomGenerated(string host)
    {
        // Check the second-level domain (e.g. "toruftuiov" from my.toruftuiov.com or toruftuiov.com)
        var parts = host.Split('.');
        // SLD is parts[^2] (before TLD), e.g. "toruftuiov" from "my.toruftuiov.com"
        var sld = parts.Length >= 2 ? parts[^2] : parts[0];
        if (sld.Length < 6) return false;

        var digits  = sld.Count(char.IsDigit);
        var letters = sld.Count(char.IsLetter);
        var hyphens = sld.Count(c => c == '-');

        // High digit ratio → suspicious (a3b7f2c1)
        if (digits > 3 && digits >= letters) return true;
        // Many hyphens → suspicious (abc-def-ghi-123)
        if (hyphens >= 3) return true;
        // High consonant cluster with no vowels → random string (toruftuiov, xkqdpzm)
        var vowels = sld.Count(c => "aeiouAEIOU".Contains(c));
        var consonants = letters - vowels;
        if (letters >= 7 && vowels > 0 && (double)consonants / letters > 0.72) return true;

        return false;
    }

    private static bool HasSuspiciousUrlParams(Uri url)
    {
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        foreach (string? key in query.Keys)
        {
            // Skip well-known CDN / cloud-storage signing parameters — always long by design
            if (key is not null && _signingParamNames.Contains(key)) continue;

            var value = query[key] ?? "";
            if (value.Length > 50 && IsBase64(value)) return true;
            if (Regex.IsMatch(value, @"[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase)) return true;
            // Raise threshold: 200 chars to reduce false positives on legitimate long params
            if (value.Length > 200) return true;
        }
        return false;
    }

    // v2.4.62 — Second-level public suffixes. Without these, GetRootDomain("bbc.co.uk")
    // returns "co.uk", which would make every *.co.uk site first-party to every other
    // one — and first-party now grants a heuristics bypass (IsFirstParty). Not the full
    // PSL (that's a 200 KB dependency); just the registries actually seen in the wild.
    private static readonly HashSet<string> _secondLevelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "net.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp",
        "com.br", "net.br", "org.br", "gov.br",
        "com.mx", "org.mx", "gob.mx",
        "com.ar", "net.ar", "org.ar", "gob.ar",
        "co.nz", "net.nz", "org.nz", "govt.nz",
        "co.in", "net.in", "org.in", "gov.in",
        "com.tr", "com.cn", "com.hk", "com.sg", "com.tw",
        "co.kr", "co.za", "co.il", "com.co", "com.pe", "com.ve",
        "co.id", "com.my", "com.ph", "com.vn", "com.es", "com.pl", "com.ua",
    };

    /// <summary>Returns the registrable root domain (e.g. "githubusercontent.com" from "objects.githubusercontent.com").</summary>
    private static string GetRootDomain(string host)
    {
        var parts = host.Split('.');
        if (parts.Length < 2) return host;

        var lastTwo = $"{parts[^2]}.{parts[^1]}";
        if (parts.Length >= 3 && _secondLevelSuffixes.Contains(lastTwo))
            return $"{parts[^3]}.{lastTwo}";

        return lastTwo;
    }

    private static bool IsBase64(string s)
    {
        if (s.Length % 4 != 0) return false;
        return Regex.IsMatch(s, @"^[A-Za-z0-9+/]*={0,3}$");
    }

    private static bool IsPrivateIp(string host)
    {
        if (!IPAddress.TryParse(host, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               bytes[0] == 127;
    }

    /// <summary>v2.4.64 — Host that lives on this machine or the local network:
    /// loopback names, mDNS <c>.local</c>, and RFC1918 / loopback literals.</summary>
    public static bool IsLocalOrPrivateTarget(string host)
        => host is "localhost" or "0.0.0.0" or "::1"
        || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || IsPrivateIp(host);

    /// <summary>True when the referring page itself is local — then a local
    /// sub-resource is same-network traffic, not a cross-boundary request.</summary>
    private static bool IsLocalPage(string referrer)
    {
        try
        {
            var uri = new Uri(referrer);
            // file:// pages have an empty host and are as local as it gets.
            return uri.IsFile || string.IsNullOrEmpty(uri.Host) || IsLocalOrPrivateTarget(uri.Host.ToLowerInvariant());
        }
        catch { return false; }
    }
}
