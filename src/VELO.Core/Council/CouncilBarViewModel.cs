using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VELO.Core.Council;

/// <summary>
/// Phase 4.1 chunk F — bindable state for the Council Bar (the prompt input +
/// "Send all" button + status banner that sits above the 2×2 layout). Lives in
/// VELO.Core so it can be unit-tested without WPF; the matching XAML control
/// (<c>CouncilBar.xaml</c>) is a thin INPC consumer that wires DataContext to
/// an instance of this class.
///
/// State machine — the bar moves through four observable statuses:
/// <list type="bullet">
///   <item><see cref="CouncilBarStatus.Idle"/> — waiting for user input. Send enabled
///         iff <see cref="PromptText"/> is non-empty AND <see cref="AvailablePanelCount"/> &gt; 0.</item>
///   <item><see cref="CouncilBarStatus.Sending"/> — host has called Send-All; orchestrator
///         is pushing the master prompt to each enabled panel. Send disabled.</item>
///   <item><see cref="CouncilBarStatus.Synthesising"/> — every panel reply landed; moderator
///         is composing the synthesis via DirectChatAdapter. Send disabled.</item>
///   <item><see cref="CouncilBarStatus.Error"/> — host caught an exception or moderator
///         failed; <see cref="ErrorText"/> populated. Send re-enabled so the user can retry.</item>
/// </list>
///
/// The orchestrator (chunk B) does NOT consume this VM directly. The host
/// (chunk G + future MainWindow) is responsible for translating orchestrator
/// events (CaptureReceived / SynthesisReady / etc) into VM state updates.
/// Keeps the view layer thin and lets future Council Bar variants (compact
/// mode, error-only banner, etc) reuse the same state machine.
/// </summary>
public sealed class CouncilBarViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _promptText = "";
    /// <summary>The master prompt the user is composing. Cleared by the host
    /// after a successful send so the next turn starts fresh.</summary>
    public string PromptText
    {
        get => _promptText;
        set
        {
            if (Set(ref _promptText, value))
                RaiseDependents();
        }
    }

    private int _availablePanelCount;
    /// <summary>How many panels (max 4) are opted-in + alive right now. Drives
    /// the Send button enable state — sending to zero panels is a no-op.</summary>
    public int AvailablePanelCount
    {
        get => _availablePanelCount;
        set
        {
            if (Set(ref _availablePanelCount, Math.Max(0, value)))
                RaiseDependents();
        }
    }

    private int _captureCount;
    /// <summary>Total captures gathered across all panels in the current session.
    /// Surfaced as a badge next to the Send button so the user knows the
    /// synthesis will include attached fragments.</summary>
    public int CaptureCount
    {
        get => _captureCount;
        set => Set(ref _captureCount, Math.Max(0, value));
    }

    private CouncilBarStatus _status = CouncilBarStatus.Idle;
    public CouncilBarStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                RaisePropertyChanged(nameof(IsSendEnabled));
                RaisePropertyChanged(nameof(IsBusy));
                RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    private string _errorText = "";
    /// <summary>Diagnostic copy shown when <see cref="Status"/> is
    /// <see cref="CouncilBarStatus.Error"/>. Empty otherwise.</summary>
    public string ErrorText
    {
        get => _errorText;
        set
        {
            if (Set(ref _errorText, value ?? ""))
                RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>True iff the user can press Send right now.</summary>
    public bool IsSendEnabled =>
        Status is CouncilBarStatus.Idle or CouncilBarStatus.Error &&
        AvailablePanelCount > 0 &&
        !string.IsNullOrWhiteSpace(PromptText);

    /// <summary>True while orchestrator work is in flight — sending or synthesising.</summary>
    public bool IsBusy =>
        Status is CouncilBarStatus.Sending or CouncilBarStatus.Synthesising;

    /// <summary>Localised single-line status copy for the bar's right-hand label.</summary>
    public string StatusText => Status switch
    {
        CouncilBarStatus.Idle when AvailablePanelCount == 0 =>
            "Sin paneles activos — activá proveedores en Settings → 🤝 Council.",
        CouncilBarStatus.Idle when CaptureCount > 0 =>
            $"{AvailablePanelCount} paneles listos · {CaptureCount} capturas adjuntas.",
        CouncilBarStatus.Idle =>
            $"{AvailablePanelCount} paneles listos.",
        CouncilBarStatus.Sending =>
            $"Enviando a {AvailablePanelCount} paneles…",
        CouncilBarStatus.Synthesising =>
            "Síntesis local en curso…",
        CouncilBarStatus.Error =>
            string.IsNullOrWhiteSpace(ErrorText)
                ? "Error sin detalle. Intentá de nuevo."
                : ErrorText,
        _ => "",
    };

    /// <summary>Convenience for the host to clear prompt + error on a successful send.</summary>
    public void ResetForNextTurn()
    {
        ErrorText  = "";
        PromptText = "";
        Status     = CouncilBarStatus.Idle;
    }

    private void RaiseDependents()
    {
        // Many of our computed surfaces depend on the same underlying fields;
        // raise them as a batch when any of those fields mutate so the bar
        // never renders an inconsistent set of bindings mid-update.
        RaisePropertyChanged(nameof(IsSendEnabled));
        RaisePropertyChanged(nameof(StatusText));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(name);
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Status the Council Bar reports through its single <see cref="CouncilBarViewModel.Status"/>
/// property. Drives both the Send button enable state and the status copy.
/// </summary>
public enum CouncilBarStatus
{
    Idle = 0,
    Sending = 1,
    Synthesising = 2,
    Error = 3,
}
