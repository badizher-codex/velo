// VELO WebView2 cloak — v0.2 (v2.4.68, F-1)
//
// Prime Video's Windows desktop app is built on WebView2. Its web app
// detects the WebView2 embedding, takes the "I am inside the Prime app"
// code path, and dies in VELO because the app's native bridge isn't
// there (HRESULT 0x80070490 in the app's RemoteMessenger machinery) —
// leaving the play button spinning forever.
//
// Field iterations 2026-07-28, in order:
//   1. Clean Sec-CH-UA (no "Microsoft Edge WebView2" brand) via DevTools
//      override, hostObjects present → still hangs.
//   2. v0.1 facade: chrome.webview kept but hostObjects hidden, headers
//      still announcing WebView2 → still hangs.
//   3. Both cleans at once (facade + DevTools UA override) → still hangs.
// Conclusion: the object's mere EXISTENCE is (part of) the probe. v0.2
// removes chrome.webview from the page entirely.
//
// VELO's own injected scripts still need the bridge (postMessage is the
// only member they use — verified by grep). The real object is stashed
// as a non-enumerable window.__veloBridge BEFORE deletion; every VELO
// script resolves `window.__veloBridge || window.chrome?.webview` so
// they work with or without the cloak applied.
//
// Fail-soft: any throw leaves the page with the real bridge — worst
// case is the pre-cloak behaviour.

(function () {
    try {
        var chrome = window.chrome;
        var real = chrome && chrome.webview;
        if (!real) return;

        Object.defineProperty(window, '__veloBridge', {
            value: real,
            configurable: false,
            enumerable: false,
            writable: false,
        });

        delete chrome.webview;
        if ('webview' in chrome) {
            // delete failed (non-configurable in some runtime version) —
            // best effort: mask the value instead.
            Object.defineProperty(chrome, 'webview', {
                value: undefined,
                configurable: true,
                enumerable: false,
            });
        }
    } catch (_) { /* fail-soft: page sees the real bridge */ }
})();
