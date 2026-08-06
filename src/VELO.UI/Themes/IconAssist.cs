using System.Windows;
using System.Windows.Media;

namespace VELO.UI.Themes;

/// <summary>
/// Attaches a vector icon to a control whose Content is set from code.
///
/// The settings nav used emoji glyphs (🔒 🌐 🧠 🔍 🔑 🤝 🌍 ⚙️) baked into the
/// Content string. Two problems: colour emoji render at full saturation and
/// ignore Foreground, so they clash with any theme and cannot dim in a
/// disabled state; and the code-behind rewrites Content on every language
/// change, so the glyph had to be re-concatenated there too.
///
/// Holding the icon in a separate attached property keeps Content purely
/// textual (and purely localisable) while the template draws a monochrome Path
/// that inherits the control's Foreground like any other stroke.
/// </summary>
public static class IconAssist
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.RegisterAttached(
            "Data", typeof(Geometry), typeof(IconAssist),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static void SetData(DependencyObject element, Geometry? value) =>
        element.SetValue(DataProperty, value);

    public static Geometry? GetData(DependencyObject element) =>
        (Geometry?)element.GetValue(DataProperty);
}
