using Microsoft.Extensions.Logging;
using VELO.Core.Localization;
using VELO.Security.AI.Models;

namespace VELO.Security.Guards;

/// <summary>
/// DownloadGuard — evaluates every download before it starts.
///
/// Protections:
///  1. Burst detection   — >1 download in 3 s → Block (drive-by pattern)
///  2. Cross-origin exec — executable from a domain ≠ page domain → Block
///  3. Dangerous ext     — .exe/.msi/... from same origin → Warn (user sees it in panel)
///  4. Safe files        — images, docs, zip, etc. → Allow silently
/// </summary>
public class DownloadGuard(ILogger<DownloadGuard> logger)
{
    private readonly ILogger<DownloadGuard> _logger = logger;

    // Per-tab burst tracking: tabId → list of recent download timestamps
    private readonly Dictionary<string, Queue<DateTime>> _burstTracker = new();
    private readonly object _burstLock = new();

    // Extensions that are dangerous when downloaded unexpectedly
    private static readonly HashSet<string> _dangerousExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msp", ".mst",
        ".bat", ".cmd", ".com", ".pif", ".scr",
        ".ps1", ".ps2", ".psm1", ".psd1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".hta", ".reg", ".inf", ".lnk", ".url",
        ".jar", ".jnlp",
        ".dll", ".ocx", ".sys", ".drv",
        ".cpl", ".msc", ".gadget",
        ".app", ".deb", ".rpm", ".pkg",
    };

    // Extensions that are always safe (never trigger warnings)
    private static readonly HashSet<string> _safeExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".csv", ".json", ".xml",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".ico", ".bmp",
        ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4a", ".flac",
        ".zip", ".tar", ".gz", ".bz2", ".7z", ".rar",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods",
        ".epub", ".mobi",
    };

    // Burst window: >1 download within this timespan = burst
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(3);

    // v2.0.5.4 — Per-session "Allow once" + "Whitelist always" exception sets.
    // Populated by SecurityPanel actions so user overrides actually unblock the
    // download (previously AllowOnce only affected sub-resource blocking via
    // RequestGuard, not WebView2 download-starting evaluation).
    private static readonly HashSet<string> _allowedOnceHosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _whitelistedHosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One-shot allow for the next download from <paramref name="host"/>.</summary>
    public static void AllowOnce(string host)
        => _allowedOnceHosts.Add(host.ToLowerInvariant());

    /// <summary>Permanent (per-process) whitelist for downloads from <paramref name="host"/>.</summary>
    public static void Whitelist(string host)
        => _whitelistedHosts.Add(host.ToLowerInvariant());

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates whether a download should be allowed, warned about, or blocked.
    /// </summary>
    /// <param name="tabId">ID of the tab initiating the download.</param>
    /// <param name="downloadUrl">Full URL of the file being downloaded.</param>
    /// <param name="fileName">Suggested file name.</param>
    /// <param name="pageUrl">Current page URL (used for cross-origin check).</param>
    /// <returns>A <see cref="DownloadVerdict"/> with the decision and reason.</returns>
    public DownloadVerdict Evaluate(string tabId, string downloadUrl, string fileName, string pageUrl)
    {
        var ext          = Path.GetExtension(fileName);
        var isDangerous  = _dangerousExts.Contains(ext);
        var isSafe       = _safeExts.Contains(ext) && !isDangerous;
        var isBurst      = RecordAndCheckBurst(tabId);
        var isCrossOrigin = IsCrossOrigin(downloadUrl, pageUrl);

        // ── Rule 0 (v2.0.5.4): user override — checked before everything else ─
        var dlHost = SafeHost(downloadUrl);
        var pgHost = SafeHost(pageUrl);
        bool userAllowed =
            (!string.IsNullOrEmpty(dlHost) && (_whitelistedHosts.Contains(dlHost) || _allowedOnceHosts.Contains(dlHost))) ||
            (!string.IsNullOrEmpty(pgHost) && (_whitelistedHosts.Contains(pgHost) || _allowedOnceHosts.Contains(pgHost)));

        if (userAllowed)
        {
            // Consume the one-shot if it matched
            _allowedOnceHosts.Remove(dlHost);
            _allowedOnceHosts.Remove(pgHost);
            _logger.LogInformation("Download allowed by user override: {File} from {Url}", fileName, downloadUrl);
            return DownloadVerdict.Allow();
        }

        // ── Rule 0b (v2.0.5.6): downloads from RequestGuard's TrustedHosts ─
        //   "Trusted" means we trust this CDN/hosting domain to serve binaries.
        //   Cross-origin Rule 2 was killing legitimate releases hosted on a
        //   different GitHub host than the project landing page (e.g. project
        //   page on *.github.io → installer on github.com or
        //   objects.githubusercontent.com). Allowing trusted CDNs unconditionally
        //   matches the user's intent without re-introducing drive-by risk.
        if (!string.IsNullOrEmpty(dlHost)
            && (RequestGuard.TrustedHosts.Contains(dlHost)
             || RequestGuard.TrustedHosts.Contains(GetEtldPlusOne(dlHost))))
        {
            _logger.LogInformation("Download from TrustedHosts allowed: {File} from {Host}", fileName, dlHost);
            return DownloadVerdict.Allow();
        }

        var L = LocalizationService.Current;

        // ── Rule 1: burst attack ─────────────────────────────────────────
        if (isBurst)
        {
            _logger.LogWarning("BURST download blocked: tab={Tab} file={File} from={Page}",
                tabId, fileName, pageUrl);
            return DownloadVerdict.Block(
                string.Format(L.T("download.block.burst"), fileName),
                ThreatType.Malware);
        }

        // ── Rule 2: cross-origin executable ─────────────────────────────
        if (isDangerous && isCrossOrigin)
        {
            _logger.LogWarning("Cross-origin exec blocked: {File} from {DownloadUrl} on page {Page}",
                fileName, downloadUrl, pageUrl);
            return DownloadVerdict.Block(
                string.Format(L.T("download.block.crossorigin"), fileName),
                ThreatType.Malware);
        }

        // ── Rule 3: dangerous extension, same origin ─────────────────────
        if (isDangerous)
        {
            _logger.LogInformation("Dangerous ext warning: {File}", fileName);
            return DownloadVerdict.Warn(
                string.Format(L.T("download.warn.dangerous"), fileName),
                ThreatType.Malware);
        }

        // ── Rule 4: safe file ────────────────────────────────────────────
        if (isSafe)
            _logger.LogDebug("Safe download allowed: {File}", fileName);
        else
            _logger.LogInformation("Unknown ext download allowed: {File} ({Ext})", fileName, ext);

        return DownloadVerdict.Allow();
    }

    // ── Burst detection ───────────────────────────────────────────────────

    /// <summary>
    /// Records a download attempt and returns true if this is a burst (>1 in BurstWindow).
    /// </summary>
    private bool RecordAndCheckBurst(string tabId)
    {
        lock (_burstLock)
        {
            if (!_burstTracker.TryGetValue(tabId, out var queue))
            {
                queue = new Queue<DateTime>();
                _burstTracker[tabId] = queue;
            }

            var now = DateTime.UtcNow;

            // Evict old entries
            while (queue.Count > 0 && (now - queue.Peek()) > BurstWindow)
                queue.Dequeue();

            var isBurst = queue.Count >= 1; // 2nd+ download in BurstWindow
            queue.Enqueue(now);
            return isBurst;
        }
    }

    /// <summary>Resets the burst counter for a tab (call on user-initiated navigation).</summary>
    public void ResetBurst(string tabId)
    {
        lock (_burstLock)
            _burstTracker.Remove(tabId);
    }

    // ── Cross-origin check ────────────────────────────────────────────────

    private static bool IsCrossOrigin(string downloadUrl, string pageUrl)
    {
        try
        {
            // v2.0.5.5 — A download with no real parent page is, by definition, NOT
            // a drive-by attack. This happens when an external program (e.g. Bambu
            // Studio's update window) launches VELO directly with a download URL,
            // or when the user pastes a download URL into a fresh tab. Treat it as
            // same-origin so Rule 2 (cross-origin executable) doesn't fire — the
            // file still gets the dangerous-extension warning via Rule 3.
            if (string.IsNullOrWhiteSpace(pageUrl)
                || pageUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || pageUrl.StartsWith("velo://", StringComparison.OrdinalIgnoreCase)
                || pageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var dlUri)) return false;
            if (!Uri.TryCreate(pageUrl,     UriKind.Absolute, out var pgUri)) return false;

            var dlHost = NormalizeHost(dlUri.Host);
            var pgHost = NormalizeHost(pgUri.Host);

            // Same host → not cross-origin
            if (string.Equals(dlHost, pgHost, StringComparison.OrdinalIgnoreCase)) return false;

            // Check if one is subdomain of the other
            // e.g. download.example.com and example.com → same eTLD+1
            var dlEtld = GetEtldPlusOne(dlHost);
            var pgEtld = GetEtldPlusOne(pgHost);

            return !string.Equals(dlEtld, pgEtld, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeHost(string host)
        => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    /// <summary>Returns the lowercased host of <paramref name="url"/> or empty on failure.</summary>
    private static string SafeHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
    }

    /// <summary>Extracts the last two labels of a hostname (simplified eTLD+1).</summary>
    private static string GetEtldPlusOne(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2
            ? $"{parts[^2]}.{parts[^1]}"
            : host;
    }
}

// ── Result type ───────────────────────────────────────────────────────────────

public class DownloadVerdict
{
    public DownloadAction Action   { get; private init; }
    public string         Reason   { get; private init; } = "";
    public ThreatType     Threat   { get; private init; } = ThreatType.None;

    public static DownloadVerdict Allow() => new() { Action = DownloadAction.Allow };

    public static DownloadVerdict Warn(string reason, ThreatType threat) => new()
    {
        Action = DownloadAction.Warn,
        Reason = reason,
        Threat = threat,
    };

    public static DownloadVerdict Block(string reason, ThreatType threat) => new()
    {
        Action = DownloadAction.Block,
        Reason = reason,
        Threat = threat,
    };
}

public enum DownloadAction { Allow, Warn, Block }
