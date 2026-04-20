namespace VELO.Core;

/// <summary>
/// Resolves the user-data directory for VELO, supporting both installed and portable modes.
///
/// Portable mode:
///   If a file named <c>portable.flag</c> exists next to the executable, all user data
///   is stored in <c>&lt;exe-dir&gt;\userdata\</c> instead of <c>%LOCALAPPDATA%\VELO\</c>.
///   This allows running VELO from a USB drive or any folder without touching the registry
///   or system directories.
///
/// Installed mode (default):
///   Data is stored in <c>%LOCALAPPDATA%\VELO\</c>.
/// </summary>
public static class DataLocation
{
    private static string? _cachedPath;

    /// <summary>
    /// Override for the exe directory, used in unit tests only.
    /// When set, GetExeDir() returns this value instead of AppContext.BaseDirectory.
    /// </summary>
    internal static string? ExeDirOverride;

    /// <summary>
    /// Returns the root user-data path. Call once at startup; result is cached.
    /// </summary>
    public static string GetUserDataPath()
    {
        if (_cachedPath != null) return _cachedPath;

        var exeDir = GetExeDir();
        var portableFlag = Path.Combine(exeDir, "portable.flag");

        _cachedPath = File.Exists(portableFlag)
            ? Path.Combine(exeDir, "userdata")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VELO");

        Directory.CreateDirectory(_cachedPath);
        return _cachedPath;
    }

    /// <summary>True when running in portable mode (next to a <c>portable.flag</c> file).</summary>
    public static bool IsPortable
    {
        get
        {
            var exeDir = GetExeDir();
            return File.Exists(Path.Combine(exeDir, "portable.flag"));
        }
    }

    /// <summary>Returns a sub-path inside the user-data directory, creating it if needed.</summary>
    public static string SubPath(string relativePath)
    {
        var full = Path.Combine(GetUserDataPath(), relativePath);
        var dir  = Path.GetDirectoryName(full);
        if (dir != null) Directory.CreateDirectory(dir);
        return full;
    }

    /// <summary>Clears the cached path and any test override. Intended for testing only.</summary>
    internal static void ResetCache()
    {
        _cachedPath = null;
        ExeDirOverride = null;
    }

    private static string GetExeDir() =>
        (ExeDirOverride ?? AppContext.BaseDirectory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
