using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VELO.Security.AI.Models;
using VELO.Security.Rules;

namespace VELO.Security.Guards;

public class RequestGuard(BlocklistManager blocklist, ILogger<RequestGuard> logger)
{
    private readonly BlocklistManager _blocklist = blocklist;
    private readonly ILogger<RequestGuard> _logger = logger;

    private static readonly HashSet<string> _userWhitelist = [];

    private static readonly Regex _trackingBeaconPattern = new(
        @"\.(gif|png)\?.*utm_|/beacon\?|/pixel\?|/track\?|1x1\.gif|/log\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    public SecurityVerdict Evaluate(string uri, string? referrer, string resourceType)
    {
        Uri? url;
        try { url = new Uri(uri); }
        catch { return SecurityVerdict.Allow(); }

        var host = url.Host.ToLowerInvariant();

        // 1. User whitelist
        if (_userWhitelist.Contains(host))
            return SecurityVerdict.Allow();

        // 2. Blocklist (O(1))
        if (_blocklist.IsBlocked(host))
        {
            _logger.LogDebug("BLOCKED by blocklist: {Host}", host);
            return SecurityVerdict.Block("Dominio en blocklist de rastreadores conocidos", ThreatType.KnownTracker, "BLOCKLIST");
        }

        // 3. DNS rebinding — public domain resolving to private IP
        if (IsSuspiciousPrivateAddress(host))
            return SecurityVerdict.Block("Posible ataque DNS rebinding", ThreatType.DnsRebinding);

        // 4. SSRF — request to private IP from public page
        if (IsPrivateIp(host) && !string.IsNullOrEmpty(referrer) && !IsLocalPage(referrer))
            return SecurityVerdict.Block("Request a IP privada desde página externa (SSRF)", ThreatType.SSRF);

        // 5. Suspicious URL params
        if (HasSuspiciousUrlParams(url))
            return SecurityVerdict.Warn("URL contiene parámetros sospechosos de exfiltración", ThreatType.DataExfiltration);

        // 6. Tracking beacons
        if (_trackingBeaconPattern.IsMatch(uri))
            return SecurityVerdict.Block("Tracking beacon detectado", ThreatType.Tracker);

        // 7. Mixed content (HTTP request from HTTPS page)
        if (uri.StartsWith("http://") && referrer?.StartsWith("https://") == true)
            return SecurityVerdict.Warn("Contenido mixto HTTP desde página HTTPS", ThreatType.MixedContent);

        // 8. Only send to AI if the domain looks genuinely suspicious:
        //    - suspicious TLD, brand impersonation, or random-generated hostname
        //    - only for main-frame navigation (WebView2 calls this "Document")
        //    - not for sub-resources (Image, Script, Stylesheet, Font, etc.)
        var isMainFrame = resourceType.Equals("Document", StringComparison.OrdinalIgnoreCase)
                       || resourceType.Equals("Other",    StringComparison.OrdinalIgnoreCase);

        if (isMainFrame && (HasSuspiciousTld(host) || LooksLikeBrandImpersonation(host) || LooksRandomGenerated(host)))
            return SecurityVerdict.NeedsAI();

        return SecurityVerdict.Allow();
    }

    public static void AddToWhitelist(string host) => _userWhitelist.Add(host.ToLowerInvariant());
    public static void RemoveFromWhitelist(string host) => _userWhitelist.Remove(host.ToLowerInvariant());

    private static bool HasSuspiciousTld(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2 && _suspiciousTlds.Contains(parts[^1]);
    }

    private static bool LooksLikeBrandImpersonation(string host)
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

    private static bool LooksRandomGenerated(string host)
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
            var value = query[key] ?? "";
            if (value.Length > 50 && IsBase64(value)) return true;
            if (Regex.IsMatch(value, @"[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase)) return true;
            if (value.Length > 100) return true;
        }
        return false;
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

    private static bool IsSuspiciousPrivateAddress(string host)
        => host is "localhost" or "0.0.0.0" || host.EndsWith(".local");

    private static bool IsLocalPage(string referrer)
    {
        try
        {
            var uri = new Uri(referrer);
            return uri.Host is "localhost" or "127.0.0.1" || uri.Host.EndsWith(".local");
        }
        catch { return false; }
    }
}
