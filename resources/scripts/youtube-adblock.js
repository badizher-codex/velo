// VELO YouTube Ad-Block — v0.2 (v2.4.67)
//
// v0.1 (v2.4.53) had a fatal init bug plus a fragile design. The bug: the
// script runs at document-start, where document.head and documentElement
// are both null — v0.1's unguarded installStyle() threw on appendChild and
// the TypeError aborted the entire IIFE, so no layer ever armed (field
// report 2026-07-28: ads play to completion, skip button visible and
// unclicked). The fragility: everything was gated on cosmetic DOM markers
// (.ad-showing et al) that YouTube rotates at will.
//
// v0.2 stops depending on volatile class names for the video ads themselves.
// Layers, outermost first:
//
//   1. Response pruning (NEW, primary): strip adPlacements/adSlots/playerAds
//      from every player response before YouTube's own code sees it —
//      the property trap catches the inline ytInitialPlayerResponse, the
//      JSON.parse hook catches SPA navigations that parse text, and the
//      fetch hook catches /youtubei/v1/player calls whose native .json()
//      bypasses the page's JSON.parse. Ads that are never scheduled never
//      render; no flicker, no countdown.
//   2. Skip/fast-forward (kept, un-gated): the skip-button click no longer
//      requires the .ad-showing player gate — any visible skip button is
//      clicked, with a class-substring fallback for future renames.
//      Fast-forward stays gated on .ad-showing/.ad-interrupting because
//      seeking the MAIN video would be far worse than showing an ad.
//   3. CSS blocklist (kept, extended): static feed/sidebar/panel ad slots
//      + the watch-page companion units seen in the field report.
//   4. Anti-adblock modal neutraliser (kept as-is).
//
// Known gap, documented for v0.3: fully server-stitched ads (SSAP
// experiment) embed the ad in the same media stream; pruning removes their
// scheduling metadata in most observed variants, but if YouTube widens the
// experiment the skip layer is the only fallback.
//
// Host gate: the entire script no-ops on non-YouTube hosts via the IIFE
// guard below. VELO injects it via AddScriptToExecuteOnDocumentCreatedAsync
// only when the setting is on (see YouTubeAdBlocker.cs) — that timing is
// what lets the hooks in layer 1 install before any YouTube code runs.

