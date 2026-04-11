using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;
using VELO.Vault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using VELO.App.Startup;
using VELO.Core.Downloads;
using VELO.Core.Events;
using VELO.Core.Localization;
using VELO.Core.Navigation;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Security.AI;
using VELO.Security.AI.Models;
using VELO.Security.Guards;
using VELO.UI.Controls;
using VELO.UI.Dialogs;

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

    private CoreWebView2Environment? _webViewEnv;
    private string _fingerprintLevel = "Aggressive";
    private string _webRtcMode       = "Relay";
    private readonly Dictionary<string, BrowserTab> _browserTabs = [];
    private readonly HashSet<string> _navigatedTabs = [];
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

    public MainWindow(IServiceProvider services)
    {
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

        InitializeComponent();

        _eventBus.Subscribe<TabCreatedEvent>(OnTabCreated);
        _eventBus.Subscribe<TabClosedEvent>(OnTabClosed);
        _eventBus.Subscribe<TabActivatedEvent>(OnTabActivated);

        UrlBarControl.BookmarkRequested  += async (_, _) => await ToggleBookmarkAsync();
        UrlBarControl.ZoomResetRequested += (_, _) => ActiveBrowserTab()?.ResetZoom();
        UrlBarControl.ReaderModeRequested += async (_, _) =>
            { if (ActiveBrowserTab() is { } bt) await bt.ToggleReaderModeAsync(); };

        TabBarControl.TabContainerChangeRequested += (_, args) =>
        {
            var (tabId, containerId) = args;
            _tabManager.UpdateTab(tabId, t => t.ContainerId = containerId);
            if (_tabManager.ActiveTab?.Id == tabId)
                UrlBarControl.SetContainer(containerId, ContainerColor(containerId));
        };

        Loaded += async (_, _) =>
        {
            // Pre-load already-captured threat types so capture logic is O(1)
            var mdex = _services.GetRequiredService<MalwaredexRepository>();
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

        var userDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VELO", "Profile");

        _webViewEnv = await CoreWebView2Environment.CreateAsync(null, userDataPath, options);

        // Cache privacy settings used by every new tab
        _fingerprintLevel = await _settings.GetAsync(SettingKeys.FingerprintLevel, "Aggressive");
        _webRtcMode       = await _settings.GetAsync(SettingKeys.WebRtcMode, "Relay");

        // Set AI status indicator — ping Ollama in background, update dot when result arrives
        var aiMode    = await _settings.GetAsync(SettingKeys.AiMode, "Offline");
        var aiModel   = await _settings.GetAsync(SettingKeys.AiClaudeModel, "");
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

        // Create initial tab
        _tabManager.CreateTab();
    }

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
                if (_tabManager.ActiveTab?.Id == e.TabId)
                    UrlBarControl.SetTlsStatus(status);
            });
            browserTab.Visibility = Visibility.Collapsed;

            browserTab.UrlChanged += (_, url) => OnBrowserUrlChanged(e.TabId, url);
            browserTab.TitleChanged += (_, title) => OnBrowserTitleChanged(e.TabId, title);
            browserTab.LoadingChanged += (_, loading) => OnBrowserLoadingChanged(e.TabId, loading);
            browserTab.NavigationStateChanged += (_, state) => OnNavigationStateChanged(e.TabId, state);
            browserTab.SecurityVerdictReceived += (_, verdict) => OnSecurityVerdict(e.TabId, verdict);
            browserTab.ZoomChanged += (_, factor) => Dispatcher.Invoke(() =>
            {
                if (_tabManager.ActiveTab?.Id == e.TabId)
                    UrlBarControl.SetZoom(factor);
            });

            // Add to panel (keeps WebView2 HWND alive across tab switches)
            BrowserContent.Children.Add(browserTab);
            _browserTabs[e.TabId] = browserTab;
            TabBarControl.AddTab(tab);

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
            TabBarControl.RemoveTab(e.TabId);
        });
    }

    private void OnTabActivated(TabActivatedEvent e)
    {
        Dispatcher.Invoke(async () =>
        {
            if (!_browserTabs.TryGetValue(e.TabId, out var browserTab)) return;

            // Show active tab, hide all others
            foreach (var kv in _browserTabs)
                kv.Value.Visibility = kv.Key == e.TabId ? Visibility.Visible : Visibility.Collapsed;

            TabBarControl.SetActiveTab(e.TabId);

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
            }

            // Only navigate on first activation — switching back must not reload
            if (!_navigatedTabs.Contains(e.TabId) && tab?.Url != null)
            {
                _navigatedTabs.Add(e.TabId);
                await browserTab.NavigateAsync(tab.Url);
            }
        });
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

            // Reset block/capture counters for this tab on new navigation
            _tabBlockCounts[tabId] = (0, 0, 0);
            _tabNewCapture.Remove(tabId);
            _tabVerdictLevel[tabId] = 0;

            _tabManager.UpdateTab(tabId, t => t.Url = url);
            if (_tabManager.ActiveTab?.Id == tabId)
            {
                UrlBarControl.SetUrl(url);
                await UpdateBookmarkStarAsync(url);
                var isRealPage = !string.IsNullOrEmpty(url)
                    && url != "velo://newtab"
                    && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                UrlBarControl.SetReaderModeAvailable(isRealPage);
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
                TabBarControl.UpdateTab(tab);
                if (_tabManager.ActiveTab?.Id == tabId)
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
            if (_tabManager.ActiveTab?.Id == tabId)
                UrlBarControl.SetLoading(loading);
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
            if (_tabManager.ActiveTab?.Id == tabId)
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

            if (_tabManager.ActiveTab?.Id != tabId) return;
            if (verdict.Verdict == VerdictType.Safe) return;

            // Only show the panel if this verdict is more severe than what was already shown
            // this navigation (prevents NavGuard + AI double-popup for the same page)
            var newLevel = verdict.Verdict == VerdictType.Block ? 2 : 1;
            var shownLevel = _tabVerdictLevel.GetValueOrDefault(tabId, 0);
            if (newLevel <= shownLevel) return;
            _tabVerdictLevel[tabId] = newLevel;

            var domain = "";
            try { domain = new Uri(_tabManager.GetTab(tabId)?.Url ?? "").Host; } catch { }
            SecurityPanelControl.Show(domain, verdict);
        });
    }

    // ── UrlBar events ────────────────────────────────────────────────────

    private async void UrlBar_NavigationRequested(object? sender, string input)
    {
        SecurityPanelControl.Hide();
        var url = await _navController.ResolveUrlAsync(input);
        var activeTabId = _tabManager.ActiveTab?.Id;
        if (activeTabId != null && _browserTabs.TryGetValue(activeTabId, out var bt))
            await bt.NavigateAsync(url);
    }

    private void UrlBar_BackRequested(object? sender, EventArgs e)
    {
        if (_tabManager.ActiveTab?.Id is string id && _browserTabs.TryGetValue(id, out var bt))
            bt.GoBack();
    }

    private void UrlBar_ForwardRequested(object? sender, EventArgs e)
    {
        if (_tabManager.ActiveTab?.Id is string id && _browserTabs.TryGetValue(id, out var bt))
            bt.GoForward();
    }

    private void UrlBar_ReloadRequested(object? sender, EventArgs e)
    {
        if (_tabManager.ActiveTab?.Id is string id && _browserTabs.TryGetValue(id, out var bt))
            bt.Reload();
    }

    private void UrlBar_StopRequested(object? sender, EventArgs e)
    {
        if (_tabManager.ActiveTab?.Id is string id && _browserTabs.TryGetValue(id, out var bt))
            bt.Stop();
    }

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
            await _services.GetRequiredService<AppBootstrapper>().ConfigureAIAdapterAsync();
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

        var itemAbout = new MenuItem { Header = loc.T("menu.about") };
        itemAbout.Click += async (_, _) =>
        {
            var activeId = _tabManager.ActiveTab?.Id;
            if (activeId != null && _browserTabs.TryGetValue(activeId, out var bt))
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
        menu.Items.Add(itemClearData);
        menu.Items.Add(itemAbout);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ── TabBar events ────────────────────────────────────────────────────

    private void TabBar_NewTabRequested(object? sender, EventArgs e)
        => _tabManager.CreateTab();

    private void TabBar_TabSelected(object? sender, string tabId)
        => _tabManager.ActivateTab(tabId);

    private void TabBar_TabCloseRequested(object? sender, string tabId)
        => _tabManager.CloseTab(tabId);

    // ── Security panel ───────────────────────────────────────────────────

    private void Security_AllowOnce(object? sender, string domain)
    {
        if (_tabManager.ActiveTab?.Id is string id && _browserTabs.TryGetValue(id, out var bt))
            bt.AllowOnce(domain);
    }

    private void Security_Whitelist(object? sender, string domain)
    {
        RequestGuard.AddToWhitelist(domain);
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
            var activeId = _tabManager.ActiveTab?.Id;
            if (activeId != null && _browserTabs.TryGetValue(activeId, out var bt))
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
            var activeId = _tabManager.ActiveTab?.Id;
            if (activeId != null && _browserTabs.TryGetValue(activeId, out var bt))
                await bt.NavigateAsync(url);
        };
        win.ShowDialog();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<bool> PingOllamaAsync(string endpoint, string model)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync($"{endpoint.TrimEnd('/')}/api/tags");
            if (!response.IsSuccessStatusCode) return false;
            var body = await response.Content.ReadAsStringAsync();
            // Check the model is actually loaded/available
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
        var id = _tabManager.ActiveTab?.Id;
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
        FindStatusText.Text = found ? "" : "No encontrado";
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

    // ── Window closing ───────────────────────────────────────────────────

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        await _navController.ClearDataOnExitAsync();
        Application.Current.Shutdown();
    }
}
