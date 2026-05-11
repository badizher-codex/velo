using System.Text.RegularExpressions;
using Xunit;

namespace VELO.Smoke.Tests;

/// <summary>
/// Wiring smoke tests — born out of three latent bugs the existing test
/// suite missed because it only covers pure C# logic, never the gluing
/// between DI and the host UI:
///
///   • Lesson #8  (v2.1.0 → v2.4.14, 6 months) — IA menu invisible because
///     MainWindow registered <c>ContextMenuBuilder</c> in DI but never
///     called <c>BrowserTab.SetContextMenuBuilder()</c>. Events fired with
///     no subscriber.
///   • Lesson #11 (v2.4.0 → v2.4.18, 18 days) — BookmarkAIService wired to
///     a ChatDelegate in DI but the bookmark-save call-site never invoked
///     it. Service was registered AND resolved AND chat-wired, just never
///     called.
///   • Lesson #12 (v2.4.16 → v2.4.19, 9 days) — RequestPaste was an event
///     on a DI singleton; every BrowserTab subscribed; pegar in tab A
///     also fired the handler in tab B because events on singletons
///     broadcast by construction.
///
/// All three are pure regex / file-scan. No WPF runtime, no STA dispatcher,
/// no DI container — same shape as <see cref="XamlResourceTests"/>.
/// They miss the "service is resolved but its key method never called"
/// case (lesson #11 part B) — that needs an AST parser. Future work.
/// </summary>
public class WiringSmokeTests
{
    // ── Test 1 — every BrowserTab setter has at least one call-site ──────

    [Fact]
    public void BrowserTab_setter_methods_must_be_called_from_host()
    {
        // Lesson #8: BrowserTab.SetContextMenuBuilder existed and the
        // builder was DI-registered, but MainWindow never called it.
        // Assert that every BrowserTab.SetX(Y) public method has a
        // matching ".SetX(" call in the host (MainWindow.xaml.cs) or in
        // any per-tab controller under src/VELO.App/Controllers/. The
        // search widened after v2.4.30 extracted the wiring ladder to
        // BrowserTabHost — the principle of the check is unchanged.

        var srcRoot      = LocateSrcRoot();
        var browserTab   = Path.Combine(srcRoot, "VELO.UI", "Controls", "BrowserTab.xaml.cs");
        var mainWindow   = Path.Combine(srcRoot, "VELO.App", "MainWindow.xaml.cs");
        var controllerDir = Path.Combine(srcRoot, "VELO.App", "Controllers");

        Assert.True(File.Exists(browserTab),  $"BrowserTab not found at {browserTab}");
        Assert.True(File.Exists(mainWindow),  $"MainWindow not found at {mainWindow}");

        var hostSources = new List<string> { File.ReadAllText(mainWindow) };
        if (Directory.Exists(controllerDir))
        {
            hostSources.AddRange(
                Directory.GetFiles(controllerDir, "*.cs", SearchOption.AllDirectories)
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    .Select(File.ReadAllText));
        }

        var btContent = File.ReadAllText(browserTab);

        // Match `public void SetXxx(...)` declarations on BrowserTab.
        var setterRx = new Regex(
            @"public\s+void\s+(Set[A-Z]\w*)\s*\(",
            RegexOptions.Compiled);

        var setters = setterRx.Matches(btContent)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(setters); // sanity — sprint code should have setters

        var orphans = new List<string>();
        foreach (var setter in setters)
        {
            // Look for a call-site like `.SetXxx(` in MainWindow or any
            // controller under src/VELO.App/Controllers/.
            var callRx = new Regex($@"\.{Regex.Escape(setter)}\s*\(", RegexOptions.Compiled);
            if (!hostSources.Any(callRx.IsMatch))
                orphans.Add(setter);
        }

        Assert.True(orphans.Count == 0,
            $"BrowserTab declares {orphans.Count} setter(s) with no call-site in MainWindow or any controller:\n  " +
            string.Join("\n  ", orphans.Select(s => $"BrowserTab.{s}(...) — never called from the host")));
    }

    // ── Test 2 — every DI-registered AI service is resolved somewhere ────

