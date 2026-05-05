// VELO — Fingerprint Protection Script
// Injected via AddScriptToExecuteOnDocumentCreatedAsync — runs before any page script
// Strategy: only intercept EXPORT methods (toDataURL, toBlob) using offscreen clones
// Never modify visible canvas content or intercept getImageData (breaks rendering)
(function () {
    'use strict';

    // v2.1.5.1 — Per-site shields allowlist. Anti-bot endpoints (Akamai,
    // PerimeterX, Imperva) reject login when our spoofed canvas/WebGL/audio
    // fingerprint mismatches their expected device baseline. Bail out for
    // those domains while keeping every other tracker/ad/request defense.
    try {
        const list = window.__VELO_RELAXED_DOMAINS__ || [];
        const h = (location.hostname || '').toLowerCase();
        if (list.some(d => h === d || h.endsWith('.' + d))) return;
    } catch { /* worst case: spoof normally */ }

    // ── Session-stable random (seeded per session, never stored) ──────────
    const seed = Math.random();
    function seededRand(min, max) {
        const x = Math.sin(seed * 9301 + (min * 49297 + max * 233)) * 10000;
        return min + Math.floor((x - Math.floor(x)) * (max - min + 1));
    }
    function seededPick(arr) { return arr[seededRand(0, arr.length - 1)]; }

    // ── 1. Canvas fingerprint protection ──────────────────────────────────
    // Only intercepts toDataURL and toBlob — uses offscreen clone so the
    // visible canvas is NEVER modified. This prevents the black-page bug.
    const _origToDataURL = HTMLCanvasElement.prototype.toDataURL;
    const _origToBlob    = HTMLCanvasElement.prototype.toBlob;
    const _origGetImageData = CanvasRenderingContext2D.prototype.getImageData;

    const noiseMag = 2; // ±2 on a handful of pixels — visually invisible

    function noiseClone(sourceCanvas) {
        const clone = document.createElement('canvas');
        clone.width  = sourceCanvas.width;
        clone.height = sourceCanvas.height;
        const ctx = clone.getContext('2d');
        if (!ctx) return sourceCanvas; // fallback: return original
        ctx.drawImage(sourceCanvas, 0, 0);
        const img  = _origGetImageData.call(ctx, 0, 0, clone.width, clone.height);
        const data = img.data;
        const step = Math.max(1, Math.floor(data.length / 32));
        for (let i = 0; i < data.length; i += step) {
            const delta = seededRand(-noiseMag, noiseMag);
            data[i] = Math.max(0, Math.min(255, data[i] + delta));
        }
        ctx.putImageData(img, 0, 0);
        return clone;
    }

    HTMLCanvasElement.prototype.toDataURL = function (...args) {
        try {
            if (this.width === 0 || this.height === 0)
                return _origToDataURL.apply(this, args);
            return _origToDataURL.apply(noiseClone(this), args);
        } catch (e) {
            return _origToDataURL.apply(this, args);
        }
    };

    HTMLCanvasElement.prototype.toBlob = function (callback, ...args) {
        try {
            if (this.width === 0 || this.height === 0)
                return _origToBlob.call(this, callback, ...args);
            return _origToBlob.call(noiseClone(this), callback, ...args);
        } catch (e) {
            return _origToBlob.call(this, callback, ...args);
        }
    };

    // ── 2. WebGL spoof — Windows-realistic via ANGLE ──────────────────────
    const gpuOptions = [
        {
            vendor:   'Google Inc. (Intel)',
            renderer: 'ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)'
        },
        {
            vendor:   'Google Inc. (Intel)',
            renderer: 'ANGLE (Intel, Intel(R) HD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)'
        },
        {
            vendor:   'Google Inc. (NVIDIA)',
            renderer: 'ANGLE (NVIDIA, NVIDIA GeForce GTX 1650 Direct3D11 vs_5_0 ps_5_0, D3D11)'
        },
        {
            vendor:   'Google Inc. (AMD)',
            renderer: 'ANGLE (AMD, AMD Radeon RX 5500 XT Direct3D11 vs_5_0 ps_5_0, D3D11)'
        }
    ];
    const gpu = seededPick(gpuOptions);

    try {
        const _get1 = WebGLRenderingContext.prototype.getParameter;
        WebGLRenderingContext.prototype.getParameter = function (p) {
            if (p === 37445) return gpu.vendor;
            if (p === 37446) return gpu.renderer;
            return _get1.apply(this, arguments);
        };
    } catch (e) { /* WebGL not available */ }

    try {
        const _get2 = WebGL2RenderingContext.prototype.getParameter;
        WebGL2RenderingContext.prototype.getParameter = function (p) {
            if (p === 37445) return gpu.vendor;
            if (p === 37446) return gpu.renderer;
            return _get2.apply(this, arguments);
        };
    } catch (e) { /* WebGL2 not available */ }

    // ── 3. AudioContext fingerprint noise ─────────────────────────────────
    try {
        const _origCreateAnalyser = AudioContext.prototype.createAnalyser;
        AudioContext.prototype.createAnalyser = function () {
            const analyser = _origCreateAnalyser.apply(this);
            const _origGetFloat = analyser.getFloatFrequencyData.bind(analyser);
            analyser.getFloatFrequencyData = function (array) {
                _origGetFloat(array);
                const mag = 0.0001;
                for (let i = 0; i < array.length; i++) {
                    array[i] += (seed - 0.5) * mag;
                }
            };
            return analyser;
        };
    } catch (e) { /* AudioContext not available */ }

    // ── 4. Hardware — realistic session-stable values ──────────────────────
    const cpuCores = seededPick([4, 6, 8, 8, 12, 16]);
    const memGb    = seededPick([4, 8, 8, 16]);

    try { Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => cpuCores }); } catch (e) { }
    try { Object.defineProperty(navigator, 'deviceMemory',        { get: () => memGb });    } catch (e) { }

    // ── 5. Platform — correct for VELO (Windows only) ─────────────────────
    try { Object.defineProperty(navigator, 'platform', { get: () => 'Win32' }); } catch (e) { }

    // ── 6. Do NOT override languages — let real browser locale through ─────

})();
