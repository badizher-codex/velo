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

    /// <summary>v2.4.40 — true while LoadCouncilStateAsync is running; suppresses
    /// the suggest-defaults logic in <see cref="CouncilBackend_Checked"/> so loading
    /// the user's existing backend choice doesn't overwrite their endpoint/model.</summary>
    private bool _loadingCouncilState;

    /// <summary>v2.4.25 — Raised after Save when the user toggles the RDAP
    /// domain-age check so the host can flip DomainAgeProbe.Enabled live.</summary>
    public event EventHandler<bool>? DomainAgeCheckChanged;

    /// <summary>v2.4.53 — Raised after Save when the user toggles the YouTube
    /// ad-block opt-out so the host can refresh YouTubeAdBlocker.IsEnabled. New
    /// tabs pick up the new value via the cached flag; existing YouTube tabs
    /// need a refresh (script-on-document-created fires once per webview).</summary>
    public event EventHandler<bool>? YouTubeAdBlockChanged;

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
        // Window + nav rail
        Title                 = L.T("title.settings");
        NavPrivacidad.Content = L.T("nav.privacy");
        NavDns.Content        = L.T("nav.dns");
        NavIA.Content         = L.T("nav.ai");
        NavBusqueda.Content   = L.T("nav.search");
        NavVault.Content      = L.T("nav.vault");
        NavIdioma.Content     = L.T("nav.language");
        NavGeneral.Content    = L.T("nav.general");

        // Privacy panel
        PrivacyTitle.Text     = L.T("settings.privacy.title");
        SecModeTitle.Text     = L.T("settings.security.label");
        SecNormalTitle.Text   = L.T("settings.secmode.normal");
        SecNormalDesc.Text    = L.T("settings.secmode.normal.desc");
        SecParanoidTitle.Text = L.T("settings.secmode.paranoid");
        SecParanoidDesc.Text  = L.T("settings.secmode.paranoid.desc");
        SecBunkerTitle.Text   = L.T("settings.secmode.bunker");
        SecBunkerDesc.Text    = L.T("settings.secmode.bunker.desc");
        FpTitle.Text          = L.T("settings.fp.title");
        FpAggressiveTitle.Text= L.T("settings.fp.aggressive");
        FpAggressiveDesc.Text = L.T("settings.fp.aggressive.desc");
        FpBalancedTitle.Text  = L.T("settings.fp.balanced");
        FpBalancedDesc.Text   = L.T("settings.fp.balanced.desc");
        FpOffLabel.Text       = L.T("settings.fp.off");
        WrtcTitle.Text        = L.T("settings.webrtc.title");
        WrtcRelayTitle.Text   = L.T("settings.webrtc.relay");
        WrtcRelayDesc.Text    = L.T("settings.webrtc.relay.desc");
        WrtcDisabledTitle.Text= L.T("settings.webrtc.disabled");
        WrtcDisabledDesc.Text = L.T("settings.webrtc.disabled.desc");
        WrtcOffLabel.Text     = L.T("settings.webrtc.off");
        HistorySaveTitle.Text = L.T("settings.history.save");
        HistorySaveDesc.Text  = L.T("settings.history.save.desc");
        HistoryClearTitle.Text= L.T("settings.history.clear");
        HistoryClearDesc.Text = L.T("settings.history.clear.desc");
        AutoUpdateTitle.Text  = L.T("settings.update.auto");
        AutoUpdateDesc.Text   = L.T("settings.update.auto.desc");

        // DNS panel
        DnsTitle.Text         = L.T("settings.dns.title");
        DnsIntro.Text         = L.T("settings.dns.intro");
        DnsQuad9Title.Text    = L.T("settings.dns.quad9");
        DnsQuad9Desc.Text     = L.T("settings.dns.quad9.desc");
        DnsCloudflareDesc.Text= L.T("settings.dns.cloudflare.desc");
        DnsNextDnsDesc.Text   = L.T("settings.dns.nextdns.desc");
        DnsCustomLabel.Text   = L.T("settings.dns.custom");
        DnsUrlLabel.Text      = L.T("settings.dns.url");

        // AI panel
        AiTitle.Text          = L.T("settings.ai.title");
        AiIntro.Text          = L.T("settings.ai.intro");
        AiOfflineTitle.Text   = L.T("settings.ai.offline");
        AiOfflineDesc.Text    = L.T("settings.ai.offline.desc");
        AiClaudeTitle.Text    = L.T("settings.ai.claude");
        AiClaudeDesc.Text     = L.T("settings.ai.claude.desc");
        AiApiKeyLabel.Text    = L.T("settings.ai.apikey");
        AiCustomTitle.Text    = L.T("settings.ai.custom");
        AiCustomDesc.Text     = L.T("settings.ai.custom.desc");
        AiEndpointLabel.Text  = L.T("settings.ai.endpoint");
        AiModelLabel.Text     = L.T("settings.ai.model");
        TestOllamaButton.Content = L.T("settings.ai.test");
        BookmarkAutoTagTitle.Text = L.T("settings.ai.bookmark_autotag");
        BookmarkAutoTagDesc.Text  = L.T("settings.ai.bookmark_autotag.desc");
        DomainAgeTitle.Text       = L.T("settings.ai.domain_age");
        DomainAgeDesc.Text        = L.T("settings.ai.domain_age.desc");

        // Search panel
        SearchTitle.Text       = L.T("settings.search.title");
        SearchDdgTitle.Text    = L.T("settings.search.ddg");
        SearchDdgDesc.Text     = L.T("settings.search.ddg.desc");
        SearchBraveDesc.Text   = L.T("settings.search.brave.desc");
        SearchSearxDesc.Text   = L.T("settings.search.searx.desc");
        SearchCustomLabel.Text = L.T("settings.dns.custom");
        SearchUrlHint.Text     = L.T("settings.search.url_hint");

        // Vault panel
        VaultPanelTitle.Text   = L.T("onboarding.s4.title");
        VaultIntro.Text        = L.T("settings.vault.intro");
        AutoLockLabel.Text     = L.T("settings.vault.autolock");
        AutoLock5.Content      = L.T("settings.vault.5m");
        AutoLock10.Content     = L.T("settings.vault.10m");
        AutoLock15.Content     = L.T("settings.vault.15m");
        AutoLock30.Content     = L.T("settings.vault.30m");
        AutoLock60.Content     = L.T("settings.vault.1h");
        AutoLockNever.Content  = L.T("settings.vault.never");
        ChangePassTitle.Text   = L.T("settings.vault.changepass");
        NewPassLabel.Text      = L.T("settings.vault.newpass");
        ConfirmPassLabel.Text  = L.T("settings.vault.confirm");
        ChangePassBtn.Content  = L.T("settings.vault.changebtn");

        // General panel
        GeneralTitle.Text         = L.T("settings.general.title");
        DefBrowserTitle.Text      = L.T("settings.defbrowser.title");
        DefBrowserHelp.Text       = L.T("settings.defbrowser.help");
        SetDefaultBrowserButton.Content = L.T("settings.defbrowser.btn");
        ClipboardHistoryTitle.Text = L.T("settings.clipboard.title");
        ClipboardHistoryDesc.Text  = L.T("settings.clipboard.desc");

        // Language panel + bottom bar
        LangTitle.Text         = L.T("lang.title");
        LangSubtitle.Text      = L.T("lang.subtitle");
        LangChooseLabel.Text   = L.T("lang.choose");
        CancelButton.Content   = L.T("settings.cancel");
        SaveButton.Content     = L.T("settings.save");
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
        AutoUpdateCheck.IsChecked  = await _settings.GetBoolAsync("updates.auto_check", false);

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

        // v2.4.18 — Sprint 9B: BookmarkAI auto-tag
        BookmarkAutoTagCheck.IsChecked = await _settings.GetBoolAsync(SettingKeys.BookmarkAutoTag, defaultValue: true);

        // v2.4.23 — Clipboard history
        ClipboardHistoryCheck.IsChecked = await _settings.GetBoolAsync(SettingKeys.ClipboardHistoryEnabled, defaultValue: false);

        // v2.4.25 — PhishingShield domain-age (RDAP)
        DomainAgeCheck.IsChecked = await _settings.GetBoolAsync(SettingKeys.PhishingShieldDomainAgeCheck, defaultValue: false);

        // v2.4.53 — YouTube ad-block opt-out. String setting ("yes"/"no") so we go
        // through GetAsync, not GetBoolAsync, to keep parity with the rest of the
        // Council/Council* "yes"/"no" string convention.
        var ytRaw = await _settings.GetAsync(SettingKeys.YouTubeAdsBlocked, "yes");
        YouTubeAdBlockCheck.IsChecked = string.Equals(ytRaw, "yes", StringComparison.OrdinalIgnoreCase);

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
        await _settings.SetBoolAsync("updates.auto_check", AutoUpdateCheck.IsChecked == true);

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

        // v2.4.39/4.40 — Council backend selection + endpoint + moderator model name.
        // Independent from Custom AI Mode (which can legitimately point at LM Studio
        // or another non-Ollama server). Persisted unconditionally so they survive
        // even if the user is on Offline AI Mode.
        var councilBackend =
            CouncilBackendLmStudioRadio.IsChecked == true
                ? nameof(VELO.Core.AI.CouncilPreflightService.Backend.LMStudio)
            : CouncilBackendOtherRadio.IsChecked == true
                ? nameof(VELO.Core.AI.CouncilPreflightService.Backend.OpenAICompat)
                : nameof(VELO.Core.AI.CouncilPreflightService.Backend.Ollama);
        await _settings.SetAsync(SettingKeys.CouncilBackendType, councilBackend);

        var councilEndpoint = CouncilOllamaEndpointBox.Text.Trim();
        if (councilEndpoint.Length == 0)
            councilEndpoint = VELO.Core.AI.CouncilPreflightService.DefaultOllamaEndpoint;
        await _settings.SetAsync(SettingKeys.CouncilOllamaEndpoint, councilEndpoint);

        var councilModel = CouncilModeratorModelBox.Text.Trim();
        if (councilModel.Length == 0)
            councilModel = VELO.Core.AI.CouncilPreflightService.DefaultModeratorModel;
        await _settings.SetAsync(SettingKeys.CouncilModeratorModel, councilModel);

        // v2.4.54 — CRITICAL HOTFIX: persist the 4 Council provider toggles.
        // From Phase 4.0 chunk H (v2.4.38) through v2.4.53 these toggles
        // were READ in LoadCouncilStateAsync but NEVER WRITTEN here. Users
        // who ticked them and clicked Save saw "Activá al menos un proveedor"
        // when invoking Council Mode because OpenCouncilModeAsync read all
        // four as "no". Convention follows the existing CouncilDisclaimerAccepted
        // pattern: string "yes"/"no" (NOT SetBoolAsync — keep parity with
        // GetCouncilBoolAsync below that compares against "yes").
        await _settings.SetAsync(SettingKeys.CouncilEnabledClaude,
            CouncilEnabledClaudeCheck.IsChecked == true ? "yes" : "no");
        await _settings.SetAsync(SettingKeys.CouncilEnabledChatGpt,
            CouncilEnabledChatGptCheck.IsChecked == true ? "yes" : "no");
        await _settings.SetAsync(SettingKeys.CouncilEnabledGrok,
            CouncilEnabledGrokCheck.IsChecked == true ? "yes" : "no");
        await _settings.SetAsync(SettingKeys.CouncilEnabledOllama,
            CouncilEnabledOllamaCheck.IsChecked == true ? "yes" : "no");

        // v2.4.18 — Sprint 9B: BookmarkAI auto-tag
        await _settings.SetBoolAsync(SettingKeys.BookmarkAutoTag, BookmarkAutoTagCheck.IsChecked == true);

        // v2.4.23 — Clipboard history (applies on next VELO restart)
        await _settings.SetBoolAsync(SettingKeys.ClipboardHistoryEnabled, ClipboardHistoryCheck.IsChecked == true);

        // v2.4.25 — PhishingShield domain-age (RDAP). Persist + apply hot.
        await _settings.SetBoolAsync(SettingKeys.PhishingShieldDomainAgeCheck, DomainAgeCheck.IsChecked == true);
        DomainAgeCheckChanged?.Invoke(this, DomainAgeCheck.IsChecked == true);

        // v2.4.53 — YouTube ad-block opt-out. Persist as "yes"/"no" string.
        // Hot-apply: new tabs see the new value; existing YouTube tabs need a
        // refresh to pick it up (script-on-document-created fires once per
        // webview lifetime).
        await _settings.SetAsync(SettingKeys.YouTubeAdsBlocked,
            YouTubeAdBlockCheck.IsChecked == true ? "yes" : "no");
        YouTubeAdBlockChanged?.Invoke(this, YouTubeAdBlockCheck.IsChecked == true);

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

        StatusText.Text = LocalizationService.Current.T("settings.saved");
        await Task.Delay(1500);
        StatusText.Text = "";
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var pwd     = NewMasterPwd.Password;
        var confirm = ConfirmMasterPwd.Password;
        var L       = LocalizationService.Current;

        if (pwd.Length < 8)
        {
            ShowVaultError(L.T("settings.vault.min8"));
            return;
        }
        if (pwd != confirm)
        {
            ShowVaultError(L.T("settings.vault.mismatch"));
            return;
        }

        VaultError.Visibility = Visibility.Collapsed;
        await _vault.InitializeAsync(pwd);
        NewMasterPwd.Clear();
        ConfirmMasterPwd.Clear();
        StatusText.Text = L.T("settings.vault.updated");
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
        var L        = LocalizationService.Current;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            ShowOllamaResult(false, L.T("settings.ai.fill_first"));
            return;
        }

        TestOllamaButton.IsEnabled  = false;
        TestOllamaButton.Content    = L.T("settings.ai.testing");
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
                    ShowOllamaResult(false, string.Format(L.T("settings.ai.server_error"), endpoint, (int)ping.StatusCode));
                    return;
                }

                // Check if the model is listed in the response
                var modelsJson = await ping.Content.ReadAsStringAsync();
                if (!modelsJson.Contains(model.Split(':')[0]))
                {
                    ShowOllamaResult(false, string.Format(L.T("settings.ai.model_missing"), model));
                    return;
                }
            }
            catch
            {
                ShowOllamaResult(false, string.Format(L.T("settings.ai.cant_connect"), endpoint));
                return;
            }

            // Step 2: inference test via OpenAI-compatible /v1/chat/completions
            TestOllamaButton.Content = L.T("settings.ai.loading_model");
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
                ShowOllamaResult(false, string.Format(L.T("settings.ai.http_error"), (int)response.StatusCode, model));
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

            ShowOllamaResult(true, string.Format(L.T("settings.ai.success"),
                model, sw.ElapsedMilliseconds,
                text.Trim()[..Math.Min(text.Trim().Length, 60)]));
        }
        catch (TaskCanceledException)
        {
            ShowOllamaResult(false, L.T("settings.ai.timeout"));
        }
        catch (Exception ex)
        {
            ShowOllamaResult(false, string.Format(L.T("settings.ai.unexpected"), ex.Message));
        }
        finally
        {
            TestOllamaButton.IsEnabled = true;
            TestOllamaButton.Content   = L.T("settings.ai.test");
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
                DefaultBrowserStatus.Text = string.Format(LocalizationService.Current.T("settings.defbrowser.openerr"), ex.Message);
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
        NavCouncil.Style    = (Style)Resources["NavButton"];
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
        PanelCouncil.Visibility    = tag == "Council"    ? Visibility.Visible : Visibility.Collapsed;
        PanelIdioma.Visibility     = tag == "Idioma"     ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility    = tag == "General"    ? Visibility.Visible : Visibility.Collapsed;

        // Phase 4.0 chunk H — load Council settings the first time the
        // panel is opened. Toggles are read-only (disabled until v2.5.x);
        // the load just reflects whatever the disclaimer wrote so the user
        // can see what's currently opted in.
        if (tag == "Council") _ = LoadCouncilStateAsync();
    }

    // ── Council Mode panel (Phase 4.0 chunk H) ───────────────────────────

    private async Task LoadCouncilStateAsync()
    {
        _loadingCouncilState = true;
        try
        {
            // v2.4.40 — backend selector + model name (independent from Custom AI Mode).
            var backendRaw = await _settings.GetAsync(
                SettingKeys.CouncilBackendType,
                nameof(VELO.Core.AI.CouncilPreflightService.Backend.Ollama));
            switch (backendRaw)
            {
                case nameof(VELO.Core.AI.CouncilPreflightService.Backend.LMStudio):
                    CouncilBackendLmStudioRadio.IsChecked = true; break;
                case nameof(VELO.Core.AI.CouncilPreflightService.Backend.OpenAICompat):
                    CouncilBackendOtherRadio.IsChecked = true; break;
                default:
                    CouncilBackendOllamaRadio.IsChecked = true; break;
            }

            CouncilOllamaEndpointBox.Text = await _settings.GetAsync(
                SettingKeys.CouncilOllamaEndpoint,
                VELO.Core.AI.CouncilPreflightService.DefaultOllamaEndpoint);
            CouncilModeratorModelBox.Text = await _settings.GetAsync(
                SettingKeys.CouncilModeratorModel,
                VELO.Core.AI.CouncilPreflightService.DefaultModeratorModel);
        }
        finally
        {
            _loadingCouncilState = false;
        }

        CouncilEnabledClaudeCheck.IsChecked  = await GetCouncilBoolAsync(SettingKeys.CouncilEnabledClaude);
        CouncilEnabledChatGptCheck.IsChecked = await GetCouncilBoolAsync(SettingKeys.CouncilEnabledChatGpt);
        CouncilEnabledGrokCheck.IsChecked    = await GetCouncilBoolAsync(SettingKeys.CouncilEnabledGrok);
        CouncilEnabledOllamaCheck.IsChecked  = await GetCouncilBoolAsync(SettingKeys.CouncilEnabledOllama);

        var accepted = (await _settings.GetAsync(SettingKeys.CouncilDisclaimerAccepted, "no")) == "yes";
        CouncilDisclaimerStatus.Text = accepted
            ? "Disclaimer aceptado. Council se abrirá sin volver a preguntar."
            : "Disclaimer pendiente — se mostrará la primera vez que abras Council.";
    }

    /// <summary>
    /// v2.4.40 — When the user picks a backend radio, suggest the default endpoint+model
    /// for that backend if the user hasn't typed anything yet. Never overwrites a non-empty
    /// value the user has entered manually — we only fill blanks or the previous default.
    /// </summary>
    private void CouncilBackend_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingCouncilState) return;
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        (string defaultEndpoint, string defaultModel) = tag switch
        {
            nameof(VELO.Core.AI.CouncilPreflightService.Backend.LMStudio) =>
                ("http://localhost:1234", "qwen3.6-35b-a3b"),
            nameof(VELO.Core.AI.CouncilPreflightService.Backend.OpenAICompat) =>
                ("http://localhost:8000", "qwen3:32b"),
            _ =>
                ("http://localhost:11434", "qwen3:32b"),
        };

        // Only repopulate when the box is empty or holds a *different* known default —
        // this gives the user fresh suggestions when switching backends without wiping
        // values they explicitly typed.
        if (IsBlankOrKnownDefault(CouncilOllamaEndpointBox.Text))
            CouncilOllamaEndpointBox.Text = defaultEndpoint;
        if (IsBlankOrKnownDefault(CouncilModeratorModelBox.Text))
            CouncilModeratorModelBox.Text = defaultModel;
    }

    private static bool IsBlankOrKnownDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value is "http://localhost:11434" or "http://localhost:1234" or "http://localhost:8000"
            or "qwen3:32b" or "qwen3.6-35b-a3b";
    }

    private async Task<bool> GetCouncilBoolAsync(string key)
        => (await _settings.GetAsync(key, "no")) == "yes";

    private async void CheckCouncilStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn) btn.IsEnabled = false;
        CouncilStatusIcon.Text   = "⏳";
        CouncilStatusText.Text   = "Comprobando servidor y modelo…";

        try
        {
            // Persist the current textbox values first so the preflight reads
            // the freshly-typed configuration, not whatever was loaded at panel open.
            var backend =
                CouncilBackendLmStudioRadio.IsChecked == true
                    ? nameof(VELO.Core.AI.CouncilPreflightService.Backend.LMStudio)
                : CouncilBackendOtherRadio.IsChecked == true
                    ? nameof(VELO.Core.AI.CouncilPreflightService.Backend.OpenAICompat)
                    : nameof(VELO.Core.AI.CouncilPreflightService.Backend.Ollama);
            await _settings.SetAsync(SettingKeys.CouncilBackendType, backend);

            var endpoint = CouncilOllamaEndpointBox.Text.Trim();
            if (endpoint.Length == 0)
                endpoint = VELO.Core.AI.CouncilPreflightService.DefaultOllamaEndpoint;
            await _settings.SetAsync(SettingKeys.CouncilOllamaEndpoint, endpoint);

            var model = CouncilModeratorModelBox.Text.Trim();
            if (model.Length == 0)
                model = VELO.Core.AI.CouncilPreflightService.DefaultModeratorModel;
            await _settings.SetAsync(SettingKeys.CouncilModeratorModel, model);

            // CouncilPreflightService construction is cheap — local HttpClient,
            // reads endpoint+backend+model from Settings each call. Constructed on
            // demand so we don't drag the dependency into SettingsWindow's ctor.
            var preflight = new VELO.Core.AI.CouncilPreflightService(_settings);
            var result    = await preflight.CheckAsync();

            if (result.IsHealthy)
            {
                CouncilStatusIcon.Text = "✓";
                CouncilStatusText.Text = result.ContextSize.HasValue
                    ? $"Servidor listo: {result.ModelName} cargado ({result.ContextSize.Value} tokens)."
                    : $"Servidor listo: {result.ModelName} detectado.";
            }
            else
            {
                CouncilStatusIcon.Text = result.EndpointReachable ? "⚠" : "✕";
                CouncilStatusText.Text = result.FailureReason ?? "Servidor no está disponible.";
            }
        }
        finally
        {
            if (sender is Button b) b.IsEnabled = true;
        }
    }

    private async void ResetCouncilDisclaimer_Click(object sender, RoutedEventArgs e)
    {
        await _settings.SetAsync(SettingKeys.CouncilDisclaimerAccepted, "no");
        await LoadCouncilStateAsync();
    }
}
