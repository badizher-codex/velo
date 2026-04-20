(function () {
    'use strict';

    // Only run once per page
    if (window.__velo_glance_loaded__) return;
    window.__velo_glance_loaded__ = true;

    const DELAY_MS  = 600;   // hover must be held this long before preview fires
    const HIDE_MS   = 150;   // grace period before hiding (prevents flicker on micro-moves)

    let showTimer = null;
    let hideTimer = null;

    function anchorFor(target) {
        let el = target;
        while (el && el.tagName !== 'A') el = el.parentElement;
        return el;
    }

    function send(kind, url) {
        try {
            window.chrome.webview.postMessage(JSON.stringify({ kind, url }));
        } catch (_) {}
    }

    document.addEventListener('mouseover', function (e) {
        const a = anchorFor(e.target);
        if (!a) return;

        const href = a.href;
        if (!href || !href.startsWith('http')) return;

        clearTimeout(hideTimer);
        clearTimeout(showTimer);

        showTimer = setTimeout(function () {
            send('glance-show', href);
        }, DELAY_MS);
    }, true);

    document.addEventListener('mouseout', function (e) {
        const a = anchorFor(e.target);
        if (!a) return;

        clearTimeout(showTimer);
        hideTimer = setTimeout(function () {
            send('glance-hide', '');
        }, HIDE_MS);
    }, true);
})();
