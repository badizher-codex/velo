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
    if (navigator.clipboard) {
        const origReadText = navigator.clipboard.readText?.bind(navigator.clipboard);
        const origRead     = navigator.clipboard.read?.bind(navigator.clipboard);

        if (origReadText) {
            Object.defineProperty(navigator.clipboard, 'readText', {
                get() {
                    return function () {
                        report('clipboard-read');
                        return origReadText();
                    };
                }
            });
        }

        if (origRead) {
            Object.defineProperty(navigator.clipboard, 'read', {
                get() {
                    return function () {
                        report('clipboard-read');
                        return origRead();
                    };
                }
            });
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
