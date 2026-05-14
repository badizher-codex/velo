using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VELO.UI.Converters;

/// <summary>
/// v2.4.43 — Materialises a <c>byte[]</c> (typically a PNG/ICO favicon payload)
/// into a WPF <see cref="BitmapImage"/> for binding into an
/// <c>Image.Source</c>.
///
/// Returns <see cref="Binding.DoNothing"/> when the input is null or empty so
/// the bound element shows nothing — sibling fallback elements in XAML (e.g.
/// a 🌐 TextBlock with <c>DataTrigger</c> on null) take over the visual slot.
///
/// Bitmap is loaded with <see cref="BitmapCacheOption.OnLoad"/> + the stream
/// is disposed immediately, so the BitmapImage keeps no reference to the
/// underlying memory and TabInfo can replace <c>FaviconData</c> without
/// orphaning resources.
///
/// One-way only. <see cref="ConvertBack"/> throws — favicons are produced by
/// WebView2 and the converter doesn't know how to serialise back to bytes.
/// </summary>
public sealed class BytesToImageSourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
            return Binding.DoNothing;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad; // copy bytes, release stream
            bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bmp.EndInit();
            bmp.Freeze(); // safe to share across threads / not re-bound to dispatcher
            return bmp;
        }
        catch
        {
            // Malformed favicon (rare — WebView2 already validates) → fallback to nothing.
            return Binding.DoNothing;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Favicons are write-once from WebView2; back-conversion has no meaning.");
}