    [Fact]
    public void DI_registered_AI_services_must_be_resolved_in_App_or_UI()
    {
        // Lesson #11 (part A): a service can be registered in DI but never
        // resolved by anything except the registration itself. Detects the
        // "dormant service" smell. Doesn't catch services that are resolved
        // but whose methods are never called — that needs an AST parser.

        var srcRoot     = LocateSrcRoot();
        var depConfig   = Path.Combine(srcRoot, "VELO.App", "Startup", "DependencyConfig.cs");
        Assert.True(File.Exists(depConfig), $"DependencyConfig not found at {depConfig}");

        var depContent = File.ReadAllText(depConfig);

        // Find every `services.AddSingleton<VELO.X.Y>()` (no factory).
        // Skip generic-with-factory like AddSingleton<X>(sp => ...) — those
        // already have a custom resolver and don't fit the dormant smell.
        var registerRx = new Regex(
            @"services\.AddSingleton<\s*(VELO\.(?:Agent|Security|UI\.Controls|Core\.AI)\.[A-Za-z0-9_.]+)\s*>\s*\(\s*\)",
            RegexOptions.Compiled);

        var registered = registerRx.Matches(depContent)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(registered); // sanity

        // Search for resolution in App + UI source dirs (excludes the
        // DependencyConfig itself).
        var appUiFiles = new[]
            {
                Path.Combine(srcRoot, "VELO.App"),
                Path.Combine(srcRoot, "VELO.UI"),
            }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !p.EndsWith("DependencyConfig.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var dormants = new List<string>();
        foreach (var fqn in registered)
        {
            // Allow services explicitly documented as "registered ahead of UI
            // wiring" in the project state. Each entry is a deliberate park —
            // when the corresponding sprint UI lands, remove the entry and
            // the test will re-prove the wiring stayed connected.
            if (_knownDeferredServices.Contains(fqn)) continue;

            var shortName = fqn.Split('.').Last();

            // A resolution looks like one of:
            //   GetRequiredService<X>()  / GetService<X>()
            //   .GetRequiredService<VELO.X.Y>()
            //   constructor parameter typed `X`
            //   field/var typed `X`
            // For simplicity, count any non-comment occurrence of the short
            // type name as a candidate. This matches the same heuristic
            // BrowserTab uses for short-name references throughout the file.
            var typeRefRx = new Regex(@"\b" + Regex.Escape(shortName) + @"\b", RegexOptions.Compiled);

            bool found = false;
            foreach (var file in appUiFiles)
            {
                var content = File.ReadAllText(file);
                if (typeRefRx.IsMatch(content)) { found = true; break; }
            }

            if (!found) dormants.Add(fqn);
        }

        Assert.True(dormants.Count == 0,
            $"DI registers {dormants.Count} service(s) with no consumer in App/UI:\n  " +
            string.Join("\n  ", dormants));
    }

    // ── Test 3 — events on DI singletons stay in a known snapshot ────────

    [Fact]
    public void Events_on_DI_singletons_match_known_snapshot()
    {
        // Lesson #12: events on a DI singleton broadcast to every subscriber,
        // by construction. v2.4.16 added RequestPaste; every BrowserTab
        // subscribed; pegar in tab A leaked into tab B. Removed in v2.4.19
        // by switching to a per-build callback.
        //
        // This test snapshots which singleton classes expose which events.
        // Adding a new event to a singleton is now a deliberate act: the
        // snapshot has to be edited and the dev should justify (in a
        // comment in this file) why broadcast semantics are intentional
        // for that event. Removing the diff updates the allowlist.
        //
        // The snapshot lives below as `_expectedSingletonEvents`. To update,
        // edit it and re-run the test.

        var srcRoot   = LocateSrcRoot();
        var depConfig = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "Startup", "DependencyConfig.cs"));

        // Collect every singleton-registered FQN (with or without factory).
        var registerRx = new Regex(
            @"services\.AddSingleton<\s*(VELO\.[A-Za-z0-9_.]+)\s*>",
            RegexOptions.Compiled);

        var singletonShortNames = registerRx.Matches(depConfig)
            .Select(m => m.Groups[1].Value.Split('.').Last())
            .ToHashSet();

        // Walk every .cs file under src/ and pick the ones whose primary
        // class name matches a singleton FQN.
        var actual = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var classRx = new Regex(@"public\s+(?:sealed\s+|abstract\s+|static\s+)?class\s+(\w+)",
                                RegexOptions.Compiled);
        var eventRx = new Regex(@"public\s+(?:static\s+)?event\s+[^\s]+(?:<[^>]+>)?\??\s+(\w+)\s*[;{]",
                                RegexOptions.Compiled);

        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                              .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                              .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var content = File.ReadAllText(file);
            var classMatch = classRx.Match(content);
            if (!classMatch.Success) continue;
            var className = classMatch.Groups[1].Value;
            if (!singletonShortNames.Contains(className)) continue;

