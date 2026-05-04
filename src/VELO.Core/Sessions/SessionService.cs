using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VELO.Core.Sessions;

/// <summary>
/// Phase 3 / Sprint 3 — Persists and restores session snapshots so VELO can
/// reopen the previous tabs after a normal shutdown or crash.
///
/// File layout: <c>%LocalAppData%\VELO\session.json</c> (overridable via
/// constructor for tests). Single-file, atomic write via temp + replace so
/// a crash mid-write leaves either the previous good snapshot or nothing.
///
/// The service is dumb file I/O. The host (MainWindow / App) decides:
///   • when to snapshot (30-s timer + Window_Closing)
///   • which tabs to include — banking and temporal-* containers are
///     filtered out via <see cref="IsSafeForSnapshot"/> per spec § 6.4.
///   • whether to skip entirely (Paranoid / Bunker security modes)
/// </summary>
public sealed class SessionService
{
    private readonly string _sessionFilePath;
    private readonly ILogger<SessionService> _logger;
    private readonly object _writeLock = new();

    /// <summary>Default location: <c>%LocalAppData%\VELO\session.json</c>.</summary>
    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VELO");
            return Path.Combine(dir, "session.json");
        }
    }

    public SessionService(string? sessionFilePath = null, ILogger<SessionService>? logger = null)
    {
        _sessionFilePath = sessionFilePath ?? DefaultPath;
        _logger = logger ?? NullLogger<SessionService>.Instance;
    }

    /// <summary>The on-disk path being used. Useful for the restore-prompt log.</summary>
    public string FilePath => _sessionFilePath;

    /// <summary>
    /// Writes <paramref name="snapshot"/> atomically. Only tabs that pass
    /// <see cref="IsSafeForSnapshot"/> survive — banking / temporal containers
    /// are stripped before serialisation per spec § 6.4.
    /// </summary>
    public async Task SnapshotAsync(SessionSnapshot snapshot, CancellationToken ct = default)
    {
        var sanitised = Sanitise(snapshot);

        // Build JSON in memory first so any serialisation error doesn't leave
        // a half-written file.
        var json = JsonSerializer.Serialize(sanitised, _jsonOpts);
        var dir  = Path.GetDirectoryName(_sessionFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmpPath = _sessionFilePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, json, ct).ConfigureAwait(false);
            // Atomic-ish replace. File.Move with overwrite=true is atomic on
            // NTFS for same-volume; on FAT it's two ops but the temp file
            // stays on cancel/crash so the previous snapshot is intact.
            File.Move(tmpPath, _sessionFilePath, overwrite: true);
            _logger.LogDebug("Session snapshot saved ({Count} tabs across {Windows} windows)",
                sanitised.TotalTabs, sanitised.Windows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session snapshot write failed");
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Loads the last snapshot. Returns null when:
    ///   • the file does not exist (first run);
    ///   • the JSON is unparseable / corrupt;
    ///   • the schema version is newer than this build understands.
    /// Never throws into the caller — restoring a session is best-effort.
    /// </summary>
    public async Task<SessionSnapshot?> LoadLastAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_sessionFilePath)) return null;
        try
        {
            var json     = await File.ReadAllTextAsync(_sessionFilePath, ct).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(json, _jsonOpts);
            if (snapshot is null) return null;
            // Future-proof: if a newer build wrote a snapshot we don't grok,
            // pretend nothing was there rather than fail boot.
            if (snapshot.Version > SupportedSchemaVersion)
            {
                _logger.LogWarning("Session snapshot at v{Found} is newer than supported v{Max} — ignoring",
                    snapshot.Version, SupportedSchemaVersion);
                return null;
            }
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session snapshot load failed; ignoring");
            return null;
        }
    }

    /// <summary>Deletes the snapshot file. Used after a successful restore to
    /// avoid re-prompting on the next launch.</summary>
    public Task ClearAsync()
    {
        try
        {
            if (File.Exists(_sessionFilePath)) File.Delete(_sessionFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session snapshot clear failed");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// True when a tab in <paramref name="containerId"/> may be persisted.
    /// Banking and temporal-* containers are session-only by design.
    /// </summary>
    public static bool IsSafeForSnapshot(string containerId)
    {
        if (string.IsNullOrEmpty(containerId)) return true;
        if (containerId.Equals("banking", StringComparison.OrdinalIgnoreCase)) return false;
        if (containerId.StartsWith("temporal-", StringComparison.OrdinalIgnoreCase)) return false;
        if (containerId.StartsWith("temp-",     StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // ── Internals ──────────────────────────────────────────────────────

    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Removes container-restricted tabs and any windows that end
    /// up empty as a result.</summary>
    private static SessionSnapshot Sanitise(SessionSnapshot input)
    {
        var windows = input.Windows.Select(w =>
        {
            var safeTabs = w.Tabs.Where(t => IsSafeForSnapshot(t.ContainerId)).ToList();
            return new WindowSnapshot
            {
                Left        = w.Left,
                Top         = w.Top,
                Width       = w.Width,
                Height      = w.Height,
                IsMaximised = w.IsMaximised,
                ActiveTabId = safeTabs.Any(t => t.Id == w.ActiveTabId) ? w.ActiveTabId :
                              safeTabs.FirstOrDefault()?.Id ?? "",
                Tabs        = safeTabs,
                Workspaces  = w.Workspaces,
            };
        }).Where(w => w.Tabs.Count > 0).ToList();

        return new SessionSnapshot
        {
            Version          = input.Version,
            SavedAtUtc       = input.SavedAtUtc,
            WasCleanShutdown = input.WasCleanShutdown,
            Windows          = windows,
        };
    }
}
