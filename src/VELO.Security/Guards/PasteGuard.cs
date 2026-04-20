using Microsoft.Extensions.Logging;
using VELO.Core.Events;

namespace VELO.Security.Guards;

/// <summary>
/// Processes PasteGuard signals from the injected script and decides whether
/// to raise PasteGuardTriggeredEvent.
///
/// Signal types:
///   "paste-listener"    — passive; logged but not alerted unless repeated.
///   "clipboard-read"    — active read attempt; always alerted.
///   "execcommand-paste" — legacy read attempt; always alerted.
///
/// Deduplication: same signal from same domain within 10 seconds is ignored.
/// </summary>
public class PasteGuard(EventBus eventBus, ILogger<PasteGuard> logger)
{
    private readonly EventBus              _eventBus = eventBus;
    private readonly ILogger<PasteGuard>   _logger   = logger;

    // Dedup cache: key = "tabId|domain|signal", value = last alert time
    private readonly Dictionary<string, DateTime> _dedup = new();

    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(10);

    // Signal types that always trigger an alert
    private static readonly HashSet<string> ActiveSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "clipboard-read",
        "execcommand-paste",
    };

    public void Process(string tabId, string domain, string signalType)
    {
        var key = $"{tabId}|{domain}|{signalType}";

        // Dedup
        if (_dedup.TryGetValue(key, out var last) && DateTime.UtcNow - last < DedupWindow)
            return;
        _dedup[key] = DateTime.UtcNow;

        _logger.LogInformation("PasteGuard: [{Signal}] from {Domain} (tab {TabId})", signalType, domain, tabId);

        if (ActiveSignals.Contains(signalType))
        {
            _eventBus.Publish(new PasteGuardTriggeredEvent(tabId, domain, signalType));
        }
    }

    /// <summary>
    /// Returns the JavaScript that bridges page-side calls to the C# handler.
    /// This is added via AddHostObjectToScript — caller must wire up the callback.
    /// </summary>
    public static string BuildBridgeScript(string callbackJson) => """
        window.__velo_pasteguard__ = function(type) {
            window.chrome.webview.postMessage(JSON.stringify({
                kind: 'pasteguard',
                signal: type,
                ts: Date.now()
            }));
        };
        """;
}
