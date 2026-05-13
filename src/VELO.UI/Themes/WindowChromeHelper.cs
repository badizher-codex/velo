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

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static void ApplyDarkTitleBar(IntPtr hwnd)
    {
        int useDark = 1;
        try
        {
            // HRESULT is ignored — older Windows builds return E_INVALIDARG
            // for unrecognised attributes; the chrome stays light, the rest
            // of the app keeps its dark theme. No user-visible failure mode.
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                                  ref useDark, sizeof(int));
        }
        catch
        {
            // dwmapi.dll missing (extremely old Windows). Silently ignore.
        }
    }
}