(function () {
    if (!/(\.|^)youtube\.com$|(\.|^)youtu\.be$/.test(location.hostname)) return;
    if (window.__veloYTAdsApplied) return;
    window.__veloYTAdsApplied = true;

    // ── 1. Response pruning ───────────────────────────────────────────
    // Every hook is fail-soft: a throw returns the original object so a
    // pruning bug degrades to "ads show" — never to "video breaks".
    const AD_KEYS = ['adPlacements', 'adSlots', 'playerAds', 'adBreakHeartbeatParams'];
    const prune = (obj) => {
        try {
            if (obj && typeof obj === 'object' &&
                (obj.adPlacements || obj.adSlots || obj.playerAds)) {
                for (const k of AD_KEYS) delete obj[k];
            }
        } catch (_) { /* fail-soft */ }
        return obj;
    };

    // 1a. Inline ytInitialPlayerResponse — assigned by a <script> tag on
    // first load. The trap prunes at assignment time, before the player
    // module reads it. configurable so YouTube can redefine (fail-soft).
    try {
        let stored;
        Object.defineProperty(window, 'ytInitialPlayerResponse', {
            configurable: true,
            get: () => stored,
            set: (v) => { stored = prune(v); },
        });
    } catch (_) { /* fail-soft */ }

    // 1b. SPA navigations that JSON.parse the next watch-page payload.
    try {
        const nativeParse = JSON.parse.bind(JSON);
        JSON.parse = function (text, reviver) {
            return prune(nativeParse(text, reviver));
        };
    } catch (_) { /* fail-soft */ }

    // 1c. Innertube player calls — Response.json() is native and never
    // touches the page's JSON.parse, so the fetch boundary needs its own
    // hook. Scoped to /youtubei/v1/player to keep every other request
    // zero-overhead.
    try {
        const nativeFetch = window.fetch.bind(window);
        window.fetch = async function (input, init) {
            const response = await nativeFetch(input, init);
            try {
                const url = typeof input === 'string' ? input : (input && input.url) || '';
                if (url.includes('/youtubei/v1/player')) {
                    const data = prune(await response.clone().json());
                    return new Response(JSON.stringify(data), {
                        status: response.status,
                        statusText: response.statusText,
                        headers: response.headers,
                    });
                }
            } catch (_) { /* fail-soft: fall through to original response */ }
            return response;
        };
    } catch (_) { /* fail-soft */ }

    // ── 2. CSS blocklist ──────────────────────────────────────────────
    // Static-DOM ad slots: hidden via display:none !important so they
    // never render. The MutationObserver below catches anything YouTube
    // lazy-loads after the initial paint.
    const css = `
        /* Pre-roll / mid-roll overlay containers (in-player) */
        .ytp-ad-overlay-slot,
        .ytp-ad-overlay-container,
        .ytp-ad-overlay-image,
        .ytp-ad-image-overlay,
        .video-ads.ytp-ad-module,
        .ytp-ad-module,
        .ytp-ad-message-container,
        .ytp-ad-action-interstitial,
        .ytp-suggested-action,
        .ytp-featured-product,
        .iv-promoted-products,
        .ytp-ce-element,

        /* Sidebar of related videos */
        ytd-promoted-sparkles-web-renderer,
        ytd-promoted-video-renderer,
        ytd-display-ad-renderer,
        ytd-ad-slot-renderer,
        ytd-in-feed-ad-layout-renderer,
        ytd-banner-promo-renderer,
        ytd-statement-banner-renderer,
        ytd-compact-promoted-item-renderer,

        /* Watch-page companion / engagement-panel ad units */
        #player-ads,
        ytd-player-legacy-desktop-watch-ads-renderer,
        ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-ads"],
        ytd-ads-engagement-panel-content-renderer,
        ytd-companion-slot-renderer,
        ytd-action-companion-ad-renderer,
        yt-mealbar-promo-renderer,

        /* Home + search results */
        #masthead-ad,
        ytd-rich-item-renderer:has(ytd-display-ad-renderer),
        ytd-rich-item-renderer:has(ytd-ad-slot-renderer),
        ytd-rich-section-renderer:has(ytd-statement-banner-renderer),
        [is-promoted],
        [is-shorts-ad],

        /* Anti-adblock modal (YouTube's "ad blockers not allowed") */
        ytd-enforcement-message-view-model,
        tp-yt-paper-dialog:has(ytd-enforcement-message-view-model),
        .ytmusic-popup-container:has(ytd-enforcement-message-view-model)
        {
            display: none !important;
        }
    `;
    const installStyle = () => {
        // At document-start (AddScriptToExecuteOnDocumentCreatedAsync)
        // neither <head> nor documentElement exists yet. v0.1 called
        // appendChild on that null unguarded — the TypeError aborted the
        // whole IIFE, which is why every layer of v0.1 went dead at once.
        // Null-safe now; the rAF/interval loops below retry until it lands.
        const parent = document.head || document.documentElement;
        if (!parent || document.getElementById('velo-yt-adblock')) return;
        const style = document.createElement('style');
        style.id = 'velo-yt-adblock';
        style.textContent = css;
        parent.appendChild(style);
    };

    // ── 3. Skip / fast-forward ────────────────────────────────────────
    // The skip click is un-gated: if a skip button is visible anywhere,
    // click it. The substring selector survives the next class rename.
    // Fast-forward keeps the ad-marker gate — seeking is only safe when
    // we are POSITIVE the playing video is an ad.
    const skipAds = () => {
        const skipBtn = document.querySelector(
            '.ytp-skip-ad-button, .ytp-ad-skip-button, .ytp-ad-skip-button-modern, ' +
            'button[class*="skip-ad"], button[class*="-skip-button"]');
        if (skipBtn && skipBtn.offsetParent !== null) {
            try { skipBtn.click(); } catch (_) { /* fail-soft */ }
            return;
        }

        const player = document.querySelector(
            '.html5-video-player.ad-showing, .html5-video-player.ad-interrupting');
        if (!player) return;
        const video = player.querySelector('video');
        if (!video) return;

        // No skip button visible (either still in the grace window OR the
        // ad is unskippable). Fast-forward to the last 100 ms so the ad's
        // "ended" event fires and YouTube advances.
        if (video.duration && isFinite(video.duration) && video.duration > 0) {
            try {
                if (video.currentTime < video.duration - 0.2) {
                    video.currentTime = Math.max(0, video.duration - 0.1);
                }
            } catch (_) { /* fail-soft */ }
        }
    };

    // ── 4. Anti-adblock popup neutraliser ─────────────────────────────
    // YouTube periodically deploys a modal that detects ad blockers and
    // pauses the video. We remove the modal AND re-start the video if it
    // was auto-paused by the modal's appearance.
    const killEnforcement = () => {
        const modals = document.querySelectorAll('ytd-enforcement-message-view-model');
        if (modals.length === 0) return;
        modals.forEach(el => { try { el.remove(); } catch (_) {} });
        const video = document.querySelector('video.html5-main-video');
        if (video && video.paused) {
            try { video.play(); } catch (_) { /* user gesture required, will retry on next mutation */ }
        }
    };

    // Auto-pause defence: some anti-adblock paths capture the pause event
    // itself. Listen in capture phase so we run before YouTube's handler;
    // if the modal is present, swallow the pause and force play.
    document.addEventListener('pause', e => {
        if (document.querySelector('ytd-enforcement-message-view-model')) {
            e.stopImmediatePropagation();
            const v = e.target;
            if (v && typeof v.play === 'function') {
                try { v.play(); } catch (_) {}
            }
        }
    }, true);

    // ── 5. Wire observers + polling ───────────────────────────────────
    // MutationObserver watches the body subtree for class changes (the
    // ad-marker toggle on the player) and child additions (lazy-loaded
    // skip button / enforcement modal). The 250 ms interval is
    // belt-and-suspenders for YouTube's SPA transitions which sometimes
    // don't fire mutations on the player class.
    const wireObserver = () => {
        const root = document.body;
        if (!root) {
            requestAnimationFrame(wireObserver);
            return;
        }
        installStyle(); // re-install if YouTube nuked our style on nav
        const obs = new MutationObserver(() => {
            skipAds();
            killEnforcement();
        });
        obs.observe(root, {
            subtree: true,
            attributes: true,
            attributeFilter: ['class'],
            childList: true,
        });
    };

    installStyle();
    wireObserver();

    setInterval(() => {
        installStyle();
        skipAds();
        killEnforcement();
    }, 250);

    // First-run sweep — handles the case where the ad is already showing
    // by the time this script runs (e.g. re-injection on an SPA page).
    skipAds();
    killEnforcement();
})();
