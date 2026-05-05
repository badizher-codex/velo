// VELO Browser — Autofill content script (Phase 3 / Sprint 5).
//
// Detects login forms, asks the host for matching credentials via the
// WebMessage bridge, and fills username/password on user choice. Also
// captures form submissions so the host can offer to save new entries.
//
// Wire protocol (all WebMessages are JSON):
//   page → host: { kind: 'autofill-detect', host, fields: { user, pass } }
//   page → host: { kind: 'autofill-submit', host, username, password }
//   host → page: { kind: 'autofill-fill', username, password }
//
// The host owns the dropdown UI (a WPF popup anchored to the WebView2);
// this script's job is purely DOM detection and value injection.

(function () {
  'use strict';

  if (window.__VELO_AUTOFILL_INSTALLED__) return;
  window.__VELO_AUTOFILL_INSTALLED__ = true;

  function host() {
    try { return location.hostname.toLowerCase(); } catch { return ''; }
  }

  function send(msg) {
    try { window.chrome?.webview?.postMessage(JSON.stringify(msg)); } catch { /* ignore */ }
  }

  // ── Field discovery ────────────────────────────────────────────────────

  function isVisible(el) {
    if (!el || el.disabled || el.readOnly) return false;
    const style = getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function findFields(root) {
    const inputs = Array.from((root || document).querySelectorAll('input'));
    const password = inputs.find(i => i.type === 'password' && isVisible(i));
    if (!password) return null;

    // Username = the visible text-like field most recently before the password.
    const candidates = inputs.filter(i =>
      i !== password &&
      isVisible(i) &&
      (i.type === 'text' || i.type === 'email' || i.type === 'tel' || i.type === ''));

    let username = null;
    for (const c of candidates) {
      const cmp = password.compareDocumentPosition(c);
      if (cmp & Node.DOCUMENT_POSITION_PRECEDING) username = c;
    }
    return { user: username, pass: password };
  }

  // ── Native value setter (React-friendly) ───────────────────────────────

  function setNativeValue(el, value) {
    const proto    = Object.getPrototypeOf(el);
    const setter   = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
    const baseSet  = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
    if (setter && setter !== baseSet) baseSet?.call(el, value);
    else el.value = value;
    el.dispatchEvent(new Event('input',  { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  // ── Public hooks (host → page) ─────────────────────────────────────────

  window.__veloAutofillFill = function (username, password) {
    const f = findFields();
    if (!f) return false;
    if (f.user && username) setNativeValue(f.user, username);
    if (f.pass)             setNativeValue(f.pass, password);
    return true;
  };

  // ── Detection trigger (page → host) ────────────────────────────────────

  let lastPing = 0;
  function pingHost() {
    const now = Date.now();
    if (now - lastPing < 750) return;
    lastPing = now;
    const f = findFields();
    if (!f) return;
    send({
      kind: 'autofill-detect',
      host: host(),
      fields: { user: !!f.user, pass: !!f.pass }
    });
  }

  // Run after initial render and on dynamic SPAs.
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', pingHost, { once: true });
  } else {
    pingHost();
  }

  const mo = new MutationObserver(() => pingHost());
  mo.observe(document.documentElement, { childList: true, subtree: true });

  // ── Submission capture ─────────────────────────────────────────────────

  function onSubmitLike(_ev) {
    const f = findFields();
    if (!f || !f.pass || !f.pass.value) return;
    send({
      kind:     'autofill-submit',
      host:     host(),
      username: f.user?.value ?? '',
      password: f.pass.value
    });
  }

  document.addEventListener('submit', onSubmitLike, true);

  // Many SPA logins don't fire a real submit — also catch button clicks
  // that look like a sign-in trigger.
  document.addEventListener('click', (ev) => {
    const t = ev.target;
    if (!(t instanceof Element)) return;
    const btn = t.closest('button, [role=button], input[type=submit]');
    if (!btn) return;
    const label = ((btn.textContent || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
    if (/sign[\s-]?in|log[\s-]?in|enter|continue|submit|acceder|iniciar/.test(label)) {
      // defer so the value is whatever the user typed, not stale
      setTimeout(onSubmitLike, 0);
    }
  }, true);
})();
