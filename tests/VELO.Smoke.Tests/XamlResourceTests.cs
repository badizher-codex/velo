using System.Text.RegularExpressions;
using Xunit;

namespace VELO.Smoke.Tests;

/// <summary>
/// Static analysis smoke test — born out of the v2.4.0 → v2.4.2 disaster
/// where a missing <c>{StaticResource DropShadowEffect}</c> reference in
/// AutofillToast.xaml caused the entire app to crash on launch with a
/// XamlParseException. The reference had ALSO existed in
/// PrivacyReceiptToast.xaml since Phase 2 but never blew up because that
/// control was never lifted into MainWindow's visual tree. As soon as
/// AutofillToast (Sprint 5, v2.1.5) was added to MainWindow.xaml directly,
/// every release v2.1.5 → v2.4.1 was DOA and we didn't notice in CI
/// because:
///
///   • <c>dotnet build</c> compiles XAML to BAML successfully — no error
///   • all 303 unit tests are pure C# / no WPF, so they didn't catch it
///   • StaticResource lookups only happen at <c>Application.Run</c>, which
///     none of our tests exercise
///
/// This test scans every <c>.xaml</c> file under <c>src/</c> for
/// <c>{StaticResource X}</c> references and verifies that <c>X</c> is
/// defined as <c>x:Key="X"</c> somewhere in the codebase. Pure regex —
/// no XAML parser, no WPF runtime, no STA dispatcher. Misses some edge
/// cases (DynamicResource, runtime-injected resources, scoped lookups
/// in nested resource dictionaries) but cleanly catches the class of
/// bug that took 7 hotfixes to recover from.
///
/// If this test had existed in v2.1.5, the v2.4.0 → v2.4.2 chain would
/// have been a single release.
/// </summary>
public class XamlResourceTests
{
    [Fact]
    public void Every_StaticResource_Reference_Has_A_Definition_Somewhere()
    {
        var srcRoot = LocateSrcRoot();
        var xamlFiles = Directory.GetFiles(srcRoot, "*.xaml", SearchOption.AllDirectories)
            // skip generated files inside obj/
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        Assert.NotEmpty(xamlFiles); // sanity — must have found something

        // ── Pass 1: collect every defined key ───────────────────────────
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var keyDefRx = new Regex(@"x:Key\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in keyDefRx.Matches(content))
                defined.Add(m.Groups[1].Value);
        }

        // ── Pass 2: every {StaticResource X} must hit the defined set ───
        var staticRefRx = new Regex(@"\{StaticResource\s+([^\s}]+)\s*\}", RegexOptions.Compiled);
        var missing = new List<string>();
        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            var fileLabel = Path.GetFileName(file);
            foreach (Match m in staticRefRx.Matches(content))
            {
                var key = m.Groups[1].Value;
                if (!defined.Contains(key))
                    missing.Add($"{fileLabel}: {{StaticResource {key}}}");
            }
        }

        Assert.True(missing.Count == 0,
            $"Found {missing.Count} unresolvable StaticResource reference(s):\n  " +
            string.Join("\n  ", missing));
    }

    [Fact]
    public void All_x_Class_Code_Behind_Is_Reachable()
    {
        // Defensive twin to the StaticResource test: every Window/UserControl
        // XAML declares an x:Class. The matching .xaml.cs must exist for the
        // build to succeed in WPF — but a stray rename leaves dangling files.
        // Catch it here instead of via a silent build warning.
        var srcRoot = LocateSrcRoot();
        var xamlFiles = Directory.GetFiles(srcRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !p.EndsWith("App.xaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}"))
            .ToArray();

        var classRx = new Regex(@"x:Class\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        var orphans = new List<string>();

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            var m = classRx.Match(content);
            if (!m.Success) continue;
            // Sibling .xaml.cs must exist
            var codeBehind = file + ".cs";
            if (!File.Exists(codeBehind))
                orphans.Add($"{Path.GetFileName(file)} declares x:Class={m.Groups[1].Value} but {Path.GetFileName(codeBehind)} is missing");
        }

        Assert.True(orphans.Count == 0,
            $"Found {orphans.Count} orphan XAML file(s) with no code-behind:\n  " +
            string.Join("\n  ", orphans));
    }

    /// <summary>
    /// Walks up from the test binary's directory to find the repo root
    /// (where the <c>src/</c> folder lives). Robust to running from
    /// <c>bin/Debug/net8.0-windows/</c> or wherever the test runner places
    /// us.
    /// </summary>
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
