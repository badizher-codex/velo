using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;
using VELO.Vault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using VELO.Agent;
using VELO.App.Startup;
using VELO.Core;
using VELO.Core.Downloads;
using VELO.Core.Events;
using VELO.Core.Localization;
using VELO.Core.Navigation;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Security;
using VELO.Security.AI;
using VELO.Security.AI.Models;
using VELO.Security.GoldenList;
using VELO.Security.Guards;
using VELO.Security.Models;
using VELO.UI.Controls;
using VELO.UI.Dialogs;
using InputDialog = VELO.UI.Dialogs.InputDialog;

namespace VELO.App;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly TabManager _tabManager;
    private readonly NavigationController _navController;
    private readonly AISecurityEngine _aiEngine;
    private readonly RequestGuard _requestGuard;
    private readonly TLSGuard _tlsGuard;
    private readonly DownloadGuard _downloadGuard;
    private readonly SettingsRepository _settings;
    private readonly EventBus _eventBus;
    private readonly DownloadManager _downloadManager;
    private readonly AgentLauncher _agentLauncher;
    private readonly AgentActionSandbox _agentSandbox;
    private readonly AgentActionExecutor _agentExecutor;

    private CoreWebView2Environment? _webViewEnv;
    private string _fingerprintLevel = "Aggressive";
    private string _webRtcMode       = "Relay";
    private readonly Dictionary<string, BrowserTab> _browserTabs = [];
    private readonly HashSet<string> _navigatedTabs = [];
    // Remembers the last active tab per workspace so switching workspaces
    // restores focus to where the user was (Arc-like UX), instead of always
    // jumping to the topmost tab.
    private readonly Dictionary<string, string> _lastActiveTabPerWorkspace = [];
    // Per-tab block counters — reset on each new navigation
    private readonly Dictionary<string, (int Blocked, int Trackers, int Malware)> _tabBlockCounts = [];
    // Per-tab flag: a new Malwaredex monster was captured during this navigation
    private readonly HashSet<string> _tabNewCapture = [];
    // Highest verdict severity already shown per tab — prevents re-showing for same navigation
    // 0=none, 1=Warn, 2=Block
    private readonly Dictionary<string, int> _tabVerdictLevel = [];
    // Threat types already in the Malwaredex (loaded at startup to avoid repeated DB checks)
    private HashSet<string> _capturedThreatTypes = [];
    private DownloadsWindow? _downloadsWindow;
    private string _initialUrl = "velo://newtab";

    // ── Split view state ─────────────────────────────────────────────────
    private bool _isSplitMode;
    private string? _primaryTabId;   // left pane
    private string? _splitTabId;     // right pane
    private GridSplitter? _panesSplitter;
    // Suppresses normal tab-activation logic while we are setting up the secondary pane
    private bool _suppressSplitActivation;

    // ── Sprint 6 — Security Inspector + Shield Score ─────────────────────
    // Last known TLS status per tab (UI enum from BrowserTab events)
    private readonly Dictionary<string, TlsStatus> _tabTlsStatus = [];
    // Last AI verdict per tab (null = no verdict yet this navigation)
    private readonly Dictionary<string, AIVerdict?> _tabLastAiVerdict = [];
    // Singleton inspector window (non-modal, stays open)
    private SecurityInspectorWindow? _inspectorWindow;

    public MainWindow(IServiceProvider services, string? initialUrl = null)
    {
        if (initialUrl != null) _initialUrl = initialUrl;
        _services = services;
        _tabManager       = services.GetRequiredService<TabManager>();
        _navController    = services.GetRequiredService<NavigationController>();
        _aiEngine         = services.GetRequiredService<AISecurityEngine>();
        _requestGuard     = services.GetRequiredService<RequestGuard>();
        _tlsGuard         = services.GetRequiredService<TLSGuard>();
        _downloadGuard    = services.GetRequiredService<DownloadGuard>();
        _settings         = services.GetRequiredService<SettingsRepository>();
        _eventBus         = services.GetRequiredService<EventBus>();
        _downloadManager  = services.GetRequiredService<DownloadManager>();
        _downloadManager.DownloadStarted += (_, _) => Dispatcher.Invoke(OpenDownloads);
        _agentLauncher    = services.GetRequiredService<AgentLauncher>();
        _agentSandbox     = services.GetRequiredService<AgentActionSandbox>();
        _agentExecutor    = services.GetRequiredService<AgentActionExecutor>();

        InitializeComponent();

        _eventBus.Subscribe<TabCreatedEvent>(OnTabCreated);
        _eventBus.Subscribe<TabClosedEvent>(OnTabClosed);
        _eventBus.Subscribe<TabActivatedEvent>(OnTabActivated);
        _eventBus.Subscribe<PasteGuardTriggeredEvent>(OnPasteGuardTriggered);
        _eventBus.Subscribe<ContainerDestroyedEvent>(OnContainerDestroyed);

        UrlBarControl.BookmarkRequested    += async (_, _) => await ToggleBookmarkAsync();
        UrlBarControl.ZoomResetRequested   += (_, _) => ActiveBrowserTab()?.ResetZoom();
        UrlBarControl.ReaderModeRequested  += async (_, _) =>
            { if (ActiveBrowserTab() is { } bt) await bt.ToggleReaderModeAsync(); };
        UrlBarControl.ShieldScoreClicked   += (_, _) => OpenSecurityInspector();

        // "Aprender más" en el panel de seguridad → navegar a documentación.
        // v2.0.5.12 — Old mapping pointed at docs/threats/{slug}.md which never
        // existed (404). Point at the real THREAT_MODEL.md doc with the slug as
        // anchor; even if the anchor is unknown, the page loads and the user
        // lands on the right reference instead of a GitHub 404.
        SecurityPanelControl.LearnMoreRequested += (_, url) =>
        {
            if (string.IsNullOrEmpty(url)) return;
            var slug = url.StartsWith("velo://docs/threats/")
                ? url["velo://docs/threats/".Length..]
                : "";
            var target = string.IsNullOrEmpty(slug)
                ? "https://github.com/badizher-codex/velo/blob/main/docs/THREAT_MODEL.md"
                : $"https://github.com/badizher-codex/velo/blob/main/docs/THREAT_MODEL.md#{slug}";
            _ = ActiveBrowserTab()?.NavigateAsync(target);
        };

        // Sidebar: seed default workspace
        TabSidebarControl.AddWorkspace(Workspace.Default);

        // Phase 3 / Sprint 1 — Threats Panel v3 wiring.
        var vmThreats   = _services.GetRequiredService<VELO.Security.Threats.ThreatsPanelViewModel>();
        var explainerSvc = _services.GetRequiredService<VELO.Security.Threats.BlockExplanationService>();
        ThreatsPanelControl.SetServices(vmThreats, explainerSvc);
        SecurityPanelControl.MiniTabClicked += (_, _) =>
            ThreatsPanelControl.Visibility = ThreatsPanelControl.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        // Phase 3 / Sprint 1E — Context Menu IA wiring.
        var aiActions = _services.GetRequiredService<VELO.Agent.AIContextActions>();
        aiActions.ChatDelegate = WireAgentChat;
        // Adapter name + capability come from AgentLauncher's adapter list. The
        // launcher picks the best available; we mirror its name into the chip
        // shown by AIResultWindow so the user always sees who answered.
        aiActions.AdapterName = "Local";
        aiActions.SupportsVision = false;

        var aiBuilder = _services.GetRequiredService<VELO.UI.Controls.AIContextMenuBuilder>();
        aiBuilder.AIActionRequested += (_, invocation) =>
        {
            var win = new VELO.UI.Dialogs.AIResultWindow { Owner = this };
            win.Show(invocation.ActionLabel,
                     invocation.SourceContext,
                     invocation.AdapterName,
                     invocation.IsCloud,
                     invocation.Generator);
        };

        // BlockExplanationService → AgentLauncher chat path. Without this the
        // service silently falls back to static templates. The wired delegate
        // sends an open-ended prompt and reads back the assistant's plain
        // reply, ignoring any structured actions the model may include.
        explainerSvc.ChatDelegate = WireAgentChat;

        // VeloAgent panel wiring
        AgentPanelControl.SetServices(_agentLauncher, _agentSandbox);

        // Phase 3 / Sprint 6 — slash commands + page priming.
        var slashRouter = _services.GetRequiredService<VELO.Agent.SlashCommandRouter>();
        var pageCtx     = _services.GetRequiredService<VELO.Agent.PageContextManager>();
        // Provide live page-content lookup so slash commands operate on the
        // active tab's reader-extracted text.
        slashRouter.PageContentProvider = () =>
            ActiveBrowserTab() is { } bt
                ? bt.GetPageContentAsync().GetAwaiter().GetResult().Content
                : "";
        AgentPanelControl.SetSlashServices(slashRouter, pageCtx);
        AgentPanelControl.AskAboutPageRequested += async (_, _) =>
        {
            if (ActiveBrowserTab() is not { } bt) return;
            var (url, title, content) = await bt.GetPageContentAsync();
            if (string.IsNullOrEmpty(content))
            {
                MessageBox.Show(this,
                    "No se encontró contenido legible en esta página. Prueba en una página de artículo.",
                    "VeloAgent", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AgentPanelControl.PrimeWithPage(url, title, content);
            AgentPanelControl.Visibility = Visibility.Visible;
        };
        _agentExecutor.ScriptExecutor = async (tabId, js) =>
        {
            if (_browserTabs.TryGetValue(tabId, out var bt))
                return await bt.ExecuteScriptAsync(js);
            return "";
        };
        // v2.0.5.12 — Lets ReadPage/Summarize actually re-prompt the LLM
        // with the extracted page text instead of no-op'ing on approval.
        _agentExecutor.FollowUpPrompt = (tabId, promptKind, pageText) =>
        {
            var tabInfo = _tabManager.GetTab(tabId);
            if (tabInfo == null) return;
            var url = tabInfo.Url ?? "";
            var host = "";
            try { host = new Uri(url).Host; } catch { }
            var ctx = new VELO.Agent.Models.AgentContext
            {
                CurrentUrl      = url,
                CurrentDomain   = host,
                PageTitle       = tabInfo.Title ?? "",
                PageTextSnippet = pageText,
                ContainerId     = tabInfo.ContainerId,
                OpenTabCount    = _browserTabs.Count,
            };
            _agentLauncher.SendAsync(tabId, promptKind, ctx);
        };
        _agentSandbox.ActionApproved += async (tabId, action) =>
            await _agentExecutor.ExecuteAsync(tabId, action);

        // (Ctrl+Shift+A is handled in OnPreviewKeyDown)

        // v2.0.5.3 — Localise the find bar + react to runtime language changes
        ApplyFindBarLanguage();
        VELO.Core.Localization.LocalizationService.Current.LanguageChanged += ApplyFindBarLanguage;
        Closed += (_, _) => VELO.Core.Localization.LocalizationService.Current.LanguageChanged -= ApplyFindBarLanguage;

        Loaded += async (_, _) =>
        {
            // Pre-load already-captured threat types so capture logic is O(1)
            var mdex = _services.GetRequiredService<MalwaredexRepository>();

            // Remove any false-positive Malwaredex entries for trusted CDN/hosting domains
            // (e.g. github.com flagged by AWS S3 pre-signed URL params in previous builds)
            await mdex.PurgeFalsePositivesAsync(VELO.Security.Guards.RequestGuard.TrustedHosts);

            _capturedThreatTypes = await mdex.GetCapturedTypesAsync();

            try { await OnLoadedAsync(); }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Fatal error in MainWindow.OnLoadedAsync");
                MessageBox.Show($"Error al cargar la ventana principal:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                    "VELO — Error crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(1);
            }
        };
    }

    private async Task OnLoadedAsync()
    {
        // Initialize WebView2 environment with zero-telemetry flags
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = string.Join(" ",
                "--disable-features=msEdgeSidebarV2",
                "--disable-features=EdgeShoppingAssistant",
                "--disable-features=EdgeCollections",
                "--disable-crash-reporter",
                "--disable-breakpad",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-background-networking",
                "--disable-client-side-phishing-detection",
                "--disable-sync",
                "--disable-translate",
                "--disable-extensions",
                "--disable-plugins",
                "--metrics-recording-only",
                "--disable-logging",
                "--disable-hang-monitor",
                "--disable-prompt-on-repost",
                "--disable-domain-reliability",
                "--disable-component-update")
        };

        var userDataPath = DataLocation.SubPath("Profile");

        _webViewEnv = await CoreWebView2Environment.CreateAsync(null, userDataPath, options);
        GlancePopupControl.SetEnvironment(_webViewEnv);

        // Cache privacy settings used by every new tab
        _fingerprintLevel = await _settings.GetAsync(SettingKeys.FingerprintLevel, "Aggressive");
        _webRtcMode       = await _settings.GetAsync(SettingKeys.WebRtcMode, "Relay");

        // Set AI status indicator — ping Ollama in background, update dot when result arrives
        await RefreshAiStatusAsync();

        // Restore persisted workspaces (replaces the in-memory Default seed)
        await RestoreWorkspacesAsync();

        // Start background update checker only if the user opted in.
        // Privacy-first: default OFF. The only network call VELO makes "on its own"
        // is this one, and it must be explicit user choice. Setting key:
        // "updates.auto_check" — toggled from Settings → Privacy.
        var settings = _services.GetRequiredService<SettingsRepository>();
        var autoCheck = await settings.GetBoolAsync("updates.auto_check", defaultValue: false);
        if (autoCheck)
        {
            var updater = _services.GetRequiredService<UpdateChecker>();
            updater.UpdateAvailable += info => Dispatcher.Invoke(() => ShowUpdateToast(info));
            updater.StartBackgroundCheck();
        }

        // Phase 3 / Sprint 3 — Session restore. Runs before the first
        // CreateTab so a restored session replaces (not stacks on top of)
        // the auto-created newtab.
        await InitSessionRestoreAsync();

        // Create initial tab (uses URL injected for tear-off windows, otherwise newtab).
        // Skip when session restore already populated tabs.
        if (_tabManager.Tabs.Count == 0)
            _tabManager.CreateTab(_initialUrl);
    }

    private async void ShowUpdateToast(VELO.Core.Updates.UpdateInfo info)
    {
        // Non-intrusive: only show once per session, and only if user hasn't dismissed
        if (_updateToastShown) return;
        _updateToastShown = true;

        // Phase 3 / Sprint 2 — three-button flow:
        //   Yes        → Download + verify SHA256 + run installer (silent).
        //   No         → Open the release page in a browser tab (legacy behaviour).
        //   Cancel     → Dismiss until next launch.
        var result = MessageBox.Show(this,
            $"VELO {info.LatestVersion} está disponible (tienes {info.CurrentVersion}).\n\n" +
            $"¿Descargar e instalar ahora?\n\n" +
            $"  Sí     → VELO descarga el instalador, verifica el hash SHA256 y lo ejecuta.\n" +
            $"  No     → Abrir la página de la release en tu pestaña.\n" +
            $"  Cancelar → No hacer nada.",
            "Actualización disponible",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Cancel) return;
        if (result == MessageBoxResult.No)
        {
            _ = ActiveBrowserTab()?.NavigateAsync(info.DownloadUrl)
                ?? Task.Run(() =>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = info.DownloadUrl,
                        UseShellExecute = true
                    }));
            return;
        }

        // Yes: secure auto-update flow.
        var downloader = new VELO.Core.Updates.UpdateDownloader(
            logger: _services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VELO.Core.Updates.UpdateDownloader>>());
        var progress = MessageBox.Show(this,
            "Descargando actualización… Esto puede tardar un minuto. Puedes seguir usando VELO mientras tanto.",
            "VELO Update",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        // (The MessageBox is acknowledgement-only; the download itself runs async.)
        var verify = await downloader.DownloadAndVerifyAsync(info);

        if (!verify.Success)
        {
            MessageBox.Show(this,
                $"❌ La descarga falló o el hash no coincide.\n\n" +
                $"Razón: {verify.Error}\n\n" +
                $"Intenta de nuevo desde la página de Releases en GitHub.",
                "VELO Update — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Hash verified — confirm with the user before running the installer.
        var confirm = MessageBox.Show(this,
            $"✅ Descarga verificada.\n\n" +
            $"SHA256: {verify.ActualHashHex}\n\n" +
            $"⚠ Este instalador NO tiene firma Authenticode. Windows SmartScreen mostrará una advertencia.\n" +
            $"Esto es normal mientras VELO no tenga un certificado comercial.\n\n" +
            $"¿Instalar ahora? VELO se cerrará y volverá a abrir tras la instalación.",
            "VELO Update — Listo para instalar",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        if (downloader.ExecuteInstaller(verify.FilePath))
        {
            // Installer takes over from here — quit so /CLOSEAPPLICATIONS
            // doesn't have to ask Inno to wait on us.
            Application.Current.Shutdown();
        }
    }
    private bool _updateToastShown;

    // ── Tab events ───────────────────────────────────────────────────────

    private void OnTabCreated(TabCreatedEvent e)
    {
        Dispatcher.Invoke(async () =>
        {
            var tab = _tabManager.GetTab(e.TabId)!;
            var browserTab = new BrowserTab();
            browserTab.Initialize(e.TabId, _aiEngine, _requestGuard, _tlsGuard, _downloadGuard, _downloadManager, _fingerprintLevel, _webRtcMode);
            browserTab.TlsStatusChanged += (_, status) => Dispatcher.Invoke(() =>
            {
                // Store per-tab (used by Security Inspector)
                _tabTlsStatus[e.TabId] = status;

                if (IsUiDrivingTab(e.TabId))
                {
                    UrlBarControl.SetTlsStatus(status);
                    // Recompute shield score after TLS status is known
                    RefreshShieldScore(e.TabId);
                }
            });
            browserTab.Visibility = Visibility.Collapsed;

            browserTab.UrlChanged += (_, url) => OnBrowserUrlChanged(e.TabId, url);
            browserTab.TitleChanged += (_, title) => OnBrowserTitleChanged(e.TabId, title);
            browserTab.LoadingChanged += (_, loading) => OnBrowserLoadingChanged(e.TabId, loading);
            browserTab.NavigationStateChanged += (_, state) => OnNavigationStateChanged(e.TabId, state);
            browserTab.SecurityVerdictReceived += (_, verdict) => OnSecurityVerdict(e.TabId, verdict);
            browserTab.ZoomChanged += (_, factor) => Dispatcher.Invoke(() =>
            {
                if (IsUiDrivingTab(e.TabId))
                    UrlBarControl.SetZoom(factor);
            });

            browserTab.GlanceLinkHovered += (_, url) => Dispatcher.Invoke(() =>
            {
                if (!IsUiDrivingTab(e.TabId)) return;   // only primary pane drives Glance
                if (string.IsNullOrEmpty(url))
                    GlancePopupControl.HidePreview();
                else
                    ShowGlanceAt(url);
            });

            // Sprint 6: inject history repo so NewTab v2 can show top sites
            var histRepo = _services.GetRequiredService<HistoryRepository>();
            browserTab.SetHistoryRepository(histRepo);

            // Phase 3 / Sprint 1E — wire the AI context-menu builder so the
            // 🤖 IA submenu appears on right-click.
            browserTab.SetAIContextMenuBuilder(
                _services.GetRequiredService<VELO.UI.Controls.AIContextMenuBuilder>());

            // v2.1.5.1 — Shields allowlist (per-site fingerprint/WebRTC relax).
            browserTab.SetShieldsAllowlist(
                _services.GetRequiredService<VELO.Security.Guards.ShieldsAllowlist>());

            // Phase 3 / Sprint 5 — autofill prompt + save-on-submit
            var autofill = _services.GetRequiredService<VELO.Vault.AutofillService>();
            browserTab.SetAutofillService(autofill);

            browserTab.AutofillFormDetected += (_, host) =>
                _ = OnAutofillFormDetectedAsync(e.TabId, host, autofill);

            browserTab.AutofillFormSubmitted += (_, payload) =>
                _ = OnAutofillFormSubmittedAsync(e.TabId, payload, autofill);

            // Add to panel (keeps WebView2 HWND alive across tab switches)
            BrowserContent.Children.Add(browserTab);
            _browserTabs[e.TabId] = browserTab;
            TabSidebarControl.AddTab(tab);

            if (_webViewEnv != null)
                await browserTab.EnsureWebViewInitializedAsync(_webViewEnv);
        });
    }

    private void OnTabClosed(TabClosedEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            _navigatedTabs.Remove(e.TabId);
            if (_browserTabs.Remove(e.TabId, out var bt))
            {
                bt.CloseTab();
                BrowserContent.Children.Remove(bt);
            }
            TabSidebarControl.RemoveTab(e.TabId);

            // If a split pane's tab was closed, tear down the split
            if (_isSplitMode && (e.TabId == _primaryTabId || e.TabId == _splitTabId))
                DeactivateSplit(closingTabId: e.TabId);
        });
    }

    private void OnPasteGuardTriggered(PasteGuardTriggeredEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsUiDrivingTab(e.TabId)) return;

            var signalText = e.SignalType switch
            {
                "clipboard-read"    => "intentó leer tu portapapeles",
                "execcommand-paste" => "intentó acceder al portapapeles (execCommand)",
                _                   => "monitoreó eventos del portapapeles",
            };

            SecurityPanelControl.Show(e.Domain, new VELO.Security.AI.Models.AIVerdict
            {
                Verdict    = VELO.Security.AI.Models.VerdictType.Warn,
                Reason     = $"PasteGuard: {e.Domain} {signalText}",
                ThreatType = VELO.Security.AI.Models.ThreatType.Fingerprinting,
                Source     = "PasteGuard",
                Confidence = 90,
            });
        });
    }

    private void OnContainerDestroyed(ContainerDestroyedEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            var tabsInContainer = _tabManager.Tabs
                .Where(t => t.ContainerId == e.ContainerId)
                .Select(t => t.Id)
                .ToList();

            foreach (var tabId in tabsInContainer)
                _tabManager.CloseTab(tabId);
        });
    }

    // ── Phase 3 / Sprint 5 — Autofill handlers ──────────────────────────────

    private bool _autofillToastShownForHost;
    private string _autofillLastHost = "";

    private async Task OnAutofillFormDetectedAsync(string tabId, string host, VELO.Vault.AutofillService autofill)
    {
        try
        {
            // Only the foreground tab gets to drive the toast, and we only
            // show it once per host visit to avoid spam from MutationObserver.
            if (!IsUiDrivingTab(tabId)) return;
            if (string.IsNullOrEmpty(host)) return;
            if (_autofillToastShownForHost && string.Equals(_autofillLastHost, host, StringComparison.OrdinalIgnoreCase)) return;

            var suggestions = await autofill.GetSuggestionsAsync(host);
            if (suggestions.Count == 0) return;

            // First suggestion wins (already ordered by exact-host then username).
            var pick = suggestions[0];
            var entry = await autofill.ResolveCredentialAsync(pick.Id, host);
            if (entry == null) return;

            _autofillLastHost = host;
            _autofillToastShownForHost = true;

            await Dispatcher.InvokeAsync(() =>
            {
                EventHandler? acceptedHandler = null;
                EventHandler? dismissedHandler = null;

                acceptedHandler = async (_, _) =>
                {
                    AutofillToastControl.Accepted -= acceptedHandler;
                    AutofillToastControl.Dismissed -= dismissedHandler;
                    if (_browserTabs.TryGetValue(tabId, out var bt))
                        await bt.FillCredentialAsync(entry.Username, entry.Password);
                };
                dismissedHandler = (_, _) =>
                {
                    AutofillToastControl.Accepted -= acceptedHandler;
                    AutofillToastControl.Dismissed -= dismissedHandler;
                };

                AutofillToastControl.Accepted  += acceptedHandler;
                AutofillToastControl.Dismissed += dismissedHandler;
                AutofillToastControl.Show(AutofillToast.Mode.UseSaved, entry.Username, host);
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Autofill detect handler failed");
        }
    }

    private async Task OnAutofillFormSubmittedAsync(
        string tabId,
        (string Host, string Username, string Password) payload,
        VELO.Vault.AutofillService autofill)
    {
        try
        {
            if (!IsUiDrivingTab(tabId)) return;

            var outcome = await autofill.SaveNewCredentialAsync(
                payload.Host, payload.Username, payload.Password, autoDetected: true);

            // Fire-and-forget HIBP check — never block the UI thread.
            var breach = await autofill.CheckBreachAsync(payload.Password);

            if (outcome is VELO.Vault.SaveOutcome.Created or VELO.Vault.SaveOutcome.Updated)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AutofillToastControl.Show(
                        AutofillToast.Mode.SaveNew,
                        payload.Username,
                        payload.Host,
                        breachCount: breach.Count);
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Autofill submit handler failed");
        }
    }

    // ── VeloAgent panel handlers ─────────────────────────────────────────────

    private void AgentPanel_CloseRequested(object sender, EventArgs e)
        => AgentPanelControl.Visibility = Visibility.Collapsed;

    private void AgentPanel_ClearRequested(object sender, EventArgs e)
    { /* history already cleared inside the panel */ }

    private void UpdateAgentContext(string tabId)
    {
        var tab = _tabManager.GetTab(tabId);
        if (tab == null) return;

        AgentPanelControl.SetTabContext(tabId, new VELO.Agent.Models.AgentContext
        {
            CurrentUrl    = tab.Url,
            CurrentDomain = ExtractDomain(tab.Url),
            PageTitle     = tab.Title ?? "",
            ContainerId   = tab.ContainerId,
            OpenTabCount  = _tabManager.Tabs.Count,
        });
    }

    /// <summary>True when <paramref name="tabId"/> is the tab whose navigation should drive the URL bar.</summary>
    private bool IsUiDrivingTab(string tabId) =>
        _isSplitMode ? tabId == _primaryTabId : tabId == _tabManager.ActiveTab?.Id;

    private static string ExtractDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        return uri.Host;
    }

    private void OnTabActivated(TabActivatedEvent e)
    {
        Dispatcher.Invoke(async () =>
        {
            if (!_browserTabs.TryGetValue(e.TabId, out var browserTab)) return;

            // Remember this as the last-active tab of its workspace so switching
            // workspaces later restores focus here.
            var activatedTab = _tabManager.GetTab(e.TabId);
            if (activatedTab != null)
                _lastActiveTabPerWorkspace[activatedTab.WorkspaceId] = e.TabId;

            // ── Split-mode path ───────────────────────────────────────────
            if (_isSplitMode)
            {
                if (_suppressSplitActivation)
                {
                    // This activation belongs to the secondary tab being set up —
                    // capture its ID and bail; ActivateSplit() will call RefreshSplitLayout.
                    _splitTabId = e.TabId;
                    return;
                }

                // User switched tabs via the sidebar while split is live:
                // • clicking the current secondary  → swap left ↔ right
                // • clicking any other tab          → that tab becomes primary
                if (e.TabId == _splitTabId && _primaryTabId != null)
                    (_primaryTabId, _splitTabId) = (_splitTabId, _primaryTabId);
                else
                    _primaryTabId = e.TabId;

                RefreshSplitLayout();
                await UpdatePrimaryUiAsync();
                return;
            }

            // ── Single-tab path ───────────────────────────────────────────
            foreach (var kv in _browserTabs)
                kv.Value.Visibility = kv.Key == e.TabId ? Visibility.Visible : Visibility.Collapsed;

            TabSidebarControl.SetActiveTab(e.TabId);

            var tab = _tabManager.GetTab(e.TabId);
            if (tab != null)
            {
                UrlBarControl.SetUrl(tab.Url);
                UrlBarControl.SetCanGoBack(tab.CanGoBack);
                UrlBarControl.SetCanGoForward(tab.CanGoForward);
                UrlBarControl.SetContainer(tab.ContainerId, ContainerColor(tab.ContainerId));
                UrlBarControl.SetZoom(browserTab.ZoomFactor);
                await UpdateBookmarkStarAsync(tab.Url);
                var isRealPage = !string.IsNullOrEmpty(tab.Url)
                    && tab.Url != "velo://newtab"
                    && tab.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                UrlBarControl.SetReaderModeAvailable(isRealPage);
                UpdateAgentContext(e.TabId);
                // Phase 3 / Sprint 6 — visual separator in the agent chat
                // when the active tab changes (instead of resetting).
                AgentPanelControl.NotifyTabSwitched(tab.Url);
            }

            // Refresh shield score and inspector for the newly-active tab
            RefreshShieldScore(e.TabId);
            RefreshInspectorWindow(e.TabId);

            if (!_navigatedTabs.Contains(e.TabId) && tab?.Url != null)
            {
                _navigatedTabs.Add(e.TabId);
                await browserTab.NavigateAsync(tab.Url);
            }
        });
    }

    /// <summary>Updates URL bar, nav buttons etc. for the current primary pane tab.</summary>
    private async Task UpdatePrimaryUiAsync()
    {
        if (_primaryTabId == null) return;
        if (!_browserTabs.TryGetValue(_primaryTabId, out var primaryBt)) return;

        TabSidebarControl.SetActiveTab(_primaryTabId);

        var tab = _tabManager.GetTab(_primaryTabId);
        if (tab != null)
        {
            UrlBarControl.SetUrl(tab.Url);
            UrlBarControl.SetCanGoBack(tab.CanGoBack);
            UrlBarControl.SetCanGoForward(tab.CanGoForward);
            UrlBarControl.SetContainer(tab.ContainerId, ContainerColor(tab.ContainerId));
            UrlBarControl.SetZoom(primaryBt.ZoomFactor);
            await UpdateBookmarkStarAsync(tab.Url);
            var isRealPage = !string.IsNullOrEmpty(tab.Url)
                && tab.Url != "velo://newtab"
                && tab.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            UrlBarControl.SetReaderModeAvailable(isRealPage);
            UpdateAgentContext(_primaryTabId);
        }

        // First-time navigation for primary pane
        if (!_navigatedTabs.Contains(_primaryTabId) && tab?.Url != null)
        {
            _navigatedTabs.Add(_primaryTabId);
            await primaryBt.NavigateAsync(tab.Url);
        }

        // First-time navigation for secondary pane (runs in parallel)
        if (_splitTabId != null && !_navigatedTabs.Contains(_splitTabId)
            && _browserTabs.TryGetValue(_splitTabId, out var splitBt))
        {
            var splitTab = _tabManager.GetTab(_splitTabId);
            if (splitTab?.Url != null)
            {
                _navigatedTabs.Add(_splitTabId);
                await splitBt.NavigateAsync(splitTab.Url);
            }
        }
    }

    // ── Browser tab events ───────────────────────────────────────────────

    private void OnBrowserUrlChanged(string tabId, string url)
    {
        Dispatcher.Invoke(async () =>
        {
            if (url.StartsWith("__navigate:"))
            {
                var input = url["__navigate:".Length..];
                var resolved = await _navController.ResolveUrlAsync(input);
                if (_browserTabs.TryGetValue(tabId, out var bt))
                    await bt.NavigateAsync(resolved);
                return;
            }

            if (url.StartsWith("__newtab:"))
            {
                var newUrl = url["__newtab:".Length..];
                var newTab = _tabManager.CreateTab(newUrl);
                return;
            }

            // Reset block/capture counters and security state for this tab on new navigation
            _tabBlockCounts[tabId]      = (0, 0, 0);
            _tabNewCapture.Remove(tabId);
            _tabVerdictLevel[tabId]     = 0;
            _tabLastAiVerdict[tabId]    = null;

            // Phase 3 / Sprint 5 — let the autofill toast re-arm on new host.
            _autofillToastShownForHost = false;
            _tabTlsStatus.Remove(tabId);

            // Show "analyzing" on shield while new page loads
            if (IsUiDrivingTab(tabId))
                UrlBarControl.SetShieldAnalyzing();

            _tabManager.UpdateTab(tabId, t => t.Url = url);
            if (IsUiDrivingTab(tabId))
            {
                UrlBarControl.SetUrl(url);
                await UpdateBookmarkStarAsync(url);
                var isRealPage = !string.IsNullOrEmpty(url)
                    && url != "velo://newtab"
                    && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                UrlBarControl.SetReaderModeAvailable(isRealPage);
                UpdateAgentContext(tabId);
            }
        });
    }

    private void OnBrowserTitleChanged(string tabId, string title)
    {
        Dispatcher.Invoke(async () =>
        {
            _tabManager.UpdateTab(tabId, t => t.Title = title);
            var tab = _tabManager.GetTab(tabId);
            if (tab != null)
            {
                TabSidebarControl.UpdateTab(tab);
                if (IsUiDrivingTab(tabId))
                    Title = $"{title} — VELO";
            }
            var url = _tabManager.GetTab(tabId)?.Url ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                var counts = _tabBlockCounts.GetValueOrDefault(tabId);
                var newCapture = _tabNewCapture.Remove(tabId);
                await _navController.RecordNavigationAsync(tabId, url, title,
                    counts.Blocked, counts.Trackers, counts.Malware, newCapture);
            }
        });
    }

    private void OnBrowserLoadingChanged(string tabId, bool loading)
    {
        Dispatcher.Invoke(() =>
        {
            _tabManager.UpdateTab(tabId, t => t.IsLoading = loading);
            if (IsUiDrivingTab(tabId))
            {
                UrlBarControl.SetLoading(loading);
                // Recompute shield score once the page finishes loading
                if (!loading)
                    RefreshShieldScore(tabId);
            }
        });
    }

    private void OnNavigationStateChanged(string tabId, (bool CanBack, bool CanForward) state)
    {
        Dispatcher.Invoke(() =>
        {
            _tabManager.UpdateTab(tabId, t =>
            {
                t.CanGoBack    = state.CanBack;
                t.CanGoForward = state.CanForward;
            });
            if (IsUiDrivingTab(tabId))
            {
                UrlBarControl.SetCanGoBack(state.CanBack);
                UrlBarControl.SetCanGoForward(state.CanForward);
            }
        });
    }

    private void OnSecurityVerdict(string tabId, AIVerdict verdict)
    {
        Dispatcher.Invoke(() =>
        {
            // Accumulate block counts regardless of active tab
            if (verdict.Verdict == VerdictType.Block)
            {
                var cur = _tabBlockCounts.GetValueOrDefault(tabId);
                var isTracker = verdict.ThreatType is ThreatType.KnownTracker or ThreatType.Tracker or ThreatType.Fingerprinting;
                var isMalware = verdict.ThreatType is ThreatType.Malware or ThreatType.Phishing or ThreatType.MitM;
                _tabBlockCounts[tabId] = (
                    cur.Blocked + 1,
                    cur.Trackers + (isTracker ? 1 : 0),
                    cur.Malware  + (isMalware  ? 1 : 0)
                );

                // Malwaredex: capture by (ThreatType, SubType) — fire-and-forget, never blocks navigation
                if (verdict.ThreatType != ThreatType.None)
                {
                    var typeKey = verdict.ThreatType.ToString();
                    var reason2 = verdict.Reason;

                    var domain2 = "";
                    try { domain2 = new Uri(_tabManager.GetTab(tabId)?.Url ?? "").Host; } catch { }

                    var subType2     = MalwaredexEntry.DetectSubType(typeKey, domain2, reason2);
                    var compositeKey = $"{typeKey}::{subType2}";

                    if (!_capturedThreatTypes.Contains(compositeKey))
                        _tabNewCapture.Add(tabId);

                    _capturedThreatTypes.Add(compositeKey);   // idempotent for HashSet

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var repo = _services.GetRequiredService<MalwaredexRepository>();
                            await repo.CaptureAsync(typeKey, domain2, reason2);
                        }
                        catch { }
                    });
                }
            }

            // Store for Security Inspector — tracker/fingerprinting blocks are already
            // counted in _tabBlockCounts.Trackers and fed to SafetyScorer via
            // TrackersBlockedCount (+2 pts each). Storing them as AIVerdict would also
            // apply a -50 Block penalty, making the shield permanently red on any site
            // with trackers. Only genuine threats (Malware, Phishing, MitM, etc.) and
            // AI analysis verdicts should influence AIVerdict.
            var isTrackerBlock = verdict.ThreatType is
                ThreatType.KnownTracker or ThreatType.Tracker or ThreatType.Fingerprinting;

            if (!isTrackerBlock)
            {
                var prev = _tabLastAiVerdict.GetValueOrDefault(tabId);
                if (prev == null || (int)verdict.Verdict >= (int)prev.Verdict)
                    _tabLastAiVerdict[tabId] = verdict;

                // Refresh shield immediately when a real threat arrives
                if (IsUiDrivingTab(tabId))
                    RefreshShieldScore(tabId);
            }

        if (!IsUiDrivingTab(tabId)) return;
            if (verdict.Verdict == VerdictType.Safe) return;

            // Only show the panel if this verdict is more severe than what was already shown
            // this navigation (prevents NavGuard + AI double-popup for the same page)
            var newLevel = verdict.Verdict == VerdictType.Block ? 2 : 1;
            var shownLevel = _tabVerdictLevel.GetValueOrDefault(tabId, 0);
            if (newLevel <= shownLevel) return;
            _tabVerdictLevel[tabId] = newLevel;

            // v2.0.5.12 — Prefer the host the verdict was raised against
            // (e.g. download URL host for DownloadGuard) so AllowOnce/Whitelist
            // whitelist the right thing. Fall back to the tab URL only when
            // the verdict didn't carry a specific host.
            var domain = !string.IsNullOrEmpty(verdict.Host) ? verdict.Host : "";
            if (string.IsNullOrEmpty(domain))
                try { domain = new Uri(_tabManager.GetTab(tabId)?.Url ?? "").Host; } catch { }
            SecurityPanelControl.Show(domain, verdict);

            // Phase 3 / Sprint 1 — Also publish the rich BlockedRequestEvent
            // so ThreatsPanelV2 can render the per-tab grouped session list.
            if (verdict.Verdict == VerdictType.Block)
                PublishBlockedRequest(tabId, domain, verdict);
        });
    }

    private void PublishBlockedRequest(string tabId, string domain, VELO.Security.AI.Models.AIVerdict verdict)
    {
        try
        {
            var fullUrl = string.IsNullOrEmpty(domain) ? "" : $"https://{domain}";
            try
            {
                var tabUrl = _tabManager.GetTab(tabId)?.Url ?? "";
                if (!string.IsNullOrEmpty(tabUrl) && tabUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    fullUrl = tabUrl;
            }
            catch { }

            var kind = verdict.ThreatType switch
            {
                ThreatType.KnownTracker or ThreatType.Tracker => "Tracker",
                ThreatType.Fingerprinting                     => "Fingerprint",
                ThreatType.Malware or ThreatType.Phishing or
                ThreatType.MitM or ThreatType.DnsRebinding    => "Malware",
                _                                              => "Other",
            };

            // verdict.Source mirrors BlockSource enum names where possible
            // (RequestGuard, DownloadGuard, AIEngine…). Anything else falls
            // back to "RequestGuard" inside ThreatsPanelViewModel.ParseSource.
            var source = string.IsNullOrEmpty(verdict.Source) ? "RequestGuard" : verdict.Source;

            var subKey   = $"{kind}::{VELO.Data.Models.MalwaredexEntry.DetectSubType(verdict.ThreatType.ToString(), domain, verdict.Reason ?? "")}";
            var hit      = _capturedThreatTypes.Contains(subKey);

            _eventBus.Publish(new BlockedRequestEvent(
                TabId:           tabId,
                Host:            domain,
                FullUrl:         fullUrl,
                Kind:            kind,
                SubKind:         verdict.ThreatType.ToString(),
                Source:          source,
                IsMalwaredexHit: hit,
                Confidence:      verdict.Confidence,
                BlockedAtUtc:    DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] PublishBlockedRequest failed: {ex.Message}");
        }
    }

    // ── UrlBar events ────────────────────────────────────────────────────

    private async void UrlBar_NavigationRequested(object? sender, string input)
    {
        SecurityPanelControl.Hide();
        var url = await _navController.ResolveUrlAsync(input);
        if (ActiveBrowserTab() is { } bt)
            await bt.NavigateAsync(url);
    }

    private void UrlBar_BackRequested(object? sender, EventArgs e)
        => ActiveBrowserTab()?.GoBack();

    private void UrlBar_ForwardRequested(object? sender, EventArgs e)
        => ActiveBrowserTab()?.GoForward();

    private void UrlBar_ReloadRequested(object? sender, EventArgs e)
        => ActiveBrowserTab()?.Reload();

    private void UrlBar_StopRequested(object? sender, EventArgs e)
        => ActiveBrowserTab()?.Stop();

    private void UrlBar_AgentChatRequested(object? sender, EventArgs e)
        => AgentPanelControl.ToggleVisibility();

    private void UrlBar_MenuRequested(object? sender, EventArgs e)
    {
        var loc  = LocalizationService.Current;
        var menu = new ContextMenu();

        var itemSettings = new MenuItem { Header = loc.T("menu.settings") };
        itemSettings.Click += async (_, _) =>
        {
            var s = _services.GetRequiredService<SettingsRepository>();
            var v = _services.GetRequiredService<VaultService>();
            new VELO.UI.Dialogs.SettingsWindow(s, v) { Owner = this }.ShowDialog();
            var bootstrapper = _services.GetRequiredService<AppBootstrapper>();
            await bootstrapper.ConfigureAIAdapterAsync();
            await bootstrapper.ConfigureAgentAdaptersAsync();
            await RefreshAiStatusAsync();
        };

        var itemVault = new MenuItem { Header = loc.T("menu.vault") };
        itemVault.Click += (_, _) =>
        {
            var s = _services.GetRequiredService<SettingsRepository>();
            var v = _services.GetRequiredService<VaultService>();
            new VELO.UI.Dialogs.VaultWindow(v, s) { Owner = this }.ShowDialog();
        };

        var itemBookmarks = new MenuItem { Header = loc.T("menu.bookmarks") };
        itemBookmarks.Click += (_, _) => OpenBookmarks();

        var itemHistory = new MenuItem { Header = loc.T("menu.history") };
        itemHistory.Click += (_, _) => OpenHistory();

        var itemDownloads = new MenuItem { Header = loc.T("menu.downloads") };
        itemDownloads.Click += (_, _) => OpenDownloads();

        var itemMalwaredex = new MenuItem { Header = loc.T("menu.malwaredex") };
        itemMalwaredex.Click += (_, _) => OpenMalwaredex();

        var itemClearData = new MenuItem { Header = loc.T("menu.cleardata") };
        itemClearData.Click += (_, _) => OpenClearData();

        var itemImport = new MenuItem { Header = loc.T("menu.import") };
        itemImport.Click += async (_, _) => await RunImportWizardAsync();

        var itemInspector = new MenuItem { Header = "🔍 Security Inspector  Ctrl+Shift+V" };
        itemInspector.Click += (_, _) => OpenSecurityInspector();

        var itemAbout = new MenuItem { Header = loc.T("menu.about") };
        itemAbout.Click += async (_, _) =>
        {
            if (ActiveBrowserTab() is { } bt)
                await bt.NavigateAsync("velo://about");
        };

        menu.Items.Add(itemSettings);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemVault);
        menu.Items.Add(itemBookmarks);
        menu.Items.Add(itemHistory);
        menu.Items.Add(itemDownloads);
        menu.Items.Add(itemMalwaredex);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemImport);
        menu.Items.Add(itemInspector);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemClearData);
        menu.Items.Add(itemAbout);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ── TabBar / TabSidebar events ───────────────────────────────────────

    private void TabBar_NewTabRequested(object? sender, EventArgs e)
        => _tabManager.CreateTab();

    private void TabBar_TabSelected(object? sender, string tabId)
        => _tabManager.ActivateTab(tabId);

    private void TabBar_TabCloseRequested(object? sender, string tabId)
        => _tabManager.CloseTab(tabId);

    private void TabBar_TabContainerChangeRequested(object? sender,
        (string TabId, string ContainerId) args)
    {
        var (tabId, containerId) = args;
        _tabManager.UpdateTab(tabId, t => t.ContainerId = containerId);
        if (IsUiDrivingTab(tabId))
            UrlBarControl.SetContainer(containerId, ContainerColor(containerId));
    }

    // ── TabSidebar-specific events ───────────────────────────────────────

    private void TabSidebar_SplitRequested(object? sender, EventArgs e)
    {
        if (_isSplitMode) DeactivateSplit();
        else ActivateSplit();
    }

    private async void TabSidebar_AddWorkspaceRequested(object? sender, EventArgs e)
    {
        var name = InputDialog.Show(
            owner: this,
            title: "Nuevo workspace",
            prompt: "Nombre del workspace:",
            defaultValue: $"Workspace {TabSidebarControl.WorkspaceCount + 1}");

        if (string.IsNullOrWhiteSpace(name)) return;

        // Pick a color cycling through a preset palette
        var palette = new[] { "#7FFF5F", "#FFB300", "#FF3D71", "#A259FF", "#00E5FF", "#FF6B35" };
        var color   = palette[TabSidebarControl.WorkspaceCount % palette.Length];

        var ws = new Workspace { Name = name, Color = color };
        TabSidebarControl.AddWorkspace(ws);
        TabSidebarControl.SetActiveWorkspace(ws.Id);

        // Persist immediately
        try
        {
            var repo  = _services.GetRequiredService<WorkspaceRepository>();
            var entry = new VELO.Data.Models.WorkspaceEntry
            {
                Id        = ws.Id,
                Name      = ws.Name,
                Color     = ws.Color,
                SortOrder = TabSidebarControl.WorkspaceCount - 1,
            };
            await repo.SaveAsync(entry);
        }
        catch (Exception ex) { Log.Warning(ex, "Could not persist workspace"); }
    }

    private async void TabSidebar_TabTearOffRequested(object? sender, string tabId)
    {
        var tab = _tabManager.GetTab(tabId);
        if (tab == null) return;

        var url = tab.Url;

        // Don't tear off if it's the only tab — that would leave an empty window
        if (_tabManager.Tabs.Count <= 1) return;

        // Close the tab in this window first
        _tabManager.CloseTab(tabId);

        // Build a completely isolated service provider for the new window.
        // Each window owns its own TabManager, EventBus, and security engine —
        // complete isolation, which is the right model for a privacy browser.
        var newServices = DependencyConfig.Build();
        try
        {
            var bootstrapper = newServices.GetRequiredService<AppBootstrapper>();
            await bootstrapper.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TearOff: bootstrapper init warning — window may open with reduced security");
        }

        var cursorPos = CursorScreenPosition();
        var newWindow = new MainWindow(newServices, url)
        {
            Left   = cursorPos.X - 100,
            Top    = cursorPos.Y - 30,
            Width  = Width,
            Height = Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        newWindow.Show();
    }

    private void TabSidebar_WorkspaceSelected(object? sender, string workspaceId)
    {
        // Arc-like UX: restore the tab the user was last viewing in this workspace
        // instead of jumping to the topmost one. Fallback chain:
        //   1) remembered last-active tab (still exists and still in this workspace)
        //   2) first tab of the workspace
        //   3) create a new tab in this workspace
        if (_lastActiveTabPerWorkspace.TryGetValue(workspaceId, out var rememberedId))
        {
            var remembered = _tabManager.GetTab(rememberedId);
            if (remembered != null && remembered.WorkspaceId == workspaceId)
            {
                _tabManager.ActivateTab(rememberedId);
                return;
            }
            _lastActiveTabPerWorkspace.Remove(workspaceId); // stale
        }

        var firstTab = _tabManager.Tabs.FirstOrDefault(t => t.WorkspaceId == workspaceId);
        if (firstTab != null)
            _tabManager.ActivateTab(firstTab.Id);
        else
        {
            var tab = _tabManager.CreateTab();
            tab.WorkspaceId = workspaceId;
        }
    }

    private async void TabSidebar_WorkspaceRemoved(object? sender, string workspaceId)
    {
        // Don't allow deleting the last workspace
        if (TabSidebarControl.WorkspaceCount == 0) return;

        try
        {
            var repo = _services.GetRequiredService<WorkspaceRepository>();
            await repo.DeleteAsync(workspaceId);
        }
        catch (Exception ex) { Log.Warning(ex, "Could not delete workspace {Id}", workspaceId); }
    }

    // ── Security panel ───────────────────────────────────────────────────

    private void Security_AllowOnce(object? sender, string domain)
    {
        ActiveBrowserTab()?.AllowOnce(domain);
    }

    // ── Browser import wizard (Phase 3 / Sprint 4) ───────────────────────

    private async Task RunImportWizardAsync()
    {
        var loc = VELO.Core.Localization.LocalizationService.Current;
        var svc = _services.GetRequiredService<VELO.Import.BrowserImportService>();

        var detected = await svc.DetectInstalledAsync();
        if (detected.Count == 0)
        {
            MessageBox.Show(this,
                loc.T("import.none_detected"),
                loc.T("import.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Browser picker — comma-separated list.
        var summary = string.Join("\n", detected.Select((b, i) =>
            $"  {i + 1}) {b.DisplayName}  ({b.ProfileName})"));
        var pickPrompt = MessageBox.Show(this,
            string.Format(loc.T("import.detected_body"), detected.Count, summary),
            loc.T("import.title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (pickPrompt != MessageBoxResult.OK) return;

        // For the MVP modal we just import from the first detected browser
        // with default options (bookmarks + history). A full picker UI lives
        // in a Phase-3-extension wizard window — for now this lets users
        // ALL of the most common case (Chrome → VELO) in one click.
        var browser = detected[0];
        var includePasswords = MessageBox.Show(this,
            string.Format(loc.T("import.confirm_passwords"), browser.DisplayName),
            loc.T("import.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        var opts = new VELO.Import.Models.ImportOptions
        {
            Bookmarks = true,
            History   = true,
            Passwords = includePasswords,
        };

        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var result = await svc.ImportAsync(browser, opts);

            var bodyKey = result.Errors.Count == 0
                ? "import.done.ok"
                : "import.done.partial";
            var report = new System.Text.StringBuilder();
            report.AppendLine(string.Format(loc.T(bodyKey), browser.DisplayName));
            report.AppendLine();
            report.AppendLine($"  • {result.BookmarksImported} {loc.T("import.summary.bookmarks")}");
            report.AppendLine($"  • {result.HistoryImported} {loc.T("import.summary.history")}");
            if (opts.Passwords)
                report.AppendLine($"  • {result.PasswordsImported} {loc.T("import.summary.passwords")}");
            if (result.Warnings.Count > 0)
            {
                report.AppendLine();
                report.AppendLine(loc.T("import.warnings"));
                foreach (var w in result.Warnings.Take(5)) report.AppendLine($"  ⚠ {w}");
            }
            if (result.Errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine(loc.T("import.errors"));
                foreach (var e in result.Errors.Take(5)) report.AppendLine($"  ✗ {e}");
            }
            MessageBox.Show(this, report.ToString(), loc.T("import.title"),
                MessageBoxButton.OK,
                result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Browser import failed");
            MessageBox.Show(this,
                string.Format(loc.T("import.fatal"), ex.Message),
                loc.T("import.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ── Session restore (Phase 3 / Sprint 3) ─────────────────────────────

    private System.Windows.Threading.DispatcherTimer? _sessionTimer;
    private bool _sessionRestoreSkippedDueToSecurityMode;

    /// <summary>
    /// Starts the periodic snapshot heartbeat (every 30s with WasCleanShutdown=false).
    /// Also performs the one-shot restore prompt when applicable.
    /// </summary>
    private async Task InitSessionRestoreAsync()
    {
        var settings = _services.GetRequiredService<SettingsRepository>();
        var secMode  = await settings.GetAsync(SettingKeys.SecurityMode, "Normal");
        _sessionRestoreSkippedDueToSecurityMode =
            secMode is "Paranoid" or "Bunker";

        if (_sessionRestoreSkippedDueToSecurityMode)
        {
            // Per spec § 6.3: never write a snapshot in these modes.
            // Also wipe any stale snapshot so a previous Normal-mode session
            // doesn't leak into a paranoid relaunch.
            await _services.GetRequiredService<VELO.Core.Sessions.SessionService>().ClearAsync();
            return;
        }

        // Restore (best-effort). Runs before we start writing fresh snapshots.
        await TryRestoreSessionAsync(settings);

        // Heartbeat: every 30s save a "we were running just before this point"
        // snapshot. If the process dies hard the next launch will see clean=false.
        _sessionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _sessionTimer.Tick += async (_, _) =>
        {
            try { await SaveSessionSnapshotAsync(cleanShutdown: false); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[VELO] session heartbeat failed: {ex.Message}");
            }
        };
        _sessionTimer.Start();
    }

    private async Task TryRestoreSessionAsync(SettingsRepository settings)
    {
        var svc = _services.GetRequiredService<VELO.Core.Sessions.SessionService>();
        var snap = await svc.LoadLastAsync();
        if (snap == null || snap.TotalTabs == 0) return;

        bool restore;
        if (!snap.WasCleanShutdown)
        {
            // Crash-recovery path always asks.
            var loc = VELO.Core.Localization.LocalizationService.Current;
            var ans = MessageBox.Show(this,
                string.Format(loc.T("session.recover.body"), snap.TotalTabs),
                loc.T("session.recover.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            restore = ans == MessageBoxResult.Yes;
        }
        else
        {
            // Clean shutdown → respect the saved preference. Ask once if no
            // preference exists yet.
            var alwaysRestore = await settings.GetBoolAsync(SettingKeys.SessionRestoreAlways, false);
            var asked         = await settings.GetBoolAsync(SettingKeys.SessionRestoreAsked,  false);

            if (alwaysRestore)
            {
                restore = true;
            }
            else if (!asked)
            {
                var loc = VELO.Core.Localization.LocalizationService.Current;
                var ans = MessageBox.Show(this,
                    string.Format(loc.T("session.restore.body"), snap.TotalTabs),
                    loc.T("session.restore.title"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                await settings.SetBoolAsync(SettingKeys.SessionRestoreAsked, true);
                if (ans == MessageBoxResult.Yes)
                {
                    restore = true;
                    await settings.SetBoolAsync(SettingKeys.SessionRestoreAlways, true);
                }
                else if (ans == MessageBoxResult.Cancel)
                {
                    restore = true;  // Cancel = restore-just-this-once
                }
                else
                {
                    restore = false;
                }
            }
            else
            {
                restore = false;
            }
        }

        if (restore)
        {
            RestoreSnapshot(snap);
        }

        // Wipe regardless — either we restored everything or the user said no.
        // Keeping the file would re-prompt forever.
        await svc.ClearAsync();
    }

    /// <summary>
    /// v2.1.4 — Maximum tabs we restore eagerly. Above this we warn the user
    /// and drop the oldest entries. Proper lazy-hydration with placeholder
    /// rows in the sidebar (per spec § 6.4) is parked for Sprint 7's
    /// MainWindow refactor; this cap keeps RAM sane in the meantime.
    /// </summary>
    private const int RestoreMaxTabs = 30;

    private void RestoreSnapshot(VELO.Core.Sessions.SessionSnapshot snap)
    {
        // First window only for now; tear-off windows are session-only by design.
        if (snap.Windows.Count == 0) return;
        var win = snap.Windows[0];

        var tabsToRestore = win.Tabs;
        if (tabsToRestore.Count > RestoreMaxTabs)
        {
            // Most recently active first — TabSnapshot.LastActiveAtUtc is set
            // at snapshot time so the user keeps the tabs they actually use.
            tabsToRestore = tabsToRestore
                .OrderByDescending(t => t.LastActiveAtUtc)
                .Take(RestoreMaxTabs)
                .ToList();
            Log.Warning("Session restore: snapshot had {Total} tabs; only the {Cap} most recent are restored",
                win.Tabs.Count, RestoreMaxTabs);
        }

        var initialIdRemovedFromInitialUrl = false;
        foreach (var tab in tabsToRestore)
        {
            // Skip the auto-created newtab if this is the very first tab —
            // we want the snapshot's URL to be the initial one.
            if (!initialIdRemovedFromInitialUrl && _browserTabs.Count == 0 && _initialUrl == "velo://newtab")
            {
                _initialUrl = tab.Url;
                initialIdRemovedFromInitialUrl = true;
                continue;
            }
            _tabManager.CreateTab(tab.Url, tab.ContainerId);
        }

        Log.Information("Session restore: hydrated {Count} tabs from snapshot saved at {SavedAt}",
            tabsToRestore.Count, snap.SavedAtUtc);
    }

    /// <summary>
    /// v2.1.4 — Cached fingerprint of the last snapshot so the 30-second
    /// heartbeat can skip the disk write when nothing relevant has changed.
    /// 120 unnecessary writes/hour add up on SSDs over time.
    /// </summary>
    private string _lastSessionFingerprint = "";

    private async Task SaveSessionSnapshotAsync(bool cleanShutdown)
    {
        if (_sessionRestoreSkippedDueToSecurityMode) return;

        var svc = _services.GetRequiredService<VELO.Core.Sessions.SessionService>();
        var tabs = _tabManager.Tabs
            .Where(t => VELO.Core.Sessions.SessionService.IsSafeForSnapshot(t.ContainerId))
            .Select(t => new VELO.Core.Sessions.TabSnapshot
            {
                Id          = t.Id,
                Url         = t.Url ?? "",
                Title       = t.Title ?? "",
                ContainerId = t.ContainerId,
                WorkspaceId = t.WorkspaceId,
                ScrollY     = 0,  // hooked from BrowserTab in a future sprint
                LastActiveAtUtc = DateTime.UtcNow,
            })
            .ToList();

        var window = new VELO.Core.Sessions.WindowSnapshot
        {
            Left        = Left,
            Top         = Top,
            Width       = ActualWidth,
            Height      = ActualHeight,
            IsMaximised = WindowState == WindowState.Maximized,
            ActiveTabId = _tabManager.ActiveTab?.Id ?? "",
            Tabs        = tabs,
        };

        var snap = new VELO.Core.Sessions.SessionSnapshot
        {
            Version          = 1,
            SavedAtUtc       = DateTime.UtcNow,
            WasCleanShutdown = cleanShutdown,
            Windows          = [window],
        };

        // v2.1.4 — Skip the disk write when the heartbeat would produce the
        // same content as before. Saves ~120 writes/hour on idle sessions.
        // Always write on a clean shutdown so the WasCleanShutdown=true flag
        // lands even if the user hasn't opened a new tab in 5 minutes.
        var fingerprint = ComputeSessionFingerprint(window, cleanShutdown);
        if (!cleanShutdown && fingerprint == _lastSessionFingerprint) return;
        _lastSessionFingerprint = fingerprint;

        await svc.SnapshotAsync(snap);
    }

    /// <summary>
    /// Cheap, allocation-light hash of the per-tab (id|url|title|container|
    /// workspace) tuples plus active-tab id and window bounds. Two heartbeats
    /// with identical browsing state produce identical strings; anything that
    /// would change the on-disk JSON also changes this.
    /// </summary>
    private static string ComputeSessionFingerprint(VELO.Core.Sessions.WindowSnapshot w, bool cleanShutdown)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append(cleanShutdown ? '1' : '0');
        sb.Append('|').Append(w.ActiveTabId);
        sb.Append('|').Append((int)w.Left).Append(',').Append((int)w.Top)
          .Append(',').Append((int)w.Width).Append(',').Append((int)w.Height)
          .Append(',').Append(w.IsMaximised ? '1' : '0');
        foreach (var t in w.Tabs)
        {
            sb.Append('\n').Append(t.Id).Append('|').Append(t.Url).Append('|')
              .Append(t.Title).Append('|').Append(t.ContainerId).Append('|')
              .Append(t.WorkspaceId);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Phase 3 — Bridges BlockExplanationService and AIContextActions to the
    /// existing AgentLauncher chat path. Captures the next ResponseReady that
    /// arrives, returning its plain text reply. Listening on a per-call basis
    /// keeps requests independent (no shared promise state).
    /// </summary>
    private Task<string> WireAgentChat(string system, string user, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string>();
        void OnResponse(VELO.Agent.Models.AgentResponse r)
        {
            if (!tcs.Task.IsCompleted) tcs.TrySetResult(r.ReplyText ?? "");
            _agentLauncher.ResponseReady -= OnResponse;
        }
        _agentLauncher.ResponseReady += OnResponse;
        ct.Register(() =>
        {
            _agentLauncher.ResponseReady -= OnResponse;
            tcs.TrySetCanceled(ct);
        });
        _agentLauncher.SendAsync("__ai__",
            $"{system}\n\n{user}",
            new VELO.Agent.Models.AgentContext { CurrentUrl = "", PageTitle = "" });
        return tcs.Task;
    }

    // ── Threats Panel v3 (Phase 3 / Sprint 1) ────────────────────────────

    private void ThreatsPanel_CloseRequested(object? sender, EventArgs e)
        => ThreatsPanelControl.Visibility = Visibility.Collapsed;

    private void ThreatsPanel_AllowRequested(object? sender, VELO.Security.Threats.BlockEntry entry)
    {
        // Reuse the SecurityPanel allow path so the host still gets whitelisted
        // through both RequestGuard and DownloadGuard (and persisted to settings).
        Security_AllowOnce(this, entry.Host);
        Security_Whitelist(this, entry.Host);
    }

    private void ThreatsPanel_ReportRequested(object? sender, VELO.Security.Threats.BlockEntry entry)
    {
        // Pre-load Malwaredex with the report and surface the window so the
        // user can confirm. CaptureAsync is idempotent on (ThreatType, SubType).
        try
        {
            var repo = _services.GetRequiredService<MalwaredexRepository>();
            _ = repo.CaptureAsync(
                threatType: entry.Kind.ToString(),
                domain:     entry.Host,
                reason:     entry.SubKind);
            OpenMalwaredex();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] ThreatsPanel report failed: {ex.Message}");
        }
    }

    private async void Security_Whitelist(object? sender, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;

        // v2.0.5.12 — In-memory + persistent. Both guards already saw it via
        // SecurityPanel.Whitelist_Click; here we save it to settings so the
        // whitelist survives a VELO restart. Loaded back at startup in
        // AppBootstrapper.
        RequestGuard.AddToWhitelist(domain);
        VELO.Security.Guards.DownloadGuard.Whitelist(domain);

        try
        {
            var settings = _services.GetRequiredService<SettingsRepository>();
            var current  = await settings.GetAsync(SettingKeys.SecurityWhitelist, "");
            var hosts    = string.IsNullOrWhiteSpace(current)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(current.Split(',', StringSplitOptions.RemoveEmptyEntries),
                                      StringComparer.OrdinalIgnoreCase);
            if (hosts.Add(domain.ToLowerInvariant()))
                await settings.SetAsync(SettingKeys.SecurityWhitelist, string.Join(",", hosts));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Persist whitelist failed: {ex.Message}");
        }
    }

    // ── Bookmarks ────────────────────────────────────────────────────────

    private async Task UpdateBookmarkStarAsync(string? url)
    {
        if (string.IsNullOrEmpty(url) || url == "velo://newtab")
        {
            UrlBarControl.SetBookmarked(false);
            return;
        }
        var repo = _services.GetRequiredService<BookmarkRepository>();
        var all  = await repo.GetAllAsync();
        UrlBarControl.SetBookmarked(all.Any(b => b.Url == url));
    }

    private async Task ToggleBookmarkAsync()
    {
        var url = _tabManager.ActiveTab?.Url;
        if (string.IsNullOrEmpty(url) || url == "velo://newtab") return;

        var repo = _services.GetRequiredService<BookmarkRepository>();
        var all  = await repo.GetAllAsync();
        var existing = all.FirstOrDefault(b => b.Url == url);

        if (existing != null)
        {
            await repo.DeleteAsync(existing.Id);
            UrlBarControl.SetBookmarked(false);
        }
        else
        {
            var title = _tabManager.ActiveTab?.Title ?? url;
            await repo.SaveAsync(new VELO.Data.Models.Bookmark { Url = url, Title = title });
            UrlBarControl.SetBookmarked(true);
        }
    }

    private void OpenHistory()
    {
        var repo = _services.GetRequiredService<HistoryRepository>();
        var win  = new VELO.UI.Dialogs.HistoryWindow(repo) { Owner = this };
        win.NavigationRequested += async (_, url) =>
        {
            if (ActiveBrowserTab() is { } bt)
                await bt.NavigateAsync(url);
        };
        win.ShowDialog();
    }

    private void OpenDownloads()
    {
        // If already open, bring to front
        if (_downloadsWindow != null && _downloadsWindow.IsLoaded)
        {
            _downloadsWindow.Activate();
            return;
        }
        try
        {
            _downloadsWindow = new DownloadsWindow(_downloadManager) { Owner = this };
            _downloadsWindow.Closed += (_, _) => _downloadsWindow = null;
            _downloadsWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir Descargas:\n{ex.Message}", "VELO", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenMalwaredex()
    {
        var repo = _services.GetRequiredService<MalwaredexRepository>();
        new VELO.UI.Dialogs.MalwaredexWindow(repo) { Owner = this }.ShowDialog();
    }

    private void OpenClearData()
    {
        var historyRepo  = _services.GetRequiredService<HistoryRepository>();
        var win = new VELO.UI.Dialogs.ClearDataWindow(historyRepo, _downloadManager,
            _browserTabs.Values.ToList()) { Owner = this };
        win.ShowDialog();
    }

    private void OpenBookmarks()
    {
        var repo = _services.GetRequiredService<BookmarkRepository>();
        var win  = new VELO.UI.Dialogs.BookmarksWindow(repo) { Owner = this };
        win.NavigationRequested += async (_, url) =>
        {
            if (ActiveBrowserTab() is { } bt)
                await bt.NavigateAsync(url);
        };
        win.ShowDialog();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    private record struct NativePoint(int X, int Y);

    /// <summary>Returns the screen cursor position in device-independent pixels.</summary>
    private Point CursorScreenPosition()
    {
        GetCursorPos(out var raw);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return new Point(raw.X, raw.Y);
        return source.CompositionTarget.TransformFromDevice.Transform(new Point(raw.X, raw.Y));
    }

    private async Task RefreshAiStatusAsync()
    {
        var aiMode     = await _settings.GetAsync(SettingKeys.AiMode, "Offline");
        var aiModel    = await _settings.GetAsync(SettingKeys.AiClaudeModel, "");
        var aiEndpoint = await _settings.GetAsync(SettingKeys.AiCustomEndpoint, "http://localhost:11434");

        if (aiMode is "Custom" or "Ollama")
        {
            UrlBarControl.SetAiStatus(UrlBar.AiStatus.Connecting, aiModel);
            _ = Task.Run(async () =>
            {
                var ready = await PingOllamaAsync(aiEndpoint, aiModel);
                Dispatcher.Invoke(() => UrlBarControl.SetAiStatus(
                    ready ? UrlBar.AiStatus.Ready : UrlBar.AiStatus.Error, aiModel));
            });
        }
        else if (aiMode == "Claude")
            UrlBarControl.SetAiStatus(UrlBar.AiStatus.Ready, "Claude API");
        else
            UrlBarControl.SetAiStatus(UrlBar.AiStatus.Offline);
    }

    private static async Task<bool> PingOllamaAsync(string endpoint, string model)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // OpenAI-compatible endpoint — works with Ollama, LM Studio, llama.cpp server
            var response = await http.GetAsync($"{endpoint.TrimEnd('/')}/v1/models");
            if (!response.IsSuccessStatusCode) return false;
            var body = await response.Content.ReadAsStringAsync();
            return string.IsNullOrEmpty(model) || body.Contains(model.Split(':')[0]);
        }
        catch { return false; }
    }

    private static string ContainerColor(string containerId) => containerId switch
    {
        "personal" => "#00E5FF",
        "work"     => "#7FFF5F",
        "banking"  => "#FF3D71",
        "shopping" => "#FFB300",
        _          => "#808080"
    };

    // ── Keyboard shortcuts ───────────────────────────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        var ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        // Don't steal text input from URL bar or other text fields
        if (!ctrl && !alt && e.Key != Key.F5 && e.Key != Key.F9 && e.Key != Key.Escape && e.Key != Key.F) return;

        switch (e.Key)
        {
            // Find in page
            case Key.F when ctrl:
                FindBarPanel.Visibility = Visibility.Visible;
                FindTextBox.Focus();
                FindTextBox.SelectAll();
                e.Handled = true;
                break;
            // New tab
            case Key.T when ctrl:
                _tabManager.CreateTab();
                e.Handled = true;
                break;

            // Close tab
            case Key.W when ctrl:
                if (_tabManager.ActiveTab is { } t)
                    _tabManager.CloseTab(t.Id);
                e.Handled = true;
                break;

            // Focus URL bar
            case Key.L when ctrl:
                UrlBarControl.FocusUrlBar();
                e.Handled = true;
                break;

            // Reload
            case Key.R when ctrl && !shift:
            case Key.F5 when !ctrl:
                ActiveBrowserTab()?.Reload();
                e.Handled = true;
                break;

            // Hard reload (bypass cache)
            case Key.R when ctrl && shift:
            case Key.F5 when ctrl:
                if (ActiveBrowserTab() is { } bt)
                {
                    bt.Stop();
                    bt.Reload();
                }
                e.Handled = true;
                break;

            // Reader mode
            case Key.F9:
                if (ActiveBrowserTab() is { } readerBt)
                    _ = readerBt.ToggleReaderModeAsync();
                e.Handled = true;
                break;

            // Stop / close find bar
            case Key.Escape:
                if (FindBarPanel.Visibility == Visibility.Visible)
                    CloseFindBar();
                else
                    ActiveBrowserTab()?.Stop();
                e.Handled = true;
                break;

            // Back / Forward
            case Key.Left  when alt: ActiveBrowserTab()?.GoBack();    e.Handled = true; break;
            case Key.Right when alt: ActiveBrowserTab()?.GoForward(); e.Handled = true; break;

            // History
            case Key.H when ctrl:
                OpenHistory();
                e.Handled = true;
                break;

            // Downloads
            case Key.J when ctrl:
                OpenDownloads();
                e.Handled = true;
                break;

            // Zoom in  (Ctrl++ or Ctrl+=)
            case Key.OemPlus  when ctrl:
            case Key.Add      when ctrl:
                ActiveBrowserTab()?.ZoomIn();
                e.Handled = true;
                break;

            // Zoom out (Ctrl+-)
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                ActiveBrowserTab()?.ZoomOut();
                e.Handled = true;
                break;

            // Reset zoom (Ctrl+0)
            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl:
                var bt0 = ActiveBrowserTab();
                if (bt0 != null) { bt0.ResetZoom(); UrlBarControl.SetZoom(1.0); }
                e.Handled = true;
                break;

            // Bookmark toggle
            case Key.D when ctrl:
                _ = ToggleBookmarkAsync();
                e.Handled = true;
                break;

            // VeloAgent panel toggle
            case Key.A when ctrl && shift:
                AgentPanelControl.ToggleVisibility();
                e.Handled = true;
                break;

            // Security Inspector (Ctrl+Shift+V)
            case Key.V when ctrl && shift:
                OpenSecurityInspector();
                e.Handled = true;
                break;

            // Split view toggle (Ctrl+\)
            case Key.OemBackslash when ctrl:
                TabSidebar_SplitRequested(this, EventArgs.Empty);
                e.Handled = true;
                break;

            // Command palette
            case Key.K when ctrl:
                _ = ShowCommandBarAsync();
                e.Handled = true;
                break;

            // Next / Previous tab
            case Key.Tab when ctrl && !shift:
                SwitchTabByOffset(+1);
                e.Handled = true;
                break;
            case Key.Tab when ctrl && shift:
                SwitchTabByOffset(-1);
                e.Handled = true;
                break;

            // Switch to tab 1-8 by number, 9 = last
            case >= Key.D1 and <= Key.D9 when ctrl:
            {
                var n = e.Key - Key.D1;
                var tabs = _tabManager.Tabs;
                var idx  = e.Key == Key.D9 ? tabs.Count - 1 : Math.Min(n, tabs.Count - 1);
                if (idx >= 0) _tabManager.ActivateTab(tabs[idx].Id);
                e.Handled = true;
                break;
            }
        }
    }

    private BrowserTab? ActiveBrowserTab()
    {
        // In split mode the "active" tab for keyboard shortcuts / URL bar is always the primary pane
        var id = _isSplitMode ? _primaryTabId : _tabManager.ActiveTab?.Id;
        return id != null && _browserTabs.TryGetValue(id, out var bt) ? bt : null;
    }

    private void SwitchTabByOffset(int offset)
    {
        var tabs = _tabManager.Tabs;
        if (tabs.Count < 2) return;
        var active = _tabManager.ActiveTab;
        var cur = tabs.ToList().FindIndex(t => t.Id == active?.Id);
        var next = (cur + offset + tabs.Count) % tabs.Count;
        _tabManager.ActivateTab(tabs[next].Id);
    }

    // ── Workspace persistence ─────────────────────────────────────────────

    private async Task RestoreWorkspacesAsync()
    {
        try
        {
            var repo    = _services.GetRequiredService<WorkspaceRepository>();
            var entries = await repo.GetAllAsync();

            if (entries.Count == 0) return; // DB is empty — keep the in-memory Default

            // Replace the seeded Default with whatever is in the DB
            // (the DB seed ensures "default" is always present on first run)
            TabSidebarControl.RemoveWorkspace(Workspace.Default.Id);

            foreach (var e in entries)
            {
                var ws = new Workspace { Id = e.Id, Name = e.Name, Color = e.Color };
                TabSidebarControl.AddWorkspace(ws);
            }

            // Activate the first workspace (preserves last-used ordering by SortOrder)
            TabSidebarControl.SetActiveWorkspace(entries[0].Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not restore workspaces — using defaults");
        }
    }

    // ── CommandBar ────────────────────────────────────────────────────────

    private async Task ShowCommandBarAsync(string query = "")
    {
        var results = await BuildCommandResultsAsync(query);
        CommandBarControl.Show(results);
    }

    private async Task<List<CommandResult>> BuildCommandResultsAsync(string query)
    {
        var q      = query.Trim();
        var list   = new List<CommandResult>();
        var isBlank = string.IsNullOrEmpty(q);

        // ── Open tabs ─────────────────────────────────────────────────────
        foreach (var tab in _tabManager.Tabs)
        {
            if (!isBlank &&
                !Contains(tab.Title, q) &&
                !Contains(tab.Url, q)) continue;

            list.Add(new CommandResult
            {
                Kind     = CommandResultKind.Tab,
                Icon     = tab.IsLoading ? "⏳" : "🌐",
                Title    = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title,
                Subtitle = tab.Url,
                Badge    = "pestaña",
                Tag      = tab.Id,
            });
        }

        // ── Bookmarks ─────────────────────────────────────────────────────
        try
        {
            var bookmarkRepo = _services.GetRequiredService<BookmarkRepository>();
            var bookmarks    = await bookmarkRepo.GetAllAsync();
            foreach (var bm in bookmarks)
            {
                if (!isBlank && !Contains(bm.Title, q) && !Contains(bm.Url, q)) continue;
                list.Add(new CommandResult
                {
                    Kind     = CommandResultKind.Bookmark,
                    Icon     = "⭐",
                    Title    = bm.Title,
                    Subtitle = bm.Url,
                    Badge    = "marcador",
                    Tag      = bm.Url,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "CommandBar: bookmark search failed (showing remaining sources)");
        }

        // ── History ────────────────────────────────────────────────────────
        try
        {
            var historyRepo = _services.GetRequiredService<HistoryRepository>();
            var entries = isBlank
                ? await historyRepo.GetRecentAsync(30)
                : await historyRepo.SearchAsync(q);

            var seen = new HashSet<string>();
            foreach (var h in entries)
            {
                if (!seen.Add(h.Url)) continue;
                if (list.Count >= 60) break;
                list.Add(new CommandResult
                {
                    Kind     = CommandResultKind.History,
                    Icon     = "🕒",
                    Title    = string.IsNullOrWhiteSpace(h.Title) ? h.Url : h.Title,
                    Subtitle = h.Url,
                    Badge    = "historial",
                    Tag      = h.Url,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "CommandBar: history search failed (showing remaining sources)");
        }

        // ── Built-in commands ─────────────────────────────────────────────
        var commands = BuiltInCommands();
        foreach (var cmd in commands)
        {
            if (!isBlank && !Contains(cmd.Title, q)) continue;
            list.Add(cmd);
        }

        // ── Navigate to typed URL / search query ───────────────────────────
        if (!isBlank && list.Count == 0)
        {
            list.Add(new CommandResult
            {
                Kind     = CommandResultKind.Navigate,
                Icon     = "↗",
                Title    = q,
                Subtitle = "Navegar o buscar",
                Badge    = "ir",
                Tag      = q,
            });
        }

        return list;
    }

    private IEnumerable<CommandResult> BuiltInCommands() =>
    [
        new() { Kind = CommandResultKind.Command, Icon = "＋", Title = "Nueva pestaña",
                Badge = "comando", Tag = (Action)(() => _tabManager.CreateTab()) },
        new() { Kind = CommandResultKind.Command, Icon = "✕", Title = "Cerrar pestaña activa",
                Badge = "comando", Tag = (Action)(() => { if (_tabManager.ActiveTab is { } t) _tabManager.CloseTab(t.Id); }) },
        new() { Kind = CommandResultKind.Command, Icon = "⊞", Title = "Vista dividida",
                Badge = "comando", Tag = (Action)(() => TabSidebar_SplitRequested(this, EventArgs.Empty)) },
        new() { Kind = CommandResultKind.Command, Icon = "⚙", Title = "Configuración",
                Badge = "comando", Tag = (Action)(async () =>
                {
                    var s = _services.GetRequiredService<SettingsRepository>();
                    var v = _services.GetRequiredService<VaultService>();
                    new VELO.UI.Dialogs.SettingsWindow(s, v) { Owner = this }.ShowDialog();
                    var bs = _services.GetRequiredService<AppBootstrapper>();
                    await bs.ConfigureAIAdapterAsync();
                    await bs.ConfigureAgentAdaptersAsync();
                    await RefreshAiStatusAsync();
                }) },
        new() { Kind = CommandResultKind.Command, Icon = "🕒", Title = "Historial",
                Badge = "comando", Tag = (Action)(() => OpenHistory()) },
        new() { Kind = CommandResultKind.Command, Icon = "⬇", Title = "Descargas",
                Badge = "comando", Tag = (Action)(() => OpenDownloads()) },
        new() { Kind = CommandResultKind.Command, Icon = "⭐", Title = "Marcadores",
                Badge = "comando", Tag = (Action)(() => OpenBookmarks()) },
        new() { Kind = CommandResultKind.Command, Icon = "🔒", Title = "Vault / Contraseñas",
                Badge = "comando", Tag = (Action)(() =>
                {
                    var s = _services.GetRequiredService<SettingsRepository>();
                    var v = _services.GetRequiredService<VaultService>();
                    new VELO.UI.Dialogs.VaultWindow(v, s) { Owner = this }.ShowDialog();
                }) },
        new() { Kind = CommandResultKind.Command, Icon = "🛡", Title = "Malwaredex",
                Badge = "comando", Tag = (Action)(() => OpenMalwaredex()) },
        new() { Kind = CommandResultKind.Command, Icon = "🤖", Title = "VeloAgent",
                Badge = "comando", Tag = (Action)(() => AgentPanelControl.ToggleVisibility()) },
        new() { Kind = CommandResultKind.Command, Icon = "🔍", Title = "Security Inspector",
                Badge = "comando", Tag = (Action)(() => OpenSecurityInspector()) },
    ];

    private static bool Contains(string? source, string query)
        => source?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private async void CommandBar_QueryChanged(object? sender, string query)
    {
        var results = await BuildCommandResultsAsync(query);
        CommandBarControl.SetResults(results);
    }

    private async void CommandBar_ResultSelected(object? sender, CommandResult result)
    {
        switch (result.Kind)
        {
            case CommandResultKind.Tab:
                if (result.Tag is string tabId)
                    _tabManager.ActivateTab(tabId);
                break;

            case CommandResultKind.Bookmark:
            case CommandResultKind.History:
            case CommandResultKind.Navigate:
                if (result.Tag is string url)
                {
                    var resolved = await _navController.ResolveUrlAsync(url);
                    if (ActiveBrowserTab() is { } bt)
                        await bt.NavigateAsync(resolved);
                }
                break;

            case CommandResultKind.Command:
                switch (result.Tag)
                {
                    case Action syncAction:
                        syncAction();
                        break;
                    case Func<Task> asyncAction:
                        await asyncAction();
                        break;
                }
                break;
        }
    }

    private void CommandBar_Closed(object? sender, EventArgs e) { /* focus returns naturally */ }

    // ── Glance modal ──────────────────────────────────────────────────────

    private void ShowGlanceAt(string url)
    {
        // Position the popup near the cursor, offset so it doesn't cover the link
        var cursor = CursorScreenPosition();

        // Convert screen position to coordinates relative to the main-area Grid
        var mainGrid = (System.Windows.Controls.Grid)GlancePopupControl.Parent;
        var relative = mainGrid.PointFromScreen(cursor);

        const double popupW = 420;
        const double popupH = 280;
        const double offsetY = 20;

        var left = relative.X - popupW / 2;
        var top  = relative.Y + offsetY;

        // Keep within window bounds
        left = Math.Max(0, Math.Min(left, mainGrid.ActualWidth  - popupW));
        top  = Math.Max(0, Math.Min(top,  mainGrid.ActualHeight - popupH));

        System.Windows.Controls.Canvas.SetLeft(GlancePopupControl, left);
        System.Windows.Controls.Canvas.SetTop(GlancePopupControl,  top);

        GlancePopupControl.ShowPreview(url);
    }

    // ── Security Inspector (Sprint 6) ────────────────────────────────────

    private void OpenSecurityInspector()
    {
        var tabId = ActiveBrowserTab()?.TabId;
        if (tabId == null) return;

        if (_inspectorWindow == null || !_inspectorWindow.IsLoaded)
        {
            _inspectorWindow = new SecurityInspectorWindow { Owner = this };
            _inspectorWindow.OpenDevToolsRequested = () => ActiveBrowserTab()?.OpenDevTools();
            _inspectorWindow.ForceScanRequested    = () =>
            {
                var id = ActiveBrowserTab()?.TabId;
                if (id != null)
                {
                    RefreshShieldScore(id);
                    RefreshInspectorWindow(id);
                }
            };
            _inspectorWindow.Closed += (_, _) => _inspectorWindow = null;
            _inspectorWindow.Show();
        }

        RefreshInspectorWindow(tabId);
        _inspectorWindow.Activate();
    }

    private void RefreshInspectorWindow(string tabId)
    {
        if (_inspectorWindow == null || !_inspectorWindow.IsLoaded) return;
        var data = BuildInspectorData(tabId);
        _inspectorWindow.Refresh(data);
    }

    private SecurityInspectorData BuildInspectorData(string tabId)
    {
        var tab    = _tabManager.GetTab(tabId);
        var url    = tab?.Url ?? "";
        var domain = ExtractDomain(url);
        var counts = _tabBlockCounts.GetValueOrDefault(tabId);
        var tlsUi  = _tabTlsStatus.GetValueOrDefault(tabId, TlsStatus.Secure);
        var ai     = _tabLastAiVerdict.GetValueOrDefault(tabId);

        // Shield score
        var safetyResult  = TryComputeSafetyResult(tabId);
        var shieldLevel   = safetyResult?.Level ?? SafetyLevel.Analyzing;
        var shieldScore   = safetyResult?.NumericScore ?? 0;
        var reasonsPos    = safetyResult?.ReasonsPositive.ToArray() ?? [];
        var reasonsNeg    = safetyResult?.ReasonsNegative.ToArray() ?? [];

        // TLS label
        var (tlsLabel, tlsIcon) = tlsUi switch
        {
            TlsStatus.Secure   => ("Seguro (HTTPS)", "✅"),
            TlsStatus.Warning  => ("Advertencia — revisar certificado", "⚠️"),
            TlsStatus.Insecure => ("Sin cifrado (HTTP)", "🔴"),
            _                  => ("Desconocido", "❓"),
        };

        // AI label
        string aiLabel, aiIcon;
        if (ai == null)
        {
            aiLabel = "Sin análisis";
            aiIcon  = "⏳";
        }
        else
        {
            (aiLabel, aiIcon) = ai.Verdict switch
            {
                VerdictType.Safe  => ("Seguro", "✅"),
                VerdictType.Warn  => ("Advertencia", "⚠️"),
                VerdictType.Block => ("Bloqueado", "🔴"),
                _                 => ("Sin análisis", "⏳"),
            };
        }

        var scriptsBlocked  = Math.Max(0, counts.Blocked - counts.Trackers - counts.Malware);

        return new SecurityInspectorData(
            url, domain, shieldLevel, shieldScore,
            reasonsPos, reasonsNeg,
            tlsLabel, tlsIcon,
            counts.Trackers, scriptsBlocked, counts.Malware,
            aiLabel, aiIcon,
            ai?.Confidence ?? 0,
            ai?.Reason     ?? "",
            ai?.Source     ?? "—",
            _fingerprintLevel != "Off",
            _fingerprintLevel,
            DateTime.UtcNow);
    }

    private SafetyResult? TryComputeSafetyResult(string tabId)
    {
        try
        {
            var url = _tabManager.GetTab(tabId)?.Url ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var tlsUi = _tabTlsStatus.GetValueOrDefault(tabId, TlsStatus.Secure);
            var tlsSec = tlsUi switch
            {
                TlsStatus.Insecure => VELO.Security.Models.TLSStatus.Http,
                TlsStatus.Warning  => VELO.Security.Models.TLSStatus.SelfSigned,
                _                  => VELO.Security.Models.TLSStatus.Valid,
            };

            var ai       = _tabLastAiVerdict.GetValueOrDefault(tabId);
            var counts   = _tabBlockCounts.GetValueOrDefault(tabId);
            var isGolden = _services.GetRequiredService<IGoldenList>().IsGolden(uri.Host);

            var ctx = new SafetyContext
            {
                Uri                        = uri,
                TLSStatus                  = tlsSec,
                AIVerdict                  = ai,
                TrackersBlockedCount       = counts.Trackers,
                FingerprintAttemptsBlocked = 0,
                IsGoldenList               = isGolden,
                IsWhitelistedByUser        = false,
                SessionVerdicts            = [],
            };

            return _services.GetRequiredService<SafetyScorer>().Compute(ctx);
        }
        catch { return null; }
    }

    /// <summary>Recomputes and pushes the shield score to the URL bar (and inspector if open).</summary>
    private void RefreshShieldScore(string tabId)
    {
        if (!IsUiDrivingTab(tabId)) return;

        var url = _tabManager.GetTab(tabId)?.Url ?? "";
        if (url == "velo://newtab" || string.IsNullOrEmpty(url))
        {
            UrlBarControl.SetShieldAnalyzing();
            return;
        }

        var result = TryComputeSafetyResult(tabId);
        if (result != null)
        {
            UrlBarControl.UpdateShieldScore(result);
            RefreshInspectorWindow(tabId);
        }
        else
        {
            UrlBarControl.SetShieldAnalyzing();
        }
    }

    // ── Split view ────────────────────────────────────────────────────────

    private void ActivateSplit()
    {
        if (_isSplitMode || _tabManager.Tabs.Count < 1) return;

        _primaryTabId = _tabManager.ActiveTab?.Id;

        // Upgrade BrowserContent to 3-column grid (left | splitter | right)
        BrowserContent.ColumnDefinitions.Clear();
        BrowserContent.ColumnDefinitions.Add(new ColumnDefinition());   // col 0: primary *
        BrowserContent.ColumnDefinitions.Add(new ColumnDefinition      // col 1: splitter 4 px
            { Width = new GridLength(4) });
        BrowserContent.ColumnDefinitions.Add(new ColumnDefinition());   // col 2: secondary *

        _panesSplitter = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Background          = new System.Windows.Media.SolidColorBrush(
                                      System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x3A)),
            ResizeBehavior      = GridResizeBehavior.PreviousAndNext,
            ResizeDirection     = GridResizeDirection.Columns,
        };
        Grid.SetColumn(_panesSplitter, 1);
        // Insert at index 0 so existing BrowserTab children sit on top in z-order
        BrowserContent.Children.Insert(0, _panesSplitter);

        // Flag split mode ON before CreateTab fires, so OnTabActivated routes correctly
        _isSplitMode = true;
        _suppressSplitActivation = true;

        // Create the secondary tab — OnTabActivated will be suppressed;
        // _splitTabId is set there when _suppressSplitActivation is true.
        _tabManager.CreateTab();

        _suppressSplitActivation = false;

        // Re-sync TabManager's active tab back to primary so that keyboard shortcuts,
        // ToggleBookmark, SwitchTabByOffset etc. all target the left pane.
        // OnTabActivated will fire, see _isSplitMode, and trigger RefreshSplitLayout + UpdatePrimaryUiAsync.
        if (_primaryTabId != null)
            _tabManager.ActivateTab(_primaryTabId);

        // Visual feedback on sidebar button
        TabSidebarControl.SetSplitActive(true);
    }

    private void DeactivateSplit(string? closingTabId = null)
    {
        if (!_isSplitMode) return;
        _isSplitMode = false;

        // Close the secondary tab (unless it was the one that triggered this cleanup)
        if (_splitTabId != null && _splitTabId != closingTabId)
            _tabManager.CloseTab(_splitTabId);

        _splitTabId   = null;
        _primaryTabId = null;

        // Remove the splitter
        if (_panesSplitter != null)
        {
            BrowserContent.Children.Remove(_panesSplitter);
            _panesSplitter = null;
        }

        // Collapse back to single implicit column
        BrowserContent.ColumnDefinitions.Clear();
        foreach (var bt in _browserTabs.Values)
        {
            Grid.SetColumn(bt, 0);
            bt.Visibility = Visibility.Collapsed;
        }

        // Re-activate the primary tab (or whatever TabManager considers active)
        var resumeId = _tabManager.ActiveTab?.Id;
        if (resumeId != null && _browserTabs.TryGetValue(resumeId, out var resumeBt))
            resumeBt.Visibility = Visibility.Visible;

        TabSidebarControl.SetActiveTab(resumeId ?? "");
        TabSidebarControl.SetSplitActive(false);
    }

    private void RefreshSplitLayout()
    {
        foreach (var (tabId, bt) in _browserTabs)
        {
            if (tabId == _primaryTabId)
            {
                Grid.SetColumn(bt, 0);
                bt.Visibility = Visibility.Visible;
            }
            else if (tabId == _splitTabId)
            {
                Grid.SetColumn(bt, 2);
                bt.Visibility = Visibility.Visible;
            }
            else
            {
                Grid.SetColumn(bt, 0);
                bt.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ── Find bar ─────────────────────────────────────────────────────────

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => _ = DoFindAsync(backwards: false);

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = DoFindAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => _ = DoFindAsync(backwards: false);
    private void FindPrev_Click(object sender, RoutedEventArgs e) => _ = DoFindAsync(backwards: true);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();

    private async Task DoFindAsync(bool backwards)
    {
        var text = FindTextBox.Text.Trim();
        var bt = ActiveBrowserTab();
        if (bt == null) return;

        if (string.IsNullOrEmpty(text))
        {
            await bt.FindClearAsync();
            FindStatusText.Text = "";
            return;
        }

        var found = await bt.FindAsync(text, backwards);
        FindStatusText.Text = found ? "" : VELO.Core.Localization.LocalizationService.Current.T("find.notfound");
        FindStatusText.Foreground = found
            ? System.Windows.Media.Brushes.Transparent
            : System.Windows.Media.Brushes.OrangeRed;
    }

    private void CloseFindBar()
    {
        FindBarPanel.Visibility = Visibility.Collapsed;
        FindTextBox.Text = "";
        FindStatusText.Text = "";
        _ = ActiveBrowserTab()?.FindClearAsync();
    }

    /// <summary>
    /// v2.0.5.3 — Localises the find-bar tooltips/labels. Wired to
    /// LocalizationService.LanguageChanged in the constructor.
    /// </summary>
    private void ApplyFindBarLanguage()
    {
        var L = VELO.Core.Localization.LocalizationService.Current;
        FindLabel.Text             = L.T("find.label");
        FindPrevButton.ToolTip     = L.T("find.prev");
        FindNextButton.ToolTip     = L.T("find.next");
        FindCloseButton.ToolTip    = L.T("find.close");
    }

    /// <summary>
    /// v2.0.5 — Public entry point used by SingleInstanceManager when another
    /// VELO process is launched with a URL (e.g. an external program opens a
    /// link, or Bambu Studio launches its update download). Opens the URL in
    /// a new tab and brings the window to the foreground.
    /// </summary>
    public void OpenUrlInNewTab(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        Dispatcher.Invoke(() =>
        {
            try
            {
                _tabManager.CreateTab(url);

                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
                Topmost = true;   // force to front…
                Topmost = false;  // …without keeping it pinned
                Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[VELO] OpenUrlInNewTab failed: {ex.Message}");
            }
        });
    }

    // ── Window closing ───────────────────────────────────────────────────

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Phase 3 / Sprint 3 — final clean snapshot. Heartbeats during the
        // session wrote WasCleanShutdown=false; this final write flips it
        // to true so next-launch knows to either silently restore (when
        // session.restore_always=true) or stay quiet.
        try
        {
            _sessionTimer?.Stop();
            await SaveSessionSnapshotAsync(cleanShutdown: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Window_Closing snapshot failed: {ex.Message}");
        }

        await _navController.ClearDataOnExitAsync();

        // v2.0.5 — Stop every WebView2 in this window BEFORE the window closes.
        // Without this, tear-off windows leak audio/video because their
        // CoreWebView2 child processes survive until GC. CloseTab() mutes,
        // navigates to about:blank and disposes the WebView control, which
        // terminates the underlying browser process for that tab.
        foreach (var bt in _browserTabs.Values.ToList())
        {
            try { bt.CloseTab(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[VELO] Window_Closing CloseTab failed: {ex.Message}");
            }
        }
        _browserTabs.Clear();

        // Only shut down the whole app if this is the last MainWindow.
        // With tear-off we may have several MainWindow instances alive; closing
        // a torn-off window must leave the others running (App.xaml uses
        // ShutdownMode="OnExplicitShutdown", so we decide explicitly).
        var remaining = Application.Current.Windows
            .OfType<MainWindow>()
            .Count(w => !ReferenceEquals(w, this));

        if (remaining == 0)
            Application.Current.Shutdown();
    }
}
