using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VELO.UI.Themes;

/// <summary>
/// Phase 5.3 (v2.4.35) — attached property that flips a WPF Window's Win32
/// title bar into dark mode via <c>DwmSetWindowAttribute</c> with
/// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (attribute 20, Windows 10 build 19041+).
///
/// The standard WPF Window doesn't theme its native chrome (the strip with
/// min/max/close lives in the Windows DWM, not in WPF). Edge/Chrome/Brave/
/// VS Code all flip this attribute so the chrome matches their dark theme.
/// VELO's Phase 5 palette would clash against a system-default title bar
/// without this flip — the title bar was the most visible un-themed surface
/// reported by the maintainer after the v2.4.34 release.
///
/// Usage (XAML, applied globally via the Window style in DarkTheme.xaml):
///     &lt;Setter Property="themes:WindowChromeHelper.DarkTitleBar" Value="True"/&gt;
///
/// Older Windows versions silently ignore the DWM attribute (the P/Invoke
/// returns a non-zero HRESULT we catch and discard).
/// </summary>
public static class WindowChromeHelper
{
    public static readonly DependencyProperty DarkTitleBarProperty =
        DependencyProperty.RegisterAttached(
            "DarkTitleBar",
            typeof(bool),
            typeof(WindowChromeHelper),
            new PropertyMetadata(false, OnDarkTitleBarChanged));

    public static void SetDarkTitleBar(Window window, bool value) =>
        window.SetValue(DarkTitleBarProperty, value);

    public static bool GetDarkTitleBar(Window window) =>
        (bool)window.GetValue(DarkTitleBarProperty);

    private static void OnDarkTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        if (e.NewValue is not true) return;

        // SourceInitialized is the earliest point the HWND is materialised.
        // Window is constructed → SourceInitialized → Loaded.
        // If the window is already past SourceInitialized, apply immediately.
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
            ApplyDarkTitleBar(helper.Handle);
        else
            window.SourceInitialized += OnSourceInitialized;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.SourceInitialized -= OnSourceInitialized;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            ApplyDarkTitleBar(hwnd);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Win 10 build 19041+ (20H1, May 2020) and Win 11: attribute 20.
    // Win 10 build 18985-19041 (pre-20H1): attribute 19.
    // Earlier builds: neither works, app falls back to system theme silently.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE     = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    // Win 11 only — round window corners explicitly (default is "round 8px"
    // on Win 11 but worth being explicit because some Windows themes / DPI
    // configurations end up square otherwise). Win 10 ignores silently.
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// <summary>
    /// Public entry point — call directly from a Window's
    /// <c>OnSourceInitialized</c> override when the attached-property
    /// path doesn't fire (e.g. third-party Window styles override the
    /// implicit setter, ModernWpfUI's Window chrome, etc.).
    /// </summary>
    public static void ApplyToWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(window).Handle;
                if (h != IntPtr.Zero) ApplyDarkTitleBar(h);
            };
            return;
        }
        ApplyDarkTitleBar(hwnd);
    }

    private static void ApplyDarkTitleBar(IntPtr hwnd)
    {
        int useDark = 1;
        try
        {
            // Try the modern attribute first; if Windows doesn't recognise
            // it (build pre-20H1), retry with the legacy attribute number.
            // Both target the same setting; only the attribute ID changed
            // between Windows 10 19041 and the older preview builds.
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                                           ref useDark, sizeof(int));
            if (hr != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD,
                                      ref useDark, sizeof(int));
            }

            // v2.4.37 — explicitly request rounded corners on Windows 11.
            // Win 10 returns E_INVALIDARG which we ignore.
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                                  ref round, sizeof(int));
        }
        catch
        {
            // dwmapi.dll missing (extremely old Windows). Silently ignore.
        }
    }
}
