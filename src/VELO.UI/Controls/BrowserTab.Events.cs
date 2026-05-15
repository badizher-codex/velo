using System.IO;
using System.Net;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using VELO.Core.Containers;
using VELO.Core.Downloads;
using VELO.Security.AI;
using VELO.Security.AI.Models;
using VELO.Security.Guards;

namespace VELO.UI.Controls;

// Phase 3 / Sprint 10b chunk 6 (v2.4.31) — Event handlers partition.
// All WebView2 event handlers (resource requests, navigation, web messages,
// certificate errors, downloads, popups, context menus) live here. The handlers
// are subscribed in BrowserTab.EnsureWebViewInitializedAsync (core file).
// Sibling partials: BrowserTab.xaml.cs (core + lifecycle + DI setters),
// BrowserTab.PublicApi.cs (host-facing methods), BrowserTab.Helpers.cs (statics).
public partial class BrowserTab
{
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
                    RiskScore       = 60,
                    // v2.4.24 — surface autofill.js's "page has a password
                    // input" signal so PhishingShield's quick-gate actually
                    // fires on login pages.
                    HasLoginForm    = _hasLoginFormOnCurrentPage,
                    // v2.4.26 — current <title> for PhishingShield's prompt.
                    // Truncation to MaxTitleChars happens inside BuildPrompt.
                    PageTitle       = _currentPageTitle,
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

        // v2.4.43 — favicon cache preload. Best-effort: if we've cached the icon
        // for this host within the FaviconRepository TTL, raise FaviconCaptured
        // immediately so the sidebar swaps 🌐 → real icon before the page even
        // commits. The real FaviconChanged from WebView2 will arrive later and
        // overwrite if the site actually updated its icon.
        if (_faviconRepo is not null &&
            Uri.TryCreate(navUri, UriKind.Absolute, out var preloadUri) &&
            !string.IsNullOrEmpty(preloadUri.Host))
        {
            var hostForPreload = preloadUri.Host;
            _ = Task.Run(async () =>
            {
                try
                {
                    var cached = await _faviconRepo.GetFreshAsync(hostForPreload).ConfigureAwait(false);
                    if (cached is { Length: > 0 })
                    {
                        Dispatcher.Invoke(() => FaviconCaptured?.Invoke(this, cached));
                    }
                }
                catch { /* cosmetic — silent */ }
            });
        }

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
        // v2.4.24 — clear has-login-form flag on every navigation; autofill.js
        // will re-set it after DOMContentLoaded if the new page has a password
        // field. Without this reset, leaving a login page to a safe one would
        // keep PhishingShield seeing a stale "login present" signal.
        _hasLoginFormOnCurrentPage = false;
        // v2.4.26 — clear page-title cache on every navigation. OnTitleChanged
        // re-populates as soon as Chromium parses the new <title>. Without
        // this reset, PhishingShield could see the previous page's title in
        // sub-resource ThreatContexts during the gap between commit and
        // DocumentTitleChanged.
        _currentPageTitle = "";

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

            // v2.4.46 Phase 4.1 chunk E — Council bridge fast-path. Council
            // payloads use a "type":"council/..." discriminator (NOT the
            // legacy "kind" field other features use), so we fork before
            // touching the kind-based switch. CouncilBridgeParser fast-fails
            // on non-council/* types, so non-Council messages flow through
            // unchanged. Only routes to the host when this tab is actually a
            // Council panel AND the parse succeeded.
            if (IsCouncilPanel && _councilProvider is { } prov &&
                !string.IsNullOrEmpty(json) &&
                json.Contains("\"council/", StringComparison.Ordinal))
            {
                var msg = VELO.Core.Council.CouncilBridgeParser.Parse(json, prov);
                if (msg is not null)
                {
                    Dispatcher.Invoke(() => CouncilBridgeMessageReceived?.Invoke(this, msg));
                    return; // Council payload — don't fall through to the legacy switch.
                }
            }

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
                    {
                        // v2.4.24 — flip the per-page flag so PhishingShield gets
                        // HasLoginForm=true in the ThreatContext for subsequent
                        // sub-resource evaluations on this page.
                        _hasLoginFormOnCurrentPage = true;
                        Dispatcher.Invoke(() => AutofillFormDetected?.Invoke(this, h));
                    }
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

