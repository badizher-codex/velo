using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VELO.Core.Downloads;
using VELO.Data.Repositories;
using VELO.Security;
using VELO.Security.AI;
using VELO.Security.AI.Models;
using VELO.Security.Guards;
using VELO.UI.Controls;

namespace VELO.App.Controllers;

/// <summary>
/// Phase 3 / Sprint 10b chunk 2 (v2.4.30) — Extracted from
/// <c>MainWindow.OnTabCreated</c>. Centralises the per-tab setup that
/// used to live as ~90 lines of inline <c>Set*()</c> calls and event
/// subscriptions in MainWindow. The host class hands the controller a
/// <see cref="TabWiringHandlers"/> bundle of callbacks and gets back a
/// fully wired <see cref="BrowserTab"/> ready to be added to the visual
/// tree.
///
/// Why this exists:
///
///   1. <b>One place catches missing wires.</b> The
///      <see cref="VELO.UI.Controls.BrowserTab"/> exposes ~10 setters
///      and ~10 events; an inline OnTabCreated easily forgets one. The
///      v2.4.21 PasteGuard bug (~6 months dormant) is the classic
///      example. The WiringSmokeTests file-scan now points at this
///      class, so a new BrowserTab setter that lacks a call here trips
///      the test at build time.
///
///   2. <b>Phase 4 reuse.</b> Council Mode spins up four BrowserTab
///      instances per session, each in its own container with its own
///      handlers (Council-specific). The 2×2 layout work in Phase 4.0
///      will call <see cref="BuildAndWire"/> with Council handlers
///      instead of duplicating the per-tab setup ladder.
///
///   3. <b>MainWindow shrinkage.</b> Sprint 10b's continuing goal —
///      moving MainWindow.xaml.cs from ~2,800 lines toward ~1,500.
///
/// Pure DI orchestration. No WPF visual-tree changes (MainWindow still
/// adds the BrowserTab to its panel and indexes it in _browserTabs).
/// </summary>
public sealed class BrowserTabHost
{
    /// <summary>
    /// All the per-tab event hooks the host (MainWindow) needs to register.
    /// Every handler is invoked on the dispatcher thread that raised the
    /// event — handlers that touch WPF must marshal themselves; the controller
    /// does not <c>Dispatcher.Invoke</c> on their behalf to keep semantics
    /// identical to the previous inline code.
    /// </summary>
    public sealed record TabWiringHandlers(
        Action<string, string>                                        OnUrlChanged,
        Action<string, string>                                        OnTitleChanged,
        Action<string, bool>                                          OnLoadingChanged,
        Action<string, (bool CanBack, bool CanForward)>               OnNavigationStateChanged,
        Action<string, AIVerdict>                                     OnSecurityVerdict,
        Action<string, TlsStatus>                                     OnTlsStatusChanged,
        Action<string, double>                                        OnZoomChanged,
        Action<string, string>                                        OnGlanceLinkHovered,
        Action<string, string>                                        OnAutofillFormDetected,
        Action<string, (string Host, string Username, string Password)> OnAutofillFormSubmitted,
        Action<string, byte[]>                                        OnFaviconCaptured,
        Action<string, VELO.Core.Council.CouncilBridgeMessage>        OnCouncilBridgeMessage);

    private readonly IServiceProvider _services;

