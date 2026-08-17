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
        // host-side search widened after v2.4.30 extracted the wiring
        // ladder to BrowserTabHost.
        //
        // The BrowserTab-side scan widened after v2.4.31 split BrowserTab.xaml.cs
        // into partial classes (BrowserTab.{xaml,PublicApi,Events,Helpers}.cs).
        // SetContainer now lives in BrowserTab.PublicApi.cs; any new setter
        // landing in a sibling partial must still be enumerable here.

        var srcRoot          = LocateSrcRoot();
        var browserTabDir    = Path.Combine(srcRoot, "VELO.UI", "Controls");
        var mainWindow       = Path.Combine(srcRoot, "VELO.App", "MainWindow.xaml.cs");
        var controllerDir    = Path.Combine(srcRoot, "VELO.App", "Controllers");

        Assert.True(Directory.Exists(browserTabDir), $"BrowserTab dir not found at {browserTabDir}");
        Assert.True(File.Exists(mainWindow),         $"MainWindow not found at {mainWindow}");

        // Read all BrowserTab partial sources (BrowserTab.xaml.cs +
        // BrowserTab.PublicApi.cs + BrowserTab.Events.cs + BrowserTab.Helpers.cs).
        // Generated .g.cs partials from XAML are excluded.
        var browserTabFiles = Directory
            .GetFiles(browserTabDir, "BrowserTab*.cs", SearchOption.TopDirectoryOnly)
            .Where(p => !p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(browserTabFiles);

        var hostSources = new List<string> { File.ReadAllText(mainWindow) };
        if (Directory.Exists(controllerDir))
        {
            hostSources.AddRange(
                Directory.GetFiles(controllerDir, "*.cs", SearchOption.AllDirectories)
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    .Select(File.ReadAllText));
        }

        var btContent = string.Concat(browserTabFiles.Select(File.ReadAllText));

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
            // v2.4.61 QW-6 — RequestImageAnalysis removed with its dead menu
            // item (0 subscribers since it shipped; DEAD-1 in the audit).
            "RequestNewWindow",      "RequestGlance",         "RequestLinkAnalysis",
            "RequestMalwaredexCheck","RequestBookmark",
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

        // Phase 4.1 chunks A+B (v2.4.41) — Council orchestrator events.
        // Council Mode is a singleton-per-process surface: ONE session
        // active at a time, every UI subscriber (transcript renderer,
        // capture-count badge, synthesis status bar) wants every event.
        // Broadcast is intentional and aligns with the orchestrator's
        // single-session contract. SynthesisReady is a strict subset of
        // MessageAppended (fires after each appended moderator message)
        // so subscribers that only care about synthesis can scope
        // themselves to it instead of filtering MessageAppended.
        ["CouncilOrchestrator"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "CaptureReceived",
            "MessageAppended",
            "SynthesisReady",
        },
    };

    // ── Phase 4.1 chunk C/D (v2.4.44) — council bridge + adapters ────────

    [Fact]
    public void CouncilBridge_script_exists_with_expectedApiSurface()
    {
        // The bridge defines a tiny stable API on window.__veloCouncil. Pin
        // the method names + outbound message types so a refactor that
        // renames "captureText" → "grabReply" or drops "council/replyDetected"
        // gets caught here instead of after a release breaks Council Mode.
        var repoRoot = LocateRepoRoot();
        var script   = Path.Combine(repoRoot, "resources", "scripts", "council-bridge.js");
        Assert.True(File.Exists(script), $"Council bridge script missing at {script}");

        var contents = File.ReadAllText(script);

        // Public API exposed on window.__veloCouncil:
        Assert.Contains("__veloCouncil",   contents);
        Assert.Contains("setAdapter",      contents);
        Assert.Contains("paste",           contents);
        Assert.Contains("send",            contents);
        Assert.Contains("captureText",     contents);
        Assert.Contains("captureCode",     contents);
        Assert.Contains("captureTable",    contents);
        Assert.Contains("captureCitation", contents);

        // Outbound message types the C# parser branches on:
        Assert.Contains("council/capture",       contents);
        Assert.Contains("council/replyDetected", contents);
        Assert.Contains("council/error",         contents);
    }

    [Fact]
    public void YouTubeAdBlock_script_exists_withExpectedSelectors()
    {
        // v2.4.53 — pin the YouTube ad-block script's existence + key
        // selector tokens so a refactor that drops the file (or removes a
        // canonical YouTube selector) trips at test time. We don't validate
        // every selector exhaustively — that's a moving target as YouTube
        // changes their DOM — just the load-bearing class names + the
        // anti-adblock element name.
        var repoRoot = LocateRepoRoot();
        var script   = Path.Combine(repoRoot, "resources", "scripts", "youtube-adblock.js");
        Assert.True(File.Exists(script), $"YouTube ad-block script missing at {script}");

        var contents = File.ReadAllText(script);

        // Host gate (no-ops on non-YouTube). Match on the regex-escaped form
        // the script uses inside the IIFE guard — literal "youtube" + "youtu"
        // are enough, the script escapes the period for regex matching.
        Assert.Contains("youtube",               contents);
        Assert.Contains("youtu",                 contents);

        // Player + skip selectors — these are the core load-bearing names.
        Assert.Contains("ad-showing",            contents);
        Assert.Contains("ytp-ad-skip-button",    contents);

        // Anti-adblock modal element (YouTube's "ad blockers not allowed").
        Assert.Contains("ytd-enforcement-message-view-model", contents);

        // The script must wire the auto-pause defence.
        Assert.Contains("addEventListener('pause'", contents);
    }

    [Fact]
    public void WebViewCloak_script_keepsPostMessage_dropsHostObjects_andIsInjected()
    {
        // v2.4.68 (F-1) — the cloak stashes the bridge as __veloBridge and
        // deletes chrome.webview so sites probing for the WebView2
        // embedding (Prime Video's app detection) see a normal browser.
        // Pin the load-bearing facts: the stash key + the delete, plus the
        // wiring facts — BrowserTab injects the file, and every VELO
        // script that posts to the host resolves the stashed bridge first
        // (lesson #21 — producer and consumer both asserted).
        var repoRoot = LocateRepoRoot();
        var script   = Path.Combine(repoRoot, "resources", "scripts", "webview-cloak.js");
        Assert.True(File.Exists(script), $"WebView cloak script missing at {script}");

        var contents = File.ReadAllText(script);
        Assert.Contains("__veloBridge",        contents);
        Assert.Contains("delete chrome.webview", contents);

        var browserTab = Path.Combine(repoRoot, "src", "VELO.UI", "Controls", "BrowserTab.xaml.cs");
        Assert.Contains("webview-cloak.js", File.ReadAllText(browserTab));

        // Every page→host postMessage callsite must prefer the stash so it
        // keeps working once chrome.webview is gone.
        foreach (var consumer in new[]
        {
            Path.Combine(repoRoot, "resources", "scripts", "autofill.js"),
            Path.Combine(repoRoot, "resources", "scripts", "council-bridge.js"),
            Path.Combine(repoRoot, "resources", "scripts", "dom-extractor.js"),
            Path.Combine(repoRoot, "resources", "scripts", "glance-hover.js"),
            Path.Combine(repoRoot, "src", "VELO.Security", "Guards", "PasteGuard.cs"),
            Path.Combine(repoRoot, "src", "VELO.UI", "Controls", "BrowserTab.Helpers.cs"),
        })
        {
            Assert.Contains("__veloBridge", File.ReadAllText(consumer));
        }
    }

    // ── Phase 6 / P1 — media detection ───────────────────────────────────

    [Fact]
    public void Every_injected_script_is_copied_to_the_output_by_the_csproj()
    {
        // A script loaded by LoadScriptResourceAsync but missing its
        // <Content Include> is never copied to the output, so the loader
        // returns null and the feature is silently inert — no exception, no
        // log line, nothing. This is not hypothetical: cookie-bypass.js and
        // dom-extractor.js sit in resources/scripts/ today with no csproj
        // entry and no loader call, which is how the failure looks from the
        // outside. This test pins the direction that actually matters: if the
        // code asks for a script, the build must ship it.

        var repoRoot = LocateRepoRoot();
        var csproj   = File.ReadAllText(Path.Combine(repoRoot, "src", "VELO.App", "VELO.App.csproj"));

        var srcFiles = Directory
            .GetFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var requested = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in srcFiles)
        {
            foreach (Match m in Regex.Matches(
                         File.ReadAllText(file),
                         @"LoadScriptResourceAsync\(\s*""([^""]+\.js)""\s*\)"))
                requested.Add(m.Groups[1].Value);
        }

        Assert.NotEmpty(requested); // sanity — the injection ladder exists

        var missing = requested
            .Where(js => !csproj.Contains($@"resources\scripts\{js}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} script(s) are loaded at runtime but have no <Content Include> in " +
            "VELO.App.csproj, so they never reach the build output and the feature is silently " +
            "inert:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_page_to_host_message_is_stringified()
    {
        // Found the hard way in P1 gate 0.5: media-detect.js posted an object
        // and NOTHING arrived — no error, no log. TryGetWebMessageAsString()
        // THROWS ArgumentException for a non-string message (WebView2 SDK
        // docs), and OnWebMessageReceived wraps its body in a try/catch that
        // swallows it. The message just vanishes.
        //
        // Any script that posts to the host must therefore stringify.

        var repoRoot = LocateRepoRoot();
        var scripts  = Directory.GetFiles(Path.Combine(repoRoot, "resources", "scripts"), "*.js");
        Assert.NotEmpty(scripts);

        // Known offender, deliberately parked rather than silently tolerated:
        // council-bridge.js posts object literals (its `post` helper at the
        // top of the file), which means every council/* message is dropped
        // before the host's fast-path ever sees it. Council Mode is paused
        // (chunk H unfinished), which is why nobody noticed. Remove this entry
        // when that is fixed — do not add new ones without the same writeup.
        var knownUnstringified = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "council-bridge.js",
        };

        var offenders = new List<string>();
        foreach (var path in scripts)
        {
            var name = Path.GetFileName(path);
            if (knownUnstringified.Contains(name)) continue;

            // Comments are stripped first. Without this, webview-cloak.js
            // trips the test on a comment explaining that postMessage is the
            // only bridge member VELO uses — it never posts anything. Same
            // trap as Every_path_that_hands_a_tab_to_the_user, and a guard
            // that fires on prose is not a guard.
            var contents = StripComments(File.ReadAllText(path));

            // Checked AT THE CALL SITE, not per file. The first version of
            // this test asked whether the file contained JSON.stringify
            // anywhere, and council-bridge.js passed it on line 177 — an
            // unrelated return value — while still posting a raw object.
            // A guard that green-lights the one file it was written for is
            // worse than no guard.
            var calls       = Regex.Matches(contents, @"\.postMessage\s*\(").Count;
            var stringified = Regex.Matches(contents, @"\.postMessage\s*\(\s*JSON\.stringify\s*\(").Count;

            if (calls > 0 && stringified < calls)
                offenders.Add($"{name} ({calls - stringified} of {calls} call(s) post a non-string)");
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} script(s) post to the host without JSON.stringify. " +
            "TryGetWebMessageAsString throws on non-string messages and the handler swallows it, " +
            "so those messages are lost silently:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void MediaDetect_script_shape_matches_the_host_parser()
    {
        // The script and MediaPageReport.TryParse are one contract split
        // across two languages. A renamed field on either side produces an
        // empty inventory forever — TryParse returns false and the handler
        // breaks out with no log. Pin both sides against the same list.

        var repoRoot = LocateRepoRoot();
        var script   = Path.Combine(repoRoot, "resources", "scripts", "media-detect.js");
        Assert.True(File.Exists(script), $"media-detect.js missing at {script}");

        var js     = File.ReadAllText(script);
        var parser = File.ReadAllText(Path.Combine(
            repoRoot, "src", "VELO.Core", "Media", "MediaPageReport.cs"));
        var events = File.ReadAllText(Path.Combine(
            repoRoot, "src", "VELO.UI", "Controls", "BrowserTab.Events.cs"));

        // The discriminator. Note it is `kind`, not `type` — `type` is taken
        // by the Council fast-path that forks before the kind switch.
        Assert.Contains("'media-detect'",       js);
        Assert.Contains("kind",                 js);
        Assert.Contains("\"media-detect\"",     parser);
        Assert.Contains("case \"media-detect\"", events);

        // Every field the parser reads must be a field the script writes.
        foreach (var field in new[]
        {
            "buffers", "mime", "appends", "bytes", "encrypted", "first", "container",
            "pssh", "sinf", "eme", "probed", "resolved", "mediaKeysAttached",
            "encryptedEvents", "elements", "tag", "srcKind", "duration", "url",
            "title",
        })
        {
            Assert.True(js.Contains($"{field}", StringComparison.Ordinal),
                $"media-detect.js does not produce the '{field}' field that MediaPageReport.TryParse reads");
            Assert.True(parser.Contains($"\"{field}\"", StringComparison.Ordinal),
                $"MediaPageReport.TryParse does not read the '{field}' field media-detect.js produces");
        }

        // §21 — the DRM verdict must describe the present, not the document's
        // history. A cumulative counter cannot go back down, and that is what
        // kept Prime Video's catalogue pages marked protected for a whole
        // session. There is no JS test harness in this repo (lesson #55: the
        // place that cannot be tested is the place the bugs live), so this
        // string guard is the only thing standing between that fix and a
        // silent regression by whoever finds `eme.setMediaKeys++` more natural.
        Assert.DoesNotContain("eme.setMediaKeys", js);
        Assert.DoesNotContain("eme.encryptedEvents++", js);
        Assert.Contains("el.mediaKeys", js);

        // Read-only means read-only: the script must never ship media bytes
        // over the bridge. It reports counts and box names.
        Assert.DoesNotContain("appendBuffer(data)", js.Replace(" ", ""));
    }

    [Fact]
    public void Media_inventory_is_produced_from_both_layers_and_consumed()
    {
        // Lesson #21 — a detector can be built, wired and completely inert
        // because nobody feeds it or nobody reads it. §9-§10 measured that
        // BOTH evidence layers are required (the network cannot see YouTube;
        // the page cannot see a progressive file's URL), so assert both
        // producers, the lifecycle reset, and a consumer.

        var srcRoot = LocateSrcRoot();
        var events  = File.ReadAllText(Path.Combine(srcRoot, "VELO.UI", "Controls", "BrowserTab.Events.cs"));
        var core    = File.ReadAllText(Path.Combine(srcRoot, "VELO.UI", "Controls", "BrowserTab.xaml.cs"));

        // Producer 1 — the network layer, via the response hook.
        Assert.Contains("WebResourceResponseReceived += OnWebResourceResponseReceived", core);
        Assert.Contains("Media.RecordResponse(", events);

        // Producer 2 — the page layer, via media-detect.js.
        Assert.Contains("media-detect.js",       core);
        Assert.Contains("Media.ApplyPageReport(", events);

        // Lifecycle — an inventory that outlives its page describes the wrong one.
        Assert.Contains("Media.Reset()", events);

        // Consumer. This used to be the measurement log, which was temporary
        // scaffolding; the real one is the URL-bar chip, so assert the chain
        // that actually reaches the screen — the tab raises, the host handles
        // it, and the handler reads the inventory into the bar.
        Assert.Contains("MediaInventoryChanged", events);

        var mainWindow = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "MainWindow.xaml.cs"));
        Assert.Contains("OnMediaInventoryChanged", mainWindow);
        Assert.Contains("SetMediaInventory(", mainWindow);
    }

    [Fact]
    public void The_media_ui_uses_only_theme_tokens_and_is_reachable_by_name()
    {
        // Lesson #57: a trait only visible in one theme has to be verified IN
        // that theme, and a hardcoded colour looks perfect in whichever theme
        // it was picked for. Rather than rely on catching that in a screenshot
        // every time, assert the mechanical property that makes both themes
        // work: every brush in the media UI is a DynamicResource role token.
        //
        // Also pins AutomationProperties on the chip. It is what a screen
        // reader announces — and, incidentally, the only reason P4 could be
        // driven and verified at runtime at all.

        var repoRoot = LocateRepoRoot();
        var panel    = File.ReadAllText(Path.Combine(repoRoot, "src", "VELO.UI", "Controls", "MediaPanel.xaml"));
        var urlBar   = File.ReadAllText(Path.Combine(repoRoot, "src", "VELO.UI", "Controls", "UrlBar.xaml"));

        // No hex literals anywhere in the panel.
        var hardcoded = Regex.Matches(panel, @"=""#[0-9A-Fa-f]{3,8}""")
            .Select(m => m.Value)
            .ToList();
        Assert.True(hardcoded.Count == 0,
            "MediaPanel.xaml hardcodes " + hardcoded.Count + " colour(s), so it cannot follow the theme swap:\n  " +
            string.Join("\n  ", hardcoded));

        // And it really does use the role tokens.
        foreach (var token in new[]
        {
            "SurfaceOverlayBrush", "SurfaceRaisedBrush", "BorderStrongBrush",
            "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush",
            "AccentBrush", "TextOnAccentBrush",
        })
        {
            Assert.Contains($"DynamicResource {token}", panel);
        }

        // The chip: named for accessibility, and both status token pairs are
        // referenced in code-behind so the DRM colour swap is not a literal.
        Assert.Contains("AutomationProperties.AutomationId=\"MediaBadge\"", urlBar);
        Assert.Contains("AutomationProperties.Name=", urlBar);

        var urlBarCode = File.ReadAllText(
            Path.Combine(repoRoot, "src", "VELO.UI", "Controls", "UrlBar.xaml.cs"));
        Assert.Contains("StatusWarningSoftBrush",  urlBarCode);
        Assert.Contains("StatusSuccessSoftBrush",  urlBarCode);
    }

    [Fact]
    public void The_media_detection_off_switch_is_wired_end_to_end()
    {
        // Lesson #21, and the specific failure this guards: a Settings toggle
        // that persists happily while nothing reads it. The point of this
        // switch is that a user whose site misbehaves can turn detection off;
        // a toggle that saves but does not gate is worse than none, because it
        // looks like it worked.
        //
        // The two subscription sites and the load/save symmetry are covered by
        // the two SettingsWindow tests above. This asserts the rest of the
        // chain: registered → refreshed → handed to each tab → consulted.

        var srcRoot = LocateSrcRoot();

        var depConfig = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "Startup", "DependencyConfig.cs"));
        Assert.Contains("AddSingleton<VELO.Core.Media.MediaDetectionGate>", depConfig);

        // Refreshed at startup, and specifically AWAITED in the bootstrapper
        // rather than fired and forgotten from MainWindow. The cached default
        // is "enabled", so a refresh still in flight while session-restored
        // tabs initialise their WebViews would inject the detector against the
        // user's setting — a race that only bites the users who turned it off.
        var bootstrapper = StripComments(File.ReadAllText(
            Path.Combine(srcRoot, "VELO.App", "Startup", "AppBootstrapper.cs")));

        Assert.True(
            Regex.IsMatch(bootstrapper, @"await\s+_services\.GetRequiredService<[\w.]*MediaDetectionGate>\(\)\s*\.\s*RefreshAsync\(\)"),
            "AppBootstrapper does not AWAIT MediaDetectionGate.RefreshAsync(). Fire-and-forget leaves the " +
            "cached default ('enabled') racing session-restored tabs, so a user who turned detection off " +
            "gets it injected anyway on the tabs they had open.");

        var mainWindow = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "MainWindow.xaml.cs"));
        Assert.Contains("OnMediaDetectionChanged", mainWindow);

        // Handed to every tab.
        var tabHost = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "Controllers", "BrowserTabHost.cs"));
        Assert.Contains("SetMediaDetectionGate", tabHost);

        // And consulted before the script is injected. Comments are stripped
        // first: the injection block is wrapped in prose that names the gate,
        // and matching that would pass with the check deleted — the same trap
        // Every_path_that_hands_a_tab_to_the_user documents.
        var code = StripComments(File.ReadAllText(
            Path.Combine(srcRoot, "VELO.UI", "Controls", "BrowserTab.xaml.cs")));

        Assert.True(
            code.Contains("_mediaDetectionGate?.IsEnabled", StringComparison.Ordinal),
            "BrowserTab injects media-detect.js without consulting MediaDetectionGate — " +
            "the Settings toggle would persist and do nothing.");
    }

    [Fact]
    public void CouncilAdapters_bundledJsonFiles_existWithRequiredFields()
    {
        // The four adapter JSON files are what makes the bridge generic
        // (selectors live there, not in the JS). If a file disappears or
        // loses a required field, CouncilAdaptersRegistry refuses to load
        // it and that provider becomes silently unavailable. Lock the
        // shape here so a missing field is caught in CI.
        var repoRoot   = LocateRepoRoot();
        var folder     = Path.Combine(repoRoot, "resources", "council", "adapters");
        var fileNames  = new[] { "claude.json", "chatgpt.json", "grok.json", "local.json" };
        var required   = new[] { "name", "composer", "sendButton", "responseContainer" };

        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(folder, fileName);
            Assert.True(File.Exists(path), $"Council adapter missing: {path}");

            var json = File.ReadAllText(path);
            foreach (var field in required)
            {
                Assert.Contains($"\"{field}\"", json);
            }
        }
    }

    [Fact]
    public void Every_path_that_hands_a_tab_to_the_user_makes_the_WebView_visible()
    {
        // v2.4.70. BrowserTab.xaml starts with WebView collapsed and
        // NewTabPageControl visible, and the only thing that swaps them is
        // ShowWebView(). NavigateAsync calls it; AttachAsPopupAsync (the F-2
        // popup path, added in v2.4.60) did not — because it deliberately never
        // navigates, so window.opener survives for OAuth.
        //
        // The tab then looked broken while working perfectly: correct URL in the
        // address bar, page loaded underneath, VELO's new-tab overlay sitting on
        // top. Nine releases before anyone reported it, because it only happens
        // on links that open in a new tab.
        //
        // Any future method that produces a tab the user is meant to look at has
        // to make the WebView visible. Assert the two that exist do.
        var browserTabDir = Path.Combine(LocateSrcRoot(), "VELO.UI", "Controls");
        var sources = Directory
            .GetFiles(browserTabDir, "BrowserTab*.cs", SearchOption.TopDirectoryOnly)
            .Where(p => !p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToList();
        Assert.NotEmpty(sources);

        var all = string.Concat(sources);

        // Grab each method body by brace-matching from its signature.
        static string BodyOf(string text, string signature)
        {
            var start = text.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return "";
            var open = text.IndexOf('{', start);
            if (open < 0) return "";
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text[open..i];
            }
            return "";
        }

        // Comments are stripped first. The first version of this test matched
        // the word ShowWebView() inside the explanatory comment that sits right
        // above the call — so it passed with the call deleted, which is exactly
        // the failure it was written to catch. A guard nobody has watched fail
        // is not a guard.
        static string WithoutComments(string code) =>
            string.Join('\n', code.Split('\n').Select(l =>
            {
                var idx = l.IndexOf("//", StringComparison.Ordinal);
                return idx >= 0 ? l[..idx] : l;
            }));

        foreach (var signature in new[] { "public async Task AttachAsPopupAsync", "public async Task NavigateAsync" })
        {
            var body = BodyOf(all, signature);
            Assert.True(body.Length > 0, $"{signature} not found — update this test if it was renamed");
            Assert.True(WithoutComments(body).Contains("ShowWebView()", StringComparison.Ordinal),
                $"{signature} hands a tab to the user without calling ShowWebView(); " +
                "the WebView starts collapsed, so the user sees the new-tab overlay " +
                "over a page that actually loaded.");
        }
    }

    // ── S-C — VELO Sentinel: producer AND consumer both wired ────────────

    [Fact]
    public void Sentinel_is_registered_produced_and_consumed()
    {
        // Lesson #21 — a classifier can be registered in DI, resolved, and
        // still never affect anything because nobody feeds it hosts (SmartBlock
        // spent a release in exactly that state). Assert the whole loop:
        //
        //   registration → RequestGuard/AISecurityEngine take the dependency
        //   → RequestGuard PRODUCES classifications (Prefetch on cache miss)
        //   → RequestGuard CONSUMES them (TryGetCachedVerdict) and can Block
        //   → the FLAG level reaches PhishingShield as a signal
        //   → the host applies the shadow/enforce setting.

        var srcRoot = LocateSrcRoot();

        var depConfig = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "Startup", "DependencyConfig.cs"));
        Assert.Contains("AddSingleton<VELO.Security.Sentinel.SentinelClassifier>", depConfig);

        var requestGuard = File.ReadAllText(Path.Combine(srcRoot, "VELO.Security", "Guards", "RequestGuard.cs"));
        Assert.Contains("SentinelClassifier? sentinel", requestGuard);   // takes the dependency
        Assert.Contains("_sentinel.Prefetch(",          requestGuard);   // producer
        Assert.Contains("_sentinel.TryGetCachedVerdict(", requestGuard); // consumer
        Assert.Contains("\"SENTINEL\"",                 requestGuard);   // attributable verdict (lesson #29)

        var aiEngine = File.ReadAllText(Path.Combine(srcRoot, "VELO.Security", "AI", "AISecurityEngine.cs"));
        Assert.Contains("SentinelClassifier? sentinel", aiEngine);
        Assert.Contains("_sentinel.ClassifyAsync(",     aiEngine);
        Assert.Contains("SentinelFlaggedPhishing",      aiEngine);       // FLAG → PhishingShield

        // PhishingShield must actually read the flag, not just carry it.
        var shield = File.ReadAllText(Path.Combine(srcRoot, "VELO.Security", "Guards", "PhishingShield.cs"));
        Assert.Contains("SentinelFlaggedPhishing", shield);
        Assert.Contains("signals.SentinelFlaggedPhishing", shield);

        // And the host has to apply the setting, or the toggle is decoration.
        var mainWindow = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "MainWindow.xaml.cs"));
        Assert.Contains("SentinelEnforce",        mainWindow);
        Assert.Contains("sentinel.Mode",          mainWindow);
        Assert.Contains("sentinel.EnsureLoaded()", mainWindow);
    }

    [Fact]
    public void Sentinel_download_channel_is_wired_end_to_end()
    {
        // S-D. The failure this guards against is the one the model download
        // is most likely to hit: a button that downloads a model nothing ever
        // loads, because the classifier's load is one-shot and nobody calls
        // Reload(). Producer (the button) and consumer (the host reload) both
        // asserted — lesson #21.

        var srcRoot = LocateSrcRoot();

        var depConfig = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "Startup", "DependencyConfig.cs"));
        Assert.Contains("AddSingleton<VELO.Security.Sentinel.SentinelModelInstaller>", depConfig);

        var settings = File.ReadAllText(Path.Combine(srcRoot, "VELO.UI", "Dialogs", "SettingsWindow.xaml.cs"));
        Assert.Contains("SentinelModelInstaller",  settings);   // producer
        Assert.Contains("InstallAsync(",           settings);
        Assert.Contains("SentinelModelInstalled?.Invoke", settings);

        var settingsXaml = File.ReadAllText(Path.Combine(srcRoot, "VELO.UI", "Dialogs", "SettingsWindow.xaml"));
        Assert.Contains("OnSentinelDownloadClick", settingsXaml);

        var mainWindow = File.ReadAllText(Path.Combine(srcRoot, "VELO.App", "MainWindow.xaml.cs"));
        Assert.Contains("OnSentinelModelInstalled", mainWindow);  // consumer
        Assert.Contains("sentinel.Reload()",        mainWindow);

        // The installer must verify before installing — the whole reason the
        // manifest publishes hashes.
        var installer = File.ReadAllText(
            Path.Combine(srcRoot, "VELO.Security", "Sentinel", "SentinelModelInstaller.cs"));
        Assert.Contains("Sha256HexAsync",    installer);
        Assert.Contains("IsSchemaSupported", installer);
    }

    [Fact]
    public void Sentinel_runs_behind_the_blocklist_and_ahead_of_SmartBlock()
    {
        // The position in the pipeline is a product decision, not an accident:
        // exact blocklists win (fast, no false-positive surface), then the
        // offline classifier, then the optional HTTP path. A refactor that
        // reorders these changes what VELO blocks — catch it here.
        var guard = File.ReadAllText(
            Path.Combine(LocateSrcRoot(), "VELO.Security", "Guards", "RequestGuard.cs"));

        var blocklistIdx  = guard.IndexOf("_blocklist.IsBlocked(host)", StringComparison.Ordinal);
        var sentinelIdx   = guard.IndexOf("_sentinel.TryGetCachedVerdict(host)", StringComparison.Ordinal);
        var smartBlockIdx = guard.IndexOf("_smartBlock?.TryGetCachedVerdict(host)", StringComparison.Ordinal);

        Assert.True(blocklistIdx  > 0, "blocklist check not found in RequestGuard");
        Assert.True(sentinelIdx   > 0, "Sentinel check not found in RequestGuard");
        Assert.True(smartBlockIdx > 0, "SmartBlock check not found in RequestGuard");

        Assert.True(blocklistIdx < sentinelIdx,
            "Sentinel must run BEHIND the exact blocklist — the list is faster and cannot be wrong.");
        Assert.True(sentinelIdx < smartBlockIdx,
            "Sentinel must run AHEAD of SmartBlock — the offline classifier is the always-on path, " +
            "the HTTP one is opt-in.");
    }

    // ── Test 6 — every SettingsWindow event is subscribed at every open site ──

    [Fact]
    public void Every_SettingsWindow_event_is_subscribed_at_every_construction_site()
    {
        // Found in S-C: SettingsWindow raises YouTubeAdBlockChanged so the host
        // can hot-apply the toggle, and the tray/menu open site subscribed it —
        // but the command-palette "Configuración" site did not. Same dialog,
        // same Save, and the ad-blocker silently kept the old value until the
        // next restart depending on HOW you opened settings. Same family as
        // lesson #8/#11: the wiring exists, just not at every call-site.
        //
        // Every `new SettingsWindow(...)` must subscribe every event the dialog
        // exposes, before ShowDialog.

        var srcRoot   = LocateSrcRoot();
        var dialogSrc = File.ReadAllText(Path.Combine(srcRoot, "VELO.UI", "Dialogs", "SettingsWindow.xaml.cs"));

        var events = Regex.Matches(dialogSrc, @"public\s+event\s+[^\s]+(?:<[^>]+>)?\??\s+(\w+)\s*;")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(events);

        var hostFiles = new[] { Path.Combine(srcRoot, "VELO.App") }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var missing = new List<string>();

        foreach (var file in hostFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match ctor in Regex.Matches(content, @"new\s+(?:VELO\.UI\.Dialogs\.)?SettingsWindow\s*\("))
            {
                // The wiring block runs between construction and ShowDialog.
                var showIdx = content.IndexOf("ShowDialog", ctor.Index, StringComparison.Ordinal);
                if (showIdx < 0) continue;
                var block = content[ctor.Index..showIdx];

                foreach (var evt in events)
                {
                    // `\s*` because the call-sites align the += column.
                    if (!Regex.IsMatch(block, $@"\.{Regex.Escape(evt)}\s*\+="))
                        missing.Add($"{Path.GetFileName(file)}: SettingsWindow opened without subscribing {evt}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} SettingsWindow event(s) unsubscribed at a construction site — " +
            "the same Save produces different behaviour depending on how the dialog was opened:\n  " +
            string.Join("\n  ", missing));
    }

    // ── Test 5 — every settings key READ in SettingsWindow is also WRITTEN ──

    [Fact]
    public void Every_setting_loaded_in_SettingsWindow_is_also_persisted()
    {
        // Lesson #23 (v2.4.54): SettingsWindow.LoadCouncilStateAsync READ the four
        // CouncilEnabled* toggles (since Phase 4.0 chunk H / v2.4.38) but Save_Click
        // never WROTE them — the user ticked a toggle, saved, reopened, and it was
        // blank. Dormant 16 releases because Council Mode was inert until it went
        // runtime-clickable, at which point OpenCouncilModeAsync read all four as
        // "no" and refused to open. A load without a matching save = dormant setting.
        //
        // This asserts the lesson #23 invariant: every SettingKeys.X the dialog
        // READS must also be WRITTEN somewhere in the same file. The reverse is NOT
        // required — a key written-but-not-read here is legitimate (e.g. Language is
        // persisted on save but loaded by LocalizationService elsewhere).
        //
        // Matching is call-flavoured, not field-scoped, so it catches helper
        // indirection: GetCouncilBoolAsync(SettingKeys.CouncilEnabledClaude) counts
        // as a READ because the call name contains "Get", and
        // _settings.SetAsync(SettingKeys.CouncilEnabledClaude, …) as a WRITE.

        var srcRoot = LocateSrcRoot();
        var path    = Path.Combine(srcRoot, "VELO.UI", "Dialogs", "SettingsWindow.xaml.cs");
        Assert.True(File.Exists(path), $"SettingsWindow not found at {path}");
        var cs = File.ReadAllText(path);

        var readKeys  = SettingKeyMatches(cs, @"Get\w*\(\s*SettingKeys\.(\w+)");
        var writeKeys = SettingKeyMatches(cs, @"Set\w*\(\s*SettingKeys\.(\w+)");

        // Conscious exceptions: keys the dialog reads purely to DISPLAY read-only
        // status and deliberately never persists. Empty today — adding one forces
        // a deliberate decision (same philosophy as _knownDeferredServices above).
        var readOnlyDisplay = new HashSet<string>(StringComparer.Ordinal);

        var loadedButNotSaved = readKeys
            .Where(k => !writeKeys.Contains(k) && !readOnlyDisplay.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            loadedButNotSaved.Count == 0,
            "Lesson #23 — SettingKeys read in SettingsWindow but never persisted in a " +
            "Set* call (a load without a save = dormant setting):\n  " +
            string.Join("\n  ", loadedButNotSaved.Select(k => $"SettingKeys.{k}")) +
            "\nAdd the matching _settings.Set* in Save_Click, or (if intentionally " +
            "read-only) add it to readOnlyDisplay in this test.");
    }

    /// <summary>
    /// Removes // line comments and /* block */ comments so a scan matches
    /// code rather than the prose explaining it.
    ///
    /// Line comments are stripped FIRST, and the order is the whole point. The
    /// first version ran the block-comment regex first, and BrowserTab.xaml.cs
    /// contains the line comment "a council/* payload" — that stray /* opened a
    /// phantom block which the lazy matcher closed ~200 lines later at the next
    /// real */, deleting the code in between. It surfaced as a test failing on
    /// a field that was plainly there.
    ///
    /// The direction of the mistake matters: over-stripping makes a Contains
    /// assertion go red (safe, noisy), but it makes a COUNT go down — and this
    /// helper feeds a test that counts postMessage call sites, where
    /// under-counting would be a false green. Stripping line comments first can
    /// at worst truncate a block that contains a //, leaving it unclosed and so
    /// unstripped, which errs the safe way.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutLines = string.Join('\n', source.Split('\n').Select(line =>
        {
            var idx = line.IndexOf("//", StringComparison.Ordinal);
            return idx >= 0 ? line[..idx] : line;
        }));

        return Regex.Replace(withoutLines, @"/\*.*?\*/", "", RegexOptions.Singleline);
    }

    private static HashSet<string> SettingKeyMatches(string text, string pattern)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, pattern))
            set.Add(m.Groups[1].Value);
        return set;
    }

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

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // Repo root = first ancestor containing BOTH src/ and resources/.
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "resources")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate repo root (src/ + resources/) — searched up from " + AppContext.BaseDirectory);
    }
}
