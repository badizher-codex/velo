using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using VELO.Core.Containers;
using VELO.Core.Downloads;
using VELO.Data.Repositories;
using VELO.Security.AI;
using VELO.Security.AI.Models;
using VELO.Security.Guards;
using VELO.Vault;

namespace VELO.UI.Controls;

// Phase 3 / Sprint 10b chunk 6 (v2.4.31) — Core partition.
// Owns the UserControl declaration, public event surface, private state,
// DI setters, constructor, and the WebView2 lifecycle methods (Initialize +
// EnsureWebViewInitializedAsync, which subscribes the handlers defined in
// BrowserTab.Events.cs). Public methods live in BrowserTab.PublicApi.cs;
// pure helpers and the external-protocol cluster live in BrowserTab.Helpers.cs.
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
    // v2.4.24 — Sprint 8B signal. Set true when the injected autofill.js
    // reports a password-bearing form on the active page; reset on every
    // navigation start. Surfaced to PhishingShield via ThreatContext so the
    // model can use "this page asks for a password" as a risk amplifier.
    private bool   _hasLoginFormOnCurrentPage = false;
    // v2.4.26 — Last DocumentTitle reported by WebView2. Reset on every
    // navigation start, updated by OnTitleChanged. Surfaced to PhishingShield
    // via ThreatContext.PageTitle so the model can spot "PayPal — Sign in"
    // titled pages on non-PayPal hosts (highest-yield phishing signal).
    private string _currentPageTitle    = "";
    private string _currentContainerId  = "none";

    // Popup burst: tracks timestamps of recent new-window requests per tab
    private readonly Queue<DateTime> _popupTimes = new();
    private static readonly TimeSpan PopupBurstWindow = TimeSpan.FromSeconds(3);

    // External URI scheme launch cluster (_webSchemes, _allowedExternalSchemes,
    // IsExternalScheme, GetScheme, _lastLaunchedUri, _lastLaunchedAt,
    // ExternalLaunchDedupWindow, TryLaunchExternalUri) lives in
    // BrowserTab.Helpers.cs (v2.4.31).

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

    // HandlePasteRequest + PasteTextAsync + PasteTextIntoFocusedEditableAsync
    // live in BrowserTab.PublicApi.cs (v2.4.31).
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

    /// <summary>Provides the history repository so NewTab v2 can load top sites.</summary>
    public void SetHistoryRepository(HistoryRepository repo) => _historyRepo = repo;

    // FillCredentialAsync, OpenDevTools, SetContainer live in BrowserTab.PublicApi.cs (v2.4.31).

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

        // Hooks — handler bodies live in BrowserTab.Events.cs (v2.4.31).
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

    // Public API methods (NavigateAsync, GoBack/Forward/Reload/Stop, Zoom*, Find*,
    // CloseTab, AllowOnce, ExecuteScriptAsync, ClearBrowsingDataAsync,
    // GetPageContentAsync, ToggleReaderModeAsync, paste cluster, FillCredentialAsync,
    // OpenDevTools, SetContainer, view-switching helpers) live in BrowserTab.PublicApi.cs.
    //
    // WebView2 event handlers (OnWebResourceRequested, OnNavigationStarting,
    // OnServerCertificateError, OnWebMessageReceived, OnNavigationCompleted,
    // OnTitleChanged, OnLaunchingExternalUriScheme, OnNewWindowRequested,
    // OnDownloadStarting, NewTabPage_NavigationRequested, OnContextMenuRequested)
    // live in BrowserTab.Events.cs.
    //
    // Pure helpers (statics, external-launch cluster, page builders,
    // ConsentScript, LoadScriptResourceAsync) live in BrowserTab.Helpers.cs.
}
