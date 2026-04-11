// VELO — Cookie Wall Bypass
// Injected on every page. Auto-dismisses consent banners without accepting tracking.
(function () {
    'use strict';

    // ── CSS injection (runs before DOM is ready, most reliable) ───────────
    function injectCSS() {
        if (document.getElementById('velo-cmp-css')) return;
        const s = document.createElement('style');
        s.id = 'velo-cmp-css';
        s.textContent = `
            /* Sourcepoint (El Mundo, many ES news) */
            #sp-message-container, [id^="sp_message_iframe"], .sp-message-container { display:none!important; }
            /* Piano / TinyPass (La Vanguardia, El Confidencial) */
            .tp-modal, .tp-backdrop, #tp-container, [id^="piano-"], [class^="piano-"] { display:none!important; }
            /* OneTrust */
            #onetrust-banner-sdk, #onetrust-consent-sdk, .onetrust-pc-dark-filter { display:none!important; }
            /* Cookiebot */
            #CybotCookiebotDialog, #CybotCookiebotDialogBodyUnderlay { display:none!important; }
            /* Didomi */
            #didomi-host, #didomi-popup { display:none!important; }
            /* Quantcast */
            .qc-cmp2-container { display:none!important; }
            /* iubenda */
            .iubenda-cs-container { display:none!important; }
            /* Generic consent/cookie overlays */
            [id*="cookie-banner"], [id*="consent-banner"], [id*="gdpr-banner"],
            [id*="cookiebar"], [id*="cookie_notice"],
            [class*="cookie-banner"], [class*="consent-banner"], [class*="gdpr-banner"] { display:none!important; }
            /* Restore scroll always */
            body { overflow:auto!important; position:static!important; }
            html { overflow:auto!important; }
        `;
        // Try head first, fallback to documentElement
        (document.head || document.documentElement).appendChild(s);
    }

    // Inject CSS immediately (script runs before page scripts)
    injectCSS();
    // Re-inject after DOMContentLoaded in case head was replaced
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', injectCSS);
    }

    // ── Known framework selectors ─────────────────────────────────────────

    // Buttons to click (reject / necessary only) — ordered by specificity
    const REJECT_BUTTONS = [
        // OneTrust
        '#onetrust-reject-all-handler',
        '.onetrust-reject-all-handler',
        '#onetrust-accept-btn-handler ~ button',  // secondary = reject
        // Cookiebot
        '#CybotCookiebotDialogBodyButtonDecline',
        '#CybotCookiebotDialogBodyLevelButtonLevelOptinDeclineAll',
        // Didomi
        '#didomi-notice-disagree-button',
        '.didomi-continue-without-agreeing',
        // Quantcast
        '.qc-cmp2-summary-buttons button:last-of-type',
        // TrustArc
        '#truste-consent-required',
        '.truste_popframe .pdynamicbutton .required',
        // iubenda
        '.iubenda-cs-reject-btn',
        '.iubenda-reject-btn',
        // Usercentrics
        '[data-testid="uc-deny-all-button"]',
        // Osano
        '.osano-cm-deny',
        '.osano-cm-denyAll',
        // Axeptio
        '#axeptio_btn_dismiss',
        // Termly
        '#termly-code-snippet-support .t-declineAll',
        // Borlabs
        '#borlabs-cookie .borlabs-cookie-btn-reject',
        // Generic text-based — Spanish
        'button[id*="rechazar"]', 'button[class*="rechazar"]',
        'button[id*="denegar"]',  'button[class*="denegar"]',
        'button[id*="reject"]',   'button[class*="reject"]',
        'button[id*="decline"]',  'button[class*="decline"]',
        'button[id*="deny"]',     'button[class*="deny"]',
        'button[id*="necessary"]','button[class*="necessary"]',
        'button[id*="essential"]','button[class*="essential"]',
        'a[id*="reject"]',        'a[class*="reject"]',
    ];

    // Text patterns to match buttons by content (case-insensitive)
    // Full match first (more specific), partial match fallback
    const REJECT_TEXT_EXACT = [
        'rechazar todo', 'rechazar todas', 'solo necesarias', 'solo esenciales',
        'reject all', 'decline all', 'deny all', 'refuse all',
        'only necessary', 'only essential', 'necessary only', 'essential only',
        'use necessary only', 'accept necessary', 'save settings',
        'continue without accepting', 'continuar sin aceptar',
        'no, gracias', 'no thanks', 'skip',
    ];

    // Partial match — only if no "suscrib" (avoid "Rechazar y suscribirse")
    const REJECT_TEXT_PARTIAL = [
        'rechazar', 'reject', 'decline', 'deny', 'refuse',
        'solo necesari', 'only necessar',
    ];

    // Words that disqualify a partial match
    const REJECT_DISQUALIFY = ['suscrib', 'subscri', 'premium', 'comprar', 'pay', 'buy'];

    // Overlays to remove if no button found
    const OVERLAY_SELECTORS = [
        '#onetrust-banner-sdk', '#onetrust-consent-sdk',
        '#CybotCookiebotDialog', '.cookiebot-overlay',
        '#didomi-host', '.didomi-popup',
        '.qc-cmp2-container', '.qc-cmp-showing',
        '#truste-consent-track', '#truste-consent-content',
        '.iubenda-cs-container',
        '.cc-window', '.cc-banner', '.cc-overlay',
        '[id*="cookie-banner"]', '[class*="cookie-banner"]',
        '[id*="cookie-consent"]', '[class*="cookie-consent"]',
        '[id*="gdpr-banner"]',   '[class*="gdpr-banner"]',
        '[id*="consent-banner"]','[class*="consent-banner"]',
        '[id*="cookie-wall"]',   '[class*="cookie-wall"]',
        '[id*="cookiebar"]',     '[class*="cookiebar"]',
        '[id*="cookie_notice"]', '[class*="cookie_notice"]',
        '.cookie-overlay', '.consent-overlay', '.privacy-overlay',
    ];

    // ── Helpers ───────────────────────────────────────────────────────────

    function textMatches(el) {
        const t = (el.innerText || el.textContent || '').toLowerCase().trim();
        if (REJECT_TEXT_EXACT.some(p => t === p || t.startsWith(p))) return true;
        const disqualified = REJECT_DISQUALIFY.some(d => t.includes(d));
        if (disqualified) return false;
        return REJECT_TEXT_PARTIAL.some(p => t.includes(p));
    }

    // Nuclear fallback: remove large fixed overlays that cover the viewport
    function removeFixedOverlays() {
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const minArea = vw * vh * 0.20; // covers >20% of screen

        document.querySelectorAll('*').forEach(el => {
            try {
                const style = window.getComputedStyle(el);
                const pos = style.position;
                if (pos !== 'fixed' && pos !== 'sticky') return;
                const r = el.getBoundingClientRect();
                const area = r.width * r.height;
                if (area < minArea) return;
                // Remove if: high z-index, OR class/id contains consent keywords
                const z = parseInt(style.zIndex) || 0;
                const cls = (el.className || '').toString().toLowerCase();
                const id  = (el.id || '').toLowerCase();
                const tag = cls + ' ' + id;
                const hasKeyword = /cookie|consent|gdpr|cookiebot|onetrust|didomi|piano|sourcepoint|cmp/.test(tag);
                if (hasKeyword) el.remove();
            } catch { }
        });

        // Remove backdrop/dimmer elements regardless of size
        document.querySelectorAll(
            '[class*="backdrop"],[class*="dimmer"],[id*="backdrop"],' +
            '[class*="modal-overlay"],[class*="consent"],[id*="consent"],' +
            '[class*="cmp"],[id*="cmp"],[class*="gdpr"],[id*="gdpr"]'
        ).forEach(el => {
            try {
                const pos = window.getComputedStyle(el).position;
                if (pos === 'fixed' || pos === 'absolute') el.remove();
            } catch { }
        });

        // Always restore scroll
        document.body.style.overflow = '';
        document.body.style.position = '';
        document.documentElement.style.overflow = '';
    }

    function tryClickReject() {
        // 1. Known selector buttons
        for (const sel of REJECT_BUTTONS) {
            try {
                const btn = document.querySelector(sel);
                if (btn && btn.offsetParent !== null) {
                    btn.click();
                    return true;
                }
            } catch { /* invalid selector, skip */ }
        }

        // 2. Text-match scan over all visible buttons and links
        const candidates = [
            ...document.querySelectorAll('button, a[role="button"], [role="button"]')
        ];
        for (const el of candidates) {
            if (el.offsetParent !== null && textMatches(el)) {
                el.click();
                return true;
            }
        }

        return false;
    }

    function removeOverlays() {
        let removed = false;
        for (const sel of OVERLAY_SELECTORS) {
            try {
                document.querySelectorAll(sel).forEach(el => {
                    el.remove();
                    removed = true;
                });
            } catch { }
        }
        // Restore body scroll (common trick to prevent scrolling behind overlay)
        if (removed) {
            document.body.style.overflow = '';
            document.body.style.position = '';
            document.documentElement.style.overflow = '';
        }
        return removed;
    }

    // ── Main ──────────────────────────────────────────────────────────────

    function bypass() {
        if (tryClickReject()) return;  // button clicked → framework will close itself
        if (removeOverlays()) return;  // known selector matched
        removeFixedOverlays();         // nuclear: remove large fixed elements
    }

    // Run immediately
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bypass);
    } else {
        bypass();
    }

    // Run again after full load — multiple passes for lazy banners
    window.addEventListener('load', () => {
        bypass();
        setTimeout(bypass, 500);
        setTimeout(bypass, 1500);
        setTimeout(bypass, 3500);
    });

    // MutationObserver: fire bypass on any DOM change (debounced 200ms)
    let _debounce = null;
    const observer = new MutationObserver(() => {
        clearTimeout(_debounce);
        _debounce = setTimeout(bypass, 200);
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });

})();
