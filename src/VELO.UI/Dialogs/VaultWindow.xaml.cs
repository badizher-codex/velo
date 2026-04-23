using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VELO.Core.Localization;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Vault;

namespace VELO.UI.Dialogs;

public partial class VaultWindow : Window
{
    private readonly VaultService _vault;
    private readonly SettingsRepository _settings;

    private List<PasswordEntry> _allEntries = [];
    private PasswordEntry? _editingEntry;

    public VaultWindow(VaultService vault, SettingsRepository settings)
    {
        _vault    = vault;
        _settings = settings;
        InitializeComponent();

        EditPassword.TextChanged += (_, _) => UpdateStrengthBar();
        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;

        Loaded += async (_, _) =>
        {
            if (_vault.IsUnlocked)
                await ShowVaultAsync();
        };
    }

    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        // Unlock screen
        Title               = L.T("title.vault");
        UnlockSubtitle.Text = L.T("vault.unlock.subtitle");
        MasterLabel.Text    = L.T("vault.master.label");
        UnlockBtn.Content   = L.T("vault.unlock.btn");
        // Main vault list
        AddBtn.Content      = L.T("vault.add");
        LockBtn.Content     = L.T("vault.lock");
        // Edit form labels
        SiteLabel.Text             = L.T("vault.form.site");
        UrlLabel.Text              = L.T("vault.form.url");
        UsernameLabel.Text         = L.T("vault.form.username");
        PasswordLabel.Text         = L.T("vault.form.password");
        NotesLabel.Text            = L.T("vault.form.notes");
        GeneratorTitleLabel.Text   = L.T("vault.form.generator");
        LengthTitleLabel.Text      = L.T("vault.form.length");
        GenUppercase.Content       = L.T("vault.form.uppercase");
        GenNumbers.Content         = L.T("vault.form.numbers");
        GenSymbols.Content         = L.T("vault.form.symbols");
        // Edit form buttons
        BackBtn.Content            = L.T("vault.form.back");
        GenerateBtn.Content        = L.T("vault.form.generate");
        TogglePasswordBtn.ToolTip  = L.T("vault.form.toggle");
        RegenerateBtn.Content      = L.T("vault.form.regenerate");
        DeleteBtn.Content          = L.T("vault.form.delete");
        CancelBtn.Content          = L.T("vault.form.cancel");
        SaveBtn.Content            = L.T("vault.form.save");
        // Required field error message (also update visible ones)
        var req = L.T("vault.form.required");
        SiteError.Text     = req;
        UsernameError.Text = req;
        PasswordError.Text = req;
    }

    // ── Unlock ────────────────────────────────────────────────────────────

    private async void Unlock_Click(object sender, RoutedEventArgs e)
        => await TryUnlockAsync();

    private async void UnlockPwd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await TryUnlockAsync();
    }

    private async Task TryUnlockAsync()
    {
        var pwd = UnlockPwd.Password;
        if (string.IsNullOrEmpty(pwd)) return;

        var ok = await _vault.UnlockAsync(pwd);
        if (!ok)
        {
            UnlockError.Text = LocalizationService.Current.T("vault.wrong.password");
            UnlockError.Visibility = Visibility.Visible;
            UnlockPwd.Clear();
            return;
        }

        await ShowVaultAsync();
    }

    private async Task ShowVaultAsync()
    {
        LockScreen.Visibility  = Visibility.Collapsed;
        VaultScreen.Visibility = Visibility.Visible;
        EditScreen.Visibility  = Visibility.Collapsed;
        await LoadEntriesAsync();
    }

    // ── Entry list ────────────────────────────────────────────────────────

    private async Task LoadEntriesAsync()
    {
        _allEntries = await _vault.GetAllAsync();
        RenderList(_allEntries);
        EntryCount.Text = $"({_allEntries.Count} entradas)";
    }

    private void RenderList(IEnumerable<PasswordEntry> entries)
    {
        EntryList.Children.Clear();

        foreach (var entry in entries)
        {
            var card = BuildEntryCard(entry);
            EntryList.Children.Add(card);
        }

        if (!EntryList.Children.Cast<UIElement>().Any())
        {
            EntryList.Children.Add(new TextBlock
            {
                Text       = LocalizationService.Current.T("vault.empty"),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin     = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private Border BuildEntryCard(PasswordEntry entry)
    {
        var card = new Border
        {
            Background      = (Brush)FindResource("BackgroundLightBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 0, 0, 6),
            Padding         = new Thickness(14, 10, 14, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: site name + username
        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text       = entry.SiteName,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        info.Children.Add(new TextBlock
        {
            Text       = entry.Username,
            FontSize   = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin     = new Thickness(0, 2, 0, 0)
        });
        info.Children.Add(new TextBlock
        {
            Text       = "••••••••",
            FontSize   = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin     = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Right: action buttons
        var actions = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            VerticalAlignment   = VerticalAlignment.Center
        };

        var L = LocalizationService.Current;
        var btnCopyUser = MakeActionButton("👤 " + L.T("vault.copy.user"), () => CopyToClipboard(entry.Username, L.T("vault.copied.user")));
        var btnCopyPwd  = MakeActionButton("🔑 " + L.T("vault.copy.pwd"),  () => CopyToClipboard(entry.Password, L.T("vault.copied.pwd")));
        var btnEdit     = MakeActionButton("✏", () => OpenEditor(entry));

        btnCopyUser.Margin = new Thickness(0, 0, 4, 0);
        btnCopyPwd.Margin  = new Thickness(0, 0, 4, 0);

        actions.Children.Add(btnCopyUser);
        actions.Children.Add(btnCopyPwd);
        actions.Children.Add(btnEdit);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private static Button MakeActionButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 11
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(q)
            ? _allEntries
            : _allEntries.Where(e =>
                e.SiteName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Url.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        RenderList(filtered);
    }

    // ── Add / Edit ────────────────────────────────────────────────────────

    private void Add_Click(object sender, RoutedEventArgs e)
        => OpenEditor(null);

    private void OpenEditor(PasswordEntry? entry)
    {
        _editingEntry = entry;
        ResetFieldErrors();

        var L = LocalizationService.Current;
        EditTitle.Text        = entry == null ? L.T("vault.form.new") : L.T("vault.form.edit");
        EditSiteName.Text     = entry?.SiteName ?? "";
        EditUrl.Text          = entry?.Url      ?? "";
        EditUsername.Text     = entry?.Username ?? "";
        EditPassword.Text     = entry?.Password ?? "";
        EditNotes.Text        = entry?.Notes    ?? "";
        DeleteBtn.Visibility  = entry == null ? Visibility.Collapsed : Visibility.Visible;
        GeneratorPanel.Visibility = Visibility.Collapsed;

        VaultScreen.Visibility = Visibility.Collapsed;
        EditScreen.Visibility  = Visibility.Visible;
        UpdateStrengthBar();
    }

    // ── Field validation helpers ──────────────────────────────────────────

    private static readonly SolidColorBrush _errorBrush = new(Color.FromRgb(0xEE, 0x55, 0x55));

    private void SetFieldError(TextBox field, TextBlock label)
    {
        field.BorderBrush = _errorBrush;
        label.Visibility  = Visibility.Visible;
    }

    private void ClearFieldError(TextBox field, TextBlock label)
    {
        field.ClearValue(TextBox.BorderBrushProperty);
        label.Visibility = Visibility.Collapsed;
    }

    private void RequiredField_Changed(object sender, TextChangedEventArgs e)
    {
        if (sender == EditSiteName)   ClearFieldError(EditSiteName,  SiteError);
        if (sender == EditUsername)   ClearFieldError(EditUsername,   UsernameError);
        if (sender == EditPassword)   ClearFieldError(EditPassword,   PasswordError);
    }

    private void ResetFieldErrors()
    {
        ClearFieldError(EditSiteName,  SiteError);
        ClearFieldError(EditUsername,  UsernameError);
        ClearFieldError(EditPassword,  PasswordError);
    }

    private async void SaveEntry_Click(object sender, RoutedEventArgs e)
    {
        var valid = true;
        if (string.IsNullOrWhiteSpace(EditSiteName.Text))  { SetFieldError(EditSiteName, SiteError);      valid = false; }
        if (string.IsNullOrWhiteSpace(EditUsername.Text))  { SetFieldError(EditUsername,  UsernameError);  valid = false; }
        if (string.IsNullOrWhiteSpace(EditPassword.Text))  { SetFieldError(EditPassword,  PasswordError);  valid = false; }
        if (!valid) return;

        var entry = new PasswordEntry
        {
            Id         = _editingEntry?.Id ?? Guid.NewGuid().ToString(),
            SiteName   = EditSiteName.Text.Trim(),
            Url        = EditUrl.Text.Trim(),
            Username   = EditUsername.Text.Trim(),
            Password   = EditPassword.Text,
            Notes      = string.IsNullOrWhiteSpace(EditNotes.Text) ? null : EditNotes.Text.Trim(),
            CreatedAt  = _editingEntry?.CreatedAt ?? DateTime.UtcNow
        };

        await _vault.SaveAsync(entry);
        await ShowVaultAsync();
        ShowStatus(_editingEntry == null ? "✓ Entrada guardada" : "✓ Entrada actualizada");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingEntry == null) return;

        var confirm = MessageBox.Show(
            $"¿Eliminar la entrada de {_editingEntry.SiteName}?",
            "VELO — Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await _vault.DeleteAsync(_editingEntry.Id);
        await ShowVaultAsync();
        ShowStatus("✓ Entrada eliminada");
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        EditScreen.Visibility  = Visibility.Collapsed;
        VaultScreen.Visibility = Visibility.Visible;
    }

    // ── Password generator ────────────────────────────────────────────────

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        GeneratorPanel.Visibility = Visibility.Visible;
        var length    = (int)LengthSlider.Value;
        var uppercase = GenUppercase.IsChecked == true;
        var numbers   = GenNumbers.IsChecked   == true;
        var symbols   = GenSymbols.IsChecked   == true;
        EditPassword.Text = VaultService.GeneratePassword(length, uppercase, numbers, symbols);
    }

    private void LengthSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LengthLabel == null) return;
        LengthLabel.Text = ((int)e.NewValue).ToString();
    }

    private void TogglePasswordVisible_Click(object sender, RoutedEventArgs e)
    {
        // EditPassword is a TextBox — toggle masking via font
        EditPassword.FontFamily = EditPassword.FontFamily.Source == "Courier New"
            ? new FontFamily("Segoe UI")
            : new FontFamily("Courier New");
    }

    // ── Strength bar ──────────────────────────────────────────────────────

    private void UpdateStrengthBar()
    {
        var pwd = EditPassword?.Text ?? "";
        var score = 0;
        if (pwd.Length >= 8)  score++;
        if (pwd.Length >= 16) score++;
        if (pwd.Any(char.IsUpper)) score++;
        if (pwd.Any(char.IsDigit)) score++;
        if (pwd.Any(c => !char.IsLetterOrDigit(c))) score++;

        var color = score <= 1 ? "#FF3D71"
                  : score <= 3 ? "#FFB300"
                  : "#7FFF5F";

        StrengthBar.Fill  = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        StrengthBar.Width = (StrengthBar.Parent as Grid)?.ActualWidth * score / 5.0 ?? 0;
    }

    // ── Lock ──────────────────────────────────────────────────────────────

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _vault.Lock();
        VaultScreen.Visibility = Visibility.Collapsed;
        LockScreen.Visibility  = Visibility.Visible;
        UnlockPwd.Clear();
        UnlockError.Visibility = Visibility.Collapsed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void CopyToClipboard(string text, string msg)
    {
        Clipboard.SetText(text);
        ShowStatus($"✓ {msg}");
        // Clear clipboard after 30s
        Task.Delay(30_000).ContinueWith(_ =>
            Dispatcher.Invoke(() =>
            {
                if (Clipboard.GetText() == text)
                    Clipboard.Clear();
            }));
    }

    private async void ShowStatus(string msg)
    {
        StatusText.Text = msg;
        await Task.Delay(2500);
        if (StatusText.Text == msg) StatusText.Text = "";
    }
}
