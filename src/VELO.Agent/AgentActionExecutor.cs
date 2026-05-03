using Microsoft.Extensions.Logging;
using VELO.Agent.Models;
using VELO.Core.Navigation;

namespace VELO.Agent;

/// <summary>
/// Executes approved AgentActions on the browser.
/// Wired to AgentActionSandbox.ActionApproved by the host (MainWindow/App).
///
/// Actions that require WebView2 DOM access (FillForm, ClickElement, ScrollTo)
/// delegate to BrowserTab via the PageScriptRequested event so the caller
/// can resolve the right tab without creating a coupling here.
/// </summary>
public class AgentActionExecutor(
    TabManager             tabManager,
    ILogger<AgentActionExecutor> logger)
{
    private readonly TabManager                  _tabManager = tabManager;
    private readonly ILogger<AgentActionExecutor> _logger    = logger;

    /// <summary>
    /// The host must hook this up to the active BrowserTab's ExecuteScriptAsync.
    /// Receives (tabId, javascript) and returns the script result.
    /// </summary>
    public Func<string, string, Task<string>>? ScriptExecutor { get; set; }

    /// <summary>
    /// v2.0.5.12 — The host wires this to AgentLauncher.SendAsync so that
    /// approved ReadPage/Summarize actions can re-prompt the LLM with the
    /// extracted page text. Without it those actions silently no-op.
    /// Signature: (tabId, prompt, pageText) → fire-and-forget; the resulting
    /// AgentLauncher.ResponseReady event displays the answer in the chat panel.
    /// </summary>
    public Action<string, string, string>? FollowUpPrompt { get; set; }

    public async Task ExecuteAsync(string tabId, AgentAction action)
    {
        _logger.LogInformation("Executor: [{Type}] for tab {TabId} — {Desc}",
            action.Type, tabId, action.Description);

        try
        {
            switch (action.Type)
            {
                case AgentActionType.OpenTab:
                    if (!string.IsNullOrEmpty(action.Url))
                    {
                        var tab = _tabManager.GetTab(tabId);
                        _tabManager.CreateTab(action.Url, tab?.ContainerId ?? "none");
                    }
                    break;

                case AgentActionType.Search:
                    // Value holds the explicit query; fall back to Text, then Description
                    var query = action.Value ?? action.Text ?? action.Description;
                    // Strip common prefixes the model adds to descriptions
                    query = System.Text.RegularExpressions.Regex.Replace(
                        query, @"^(buscar|search|búsqueda de|busca)\s+", "",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        var encoded = Uri.EscapeDataString(query);
                        var tab2    = _tabManager.GetTab(tabId);
                        _tabManager.CreateTab(
                            $"https://duckduckgo.com/?q={encoded}",
                            tab2?.ContainerId ?? "none");
                    }
                    break;

                case AgentActionType.CopyToClipboard:
                    if (!string.IsNullOrEmpty(action.Text))
                        System.Windows.Clipboard.SetText(action.Text);
                    break;

                case AgentActionType.FillForm:
                    if (ScriptExecutor != null && !string.IsNullOrEmpty(action.Selector))
                    {
                        var sel   = action.Selector.Replace("'", "\\'");
                        var val   = (action.Value ?? "").Replace("'", "\\'");
                        var js    = $"(function(){{var el=document.querySelector('{sel}');if(el){{el.value='{val}';el.dispatchEvent(new Event('input',{{bubbles:true}}));}}}})()";
                        await ScriptExecutor(tabId, js);
                    }
                    break;

                case AgentActionType.ClickElement:
                    if (ScriptExecutor != null && !string.IsNullOrEmpty(action.Selector))
                    {
                        var sel2 = action.Selector.Replace("'", "\\'");
                        var js2  = $"(function(){{var el=document.querySelector('{sel2}');if(el)el.click();}})()";
                        await ScriptExecutor(tabId, js2);
                    }
                    break;

                case AgentActionType.ScrollTo:
                    if (ScriptExecutor != null)
                    {
                        var js3 = string.IsNullOrEmpty(action.Selector)
                            ? "window.scrollBy(0, 400)"
                            : $"document.querySelector('{action.Selector.Replace("'", "\\'")}')?.scrollIntoView({{behavior:'smooth'}})";
                        await ScriptExecutor(tabId, js3);
                    }
                    break;

                case AgentActionType.ReadPage:
                case AgentActionType.Summarize:
                    // v2.0.5.12 — Extract page text via the script executor and
                    // re-prompt the LLM with it. Without this the action used to
                    // no-op silently after approval. The follow-up call is
                    // fire-and-forget; the host's ResponseReady handler appends
                    // the answer to the chat panel.
                    if (ScriptExecutor != null && FollowUpPrompt != null)
                    {
                        // innerText avoids dumping HTML/script. JSON.stringify
                        // makes WebView2's return path safe (script returns a
                        // quoted string we can deserialize trivially).
                        const string js =
                            "JSON.stringify((document.body && document.body.innerText) || '')";
                        var raw = await ScriptExecutor(tabId, js);
                        // raw arrives as a JSON-encoded JSON string (double-quoted),
                        // so peel one layer off.
                        var pageText = "";
                        try { pageText = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? ""; }
                        catch { pageText = raw; }
                        // Cap to a sane size so adapters don't blow up.
                        if (pageText.Length > 8000) pageText = pageText[..8000];

                        var promptKind = action.Type == AgentActionType.Summarize
                            ? "Summarise the page content below in 5 bullet points or fewer (TL;DR)."
                            : "Read the page content below and answer the user's previous question using only what is on this page.";
                        FollowUpPrompt(tabId, promptKind, pageText);
                    }
                    break;

                case AgentActionType.Respond:
                    // Pure conversational reply — already shown in the chat panel.
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executor: error running [{Type}]", action.Type);
        }
    }
}
