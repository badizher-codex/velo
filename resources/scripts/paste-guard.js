/**
 * VELO PasteGuard — detects unauthorized clipboard access attempts.
 *
 * Signals reported via window.__velo_pasteguard__(type):
 *   "paste-listener"   — page added a 'paste' event listener (passive monitor)
 *   "clipboard-read"   — page called navigator.clipboard.readText() or read()
 *   "execcommand-paste" — page called document.execCommand('paste') (legacy)
 *
 * Only unexpected/background reads are flagged (not user-initiated paste actions).
 */
(function () {
    'use strict';

    const report = (type) => {
        if (window.__velo_pasteguard__) {
            window.__velo_pasteguard__(type);
        }
    };

    // ── 1. Intercept navigator.clipboard.readText / read ────────────────────
    // v2.4.57 — Chromium 121+ ships navigator.clipboard.readText/read as
    // non-configurable native properties (anti-fingerprinting / -tampering
    // hardening). Object.defineProperty against them throws
    // "Cannot redefine property: readText", which kills the IIFE and leaves
    // sections 2 and 3 unwired. Worse, the uncaught throw bubbles into the
    // page and broke the Google OAuth flow on accounts.google.com (the
    // sign-in callback page stayed blank after "Continuar"). Each defineProperty
    // is now wrapped — if the native property is locked, we silently skip
    // clipboard-read detection on that page (the execCommand path and
    // paste-listener path still arm in sections 2 and 3 below).
    if (navigator.clipboard) {
        const origReadText = navigator.clipboard.readText?.bind(navigator.clipboard);
        const origRead     = navigator.clipboard.read?.bind(navigator.clipboard);

        if (origReadText) {
            try {
                Object.defineProperty(navigator.clipboard, 'readText', {
                    configurable: true,
                    get() {
                        return function () {
                            report('clipboard-read');
                            return origReadText();
                        };
                    }
                });
            } catch (_) { /* native non-configurable — see comment above */ }
        }

        if (origRead) {
            try {
                Object.defineProperty(navigator.clipboard, 'read', {
                    configurable: true,
                    get() {
                        return function () {
                            report('clipboard-read');
                            return origRead();
                        };
                    }
                });
            } catch (_) { /* native non-configurable — see comment above */ }
        }
    }

    // ── 2. Intercept document.execCommand('paste') ───────────────────────────
    const origExecCommand = document.execCommand.bind(document);
    document.execCommand = function (command, ...args) {
        if (typeof command === 'string' && command.toLowerCase() === 'paste') {
            report('execcommand-paste');
        }
        return origExecCommand(command, ...args);
    };

    // ── 3. Track 'paste' event listener additions ────────────────────────────
    const origAddEventListener = EventTarget.prototype.addEventListener;
    EventTarget.prototype.addEventListener = function (type, listener, options) {
        if (type === 'paste' && this === document) {
            report('paste-listener');
        }
        return origAddEventListener.call(this, type, listener, options);
    };
})();
