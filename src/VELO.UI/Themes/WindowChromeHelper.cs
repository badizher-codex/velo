using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VELO.UI.Themes;

/// <summary>
/// Flips a WPF Window's Win32 title bar between light and dark via
/// <c>DwmSetWindowAttribute</c> / <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>
/// (attribute 20, Windows 10 build 19041+).
///
/// The standard WPF Window doesn't theme its native chrome — the strip with
/// min/max/close lives in the DWM, not in WPF. Edge/Chrome/Brave/VS Code all
/// flip this attribute so the chrome matches their content.
///
/// v2.4.35 shipped this as a one-way "DarkTitleBar=True". With a light theme
/// in play that assumption inverts: the attribute now follows
/// <see cref="ThemeService.IsDark"/> and gets re-applied to every open window
/// when the theme changes. Windows that miss the attached property (a Window
/// style overridden downstream, ModernWpfUI chrome) can call
/// <see cref="ApplyToWindow"/> directly from OnSourceInitialized.
///
/// Older Windows versions silently ignore the DWM attribute (the P/Invoke
/// returns a non-zero HRESULT we catch and discard).
/// </summary>
public static class WindowChromeHelper
{
    public static readonly DependencyProperty FollowThemeProperty =
        DependencyProperty.RegisterAttached(
            "FollowTheme",
            typeof(bool),
            typeof(WindowChromeHelper),
            new PropertyMetadata(false, OnFollowThemeChanged));

    public static void SetFollowTheme(Window window, bool value) =>
        window.SetValue(FollowThemeProperty, value);

    public static bool GetFollowTheme(Window window) =>
        (bool)window.GetValue(FollowThemeProperty);

    private static void OnFollowThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        if (e.NewValue is not true) return;
        ApplyToWindow(window);
    }

    /// <summary>
    /// Applies the current theme's title-bar mode to <paramref name="window"/>,
    /// deferring to SourceInitialized if the HWND doesn't exist yet.
    /// Window is constructed → SourceInitialized → Loaded; the HWND is only
    /// materialised at the second step.
    /// </summary>
    public static void ApplyToWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        ApplyTitleBar(hwnd, ThemeService.IsDark);
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.SourceInitialized -= OnSourceInitialized;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            ApplyTitleBar(hwnd, ThemeService.IsDark);
    }

    /// <summary>Re-applies the title-bar mode to every open window. Called by
    /// ThemeService after a live swap — the DWM attribute is per-HWND and does
    /// not react to WPF resource changes on its own.</summary>
    public static void RefreshAllWindows(bool dark)
    {
        if (Application.Current is null) return;

        foreach (Window window in Application.Current.Windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) continue;

            ApplyTitleBar(hwnd, dark);

            // The DWM only repaints the non-client area on the next frame
            // change. Without a nudge the old title bar stays on screen until
            // the user moves or resizes the window, which reads as a bug.
            NudgeNonClientArea(hwnd);
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

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

    private const uint SWP_NOSIZE       = 0x0001;
    private const uint SWP_NOMOVE       = 0x0002;
    private const uint SWP_NOZORDER     = 0x0004;
    private const uint SWP_NOACTIVATE   = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static void ApplyTitleBar(IntPtr hwnd, bool dark)
    {
        int useDark = dark ? 1 : 0;
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

    private static void NudgeNonClientArea(IntPtr hwnd)
    {
        try
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER |
                         SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch
        {
            // user32 is always present; guard anyway so a theme switch can
            // never take the app down.
        }
    }
}
