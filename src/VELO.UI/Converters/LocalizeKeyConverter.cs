using System.Globalization;
using System.Windows.Data;
using VELO.Core.Localization;

namespace VELO.UI.Converters;

/// <summary>
/// v2.0.5.3 — Resolves a localization key into the current language's string
/// at bind time. Used by data templates that need a static label localised
/// without polluting the underlying data model with VELO.Core dependencies
/// (cycle: VELO.Core → VELO.Data, so VELO.Data cannot reach LocalizationService).
///
/// Usage in XAML:
///   <conv:LocalizeKeyConverter x:Key="LK"/>
///   ...
///   <TextBlock Text="{Binding Source='history.badge.blocked', Converter={StaticResource LK}}"/>
///
/// To refresh after a language change, re-render the host control (e.g. the
/// HistoryWindow re-binds its ItemsSource on LanguageChanged).
/// </summary>
public sealed class LocalizeKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? parameter as string;
        return string.IsNullOrEmpty(key) ? "" : LocalizationService.Current.T(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
