using System.ComponentModel;
using VELO.Core.Council;
using Xunit;

namespace VELO.Core.Tests.Council;

/// <summary>
/// Phase 4.1 chunk F — state machine + property-change semantics for the
/// Council Bar VM. Pure C# (no WPF) so the bindings consumed by
/// <c>CouncilBar.xaml</c> are exercised here without needing an STA dispatcher.
/// </summary>
public class CouncilBarViewModelTests
{
    // ── Send enable gating ──────────────────────────────────────────────

    [Fact]
    public void IsSendEnabled_isFalse_whenIdleAndPromptEmpty()
    {
        var vm = new CouncilBarViewModel { AvailablePanelCount = 4 };
        Assert.False(vm.IsSendEnabled);
    }

    [Fact]
    public void IsSendEnabled_isFalse_whenPromptIsOnlyWhitespace()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 4,
            PromptText = "   \t\n",
        };
        Assert.False(vm.IsSendEnabled);
    }

    [Fact]
    public void IsSendEnabled_isFalse_whenNoPanelsAvailable()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 0,
            PromptText = "hello",
        };
        Assert.False(vm.IsSendEnabled);
    }

    [Fact]
    public void IsSendEnabled_isTrue_whenIdlePromptAndPanels()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 2,
            PromptText = "Compare these three approaches",
        };
        Assert.True(vm.IsSendEnabled);
    }

    [Fact]
    public void IsSendEnabled_isFalse_whileSending()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 4,
            PromptText = "x",
            Status = CouncilBarStatus.Sending,
        };
        Assert.False(vm.IsSendEnabled);
    }

    [Fact]
    public void IsSendEnabled_isFalse_whileSynthesising()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 4,
            PromptText = "x",
            Status = CouncilBarStatus.Synthesising,
        };
        Assert.False(vm.IsSendEnabled);
    }

    [Fact]
    public void IsSendEnabled_isTrue_afterError_soUserCanRetry()
    {
        // After a synthesiser failure the bar should let the user press Send
        // again without having to retype the prompt. ErrorText is shown but
        // the gate is open.
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 4,
            PromptText = "retry me",
            Status = CouncilBarStatus.Error,
            ErrorText = "Synthesiser timed out",
        };
        Assert.True(vm.IsSendEnabled);
    }

    // ── IsBusy ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CouncilBarStatus.Idle,        false)]
    [InlineData(CouncilBarStatus.Sending,      true)]
    [InlineData(CouncilBarStatus.Synthesising, true)]
    [InlineData(CouncilBarStatus.Error,       false)]
    public void IsBusy_tracksStatus(CouncilBarStatus status, bool expected)
    {
        var vm = new CouncilBarViewModel { Status = status };
        Assert.Equal(expected, vm.IsBusy);
    }

    // ── StatusText ──────────────────────────────────────────────────────

    [Fact]
    public void StatusText_idleZeroPanels_promptsToActivate()
    {
        var vm = new CouncilBarViewModel { AvailablePanelCount = 0 };
        Assert.Contains("Sin paneles activos", vm.StatusText);
    }

    [Fact]
    public void StatusText_idleSomePanels_showsReadyCount()
    {
        var vm = new CouncilBarViewModel { AvailablePanelCount = 3 };
        Assert.Contains("3 paneles", vm.StatusText);
    }

    [Fact]
    public void StatusText_idleWithCaptures_mentionsAttachments()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 2,
            CaptureCount = 5,
        };
        Assert.Contains("5 capturas", vm.StatusText);
    }

    [Fact]
    public void StatusText_sending_showsTargetCount()
    {
        var vm = new CouncilBarViewModel
        {
            AvailablePanelCount = 4,
            Status = CouncilBarStatus.Sending,
        };
        Assert.Contains("Enviando", vm.StatusText);
        Assert.Contains("4 paneles", vm.StatusText);
    }

    [Fact]
    public void StatusText_error_returnsErrorTextWhenPopulated()
    {
        var vm = new CouncilBarViewModel
        {
            Status = CouncilBarStatus.Error,
            ErrorText = "Timeout calling local moderator",
        };
        Assert.Equal("Timeout calling local moderator", vm.StatusText);
    }

    [Fact]
    public void StatusText_errorBlank_returnsFallback()
    {
        var vm = new CouncilBarViewModel { Status = CouncilBarStatus.Error };
        Assert.Contains("sin detalle", vm.StatusText);
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────────

    [Fact]
    public void SettingPromptText_raisesPropertyChangedForDerivedFields()
    {
        var vm = new CouncilBarViewModel { AvailablePanelCount = 1 };
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.PromptText = "hello";

        Assert.Contains(nameof(CouncilBarViewModel.PromptText),    raised);
        Assert.Contains(nameof(CouncilBarViewModel.IsSendEnabled), raised);
        Assert.Contains(nameof(CouncilBarViewModel.StatusText),    raised);
    }

    [Fact]
    public void SettingStatus_raisesPropertyChangedForIsBusy()
    {
        var vm = new CouncilBarViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Status = CouncilBarStatus.Sending;

        Assert.Contains(nameof(CouncilBarViewModel.Status),        raised);
        Assert.Contains(nameof(CouncilBarViewModel.IsSendEnabled), raised);
        Assert.Contains(nameof(CouncilBarViewModel.IsBusy),        raised);
        Assert.Contains(nameof(CouncilBarViewModel.StatusText),    raised);
    }

    [Fact]
    public void SettingSameValue_doesNotRaisePropertyChanged()
    {
        var vm = new CouncilBarViewModel { PromptText = "x" };
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.PromptText = "x"; // same value

        Assert.Empty(raised);
    }

    // ── Clamping ────────────────────────────────────────────────────────

    [Fact]
    public void AvailablePanelCount_clampsToZero_onNegativeInput()
    {
        var vm = new CouncilBarViewModel { AvailablePanelCount = -5 };
        Assert.Equal(0, vm.AvailablePanelCount);
    }

    [Fact]
    public void CaptureCount_clampsToZero_onNegativeInput()
    {
        var vm = new CouncilBarViewModel { CaptureCount = -3 };
        Assert.Equal(0, vm.CaptureCount);
    }

    // ── ResetForNextTurn ────────────────────────────────────────────────

    [Fact]
    public void ResetForNextTurn_clearsPromptAndErrorAndStatus()
    {
        var vm = new CouncilBarViewModel
        {
            PromptText = "old prompt",
            Status = CouncilBarStatus.Error,
            ErrorText = "previous failure",
        };

        vm.ResetForNextTurn();

        Assert.Equal("",                       vm.PromptText);
        Assert.Equal("",                       vm.ErrorText);
        Assert.Equal(CouncilBarStatus.Idle,    vm.Status);
    }
}
