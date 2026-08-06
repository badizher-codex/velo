using System.Windows;
using Microsoft.Win32;

namespace VELO.UI.Themes;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// Swaps the active theme dictionary at runtime.
///
/// The mechanics: App.xaml merges exactly two dictionaries — the theme
/// (Dark.xaml or Light.xaml) at <see cref="ThemeSlot"/>, and Controls.xaml
/// after it. Replacing the entry at that index raises the resource-changed
/// notification WPF needs to re-evaluate every <c>DynamicResource</c> in the
/// live visual tree. Nothing is re-created, no window is reopened.
///
/// This only works because every colour in Controls.xaml and in the views is
/// a DynamicResource. A StaticResource is baked in at parse time and would
/// keep its pre-swap brush — the reason the pre-v2.5 codebase (628
/// StaticResource, 0 DynamicResource) could not have had a live theme toggle
/// no matter how the dictionaries were arranged.
/// </summary>
public static class ThemeService
{
    /// <summary>Index of the theme dictionary inside
    /// <c>Application.Current.Resources.MergedDictionaries</c>. Must match the
    /// order in App.xaml — theme first, Controls.xaml second.</summary>
    private const int ThemeSlot = 0;

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri DarkUri  = new("pack://application:,,,/VELO.UI;component/Themes/Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/VELO.UI;component/Themes/Light.xaml");

    private static bool _systemHookInstalled;

    /// <summary>The mode the user picked (System / Light / Dark).</summary>
    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>The variant actually on screen. With <see cref="ThemeMode.System"/>
    /// this follows Windows; otherwise it mirrors <see cref="Mode"/>.</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after a swap completes, on the UI thread. Code-behind
    /// that paints with colours resolved in C# (badge fills, the shield ring,
    /// per-container tab dots) subscribes to repaint itself — a DynamicResource
    /// can't reach a brush that was built with Color.FromRgb.</summary>
    public static event Action? ThemeChanged;

    public static ThemeMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "dark"  => ThemeMode.Dark,
        _       => ThemeMode.System
    };

    public static string Serialize(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "Light",
        ThemeMode.Dark  => "Dark",
        _               => "System"
    };

    /// <summary>Applies <paramref name="mode"/> immediately. Safe to call
    /// before any window exists (App.OnStartup) and after (Settings).</summary>
    public static void Apply(ThemeMode mode)
    {
        Mode = mode;
        var dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark  => true,
            _               => SystemPrefersDark()
        };

        if (mode == ThemeMode.System)
            InstallSystemHook();

        SwapDictionary(dark);
    }

    private static void SwapDictionary(bool dark)
    {
        var app = Application.Current;
        if (app is null) return;

        IsDark = dark;

        var dictionary = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        var merged     = app.Resources.MergedDictionaries;

        // Defensive: App.xaml owns the slot, but a partially-initialised
        // Application (unit tests, designer) may have none.
        if (merged.Count > ThemeSlot)
            merged[ThemeSlot] = dictionary;
        else
            merged.Insert(0, dictionary);

        WindowChromeHelper.RefreshAllWindows(dark);
        ThemeChanged?.Invoke();
    }

    /// <summary>Reads Windows' own app-theme preference. The registry value is
    /// <c>AppsUseLightTheme</c> (1 = light), not SystemUsesLightTheme — the
    /// latter is the taskbar/Start theme and users commonly set the two
    /// differently.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLight)
                return appsUseLight == 0;
        }
        catch
        {
            // Registry unavailable (locked-down policy, non-standard SKU).
        }
        return true;
    }

    private static void InstallSystemHook()
    {
        if (_systemHookInstalled) return;
        _systemHookInstalled = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (Mode != ThemeMode.System) return;

        var dark = SystemPrefersDark();
        if (dark == IsDark) return;

        // SystemEvents fires on its own thread; resource dictionaries are
        // owned by the UI thread.
        Application.Current?.Dispatcher.Invoke(() => SwapDictionary(dark));
    }

    /// <summary>Detaches the system hook. Called from App.OnExit so a
    /// long-lived static event doesn't keep the app object alive on shutdown.</summary>
    public static void Shutdown()
    {
        if (!_systemHookInstalled) return;
        _systemHookInstalled = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
