using Microsoft.Extensions.Logging;

namespace VELO.Security.CookieWall;

/// <summary>
/// Coordinates cookie/consent wall bypass.
/// Strategy 1 (auto-dismiss) runs via injected cookie-bypass.js on every page.
/// Strategy 2 (DOM extraction) is triggered manually via ExtractContentAsync.
/// </summary>
public class CookieWallBypassEngine(ILogger<CookieWallBypassEngine> logger)
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// JS to force-dismiss any surviving consent overlay after navigation.
    /// Called from NavigationCompleted — runs at 0ms, 800ms and 2000ms.
    /// </summary>
    public const string ForceDismissScript = """
        (function(){
            // Re-inject CSS in case it was stripped
            if (!document.getElementById('velo-cmp-css')) {
                var s = document.createElement('style');
                s.id = 'velo-cmp-css';
                s.textContent = [
                    '#sp-message-container,[id^="sp_message_iframe"],.sp-message-container{display:none!important}',
                    '.tp-modal,.tp-backdrop,#tp-container,[id^="piano-"],[class^="piano-"]{display:none!important}',
                    '#onetrust-banner-sdk,#onetrust-consent-sdk,.onetrust-pc-dark-filter{display:none!important}',
                    '#CybotCookiebotDialog,#CybotCookiebotDialogBodyUnderlay{display:none!important}',
                    '#didomi-host,#didomi-popup,.qc-cmp2-container,.iubenda-cs-container{display:none!important}',
                    '[id*="cookie-banner"],[id*="consent-banner"],[id*="gdpr-banner"]{display:none!important}',
                    'body{overflow:auto!important;position:static!important}html{overflow:auto!important}'
                ].join('');
                document.head && document.head.appendChild(s);
            }
            // Try reject buttons
            var rejectTexts = ['rechazar todo','reject all','decline all','deny all','only necessary','solo necesarias'];
            document.querySelectorAll('button,[role="button"]').forEach(function(b){
                var t=(b.innerText||'').toLowerCase().trim();
                if(rejectTexts.some(function(r){return t.includes(r);})) b.click();
            });
            // Nuclear: remove large fixed elements
            var vw=window.innerWidth, vh=window.innerHeight, min=vw*vh*0.2;
            document.querySelectorAll('*').forEach(function(el){
                try{
                    var st=window.getComputedStyle(el);
                    if(st.position!=='fixed'&&st.position!=='sticky') return;
                    var r=el.getBoundingClientRect();
                    if(r.width*r.height<min) return;
                    var cls=(el.className||'').toString().toLowerCase()+' '+(el.id||'');
                    if(/cookie|consent|gdpr|cmp|cookiebot|onetrust|didomi|piano|sourcepoint/.test(cls)) el.remove();
                }catch(e){}
            });
            document.body.style.overflow='';
            document.documentElement.style.overflow='';
        })();
        """;

    public bool TryHandleDomExtracted(string json, out string html)
    {
        html = "";
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t) &&
                t.GetString() == "DOM_EXTRACTED" &&
                doc.RootElement.TryGetProperty("html", out var h))
            {
                html = h.GetString() ?? "";
                _logger.LogDebug("DOM extracted: {Chars} chars", html.Length);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DOM_EXTRACTED message");
        }
        return false;
    }
}
