// VELO Council Mode — page-side bridge.
// Phase 4.1 chunk C — exposes a tiny imperative API on window.__veloCouncil
// that the host (C#) drives via ExecuteScriptAsync, and posts capture events
// back to the host via chrome.webview.postMessage.
//
// Design notes:
//   • Adapter-driven: this script does not hard-code selectors for any
//     particular provider. The host calls __veloCouncil.setAdapter(json)
//     after the page loads, passing the JSON loaded by CouncilAdaptersRegistry
//     (Phase 4.1 chunk D). Provider-specific quirks (composer being a
//     contenteditable in Claude vs textarea in others, multiple send-button
//     candidates, response-container selectors) live in those JSON files.
//   • One injection per document — guarded by window.__veloCouncilInstalled
//     so multiple AddScriptToExecuteOnDocumentCreated invocations don't
//     redefine the namespace mid-session.
//   • Side-effect-free at load time: defines the API, does not start
//     observers until setAdapter() runs. Pages that aren't Council panels
//     pay nothing.
//   • Fail-soft: every API method swallows errors and returns a sensible
//     empty/false value. Council UX should never crash a webview.
//
// Outbound message shapes (chrome.webview.postMessage payloads):
//   { type: 'council/capture',
//     captureType: 'text'|'code'|'table'|'citation',
//     content: '<string>',
//     sourceUrl: '<string>' }
//   { type: 'council/replyDetected',
//     text: '<latest assistant reply text>',
//     sourceUrl: '<string>' }
//   { type: 'council/error',
//     message: '<diagnostic>' }
//
// Inbound API surface (host → page via ExecuteScriptAsync):
//   window.__veloCouncil.setAdapter(jsonString) — install selectors.
//   window.__veloCouncil.paste(text)            — fill the composer.
//   window.__veloCouncil.send()                 — click the send button.
//   window.__veloCouncil.captureText()          — return latest reply text.
//   window.__veloCouncil.captureCode()          — return concatenated code blocks.
//   window.__veloCouncil.captureTable()         — return first table as markdown-ish text.
//   window.__veloCouncil.captureCitation()      — return citations as JSON string.

