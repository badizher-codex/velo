using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VELO.Core.Localization;

namespace VELO.UI.Dialogs;

/// <summary>
/// v2.4.61 F-3 — Site permission prompt (camera/mic/geolocation/notifications/
/// clipboard). Built in code, not XAML: StaticResource lookups only fail at
/// runtime (lesson #1) and this dialog must never be the reason a permission
/// silently denies. The caller passes the decision to WebView2, which persists
/// it in the profile when Remember is checked (CoreWebView2 SavesInProfile).
/// </summary>
public sealed class PermissionPrompt : Window
{
    public readonly record struct Decision(bool Allow, bool Remember);

    private static readonly Brush _bg      = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x26));
    private static readonly Brush _fg      = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xF2));
    private static readonly Brush _muted   = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB0));
    private static readonly Brush _accent  = new SolidColorBrush(Color.FromRgb(0x7C, 0x4D, 0xFF));
    private static readonly Brush _btnBg   = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x40));

    private readonly CheckBox _remember;
    private bool _allow;

    private PermissionPrompt(Window? owner, string host, string kindLocKey)
    {
        var L = LocalizationService.Current;
        var kindName = L.T(kindLocKey);

        Title                 = L.T("perm.title");
        Owner                 = owner;
        WindowStartupLocation = owner != null
            ? WindowStartupLocation.CenterOwner
            : WindowStartupLocation.CenterScreen;
        SizeToContent   = SizeToContent.WidthAndHeight;
        ResizeMode      = ResizeMode.NoResize;
        WindowStyle     = WindowStyle.ToolWindow;
        Background      = _bg;
        MaxWidth        = 440;
        ShowInTaskbar   = false;

        var body = new TextBlock
        {
            Text         = string.Format(L.T("perm.body"), host, kindName),
            Foreground   = _fg,
            FontSize     = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 14),
        };

        _remember = new CheckBox
        {
            Content    = new TextBlock { Text = L.T("perm.remember"), Foreground = _muted, FontSize = 12 },
            IsChecked  = true,
            Margin     = new Thickness(0, 0, 0, 16),
        };

        var allowBtn = MakeButton(L.T("perm.allow"), _accent, isDefault: true);
        allowBtn.Click += (_, _) => { _allow = true; DialogResult = true; };

        var blockBtn = MakeButton(L.T("perm.block"), _btnBg, isDefault: false);
        blockBtn.Click += (_, _) => { _allow = false; DialogResult = true; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(blockBtn);
        buttons.Children.Add(allowBtn);

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        root.Children.Add(body);
        root.Children.Add(_remember);
        root.Children.Add(buttons);
        Content = root;
    }

    private static Button MakeButton(string text, Brush bg, bool isDefault) => new()
    {
        Content    = text,
        Background = bg,
        Foreground = _fg,
        BorderThickness = new Thickness(0),
        Padding    = new Thickness(18, 7, 18, 7),
        Margin     = new Thickness(8, 0, 0, 0),
        IsDefault  = isDefault,
        Cursor     = System.Windows.Input.Cursors.Hand,
    };

    /// <summary>Blocks on the UI thread until the user decides. Closing the
    /// window without choosing (Esc / X) counts as Block-once (deny, don't
    /// remember) so a dismissed prompt can be re-asked later.</summary>
    public static Decision Show(Window? owner, string host, string kindLocKey)
    {
        var dlg    = new PermissionPrompt(owner, host, kindLocKey);
        var chosen = dlg.ShowDialog() == true;
        return chosen
            ? new Decision(dlg._allow, dlg._remember.IsChecked == true)
            : new Decision(Allow: false, Remember: false);
    }
}
