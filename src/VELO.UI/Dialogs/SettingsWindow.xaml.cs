using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VELO.Core.Localization;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Vault;

namespace VELO.UI.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly SettingsRepository _settings;
    private readonly VaultService _vault;

    // Track active nav button
    private Button? _activeNav;

    public SettingsWindow(SettingsRepository settings, VaultService vault)
    {
        _settings = settings;
        _vault = vault;
        InitializeComponent();

        // Wire up conditional enables
        AiOffline.Checked  += (_, _) => { ApiKeyField.IsEnabled = false; CustomEndpointField.IsEnabled = false; CustomModelField.IsEnabled = false; TestOllamaButton.IsEnabled = false; OllamaTestResult.Visibility = Visibility.Collapsed; };
        AiClaude.Checked   += (_, _) => { ApiKeyField.IsEnabled = true;  CustomEndpointField.IsEnabled = false; CustomModelField.IsEnabled = false; TestOllamaButton.IsEnabled = false; OllamaTestResult.Visibility = Visibility.Collapsed; };
        AiCustom.Checked   += (_, _) => { ApiKeyField.IsEnabled = false; CustomEndpointField.IsEnabled = true;  CustomModelField.IsEnabled = true;  TestOllamaButton.IsEnabled = true; };

        DnsCustom.Checked      += (_, _) => DnsCustomUrl.IsEnabled = true;
        DnsQuad9.Checked       += (_, _) => DnsCustomUrl.IsEnabled = false;
        DnsCloudflare.Checked  += (_, _) => DnsCustomUrl.IsEnabled = false;
        DnsNextDns.Checked     += (_, _) => DnsCustomUrl.IsEnabled = false;

        SearchCustom.Checked   += (_, _) => SearchCustomUrl.IsEnabled = true;
        SearchDDG.Checked      += (_, _) => SearchCustomUrl.IsEnabled = false;
        SearchBrave.Checked    += (_, _) => SearchCustomUrl.IsEnabled = false;
        SearchSearx.Checked    += (_, _) => SearchCustomUrl.IsEnabled = false;

        _activeNav = NavPrivacidad;
        Loaded += async (_, _) =>
        {
            await LoadSettingsAsync();
            LoadLanguagePicker();
            ApplyNavLanguage();
        };
        LocalizationService.Current.LanguageChanged += ApplyNavLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyNavLanguage;
    }

    private void LoadLanguagePicker()
    {
        LanguagePicker.ItemsSource = LocalizationService.Languages
            .Select(kv => new LanguageItem(kv.Key, kv.Value))
            .ToList();
        LanguagePicker.DisplayMemberPath = nameof(LanguageItem.Display);
        LanguagePicker.SelectedValuePath = nameof(LanguageItem.Key);
        LanguagePicker.SelectedValue = LocalizationService.Current.Language;
    }

    private sealed record LanguageItem(string Key, string Display);

    private void ApplyNavLanguage()
    {
        var L = LocalizationService.Current;
        NavPrivacidad.Content = L.T("nav.privacy");
        NavIA.Content         = L.T("nav.ai");
        NavBusqueda.Content   = L.T("nav.search");
        NavIdioma.Content     = L.T("nav.language");
        LangTitle.Text        = L.T("lang.title");
        LangSubtitle.Text     = L.T("lang.subtitle");
        LangChooseLabel.Text  = L.T("lang.choose");
        Title                 = L.T("title.settings");
    }

    private bool _langPickerLoaded;

    private async void LanguagePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_langPickerLoaded) { _langPickerLoaded = true; return; } // skip initial load event
        if (LanguagePicker.SelectedValue is not string lang) return;
        LocalizationService.Current.SetLanguage(lang);
        await _settings.SetAsync(SettingKeys.Language, lang);
    }

    private async Task LoadSettingsAsync()
    {
        // Security mode
        var secMode = await _settings.GetAsync(SettingKeys.SecurityMode, "Normal");
        SecNormal.IsChecked   = secMode == "Normal";
        SecParanoid.IsChecked = secMode == "Paranoid";
        SecBunker.IsChecked   = secMode == "Bunker";

        // Fingerprint
        var fp = await _settings.GetAsync(SettingKeys.FingerprintLevel, "Aggressive");
        FpAggressive.IsChecked = fp == "Aggressive";
        FpBalanced.IsChecked   = fp == "Balanced";
        FpOff.IsChecked        = fp == "Off";

        // WebRTC
        var webrtc = await _settings.GetAsync(SettingKeys.WebRtcMode, "Relay");
        WrtcRelay.IsChecked    = webrtc == "Relay";
        WrtcDisabled.IsChecked = webrtc == "Disabled";
        WrtcOff.IsChecked      = webrtc == "Off";

        // History / clear on exit
        HistoryCheck.IsChecked    = await _settings.GetBoolAsync(SettingKeys.HistoryEnabled, true);
        ClearOnExitCheck.IsChecked = await _settings.GetBoolAsync(SettingKeys.ClearOnExit, false);

        // DNS
        var dns = await _settings.GetAsync(SettingKeys.DnsProvider, "Quad9");
        DnsQuad9.IsChecked      = dns == "Quad9";
        DnsCloudflare.IsChecked = dns == "Cloudflare";
        DnsNextDns.IsChecked    = dns == "NextDNS";
        DnsCustom.IsChecked     = dns == "Custom";
        DnsCustomUrl.Text       = await _settings.GetAsync(SettingKeys.DnsCustomUrl, "");
        DnsCustomUrl.IsEnabled  = dns == "Custom";

        // AI
        var aiMode = await _settings.GetAsync(SettingKeys.AiMode, "Offline");
        AiOffline.IsChecked = aiMode == "Offline";
        AiClaude.IsChecked  = aiMode == "Claude";
        AiCustom.IsChecked  = aiMode == "Custom";
        ApiKeyField.Text          = await _settings.GetAsync(SettingKeys.AiApiKey, "");
        CustomEndpointField.Text  = await _settings.GetAsync(SettingKeys.AiCustomEndpoint, "");
        CustomModelField.Text     = await _settings.GetAsync(SettingKeys.AiClaudeModel, "");
        ApiKeyField.IsEnabled         = aiMode == "Claude";
        CustomEndpointField.IsEnabled = aiMode == "Custom";
        CustomModelField.IsEnabled    = aiMode == "Custom";
        TestOllamaButton.IsEnabled    = aiMode == "Custom";

        // Search
        var search = await _settings.GetAsync(SettingKeys.SearchEngine, "DuckDuckGo");
        SearchDDG.IsChecked    = search == "DuckDuckGo";
        SearchBrave.IsChecked  = search == "BraveSearch";
        SearchSearx.IsChecked  = search == "SearxNG";
        SearchCustom.IsChecked = search == "Custom";
        SearchCustomUrl.Text   = await _settings.GetAsync(SettingKeys.SearchCustomUrl, "");
        SearchCustomUrl.IsEnabled = search == "Custom";

        // Vault auto-lock
        var autoLock = await _settings.GetIntAsync(SettingKeys.VaultAutoLockMinutes, 15);
        foreach (ComboBoxItem item in AutoLockCombo.Items)
        {
            if (item.Tag?.ToString() == autoLock.ToString())
            {
                AutoLockCombo.SelectedItem = item;
                break;
            }
        }
        if (AutoLockCombo.SelectedItem == null)
            AutoLockCombo.SelectedIndex = 2; // 15 min default
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // Security
        var secMode = SecParanoid.IsChecked == true ? "Paranoid"
                    : SecBunker.IsChecked   == true ? "Bunker"
                    : "Normal";
        await _settings.SetAsync(SettingKeys.SecurityMode, secMode);

        // Fingerprint
        var fp = FpBalanced.IsChecked == true ? "Balanced"
               : FpOff.IsChecked      == true ? "Off"
               : "Aggressive";
        await _settings.SetAsync(SettingKeys.FingerprintLevel, fp);

        // WebRTC
        var webrtc = WrtcDisabled.IsChecked == true ? "Disabled"
                   : WrtcOff.IsChecked      == true ? "Off"
                   : "Relay";
        await _settings.SetAsync(SettingKeys.WebRtcMode, webrtc);

        await _settings.SetBoolAsync(SettingKeys.HistoryEnabled, HistoryCheck.IsChecked == true);
        await _settings.SetBoolAsync(SettingKeys.ClearOnExit, ClearOnExitCheck.IsChecked == true);

        // DNS
        var dns = DnsCloudflare.IsChecked == true ? "Cloudflare"
                : DnsNextDns.IsChecked    == true ? "NextDNS"
                : DnsCustom.IsChecked     == true ? "Custom"
                : "Quad9";
        await _settings.SetAsync(SettingKeys.DnsProvider, dns);
        if (dns == "Custom")
            await _settings.SetAsync(SettingKeys.DnsCustomUrl, DnsCustomUrl.Text.Trim());

        // AI
        var ai = AiClaude.IsChecked == true ? "Claude"
               : AiCustom.IsChecked == true ? "Custom"
               : "Offline";
        await _settings.SetAsync(SettingKeys.AiMode, ai);
        if (ai == "Claude")
            await _settings.SetAsync(SettingKeys.AiApiKey, ApiKeyField.Text.Trim());
        if (ai == "Custom")
        {
            await _settings.SetAsync(SettingKeys.AiCustomEndpoint, CustomEndpointField.Text.Trim());
            await _settings.SetAsync(SettingKeys.AiClaudeModel, CustomModelField.Text.Trim());
        }

        // Search
        var eng = SearchBrave.IsChecked  == true ? "BraveSearch"
                : SearchSearx.IsChecked  == true ? "SearxNG"
                : SearchCustom.IsChecked == true ? "Custom"
                : "DuckDuckGo";
        await _settings.SetAsync(SettingKeys.SearchEngine, eng);
        if (eng == "Custom")
            await _settings.SetAsync(SettingKeys.SearchCustomUrl, SearchCustomUrl.Text.Trim());

        // Vault auto-lock
        if (AutoLockCombo.SelectedItem is ComboBoxItem lockItem && lockItem.Tag != null)
            await _settings.SetIntAsync(SettingKeys.VaultAutoLockMinutes, int.Parse(lockItem.Tag.ToString()!));

        StatusText.Text = "✓ Guardado";
        await Task.Delay(1500);
        StatusText.Text = "";
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var pwd     = NewMasterPwd.Password;
        var confirm = ConfirmMasterPwd.Password;

        if (pwd.Length < 8)
        {
            ShowVaultError("Mínimo 8 caracteres.");
            return;
        }
        if (pwd != confirm)
        {
            ShowVaultError("Las passwords no coinciden.");
            return;
        }

        VaultError.Visibility = Visibility.Collapsed;
        await _vault.InitializeAsync(pwd);
        NewMasterPwd.Clear();
        ConfirmMasterPwd.Clear();
        StatusText.Text = "✓ Master password actualizada";
        await Task.Delay(2000);
        StatusText.Text = "";
    }

    private void ShowVaultError(string msg)
    {
        VaultError.Text = msg;
        VaultError.Visibility = Visibility.Visible;
    }

    private async void OnTestOllamaClick(object sender, RoutedEventArgs e)
    {
        var endpoint = CustomEndpointField.Text.TrimEnd('/');
        var model    = CustomModelField.Text.Trim();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            ShowOllamaResult(false, "Completa el endpoint y el modelo antes de probar.");
            return;
        }

        TestOllamaButton.IsEnabled  = false;
        TestOllamaButton.Content    = "Probando…";
        OllamaTestResult.Visibility = Visibility.Collapsed;

        try
        {
            // Step 1: quick ping via OpenAI-compatible /v1/models (works with Ollama, LM Studio, llama.cpp)
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                var ping = await http.GetAsync($"{endpoint}/v1/models");
                if (!ping.IsSuccessStatusCode)
                {
                    ShowOllamaResult(false, $"El servidor responde en {endpoint} pero con error {(int)ping.StatusCode}.\n" +
                        "Comprueba que el servidor esté iniciado y el endpoint sea correcto (p.ej. http://127.0.0.1:11434 para Ollama, http://127.0.0.1:1234 para LM Studio).");
                    return;
                }

                // Check if the model is listed in the response
                var modelsJson = await ping.Content.ReadAsStringAsync();
                if (!modelsJson.Contains(model.Split(':')[0]))
                {
                    ShowOllamaResult(false, $"Servidor activo ✓, pero el modelo '{model}' no aparece en la lista.\n" +
                        "Asegúrate de haber cargado el modelo en LM Studio o ejecuta: ollama pull {model}");
                    return;
                }
            }
            catch
            {
                ShowOllamaResult(false, $"No se pudo conectar a {endpoint}.\n" +
                    "• Ollama: abre una terminal y ejecuta ollama serve\n" +
                    "• LM Studio: activa el servidor local desde la pestaña Developer");
                return;
            }

            // Step 2: inference test via OpenAI-compatible /v1/chat/completions
            TestOllamaButton.Content = "Cargando modelo…";
            // 120s: thinking models (Qwen3, DeepSeek-R1) need extra time for the reasoning phase
            using var http2 = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

            var body = JsonSerializer.Serialize(new
            {
                model,
                stream          = false,
                max_tokens      = 256,
                temperature     = 0,
                enable_thinking = false,   // skip chain-of-thought for thinking models
                messages        = new[]
                {
                    new { role = "user", content = "Reply with exactly: OK" }
                }
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await http2.PostAsync(
                $"{endpoint}/v1/chat/completions",
                new StringContent(body, Encoding.UTF8, "application/json"));
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                ShowOllamaResult(false, $"Error HTTP {(int)response.StatusCode} al generar respuesta con '{model}'.");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);
            var msgEl = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            // content may be empty for thinking models (reasoning fills the budget)
            var text = msgEl.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text) &&
                msgEl.TryGetProperty("reasoning_content", out var rc))
                text = "[Thinking model — reasoning OK]";

            ShowOllamaResult(true,
                $"✓ Conectado · Modelo '{model}' listo\n" +
                $"Tiempo de respuesta: {sw.ElapsedMilliseconds} ms\n" +
                $"Respuesta: {text.Trim()[..Math.Min(text.Trim().Length, 60)]}");
        }
        catch (TaskCanceledException)
        {
            ShowOllamaResult(false,
                $"Timeout (120s) — el modelo tardó demasiado en responder.\n" +
                $"Si usas un modelo grande, asegúrate de que esté cargado en LM Studio.\n" +
                $"También puedes probar con un modelo más pequeño.");
        }
        catch (Exception ex)
        {
            ShowOllamaResult(false, $"Error inesperado: {ex.Message}");
        }
        finally
        {
            TestOllamaButton.IsEnabled = true;
            TestOllamaButton.Content   = "Probar conexión";
        }
    }

    private void ShowOllamaResult(bool success, string message)
    {
        OllamaTestResult.Text       = (success ? "✓ " : "✗ ") + message;
        OllamaTestResult.Foreground = success
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))   // green
            : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));   // red
        OllamaTestResult.Visibility = Visibility.Visible;
    }

    private void SetDefaultBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps?registeredAppUser=VELO",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback a la pantalla genérica de apps predeterminadas
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:defaultapps",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DefaultBrowserStatus.Text = $"No se pudo abrir Configuración de Windows: {ex.Message}";
                DefaultBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                DefaultBrowserStatus.Visibility = Visibility.Visible;
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        NavPrivacidad.Style = (Style)Resources["NavButton"];
        NavDns.Style        = (Style)Resources["NavButton"];
        NavIA.Style         = (Style)Resources["NavButton"];
        NavBusqueda.Style   = (Style)Resources["NavButton"];
        NavVault.Style      = (Style)Resources["NavButton"];
        NavIdioma.Style     = (Style)Resources["NavButton"];
        NavGeneral.Style    = (Style)Resources["NavButton"];

        btn.Style = (Style)Resources["NavButtonActive"];
        _activeNav = btn;

        var tag = btn.Tag?.ToString();
        PanelPrivacidad.Visibility = tag == "Privacidad" ? Visibility.Visible : Visibility.Collapsed;
        PanelDns.Visibility        = tag == "DNS"        ? Visibility.Visible : Visibility.Collapsed;
        PanelIA.Visibility         = tag == "IA"         ? Visibility.Visible : Visibility.Collapsed;
        PanelBusqueda.Visibility   = tag == "Busqueda"   ? Visibility.Visible : Visibility.Collapsed;
        PanelVault.Visibility      = tag == "Vault"      ? Visibility.Visible : Visibility.Collapsed;
        PanelIdioma.Visibility     = tag == "Idioma"     ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility    = tag == "General"    ? Visibility.Visible : Visibility.Collapsed;
    }
}
