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

    // Fase 2: enriched context menu (optional — falls back to WebView2 default if null)
    private ContextMenuBuilder? _contextMenuBuilder;

    public void SetContextMenuBuilder(ContextMenuBuilder builder) => _contextMenuBuilder = builder;

    // Fase 2: banking-mode flag (set by caller after Initialize)
    private bool _isBankingContainer;

    // Fase 2: PasteGuard
    private PasteGuard? _pasteGuard;

    // Fase 2: Sprint 6 — history repo for NewTab v2 top sites
    private HistoryRepository? _historyRepo;

    public void SetPasteGuard(PasteGuard guard) => _pasteGuard = guard;

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
        catch { }
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
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        WebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        WebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        // Cookie consent auto-dismiss (embedded — no external files)
        await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ConsentScript);

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
        catch { }

        // Glance modal — hover-link preview
        try
        {
            var glanceScript = await LoadScriptResourceAsync("glance-hover.js");
            if (glanceScript != null)
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(glanceScript);
        }
        catch { }

        // Banking container — inject strict fingerprint spoofing + no-referrer
        if (_isBankingContainer)
        {
            try
            {
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BankingContainerPolicy.FingerprintScript);
            }
            catch { }
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
            TitleChanged?.Invoke(this, "Acerca de VELO");
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
        var version = System.Reflection.Assembly
            .GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        return BuildAboutPageTemplate().Replace("VELO_VERSION_PLACEHOLDER", version);
    }

    private static string BuildAboutPageTemplate() => """
        <!DOCTYPE html>
        <html lang="es">
        <head>
        <meta charset="utf-8"/>
        <title>Acerca de VELO</title>
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
            <div class="meta">
              Construido con C# · .NET 8 · WPF · Microsoft WebView2<br/>
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

    /// <summary>Stops all media before the tab is removed from the visual tree.</summary>
    public void CloseTab()
    {
        if (!_webViewInitialized) return;
        try { WebView.CoreWebView2?.Navigate("about:blank"); } catch { }
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
            }
        }
        catch { }
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

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // always suppress native window

        var targetUri = e.Uri ?? "";

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
        // Even when IsUserInitiated=true, >1 popup in 3s is abusive behavior
        var now = DateTime.UtcNow;
        while (_popupTimes.Count > 0 && (now - _popupTimes.Peek()) > PopupBurstWindow)
            _popupTimes.Dequeue();

        var isBurst = _popupTimes.Count >= 1; // 2nd+ popup within burst window
        _popupTimes.Enqueue(now);

        if (isBurst)
        {
            Dispatcher.Invoke(() => SecurityVerdictReceived?.Invoke(this, new AIVerdict
            {
                Verdict    = VerdictType.Block,
                Reason     = $"Popup bloqueado: múltiples pestañas abiertas en pocos segundos (técnica de sitios de piratería/malware). '{ShortenUrl(targetUri)}' fue bloqueado.",
                ThreatType = ThreatType.Malware,
                Source     = "PopupGuard",
                Confidence = 95
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
                    Confidence = 97
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
                    Confidence = 75
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
            item.State = op.State switch
            {
                CoreWebView2DownloadState.Completed   => DownloadState.Completed,
                CoreWebView2DownloadState.Interrupted => DownloadState.Interrupted,
                _                                     => DownloadState.InProgress
            };
        };
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
            // ── Fase 2: use enriched ContextMenuBuilder if available ──────
            if (_contextMenuBuilder is not null)
            {
                var target = e.ContextMenuTarget;
                var ctx = new ContextMenuContext(
                    LinkUrl:           target.HasLinkUri    ? target.LinkUri    : null,
                    LinkText:          target.HasLinkText   ? target.LinkText   : null,
                    HasImage:          target.HasSourceUri,
                    ImageUrl:          target.HasSourceUri  ? target.SourceUri  : null,
                    SelectedText:      target.HasSelection  ? target.SelectionText : null,
                    CurrentDomain:     GetHost(_currentPageUrl),
                    CurrentContainerId: _currentContainerId ?? "none",
                    Location:          new System.Windows.Point(e.Location.X, e.Location.Y));

                var enrichedMenu = _contextMenuBuilder.Build(ctx);
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
