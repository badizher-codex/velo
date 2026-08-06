using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VELO.Security.Models;
using VELO.UI.ViewModels;
using VELO.UI.Themes;

namespace VELO.UI.Controls;

public partial class ShieldScoreControl : UserControl
{
    private readonly ShieldScoreViewModel _vm = new();
    private SafetyLevel _previousLevel = SafetyLevel.Analyzing;

    public event EventHandler? ShieldClicked;

    public ShieldScoreControl()
    {
        InitializeComponent();
        DataContext = _vm;
        MouseLeftButtonUp += (_, _) => ShieldClicked?.Invoke(this, EventArgs.Empty);
    }

    public void Update(SafetyResult result)
    {
        var prevLevel = _previousLevel;
        _vm.Update(result);
        ToolTipService.SetToolTip(this, _vm.Tooltip);
        ApplyStyle(result.Level);

        if (result.Level != prevLevel)
            PlayPulse(result.Level);

        _previousLevel = result.Level;
    }

    public void SetAnalyzing()
    {
        _vm.SetAnalyzing();
        ApplyStyle(SafetyLevel.Analyzing);
        ToolTipService.SetToolTip(this, "Analizando seguridad…");
    }

    private void ApplyStyle(SafetyLevel level)
    {
        var (bg, border) = level switch
        {
            SafetyLevel.Gold   => (ThemePalette.Keys.SurfaceRaised,     ThemePalette.Keys.ShieldGold),
            SafetyLevel.Green  => (ThemePalette.Keys.StatusSuccessSoft, ThemePalette.Keys.ShieldGreen),
            SafetyLevel.Yellow => (ThemePalette.Keys.StatusWarningSoft, ThemePalette.Keys.ShieldYellow),
            SafetyLevel.Red    => (ThemePalette.Keys.StatusDangerSoft,  ThemePalette.Keys.ShieldRed),
            _                  => (ThemePalette.Keys.SurfaceRaised,     ThemePalette.Keys.ShieldNeutral),
        };

        BadgeBorder.Background   = ThemePalette.Brush(bg);
        BadgeBorder.BorderBrush  = ThemePalette.Brush(border);
        BadgeBorder.BorderThickness = new Thickness(1.5);
        IconLabel.Text = _vm.Icon;
    }

    private void PlayPulse(SafetyLevel level)
    {
        PulseRing.BorderBrush = ThemePalette.Brush(level switch
        {
            SafetyLevel.Gold   => ThemePalette.Keys.ShieldGold,
            SafetyLevel.Green  => ThemePalette.Keys.ShieldGreen,
            SafetyLevel.Yellow => ThemePalette.Keys.ShieldYellow,
            SafetyLevel.Red    => ThemePalette.Keys.ShieldRed,
            _                  => ThemePalette.Keys.ShieldNeutral,
        });

        var fadeIn  = new DoubleAnimation(0, 0.8, TimeSpan.FromMilliseconds(150));
        var fadeOut = new DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(400));
        fadeOut.BeginTime = TimeSpan.FromMilliseconds(150);

        var group = new AnimationClock[0]; // placeholder
        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeIn, PulseRing);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, PulseRing);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }
}
