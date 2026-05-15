using System.Net;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using VELO.Core.Containers;
using VELO.Core.Localization;

namespace VELO.UI.Controls;

// Phase 3 / Sprint 10b chunk 6 (v2.4.31) — Public API partition.
// Host-facing methods (navigation, zoom, find, paste, page-content extraction,
// reader mode, view switching) + the paste cluster called by ContextMenuBuilder.
// Sibling partials: BrowserTab.xaml.cs (core + lifecycle + DI setters),
// BrowserTab.Helpers.cs (statics + external-launch), BrowserTab.Events.cs (WebView2 handlers).
public partial class BrowserTab
{
    // ── Paste cluster ────────────────────────────────────────────────────

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
    /// v2.4.23 — exposed so the clipboard-history dialog can paste an entry
    /// into the page's focused editable without going through the system
    /// clipboard a second time.
    /// </summary>
    public Task PasteTextAsync(string text) => PasteTextIntoFocusedEditableAsync(text);

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

    // ── Autofill / DevTools / Container ─────────────────────────────────

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
        // v2.4.46 Phase 4.1 chunk E — resolve Council provider from container ID.
        // Returns null for non-Council containers (Personal, Work, Banking, Shopping,
        // None …), in which case the bridge JS is never injected and the
        // WebMessageReceived dispatcher ignores council/* payloads. The mapping
        // is canonical: council-claude → Claude, council-chatgpt → ChatGpt, etc.
        _councilProvider = VELO.Core.Council.CouncilProviderMap.FromContainerId(containerId);
    }

    /// <summary>True when this tab lives in a Council container — i.e. the
    /// bridge JS is supposed to be active for it.</summary>
    public bool IsCouncilPanel => _councilProvider is not null;

    /// <summary>The Council provider this panel hosts, or null when the tab is
    /// not a Council slot. Resolved from <see cref="_currentContainerId"/>
    /// inside <see cref="SetContainer"/>.</summary>
    public VELO.Core.Council.CouncilProvider? CouncilProvider => _councilProvider;

    // ── Navigation ──────────────────────────────────────────────────────

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

    public void GoBack()    { if (WebView.CoreWebView2?.CanGoBack    == true) WebView.CoreWebView2.GoBack(); }
    public void GoForward() { if (WebView.CoreWebView2?.CanGoForward == true) WebView.CoreWebView2.GoForward(); }
    public void Reload()    => WebView.CoreWebView2?.Reload();
    public void Stop()      => WebView.CoreWebView2?.Stop();

    // ── Zoom ────────────────────────────────────────────────────────────

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

    // ── Find ────────────────────────────────────────────────────────────

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

    // ── Lifecycle (close, allow once, script execution) ─────────────────

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

    /// <summary>v2.4.49 — Captures the current page's scroll position so a tear-off
    /// can restore it after the new window navigates to the same URL. Returns
    /// <see cref="VELO.Core.Navigation.TabSnapshot.Empty"/> on any failure (page
    /// not loaded, script execution refused, malformed JSON).</summary>
    public async Task<VELO.Core.Navigation.TabSnapshot> CaptureSnapshotAsync()
    {
        if (!_webViewInitialized) return VELO.Core.Navigation.TabSnapshot.Empty;
        try
        {
            var raw = await WebView.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify({x: window.scrollX || 0, y: window.scrollY || 0})");
            if (string.IsNullOrEmpty(raw) || raw == "null") return VELO.Core.Navigation.TabSnapshot.Empty;
            // ExecuteScriptAsync returns the JSON-encoded string of the expression's
            // value — i.e. "{\"x\":0,\"y\":42}". Deserialise once to unwrap.
            var inner = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrEmpty(inner)) return VELO.Core.Navigation.TabSnapshot.Empty;
            using var doc = System.Text.Json.JsonDocument.Parse(inner);
            var x = doc.RootElement.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xv) ? xv : 0;
            var y = doc.RootElement.TryGetProperty("y", out var yEl) && yEl.TryGetDouble(out var yv) ? yv : 0;
            return new VELO.Core.Navigation.TabSnapshot(x, y);
        }
        catch
        {
            return VELO.Core.Navigation.TabSnapshot.Empty;
        }
    }

    /// <summary>v2.4.49 — Restores the scroll position captured by
    /// <see cref="CaptureSnapshotAsync"/>. Called after NavigationCompleted in
    /// the new window. The 200 ms delay lets layout settle for pages with
    /// lazy-loaded images / dynamic content above the fold; without it
    /// <c>window.scrollTo</c> can run before the document reaches its full
    /// height and the scroll silently clamps to 0.</summary>
    public async Task RestoreSnapshotAsync(VELO.Core.Navigation.TabSnapshot snapshot)
    {
        if (!_webViewInitialized) return;
        if (!snapshot.HasContent) return;
        try
        {
            await Task.Delay(200);
            var x = snapshot.ScrollX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var y = snapshot.ScrollY.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WebView.CoreWebView2.ExecuteScriptAsync(
                $"window.scrollTo({{ left: {x}, top: {y}, behavior: 'instant' }})");
        }
        catch { /* cosmetic — never propagate */ }
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

    // ── Page content / Reader mode ──────────────────────────────────────

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
            text = WebUtility.HtmlDecode(text);
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

    // ── View switching helpers (used by NavigateAsync) ──────────────────

    private void ShowNewTabPage()
    {
        NewTabPageControl.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        TitleChanged?.Invoke(this, LocalizationService.Current.T("newtab.title"));
        NewTabPageControl.Focus();

        // Sprint 6: load top sites from history DB (fire-and-forget; never blocks nav)
        // v2.4.50 — also pass the FaviconRepository so the tiles render with real
        // site icons instead of letter-circle fallbacks.
        if (_historyRepo != null)
            _ = NewTabPageControl.LoadTopSitesAsync(_historyRepo, _faviconRepo);
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
}
