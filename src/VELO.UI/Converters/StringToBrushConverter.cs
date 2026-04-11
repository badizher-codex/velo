using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VELO.UI.Converters;

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var str = value?.ToString();
            if (string.IsNullOrEmpty(str) || str == "Transparent") return Brushes.Transparent;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(str));
        }
        catch { return Brushes.Transparent; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
