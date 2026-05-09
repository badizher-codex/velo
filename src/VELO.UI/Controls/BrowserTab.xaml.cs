using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using VELO.Core.Containers;
using VELO.Core.Downloads;
using VELO.Core.Localization;
using VELO.Core.Navigation;
using VELO.Data.Repositories;
using VELO.Security.AI;
using VELO.Security.AI.Models;
using VELO.Security.CookieWall;
using VELO.Security.Guards;
using VELO.Vault;

namespace VELO.UI.Controls;

public partial class BrowserTab : UserControl
{
    public event EventHandler<string>? UrlChanged;
    public event EventHandler<string>? TitleChanged;
    public event EventHandler<bool>? LoadingChanged;
    public event EventHandler<(bool CanBack, bool CanForward)>? NavigationStateChanged;
    public event EventHandler<AIVerdict>? SecurityVerdictReceived;
    public event EventHandler<TlsStatus>? TlsStatusChanged;
    /// <summary>Raised when the user hovers a link long enough. Arg = URL to preview (empty = hide).</summary>
    public event EventHandler<string>? GlanceLinkHovered;

    private string _tabId = "";
    private bool _webViewInitialized;
    private CoreWebView2Environment? _env;
    private readonly List<string> _allowedOnce = [];

    private AISecurityEngine? _aiEngine;
    private RequestGuard? _requestGuard;
    private TLSGuard? _tlsGuard;
    private DownloadGuard? _downloadGuard;
    private DownloadManager? _downloadManager;
    private string _fingerprintLevel    = "Aggressive";
    private string _webRtcMode          = "Relay";
    private string _currentPageUrl      = "";
    private string _currentContainerId  = "none";

    // Popup burst: tracks timestamps of recent new-window requests per tab
    private readonly Queue<DateTime> _popupTimes = new();
    private static readonly TimeSpan PopupBurstWindow = TimeSpan.FromSeconds(3);

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

    // Fase 2: enriched context menu (optional — falls back to WebView2 default if null)
    private ContextMenuBuilder? _contextMenuBuilder;
    // Phase 3 / Sprint 1E: when set, the AI variant decorates the menu with the
    // 🤖 IA submenu. Falls back to the plain ContextMenuBuilder when null.
    private AIContextMenuBuilder? _aiContextMenuBuilder;

    public void SetContextMenuBuilder(ContextMenuBuilder builder)
    {
        _contextMenuBuilder = builder;
        // v2.4.19 — formerly subscribed RequestPaste here, but the builder
        // is a DI singleton so every tab's handler stayed live; pegar in
        // tab A also pasted into tab B if B had a focused editable. Now
        // the paste callback is supplied per-build in OnContextMenuRequested,
        // captured to *this* BrowserTab via closure. No event, no broadcast.
    }

