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
    //
    // v0.3 (v2.4.69) — layer 1 rebuilt after a field bisect on 2026-08-03.
    //
    // v0.2's version of this layer stopped video playback outright (no start,
    // page unclickable) on both a regular video and a livestream replay, and
    // playback was fine the instant the script was absent. Disabling layer 1
    // alone restored it in one test, so the fault was here — but the bisect
    // only proved the layer, never which of its three hooks. Rather than guess,
    // v0.3 keeps the two hooks that can be written without side effects on
    // shared state and drops the one that cannot:
    //
    //   1a. ytInitialPlayerResponse property trap — KEPT. Touches one window
    //       property that only YouTube writes.
    //   1b. Global JSON.parse override — DROPPED. It hijacked a language
    //       builtin for every script on the page to catch SPA navigations;
    //       1c covers the same payloads at the network boundary, and no
    //       cosmetic feature justifies that blast radius.
    //   1c. fetch hook — KEPT, rewritten. v0.2 built a NEW Response around the
    //       re-serialised body while copying the original headers, so
    //       content-length and content-encoding described bytes that no longer
    //       existed. It now wraps .json() on the response instance instead, so
    //       the response object and its headers are never replaced.
    //
    // Trade-off accepted: a caller that reads the player payload with .text()
    // and parses it itself now bypasses pruning. The skip layer is the
    // fallback there, exactly as it is for server-stitched ads.
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

    // 1b. Dropped in v0.3 — see the note above. The global JSON.parse override
    // lived here.

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
                    // Wrap .json() on the instance. The response object, its
                    // body stream and its headers all stay exactly as the
                    // network delivered them — v0.2 returned a NEW Response
                    // carrying the original content-length and
                    // content-encoding over a body that had been re-serialised
                    // to a different size.
                    const nativeJson = response.json.bind(response);
                    response.json = async () => prune(await nativeJson());
                }
            } catch (_) { /* fail-soft: caller keeps the untouched response */ }
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

        // Remove the dialog that OWNS the modal, resolved from the modal
        // itself. The first version of this removed every
        // tp-yt-paper-dialog[opened] on the page whenever an enforcement modal
        // appeared, which is a much bigger hammer than the job needs — any
        // unrelated YouTube dialog open at that instant went with it.
        modals.forEach(el => {
            let target = el;
            try { target = el.closest('tp-yt-paper-dialog') || el; } catch (_) { /* fail-soft */ }
            try { target.remove(); } catch (_) { /* fail-soft */ }
        });

        // v2.4.69 — removing the dialog is not enough, and this is what the
        // field report looked like: video playing, no controls, the page
        // ignoring every click, and "the screen went slightly dark when I came
        // in". Polymer builds its overlays as a dialog PLUS a sibling
        // backdrop; tearing the dialog out of the DOM never closes the
        // overlay, so the backdrop is orphaned — a full-viewport scrim, high
        // z-index, dimming the page and swallowing every pointer event
        // forever. The CSS blocklist above hid the dialog and made it worse by
        // hiding the evidence.
        //
        // Polymer also locks scrolling on <body> while an overlay is open, so
        // that lock has to come off with it.
        document.querySelectorAll('tp-yt-iron-overlay-backdrop')
            .forEach(el => { try { el.remove(); } catch (_) {} });

        for (const el of [document.body, document.documentElement]) {
            if (!el) continue;
            try {
                el.style.removeProperty('overflow');
                el.classList.remove('no-scroll', 'iron-overlay-backdrop');
                el.removeAttribute('scroll-lock');
            } catch (_) { /* fail-soft */ }
        }

        const video = document.querySelector('video.html5-main-video');
        if (video && video.paused) {
            try { video.play(); } catch (_) { /* user gesture required, will retry on next mutation */ }
        }
    };

    // Orphaned-backdrop sweep. killEnforcement returns early once the modal is
    // gone, so a backdrop that Polymer attaches a tick later would survive
    // every subsequent pass — and one orphaned scrim makes the whole page
    // unclickable. This runs unconditionally but only fires when a backdrop
    // exists with NO dialog left to belong to, so YouTube's own share /
    // save-to-playlist dialogs keep their scrim and keep working.
    const killOrphanBackdrop = () => {
        const backdrops = document.querySelectorAll('tp-yt-iron-overlay-backdrop');
        if (backdrops.length === 0) return;

        const dialog = document.querySelector(
            'tp-yt-paper-dialog[opened], ytd-popup-container [aria-modal="true"], ' +
            'tp-yt-paper-dialog:not([aria-hidden="true"])');
        if (dialog && dialog.getBoundingClientRect().height > 0) return;   // a real dialog owns it

        backdrops.forEach(el => { try { el.remove(); } catch (_) {} });
        for (const el of [document.body, document.documentElement]) {
            if (!el) continue;
            try { el.style.removeProperty('overflow'); } catch (_) { /* fail-soft */ }
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
            killOrphanBackdrop();
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
        killOrphanBackdrop();
    }, 250);

    // First-run sweep — handles the case where the ad is already showing
    // by the time this script runs (e.g. re-injection on an SPA page).
    skipAds();
    killEnforcement();
    killOrphanBackdrop();
})();
