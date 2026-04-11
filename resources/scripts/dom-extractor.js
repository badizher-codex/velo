// VELO — DOM Extractor (Cookie Wall Bypass Strategy 2)
// Runs after DOMContentLoaded if PaywallDetector triggers it
(function () {
    'use strict';

    const CONTENT_SELECTORS = [
        'article',
        '[role="main"]',
        '.article-body',
        '.post-content',
        '.entry-content',
        '#article-body',
        '.content-body',
        'main p'
    ];

    const OVERLAY_SELECTORS = [
        '.paywall', '.cookie-wall', '.consent-overlay',
        '[class*="paywall"]', '[class*="overlay"][class*="cookie"]',
        '[id*="paywall"]', '[id*="consent"]',
        '.subscription-wall', '.meter-wall', '.register-wall'
    ];

    function extractContent() {
        // Remove overlays first
        OVERLAY_SELECTORS.forEach(sel => {
            document.querySelectorAll(sel).forEach(el => el.remove());
        });

        // Restore scroll if body/html was locked
        document.body.style.overflow = '';
        document.documentElement.style.overflow = '';

        // Find main content
        for (const sel of CONTENT_SELECTORS) {
            const el = document.querySelector(sel);
            if (el && el.textContent.trim().length > 200) {
                return el.innerHTML;
            }
        }
        return null;
    }

    const content = extractContent();
    if (content) {
        // Send extracted content to C# host
        window.chrome.webview.postMessage(JSON.stringify({
            type: 'DOM_EXTRACTED',
            html: content
        }));
    }

})();
