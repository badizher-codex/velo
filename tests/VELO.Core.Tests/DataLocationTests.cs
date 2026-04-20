using VELO.Core;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Tests for DataLocation.
///
/// Note: DataLocation caches the resolved path in a static field.
/// Each test calls ResetCache() at the start and relies on Dispose() for cleanup.
/// </summary>
public class DataLocationTests : IDisposable
{
    public void Dispose() => DataLocation.ResetCache();

    // ── IsPortable — no flag ──────────────────────────────────────────────────

    [Fact]
    public void IsPortable_ReturnsFalse_WhenNoFlagExists()
    {
        // Default exe dir has no portable.flag in a clean test run
        Assert.False(DataLocation.IsPortable);
    }

    // ── GetUserDataPath — installed mode ──────────────────────────────────────

    [Fact]
    public void GetUserDataPath_InstalledMode_EndsWithVELO()
    {
        DataLocation.ResetCache();

        var path = DataLocation.GetUserDataPath();

        Assert.EndsWith("VELO", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserDataPath_InstalledMode_DirectoryIsCreated()
    {
        DataLocation.ResetCache();

        var path = DataLocation.GetUserDataPath();

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void GetUserDataPath_IsCached_ReturnsSamePathOnSecondCall()
    {
        DataLocation.ResetCache();

        var first  = DataLocation.GetUserDataPath();
        var second = DataLocation.GetUserDataPath();

        Assert.Equal(first, second);
    }

    // ── SubPath ───────────────────────────────────────────────────────────────

    [Fact]
    public void SubPath_CreatesSubdirectory_AndReturnsCorrectPath()
    {
        DataLocation.ResetCache();

        var sub = DataLocation.SubPath("logs");

        Assert.EndsWith("logs", sub, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(sub));
    }

    [Fact]
    public void SubPath_NestedPath_CreatesMissingDirectories()
    {
        DataLocation.ResetCache();

        var sub = DataLocation.SubPath(Path.Combine("models", "agent"));

        Assert.True(Directory.Exists(Path.GetDirectoryName(sub)!));
    }

    // ── ResetCache ────────────────────────────────────────────────────────────

    [Fact]
    public void ResetCache_AllowsNewResolution()
    {
        DataLocation.ResetCache();
        var first = DataLocation.GetUserDataPath();

        DataLocation.ResetCache();
        var second = DataLocation.GetUserDataPath();

        Assert.Equal(first, second);
    }

    // ── Portable mode — via ExeDirOverride ────────────────────────────────────
    //
    // Uses ExeDirOverride to inject a controlled temp directory, so this test
    // is fully isolated from the real exe dir and works in any CI environment.

    [Fact]
    public void PortableMode_UserDataPath_IsInsideExeDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"velo-portable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Inject a controlled exe dir so both IsPortable and GetUserDataPath
            // resolve the same base, regardless of AppContext.BaseDirectory.
            DataLocation.ResetCache();
            DataLocation.ExeDirOverride = tempDir;

            Assert.False(DataLocation.IsPortable, "No flag yet");

            File.WriteAllText(Path.Combine(tempDir, "portable.flag"), "");

            Assert.True(DataLocation.IsPortable, "Flag exists → portable");

            var path = DataLocation.GetUserDataPath();
            Assert.StartsWith(tempDir, path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("userdata", path, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            DataLocation.ResetCache();   // clears ExeDirOverride too
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