        // v2.4.46 Phase 4.1 chunk E — push the per-provider selector pack to
        // the page-side bridge so __veloCouncil's observer wires against the
        // fresh DOM. Fire-and-forget: failures (network nav stall, page not
        // ready, bridge not injected for some reason) silently fall back to
        // the inert state — the panel won't capture but won't crash either.
        if (IsCouncilPanel && _councilBridgeInjected &&
            _councilProvider is { } prov && _councilAdapters is not null)
        {
            _ = PushCouncilAdapterAsync(prov);
        }
    }

    /// <summary>v2.4.46 — Serialises the adapter JSON for <paramref name="provider"/>
    /// and hands it to <c>window.__veloCouncil.setAdapter(...)</c>. Runs on the
    /// dispatcher because ExecuteScriptAsync wants to be marshalled to the
    /// browser thread.</summary>
    private async Task PushCouncilAdapterAsync(VELO.Core.Council.CouncilProvider provider)
    {
        try
        {
            var json = _councilAdapters?.GetAdapterJson(provider);
            if (string.IsNullOrEmpty(json)) return;
            // The bridge's setAdapter accepts either an object or a JSON string.
            // We embed as a JSON string literal so we don't have to worry about
            // the consumer's quote-escaping for nested selectors.
            var jsLiteral = System.Text.Json.JsonSerializer.Serialize(json);
            var script    = $"if (window.__veloCouncil) {{ window.__veloCouncil.setAdapter({jsLiteral}); }}";
            await Dispatcher.InvokeAsync(async () =>
            {
                try { await WebView.CoreWebView2.ExecuteScriptAsync(script); }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[VELO] Council setAdapter exec failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[VELO] Council setAdapter outer failed: {ex.Message}");
        }
    }

    private void OnTitleChanged(object? sender, object e)
        => Dispatcher.Invoke(() =>
        {
            // v2.4.26 — cache the title so ThreatContext.PageTitle picks it up
            // without round-tripping to WebView2 on every sub-resource request.
            _currentPageTitle = WebView.CoreWebView2.DocumentTitle ?? "";
            TitleChanged?.Invoke(this, _currentPageTitle);
        });

    private void OnLaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
    {
        // Suppress WebView2's default permission dialog — we show our own.
        e.Cancel = true;
        var uri = e.Uri ?? "";
        Dispatcher.Invoke(() => TryLaunchExternalUri(uri));
    }

    /// <summary>
    /// v2.4.43 — WebView2 raises FaviconChanged once a page declares an icon
    /// via &lt;link rel="icon"&gt; or once /favicon.ico resolves. We materialise
    /// the PNG bytes via <c>GetFaviconAsync</c>, raise <see cref="FaviconCaptured"/>
    /// (the host updates TabInfo.FaviconData → TabSidebar re-renders via the
    /// BytesToImage converter) and persist by host in <c>FaviconRepository</c>
    /// so future sessions get the icon instantly from the local SQLite cache
    /// without re-paying the network fetch.
    ///
    /// Failures are swallowed — favicon capture is best-effort cosmetic and
    /// must never crash a tab. The sidebar's 🌐 fallback covers all error paths.
    /// </summary>
    private async void OnFaviconChanged(object? sender, object e)
    {
        try
        {
            // v2.4.45 — CoreWebView2.GetFaviconAsync(format) returns Task<Stream>
            // directly in current WebView2 SDK (1.0.2592+). v2.4.43 chained
            // .AsTask() assuming the older IAsyncOperation<IRandomAccessStream>
            // shape; that compiled locally (cached references in the obj/
            // tree) but failed clean-publish in CI with CS1061 — see lesson #22.
            using var stream = await WebView.CoreWebView2
                .GetFaviconAsync(CoreWebView2FaviconImageFormat.Png)
                .ConfigureAwait(true);

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms).ConfigureAwait(true);
                bytes = ms.ToArray();
            }

            // Raise on the dispatcher so the host's binding update happens on UI thread.
            Dispatcher.Invoke(() => FaviconCaptured?.Invoke(this, bytes));

            // Persist by host (lowercased + www-stripped inside the repo).
            if (_faviconRepo is not null && bytes.Length > 0)
            {
                try
                {
                    var url = WebView.CoreWebView2.Source;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
                        await _faviconRepo.SaveAsync(u.Host, bytes).ConfigureAwait(false);
                }
                catch { /* favicon cache is cosmetic — never propagate */ }
            }
        }
        catch
        {
            // GetFaviconAsync occasionally throws on non-resolvable icon URIs
            // or before the page is fully attached. Silent fail — keep 🌐.
        }
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
}