            var eventNames = eventRx.Matches(content)
                .Select(m => m.Groups[1].Value)
                .ToList();

            if (eventNames.Count == 0) continue;
            actual[className] = new SortedSet<string>(eventNames, StringComparer.Ordinal);
        }

        // Compare with the expected snapshot. Build a deterministic diff.
        var diff = new List<string>();

        foreach (var (cls, events) in actual)
        {
            if (!_expectedSingletonEvents.TryGetValue(cls, out var expected))
            {
                diff.Add($"NEW singleton with events: {cls} → [{string.Join(", ", events)}]");
                continue;
            }
            var added   = events.Except(expected).ToList();
            var removed = expected.Except(events).ToList();
            foreach (var e in added)
                diff.Add($"NEW event on {cls}: {e}");
            foreach (var e in removed)
                diff.Add($"REMOVED event on {cls}: {e} (update snapshot)");
        }
        foreach (var cls in _expectedSingletonEvents.Keys.Except(actual.Keys))
            diff.Add($"REMOVED singleton-with-events: {cls} (update snapshot)");

        Assert.True(diff.Count == 0,
            "DI-singleton event surface drifted from the approved snapshot. " +
            "Each new event broadcasts to every subscriber by construction — " +
            "if you really need it, edit _expectedSingletonEvents below and " +
            "leave a comment justifying broadcast semantics.\n  " +
            string.Join("\n  ", diff));
    }

    /// <summary>
    /// Services that are intentionally registered ahead of their UI wiring,
    /// per the project_phase3_state.md "deferred" section. Remove from this
    /// set when the corresponding sprint ships the call-site so the test
    /// guards against the wiring regressing afterwards.
    /// </summary>
    private static readonly HashSet<string> _knownDeferredServices = new(StringComparer.Ordinal)
    {
        // Sprint 8C wired in v2.4.22 — toast subscriber lives in MainWindow.
        // No deferred entries at the moment. Future sprints add services
        // ahead of UI here with a comment naming the gating sprint.
    };

    /// <summary>
    /// Approved set of public events on DI-singleton classes. New entries
    /// require a comment justifying that broadcast semantics are intended
    /// (i.e. all subscribers SHOULD react). Examples of intentional
    /// broadcast: state-changed notifications fanned out to every UI panel.
    /// Examples that should NOT be on this list: per-target callbacks like
    /// "paste this into the tab that asked" — those go through a method
    /// parameter (see ContextMenuBuilder.Build's onPaste argument, v2.4.19).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>>
        _expectedSingletonEvents = new Dictionary<string, IReadOnlySet<string>>
    {
        // 18 menu-action requests. Each is fanned out to one subscriber
        // (MainWindow) per host window today. Broadcast safe as long as
        // tear-off windows continue to wire one ContextMenuBuilder per
        // window — keep an eye on this if multi-window state ever shares
        // the same builder.
        ["ContextMenuBuilder"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "RequestNewWindow",      "RequestGlance",         "RequestLinkAnalysis",
            "RequestMalwaredexCheck","RequestBookmark",       "RequestImageAnalysis",
            "RequestSearch",         "RequestAgentPrompt",    "RequestSaveAs",
            "RequestPrint",          "RequestViewSource",     "RequestDevTools",
            "RequestSecurityInspector","RequestPrivacyReceipt","RequestAIReanalysis",
            "RequestForgetSite",     "RequestReaderMode",     "RequestTemporaryContainer",
        },

        // AIActionRequested — fired when the user picks an AI menu item.
        // Single subscriber (MainWindow) opens AIResultWindow. Intentional.
        ["AIContextMenuBuilder"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "AIActionRequested",
        },

        // NarrationReady — Sprint 8C narration toast. Currently no UI
        // subscriber (deferred); when the toast WPF lands it should be the
        // only consumer, so broadcast is fine.
        ["BlockNarrationService"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "NarrationReady",
        },

        // PropertyChanged — INotifyPropertyChanged plumbing for WPF binding.
        // Intentional broadcast: every binding observer must see updates.
        ["ThreatsPanelViewModel"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "PropertyChanged",
        },

        // EntryAdded — Sprint 9D v2.4.23. The clipboard history is a single
        // process-wide buffer; broadcasting "new entry" to every subscriber
        // is correct (the dialog refreshes its list when open, future
        // subscribers like a tray indicator would just append). Open the
        // history dialog isn't subscribed when closed — handler is detached
        // on Window.Closed.
        ["ClipboardHistory"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "EntryAdded",
        },
    };

    // ── Helpers ──────────────────────────────────────────────────────────

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
