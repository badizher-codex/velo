using System.Reflection;
using VELO.Core;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Tests for DataLocation.
///
/// Note: DataLocation caches the resolved path in a static field.
/// Each test that calls GetUserDataPath() must reset the cache afterwards
/// via the internal ResetCache() method to avoid polluting other tests.
/// </summary>
public class DataLocationTests : IDisposable
{
    // Scratch directory for portable-mode tests
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"velo-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        DataLocation.ResetCache();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── IsPortable — no flag ──────────────────────────────────────────────────

    [Fact]
    public void IsPortable_ReturnsFalse_WhenNoFlagExists()
    {
        // The test assembly runs in the test output dir — no portable.flag there
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

        // Both should resolve to the same path (same environment)
        Assert.Equal(first, second);
    }

    // ── GetUserDataPath — portable mode (simulated) ───────────────────────────
    //
    // We cannot place portable.flag next to the test assembly without
    // affecting other tests permanently, so we verify the portable path
    // logic by inspecting what the method WOULD return via the IsPortable
    // property after manually creating the flag in the exe dir.
    //
    // This test is skipped if it cannot write to the test output directory.

    [Fact]
    public void PortableMode_UserDataPath_IsInsideExeDir()
    {
        // Use typeof(DataLocation).Assembly so we resolve the same directory that
        // DataLocation.GetExeDir() resolves internally (VELO.Core.dll location).
        // In CI the test runner may load VELO.Core.dll from a different path than
        // VELO.Core.Tests.dll, so using GetExecutingAssembly() from the test method
        // would point to a different directory.
        var exeDir = Path.GetDirectoryName(typeof(DataLocation).Assembly.Location)!;
        var flagPath = Path.Combine(exeDir, "portable.flag");

        // Only run if we can write to the exe dir (skipped in read-only CI artifacts)
        if (!CanWriteTo(exeDir)) return;

        try
        {
            File.WriteAllText(flagPath, "");
            DataLocation.ResetCache();

            Assert.True(DataLocation.IsPortable);

            var path = DataLocation.GetUserDataPath();
            Assert.StartsWith(exeDir, path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("userdata", path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(flagPath);
            DataLocation.ResetCache();
        }
    }

    private static bool CanWriteTo(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
