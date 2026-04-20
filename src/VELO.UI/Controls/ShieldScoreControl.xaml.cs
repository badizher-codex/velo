using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VELO.Security.Models;
using VELO.UI.ViewModels;

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
            SafetyLevel.Gold      => ("#FFD4AF37", "#FFD4AF37"),
            SafetyLevel.Green     => ("#FF1A3A20", "#FF2EB54F"),
            SafetyLevel.Yellow    => ("#FF3A2E10", "#FFF0B429"),
            SafetyLevel.Red       => ("#FF3A0E0E", "#FFE53E3E"),
            SafetyLevel.Analyzing => ("#FF1A1A2E", "#FF555566"),
            _                     => ("#FF1A1A2E", "#FF555566"),
        };

        BadgeBorder.Background   = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        BadgeBorder.BorderBrush  = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        BadgeBorder.BorderThickness = new Thickness(1.5);
        IconLabel.Text = _vm.Icon;
    }

    private void PlayPulse(SafetyLevel level)
    {
        var colorHex = level switch
        {
            SafetyLevel.Gold   => "#FFD4AF37",
            SafetyLevel.Green  => "#FF2EB54F",
            SafetyLevel.Yellow => "#FFF0B429",
            SafetyLevel.Red    => "#FFE53E3E",
            _                  => "#FF555566",
        };

        PulseRing.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

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
