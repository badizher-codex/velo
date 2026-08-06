using System.Text.RegularExpressions;
using Xunit;

namespace VELO.Smoke.Tests;

/// <summary>
/// Static analysis of the v2.5.0 theme system. Same technique as
/// <see cref="XamlResourceTests"/> — pure regex over the .xaml sources, no WPF
/// runtime — guarding the three invariants that make a live light/dark swap
/// work, each of which fails silently rather than loudly:
///
///   1. Dark.xaml and Light.xaml must define the SAME key set. A key present
///      in one and missing from the other resolves to null after a swap, and
///      DynamicResource does not throw — the element just paints nothing.
///   2. Every {DynamicResource X} must resolve to some x:Key="X". Same silent
///      failure. (StaticResource at least crashed loudly, which is what
///      XamlResourceTests was written for.)
///   3. Controls.xaml must not reach a colour token through StaticResource.
///      StaticResource is baked in at parse time, so any control painted that
///      way keeps its pre-swap brush forever. This is the invariant that the
///      pre-v2.5 codebase violated 628 times.
/// </summary>
public class ThemeParityTests
{
    private static readonly Regex KeyDefRx =
        new(@"x:Key\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void Light_And_Dark_Define_The_Same_Tokens()
    {
        var themes = Path.Combine(LocateSrcRoot(), "VELO.UI", "Themes");
        var dark  = KeysIn(Path.Combine(themes, "Dark.xaml"));
        var light = KeysIn(Path.Combine(themes, "Light.xaml"));

        Assert.NotEmpty(dark);

        var onlyInDark  = dark.Except(light).OrderBy(k => k).ToArray();
        var onlyInLight = light.Except(dark).OrderBy(k => k).ToArray();

        Assert.True(onlyInDark.Length == 0 && onlyInLight.Length == 0,
            "Theme dictionaries have drifted apart.\n" +
            $"  Only in Dark.xaml ({onlyInDark.Length}): {string.Join(", ", onlyInDark)}\n" +
            $"  Only in Light.xaml ({onlyInLight.Length}): {string.Join(", ", onlyInLight)}");
    }

    [Fact]
    public void Every_DynamicResource_Reference_Has_A_Definition_Somewhere()
    {
        var files = XamlFiles();

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
            foreach (Match m in KeyDefRx.Matches(File.ReadAllText(file)))
                defined.Add(m.Groups[1].Value);

        var refRx = new Regex(@"\{DynamicResource\s+([^\s}]+)\s*\}", RegexOptions.Compiled);
        var missing = new List<string>();
        foreach (var file in files)
        {
            foreach (Match m in refRx.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!defined.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: {{DynamicResource {key}}}");
            }
        }

        Assert.True(missing.Count == 0,
            $"Found {missing.Count} unresolvable DynamicResource reference(s) — these paint nothing at runtime:\n  " +
            string.Join("\n  ", missing.Distinct()));
    }

    [Fact]
    public void Control_Styles_Reach_Colours_Only_Through_DynamicResource()
    {
        var controls = Path.Combine(LocateSrcRoot(), "VELO.UI", "Themes", "Controls.xaml");
        var text = File.ReadAllText(controls);

        // Colour tokens all end in "Brush" or are one of the named shadows.
        var offenders = new Regex(@"\{StaticResource\s+(\w*Brush|Shadow\w+)\s*\}", RegexOptions.Compiled)
            .Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(k => k)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Controls.xaml reaches a theme colour through StaticResource. Those resolve once at " +
            "parse time and will keep their pre-swap value forever:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Views_Do_Not_Hardcode_Colours()
    {
        // Third-party brand colours are exempt: Claude orange, OpenAI green and
        // Twitter blue in the Council disclaimer identify someone else's
        // product and must NOT drift with VELO's theme.
        var allowedFiles = new[] { "CouncilFirstRunDisclaimer.xaml" };

        var hexRx = new Regex(@"#[0-9A-Fa-f]{6,8}\b", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            var name = Path.GetFileName(file);
            // The theme dictionaries are where the literals are SUPPOSED to live.
            if (file.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}")) continue;
            if (allowedFiles.Contains(name)) continue;

            foreach (Match m in hexRx.Matches(File.ReadAllText(file)))
                offenders.Add($"{name}: {m.Value}");
        }

        Assert.True(offenders.Count == 0,
            $"Found {offenders.Count} hardcoded colour(s) outside the theme dictionaries — " +
            "these stay put when the theme changes:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Resource_Keys_Looked_Up_From_CSharp_Still_Exist()
    {
        // The hole the v2.5.0 migration fell through. Rewriting every
        // {StaticResource X} in the XAML left twelve resource-key names that
        // live as C# string literals — FindResource("BadgeGreenBrush") and
        // friends — pointing at keys that no longer existed. The build stayed
        // green, all 721 tests passed, and VELO died on launch with
        // "Unable to cast MS.Internal.NamedObject to Brush" the first time
        // UrlBar.SetAiStatus ran.
        //
        // Nothing static could have caught it: a resource key in a string is
        // invisible to the compiler and to every XAML-only check.
        //
        // Limit: this only sees literals. Keys held in a variable are covered
        // instead by routing them through ThemePalette.Keys constants, which
        // turns a rename into a build error.
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in XamlFiles())
            foreach (Match m in KeyDefRx.Matches(File.ReadAllText(file)))
                defined.Add(m.Groups[1].Value);

        var lookupRx = new Regex(
            @"(?:FindResource|TryFindResource)\s*\(\s*""(\w+)""\s*\)|Resources\s*\[\s*""(\w+)""\s*\]",
            RegexOptions.Compiled);

        var missing = new List<string>();
        foreach (var file in CsFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in lookupRx.Matches(text))
            {
                var key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (!defined.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: \"{key}\"");
            }
        }

        Assert.True(missing.Count == 0,
            $"Found {missing.Count} C# resource lookup(s) naming a key that no longer exists — " +
            "these throw or return UnsetValue at runtime:\n  " + string.Join("\n  ", missing));
    }

    private static HashSet<string> KeysIn(string path)
    {
        Assert.True(File.Exists(path), $"Theme dictionary missing: {path}");
        return KeyDefRx.Matches(File.ReadAllText(path))
                       .Select(m => m.Groups[1].Value)
                       .ToHashSet(StringComparer.Ordinal);
    }

    private static string[] CsFiles() =>
        Directory.GetFiles(LocateSrcRoot(), "*.cs", SearchOption.AllDirectories)
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                 .ToArray();

    private static string[] XamlFiles() =>
        Directory.GetFiles(LocateSrcRoot(), "*.xaml", SearchOption.AllDirectories)
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                 .ToArray();

    private static string LocateSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/ — searched up from " + AppContext.BaseDirectory);
    }
}
