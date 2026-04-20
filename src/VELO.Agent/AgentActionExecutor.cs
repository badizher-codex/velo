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
                case AgentActionType.Respond:
                    // These produce output in the chat panel — no DOM action needed
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executor: error running [{Type}]", action.Type);
        }
    }
}
