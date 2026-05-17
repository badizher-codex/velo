using System.Text.RegularExpressions;
using Xunit;

namespace VELO.Smoke.Tests;

/// <summary>
/// Lesson #24 (v2.4.55) — WPF <c>&lt;Control IsChecked="True" Checked="Handler"/&gt;</c>
/// fires <c>Handler</c> DURING <c>InitializeComponent()</c> the moment
/// <c>IsChecked="True"</c> binds. If <c>Handler</c> dereferences a named widget
/// that's defined LATER in the XAML, the named field is still null at that
/// point and the call throws <c>NullReferenceException</c>. The crash is silent
/// when the constructor caller has a swallowing <c>try/catch</c> — which is
/// exactly what hid the Council disclaimer NRE through 6 releases.
///
/// This test scans every <c>x:Class</c>'d XAML in <c>src/</c> for the triggering
/// pattern, identifies named widgets declared after the trigger element, and
/// fails if the matching handler (or any single-hop method it calls in the
/// same code-behind) dereferences the LATEST-declared one without a null
/// guard. Guarding the latest-declared field is sufficient because BAML
/// populates named fields in document order via <c>Connect()</c> — if the
/// last field is non-null, every earlier-declared one is too.
///
/// Pure regex + file IO. No WPF runtime, no STA dispatcher.
///
/// Known limits (acceptable for v0.1):
///   • Single-hop call graph only. A handler that calls A which calls B does
///     not get B's body inspected. If you add a two-hop dispatch in a hot
///     handler, deepen the recursion below.
///   • False negatives possible when handlers route through interfaces or
///     delegates. The bug class we're guarding against fires synchronously
///     during BAML loading, so direct + single-hop covers the realistic shapes.
///   • Recognises <c>if (X is null) return;</c>, <c>if (X == null) return;</c>,
///     <c>null == X</c>, and the null-conditional <c>X?.Member</c> as guards.
///     Other forms (e.g. <c>X ?? defaultValue</c>) are not currently recognised.
/// </summary>
public class XamlInitOrderSmokeTests
{
    [Fact]
    public void Checked_handlers_that_fire_during_init_must_null_guard_latest_referenced_widget()
    {
        var srcRoot = LocateSrcRoot();
        var violations = new List<string>();

        foreach (var xamlPath in EnumerateXamlWithCodeBehind(srcRoot))
        {
            var xaml = File.ReadAllText(xamlPath);
            var csPath = xamlPath + ".cs";
            var cs = File.ReadAllText(csPath);

            // Collect every x:Name'd widget with its byte offset. namedAt is in
            // document order, so later index = later Connect() call.
            var namedAt = NamedWidgetPositions(xaml);
            if (namedAt.Count == 0) continue;

            // Look for the bug-class trigger: a single element that carries both
            // IsChecked="True" AND a Checked/Unchecked handler attribute. Other
            // event handlers (Click, SelectionChanged, etc.) do not fire during
            // BAML loading the way Checked does on the initial True binding, so
            // they're out of scope for this lesson.
            foreach (var trig in TriggerSites(xaml))
            {
                // Fields declared strictly LATER than the trigger element.
                // The trigger's own x:Name is excluded — Connect() runs before
                // attribute application for the element being built, so the
                // field is non-null by the time Checked fires for it.
                var laterFields = namedAt
                    .Where(n => n.Position > trig.Position && n.Name != trig.OwnName)
                    .ToList();
                if (laterFields.Count == 0) continue;

                CheckHandler(cs, trig.Handler, laterFields,
                             callerChain: trig.Handler,
                             xamlPath, violations, depthRemaining: 1);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} XAML init-order violation(s) — lesson #24:\n  " +
            string.Join("\n  ", violations));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private record NamedWidget(string Name, int Position);
    private record Trigger(string Handler, int Position, string? OwnName);

    private static readonly Regex NamedWidgetRegex =
        new(@"x:Name\s*=\s*""(?<name>[A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);

    private static readonly Regex TriggerElementRegex =
        new(@"<[A-Za-z_][\w:.]*\b(?<attrs>[^>]*?)/?>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex HasIsCheckedTrueRegex =
        new(@"\bIsChecked\s*=\s*""True""", RegexOptions.Compiled);

    private static readonly Regex CheckedHandlerRegex =
        new(@"\b(?:Checked|Unchecked)\s*=\s*""(?<handler>[A-Za-z_][A-Za-z0-9_]*)""",
            RegexOptions.Compiled);

    private static readonly Regex InlineNameRegex =
        new(@"\bx:Name\s*=\s*""(?<name>[A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);

    private static List<NamedWidget> NamedWidgetPositions(string xaml)
    {
        var result = new List<NamedWidget>();
        foreach (Match m in NamedWidgetRegex.Matches(xaml))
            result.Add(new NamedWidget(m.Groups["name"].Value, m.Index));
        return result;
    }

    private static IEnumerable<Trigger> TriggerSites(string xaml)
    {
        foreach (Match el in TriggerElementRegex.Matches(xaml))
        {
            var attrs = el.Groups["attrs"].Value;
            if (!HasIsCheckedTrueRegex.IsMatch(attrs)) continue;
            var h = CheckedHandlerRegex.Match(attrs);
            if (!h.Success) continue;
            var own = InlineNameRegex.Match(attrs);
            yield return new Trigger(
                Handler: h.Groups["handler"].Value,
                Position: el.Index,
                OwnName: own.Success ? own.Groups["name"].Value : null);
        }
    }

    private static void CheckHandler(
        string cs, string methodName, List<NamedWidget> laterFields,
        string callerChain, string xamlPath,
        List<string> violations, int depthRemaining)
    {
        if (!TryExtractMethodBody(cs, methodName, out var body)) return;

        // Find every later-field actually mentioned in the body (word-boundary
        // match avoids hitting substrings like AcceptButtonStyle).
        var referenced = laterFields
            .Where(f => Regex.IsMatch(body, $@"\b{Regex.Escape(f.Name)}\b"))
            .OrderByDescending(f => f.Position)
            .ToList();

        if (referenced.Count > 0)
        {
            // Guarding the LATEST-declared field is sufficient: BAML populates
            // named fields in document order, so the latest being non-null
            // implies every earlier one is too. If the latest is unguarded,
            // emit one violation that names it.
            var latest = referenced[0];
            if (!HasNullGuard(body, latest.Name))
            {
                violations.Add(
                    $"{Path.GetFileName(xamlPath)} :: {callerChain} touches '{latest.Name}' " +
                    "(latest later-declared field referenced) without an `is null`/`== null`/`?.` guard.");
            }
        }

        if (depthRemaining <= 0) return;

        // Single-hop: any PascalCase call inside body that resolves to a method
        // defined in the same code-behind file gets walked once. depthRemaining=1
        // by design — see class-doc "known limits".
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match call in Regex.Matches(body, @"\b(?<name>[A-Z][A-Za-z0-9_]*)\s*\("))
        {
            var callee = call.Groups["name"].Value;
            if (callee == methodName || !seen.Add(callee)) continue;
            if (!IsMethodDefinedIn(cs, callee)) continue;
            CheckHandler(cs, callee, laterFields,
                         callerChain: $"{callerChain} → {callee}",
                         xamlPath, violations, depthRemaining - 1);
        }
    }

    private static bool TryExtractMethodBody(string cs, string methodName, out string body)
    {
        body = string.Empty;
        var sigRegex = new Regex(
            $@"(?:public|private|internal|protected)?\s*(?:async\s+)?(?:void|Task|Task<[^>]+>|ValueTask|ValueTask<[^>]+>)\s+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.Compiled);
        var sig = sigRegex.Match(cs);
        if (!sig.Success) return false;
        var brace = cs.IndexOf('{', sig.Index + sig.Length);
        if (brace < 0) return false;
        var end = FindMatchingBrace(cs, brace);
        if (end < 0) return false;
        body = cs.Substring(brace, end - brace + 1);
        return true;
    }

    private static bool IsMethodDefinedIn(string cs, string methodName)
        => Regex.IsMatch(
            cs,
            $@"(?:public|private|internal|protected)?\s*(?:async\s+)?(?:void|Task|Task<[^>]+>|ValueTask|ValueTask<[^>]+>)\s+{Regex.Escape(methodName)}\s*\(");

    private static bool HasNullGuard(string body, string fieldName)
    {
        var esc = Regex.Escape(fieldName);
        return Regex.IsMatch(body, $@"\b{esc}\s+is\s+null\b")
            || Regex.IsMatch(body, $@"\b{esc}\s*==\s*null\b")
            || Regex.IsMatch(body, $@"\bnull\s*==\s*{esc}\b")
            || Regex.IsMatch(body, $@"\b{esc}\?\.");
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 1;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (int i = openBrace + 1; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
            if (inBlockComment) { if (c == '*' && next == '/') { inBlockComment = false; i++; } continue; }
            if (inString) { if (c == '\\') { i++; continue; } if (c == '"') inString = false; continue; }
            if (inChar)   { if (c == '\\') { i++; continue; } if (c == '\'') inChar = false; continue; }

            if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (c == '"') { inString = true; continue; }
            if (c == '\'') { inChar = true; continue; }

            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static IEnumerable<string> EnumerateXamlWithCodeBehind(string srcRoot)
        => Directory
            .EnumerateFiles(srcRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => File.Exists(p + ".cs"));

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
