using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using VELO.Core;
using VELO.Core.Sessions;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.App.Controllers;

/// <summary>
/// Phase 3 / Sprint 10b chunk 1 (v2.4.27) — Extracted from MainWindow.xaml.cs
/// to shrink the host class. Owns the full session save/restore lifecycle:
///
///   • <b>Init</b>: reads the SecurityMode setting (Paranoid/Bunker → wipe
///     any stale snapshot and bail), then runs the restore prompt if there
///     is a usable snapshot, then starts the 30 s heartbeat timer.
///   • <b>Heartbeat</b>: every 30 s computes a <see cref="SessionFingerprint"/>
///     of the current window+tabs and writes a fresh snapshot
///     (WasCleanShutdown=false) when it changed. Saves ~120 writes/hour on
///     idle sessions.
///   • <b>Shutdown</b>: <see cref="StopAndFlushAsync"/> stops the timer and
///     writes a final WasCleanShutdown=true snapshot.
///
/// MainWindow keeps the parts that need direct access to its private state
/// (notably <c>_initialUrl</c> swap on first restore and the
/// <c>RestoreMaxTabs</c> cap). The controller raises
/// <see cref="RestoreRequested"/> with the snapshot it picked and lets
/// the host hydrate tabs.
///
/// Why the host window owns tab restore but not the rest: the heartbeat,
/// fingerprint, prompt dialog and settings dance are all pure data —
/// they don't touch the WPF visual tree. Tab restoration on the other
/// hand mutates <c>_browserTabs</c>, swaps <c>_initialUrl</c> on first
/// tab, and applies the <c>RestoreMaxTabs</c> cap. Keeping that in
/// MainWindow avoids a controller-to-MainWindow back-reference.
/// </summary>
public sealed class SessionPersistenceController
{
    private readonly SessionService _sessionService;
    private readonly SettingsRepository _settings;
    private readonly Window _owner;
    private readonly Func<SessionSnapshot> _buildSnapshot;
    private readonly ILogger<SessionPersistenceController> _logger;

    private DispatcherTimer? _timer;
    private string _lastFingerprint = "";
    private bool _skippedDueToSecurityMode;

    /// <summary>Heartbeat cadence. Tunable; 30 s matches the historical default.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Raised by <see cref="InitializeAsync"/> when there is a usable
    /// snapshot and the user (or saved preference) chose to restore.
    /// Subscribers are responsible for materialising tabs from
    /// <c>SessionSnapshot.Windows[0].Tabs</c>.
    /// </summary>
    public event Action<SessionSnapshot>? RestoreRequested;

    public SessionPersistenceController(
        SessionService sessionService,
        SettingsRepository settings,
        Window owner,
        Func<SessionSnapshot> buildSnapshot,
        ILogger<SessionPersistenceController>? logger = null)
    {
        _sessionService = sessionService;
        _settings       = settings;
        _owner          = owner;
        _buildSnapshot  = buildSnapshot;
        _logger         = logger ?? NullLogger<SessionPersistenceController>.Instance;
    }

    /// <summary>
    /// Per spec § 6.3: never write a snapshot in Paranoid/Bunker modes,
    /// and wipe any stale snapshot so a previous Normal-mode session
    /// can't leak into a paranoid relaunch. Otherwise: maybe-prompt the
    /// user to restore, then start the heartbeat.
    /// </summary>
    public async Task InitializeAsync()
    {
        var secMode = await _settings.GetAsync(SettingKeys.SecurityMode, "Normal");
        _skippedDueToSecurityMode = secMode is "Paranoid" or "Bunker";

        if (_skippedDueToSecurityMode)
        {
            await _sessionService.ClearAsync();
            return;
        }

        await MaybeRestoreAsync();
        StartHeartbeat();
    }

    /// <summary>
    /// Called by <see cref="MainWindow.Window_Closing"/>. Stops the
    /// heartbeat timer and writes a final WasCleanShutdown=true snapshot
    /// so the next launch knows the previous run ended cleanly.
    /// </summary>
    public async Task StopAndFlushAsync()
    {
        _timer?.Stop();
        try
        {
            await SaveSnapshotAsync(cleanShutdown: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[VELO] SessionPersistenceController flush failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the current window+tabs snapshot. Skipped in
    /// Paranoid/Bunker. Also skipped on the heartbeat path if the
    /// fingerprint matches the previous write — saves disk wear on
    /// idle sessions. Always writes on cleanShutdown to flip the
    /// WasCleanShutdown flag.
    /// </summary>
    public async Task SaveSnapshotAsync(bool cleanShutdown)
    {
        if (_skippedDueToSecurityMode) return;

        var snap = _buildSnapshot();
        if (snap.Windows.Count == 0) return;
        var window = snap.Windows[0];

        var fingerprint = SessionFingerprint.Compute(window, cleanShutdown);
        if (!cleanShutdown && fingerprint == _lastFingerprint) return;
        _lastFingerprint = fingerprint;

        await _sessionService.SnapshotAsync(snap);
    }

    // ── Internals ────────────────────────────────────────────────────────

    private void StartHeartbeat()
    {
        _timer = new DispatcherTimer { Interval = HeartbeatInterval };
        _timer.Tick += async (_, _) =>
        {
            try { await SaveSnapshotAsync(cleanShutdown: false); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[VELO] session heartbeat failed: {ex.Message}");
            }
        };
        _timer.Start();
    }

    private async Task MaybeRestoreAsync()
    {
        var snap = await _sessionService.LoadLastAsync();
        if (snap == null || snap.TotalTabs == 0) return;

        bool restore;
        if (!snap.WasCleanShutdown)
        {
            // Crash-recovery path always asks.
            var loc = VELO.Core.Localization.LocalizationService.Current;
            var ans = MessageBox.Show(_owner,
                string.Format(loc.T("session.recover.body"), snap.TotalTabs),
                loc.T("session.recover.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            restore = ans == MessageBoxResult.Yes;
        }
        else
        {
            // Clean shutdown → respect the saved preference. Ask once if no
            // preference exists yet.
            var alwaysRestore = await _settings.GetBoolAsync(SettingKeys.SessionRestoreAlways, false);
            var asked         = await _settings.GetBoolAsync(SettingKeys.SessionRestoreAsked,  false);

            if (alwaysRestore)
            {
                restore = true;
            }
            else if (!asked)
            {
                var loc = VELO.Core.Localization.LocalizationService.Current;
                var ans = MessageBox.Show(_owner,
                    string.Format(loc.T("session.restore.body"), snap.TotalTabs),
                    loc.T("session.restore.title"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                await _settings.SetBoolAsync(SettingKeys.SessionRestoreAsked, true);
                if (ans == MessageBoxResult.Yes)
                {
                    restore = true;
                    await _settings.SetBoolAsync(SettingKeys.SessionRestoreAlways, true);
                }
                else if (ans == MessageBoxResult.Cancel)
                {
                    restore = true;  // Cancel = restore-just-this-once
                }
                else
                {
                    restore = false;
                }
            }
            else
            {
                restore = false;
            }
        }

        if (restore)
        {
            try { RestoreRequested?.Invoke(snap); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RestoreRequested handler threw");
            }
        }

        // Wipe regardless — either we restored or the user said no.
        // Keeping the file would re-prompt forever.
        await _sessionService.ClearAsync();
    }
}
