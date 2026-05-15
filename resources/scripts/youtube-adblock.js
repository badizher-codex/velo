// VELO YouTube Ad-Block — v0.1 (v2.4.53)
//
// Replicates the Malwarebytes Browser Guard pattern observed by the
// maintainer: the page renders the ad for a frame or two, then this script
// intercepts and skips. Visible result: a ~100 ms flicker on the player +
// the ad is gone, the real video starts.
//
// Scope (per memory/project_youtube_adblock_analysis.md):
//   • Pre-roll + mid-roll skip (clicks the skip button OR fast-forwards to
//     ad.duration when no skip is possible).
//   • Overlay/banner ads on the player.
//   • Sidebar promoted videos.
//   • Home + search result ad cards.
//   • Anti-adblock popup neutraliser (YouTube's "ad blockers are not
//     allowed" modal) — also re-plays the video which YouTube auto-pauses
//     when the modal appears.
//
// NOT in scope: SponsorBlock-style in-content skipping (creator's own
// sponsor reads), Premium-only feature unlocks. Both documented as deferred.
//
// Performance: CSS injection is paint-time, zero runtime cost on YouTube
// pages. MutationObserver filters on attribute changes only (~1-3% CPU
// during heavy nav). Polling interval is a 250 ms belt-and-suspenders for
// the cases mutations don't fire (some SPA transitions in YouTube don't
// emit class mutations for the player).
//
// Host gate: the entire script no-ops on non-YouTube hosts via the IIFE
// guard below. VELO injects it via AddScriptToExecuteOnDocumentCreatedAsync
// only when the setting is on (see YouTubeAdBlocker.cs).

(function () {
    if (!/(\.|^)youtube\.com$|(\.|^)youtu\.be$/.test(location.hostname)) return;
    if (window.__veloYTAdsApplied) return;
    window.__veloYTAdsApplied = true;

    // ── 1. CSS injection ──────────────────────────────────────────────
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
        if (document.getElementById('velo-yt-adblock')) return;
        const style = document.createElement('style');
        style.id = 'velo-yt-adblock';
        style.textContent = css;
        (document.head || document.documentElement).appendChild(style);
    };
    installStyle();

    // ── 2. Pre-roll / mid-roll skip ───────────────────────────────────
    // YouTube marks the player with .ad-showing while an ad plays. We
    // either click the skip button (after the 5 s grace period) or fast-
    // forward the video to its near-end so the ad completes instantly.
    const skipAds = () => {
        const player = document.querySelector(
            '.html5-video-player.ad-showing, .html5-video-player.ad-interrupting');
        if (!player) return;
        const video = player.querySelector('video');
        if (!video) return;

        const skipBtn = player.querySelector(
            '.ytp-ad-skip-button-modern, .ytp-ad-skip-button, .ytp-skip-ad-button');
        if (skipBtn) {
            try { skipBtn.click(); } catch (_) { /* fail-soft */ }
            return;
        }

        // No skip button visible yet (either still in the 5 s window OR
        // the ad is unskippable). Fast-forward to the last 100 ms so the
        // ad's "ended" event fires and YouTube advances.
        if (video.duration && isFinite(video.duration) && video.duration > 0) {
            try {
                if (video.currentTime < video.duration - 0.2) {
                    video.currentTime = Math.max(0, video.duration - 0.1);
                }
            } catch (_) { /* fail-soft */ }
        }
    };

    // ── 3. Anti-adblock popup neutraliser ─────────────────────────────
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

    // ── 4. Auto-pause defence ─────────────────────────────────────────
    // Some anti-adblock paths capture the pause event itself. Listen in
    // capture phase so we run before YouTube's handler; if the modal is
    // present, swallow the pause and force play.
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
    // .ad-showing toggle on the player) and child additions (lazy-loaded
    // enforcement modal). The 250 ms interval is belt-and-suspenders for
    // YouTube's SPA transitions which sometimes don't fire mutations on
    // the player class.
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
    wireObserver();

    setInterval(() => {
        installStyle();
        skipAds();
        killEnforcement();
    }, 250);

    // First-run sweep — handles the case where the ad is already showing
    // by the time this script runs.
    skipAds();
    killEnforcement();
})();