    /// <summary>v2.4.16 — Reads the clipboard on the UI thread and injects the
    /// text into this tab's focused editable element (and only this tab).
    /// Called by ContextMenuBuilder via the per-build onPaste callback.</summary>
    private void HandlePasteRequest()
    {
        try
        {
            var text = System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : "";
            if (string.IsNullOrEmpty(text)) return;
            _ = PasteTextIntoFocusedEditableAsync(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Paste failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Injects <paramref name="text"/> into the page's focused editable element.
    /// Handles three target shapes: native &lt;input&gt;/&lt;textarea&gt; (uses .value
    /// + selection insert), contentEditable elements (document.execCommand
    /// insertText), and as a last-resort fallback the deprecated execCommand
    /// 'paste'. JSON-encodes the payload so quotes/newlines don't break.
    /// </summary>
    private async Task PasteTextIntoFocusedEditableAsync(string text)
    {
        if (!_webViewInitialized) return;
        var jsonText = System.Text.Json.JsonSerializer.Serialize(text);
        var script = $$"""
            (() => {
                const t = {{jsonText}};
                const el = document.activeElement;
                if (!el) return false;
                const tag = (el.tagName || '').toUpperCase();
                if (tag === 'INPUT' || tag === 'TEXTAREA') {
                    const start = el.selectionStart ?? el.value.length;
                    const end   = el.selectionEnd   ?? el.value.length;
                    el.value = el.value.substring(0, start) + t + el.value.substring(end);
                    el.selectionStart = el.selectionEnd = start + t.length;
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    return true;
                }
                if (el.isContentEditable) {
                    document.execCommand('insertText', false, t);
                    return true;
                }
                return false;
            })();
            """;
        try { await WebView.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Paste-inject script failed: {ex.Message}");
        }
    }
    public void SetAIContextMenuBuilder(AIContextMenuBuilder builder) => _aiContextMenuBuilder = builder;

    // Fase 2: banking-mode flag (set by caller after Initialize)
    private bool _isBankingContainer;

    // Fase 2: PasteGuard
    private PasteGuard? _pasteGuard;

    // Fase 2: Sprint 6 — history repo for NewTab v2 top sites
    private HistoryRepository? _historyRepo;

    public void SetPasteGuard(PasteGuard guard) => _pasteGuard = guard;

    // v2.1.5.1 — Shields allowlist wired in from MainWindow before WebView init.
    private ShieldsAllowlist? _shieldsAllowlist;
    public void SetShieldsAllowlist(ShieldsAllowlist allow) => _shieldsAllowlist = allow;

    // v2.4.22 — Sprint 8A wire. SmartBlockClassifier fires async on cache
    // misses for sub-resource hosts; the verdict is read sync from the
    // classifier's cache by RequestGuard on the next request. Fire-and-forget
    // so the current request never waits for the model.
    private SmartBlockClassifier? _smartBlock;
    public void SetSmartBlockClassifier(SmartBlockClassifier classifier) => _smartBlock = classifier;

    // Phase 3 / Sprint 5 — Autofill
    private AutofillService? _autofill;
    /// <summary>Raised when the page reports a login form is present. Arg = host.</summary>
    public event EventHandler<string>? AutofillFormDetected;
    /// <summary>Raised when the user submits a login form. Args = (host, username, password).</summary>
    public event EventHandler<(string Host, string Username, string Password)>? AutofillFormSubmitted;
    public void SetAutofillService(AutofillService svc) => _autofill = svc;

    /// <summary>Fills the active login form with the chosen credential. No-op if no form is detected.</summary>
    public async Task FillCredentialAsync(string username, string password)
    {
        if (!_webViewInitialized) return;
        // The page-side hook returns a boolean we don't read — JS-encode args safely.
        var u = JsonSerializer.Serialize(username);
        var p = JsonSerializer.Serialize(password);
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(
                $"window.__veloAutofillFill && window.__veloAutofillFill({u},{p})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Autofill fill failed: {ex.Message}");
        }
    }

    /// <summary>Provides the history repository so NewTab v2 can load top sites.</summary>
    public void SetHistoryRepository(HistoryRepository repo) => _historyRepo = repo;

    /// <summary>Opens the native Chromium DevTools window for this tab.</summary>
    public void OpenDevTools()
    {
        if (!_webViewInitialized) return;
        try
        {
            // Re-enable dev tools for programmatic open (they are off by default for privacy)
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.OpenDevToolsWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] DevTools open failed: {ex.Message}");
        }
    }

    public void SetContainer(string containerId)
    {
        _currentContainerId  = containerId;
        _isBankingContainer  = BankingContainerPolicy.Applies(containerId);
    }

    public string TabId => _tabId;

    public BrowserTab()
    {
        InitializeComponent();
    }

    public void Initialize(string tabId, AISecurityEngine aiEngine, RequestGuard requestGuard,
        TLSGuard tlsGuard, DownloadGuard downloadGuard, DownloadManager downloadManager,
        string fingerprintLevel = "Aggressive", string webRtcMode = "Relay")
    {
        _tabId            = tabId;
        _aiEngine         = aiEngine;
        _requestGuard     = requestGuard;
        _tlsGuard         = tlsGuard;
        _downloadGuard    = downloadGuard;
        _downloadManager  = downloadManager;
        _fingerprintLevel = fingerprintLevel;
        _webRtcMode       = webRtcMode;

        if (_tlsGuard != null)
            _tlsGuard.ThreatDetected += (uri, verdict) =>
                Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
                {
                    Verdict    = verdict.Verdict,
                    Reason     = verdict.Reason,
                    ThreatType = verdict.ThreatType,
                    Source     = verdict.Source,
                    Confidence = 90
                }));
    }

    public async Task EnsureWebViewInitializedAsync(CoreWebView2Environment env)
    {
        _env = env;
        if (_webViewInitialized) return;

        await WebView.EnsureCoreWebView2Async(env);

        // Zero-telemetry settings
        WebView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
        WebView.CoreWebView2.Settings.IsWebMessageEnabled = true; // required for PasteGuard bridge
        // NOTE: AreDefaultContextMenusEnabled must stay TRUE for ContextMenuRequested to fire.
        // Our handler (OnContextMenuRequested) sets e.Handled = true to suppress the native menu
        // and show the custom WPF dark-themed one instead. Setting this to false disables the event.
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        WebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        WebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        // Cookie consent auto-dismiss (embedded — no external files)
        await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ConsentScript);

        // v2.1.5.1 — Inject shields allowlist constant FIRST so subsequent
        // fingerprint / webrtc scripts can early-return on relaxed domains.
        if (_shieldsAllowlist != null && _shieldsAllowlist.Count > 0)
        {
            try
            {
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    _shieldsAllowlist.BuildJsConstant());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[VELO] Shields allowlist inject failed: {ex.Message}");
            }
        }

        // Fingerprint protection
        try
        {
            if (_fingerprintLevel != "Off")
            {
                var fpScript = await LoadScriptResourceAsync("fingerprint-noise.js");
                if (fpScript != null)
                    await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(fpScript);
            }
        }
        catch { /* never block WebView init */ }

        // WebRTC IP protection
        try
        {
            if (_webRtcMode != "Off")
            {
                var webRtcScript = await LoadScriptResourceAsync("webrtc-spoof.js");
                if (webRtcScript != null)
                {
                    var mode = _webRtcMode == "Disabled" ? "disabled" : "relay";
                    var fullScript = $"window.__VELO_WEBRTC_MODE__ = '{mode}';\n{webRtcScript}";
                    await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(fullScript);
                }
            }
        }
        catch { /* never block WebView init */ }

        // PasteGuard — inject bridge + detector script
        try
        {
            var pgScript = await LoadScriptResourceAsync("paste-guard.js");
            if (pgScript != null)
            {
                var bridge = PasteGuard.BuildBridgeScript("");
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(bridge + "\n" + pgScript);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] PasteGuard inject failed: {ex.Message}");
        }

        // Phase 3 / Sprint 5 — Autofill content script
        try
        {
            var autofillScript = await LoadScriptResourceAsync("autofill.js");
            if (autofillScript != null)
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(autofillScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Autofill inject failed: {ex.Message}");
        }

        // Glance modal — hover-link preview
        try
        {
            var glanceScript = await LoadScriptResourceAsync("glance-hover.js");
            if (glanceScript != null)
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(glanceScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Glance inject failed: {ex.Message}");
        }

        // Banking container — inject strict fingerprint spoofing + no-referrer
        if (_isBankingContainer)
        {
            try
            {
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BankingContainerPolicy.FingerprintScript);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[VELO] Banking FP inject failed: {ex.Message}");
            }
        }

        // Hooks
        WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
        WebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        WebView.CoreWebView2.DocumentTitleChanged += OnTitleChanged;
        WebView.CoreWebView2.NewWindowRequested         += OnNewWindowRequested;
        WebView.CoreWebView2.DownloadStarting           += OnDownloadStarting;
        WebView.CoreWebView2.ContextMenuRequested       += OnContextMenuRequested;
        WebView.CoreWebView2.ServerCertificateErrorDetected += OnServerCertificateError;
        WebView.CoreWebView2.WebMessageReceived             += OnWebMessageReceived;

        // v2.0.5 — Custom-protocol launch (bambustudioopen://, obsidian://, …).
        // Without this handler WebView2 falls back to its built-in confirmation
        // dialog AND, in our case, our NewWindowRequested intercept fires for
        // <a target="_blank" href="custom://..."> which would otherwise drop
        // the URI on the floor. Suppressing the default dialog and using our
        // own consistent prompt keeps the UX coherent.
        try
        {
            WebView.CoreWebView2.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
        }
        catch (Exception ex)
        {
            // Older WebView2 runtimes (<1.0.1185) don't expose this event.
            // Custom protocols still work via NewWindowRequested fallback.
            System.Diagnostics.Trace.WriteLine($"[VELO] LaunchingExternalUriScheme not available: {ex.Message}");
        }

        _webViewInitialized = true;
    }

    public async Task NavigateAsync(string url)
    {
        if (url == "velo://newtab")
        {
            ShowNewTabPage();
            return;
        }

        if (url == "velo://about")
        {
            if (!_webViewInitialized && _env != null)
                await EnsureWebViewInitializedAsync(_env);
            if (!_webViewInitialized) return;
            ShowWebView();
            WebView.CoreWebView2.NavigateToString(BuildAboutPage());
            UrlChanged?.Invoke(this, "velo://about");
            TitleChanged?.Invoke(this, LocalizationService.Current.T("about.title"));
            return;
        }

        if (!_webViewInitialized && _env != null)
            await EnsureWebViewInitializedAsync(_env);

        if (!_webViewInitialized) return;

        ShowWebView();
        WebView.CoreWebView2.Navigate(url);
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


    public void GoBack()    { if (WebView.CoreWebView2?.CanGoBack    == true) WebView.CoreWebView2.GoBack(); }
    public void GoForward() { if (WebView.CoreWebView2?.CanGoForward == true) WebView.CoreWebView2.GoForward(); }
    public void Reload()    => WebView.CoreWebView2?.Reload();
    public void Stop()      => WebView.CoreWebView2?.Stop();

    // Zoom levels matching Chrome's steps
    private static readonly double[] ZoomLevels = [0.25, 0.33, 0.50, 0.67, 0.75, 0.90, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0];

    public double ZoomFactor => _webViewInitialized ? WebView.ZoomFactor : 1.0;

    public event EventHandler<double>? ZoomChanged;

    public void ZoomIn()
    {
        if (!_webViewInitialized) return;
        var cur  = WebView.ZoomFactor;
        var next = ZoomLevels.FirstOrDefault(z => z > cur + 0.01);
        if (next == 0) next = ZoomLevels[^1];
        WebView.ZoomFactor = next;
        ZoomChanged?.Invoke(this, next);
    }

    public void ZoomOut()
    {
        if (!_webViewInitialized) return;
        var cur  = WebView.ZoomFactor;
        var prev = ZoomLevels.LastOrDefault(z => z < cur - 0.01);
        if (prev == 0) prev = ZoomLevels[0];
        WebView.ZoomFactor = prev;
        ZoomChanged?.Invoke(this, prev);
    }

    public void ResetZoom()
    {
        if (!_webViewInitialized) return;
        WebView.ZoomFactor = 1.0;
        ZoomChanged?.Invoke(this, 1.0);
    }

    public async Task<bool> FindAsync(string text, bool backwards = false)
    {
        if (!_webViewInitialized || string.IsNullOrEmpty(text)) return false;
        var escaped = text.Replace("\\", "\\\\").Replace("'", "\\'");
        var result = await WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.find('{escaped}', false, {(backwards ? "true" : "false")}, true)");
        return result == "true";
    }

    public async Task FindClearAsync()
    {
        if (!_webViewInitialized) return;
        await WebView.CoreWebView2.ExecuteScriptAsync("window.getSelection().removeAllRanges()");
    }

    /// <summary>
    /// Stops all media and releases the WebView2 process handles before the tab
    /// is removed. Without an explicit Dispose() the underlying browser process
    /// keeps running (and audio/video keeps playing) until the host window is GC'd.
    /// </summary>
    public void CloseTab()
    {
        if (!_webViewInitialized) return;

        try
        {
            if (WebView.CoreWebView2 != null)
            {
                // Mute first so even latency in dispose doesn't leak audio
                WebView.CoreWebView2.IsMuted = true;
                WebView.CoreWebView2.Stop();
                WebView.CoreWebView2.Navigate("about:blank");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] CloseTab navigate-blank failed: {ex.Message}");
        }

        try
        {
            // Releases the underlying CoreWebView2 — terminates the WebView2
            // child process for this tab and stops any active media playback.
            WebView.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] CloseTab WebView dispose failed: {ex.Message}");
        }

        _webViewInitialized = false;
    }

    public void AllowOnce(string domain) => _allowedOnce.Add(domain.ToLowerInvariant());

    /// <summary>Executes arbitrary JavaScript — used by AgentActionExecutor for DOM actions.</summary>
    public async Task<string> ExecuteScriptAsync(string javascript)
    {
        if (!_webViewInitialized) return "";
        try { return await WebView.CoreWebView2.ExecuteScriptAsync(javascript); }
        catch { return ""; }
    }

    /// <summary>Clears cookies and/or cache for this tab's WebView2 profile.</summary>
    public async Task ClearBrowsingDataAsync(bool cookies, bool cache)
    {
        if (!_webViewInitialized) return;
        try
        {
            var profile = WebView.CoreWebView2.Profile;
            if (cookies && cache)
                await profile.ClearBrowsingDataAsync();
            else if (cookies)
                await profile.ClearBrowsingDataAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.Cookies);
            else if (cache)
                await profile.ClearBrowsingDataAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache |
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DownloadHistory);
        }
        catch { /* Profile may not support it on older WebView2 runtimes */ }
    }

    /// <summary>
    /// Sprint 6 — Reads the active page's article text without navigating
    /// away from it. Returns (url, title, content) for VeloAgent priming.
    /// All-empty tuple when extraction fails or the page has no readable
    /// article (e.g. SPA login screens, image galleries).
    /// </summary>
    public async Task<(string Url, string Title, string Content)> GetPageContentAsync()
    {
        if (!_webViewInitialized) return ("", "", "");
        try
        {
            var script = await LoadScriptResourceAsync("reader.js");
            if (script == null) return ("", "", "");

            var jsonResult = await WebView.CoreWebView2.ExecuteScriptAsync(script);
            var jsonStr = JsonSerializer.Deserialize<string>(jsonResult);
            if (string.IsNullOrEmpty(jsonStr)) return ("", "", "");

            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            if (!root.GetProperty("found").GetBoolean()) return ("", "", "");

            var title = root.GetProperty("title").GetString() ?? "";
            var html  = root.GetProperty("html").GetString()  ?? "";

            // Strip HTML tags for the priming prompt — the model doesn't need
            // markup, just the prose.
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            return (WebView.CoreWebView2.Source, title, text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] GetPageContent failed: {ex.Message}");
            return ("", "", "");
        }
    }

    /// <summary>
    /// Extracts article content via reader.js, then navigates to a clean reader page.
    /// Reloading the page exits reader mode (returns to the original content).
    /// </summary>
    public async Task ToggleReaderModeAsync()
    {
        if (!_webViewInitialized) return;
        try
        {
            var extractScript = await LoadScriptResourceAsync("reader.js");
            if (extractScript == null) return;

            var jsonResult = await WebView.CoreWebView2.ExecuteScriptAsync(extractScript);

            // ExecuteScriptAsync wraps the return value in extra JSON quotes — unwrap
            var jsonStr = JsonSerializer.Deserialize<string>(jsonResult);
            if (string.IsNullOrEmpty(jsonStr)) return;

            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            if (!root.GetProperty("found").GetBoolean()) return;

            var title  = root.GetProperty("title").GetString()  ?? "";
            var byline = root.GetProperty("byline").GetString() ?? "";
            var date   = root.GetProperty("date").GetString()   ?? "";
            var html   = root.GetProperty("html").GetString()   ?? "";

            var meta = new[] { byline, date }.Where(s => !string.IsNullOrEmpty(s));
            var metaLine = string.Join(" · ", meta);

            var page = BuildReaderPage(title, metaLine, html);
            WebView.CoreWebView2.NavigateToString(page);
        }
        catch { /* never block navigation */ }
    }

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

    // ── WebView2 event handlers ──────────────────────────────────────────

    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        // Must respond within 100ms — use a deferral
        var deferral = e.GetDeferral();
        try
        {
            await ProcessRequestAsync(e);
        }
        catch { /* never let async void propagate to DispatcherUnhandledException */ }
        finally
        {
            try { deferral.Complete(); } catch { }
            deferral.Dispose();
        }
    }

    private Task ProcessRequestAsync(CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri;
        var referrer = e.Request.Headers.Contains("Referer") ? e.Request.Headers.GetHeader("Referer") : "";
        var resourceType = e.ResourceContext.ToString();

        try
        {
            // 1. RequestGuard (sync — fast)
            var verdict = _requestGuard?.Evaluate(uri, referrer, resourceType);

            if (verdict?.Verdict == VerdictType.Block && !_allowedOnce.Contains(GetHost(uri)))
            {
                e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 403, "Blocked by VELO", "");

                Dispatcher.Invoke(() =>
                    SecurityVerdictReceived?.Invoke(this, new AIVerdict
                    {
                        Verdict    = VerdictType.Block,
                        Reason     = verdict.Reason,
                        ThreatType = verdict.ThreatType,
                        Source     = verdict.Source,
                        Confidence = verdict.Confidence
                    }));
                return Task.CompletedTask;
            }

            // 2. AI Engine — fire-and-forget so it never blocks page load.
            //    Only reached for genuinely suspicious navigation URLs (not sub-resources).
            if (verdict?.NeedsAIAnalysis == true && _aiEngine != null)
            {
                var context = new ThreatContext
                {
                    Domain          = GetHost(uri),
                    ResourceType    = resourceType,
                    Referrer        = referrer,
                    // RequestGuard already flagged this domain as suspicious —
                    // set RiskScore=60 so AISecurityEngine passes the threshold gate
                    RiskScore       = 60
                };
                var capturedEnv = WebView.CoreWebView2.Environment;

                // Run async, don't await — page loads immediately, AI result appears when ready
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                        var aiVerdict = await _aiEngine.AnalyzeAsync(context, cts.Token);

                        if (aiVerdict.IsFallback) return; // no Ollama running — skip silently

                        Dispatcher.Invoke(() =>
                        {
                            if (aiVerdict.Verdict != VerdictType.Safe)
                                SecurityVerdictReceived?.Invoke(this, aiVerdict);
                        });
                    }
                    catch { /* never crash on AI timeout */ }
                });
            }

            // 3 (v2.4.22). SmartBlock async classification — fire-and-forget.
            //              Skipped for main-frame requests (those already get
            //              the AISecurityEngine treatment via NeedsAIAnalysis),
            //              trusted-host CDNs, and sub-resources whose host is
            //              already cached. The classifier owns its own budget
            //              + cache; we just kick the work off and let the next
            //              request to the same host read the verdict via
            //              RequestGuard.TryGetCachedVerdict().
            if (_smartBlock != null && verdict?.Verdict == VerdictType.Safe)
            {
                var host = GetHost(uri);
                if (!string.IsNullOrEmpty(host)
                    && !VELO.Security.Guards.RequestGuard.TrustedHosts.Contains(host)
                    && !resourceType.Equals("Document", StringComparison.OrdinalIgnoreCase)
                    && _smartBlock.TryGetCachedVerdict(host) is null)
                {
                    var refHost = string.IsNullOrEmpty(referrer) ? "" : GetHost(referrer);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            await _smartBlock.ClassifyAsync(host, resourceType, refHost, cts.Token);
                        }
                        catch { /* fire-and-forget; classifier logs its own warnings */ }
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout — let request through, don't block browsing
        }
        catch (Exception ex)
        {
            _ = ex; // Log in production
        }

        return Task.CompletedTask;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var navUri = e.Uri ?? "";

        // ── Banking container: force HTTPS ────────────────────────────────────
        if (_isBankingContainer && BankingContainerPolicy.ShouldBlockHttp(navUri))
        {
            e.Cancel = true;
            var httpsUrl = BankingContainerPolicy.ForceHttps(navUri);
            _ = Dispatcher.InvokeAsync(() => WebView.CoreWebView2.Navigate(httpsUrl));
            return;
        }

        // ── NavGuard: block navigations to known malicious or suspicious domains ─
        // Runs for ALL navigations including first load of a new tab (_currentPageUrl = "")
        if (_requestGuard != null)
        {
            var navHost      = GetHost(navUri);
            var isCrossOrigin = string.IsNullOrEmpty(_currentPageUrl) || !IsSameEtld(navUri, _currentPageUrl);

            if (!string.IsNullOrEmpty(navHost) && isCrossOrigin)
            {
                var guardVerdict = _requestGuard.Evaluate(navUri, _currentPageUrl, "Document");
                if (guardVerdict.Verdict == VerdictType.Block)
                {
                    e.Cancel = true;
                    Dispatcher.Invoke(() =>
                    {
                        SecurityVerdictReceived?.Invoke(this, new AIVerdict
                        {
                            Verdict    = VerdictType.Block,
                            Reason     = $"Navegación bloqueada: '{navHost}' está en la lista de dominios maliciosos o sospechosos",
                            ThreatType = guardVerdict.ThreatType,
                            Source     = "NavGuard",
                            Confidence = 97
                        });
                        LoadingBar.Visibility = Visibility.Collapsed;
                        LoadingChanged?.Invoke(this, false);
                    });
                    return;
                }
            }
        }

        // Track current page for cross-origin detection (update AFTER guard check)
        _currentPageUrl = navUri;

        // Reset burst counter on user-initiated navigations (not iframes/subresources)
        if (e.IsUserInitiated && _downloadGuard != null)
            _downloadGuard.ResetBurst(_tabId);

        // TLSGuard — HSTS quick check (sync, can cancel navigation)
        if (_tlsGuard != null)
        {
            var quickResult = _tlsGuard.QuickCheck(navUri);
            if (quickResult.ShouldRedirect && quickResult.RedirectUrl != null)
            {
                e.Cancel = true;
                _ = Dispatcher.InvokeAsync(() =>
                    WebView.CoreWebView2.Navigate(quickResult.RedirectUrl));
                return;
            }

            var domain = GetHost(navUri);
            _ = Task.Run(() => _tlsGuard.CheckCTLogsAsync(domain, navUri));
        }

        Dispatcher.Invoke(() =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            LoadingChanged?.Invoke(this, true);
            UrlChanged?.Invoke(this, navUri);
        });
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

    private void OnServerCertificateError(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        if (_tlsGuard == null) return;

        // RequestUri is a string in WebView2
        var uri  = e.RequestUri ?? "";
        var host = GetHost(uri);

        var isLocal = host is "localhost" or "127.0.0.1" or "::1"
                      || host.EndsWith(".local")
                      || IPAddress.TryParse(host, out _);

        if (isLocal)
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            return;
        }

        // Map status to booleans without depending on specific enum names
        var statusName   = e.ErrorStatus.ToString();
        var isSelfSigned = statusName.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
                        || statusName.Contains("Mismatch", StringComparison.OrdinalIgnoreCase)
                        || statusName.Contains("Authority", StringComparison.OrdinalIgnoreCase);
        var isExpired    = statusName.Contains("Expir", StringComparison.OrdinalIgnoreCase)
                        || statusName.Contains("Date",   StringComparison.OrdinalIgnoreCase);

        var verdict = _tlsGuard.EvaluateCertError(uri, isSelfSigned, isExpired, isLocal);
        e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; // show WARN, don't hard-block

        Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
        {
            Verdict    = verdict.Verdict,
            Reason     = verdict.Reason,
            ThreatType = verdict.ThreatType,
            Source     = "TLS",
            Confidence = 95
        }));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            var node = JsonNode.Parse(json);
            if (node == null) return;

            var kind = node["kind"]?.GetValue<string>();

            switch (kind)
            {
                case "pasteguard" when _pasteGuard != null:
                {
                    var signal = node["signal"]?.GetValue<string>() ?? "unknown";
                    var domain = GetHost(_currentPageUrl);
                    _pasteGuard.Process(_tabId, domain, signal);
                    break;
                }
                case "glance-show":
                {
                    var url = node["url"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(url))
                        GlanceLinkHovered?.Invoke(this, url);
                    break;
                }
                case "glance-hide":
                    GlanceLinkHovered?.Invoke(this, "");
                    break;
                case "autofill-detect":
                {
                    var h = node["host"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(h))
                        Dispatcher.Invoke(() => AutofillFormDetected?.Invoke(this, h));
                    break;
                }
                case "autofill-submit":
                {
                    var h  = node["host"]?.GetValue<string>() ?? "";
                    var u  = node["username"]?.GetValue<string>() ?? "";
                    var pw = node["password"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(h) && !string.IsNullOrEmpty(pw))
                        Dispatcher.Invoke(() => AutofillFormSubmitted?.Invoke(this, (h, u, pw)));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] WebMessage handler failed: {ex.Message}");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            LoadingChanged?.Invoke(this, false);
            NavigationStateChanged?.Invoke(this, (
                WebView.CoreWebView2.CanGoBack,
                WebView.CoreWebView2.CanGoForward));

            var source = WebView.CoreWebView2.Source;
            UrlChanged?.Invoke(this, source);

            // TLS indicator
            var tlsStatus = source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? TlsStatus.Secure
                : source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    ? TlsStatus.Insecure
                    : TlsStatus.Secure; // velo:// pages are "secure"
            TlsStatusChanged?.Invoke(this, tlsStatus);
        });
    }

    private void OnTitleChanged(object? sender, object e)
        => Dispatcher.Invoke(() =>
            TitleChanged?.Invoke(this, WebView.CoreWebView2.DocumentTitle));

    private void OnLaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
    {
        // Suppress WebView2's default permission dialog — we show our own.
        e.Cancel = true;
        var uri = e.Uri ?? "";
        Dispatcher.Invoke(() => TryLaunchExternalUri(uri));
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // always suppress native window

        var targetUri = e.Uri ?? "";

        // ── Rule 0 (v2.0.5): external custom protocols (bambustudioopen://, …) ─
        //    Hand directly to the OS instead of trying to load as a tab.
        //    Without this, MakerWorld → Bambu Studio handoff silently fails
        //    because WebView2 cannot navigate non-web schemes inside a tab.
        if (IsExternalScheme(targetUri))
        {
            Dispatcher.Invoke(() =>
            {
                if (TryLaunchExternalUri(targetUri))
                {
                    SecurityVerdictReceived?.Invoke(this, new AIVerdict
                    {
                        Verdict    = VerdictType.Safe,
                        Reason     = $"Aplicación externa abierta vía '{GetScheme(targetUri)}://'",
                        Source     = "ExternalProtocol",
                        Confidence = 100
                    });
                }
            });
            return;
        }

        // ── Rule 1: script-initiated popup — never allow ──────────────────
        if (!e.IsUserInitiated)
        {
            Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
            {
                Verdict    = VerdictType.Block,
                Reason     = $"Popup bloqueado: '{ShortenUrl(targetUri)}' fue abierto automáticamente por un script (sin acción del usuario)",
                ThreatType = ThreatType.Malware,
                Source     = "PopupGuard",
                Confidence = 98
            }));
            return;
        }

        // ── Rule 2: popup burst — piracy sites open multiple tabs per click ─
        // v2.4.6 — Two adjustments after a real-world false-positive on
        // github.com (user opened multiple repo links via Ctrl+click and got
        // their own repo flagged as malware/piracy):
        //
        //   • Domains in the ShieldsAllowlist (github.com, gitlab.com, big
        //     retail, banks, gov, etc.) skip this rule entirely. Power users
        //     routinely Ctrl+click sprees on those.
        //   • Threshold bumped from 2 popups → 4 popups in the window for
        //     everything else, so a normal pair-of-tabs Ctrl+click on any
        //     other site doesn't trigger either. Real piracy sites typically
        //     burst 5-10 popups per click; 4 is still well under that.
        var pageHost = GetHost(_currentPageUrl);
        var isAllowlistedSite = _shieldsAllowlist?.Matches(pageHost) ?? false;

        var now = DateTime.UtcNow;
        while (_popupTimes.Count > 0 && (now - _popupTimes.Peek()) > PopupBurstWindow)
            _popupTimes.Dequeue();
        _popupTimes.Enqueue(now);

        const int PopupBurstThreshold = 4;
        var isBurst = !isAllowlistedSite && _popupTimes.Count > PopupBurstThreshold;

        if (isBurst)
        {
            Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
            {
                Verdict    = VerdictType.Block,
                Reason     = $"Popup bloqueado: {_popupTimes.Count} pestañas abiertas en pocos segundos (técnica de sitios de piratería/malware). '{ShortenUrl(targetUri)}' fue bloqueado.",
                ThreatType = ThreatType.Malware,
                Source     = "PopupGuard",
                Confidence = 90
            }));
            return;
        }

        // ── Rule 3: URL in blocklist ──────────────────────────────────────
        if (_requestGuard != null)
        {
            var verdict = _requestGuard.Evaluate(targetUri, _currentPageUrl, "navigation");
            if (verdict.Verdict == VerdictType.Block)
            {
                Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
                {
                    Verdict    = VerdictType.Block,
                    Reason     = $"Enlace bloqueado: '{ShortenUrl(targetUri)}' está en la lista de dominios maliciosos",
                    ThreatType = ThreatType.KnownTracker,
                    Source     = "PopupGuard",
                    Confidence = 97
                }));
                return;
            }
        }

        // ── Rule 4: clean, user-initiated, first popup → allow as new tab ─
        Dispatcher.Invoke(() => UrlChanged?.Invoke(this, $"__newtab:{targetUri}"));
    }

    private static string ShortenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var host = uri.Host;
        var path = uri.AbsolutePath.Length > 20 ? uri.AbsolutePath[..20] + "…" : uri.AbsolutePath;
        return host + path;
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (_downloadManager == null) return;

        // Suppress the default WebView2 download UI
        e.Handled = true;

        var op       = e.DownloadOperation;
        var filePath = op.ResultFilePath;
        var fileName = Path.GetFileName(filePath);

        // ── DownloadGuard evaluation ─────────────────────────────────────
        if (_downloadGuard != null)
        {
            var verdict = _downloadGuard.Evaluate(_tabId, op.Uri, fileName, _currentPageUrl);
            // v2.0.5.12 — Use the download URL's host (not the page tab) so the
            // security panel's "Allow once / Whitelist always" actually whitelists
            // the offending host. Previously the panel derived domain from the
            // tab URL, which is empty for external-launched downloads.
            string downloadHost = "";
            try { downloadHost = new Uri(op.Uri).Host; } catch { }

            if (verdict.Action == DownloadAction.Block)
            {
                // Cancel the download immediately
                e.Cancel = true;

                // Fire security panel warning
                Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
                {
                    Verdict    = VerdictType.Block,
                    Reason     = verdict.Reason,
                    ThreatType = verdict.Threat,
                    Source     = "DownloadGuard",
                    Confidence = 97,
                    Host       = downloadHost,
                }));
                return;
            }

            if (verdict.Action == DownloadAction.Warn)
            {
                // Allow the download but show a warning in the security panel
                Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
                {
                    Verdict    = VerdictType.Warn,
                    Reason     = verdict.Reason,
                    ThreatType = verdict.Threat,
                    Source     = "DownloadGuard",
                    Confidence = 75,
                    Host       = downloadHost,
                }));
                // Fall through — download proceeds
            }
        }

        // ── Register with DownloadManager ────────────────────────────────
        var item = _downloadManager.StartDownload(op.Uri, fileName, filePath, (long)(op.TotalBytesToReceive ?? 0));

        op.BytesReceivedChanged += (_, _) =>
        {
            item.ReceivedBytes = op.BytesReceived;
            if (item.TotalBytes == 0 && op.TotalBytesToReceive.HasValue)
                item.TotalBytes = (long)op.TotalBytesToReceive.Value;
        };

        op.StateChanged += (_, _) =>
        {
            var newState = op.State switch
            {
                CoreWebView2DownloadState.Completed   => DownloadState.Completed,
                CoreWebView2DownloadState.Interrupted => DownloadState.Interrupted,
                _                                     => DownloadState.InProgress
            };
            Serilog.Log.Information("Download StateChanged: {File} → {State}", fileName, newState);
            item.State = newState;
        };

        // v2.4.20 — File-system polling fallback. With e.Handled=true above,
        // WebView2's StateChanged event sometimes never fires Completed
        // (the host is supposed to drive the lifecycle but we suppressed
        // the default UI). Without a Completed transition the app shows
        // "downloading…" forever AND on shutdown Chromium considers the
        // download abandoned and *deletes the final file* — exactly the
        // bug reported on v2.4.19. Polling the disk is authoritative:
        // when <name>.crdownload disappears and <name>.<ext> is present
        // with size == TotalBytes (or any bytes if TotalBytes unknown),
        // the download is finished regardless of what the event says.
        _ = Task.Run(async () =>
        {
            var partialPath = filePath + ".crdownload";
            // 1-hour cap so a stuck download doesn't poll forever.
            var deadline = DateTime.UtcNow.AddHours(1);
            while (DateTime.UtcNow < deadline && item.State == DownloadState.InProgress)
            {
                await Task.Delay(500).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(filePath)) continue;
                    if (File.Exists(partialPath)) continue;
                    var len = new FileInfo(filePath).Length;
                    if (item.TotalBytes > 0 && len < item.TotalBytes) continue;
                    // File present, no .crdownload sibling, size matches (or
                    // total unknown) → declare Completed.
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (item.State == DownloadState.InProgress)
                        {
                            item.State = DownloadState.Completed;
                            if (item.TotalBytes == 0) item.TotalBytes = len;
                            item.ReceivedBytes = len;
                            Serilog.Log.Information(
                                "Download polled-Completed: {File} ({Bytes} bytes)",
                                fileName, len);
                        }
                    });
                    break;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Debug(ex, "Download poll iteration failed for {File}", fileName);
                }
            }
        });
    }

    private void NewTabPage_NavigationRequested(object? sender, string input)
    {
        // Don't call ShowWebView() here — NavigateAsync handles it after initialization
        UrlChanged?.Invoke(this, $"__navigate:{input}");
    }

    // ── Context menu (dark WPF theme) ────────────────────────────────────

    private static readonly Color _menuBg     = Color.FromRgb(0x1E, 0x1E, 0x2E);
    private static readonly Color _menuBorder = Color.FromRgb(0x3A, 0x3A, 0x55);
    private static readonly Color _menuFg     = Color.FromRgb(0xE0, 0xE0, 0xFF);
    private static readonly Color _menuHover  = Color.FromRgb(0x2A, 0x2A, 0x45);
    private static readonly Color _menuMuted  = Color.FromRgb(0x70, 0x70, 0x90);

    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        e.Handled    = true;

        Dispatcher.InvokeAsync(() =>
        {
            // ── v2.4.14: Use the enriched AI builder when wired, falling back
            //    to the Phase 2 plain builder, falling back to the WebView2
            //    native list. Previous gate required _contextMenuBuilder to
            //    be non-null, but MainWindow only ever wires the AI builder
            //    (which already composes ContextMenuBuilder via DI). Result:
            //    the 🤖 IA submenu never appeared in production from Sprint 1E
            //    (v2.1.0) all the way through v2.4.13. Bug latent for the
            //    entire Phase 3 because the fallback path renders perfectly
            //    fine WebView2 items and nobody noticed the IA menu was
            //    missing.
            if (_aiContextMenuBuilder is not null || _contextMenuBuilder is not null)
            {
                var target = e.ContextMenuTarget;
                // v2.4.16 — WebView2's CoreWebView2ContextMenuTargetKind enum
                // doesn't include `Editable`, so detect via the native menu
                // contents instead: WebView2 only adds a "paste" command in
                // editable contexts (input/textarea/contenteditable). Robust
                // across locales because the Name property is the English
                // identifier, not the localised label.
                bool isEditableTarget = e.MenuItems.Any(mi =>
                    string.Equals(mi.Name, "paste", StringComparison.OrdinalIgnoreCase));

                var ctx = new ContextMenuContext(
                    LinkUrl:           target.HasLinkUri    ? target.LinkUri    : null,
                    LinkText:          target.HasLinkText   ? target.LinkText   : null,
                    HasImage:          target.HasSourceUri,
                    ImageUrl:          target.HasSourceUri  ? target.SourceUri  : null,
                    SelectedText:      target.HasSelection  ? target.SelectionText : null,
                    CurrentDomain:     GetHost(_currentPageUrl),
                    CurrentContainerId: _currentContainerId ?? "none",
                    Location:          new System.Windows.Point(e.Location.X, e.Location.Y),
                    IsEditableTarget:  isEditableTarget);

                // AI builder wraps the inner ContextMenuBuilder via DI, so
                // resolving from it gives us both the Phase 2 items and the
                // 🤖 IA submenu (with the v2.4.13 💻 Code branch on selections
                // that look like code).
                // v2.4.19 — pass HandlePasteRequest as per-build onPaste so
                // the paste lands in *this* tab only. Closure captures the
                // current BrowserTab instance; no singleton-event broadcast.
                var enrichedMenu = _aiContextMenuBuilder is not null
                    ? _aiContextMenuBuilder.Build(ctx, onPaste: HandlePasteRequest)
                    : _contextMenuBuilder!.Build(ctx, onPaste: HandlePasteRequest);
                enrichedMenu.PlacementTarget = WebView;
                enrichedMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                enrichedMenu.IsOpen = true;
                deferral.Complete();
                return;
            }

            // ── Fallback: WebView2 default items styled as WPF ───────────
            var menu = new ContextMenu
            {
                Background        = new SolidColorBrush(_menuBg),
                BorderBrush       = new SolidColorBrush(_menuBorder),
                BorderThickness   = new Thickness(1),
                Padding           = new Thickness(0, 4, 0, 4),
                HasDropShadow     = true,
                StaysOpen         = false,
                UseLayoutRounding = true,
                // PlacementTarget is required so WPF can attach the popup to the visual tree
                PlacementTarget   = WebView,
                Placement         = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            // Map WebView2 menu items to styled WPF items
            foreach (var item in e.MenuItems)
            {
                if (item.Kind == CoreWebView2ContextMenuItemKind.Separator)
                {
                    menu.Items.Add(new Separator
                    {
                        Background = new SolidColorBrush(_menuBorder),
                        Margin     = new Thickness(0, 3, 0, 3)
                    });
                    continue;
                }

                if (item.Kind == CoreWebView2ContextMenuItemKind.Submenu)
                    continue; // skip submenus for simplicity

                var label = item.Label.Replace("&", "");
                var shortcut = item.ShortcutKeyDescription ?? "";

                var header = new System.Windows.Controls.Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var labelBlock = new TextBlock
                {
                    Text       = label,
                    Foreground = item.IsEnabled
                        ? new SolidColorBrush(_menuFg)
                        : new SolidColorBrush(_menuMuted),
                    FontSize   = 12.5,
                    FontFamily = new FontFamily("Segoe UI")
                };
                System.Windows.Controls.Grid.SetColumn(labelBlock, 0);
                header.Children.Add(labelBlock);

                if (!string.IsNullOrEmpty(shortcut))
                {
                    var shortBlock = new TextBlock
                    {
                        Text       = shortcut,
                        Foreground = new SolidColorBrush(_menuMuted),
                        FontSize   = 11,
                        FontFamily = new FontFamily("Segoe UI"),
                        Margin     = new Thickness(24, 0, 0, 0)
                    };
                    System.Windows.Controls.Grid.SetColumn(shortBlock, 1);
                    header.Children.Add(shortBlock);
                }

                var mi = new MenuItem
                {
                    Header          = header,
                    IsEnabled       = item.IsEnabled,
                    Background      = new SolidColorBrush(_menuBg),
                    Foreground      = new SolidColorBrush(_menuFg),
                    BorderThickness = new Thickness(0),
                    Padding         = new Thickness(12, 5, 12, 5)
                };

                var capturedId = item.CommandId;
                mi.Click += (_, _) =>
                {
                    e.SelectedCommandId = capturedId;
                    deferral.Complete();
                };

                menu.Items.Add(mi);
            }

            bool completed = false;
            menu.Closed += (_, _) =>
            {
                if (!completed) { completed = true; deferral.Complete(); }
            };

            menu.IsOpen = true;
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void ShowNewTabPage()
    {
        NewTabPageControl.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        TitleChanged?.Invoke(this, LocalizationService.Current.T("newtab.title"));
        NewTabPageControl.Focus();

        // Sprint 6: load top sites from history DB (fire-and-forget; never blocks nav)
        if (_historyRepo != null)
            _ = NewTabPageControl.LoadTopSitesAsync(_historyRepo);
    }

    private void ShowWebView()
    {
        NewTabPageControl.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;
    }

    private async Task EnsureWebViewReadyAsync()
    {
        if (!_webViewInitialized)
            throw new InvalidOperationException("WebView not initialized. Call EnsureWebViewInitializedAsync first.");
        await Task.CompletedTask;
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
