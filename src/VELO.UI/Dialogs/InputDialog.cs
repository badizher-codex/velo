using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VELO.UI.Dialogs;

/// <summary>Lightweight programmatic prompt dialog — no XAML dependency.</summary>
public static class InputDialog
{
    /// <summary>
    /// Shows a modal prompt and returns the user's input, or <see langword="null"/> if cancelled.
    /// </summary>
    public static string? Show(Window owner, string title, string prompt, string defaultValue = "")
    {
        var dialog = new Window
        {
            Owner                 = owner,
            Title                 = title,
            Width                 = 360,
            Height                = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode            = ResizeMode.NoResize,
            Background            = System.Windows.Media.Brushes.Transparent,
        };

        // Dark-themed border matching VELO palette
        var root = new Border
        {
            Background      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x0D, 0x0D, 0x1A)),
            BorderBrush     = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x3A)),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Margin = new Thickness(16) };

        var label = new TextBlock
        {
            Text       = prompt,
            Foreground = new System.Windows.Media.SolidColorBrush(
                             System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xC0)),
            FontSize   = 13,
            Margin     = new Thickness(0, 0, 0, 8),
        };

        var input = new TextBox
        {
            Text            = defaultValue,
            Background      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x2A)),
            Foreground      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xFF)),
            BorderBrush     = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x60)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(6, 4, 6, 4),
            FontSize        = 13,
            Margin          = new Thickness(0, 0, 0, 12),
        };

        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        string? result = null;

        var okBtn = new Button
        {
            Content         = "Aceptar",
            Padding         = new Thickness(16, 4, 16, 4),
            Margin          = new Thickness(0, 0, 8, 0),
            Background      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
            Foreground      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xFF)),
            BorderThickness = new Thickness(1),
            BorderBrush     = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x88)),
            IsDefault       = true,
        };
        okBtn.Click += (_, _) => { result = input.Text.Trim(); dialog.DialogResult = true; };

        var cancelBtn = new Button
        {
            Content         = "Cancelar",
            Padding         = new Thickness(16, 4, 16, 4),
            Background      = System.Windows.Media.Brushes.Transparent,
            Foreground      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x88)),
            BorderThickness = new Thickness(0),
            IsCancel        = true,
        };

        buttonRow.Children.Add(okBtn);
        buttonRow.Children.Add(cancelBtn);

        stack.Children.Add(label);
        stack.Children.Add(input);
        stack.Children.Add(buttonRow);
        root.Child = stack;
        dialog.Content = root;

        // Select-all on open
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };

        // Enter = OK, Esc = cancel
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.DialogResult = false; }
        };

        return dialog.ShowDialog() == true ? result : null;
    }
}
