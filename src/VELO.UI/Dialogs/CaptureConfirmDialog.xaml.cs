using System.Windows;

namespace VELO.UI.Dialogs;

/// <summary>
/// Phase 6 — the "here is what a capture does" notice, with a way to stop
/// being told.
///
/// It replaces a native <c>MessageBox</c>, for two reasons that both mattered:
/// a MessageBox has nowhere to put a "don't show again" checkbox, and a native
/// one cannot be themed, so it flashed a light dialog in the middle of a dark
/// browser. Suppression is deliberately for the session only — it lives in a
/// caller-side flag, not in Settings — because a capture reloads the page and
/// speeds up playback, and a user coming back tomorrow deserves to be reminded
/// of that once.
/// </summary>
public partial class CaptureConfirmDialog : Window
{
    /// <summary>True when the user ticked "don't show this again".</summary>
    public bool SuppressForSession => DontShowAgain.IsChecked == true;

    public CaptureConfirmDialog(string kind, double playbackRate)
    {
        InitializeComponent();

        HeaderText.Text = $"Capture {kind}";
        BodyText.Text =
            $"VELO will capture the {kind} track as it plays. The page reloads and " +
            $"the video then plays fast and muted — that is how the capture goes " +
            $"quicker than the running time, not a fault. At {playbackRate:0}× speed, " +
            "ten minutes of video take a couple of minutes.";
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
