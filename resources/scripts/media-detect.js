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

    // blob: URL -> MediaSource, so a SourceBuffer can be traced back to the
    // element actually playing it. YouTube builds a NEW MediaSource on quality
    // switches, ads and autoplay-next, and every one of them adds its own
    // SourceBuffers — without this the panel accumulated ten stale pairs of
    // dead tracks, most reading 0 B, and reported "20 found" on one video.
    const sourceUrls = new WeakMap();
    try {
        const origCreate = URL.createObjectURL;
        URL.createObjectURL = function (obj) {
            const url = origCreate.apply(URL, arguments);
            try {
                if (window.MediaSource && obj instanceof window.MediaSource) sourceUrls.set(obj, url);
            } catch (_) { }
            return url;
        };
    } catch (_) { }

    // The MediaSource currently attached to a media element, by blob URL.
    const liveSourceUrl = () => {
        try {
            const els = document.querySelectorAll('video,audio');
            for (let i = 0; i < els.length; i++) {
                const src = els[i].currentSrc || els[i].src || '';
                if (src.startsWith('blob:')) return src;
            }
        } catch (_) { }
        return '';
    };

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

    // `deep` controls the pssh/sinf scan, which is O(n·m) over the head of the
    // buffer. It runs ONLY on the first append of each SourceBuffer, because
    // that is the initialisation segment and that is where encryption boxes
    // live — the measured ones were 259 to 729 bytes. Running it on every
    // append (as the gated version did) burned ~21 M byte comparisons per
    // track per 40 s of YouTube for information that cannot change, on the
    // media hot path. That is the kind of cost that turns a detector into a
    // playback bug, which is what the YouTube ad-block v0.2 was.
    const sniff = (u8, deep) => {
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
                pssh: deep ? scanFor(u8, 'pssh') : false,
                sinf: deep ? scanFor(u8, 'sinf') : false,
            };
        }
        return { container: 'unknown', boxes: [] };
    };

    // ── P2b — capture sink ───────────────────────────────────────────────
    //
    // Armed by the host prepending window.__VELO_CAPTURE__ = {id, kind}
    // before this script, so the arming survives the reload the flow depends
    // on: capture can only ever get what is appended from now on, and the
    // start of a video that is already playing is gone — the player will not
    // re-append data it already holds. A fresh document builds a new
    // MediaSource and appends from byte zero, which is the only way to get
    // the whole thing.
    //
    // Armed by KIND, not by index: SourceBuffer indices are assigned as the
    // page creates them, so they do not exist yet at arming time.
    const capture = (window.__VELO_CAPTURE__ && window.__VELO_CAPTURE__.id)
        ? { id: String(window.__VELO_CAPTURE__.id),
            kind: String(window.__VELO_CAPTURE__.kind || 'video'),
            target: null, seq: 0, done: false }
        : null;

    // 256 KB pieces — the size Gate P2b-0 benchmarked at 91-122 MB/s. An
    // append can be several MB and one giant base64 string per append would
    // be a needlessly large single message.
    const CHUNK = 262144;

    const toBase64 = (u8) => {
        let binary = '';
        for (let i = 0; i < u8.length; i += 8192) {
            binary += String.fromCharCode.apply(null, u8.subarray(i, Math.min(i + 8192, u8.length)));
        }
        return btoa(binary);
    };

    const captureBytes = (data) => {
        if (!capture || capture.done) return;
        try {
            let u8 = null;
            if (data instanceof ArrayBuffer) u8 = new Uint8Array(data);
            else if (ArrayBuffer.isView(data)) u8 = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
            if (!u8 || !u8.length) return;

            // Streamed straight out, never accumulated: §10 measured 91 MB in
            // 40 s on one track, and holding that in the page is the one thing
            // the design cannot do.
            for (let off = 0; off < u8.length; off += CHUNK) {
                post({
                    kind: 'media-capture', phase: 'chunk', id: capture.id,
                    seq: capture.seq++,
                    data: toBase64(u8.subarray(off, Math.min(off + CHUNK, u8.length))),
                });
            }
        } catch (e) {
            capture.done = true;
            post({ kind: 'media-capture', phase: 'error', id: capture.id, message: String(e) });
        }
    };

    const endCapture = (reason) => {
        if (!capture || capture.done) return;
        capture.done = true;
        post({ kind: 'media-capture', phase: 'end', id: capture.id, chunks: capture.seq, reason: reason || 'ended' });
    };

    // The host stops a capture through this; the page never decides on its
    // own except when playback genuinely ends.
    window.__veloStopCapture = () => { endCapture('stopped'); };

    try {
        document.addEventListener('ended', () => endCapture('ended'), true);
    } catch (_) { }

    // ── Playback-rate probe (TEMPORARY) ──────────────────────────────────
    //
    // Capture is real-time by construction: the bytes only exist as the player
    // fetches and appends them, so a 40-minute video takes 40 minutes. If a
    // player honours a high playbackRate it will fetch and append faster, and
    // the capture finishes in a fraction of the time. Whether YouTube's player
    // honours it or fights it is the question — measured before it becomes a
    // feature. Re-applied on a timer because players reset the rate.
    try {
        const wanted = Number(window.__VELO_RATE__ || 0);
        if (wanted > 0) {
            setInterval(() => {
                try {
                    document.querySelectorAll('video,audio').forEach((el) => {
                        if (el.playbackRate !== wanted) el.playbackRate = wanted;
                        el.muted = true;
                    });
                } catch (_) { }
            }, 1000);
        }
    } catch (_) { }

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
                        // The blob: URL of the MediaSource this belongs to.
                        // Reporting filters on it so a replaced MediaSource's
                        // buffers stop being offered as if they were live.
                        srcUrl: sourceUrls.get(this) || '',
                    };
                    buffers.push(entry);
                    // Bounded: a long session on an autoplay playlist would
                    // otherwise grow this array without limit.
                    if (buffers.length > 40) buffers.splice(0, buffers.length - 40);
                    // The entry itself, not an index into the array — trimming
                    // above shifts indices and would silently rebind old
                    // SourceBuffers to the wrong entry.
                    sb.__veloEntry = entry;

                    // Bind the capture to the first SourceBuffer of the armed
                    // kind. Binding here rather than at append time means the
                    // very first append — the initialisation segment, which
                    // every fMP4 file must lead with — is captured too.
                    if (capture && !capture.target &&
                        entry.mime.toLowerCase().indexOf(capture.kind + '/') === 0) {
                        capture.target = sb;
                        post({
                            kind: 'media-capture', phase: 'begin', id: capture.id,
                            trackKind: capture.kind, mime: entry.mime,
                        });
                    }

                    dirty = true;
                } catch (_) { }
                return sb;
            };
        }

        if (window.SourceBuffer && window.SourceBuffer.prototype.appendBuffer) {
            const origAppend = window.SourceBuffer.prototype.appendBuffer;
            window.SourceBuffer.prototype.appendBuffer = function (data) {
                // Capture first and in its own try: these are the bytes, and
                // a failure here must neither corrupt the file silently nor
                // stop the player being fed.
                try {
                    if (capture && capture.target === this) captureBytes(data);
                } catch (_) { }

                // Inspection is wrapped separately from the call so a bug in
                // the sniffer can never stop the player from being fed.
                try {
                    const entry = this.__veloEntry;
                    if (entry) {
                        const len = (data && (data.byteLength || 0)) || 0;
                        entry.appends++;
                        entry.bytes += len;

                        const isInitSegment = entry.first === null;
                        const u8 = asBytes(data);
                        const s = u8 ? sniff(u8, isInitSegment) : null;
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
    // Only the tracks of the MediaSource currently attached to a media
    // element. Everything else is a corpse: YouTube replaces the MediaSource
    // on quality switches, ads and autoplay-next, and reporting all of them
    // turned one video into "20 found" — ten pairs of dead tracks, most
    // reading 0 B, each offering itself as if it were the thing on screen.
    //
    // Falls back to the newest pair when nothing can be matched (no element
    // yet, or a browser that does not expose currentSrc as the blob URL), so
    // a failure to match degrades to "slightly stale" rather than "empty".
    const liveBuffers = () => {
        const live = liveSourceUrl();
        const matched = live ? buffers.filter(b => b.srcUrl === live) : [];
        if (matched.length) return matched;

        const withUrls = buffers.filter(b => b.srcUrl);
        if (!withUrls.length) return buffers.slice(-4);

        const newest = withUrls[withUrls.length - 1].srcUrl;
        return buffers.filter(b => b.srcUrl === newest);
    };

    // ── Gate P2b-0 — bridge throughput bench (TEMPORARY) ─────────────────
    //
    // Runs only when the host prepends window.__VELO_BENCH__, the same shape
    // webrtc-spoof.js uses for its mode constant. Delete this block and the
    // prepend in BrowserTab once §18 records the numbers.
    //
    // The question it answers: can postMessage carry media-rate data? A
    // capture sink for MSE would need ~3 MB/s (measured: 91 MB in 40 s on one
    // track), postMessage takes strings only, so bytes need base64 (+33 %),
    // and every message is parsed on the UI thread. Encode cost and drain
    // rate are reported separately, because if the bottleneck is the encoding
    // the fix is a different encoding, and if it is the bridge the fix is a
    // different channel.
    try {
        if (window.__VELO_BENCH__) {
            const cfg = window.__VELO_BENCH__;
            const size = cfg.bytes | 0, count = cfg.chunks | 0;

            setTimeout(() => {
                // Only the visible tab. The host-side accumulator is a static,
                // so several restored tabs running this at once interleave
                // their counts — the first run reported 367 and 400 chunks for
                // a 200-chunk config, which is two benches in one bucket.
                if (document.visibilityState !== 'visible') return;

                // Build the payload ONCE so the loop measures the bridge, not
                // the generator.
                const t0 = performance.now();
                const raw = new Uint8Array(size);
                for (let i = 0; i < size; i++) raw[i] = i & 0xff;
                let binary = '';
                for (let i = 0; i < size; i += 8192) {
                    binary += String.fromCharCode.apply(null, raw.subarray(i, Math.min(i + 8192, size)));
                }
                const payload = btoa(binary);
                const encodeMs = performance.now() - t0;

                post({ kind: 'bridge-bench', phase: 'start', bytes: size, chunks: count, encodeMs: encodeMs });

                const t1 = performance.now();
                for (let n = 0; n < count; n++) {
                    post({ kind: 'bridge-bench', phase: 'chunk', n: n, data: payload });
                }
                const postMs = performance.now() - t1;

                post({ kind: 'bridge-bench', phase: 'end', postMs: postMs, encodeMs: encodeMs });
            }, cfg.delayMs || 8000);
        }
    } catch (_) { }

    setInterval(() => {
        if (!dirty) return;
        dirty = false;
        post({
            kind: 'media-detect',
            url: location.href,
            buffers: liveBuffers().map(b => ({
                i: b.i, mime: b.mime, appends: b.appends, bytes: b.bytes,
                first: b.first, encrypted: b.encrypted,
            })),
            eme: eme,
            elements: elements(),
        });
    }, 2000);
})();
