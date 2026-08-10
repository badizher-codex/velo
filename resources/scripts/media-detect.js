// VELO media detection — Phase 6 / P1.
//
// Read-only. This script never downloads, never buffers media, and never
// sends media bytes to the host: it reports STRUCTURE ONLY — MIME strings,
// container box names, append counts and byte totals.
//
// Why it exists (measured, see docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md §9):
// the network layer alone cannot identify media. YouTube delivers everything
// through one `videoplayback` URL whose only varying parameter is a request
// counter, answering `application/vnd.yt-ump`; HLS manifests answer
// `audio/mpegurl` for video streams; HLS segments answer
// `application/octet-stream`, same as fonts. What the page knows and the
// network does not is which tracks exist and what codec each carries.
//
// Gate 0.5 measures one specific claim: that the bytes handed to
// SourceBuffer.appendBuffer form a coherent, per-track container — an
// initialisation segment followed by media segments. If they do, media
// capture is possible at the MSE layer, generically, without touching any
// site's private delivery protocol. That claim is UNPROVEN until this runs.
//
// Injection: after webview-cloak.js (which stashes window.__veloBridge before
// deleting chrome.webview). Currently injected only when the media probe is
// enabled — hooks on the media path are exactly what broke playback in the
// YouTube ad-block v0.2, so this stays opt-in until it is proven inert.

