using VELO.Data.Models;

namespace VELO.Core.Containers;

/// <summary>
/// Enforces strict isolation rules for Banking-mode containers.
///
/// Rules (all enforced at navigation/request time by the caller):
///   1. Force HTTPS — HTTP navigations are redirected or blocked.
///   2. No history — visits are never written to the history table.
///   3. Fingerprint protection at 100% — all canvas/WebGL/font APIs spoofed.
///   4. No cross-container cookie sharing (guaranteed by WebView2 partition).
///   5. No external referrer — Referrer-Policy: no-referrer injected.
/// </summary>
public static class BankingContainerPolicy
{
    public static bool Applies(Container? container)
        => container?.IsBankingMode == true;

    public static bool Applies(string containerId)
        => containerId == "banking";

    /// <summary>
    /// Returns true if the given URL should be blocked because it is plain HTTP.
    /// </summary>
    public static bool ShouldBlockHttp(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>
    /// Converts an HTTP URL to HTTPS. Returns the original if not HTTP.
    /// </summary>
    public static string ForceHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)) return url;

        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri;
    }

    /// <summary>
    /// JavaScript injected into every page inside a banking container.
    /// Spoof canvas/WebGL fingerprinting and inject strict referrer policy.
    /// </summary>
    public const string FingerprintScript = """
        (function() {
            // Canvas spoofing
            const origToDataURL = HTMLCanvasElement.prototype.toDataURL;
            HTMLCanvasElement.prototype.toDataURL = function(type) {
                const ctx = this.getContext('2d');
                if (ctx) {
                    const data = ctx.getImageData(0, 0, this.width, this.height);
                    for (let i = 0; i < data.data.length; i += 4) {
                        data.data[i]     = (data.data[i]     + (Math.random() * 2 - 1)) | 0;
                        data.data[i + 1] = (data.data[i + 1] + (Math.random() * 2 - 1)) | 0;
                        data.data[i + 2] = (data.data[i + 2] + (Math.random() * 2 - 1)) | 0;
                    }
                    ctx.putImageData(data, 0, 0);
                }
                return origToDataURL.apply(this, arguments);
            };

            // WebGL renderer spoofing
            const origGetParam = WebGLRenderingContext.prototype.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(param) {
                if (param === 37445) return 'VELO Graphics';   // UNMASKED_VENDOR_WEBGL
                if (param === 37446) return 'VELO Renderer';   // UNMASKED_RENDERER_WEBGL
                return origGetParam.call(this, param);
            };

            // No-referrer meta
            const meta = document.createElement('meta');
            meta.name    = 'referrer';
            meta.content = 'no-referrer';
            document.head && document.head.appendChild(meta);
        })();
        """;
}