(function () {
    if (window.__veloCouncilInstalled) return;
    window.__veloCouncilInstalled = true;

    const post = (payload) => {
        try {
            if (window.chrome && window.chrome.webview &&
                typeof window.chrome.webview.postMessage === 'function') {
                window.chrome.webview.postMessage(payload);
            }
        } catch (_) { /* fail-soft */ }
    };

    const safeText = (el) => {
        if (!el) return '';
        return ((el.innerText || el.textContent || '') + '').trim();
    };

    const lastMatch = (selector) => {
        try {
            const nodes = document.querySelectorAll(selector);
            return nodes.length > 0 ? nodes[nodes.length - 1] : null;
        } catch (_) { return null; }
    };

    const api = {
        adapter: null,
        _observer: null,
        _debounce: null,
        _lastReplyText: '',

        // Host installs the per-provider selectors after navigation completes.
        // jsonString may also be an already-parsed object (test helper).
        setAdapter(jsonOrObject) {
            try {
                const adapter = (typeof jsonOrObject === 'string')
                    ? JSON.parse(jsonOrObject)
                    : jsonOrObject;
                this.adapter = adapter || null;
                this._installObserver();
                return true;
            } catch (e) {
                post({ type: 'council/error', message: 'setAdapter parse failed: ' + (e && e.message) });
                return false;
            }
        },

        // Drop text into the composer, simulating an input event so the
        // provider's UI (which usually disables Send until the textarea has
        // content) updates the button state.
        paste(text) {
            if (!this.adapter || !this.adapter.composer) return false;
            try {
                const composer = document.querySelector(this.adapter.composer);
                if (!composer) return false;
                composer.focus();
                if (composer.tagName === 'TEXTAREA' || composer.tagName === 'INPUT') {
                    composer.value = text == null ? '' : String(text);
                } else {
                    // contentEditable path (Claude uses ProseMirror).
                    composer.textContent = text == null ? '' : String(text);
                }
                composer.dispatchEvent(new Event('input', { bubbles: true }));
                composer.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
            } catch (_) { return false; }
        },

        // Click the send button. Adapter may declare multiple candidates
        // (comma-separated CSS) — first match wins.
        send() {
            if (!this.adapter || !this.adapter.sendButton) return false;
            try {
                const btn = document.querySelector(this.adapter.sendButton);
                if (!btn) return false;
                if (btn.disabled) return false;
                btn.click();
                return true;
            } catch (_) { return false; }
        },

        captureText() {
            if (!this.adapter || !this.adapter.responseContainer) return '';
            const latest = lastMatch(this.adapter.responseContainer);
            return safeText(latest);
        },

        captureCode() {
            if (!this.adapter || !this.adapter.responseContainer) return '';
            const latest = lastMatch(this.adapter.responseContainer);
            if (!latest) return '';
            const codeSel = this.adapter.codeBlock || 'pre code, pre';
            try {
                const blocks = latest.querySelectorAll(codeSel);
                if (!blocks || blocks.length === 0) return '';
                return Array.from(blocks).map(b => safeText(b)).filter(s => s).join('\n\n');
            } catch (_) { return ''; }
        },

        captureTable() {
            if (!this.adapter || !this.adapter.responseContainer) return '';
            const latest = lastMatch(this.adapter.responseContainer);
            if (!latest) return '';
            const tableSel = this.adapter.table || 'table';
            try {
                const tbl = latest.querySelector(tableSel);
                if (!tbl) return '';
                // Tiny markdown-table flattening — not full GFM, but enough
                // to keep cell alignment and row order legible in synthesis.
                const rows = Array.from(tbl.querySelectorAll('tr'));
                if (rows.length === 0) return '';
                const lines = rows.map(r =>
                    Array.from(r.querySelectorAll('th, td'))
                        .map(c => safeText(c).replace(/\|/g, '\\|'))
                        .join(' | '));
                return lines.join('\n');
            } catch (_) { return ''; }
        },

        // Collect citation-like links from the latest response. Returns a
        // JSON-encoded array string so the host's incoming-message parser can
        // hand the payload to JsonDocument verbatim.
        captureCitation() {
            if (!this.adapter || !this.adapter.responseContainer) return '[]';
            const latest = lastMatch(this.adapter.responseContainer);
            if (!latest) return '[]';
            const linkSel = this.adapter.citation || 'a[href]';
            try {
                const links = Array.from(latest.querySelectorAll(linkSel));
                const items = links.slice(0, 50).map(a => ({
                    text: safeText(a),
                    url:  a.href || '',
                }));
                return JSON.stringify(items);
            } catch (_) { return '[]'; }
        },

        // ── Observation: post a replyDetected message once the latest
        // assistant response stops mutating for 1.5 s (stream complete).
        // The host uses this to mark the panel's reply ready for synthesis
        // and to refresh the per-panel "captured" badge.
        _installObserver() {
            try {
                if (this._observer) {
                    this._observer.disconnect();
                    this._observer = null;
                }
                if (!this.adapter || !this.adapter.responseContainer) return;
                const root = document.body;
                if (!root) {
                    requestAnimationFrame(() => this._installObserver());
                    return;
                }

                const flush = () => {
                    const text = this.captureText();
                    if (text && text !== this._lastReplyText) {
                        this._lastReplyText = text;
                        post({
                            type: 'council/replyDetected',
                            text,
                            sourceUrl: location.href,
                        });
                    }
                };

                const onMutation = () => {
                    if (this._debounce) clearTimeout(this._debounce);
                    this._debounce = setTimeout(flush, 1500);
                };

                this._observer = new MutationObserver(onMutation);
                this._observer.observe(root, {
                    subtree: true,
                    childList: true,
                    characterData: true,
                });
            } catch (_) { /* observer install failed — captures still work via host poll */ }
        },
    };

    window.__veloCouncil = api;
})();
