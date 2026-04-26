using System.Windows;
using System.Windows.Controls;
using VELO.Core.Localization;
using VELO.Data.Models;
using VELO.Data.Repositories;
using VELO.Vault;

namespace VELO.UI.Dialogs;

public partial class OnboardingWizard : Window
{
    private readonly SettingsRepository _settings;
    private readonly VaultService _vault;
    private int _currentStep = 1;
    private const int TotalSteps = 4;

    public OnboardingWizard(SettingsRepository settings, VaultService vault)
    {
        _settings = settings;
        _vault = vault;
        InitializeComponent();

        RadioClaude.Checked   += (_, _) => { ApiKeyField.IsEnabled = true; CustomEndpointField.IsEnabled = false; CustomModelField.IsEnabled = false; };
        RadioOffline.Checked  += (_, _) => { ApiKeyField.IsEnabled = false; CustomEndpointField.IsEnabled = false; CustomModelField.IsEnabled = false; };
        RadioCustom.Checked   += (_, _) => { ApiKeyField.IsEnabled = false; CustomEndpointField.IsEnabled = true; CustomModelField.IsEnabled = true; };

        DnsCustom.Checked     += (_, _) => DnsCustomUrl.IsEnabled = true;
        DnsQuad9.Checked      += (_, _) => DnsCustomUrl.IsEnabled = false;
        DnsCloudflare.Checked += (_, _) => DnsCustomUrl.IsEnabled = false;
        DnsNextDns.Checked    += (_, _) => DnsCustomUrl.IsEnabled = false;

        ApplyLanguage();
        LocalizationService.Current.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= ApplyLanguage;
    }

    /// <summary>
    /// v2.0.5.3 — Pulls every label from <see cref="LocalizationService"/>.
    /// Onboarding is the user's first impression — every string here ships
    /// in all 8 supported UI languages (es/en/pt/fr/de/zh/ru/ja).
    /// </summary>
    private void ApplyLanguage()
    {
        var L = LocalizationService.Current;
        Title = L.T("onboarding.title");

        StepIndicator.Text = string.Format(L.T("onboarding.step"), _currentStep, TotalSteps);
        BackBtn.Content    = L.T("onboarding.back");
        NextBtn.Content    = _currentStep == TotalSteps ? L.T("onboarding.start") : L.T("onboarding.next");

        // Step 1
        Step1Title.Text  = L.T("onboarding.s1.title");
        OfflineTitle.Text = L.T("settings.ai.offline");
        OfflineDesc.Text  = L.T("onboarding.s1.offline.desc");
        ClaudeTitle.Text  = L.T("settings.ai.claude");
        ClaudeDesc.Text   = L.T("onboarding.s1.claude.desc");
        CustomTitle.Text  = L.T("onboarding.s1.custom");
        CustomDesc.Text   = L.T("onboarding.s1.custom.desc");

        // Step 2
        Step2Title.Text   = L.T("onboarding.s2.title");
        Step2Intro.Text   = L.T("onboarding.s2.intro");
        Quad9Title.Text   = L.T("settings.dns.quad9");
        Quad9Desc.Text    = L.T("onboarding.s2.quad9.desc");
        DnsCustomLabel.Text = L.T("settings.dns.custom");

        // Step 3
        Step3Title.Text   = L.T("onboarding.s3.title");
        Step3Intro.Text   = L.T("onboarding.s3.intro");
        AlreadyOn.Text    = L.T("onboarding.s3.already");
        ProtCanvas.Text   = L.T("onboarding.s3.canvas");
        ProtHardware.Text = L.T("onboarding.s3.hardware");
        ProtUa.Text       = L.T("onboarding.s3.ua");
        ProtWebrtc.Text   = L.T("onboarding.s3.webrtc");
        LevelLine.Text    = L.T("onboarding.s3.level");
        ChangeableLine.Text = L.T("onboarding.s3.changeable");

        // Step 4
        Step4Title.Text   = L.T("onboarding.s4.title");
        Step4Intro.Text   = L.T("onboarding.s4.intro");
        MasterLabel.Text  = L.T("onboarding.s4.master");
        ConfirmLabel.Text = L.T("onboarding.s4.confirm");
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (!await ValidateCurrentStepAsync()) return;
        await SaveCurrentStepAsync();

        if (_currentStep == TotalSteps)
        {
            await _settings.SetBoolAsync(SettingKeys.OnboardingCompleted, true);
            DialogResult = true;
            Close();
            return;
        }

        _currentStep++;
        UpdateView();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateView();
        }
    }

    private void UpdateView()
    {
        Step1.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        var L = LocalizationService.Current;
        StepIndicator.Text = string.Format(L.T("onboarding.step"), _currentStep, TotalSteps);
        BackBtn.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Hidden;
        NextBtn.Content    = _currentStep == TotalSteps ? L.T("onboarding.start") : L.T("onboarding.next");
    }

    private async Task<bool> ValidateCurrentStepAsync()
    {
        if (_currentStep == 4)
        {
            var pwd = MasterPasswordField.Password;
            var confirm = ConfirmPasswordField.Password;
            var L = LocalizationService.Current;

            if (pwd.Length < 8)
            {
                ShowPasswordError(L.T("onboarding.s4.min8"));
                return false;
            }
            if (pwd != confirm)
            {
                ShowPasswordError(L.T("onboarding.s4.mismatch"));
                return false;
            }
            PasswordError.Visibility = Visibility.Collapsed;
        }
        return await Task.FromResult(true);
    }

    private async Task SaveCurrentStepAsync()
    {
        switch (_currentStep)
        {
            case 1:
                if (RadioOffline.IsChecked == true)
                    await _settings.SetAsync(SettingKeys.AiMode, "Offline");
                else if (RadioClaude.IsChecked == true)
                {
                    await _settings.SetAsync(SettingKeys.AiMode, "Claude");
                    await _settings.SetAsync(SettingKeys.AiApiKey, ApiKeyField.Text.Trim());
                }
                else if (RadioCustom.IsChecked == true)
                {
                    await _settings.SetAsync(SettingKeys.AiMode, "Custom");
                    await _settings.SetAsync(SettingKeys.AiCustomEndpoint, CustomEndpointField.Text.Trim());
                    await _settings.SetAsync(SettingKeys.AiClaudeModel, CustomModelField.Text.Trim());
                }
                break;

            case 2:
                if (DnsQuad9.IsChecked == true)      await _settings.SetAsync(SettingKeys.DnsProvider, "Quad9");
                else if (DnsCloudflare.IsChecked == true) await _settings.SetAsync(SettingKeys.DnsProvider, "Cloudflare");
                else if (DnsNextDns.IsChecked == true)    await _settings.SetAsync(SettingKeys.DnsProvider, "NextDNS");
                else if (DnsCustom.IsChecked == true)
                {
                    await _settings.SetAsync(SettingKeys.DnsProvider, "Custom");
                    await _settings.SetAsync(SettingKeys.DnsCustomUrl, DnsCustomUrl.Text.Trim());
                }
                break;

            case 3:
                // Fingerprint is already set to Aggressive by default — nothing to save
                await _settings.SetAsync(SettingKeys.FingerprintLevel, "Aggressive");
                break;

            case 4:
                await _vault.InitializeAsync(MasterPasswordField.Password);
                break;
        }
    }

    private void ShowPasswordError(string msg)
    {
        PasswordError.Text = msg;
        PasswordError.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Wizard cannot be dismissed without completing it
        if (DialogResult != true)
            e.Cancel = true;
    }
}
