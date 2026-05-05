using System.Text;

namespace VELO.Core.Sessions;

/// <summary>
/// Phase 3 / Sprint 10 — Cheap stable fingerprint of a session snapshot
/// used by the heartbeat to skip disk writes when nothing has changed.
/// Extracted from MainWindow as part of the v2.4.1 refactor so the
/// fingerprint logic can be unit-tested without spinning up WPF.
///
/// The fingerprint is a single string built deterministically from:
/// • the clean-shutdown flag (so a clean-vs-dirty change always re-writes)
/// • the active tab id
/// • the window bounds and maximised state
/// • each tab's (id, url, title, container, workspace) tuple
///
/// Equality is plain ordinal string comparison — no hashing, no
/// allocation tax beyond a single StringBuilder. The shape was chosen so
/// every byte that ends up serialised to <c>session.json</c> contributes
/// to the fingerprint; if two snapshots fingerprint-equal, their JSON
/// will too.
/// </summary>
public static class SessionFingerprint
{
    /// <summary>
    /// Computes the fingerprint for the given window snapshot. <paramref name="cleanShutdown"/>
    /// is included so the heartbeat always re-writes when transitioning
    /// from running → clean shutdown (otherwise the WasCleanShutdown=true
    /// flag could be skipped on idle exit).
    /// </summary>
    public static string Compute(WindowSnapshot window, bool cleanShutdown)
    {
        var sb = new StringBuilder(256);
        sb.Append(cleanShutdown ? '1' : '0');
        sb.Append('|').Append(window.ActiveTabId);
        sb.Append('|')
          .Append((int)window.Left).Append(',')
          .Append((int)window.Top).Append(',')
          .Append((int)window.Width).Append(',')
          .Append((int)window.Height).Append(',')
          .Append(window.IsMaximised ? '1' : '0');

        foreach (var t in window.Tabs)
        {
            sb.Append('\n')
              .Append(t.Id).Append('|')
              .Append(t.Url).Append('|')
              .Append(t.Title).Append('|')
              .Append(t.ContainerId).Append('|')
              .Append(t.WorkspaceId);
        }
        return sb.ToString();
    }
}
