using System.Windows;
using System.Windows.Media;

namespace VELO.UI.Themes;

/// <summary>
/// Resolves theme tokens from C#.
///
/// XAML gets live theming for free through DynamicResource. Code-behind that
/// builds brushes imperatively does not: a <c>new SolidColorBrush(Color.FromRgb(…))</c>
/// assigned to <c>element.Background</c> is a literal that survives every
/// theme swap. Roughly sixty of those were scattered across the UI project —
/// the shield ring, malwaredex severity rows, agent chat bubbles, per-container
/// tab dots — and each one was a patch of the old dark palette that would have
/// stayed dark forever under a light theme.
///
/// Call sites resolve through here and re-resolve on
/// <see cref="ThemeService.ThemeChanged"/>.
/// </summary>
public static class ThemePalette
{
    /// <summary>Looks up a brush token by key. Returns <paramref name="fallback"/>
    /// (default: transparent) when the key is missing, so a typo degrades to an
    /// invisible element rather than taking the window down — the failure mode
    /// StaticResource had, which crashed VELO on launch in v2.4.0/v2.4.1.</summary>
    public static Brush Brush(string key, Brush? fallback = null)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return fallback ?? Brushes.Transparent;
    }

    public static Color Color(string key, Color fallback = default)
    {
        var resource = Application.Current?.TryFindResource(key);
        return resource switch
        {
            Color color            => color,
            SolidColorBrush brush  => brush.Color,
            _                      => fallback
        };
    }

    /// <summary>Token as an "#AARRGGBB" string, for the call sites that store a
    /// colour as text — TabInfo.ContainerColor and friends, which reach the
    /// visual tree through StringToBrushConverter.</summary>
    public static string Hex(string key)
    {
        var c = Color(key);
        return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static Brush Surface        => Brush(Keys.SurfaceBase);
    public static Brush SurfaceRaised  => Brush(Keys.SurfaceRaised);
    public static Brush SurfaceOverlay => Brush(Keys.SurfaceOverlay);
    public static Brush SurfaceHover   => Brush(Keys.SurfaceHover);
    public static Brush SurfaceActive  => Brush(Keys.SurfaceActive);
    public static Brush SurfaceSelected => Brush(Keys.SurfaceSelected);

    public static Brush TextPrimary   => Brush(Keys.TextPrimary);
    public static Brush TextSecondary => Brush(Keys.TextSecondary);
    public static Brush TextMuted     => Brush(Keys.TextMuted);
    public static Brush TextOnAccent  => Brush(Keys.TextOnAccent);

    public static Brush BorderSubtle => Brush(Keys.BorderSubtle);
    public static Brush BorderStrong => Brush(Keys.BorderStrong);

    public static Brush Accent     => Brush(Keys.Accent);
    public static Brush AccentText => Brush(Keys.AccentText);
    public static Brush AccentSoft => Brush(Keys.AccentSoft);

    public static Brush Success     => Brush(Keys.StatusSuccess);
    public static Brush SuccessText => Brush(Keys.StatusSuccessText);
    public static Brush SuccessSoft => Brush(Keys.StatusSuccessSoft);
    public static Brush Danger      => Brush(Keys.StatusDanger);
    public static Brush DangerText  => Brush(Keys.StatusDangerText);
    public static Brush DangerSoft  => Brush(Keys.StatusDangerSoft);
    public static Brush Warning     => Brush(Keys.StatusWarning);
    public static Brush WarningText => Brush(Keys.StatusWarningText);
    public static Brush WarningSoft => Brush(Keys.StatusWarningSoft);
    public static Brush Info        => Brush(Keys.StatusInfo);
    public static Brush InfoText    => Brush(Keys.StatusInfoText);
    public static Brush InfoSoft    => Brush(Keys.StatusInfoSoft);

    /// <summary>Token key names. Kept as constants so a rename in the theme
    /// dictionaries breaks the build instead of silently painting nothing.</summary>
    public static class Keys
    {
        public const string SurfaceCanvas   = "SurfaceCanvasBrush";
        public const string SurfaceBase     = "SurfaceBaseBrush";
        public const string SurfaceRaised   = "SurfaceRaisedBrush";
        public const string SurfaceOverlay  = "SurfaceOverlayBrush";
        public const string SurfaceInput    = "SurfaceInputBrush";
        public const string SurfaceHover    = "SurfaceHoverBrush";
        public const string SurfaceActive   = "SurfaceActiveBrush";
        public const string SurfaceSelected = "SurfaceSelectedBrush";

        public const string TextPrimary   = "TextPrimaryBrush";
        public const string TextSecondary = "TextSecondaryBrush";
        public const string TextMuted     = "TextMutedBrush";
        public const string TextOnAccent  = "TextOnAccentBrush";

        public const string BorderSubtle = "BorderSubtleBrush";
        public const string BorderStrong = "BorderStrongBrush";

        public const string Accent        = "AccentBrush";
        public const string AccentHover   = "AccentHoverBrush";
        public const string AccentPressed = "AccentPressedBrush";
        public const string AccentText    = "AccentTextBrush";
        public const string AccentSoft    = "AccentSoftBrush";

        public const string StatusSuccess     = "StatusSuccessBrush";
        public const string StatusSuccessText = "StatusSuccessTextBrush";
        public const string StatusSuccessSoft = "StatusSuccessSoftBrush";
        public const string StatusDanger      = "StatusDangerBrush";
        public const string StatusDangerText  = "StatusDangerTextBrush";
        public const string StatusDangerSoft  = "StatusDangerSoftBrush";
        public const string StatusWarning     = "StatusWarningBrush";
        public const string StatusWarningText = "StatusWarningTextBrush";
        public const string StatusWarningSoft = "StatusWarningSoftBrush";
        public const string StatusInfo        = "StatusInfoBrush";
        public const string StatusInfoText    = "StatusInfoTextBrush";
        public const string StatusInfoSoft    = "StatusInfoSoftBrush";

        public const string ShieldRed     = "ShieldRedBrush";
        public const string ShieldYellow  = "ShieldYellowBrush";
        public const string ShieldGreen   = "ShieldGreenBrush";
        public const string ShieldGold    = "ShieldGoldBrush";
        public const string ShieldNeutral = "ShieldNeutralBrush";

        public const string ContainerPersonal = "ContainerPersonalBrush";
        public const string ContainerWork     = "ContainerWorkBrush";
        public const string ContainerBanking  = "ContainerBankingBrush";
        public const string ContainerShopping = "ContainerShoppingBrush";
        public const string ContainerNone     = "ContainerNoneBrush";
    }
}
