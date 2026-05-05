// VELO — WebRTC IP Protection
// Mode "Spoof": intercept ICE candidates, strip local IPs
// Mode "Disabled": remove RTCPeerConnection entirely (set from C# based on settings)
(function () {
    'use strict';

    // v2.1.5.1 — Per-site shields allowlist. Same rationale as fingerprint
    // script: Akamai/PerimeterX login endpoints inspect ICE candidates;
    // mismatches between spoofed and real signals are treated as bot.
    try {
        const list = window.__VELO_RELAXED_DOMAINS__ || [];
        const h = (location.hostname || '').toLowerCase();
        if (list.some(d => h === d || h.endsWith('.' + d))) return;
    } catch { /* fall through */ }

    const VELO_WEBRTC_MODE = window.__VELO_WEBRTC_MODE__ || 'spoof'; // injected by C# per settings

    if (VELO_WEBRTC_MODE === 'disabled') {
        // Hard disable — Bunker / Paranoid mode
        window.RTCPeerConnection          = undefined;
        window.webkitRTCPeerConnection    = undefined;
        window.mozRTCPeerConnection       = undefined;
        window.RTCSessionDescription      = undefined;
        window.RTCIceCandidate            = undefined;
        return;
    }

    // Spoof mode: let WebRTC work but mask local IPs in ICE candidates
    const _OrigRTCPeerConnection = window.RTCPeerConnection;
    if (!_OrigRTCPeerConnection) return;

    window.RTCPeerConnection = function (config, constraints) {
        // Force srflx/relay only (no host candidates that leak local IP)
        const safeConfig = Object.assign({}, config, {
            iceTransportPolicy: 'relay' // only relay — prevents local IP leak
        });

        const pc = new _OrigRTCPeerConnection(safeConfig, constraints);

        // Additionally filter out mDNS / local candidates from SDP
        const _origCreateOffer = pc.createOffer.bind(pc);
        pc.createOffer = function (...args) {
            return _origCreateOffer(...args).then(offer => {
                offer.sdp = stripLocalCandidates(offer.sdp);
                return offer;
            });
        };

        return pc;
    };

    window.RTCPeerConnection.prototype = _OrigRTCPeerConnection.prototype;

    function stripLocalCandidates(sdp) {
        return sdp.split('\n')
            .filter(line => {
                if (!line.startsWith('a=candidate:')) return true;
                // Remove host candidates (they contain local IPs)
                return !line.includes(' host ');
            })
            .join('\n');
    }

})();