(function () {
    if (window.__veloMediaDetectInstalled) return;
    window.__veloMediaDetectInstalled = true;

    // The payload MUST be stringified. The host reads it with
    // TryGetWebMessageAsString(), which throws ArgumentException for any
    // non-string message — and OnWebMessageReceived's try/catch swallows it,
    // so posting an object fails completely silently. autofill.js and
    // glance-hover.js (both proven in the field) stringify; council-bridge.js
    // does not, which is worth a look on its own.
    const post = (payload) => {
        try {
            const bridge = window.__veloBridge || (window.chrome && window.chrome.webview);
            if (bridge && typeof bridge.postMessage === 'function') {
                bridge.postMessage(JSON.stringify(payload));
            }
        } catch (_) { /* fail-soft */ }
    };

    // ── State ────────────────────────────────────────────────────────────
    const buffers = [];   // one entry per addSourceBuffer call
    const eme = { probed: [], resolved: [], setMediaKeys: 0, encryptedEvents: 0 };
    let dirty = false;

    // ── Container sniffing ───────────────────────────────────────────────
    // ISOBMFF is [4-byte big-endian size][4-char type]; walking the top level
    // is enough to tell an init segment (ftyp/moov) from a media segment
    // (styp/moof/mdat). Capped hard: this runs on every append of every
    // stream, so it must stay cheap and must never walk a whole segment.

    const asBytes = (data) => {
        try {
            if (!data) return null;
            if (data instanceof ArrayBuffer) return new Uint8Array(data, 0, Math.min(data.byteLength, 65536));
            if (ArrayBuffer.isView(data))
                return new Uint8Array(data.buffer, data.byteOffset, Math.min(data.byteLength, 65536));
        } catch (_) { }
        return null;
    };

    const topLevelBoxes = (u8) => {
        const boxes = [];
        let off = 0, guard = 0;
        while (off + 8 <= u8.length && guard++ < 16) {
            const size =
                ((u8[off] << 24) | (u8[off + 1] << 16) | (u8[off + 2] << 8) | u8[off + 3]) >>> 0;
            let type = '';
            for (let i = 4; i < 8; i++) type += String.fromCharCode(u8[off + i]);
            if (!/^[A-Za-z0-9 ]{4}$/.test(type)) break;
            boxes.push(type);
            if (size < 8) break;      // 0 or 1 mean "to EOF" / 64-bit — stop walking
            off += size;
        }
        return boxes;
    };

    // pssh and sinf live INSIDE moov, so the top-level walk steps over them.
    // A raw scan of the head of the buffer is enough as a presence signal —
    // this is a detector, not a parser.
    const scanFor = (u8, needle) => {
        const n = [];
        for (let i = 0; i < needle.length; i++) n.push(needle.charCodeAt(i));
        const cap = Math.min(u8.length, 65536) - n.length;
        outer:
        for (let i = 0; i < cap; i++) {
            for (let j = 0; j < n.length; j++) if (u8[i + j] !== n[j]) continue outer;
            return true;
        }
        return false;
    };

    const sniff = (u8) => {
        if (!u8 || u8.length < 8) return { container: 'unknown', boxes: [] };

        // WebM / Matroska: EBML magic 1A 45 DF A3.
        if (u8[0] === 0x1A && u8[1] === 0x45 && u8[2] === 0xDF && u8[3] === 0xA3)
            return { container: 'webm', boxes: ['EBML'] };
        // WebM media segments start at a Cluster (1F 43 B6 75).
        if (u8[0] === 0x1F && u8[1] === 0x43 && u8[2] === 0xB6 && u8[3] === 0x75)
            return { container: 'webm', boxes: ['Cluster'] };

        const boxes = topLevelBoxes(u8);
        if (boxes.length) {
            return {
                container: 'isobmff',
                boxes: boxes,
                pssh: scanFor(u8, 'pssh'),
                sinf: scanFor(u8, 'sinf'),
            };
        }
        return { container: 'unknown', boxes: [] };
    };

    // ── MSE hooks ────────────────────────────────────────────────────────
    try {
        if (window.MediaSource && window.MediaSource.prototype.addSourceBuffer) {
            const origAdd = window.MediaSource.prototype.addSourceBuffer;
            window.MediaSource.prototype.addSourceBuffer = function (mime) {
                const sb = origAdd.apply(this, arguments);
                try {
                    const entry = {
                        i: buffers.length,
                        mime: String(mime || ''),
                        appends: 0,
                        bytes: 0,
                        first: null,     // structure of the first append (init segment?)
                        later: {},       // box-signature -> count, for every later append
                        encrypted: false,
                    };
                    buffers.push(entry);
                    sb.__veloIndex = entry.i;
                    dirty = true;
                } catch (_) { }
                return sb;
            };
        }

        if (window.SourceBuffer && window.SourceBuffer.prototype.appendBuffer) {
            const origAppend = window.SourceBuffer.prototype.appendBuffer;
            window.SourceBuffer.prototype.appendBuffer = function (data) {
                // Inspection is wrapped separately from the call so a bug in
                // the sniffer can never stop the player from being fed.
                try {
                    const entry = buffers[this.__veloIndex];
                    if (entry) {
                        const len = (data && (data.byteLength || 0)) || 0;
                        entry.appends++;
                        entry.bytes += len;

                        const u8 = asBytes(data);
                        const s = u8 ? sniff(u8) : null;
                        if (s) {
                            if (s.pssh || s.sinf) entry.encrypted = true;
                            if (entry.first === null) {
                                entry.first = {
                                    container: s.container,
                                    boxes: s.boxes,
                                    bytes: len,
                                    pssh: !!s.pssh,
                                    sinf: !!s.sinf,
                                };
                            } else {
                                const sig = s.container + ':' + s.boxes.slice(0, 4).join('+');
                                entry.later[sig] = (entry.later[sig] || 0) + 1;
                            }
                        }
                        dirty = true;
                    }
                } catch (_) { }
                return origAppend.apply(this, arguments);
            };
        }
    } catch (_) { }

    // ── EME hooks ────────────────────────────────────────────────────────
    // P0's refinement: a page merely PROBING key systems is not protected
    // content — bitmovin probes 13 on load. What counts as use is a resolved
    // access that reaches setMediaKeys, or an `encrypted` event. All three
    // are recorded separately so the rule can be settled on data.
    try {
        if (navigator.requestMediaKeySystemAccess) {
            const origReq = navigator.requestMediaKeySystemAccess.bind(navigator);
            navigator.requestMediaKeySystemAccess = function (keySystem) {
                try {
                    if (eme.probed.indexOf(keySystem) < 0) eme.probed.push(String(keySystem));
                    dirty = true;
                } catch (_) { }
                return origReq.apply(null, arguments).then((access) => {
                    try {
                        if (eme.resolved.indexOf(keySystem) < 0) eme.resolved.push(String(keySystem));
                        dirty = true;
                    } catch (_) { }
                    return access;
                });
            };
        }

        if (window.HTMLMediaElement && HTMLMediaElement.prototype.setMediaKeys) {
            const origSet = HTMLMediaElement.prototype.setMediaKeys;
            HTMLMediaElement.prototype.setMediaKeys = function () {
                try { eme.setMediaKeys++; dirty = true; } catch (_) { }
                return origSet.apply(this, arguments);
            };
        }

        document.addEventListener('encrypted', () => {
            try { eme.encryptedEvents++; dirty = true; } catch (_) { }
        }, true);
    } catch (_) { }

    // ── Media elements ───────────────────────────────────────────────────
    const elements = () => {
        const out = [];
        try {
            document.querySelectorAll('video,audio').forEach((el) => {
                const src = el.currentSrc || el.src || '';
                out.push({
                    tag: el.tagName.toLowerCase(),
                    // The scheme is what matters: a blob: src means the real
                    // addresses exist only on the network layer (P0 §8).
                    srcKind: src.startsWith('blob:') ? 'blob'
                        : src.startsWith('http') ? 'http'
                            : src ? 'other' : 'none',
                    duration: isFinite(el.duration) ? Math.round(el.duration) : 0,
                });
            });
        } catch (_) { }
        return out;
    };

    // ── Reporting ────────────────────────────────────────────────────────
    // Throttled and change-gated: appendBuffer fires constantly during
    // playback and the bridge must not become the bottleneck.
    setInterval(() => {
        if (!dirty) return;
        dirty = false;
        post({
            kind: 'media-detect',
            url: location.href,
            buffers: buffers,
            eme: eme,
            elements: elements(),
        });
    }, 2000);
})();
