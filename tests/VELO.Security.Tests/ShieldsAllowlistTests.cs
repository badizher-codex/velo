using VELO.Security.Guards;
using Xunit;

namespace VELO.Security.Tests;

public class ShieldsAllowlistTests
{
    private static ShieldsAllowlist Build(params string[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"velo-allowlist-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, entries);

        // Mirror the bundled-file lookup the loader does in production by
        // dropping our temp file under <appDir>/resources/blocklists/.
        var appDir = Path.Combine(Path.GetTempPath(), $"velo-allowlist-app-{Guid.NewGuid():N}");
        var bundleDir = Path.Combine(appDir, "resources", "blocklists");
        Directory.CreateDirectory(bundleDir);
        File.Copy(path, Path.Combine(bundleDir, "shields-allowlist.txt"));

        var allow = new ShieldsAllowlist();
        allow.Load(appDir: appDir);
        return allow;
    }

    [Fact]
    public void Matches_ExactHost()
    {
        var a = Build("homedepot.com");
        Assert.True(a.Matches("homedepot.com"));
    }

    [Fact]
    public void Matches_WwwPrefix_ViaNormalisation()
    {
        var a = Build("homedepot.com");
        Assert.True(a.Matches("www.homedepot.com"));
    }

    [Fact]
    public void Matches_DeepSubdomain()
    {
        var a = Build("homedepot.com");
        Assert.True(a.Matches("checkout.homedepot.com"));
        Assert.True(a.Matches("a.b.c.homedepot.com"));
    }

    [Fact]
    public void DoesNotMatch_LookalikeDomain()
    {
        var a = Build("homedepot.com");
        Assert.False(a.Matches("evil-homedepot.com"));
        Assert.False(a.Matches("homedepot.com.evil.tld"));
    }

    [Fact]
    public void DoesNotMatch_UnrelatedDomain()
    {
        var a = Build("homedepot.com");
        Assert.False(a.Matches("lowes.com"));
    }

    [Fact]
    public void Disabled_ReturnsFalse_ForEverything()
    {
        var a = Build("homedepot.com");
        a.Enabled = false;
        Assert.False(a.Matches("homedepot.com"));
        Assert.False(a.Matches("www.homedepot.com"));
    }

    [Fact]
    public void IgnoresCommentsAndBlankLines()
    {
        var a = Build("# header comment", "", "homedepot.com", "  ", "# another", "lowes.com");
        Assert.True(a.Matches("homedepot.com"));
        Assert.True(a.Matches("lowes.com"));
        Assert.Equal(2, a.Count);
    }

    [Fact]
    public void BuildJsConstant_EmitsAllDomains()
    {
        var a = Build("homedepot.com", "lowes.com");
        var js = a.BuildJsConstant();
        Assert.Contains("__VELO_RELAXED_DOMAINS__", js);
        Assert.Contains("\"homedepot.com\"", js);
        Assert.Contains("\"lowes.com\"", js);
        Assert.StartsWith("window.__VELO_RELAXED_DOMAINS__=[", js);
        Assert.EndsWith("];", js);
    }
}