    public BrowserTabHost(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Builds a <see cref="BrowserTab"/>, initialises it with the security
    /// stack, subscribes every event to the supplied handlers, calls every
    /// <c>Set*()</c> on the resolved DI services, and returns the tab
    /// ready to be inserted into the visual tree. The host is still
    /// responsible for:
    ///
    ///   • Adding the returned BrowserTab to its panel.
    ///   • Indexing it in its tab dictionary.
    ///   • Calling <c>EnsureWebViewInitializedAsync</c> with the WebView2
    ///     environment after the visual tree adoption.
    /// </summary>
    public BrowserTab BuildAndWire(
        string             tabId,
        AISecurityEngine   aiEngine,
        RequestGuard       requestGuard,
        TLSGuard           tlsGuard,
        DownloadGuard      downloadGuard,
        DownloadManager    downloadManager,
        string             fingerprintLevel,
        string             webRtcMode,
        TabWiringHandlers  handlers)
    {
        var browserTab = new BrowserTab();

        // Core init — passes the security stack the tab needs to gate
        // every navigation, sub-resource and download decision.
        browserTab.Initialize(tabId, aiEngine, requestGuard, tlsGuard,
            downloadGuard, downloadManager, fingerprintLevel, webRtcMode);
        browserTab.Visibility = Visibility.Collapsed;

        // ── Event subscriptions ──────────────────────────────────────────
        browserTab.UrlChanged              += (_, url)     => handlers.OnUrlChanged(tabId, url);
        browserTab.TitleChanged            += (_, title)   => handlers.OnTitleChanged(tabId, title);
        browserTab.LoadingChanged          += (_, loading) => handlers.OnLoadingChanged(tabId, loading);
        browserTab.NavigationStateChanged  += (_, state)   => handlers.OnNavigationStateChanged(tabId, state);
        browserTab.SecurityVerdictReceived += (_, verdict) => handlers.OnSecurityVerdict(tabId, verdict);
        browserTab.TlsStatusChanged        += (_, status)  => handlers.OnTlsStatusChanged(tabId, status);
        browserTab.ZoomChanged             += (_, factor)  => handlers.OnZoomChanged(tabId, factor);
        browserTab.GlanceLinkHovered       += (_, url)     => handlers.OnGlanceLinkHovered(tabId, url);
        browserTab.AutofillFormDetected    += (_, host)    => handlers.OnAutofillFormDetected(tabId, host);
        browserTab.AutofillFormSubmitted   += (_, payload) => handlers.OnAutofillFormSubmitted(tabId, payload);
        browserTab.FaviconCaptured         += (_, bytes)   => handlers.OnFaviconCaptured(tabId, bytes);
        browserTab.CouncilBridgeMessageReceived += (_, msg) => handlers.OnCouncilBridgeMessage(tabId, msg);

        // ── Setters — resolved from DI ───────────────────────────────────
        // Sprint 6: history repo so NewTab v2 can render top sites.
        browserTab.SetHistoryRepository(_services.GetRequiredService<HistoryRepository>());

        // v2.4.43 — favicon cache (per-host SQLite). Preload on NavigationStarting,
        // overwrite on WebView2 FaviconChanged. Sidebar swaps 🌐 fallback for real
        // bitmap once bytes arrive via the FaviconCaptured event above.
        browserTab.SetFaviconRepository(
            _services.GetRequiredService<FaviconRepository>());

        // v2.4.46 Phase 4.1 chunk E — Council Mode panel integration.
        // Only Council-container tabs ever inject council-bridge.js or push
        // an adapter (the BrowserTab.IsCouncilPanel gate handles that — these
        // setters are cheap when the tab is non-Council, the references just
        // stay un-consulted).
        browserTab.SetCouncilOrchestrator(
            _services.GetRequiredService<VELO.Core.Council.CouncilOrchestrator>());
        browserTab.SetCouncilAdaptersRegistry(
            _services.GetRequiredService<VELO.Core.Council.CouncilAdaptersRegistry>());

        // Phase 3 / Sprint 1E — IA menu (composes the inner ContextMenuBuilder).
        browserTab.SetAIContextMenuBuilder(
            _services.GetRequiredService<AIContextMenuBuilder>());

        // v2.4.16 — also wire the inner ContextMenuBuilder so the tab can
        // raise menu-action events (RequestPaste needs WebView access).
        browserTab.SetContextMenuBuilder(
            _services.GetRequiredService<ContextMenuBuilder>());

        // v2.1.5.1 — per-site fingerprint/WebRTC relaxation list.
        browserTab.SetShieldsAllowlist(
            _services.GetRequiredService<ShieldsAllowlist>());

        // v2.4.21 — PasteGuard wiring caught by WiringSmokeTests. PasteGuard
        // existed in DI since Phase 2 / Sprint 3 but MainWindow never called
        // the setter; the JS message bridge was always inert. Smoke test now
        // blocks regressions of this exact pattern.
        browserTab.SetPasteGuard(
            _services.GetRequiredService<PasteGuard>());

        // v2.4.22 — Sprint 8A wire. SmartBlock async classifier per tab.
        // Cache miss → fire-and-forget classification; the next request to
        // the same host reads the verdict sync via RequestGuard.
        browserTab.SetSmartBlockClassifier(
            _services.GetRequiredService<SmartBlockClassifier>());

        // Phase 3 / Sprint 5 — autofill prompt + save-on-submit.
        browserTab.SetAutofillService(
            _services.GetRequiredService<VELO.Vault.AutofillService>());

        return browserTab;
    }
}
