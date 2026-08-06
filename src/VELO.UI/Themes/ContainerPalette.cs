namespace VELO.UI.Themes;

/// <summary>
/// The colour identity of a tab container (Personal / Work / Banking /
/// Shopping / none).
///
/// This table existed verbatim in three places — TabBar, TabSidebar and
/// MainWindow — each with its own copy of the same five neon hex values, so a
/// tweak in one left the other two behind. One table, resolved from the active
/// theme, so the colours also stay legible when the app is light.
/// </summary>
public static class ContainerPalette
{
    public static string KeyFor(string? containerId) => containerId switch
    {
        "personal" => ThemePalette.Keys.ContainerPersonal,
        "work"     => ThemePalette.Keys.ContainerWork,
        "banking"  => ThemePalette.Keys.ContainerBanking,
        "shopping" => ThemePalette.Keys.ContainerShopping,
        _          => ThemePalette.Keys.ContainerNone
    };

    /// <summary>"#AARRGGBB" for the container, in the theme currently applied.</summary>
    public static string HexFor(string? containerId) => ThemePalette.Hex(KeyFor(containerId));

    /// <summary>The same colour at 20% alpha — the row tint the sidebar paints
    /// behind a tab so the container is readable at a glance without a label.</summary>
    public static string SubtleHexFor(string? containerId)
    {
        var c = ThemePalette.Color(KeyFor(containerId));
        return $"#33{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static readonly string[] Ids = ["none", "personal", "work", "banking", "shopping"];
}
