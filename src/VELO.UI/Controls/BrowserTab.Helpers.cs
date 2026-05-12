using System.Diagnostics;
using System.IO;
using System.Windows;
using VELO.Core.Localization;

namespace VELO.UI.Controls;

// Phase 3 / Sprint 10b chunk 6 (v2.4.31) — Helpers partition.
// Pure static helpers + the external-protocol launch cluster.
// Sibling partials: BrowserTab.xaml.cs (core + lifecycle + DI setters),
// BrowserTab.PublicApi.cs (host-facing methods), BrowserTab.Events.cs (WebView2 handlers).
public partial class BrowserTab
{
    // v2.0.5 — External URI schemes (custom protocols) are handed off to the OS
    // via ShellExecute. Web schemes are handled inside the browser; everything
    // else (bambustudioopen, obsidian, vscode, zoommtg, mailto, magnet, tel, …)
    // is launched by Windows' registered protocol handler.
    private static readonly HashSet<string> _webSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "file", "about", "data", "blob", "javascript",
        "view-source", "chrome", "edge", "ws", "wss", "velo"
    };

    // Per-session memory of "always allow" decisions for unknown external schemes.
    // Reset on app restart by design — privacy over convenience for unfamiliar protocols.
    private static readonly HashSet<string> _allowedExternalSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Pre-approved well-known protocols — silently launched without prompt
        "mailto", "tel", "sms", "magnet",
        "bambustudioopen", "bambustudio",
        "obsidian", "vscode", "vscode-insiders",
        "zoommtg", "zoomus", "msteams", "slack", "discord",
        "spotify", "steam", "ftp", "sftp", "ssh"
    };

    private static bool IsExternalScheme(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var colonIdx = uri.IndexOf(':');
        if (colonIdx <= 0) return false;
        var scheme = uri[..colonIdx];
        return !_webSchemes.Contains(scheme);
    }

    private static string GetScheme(string uri)
    {
        var colonIdx = uri.IndexOf(':');
        return colonIdx > 0 ? uri[..colonIdx].ToLowerInvariant() : "";
    }

    // v2.1.5 — dedup window. Some WebView2 builds raise BOTH
    // NewWindowRequested (for target=_blank) AND LaunchingExternalUriScheme
    // for the same custom-protocol click. Without this guard, MakerWorld →
    // Bambu Studio launches twice (open dialog twice in the desktop app).
    private string _lastLaunchedUri = "";
    private DateTime _lastLaunchedAt = DateTime.MinValue;
    private static readonly TimeSpan ExternalLaunchDedupWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Hands an external-protocol URI off to the OS via ShellExecute, with a
    /// confirmation prompt for unknown schemes. Returns true if launched.
    /// </summary>
    private bool TryLaunchExternalUri(string uri)
    {
        var scheme = GetScheme(uri);
        if (string.IsNullOrEmpty(scheme)) return false;

        // Dedup: skip if we just launched this same URI a moment ago.
        if (string.Equals(_lastLaunchedUri, uri, StringComparison.Ordinal) &&
            (DateTime.UtcNow - _lastLaunchedAt) < ExternalLaunchDedupWindow)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] External URI dedup: ignoring duplicate {scheme}:// within window");
            return true;
        }

        bool allowed = _allowedExternalSchemes.Contains(scheme);

        if (!allowed)
        {
            // Prompt for unknown schemes — user can grant per-session.
            var L = LocalizationService.Current;
            var trimmedUri = uri.Length > 200 ? uri[..200] + "…" : uri;
            var msg = string.Format(L.T("ext.protocol.prompt"), scheme, trimmedUri);
            var result = MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                msg, L.T("ext.protocol.title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return false;

            _allowedExternalSchemes.Add(scheme);
            allowed = true;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = uri,
                UseShellExecute = true
            });
            _lastLaunchedUri = uri;
            _lastLaunchedAt  = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] External URI launch failed ({scheme}): {ex.Message}");
            var L = LocalizationService.Current;
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                string.Format(L.T("ext.protocol.fail"), scheme),
                L.T("ext.protocol.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static string BuildAboutPage()
    {
        // v2.0.5.8 — Show 4-component version when the revision is non-zero
        // (e.g. 2.0.5.7 hotfixes), otherwise the conventional 3-component
        // form (2.0.4 not 2.0.4.0). Previously ToString(3) silently dropped
        // the hotfix counter, so v2.0.5.7 looked like v2.0.5 in About.
        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        var version = v == null
            ? "?"
            : v.Revision > 0 ? v.ToString(4) : v.ToString(3);

        // v2.0.5.10 — Localise the inline copy that lives inside the HTML
        // template. Title, unsigned-build banner and "Built with…" footer
        // resolve from LocalizationService so the about page matches the
        // active UI language instead of staying in Spanish forever.
        var L = LocalizationService.Current;
        return BuildAboutPageTemplate()
            .Replace("VELO_VERSION_PLACEHOLDER",       version)
            .Replace("VELO_TITLE_PLACEHOLDER",         L.T("about.title"))
            .Replace("VELO_UNSIGNED_HEADER_PLACEHOLDER", L.T("about.unsigned.header"))
            .Replace("VELO_UNSIGNED_BODY_PLACEHOLDER",   L.T("about.unsigned.body"))
            .Replace("VELO_BUILTWITH_PLACEHOLDER",       L.T("about.builtwith"));
    }

    private static string BuildAboutPageTemplate() => """
        <!DOCTYPE html>
        <html lang="es">
        <head>
        <meta charset="utf-8"/>
        <title>VELO_TITLE_PLACEHOLDER</title>
        <style>
          * { margin:0; padding:0; box-sizing:border-box; }
          body {
            background: #0e0e0e;
            color: #e8e8e8;
            font-family: 'Segoe UI', system-ui, sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            gap: 0;
          }
          .card {
            background: #181818;
            border: 1px solid #2a2a2a;
            border-radius: 16px;
            padding: 48px 60px;
            max-width: 520px;
            width: 90%;
            text-align: center;
            box-shadow: 0 8px 40px rgba(0,0,0,0.6);
          }
          .logo {
            font-size: 52px;
            font-weight: 800;
            letter-spacing: -2px;
            background: linear-gradient(135deg, #00e5ff 0%, #7c4dff 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
            margin-bottom: 4px;
          }
          .tagline {
            font-size: 13px;
            color: #666;
            letter-spacing: 3px;
            text-transform: uppercase;
            margin-bottom: 36px;
          }
          .version-badge {
            display: inline-block;
            background: #00e5ff18;
            border: 1px solid #00e5ff44;
            color: #00e5ff;
            font-size: 13px;
            font-weight: 600;
            padding: 6px 18px;
            border-radius: 99px;
            margin-bottom: 36px;
            letter-spacing: 1px;
          }
          .feature-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 10px;
            margin-bottom: 36px;
            text-align: left;
          }
          .feature-item {
            background: #1e1e1e;
            border: 1px solid #2a2a2a;
            border-radius: 8px;
            padding: 12px 14px;
            font-size: 12px;
            color: #aaa;
            display: flex;
            align-items: center;
            gap: 8px;
          }
          .feature-item span.icon { font-size: 16px; }
          .divider {
            border: none;
            border-top: 1px solid #222;
            margin: 0 0 24px 0;
          }
          .meta {
            font-size: 12px;
            color: #444;
            line-height: 1.8;
          }
          .meta a { color: #00e5ff; text-decoration: none; }
          .meta a:hover { text-decoration: underline; }
          .hero-img {
            width: 140px;
            height: 140px;
            object-fit: cover;
            border-radius: 50%;
            margin: 0 auto 16px;
            display: block;
            border: 2px solid #00e5ff33;
            box-shadow: 0 0 32px #7c4dff44;
          }
        </style>
        </head>
        <body>
          <div class="card">
            <img class="hero-img"
                 src="https://raw.githubusercontent.com/Badizher-codex/velo/main/src/VELO.UI/Assets/velo-logo.png"
                 onerror="this.style.display='none';document.getElementById('shield-fallback').style.display='flex'"/>
            <div id="shield-fallback" style="width:80px;height:80px;margin:0 auto 16px;background:linear-gradient(135deg,#00e5ff22,#7c4dff22);border:2px solid #00e5ff44;border-radius:50%;display:none;align-items:center;justify-content:center;font-size:36px">🛡</div>
            <div class="logo">VELO</div>
            <div class="tagline">Privacy Browser · Windows</div>
            <div class="version-badge">vVELO_VERSION_PLACEHOLDER</div>
            <div class="feature-grid">
              <div class="feature-item"><span class="icon">🧬</span>Fingerprint Guard</div>
              <div class="feature-item"><span class="icon">🚫</span>Tracker Blocker</div>
              <div class="feature-item"><span class="icon">🤖</span>AI Threat Detection</div>
              <div class="feature-item"><span class="icon">🔒</span>DNS-over-HTTPS</div>
              <div class="feature-item"><span class="icon">🔑</span>Password Vault</div>
              <div class="feature-item"><span class="icon">🌐</span>WebRTC Guard</div>
              <div class="feature-item"><span class="icon">📖</span>Reader Mode</div>
              <div class="feature-item"><span class="icon">👾</span>Malwaredex</div>
            </div>
            <hr class="divider"/>
            <div style="padding:10px 12px;margin:0 0 16px 0;background:#2a1a00;border-left:3px solid #ffb300;color:#ffb300;font-size:11px;line-height:1.5;text-align:left;border-radius:4px">
              <strong>VELO_UNSIGNED_HEADER_PLACEHOLDER</strong><br/>
              VELO_UNSIGNED_BODY_PLACEHOLDER
              <a href="https://github.com/badizher-codex/velo/releases" target="_blank">github.com/badizher-codex/velo/releases</a>
            </div>
            <div class="meta">
              VELO_BUILTWITH_PLACEHOLDER<br/>
              <a href="https://github.com/badizher-codex/velo" target="_blank">github.com/badizher-codex/velo</a><br/>
              <br/>
              © 2026 VELO Browser Contributors · GNU AGPLv3
            </div>
          </div>
        </body>
        </html>
        """;

    private static string BuildReaderPage(string title, string metaLine, string content)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var safeMeta  = System.Net.WebUtility.HtmlEncode(metaLine);
        var metaBlock = string.IsNullOrEmpty(metaLine)
            ? ""
            : $"<div id=\"velo-meta\">{safeMeta}</div>";

        // Use $$ raw string so CSS braces are literal and {{var}} = interpolation
        return $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{safeTitle}} — Modo Lector</title>
            <style>
              :root {
                --bg:      #181820; --surface: #22222e; --border:  #2e2e3e;
                --text:    #e2e2e8; --muted:   #888899; --accent:  #00e5ff;
                --link:    #7ecfff; --max-w:   680px;
                --font:    Georgia, 'Times New Roman', serif;
                --ui:      'Segoe UI', system-ui, sans-serif;
              }
              *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
              html { background: var(--bg); color: var(--text);
                     font-family: var(--font); font-size: 18px; line-height: 1.75; }
              body { max-width: var(--max-w); margin: 0 auto; padding: 68px 24px 96px; }
              #velo-bar {
                position: fixed; top: 0; left: 0; right: 0; z-index: 999;
                background: var(--surface); border-bottom: 1px solid var(--border);
                display: flex; align-items: center; gap: 10px;
                padding: 8px 20px; font-family: var(--ui); font-size: 13px;
              }
              #velo-bar .label { color: var(--accent); font-weight: 600; }
              #velo-bar button {
                background: transparent; border: 1px solid var(--border);
                color: var(--text); padding: 3px 10px; border-radius: 4px;
                cursor: pointer; font-family: var(--ui); font-size: 12px;
              }
              #velo-bar button:hover { background: var(--border); }
              #velo-bar .exit { margin-left: auto; }
              h1#velo-title { font-size: 1.8em; line-height: 1.25; margin: 20px 0 10px; }
              #velo-meta {
                font-family: var(--ui); font-size: 13px; color: var(--muted);
                margin-bottom: 28px; padding-bottom: 18px; border-bottom: 1px solid var(--border);
              }
              #velo-content img { max-width: 100%; height: auto; border-radius: 6px; margin: 1em 0; display: block; }
              #velo-content h1, #velo-content h2, #velo-content h3,
              #velo-content h4, #velo-content h5, #velo-content h6 { margin: 1.4em 0 0.5em; line-height: 1.3; }
              #velo-content h2 { font-size: 1.3em; }
              #velo-content h3 { font-size: 1.1em; }
              #velo-content p  { margin: 0 0 1em; }
              #velo-content a  { color: var(--link); text-decoration: none; }
              #velo-content a:hover { text-decoration: underline; }
              #velo-content blockquote {
                border-left: 3px solid var(--accent); margin: 1.2em 0;
                padding: .5em 1em; color: var(--muted); font-style: italic;
              }
              #velo-content ul, #velo-content ol { margin: .5em 0 1em 1.5em; }
              #velo-content li  { margin-bottom: .3em; }
              #velo-content pre, #velo-content code {
                font-family: 'Cascadia Code', Consolas, monospace;
                background: var(--surface); border-radius: 4px; font-size: .85em;
              }
              #velo-content pre  { padding: 1em; overflow-x: auto; }
              #velo-content code { padding: 2px 5px; }
              #velo-content figure { margin: 1.2em 0; }
              #velo-content figcaption {
                font-family: var(--ui); font-size: 12px; color: var(--muted);
                text-align: center; margin-top: 6px;
              }
              #velo-content table { width: 100%; border-collapse: collapse; font-size: .9em; margin: 1em 0; }
              #velo-content th, #velo-content td { border: 1px solid var(--border); padding: 8px 12px; }
              #velo-content th { background: var(--surface); }
            </style>
            </head>
            <body>
            <div id="velo-bar">
              <span class="label">📖 Modo Lector</span>
              <button onclick="changeFontSize(-1)">A−</button>
              <button onclick="changeFontSize(+1)">A+</button>
              <button class="exit" onclick="history.back()">✕ Salir</button>
            </div>
            <h1 id="velo-title">{{safeTitle}}</h1>
            {{metaBlock}}
            <div id="velo-content">{{content}}</div>
            <script>
            function changeFontSize(d) {
              var el = document.documentElement;
              var cur = parseFloat(getComputedStyle(el).fontSize);
              el.style.fontSize = Math.max(14, Math.min(28, cur + d)) + 'px';
            }
            </script>
            </body>
            </html>
            """;
    }

    /// <summary>Returns true if both URIs share the same eTLD+1 (e.g. sub.foo.com == foo.com).</summary>
    private static bool IsSameEtld(string uriA, string uriB)
    {
        try
        {
            if (!Uri.TryCreate(uriA, UriKind.Absolute, out var a)) return false;
            if (!Uri.TryCreate(uriB, UriKind.Absolute, out var b)) return false;
            return GetEtld(a.Host).Equals(GetEtld(b.Host), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string GetEtld(string host)
    {
        var parts = host.TrimStart('.').Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : host;
    }

    private static string ShortenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var host = uri.Host;
        var path = uri.AbsolutePath.Length > 20 ? uri.AbsolutePath[..20] + "…" : uri.AbsolutePath;
        return host + path;
    }

    private static string GetHost(string uri)
    {
        try { return new Uri(uri).Host.ToLowerInvariant(); }
        catch { return uri; }
    }

    // ── Cookie consent bypass ────────────────────────────────────────────

    private const string ConsentScript = """
        (function() {
          var kw = [
            'aceptar','aceptar todo','acepto','acepto y continúo','acepto y continuo',
            'accept','accept all','accept & continue','accept and continue','agree','i agree',
            'allow all','allow cookies','allow all cookies','continuar sin anuncios',
            'entendido','ok, entendido','got it','i understand',
            'akzeptieren','alle akzeptieren','accepter','tout accepter',
            'accetta','accetta tutto','aceitar','aceitar tudo'
          ];
          function norm(s){ return (s||'').replace(/[\n\r]+/g,' ').replace(/\s+/g,' ').trim().toLowerCase(); }

          function tryDismiss() {
            // 1. Known CMP selectors (most reliable, check first)
            var sels = [
              // OneTrust
              '#onetrust-accept-btn-handler',
              // Cookiebot
              '#CybotCookiebotDialogBodyLevelButtonLevelOptinAllowAll',
              // Didomi (common in Spain/France)
              '#didomi-notice-agree-button','.didomi-notice-agree-button',
              // SourcePoint (Marca, El Mundo, Unidad Editorial)
              '.message-button.accept-all','[title="Acepto"]',
              'button.sp_choice_type_11','button[sp-message-id]',
              '.sp_choice_type_ACCEPT_ALL',
              // Quantcast
              '.qc-cmp2-summary-buttons button:last-child',
              // Commanders Act / TrustCommander
              '#tc-privacy-button-accept','.tc-privacy-button--accept',
              // Fundéu/Prisa (El País, AS)
              '[data-role="agree"]','[data-action="agree"]',
              // Generic
              '.fc-cta-consent','.fc-button-label',
              '.js-accept-cookies','.cookie-consent-accept',
              '#accept-all-cookies','#acceptAllBtn','#accept-cookies',
              '[id*="accept-all"i]','[class*="accept-all"i]',
              '[id*="gdpr"][id*="accept"i]','[class*="gdpr"][class*="accept"i]',
              // AMO (Spanish media consortium)
              '.amo-rgpd__btn--accept','.rgpd-btn--accept',
              // Piano (subscription/consent walls)
              '.tp-btn-primary','.piano-id__btn-primary',
            ];
            for (var s of sels) {
              var el = document.querySelector(s);
              if (el && el.offsetParent !== null) { el.click(); return true; }
            }

            // 2. Text-based button scan (fallback)
            var els = document.querySelectorAll(
              'button,a[role="button"],[role="button"],input[type="button"],input[type="submit"]'
            );
            for (var el of els) {
              if (el.offsetParent === null) continue; // skip hidden elements
              var t = norm(el.innerText || el.value || el.getAttribute('aria-label') || '');
              if (kw.some(function(k){ return t === k; })) {
                el.click(); return true;
              }
            }
            return false;
          }

          function run() {
            tryDismiss();
            // Retry for late-rendered dialogs (SPA / lazy-loaded consent)
            setTimeout(tryDismiss, 800);
            setTimeout(tryDismiss, 2000);
            setTimeout(tryDismiss, 4500);
          }

          if (document.readyState === 'loading')
            document.addEventListener('DOMContentLoaded', run);
          else
            run();
        })();
        """;

    private static async Task<string?> LoadScriptResourceAsync(string name)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(appDir, "resources", "scripts", name);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }
}
