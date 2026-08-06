namespace VELO.UI.Themes;

/// <summary>
/// Hues cycled for things that need to be told apart but carry no meaning:
/// new-tab shortcut tiles, workspace chips.
///
/// Not part of the theme dictionaries on purpose — these are identity colours,
/// and an item that is teal must stay teal when the user flips to light mode
/// or they lose the thing they were recognising. What they are NOT is the v2.4
/// set (#00BCD4 / #7C4DFF / #E91E63 / #FF5722 at full saturation, straight off
/// the 2014 Material 500 ramp), which vibrated against the dark chrome and had
/// no chance of reading on white.
///
/// Every value here is a 600-weight tone: dark enough for white text, light
/// enough to survive on a #0B0B12 canvas.
/// </summary>
public static class DecorativePalette
{
    public static readonly string[] Hues =
    [
        "#FF0891B2", // cyan
        "#FF7C5CFF", // violet
        "#FF059669", // emerald
        "#FFD97706", // amber
        "#FFE11D48", // rose
        "#FF2563EB", // blue
        "#FFEA580C", // orange
        "#FF0D9488", // teal
    ];

    public static string At(int index) => Hues[Math.Abs(index) % Hues.Length];
}
