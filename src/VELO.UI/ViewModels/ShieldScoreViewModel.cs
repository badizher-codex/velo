using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using VELO.Security.Models;

namespace VELO.UI.ViewModels;

public class ShieldScoreViewModel : INotifyPropertyChanged
{
    private SafetyLevel _level = SafetyLevel.Analyzing;
    private int         _score = 0;
    private string      _tooltip = "Analizando…";

    public SafetyLevel Level
    {
        get => _level;
        private set { _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(Icon)); OnPropertyChanged(nameof(BadgeBrush)); }
    }

    public int Score
    {
        get => _score;
        private set { _score = value; OnPropertyChanged(); }
    }

    public string Tooltip
    {
        get => _tooltip;
        private set { _tooltip = value; OnPropertyChanged(); }
    }

    public string Icon => Level switch
    {
        SafetyLevel.Gold      => "🥇",
        SafetyLevel.Green     => "🛡",
        SafetyLevel.Yellow    => "⚠",
        SafetyLevel.Red       => "🔴",
        SafetyLevel.Analyzing => "⏳",
        _                     => "🛡",
    };

    public Brush BadgeBrush => Level switch
    {
        SafetyLevel.Gold      => new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37)),
        SafetyLevel.Green     => new SolidColorBrush(Color.FromRgb(0x2E, 0xB5, 0x4F)),
        SafetyLevel.Yellow    => new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x29)),
        SafetyLevel.Red       => new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E)),
        SafetyLevel.Analyzing => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        _                     => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
    };

    public void Update(SafetyResult result)
    {
        Level = result.Level;
        Score = result.NumericScore;
        Tooltip = BuildTooltip(result);
    }

    public void SetAnalyzing()
    {
        Level   = SafetyLevel.Analyzing;
        Score   = 0;
        Tooltip = "Analizando seguridad…";
    }

    private static string BuildTooltip(SafetyResult r)
    {
        var label = r.Level switch
        {
            SafetyLevel.Gold   => "Excelente privacidad",
            SafetyLevel.Green  => "Sin amenazas detectadas",
            SafetyLevel.Yellow => "Señales de advertencia",
            SafetyLevel.Red    => "Amenaza detectada",
            _                  => "Analizando…",
        };

        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"🛡 Shield Score: {r.NumericScore:+#;-#;0} — {label}");

        if (r.ShortCircuitReason is { } sc)
        {
            lines.AppendLine($"🔴 {sc}");
        }
        else
        {
            foreach (var p in r.ReasonsPositive) lines.AppendLine($"✅ {p}");
            foreach (var n in r.ReasonsNegative) lines.AppendLine($"⚠ {n}");
        }

        return lines.ToString().TrimEnd();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
