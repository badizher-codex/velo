using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Security.Guards;

/// <summary>
/// v2.1.5.1 — Per-site allowlist that auto-relaxes fingerprint spoofing and
/// WebRTC relay on a curated set of domains known to use anti-bot
/// fingerprinting (Akamai Bot Manager, PerimeterX, Imperva, Cloudflare Bot
/// Management, hCaptcha enterprise). Without relaxation those endpoints
/// reject login attempts when the browser's canvas/WebGL/audio/ICE
/// signatures don't match their expected device baseline — and the user
/// sees a generic "wrong username or password" message instead of an
/// honest bot-detection notice. Tracker, ad, and request-guard protections
/// remain fully active; only fingerprint and WebRTC are loosened.
///
/// Match rule (eTLD+1 with parent-host): a navigation host matches when
/// its trailing eTLD+1 equals an allowlist entry, OR when the host ends
/// with <c>.&lt;entry&gt;</c>. So <c>www.homedepot.com</c> and
/// <c>checkout.homedepot.com</c> both match <c>homedepot.com</c>, but
/// <c>evil-homedepot.com</c> does not.
/// </summary>
public sealed class ShieldsAllowlist
{
    private readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ShieldsAllowlist> _logger;

    /// <summary>Caller-controlled global toggle. When false, <see cref="Matches"/> always returns false.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Number of bundled + user-supplied domains currently loaded.</summary>
    public int Count => _domains.Count;

    public ShieldsAllowlist(ILogger<ShieldsAllowlist>? logger = null)
    {
        _logger = logger ?? NullLogger<ShieldsAllowlist>.Instance;
    }

    /// <summary>
    /// Loads the bundled seed list from the app directory and the optional
    /// user list from <paramref name="userDataPath"/>. Both files are
    /// optional — missing files are logged at Debug, not an error.
    /// </summary>
    public void Load(string? appDir = null, string? userDataPath = null)
    {
        appDir ??= AppDomain.CurrentDomain.BaseDirectory;
        var seedPath = Path.Combine(appDir, "resources", "blocklists", "shields-allowlist.txt");
        LoadFile(seedPath, label: "bundled");

        if (!string.IsNullOrEmpty(userDataPath))
        {
            var userPath = Path.Combine(userDataPath, "shields-allowlist-user.txt");
            LoadFile(userPath, label: "user");
        }

        _logger.LogInformation("ShieldsAllowlist loaded {Count} domains", _domains.Count);
    }

    private void LoadFile(string path, string label)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogDebug("ShieldsAllowlist: no {Label} file at {Path}", label, path);
                return;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                _domains.Add(line.ToLowerInvariant());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ShieldsAllowlist: failed to load {Path}", path);
        }
    }

    /// <summary>
    /// True when the navigation host should run with shields relaxed
    /// (fingerprint spoofing off, WebRTC unmodified).
    /// </summary>
    public bool Matches(string host)
    {
        if (!Enabled || string.IsNullOrEmpty(host) || _domains.Count == 0) return false;

        var h = NormalizeHost(host);
        if (_domains.Contains(h)) return true;

        // Parent-host check: if any allowlist entry is a suffix of h preceded
        // by a dot, h matches. e.g. h="checkout.homedepot.com" matches entry
        // "homedepot.com" because "checkout.homedepot.com".EndsWith(".homedepot.com").
        foreach (var entry in _domains)
        {
            if (h.Length > entry.Length + 1 &&
                h.EndsWith("." + entry, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Returns the JS literal payload to inject before content scripts.</summary>
    public string BuildJsConstant()
    {
        // Emits: window.__VELO_RELAXED_DOMAINS__ = ["homedepot.com", ...];
        // Each script then does:
        //   const h = location.hostname.toLowerCase();
        //   const relaxed = (window.__VELO_RELAXED_DOMAINS__ || []).some(
        //     d => h === d || h.endsWith('.' + d));
        //   if (relaxed) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("window.__VELO_RELAXED_DOMAINS__=[");
        bool first = true;
        foreach (var d in _domains)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"');
            // Escape just the chars that can appear in a domain literal.
            sb.Append(d.Replace("\\", "").Replace("\"", ""));
            sb.Append('"');
        }
        sb.Append("];");
        return sb.ToString();
    }

    private static string NormalizeHost(string host)
    {
        var h = host.Trim().ToLowerInvariant();
        if (h.StartsWith("www.")) h = h[4..];
        return h;
    }
}
