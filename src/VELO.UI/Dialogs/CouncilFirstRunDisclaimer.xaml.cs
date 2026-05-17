using System.Windows;
using VELO.Core.AI;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.UI.Dialogs;

/// <summary>
/// Phase 4.0 chunk G — modal shown the first time the user activates
/// Council Mode. Three jobs:
///
///   1. Explain the privacy contract: master prompts to enabled providers
///      go to their servers; the local moderator stays on the device.
///   2. Let the user opt out of any of the four providers BEFORE the
///      activation actually happens.
///   3. Run <see cref="CouncilPreflightService.CheckAsync"/> in the
///      background and surface the result so the user knows whether
///      synthesis will work.
///
/// Persists the decision in Settings (per-provider toggles +
/// disclaimer_accepted). Subsequent activations skip this dialog and
/// read straight from those keys.
///
/// Constructed with the bare service dependencies the dialog needs —
/// no DI container directly (matches the rest of VELO's dialog
/// pattern — the host resolves the deps and passes them through).
/// </summary>
public partial class CouncilFirstRunDisclaimer : Window
{
    private readonly SettingsRepository       _settings;
    private readonly CouncilPreflightService  _preflight;

    /// <summary>
    /// Returns true after the user clicked "Aceptar y abrir Council".
    /// On Cancel / Close stays false. The host inspects this (or
    /// <see cref="Window.DialogResult"/>) when <see cref="Window.ShowDialog"/>
    /// returns to decide whether to call <c>ActivateCouncilModeAsync</c>.
    /// </summary>
    public bool Accepted { get; private set; }

    public CouncilFirstRunDisclaimer(
        SettingsRepository settings,
        CouncilPreflightService preflight)
    {
        _settings  = settings;
        _preflight = preflight;
        InitializeComponent();

        // Kick the probe as soon as the window has its HWND. The UI starts
        // in the "checking" state (⏳ + grey message); the probe overwrites
        // it with the success / failure summary.
        Loaded += async (_, _) => await RunPreflightAsync();
    }

    private async Task RunPreflightAsync()
    {
        try
        {
            var result = await _preflight.CheckAsync();
            ApplyPreflightResult(result);
        }
        catch (Exception ex)
        {
            // Belt-and-braces — CouncilPreflightService.CheckAsync is
            // documented as never-throwing but we'd rather degrade
            // gracefully than crash the disclaimer.
            PreflightIcon.Text     = "⚠";
            PreflightMessage.Text  = $"No se pudo verificar Ollama: {ex.Message}";
            UpdateAcceptEnabled(preflightHealthy: false);
        }
    }

    private void ApplyPreflightResult(CouncilPreflightService.Result result)
    {
        if (result.IsHealthy)
        {
            PreflightIcon.Text    = "✓";
            PreflightMessage.Text = result.ContextSize.HasValue
                ? $"Ollama listo: qwen3:32b cargado ({result.ContextSize.Value} tokens de contexto)."
                : "Ollama listo: qwen3:32b detectado (contexto no reportado).";
        }
        else
        {
            PreflightIcon.Text    = result.EndpointReachable ? "⚠" : "✕";
            PreflightMessage.Text = result.FailureReason
                ?? "Ollama no está disponible para la síntesis local.";
        }

        UpdateAcceptEnabled(result.IsHealthy);
    }

    private void Provider_Toggled(object sender, RoutedEventArgs e)
    {
        // Re-evaluate enable state — at least one provider must stay checked.
        UpdateAcceptEnabled(LastPreflightWasHealthy);
    }

    // Cached so Provider_Toggled can re-evaluate without re-running the probe.
    private bool LastPreflightWasHealthy { get; set; }

    private void UpdateAcceptEnabled(bool preflightHealthy)
    {
        LastPreflightWasHealthy = preflightHealthy;

        // v2.4.55 — Provider_Toggled fires while InitializeComponent is still binding
        // the four <CheckBox IsChecked="True" Checked="Provider_Toggled"/> instances,
        // BEFORE AcceptButton (defined later in the XAML) has been assigned. Touching
        // AcceptButton.IsEnabled in that window threw NullReferenceException, which
        // OpenCouncilModeAsync's catch swallowed as a silent teardown. Phase 4.0 chunk G
        // never hit this path until v2.4.54 unlocked the Settings provider toggles —
        // 6 releases dormant. The Loaded-driven RunPreflightAsync path reaches this
        // method too, where AcceptButton is guaranteed assigned, so the guard is a no-op
        // for the legitimate call sites.
        if (AcceptButton is null) return;

        var anyProvider = ChkClaude.IsChecked  == true
                       || ChkChatGpt.IsChecked == true
                       || ChkGrok.IsChecked    == true
                       || ChkOllama.IsChecked  == true;

        AcceptButton.IsEnabled = preflightHealthy && anyProvider;
    }

    private async void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        // Persist the four per-provider toggles + the disclaimer ack.
        // Settings repository is async; the window stays open during the
        // four awaits but the writes are fast (local SQLite, ~ms each).
        await _settings.SetAsync(SettingKeys.CouncilEnabledClaude,  Bool(ChkClaude.IsChecked));
        await _settings.SetAsync(SettingKeys.CouncilEnabledChatGpt, Bool(ChkChatGpt.IsChecked));
        await _settings.SetAsync(SettingKeys.CouncilEnabledGrok,    Bool(ChkGrok.IsChecked));
        await _settings.SetAsync(SettingKeys.CouncilEnabledOllama,  Bool(ChkOllama.IsChecked));
        await _settings.SetAsync(SettingKeys.CouncilDisclaimerAccepted, "yes");

        Accepted     = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Accepted     = false;
        DialogResult = false;
        Close();
    }

    private static string Bool(bool? value) => value == true ? "yes" : "no";
}
