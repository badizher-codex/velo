# Changelog

All notable changes to VELO Browser are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions follow [Semantic Versioning](https://semver.org/).

> **Note:** Releases v2.1.0 through v2.4.30 are summarised in
> [`memory/project_phase3_state.md`](memory/project_phase3_state.md) and the
> GitHub release notes; per-release CHANGELOG entries resume from v2.4.31
> (Phase 3 / Sprint 10b chunk 6 — partial-class refactor).

---

## [2.4.48] — 2026-05-14 — Phase 4.1 chunk G: Council Mode goes live (first runtime-testable activation)

The chunk every prior Phase 4.1 release was inert in preparation for. This release wires together everything chunks A-F shipped and exposes **a real, clickable Council Mode**: the Settings toggles are enabled, the command palette has a "🤝 Council Mode" entry, and clicking it opens the 2×2 layout populated with the four `council-*` container tabs, the master-prompt Bar on top, and the per-panel capture toolbars over each cell.

**This is the first end-to-end runtime path in Phase 4.1.** Up to v2.4.47 every chunk was guarded by `if (IsCouncilPanel)` / `HasActiveSession` checks that returned early because no caller ever flipped the bits. v2.4.48 flips them.

### Added — runtime entry points

- **Settings → 🤝 Council toggles unlocked**: removed `IsEnabled="False"` from the four CheckBox controls (Claude / ChatGPT / Grok / Local). Section header relabelled "PROVEEDORES" (the "(activables en v2.5.x)" hint is gone). The Ollama option's label updated to "Local (Ollama / LM Studio)" to match the v2.4.40 backend-agnostic story. Save/Load already wired since Phase 4.0 chunk H.
- **Command palette entry "🤝 Council Mode"** (`MainWindow.BuiltInCommands`). Single command that toggles: opens Council if inactive, closes it if active. No new keyboard shortcut yet — chunk H will pick the binding after the maintainer reviews the runtime feel.

### Added — `MainWindow.OpenCouncilModeAsync`

Six-step orchestrated activation:

1. **Resolve opted-in providers** from `SettingKeys.CouncilEnabled{Claude,ChatGpt,Grok,Ollama}`. Zero providers → friendly MessageBox + abort.
2. **First-run disclaimer**: shows `CouncilFirstRunDisclaimer` (Phase 4.0 chunk G) if the user hasn't accepted yet. Dismiss-without-accept aborts cleanly.
3. **Open four `council-*` tabs**: one per provider in canonical order (Claude → ChatGpt → Grok → Local). Enabled providers navigate to their adapter's `homeUrl` (claude.ai/new, chat.openai.com/, grok.com/, about:blank). Disabled providers get a blank placeholder tab so the 2×2 layout has all four cells filled.
4. **Activate the 2×2 layout** via the Phase 4.0 `CouncilLayoutController.ActivateAsync`. Mutually exclusive with the 2-pane split (auto-deactivated first).
5. **Bring up the chunk F UI** via `EnsureCouncilUiAsync(enabled)`: instantiates the `CouncilBar` at the top of `BrowserContent`, hooks its `SendRequested` event, instantiates a `CouncilPanelOverlay` over each enabled cell and hooks its `CaptureRequested` event.
6. **Start the session**: `CouncilOrchestrator.StartSession(enabled)` flips the chunk E dispatch path live — every council/* WebMessage now lands as a typed event on the orchestrator and mutates session state.

Fail-soft at every step: an exception anywhere triggers `CloseCouncilModeAsync` which tears down the session, hides UI, deactivates the layout. The user sees a Spanish error toast if synthesis fails — Council never crashes VELO.

### Added — `MainWindow.OnCouncilSendRequested` (Send-all flow)

User clicks "Enviar a todos" → handler:
1. Sets `_councilBarVm.Status = Sending`.
2. `orchestrator.AppendUserPrompt(prompt)`.
3. For each opted-in panel: `ExecuteScriptAsync("window.__veloCouncil.paste(prompt)")` then `ExecuteScriptAsync("window.__veloCouncil.send()")`. Failures per-panel logged but don't abort the others.
4. Schedules `ScheduleCouncilSynthesisAsync` in the background — waits up to 90 s for every available panel's `LatestReply` to be populated (via chunk E's `replyDetected` dispatch), then calls `orchestrator.SynthesizeAsync(prompt)`.
5. On success: bar resets for next turn. On failure: `Status = Error`, `ErrorText` populated (retry path opens).

### Added — `MainWindow.OnCouncilCaptureRequested` (capture flow)

User clicks 📝/💻/📊/🔗 button on an overlay → handler:
1. Finds the matching BrowserTab via `_councilTabIds[provider]`.
2. `ExecuteScriptAsync("window.__veloCouncil.captureXxx()")` — returns JSON-encoded string.
3. Unwraps via `JsonSerializer.Deserialize<string>(raw)`.
4. Materialises `CouncilCapture.Create(provider, type, content, sourceUrl)` and hands to `orchestrator.AddCapture(...)`.
5. Updates `_councilBarVm.CaptureCount` from `session.Panels.Sum(p => p.Captures.Count)`.

### Added — `MainWindow.CloseCouncilModeAsync`

`orchestrator.EndSession()` → `bar.HideAndReset()` → overlay teardown → `DeactivateCouncilModeAsync()` (Phase 4.0). The four `council-*` tabs **stay open** in the sidebar so the user can review the conversation; manual close via TabSidebar context menu.

### Verified locally with `dotnet publish --self-contained` (lesson #22)

Touches three ExecuteScriptAsync calls — clean-published before commit. CS1061 / FQN / SDK shape mismatch checks all clean.

### Tests

No new dedicated tests for chunk G — the runtime path is WPF + WebView2 + dispatcher-heavy and the existing unit tests (orchestrator state, parser, ViewModel state machine) cover every non-WPF surface the path traverses. **Manual verification is the gate for v2.5.0** (chunk H): the maintainer needs to flip Settings toggles + open Council + send a prompt + click captures + watch the synthesis land. Findings drive chunk H polish + the v2.5.0 release.

- VELO.Core.Tests: 238 (unchanged).
- Full suite: **529** (unchanged).

### Phase 4.1 progress

- ✅ Chunk A (DTOs) — v2.4.41
- ✅ Chunk B (orchestrator) — v2.4.41
- ✅ Chunk C (bridge JS + parser) — v2.4.44
- ✅ Chunk D (adapters JSON + registry) — v2.4.44
- ✅ Chunk E (BrowserTab ↔ Council bridge wiring) — v2.4.46
- ✅ Chunk F (Council Bar + per-panel mini-toolbar) — v2.4.47
- ✅ **Chunk G (Settings unlock + command palette + activation flow) — this release**
- ⏳ Chunk H (manual verification + smoke + release v2.5.0) — next

### Operational guidance for the maintainer

After installing v2.4.48 and **before opening Council Mode**:
1. Settings → 🤝 Council → enable at least one provider toggle (the disclaimer modal blocks until accepted on first activation).
2. Verify "Verificar conexión" works against your local moderator backend (LM Studio at 1234 with qwen3.6-35b-a3b OR Ollama at 11434 with qwen3:32b).
3. Open Council via command palette (Ctrl+K → "Council Mode") or via the new entry.
4. Type a prompt → Enviar a todos. You'll see four panels load the providers' chat surfaces.
5. After each panel replies, click 📝 Texto on the panels you want to include in the synthesis.
6. Wait ~90 s max for the synthesis (timeout governed by `ScheduleCouncilSynthesisAsync`).
7. Findings → file as issues for chunk H polish. Especially watch for:
   - Adapter selector drift (chunk D JSON files may need re-tuning if a provider changed DOM since 2026-05-14).
   - 2×2 layout overlap with FindBar or other top-pinned controls.
   - ExecuteScriptAsync timing — adapter setAdapter happens on NavigationCompleted; if the page lazy-loads chat surfaces after first paint, captures may return empty until the user has interacted with the panel.

---

## [2.4.47] — 2026-05-14 — Phase 4.1 chunk F: Council Bar UI + per-panel mini-toolbar (still no activator)

Continues Phase 4.1 from v2.4.46 (chunk E = BrowserTab ↔ bridge wiring). This release lands **the two WPF surfaces** Council Mode needs once activation flips on: the Council Bar that hosts the master prompt + Send-all button, and the per-panel mini-toolbar that floats over each cell of the 2×2 layout for capture clicks. Both controls ship with `Visibility="Collapsed"` and **no caller instantiates them yet** — chunk G is the one that brings them up alongside the Phase 4.0 `CouncilLayoutController`. Council Mode stays inert at runtime; the toggles in Settings → 🤝 Council remain disabled.

### Added — Council Bar

- **`CouncilBarViewModel`** (`src/VELO.Core/Council/CouncilBarViewModel.cs`) — pure-C# INPC state machine for the bar. Properties: `PromptText`, `AvailablePanelCount`, `CaptureCount`, `Status` (4-state: Idle / Sending / Synthesising / Error), `ErrorText`. Computed surfaces: `IsSendEnabled` (gated on prompt + panels + status), `IsBusy`, `StatusText` (Spanish copy adapted per state). Lives in VELO.Core so the state machine is unit-testable without WPF — 23 tests cover the gating, status copy, INPC dependency propagation, and the negative-clamp / reset helpers.

- **`CouncilBar.xaml(.cs)`** (`src/VELO.UI/Controls/CouncilBar.xaml`) — UserControl bound to the VM via DataContext. Layout: `SurfaceDarkBrush` panel, prompt TextBox (Phase 5 `ModernTextBox` style), status banner under the prompt, captures badge (visible only when `CaptureCount > 0` via the local `ZeroToCollapsedConverter`), Send-all button (`PrimaryButton` Phase 5 style). Public API: `ShowAndFocus(viewModel)` brings the bar up and focuses the prompt box on the next layout pass; `HideAndReset()` collapses + clears DataContext; `SendRequested` event raises with the trimmed prompt text when the user clicks Send (and `IsSendEnabled` was true).

### Added — Per-panel mini-toolbar

- **`CouncilPanelOverlay.xaml(.cs)`** (`src/VELO.UI/Controls/CouncilPanelOverlay.xaml`) — UserControl that floats top-right over each 2×2 cell. Provider chip (purple, label from `SetProvider(provider)`) + four `GhostButton` capture buttons with brand emojis (📝 Texto, 💻 Código, 📊 Tabla, 🔗 Cita). DropShadow + 0.95 opacity so the toolbar reads "above" the panel without obscuring content. Public API: `SetProvider(provider)` updates the chip label and stores the provider; `Show()` / `HideAndReset()` for activation/teardown; `CaptureRequested` event raises with `(provider, captureType)` so the host can route through `ExecuteScriptAsync("window.__veloCouncil.captureXxx()")` against the matching panel's bridge.

### Why this lands inert

Same pattern as chunks A-E: the controls exist but no caller instantiates them from MainWindow. The host wiring (Council Bar visibility, per-panel overlay placement over each 2×2 cell, event subscriptions into the orchestrator) lands in chunk G alongside the activation toggles and the command-palette entry. Keeping chunk F purely additive avoids the v2.4.38 trap where 8 chunks landed together and a single misbehaviour was impossible to localise.

### Verified locally with `dotnet publish --self-contained` (lesson #22)

Two new XAML files added — the XamlResourceTests smoke test auto-scans the new resources without snapshot updates. Followed the clean-publish rule from v2.4.45 again before push.

### Tests

- **`CouncilBarViewModelTests`** (23 new):
  - Send enable gating: empty prompt → disabled; whitespace-only → disabled; zero panels → disabled; busy states (Sending / Synthesising) → disabled; Error state → re-enabled (retry path).
  - `IsBusy` truth-table across all four statuses.
  - `StatusText` adapts per state: zero panels (CTA to activate), ready count, captures-attached suffix, sending target, synthesis copy, error text or fallback when blank.
  - INPC: prompt change raises `PromptText` + `IsSendEnabled` + `StatusText`; status change raises `Status` + `IsBusy` + `IsSendEnabled` + `StatusText`; setting same value is a no-op.
  - Negative-value clamping on `AvailablePanelCount` + `CaptureCount`.
  - `ResetForNextTurn` clears prompt + error + status.
- **XamlResourceTests** (existing smoke) auto-scans `CouncilBar.xaml` + `CouncilPanelOverlay.xaml` for `{StaticResource X}` refs; all resolve against `Themes/Colors.xaml` and `Themes/DarkTheme.xaml` (Phase 5 styles).
- VELO.Core.Tests: 215 → **238** (+23).
- Full suite: 506 → **529**. All green.

### Phase 4.1 progress

- ✅ Chunk A (DTOs) — v2.4.41
- ✅ Chunk B (orchestrator) — v2.4.41
- ✅ Chunk C (bridge JS + parser) — v2.4.44
- ✅ Chunk D (adapters JSON + registry) — v2.4.44
- ✅ Chunk E (BrowserTab ↔ Council bridge wiring) — v2.4.46
- ✅ **Chunk F (Council Bar + per-panel mini-toolbar) — this release**
- ⏳ Chunk G (enable provider toggles + activation flow) — **first chunk runtime-testable end-to-end**
- ⏳ Chunk H (smoke + release v2.5.0)

### What chunk G will plug together

Once chunk G ships:
1. Remove `IsEnabled="False"` from the four Settings → Council toggles (one line per).
2. Add a "Council Mode" command-palette entry + menu item that calls `MainWindow.ActivateCouncilModeAsync()`.
3. In `ActivateCouncilModeAsync`: invoke `CouncilLayoutController.ActivateAsync` (Phase 4.0), open four `council-*` container tabs, instantiate **this release's** Council Bar + four overlays, wire the events, and call `_orchestrator.StartSession(opted-in-providers)` so the chunk E dispatch path becomes live.

---

## [2.4.46] — 2026-05-14 — Phase 4.1 chunk E: BrowserTab ↔ Council bridge wiring (still no UI entry point)

Continues Phase 4.1 from v2.4.44 (chunks A-D = DTOs + Orchestrator + bridge JS + adapters registry). This release plugs the page-side bridge into the host so that **whenever Council Mode is eventually activated by chunk G, the dispatch path is already in place**. Nothing is exposed in the UI yet — no menu item, no command-palette entry, no hotkey. The toggles in Settings → 🤝 Council remain disabled. Council Mode stays inert at runtime.

### What chunk E adds

- **`BrowserTab._councilProvider`** populates in `SetContainer(containerId)` via `CouncilProviderMap.FromContainerId`. Tabs in `council-claude` / `council-chatgpt` / `council-grok` / `council-ollama` containers report `IsCouncilPanel = true`. All other tabs (Personal, Work, Banking, Shopping, None) are unaffected.

- **Conditional bridge injection in `EnsureWebViewInitializedAsync`** (chunk C resource `council-bridge.js`). When `IsCouncilPanel == true`, the bridge is added via `AddScriptToExecuteOnDocumentCreatedAsync` alongside the existing scripts (autofill, paste-guard, glance-hover, fingerprint-noise). For non-Council tabs the bridge is never injected — the page pays zero overhead. Inject failure is logged and swallowed (panel becomes inert; never crashes the tab).

- **Adapter push in `OnNavigationCompleted`**. After a Council panel finishes navigating, `PushCouncilAdapterAsync(provider)` reads `CouncilAdaptersRegistry.GetAdapterJson(provider)` (chunk D) and calls `window.__veloCouncil.setAdapter(jsonString)` via `ExecuteScriptAsync`. The page-side observer wires against the fresh DOM. Fire-and-forget — failure logs + falls back to inert state.

- **`OnWebMessageReceived` fast-path for council/* payloads**. Before the existing legacy `kind`-based switch (autofill / glance / pasteguard), the handler peeks the JSON string for `"council/"`. When the tab is `IsCouncilPanel` and the parser succeeds, the parsed `CouncilBridgeMessage` is raised via the new `CouncilBridgeMessageReceived` event and the handler returns early — non-Council messages flow through untouched. Parser fast-fails on non-Council prefixes (chunk C) so the peek-and-route pattern is safe.

- **`MainWindow.OnCouncilBridgeMessage`** routes each event into the singleton `CouncilOrchestrator`:
  - `CouncilCaptureMessage` → `orch.AddCapture(...)`
  - `CouncilReplyDetectedMessage` → `orch.RecordPanelReply(...)`
  - `CouncilBridgeErrorMessage` → `Log.Warning` only
  Inert when `orch.HasActiveSession == false` — the dispatch silently no-ops if no session has been started, which is the entire runtime state of chunk E.

- **`BrowserTabHost.TabWiringHandlers`** gains an `OnCouncilBridgeMessage` action and two new DI-injected setters (`SetCouncilOrchestrator`, `SetCouncilAdaptersRegistry`). WiringSmokeTests test #1 picks up the new setters automatically through its file scan — both are called in `BuildAndWire` alongside the existing per-tab service setters.

### Why this lands inert

Chunk E ships the **wiring** for Council Mode but **no caller** activates a session. `CouncilOrchestrator.StartSession(...)` is never invoked in production code yet — it's reachable only from the tests. Chunk G (next in the queue) is the one that:
1. Removes `IsEnabled="False"` from the Settings → Council toggles.
2. Adds a "Open Council Mode" entry in command palette + menu.
3. Calls `MainWindow.ActivateCouncilModeAsync` which spins up the 2×2 layout (Phase 4.0), opens four `council-*` container tabs, **then** calls `_orchestrator.StartSession(opted-in-providers)`.

Until G ships, the dispatch path in this release is dead code at runtime but live in tests.

### Verified locally with `dotnet publish --self-contained` (lesson #22)

Followed the new rule from v2.4.45 — ran a full clean publish locally before push to mirror the CI build path. No CS1061 / FQN drift / SDK shape mismatch surfaced. The build that ships is the build CI will produce.

### Tests

- **`CouncilDispatchIntegrationTests`** (6 new): full end-to-end of the parser-to-orchestrator pipeline that production runtime traverses. Coverage:
  - `council/capture` payload → matching panel's Captures list grows.
  - `council/replyDetected` → panel `LatestReply` + transcript Panel-role message.
  - Dispatch with no active session → no-op.
  - Multiple captures in order land sequentially with correct types.
  - Provider on parsed message wins over tab identity (regression guard for future routing shortcuts).
  - `council/error` does NOT mutate orchestrator state (log-only).
- Smoke tests #1 (`BrowserTab.SetX has caller in host`) automatically picks up `SetCouncilOrchestrator` + `SetCouncilAdaptersRegistry` — both wired in `BrowserTabHost.BuildAndWire`. No snapshot update required.
- VELO.Core.Tests: 209 → **215** (+6).
- Full suite: 500 → **506**. All green.

### Phase 4.1 progress

- ✅ Chunk A (DTOs) — v2.4.41
- ✅ Chunk B (orchestrator) — v2.4.41
- ✅ Chunk C (bridge JS + parser) — v2.4.44
- ✅ Chunk D (adapters JSON + registry) — v2.4.44
- ✅ **Chunk E (BrowserTab ↔ Council bridge wiring) — this release**
- ⏳ Chunk F (Council Bar UI + per-panel mini-toolbar) — next
- ⏳ Chunk G (enable provider toggles + activation flow) — first runtime-testable chunk
- ⏳ Chunk H (smoke + release v2.5.0)

---

## [2.4.45] — 2026-05-14 — Hotfix: CI build failure inherited from v2.4.43 (favicon Stream type)

**v2.4.43 + v2.4.44 builds failed in CI** with `CS1061: 'Task<Stream>' does not contain a definition for 'AsTask'`, so no installer was published for either tag. v2.4.42 stayed the latest shippable release. This hotfix lands the missing favicon binary path and unblocks the Phase 4.1 chain.

### Root cause

`v2.4.43` introduced `BrowserTab.OnFaviconChanged`:
```csharp
var stream = await WebView.CoreWebView2
    .GetFaviconAsync(CoreWebView2FaviconImageFormat.Png)
    .AsTask()
    .ConfigureAwait(true);
```

The local `dotnet build` succeeded because the obj/ tree had cached references from an older WebView2 SDK shape (`IAsyncOperation<IRandomAccessStream>` → required `.AsTask()`). The current bundled WebView2 SDK in the project's PackageReference returns `Task<Stream>` directly from `GetFaviconAsync(format)` — `.AsTask()` is no longer a member of `Task<>`, hence CS1061.

CI runs `dotnet publish -c Release -r win-x64 --self-contained true`, which restores cleanly without obj/ caches and surfaces the type mismatch every time.

### Fix

```diff
- var stream = await WebView.CoreWebView2.GetFaviconAsync(
-     CoreWebView2FaviconImageFormat.Png).AsTask().ConfigureAwait(true);
+ using var stream = await WebView.CoreWebView2
+     .GetFaviconAsync(CoreWebView2FaviconImageFormat.Png)
+     .ConfigureAwait(true);

  byte[] bytes;
  using (var ms = new MemoryStream())
  {
-     using var reader = stream.AsStreamForRead();
-     await reader.CopyToAsync(ms).ConfigureAwait(true);
+     await stream.CopyToAsync(ms).ConfigureAwait(true);
      bytes = ms.ToArray();
  }
```

`stream` is now a regular `Stream` (no `.AsStreamForRead()` step needed), wrapped in `using` so the WebView2-owned buffer is released when we finish copying it.

### Verification

Reproduced the CI build path locally:
```bash
dotnet publish src/VELO.App/VELO.App.csproj -c Release -r win-x64 --self-contained true
```
→ clean publish succeeds. Full test suite stays at 500/500.

### Lesson #22 — clean publish locally before pushing WebView2-touching changes

`dotnet build` uses obj/ caches that can keep older API shapes alive across SDK bumps. `dotnet publish --self-contained` does the same clean-restore that CI runs and would have caught this in 30 seconds locally.

**New rule** added to memory: whenever a commit touches `CoreWebView2.*Async` calls (or any new WebView2 API surface), run `dotnet publish src/VELO.App/VELO.App.csproj -c Release -r win-x64 --self-contained true` locally before committing. This is the third time CI has caught a "build-clean-but-not-really" regression that local builds missed (lessons #16 = FQN drift, #18 = WebView2 SDK type drift via floating reference, this one = WebView2 SDK API shape change). The pattern repeats; the check needs to be part of the workflow.

### State of v2.4.43 + v2.4.44 features

Both code-on-main, both untested as binaries (because CI never produced an installer for them). Effectively they ship inside v2.4.45 — favicons + Phase 4.1 chunks C+D are all in this build.

When installing v2.4.45 the maintainer gets:
- **From v2.4.43**: real tab favicons (replacing the universal 🌐), 30-day SQLite cache, host-normalised lookup, preload on NavigationStarting.
- **From v2.4.44**: `resources/scripts/council-bridge.js`, four bundled adapter JSON files, `CouncilAdaptersRegistry` + `CouncilBridgeParser`. Still inert at runtime (no entry point yet).
- **From v2.4.45**: this hotfix.

### Tests

No new tests — same 500/500 as v2.4.44. The regression was caught by clean publish, not by the test suite (which doesn't exercise `dotnet publish`).

---

## [2.4.44] — 2026-05-14 — Phase 4.1 chunks C + D: Council bridge JS + adapters registry (still inert)

Continues Phase 4.1 from v2.4.41 (chunks A + B = DTOs + Orchestrator). This release lands the page-side bridge JS and the per-provider adapter registry that future chunks E/F/G will plug together with a real user-facing entry point. Council Mode remains 100% inert at runtime: no menu item, no command-palette entry, no hotkey, no Council UI cell — the bridge JS exists in `resources/scripts/` but is never injected; the adapter registry is constructed at startup but no caller resolves from it yet.

### Added — chunk C: bridge JS + WebMessage protocol

- **`resources/scripts/council-bridge.js`** — page-side imperative API on `window.__veloCouncil`. Adapter-driven: this script does not hard-code any provider's selectors. The host calls `__veloCouncil.setAdapter(json)` after the panel loads, passing the per-provider JSON loaded by the registry (chunk D). Provider-specific quirks (Claude's ProseMirror contenteditable vs ChatGPT's textarea, multiple send-button candidates, drift-prone response containers) live in those JSON files.
  - Public surface: `setAdapter(json)`, `paste(text)`, `send()`, `captureText()`, `captureCode()`, `captureTable()`, `captureCitation()`.
  - Outbound `chrome.webview.postMessage` shapes: `council/capture`, `council/replyDetected`, `council/error`.
  - MutationObserver wired by `setAdapter` posts `council/replyDetected` after 1.5 s of silence on the latest assistant bubble — signal for "stream complete, the panel's reply is ready for synthesis".
  - One injection per document (guarded by `window.__veloCouncilInstalled`). Side-effect-free at load time until `setAdapter` runs.
  - Fail-soft at every method: errors are swallowed, defaults returned. Council UX never crashes a webview.

- **`VELO.Core.Council.CouncilBridgeMessage`** (record hierarchy) + **`CouncilBridgeParser`** — typed conversion from the JS payloads to records the orchestrator can route. Three concrete records: `CouncilCaptureMessage` (with `CouncilCaptureType` enum), `CouncilReplyDetectedMessage`, `CouncilBridgeErrorMessage`. The parser is fast-fail (returns null for any payload that doesn't start with `council/`) so the host's existing `WebMessageReceived` switch can pass non-Council payloads to autofill / paste-guard / glance-hover branches unchanged.

### Added — chunk D: adapter JSON registry

- **`resources/council/adapters/{claude,chatgpt,grok,local}.json`** — four bundled selector packs, hand-curated 2026-05-14, version-stamped (`v1-2026-05-14`). Each adapter declares: composer (input element), send button (comma-separated fallback list), response container, optional code-block / table / citation selectors, maintainer notes. The `local` adapter targets the in-process moderator panel Phase 4.1 chunk F will render — not a remote site.
- **`VELO.Core.Council.CouncilAdapter`** — strongly-typed model mirroring the JSON shape (camelCase via `JsonPropertyName`). `IsValid` predicate gates loading: an adapter without `composer`, `sendButton`, AND `responseContainer` is silently dropped (registry logs warning; provider becomes "unavailable" rather than half-broken).
- **`CouncilAdaptersRegistry`** — singleton loaded from `AppContext.BaseDirectory/resources/council/adapters/` (or a test-override folder). `TryGet(provider)` returns the adapter or null. `GetAdapterJson(provider)` serialises back to JSON so the host can hand it to `__veloCouncil.setAdapter(json)` via `ExecuteScriptAsync`. Fail-soft: missing files / malformed JSON / invalid schema log a warning but don't crash startup; other providers stay available.
- **DI registration**: `CouncilAdaptersRegistry` + `CouncilOrchestrator` registered as singletons in `DependencyConfig.cs`.

### Build / packaging

- Five new `<Content Include>` entries in `VELO.App.csproj` with `CopyToOutputDirectory=PreserveNewest`: the bridge JS + the four adapter JSON files. Lands them under the install root next to the existing bundled scripts (`fingerprint-noise.js`, `paste-guard.js`, etc).

### Tests

- **CouncilBridgeParserTests** — 13 facts/theory cases: empty input, non-object JSON, missing/wrong-prefix `type`, malformed JSON, capture-type decode (Text/Code/Table/Citation case-insensitive), unknown capture type rejected, replyDetected + error payloads, unknown council/* subtypes return null.
- **CouncilAdaptersRegistryTests** — 9 tests: all-four happy path, `TryGet` field shape, JSON round-trip, partial files (some missing → others still load), malformed JSON skip, invalid-schema skip, nonexistent folder = empty, **bundled-adapters smoke** (walks up from `AppContext.BaseDirectory` to find the repo's `resources/council/adapters/` and asserts the four real files load).
- **Smoke tests** (`tests/VELO.Smoke.Tests/WiringSmokeTests.cs`) +2:
  - `CouncilBridge_script_exists_with_expectedApiSurface` — file exists, contains every public API method name (`setAdapter`, `paste`, `send`, `captureText`, etc) and every outbound message type the C# parser branches on. Catches refactors that rename JS methods without updating the C# side.
  - `CouncilAdapters_bundledJsonFiles_existWithRequiredFields` — locks the bundled JSON file presence + required fields (name, composer, sendButton, responseContainer). Catches a file accidentally deleted or a required field renamed.
- **Snapshot update in WiringSmokeTests test #3** — `CouncilOrchestrator` added to `_expectedSingletonEvents` with all three events (`CaptureReceived`, `MessageAppended`, `SynthesisReady`). Justification documented inline: orchestrator is per-session (singleton process-wide); every UI subscriber (transcript, badge, synthesis status) wants every event — broadcast is intentional. Caught by the test as a deliberate-confirmation gate; this commit records the approval.
- VELO.Core.Tests: 185 → **209** (+24).
- VELO.Smoke.Tests: 5 → **7** (+2).
- Full suite: 474 → **500**. All green.

### Phase 4.1 progress

- ✅ Chunk A (DTOs) — v2.4.41
- ✅ Chunk B (orchestrator) — v2.4.41
- ✅ **Chunk C (bridge JS + parser) — this release**
- ✅ **Chunk D (adapters JSON + registry) — this release**
- ⏳ Chunk E (BrowserTab integration: inject bridge, wire WebMessageReceived → orchestrator) — next
- ⏳ Chunk F (Council Bar UI + per-panel mini-toolbar)
- ⏳ Chunk G (enable provider toggles + activation flow) — first chunk that exits inert mode
- ⏳ Chunk H (smoke + release v2.5.0)

---

## [2.4.43] — 2026-05-14 — Tab favicons (real site icons, not the 🌐 placeholder)

Maintainer spotted that every tab in the sidebar showed the same 🌐 globe icon regardless of site. Diagnosis confirmed it as legacy debt rather than a bug: `TabInfo.FaviconData byte[]?` existed since Phase 1 (declared) but was never populated and never bound in `TabSidebar.xaml`. The XAML hard-coded `Value="🌐"` as a Setter with no path to a real bitmap. **Fourth instance** of the wiring-without-callsite anti-pattern that lessons #8, #11 and #15 already track — DI registered / property declared / event raised, but nobody wired the consumer.

### Added

- **`BytesToImageSourceConverter`** (`src/VELO.UI/Converters/BytesToImageSourceConverter.cs`) — materialises a `byte[]` PNG/ICO into a frozen `BitmapImage`. Returns `Binding.DoNothing` on null/empty/malformed bytes so a sibling fallback element (the 🌐 TextBlock) takes the visual slot. `OnLoad` cache + immediate stream dispose so swapping `FaviconData` doesn't pin memory.
- **`FaviconEntry` model + `FaviconRepository`** (`src/VELO.Data/{Models,Repositories}/`) — SQLite cache keyed by host. `GetFreshAsync(host, ttl)` returns null on miss/expired/empty (negative-cache marker). `SaveAsync(host, bytes)` upserts. `PurgeExpiredAsync(ttl)` evicts. Default TTL: **30 days**. Host normalisation (`Normalise(host)` lowercases + strips leading `www.`) ensures `bambu.com` and `WWW.Bambu.com` share one cache row.
- **`FaviconEntry` table** created in `VeloDatabase.InitializeAsync`. New users + upgrades both get it on next launch.
- **`BrowserTab` favicon capture**:
  - New `FaviconCaptured` event (arg = PNG bytes).
  - New `SetFaviconRepository(repo)` setter.
  - `OnFaviconChanged` handler (subscribed in `EnsureWebViewInitializedAsync`) calls `CoreWebView2.GetFaviconAsync(Png)`, copies the stream into a byte array, raises the event, and persists by host.
  - **Cache preload in `OnNavigationStarting`** — fires a background `GetFreshAsync(host)`; if a fresh row exists, raises `FaviconCaptured` immediately so the sidebar swaps 🌐 → real icon **before the page even commits**. The real `FaviconChanged` event still fires later and overwrites with the freshest bytes.
- **`BrowserTabHost` (`BuildAndWire`) wires the event** through a new `OnFaviconCaptured` handler in `TabWiringHandlers`. Calls `SetFaviconRepository` from DI alongside the existing setters. WiringSmokeTests file-scan picks up the new setter automatically.
- **`TabSidebar.xaml`** registers the converter and replaces the 🌐 TextBlock with a `Grid` containing:
  - `Image` with `Source="{Binding FaviconData, Converter={StaticResource BytesToImage}}"` — shows the real favicon when bytes exist.
  - A fallback `TextBlock` shown via `MultiDataTrigger` only when `FaviconData == null` AND `IsLoading == false`. Loading still shows ⏳.

### Changed

- **`TabInfo.FaviconData`** is now `INotifyPropertyChanged`-wired (`Set(ref _faviconData, value)`). Setting `tab.FaviconData = bytes` from the host raises `PropertyChanged("FaviconData")` and the binding re-runs the converter → the sidebar Image swaps in.
- **`MainWindow.OnTabCreated`** registers `OnFaviconCaptured` handler that resolves the matching `TabInfo` via `_tabManager.GetTab(tabId)` and assigns `FaviconData` on the UI dispatcher.

### Fail-soft behaviour

Every layer degrades gracefully:
- WebView2 `GetFaviconAsync` throws (unresolvable icon URI, page not yet attached) → handler silently catches, sidebar keeps showing 🌐.
- Repository SaveAsync fails (disk / permissions) → swallowed inside the handler, no crash.
- Converter receives malformed bytes → returns `Binding.DoNothing`, fallback TextBlock takes over.
- Preload race (cache hit + new FaviconChanged) → second update overwrites the first; binding only renders the latest.

### Tests

- **18 new** tests in `tests/VELO.Core.Tests/FaviconRepositoryTests.cs`:
  - Host normalisation (lowercase, leading `www.` strip, idempotent, doesn't break `wwwexample.com`).
  - `GetFreshAsync` ↔ `SaveAsync` round-trip with normalised lookup.
  - Negative-cache marker (empty bytes saved → GetFresh returns null but row exists).
  - TTL eviction (`GetFreshAsync(host, TimeSpan.Zero)` reports stale; `PurgeExpiredAsync` removes them; fresh rows survive).
  - `DeleteAsync` removes single host (normalised).
- VELO.Core.Tests: 167 → **185** (+18).
- Full suite: 456 → **474**. All green.

### Lesson learnt #21

`TabInfo.FaviconData` is the fourth instance of "**declared field, no wiring**" caught in VELO history:
1. IA menu (Phase 1 → v2.4.14, ~6 months dormant).
2. `BookmarkAIService` (Sprint 9B → v2.4.18, ~2 weeks dormant).
3. `PasteGuard` (Sprint 3 → v2.4.21, ~6 months dormant — caught by `WiringSmokeTests` test #1 on first run).
4. **`TabInfo.FaviconData`** (Phase 1 → v2.4.43, ~14 months dormant — caught by user observation, not by tests).

WiringSmokeTests can catch "setter exists but no caller" (case 3 — caught instantly). It can NOT catch "property exists but no producer **and** no consumer" — both ends of the wire are missing. New rule going forward: when a model declares a non-trivial data property (`byte[]`, lists, dictionaries), an accompanying smoke test should assert there's a producer (something writes to it) AND a consumer (something reads from it in XAML or code).

### Operational guidance for the maintainer

After installing v2.4.43:
1. **First visit** to any site → real favicon appears within ~100-500 ms of NavigationCompleted (depending on `<link rel="icon">` resolution). The first sub-second still shows 🌐 because the favicon-changed event hasn't fired yet.
2. **Second visit** to the same host → favicon appears **before the page renders** (preloaded from SQLite cache).
3. Sites without a favicon stay on 🌐. The repo writes a negative-cache row so we don't re-query the server every nav.
4. SQLite cache size: typically <1 MB for hundreds of hosts. Eviction is age-based at 30 days — no manual cleanup needed.

---

## [2.4.42] — 2026-05-14 — Fix: internal AI services were melting local LLMs (context bloat + role mix)

Maintainer reported their local LM Studio server was hitting "modelo a full", running out of tokens, and timing out on simple page visits. Diagnosis surfaced **three bugs stacked on top of each other**, all in the path that internal AI services (`SmartBlockClassifier`, `PhishingShield`, `CodeActions`, `BookmarkAIService`, `AIContextActions`) used since Sprint 10A introduced `AiChatRouter`.

### What was wrong

1. **System prompt smuggled into the user message.** `MainWindow.WireAgentChat` concatenated `$"{system}\n\n{user}"` and passed the whole string as the user content, with the actual system role left empty. The local model received instructions like *"You classify network requests as TRACKER or LEGITIMATE…"* in a user-roled message, which is the wrong slot.
2. **Shared history bucket for every internal caller.** `AgentLauncher` keeps per-tabId conversation history in `_history[tabId]`. Every internal AI service called with `tabId="__ai__"` → all of them accumulated history into a single bucket. After 30-50 page loads the context for each fresh classification carried hundreds of past prompts and replies, including old SmartBlock verdicts being replayed back to the model as if they were the current request. LM Studio logs from the maintainer showed `role:"assistant"` entries whose content was the SmartBlock system prompt — pure context bloat.
3. **Zero global concurrency cap.** Per-service rate limits exist (SmartBlock 30/min, PhishingShield TTL 30min) but a single page load could fan out SmartBlock + PhishingShield + BookmarkAI in parallel. On consumer hardware running a 30B-class model, parallel requests don't speed up — they queue inside the runtime, thrash VRAM, and crater tail latency.

### What v2.4.42 changes

- **New `VELO.Core.AI.DirectChatAdapter`** (`src/VELO.Core/AI/DirectChatAdapter.cs`) — stateless one-shot OpenAI-compatible adapter. Each call composes the payload from only the (system, user) it was handed and sends it to `/v1/chat/completions` with `messages: [{role:"system", content:system}, {role:"user", content:user}]`. No history, no shared bucket. Global `SemaphoreSlim(1, 1)` serialises parallel callers so exactly one request is in-flight at a time. Fail-soft (returns "" on transport failure / non-2xx / parse failure); cancellation propagates verbatim.
- **Wired in DI** (`Startup/DependencyConfig.cs`) as a singleton.
- **MainWindow switches internal services to the direct path** (`MainWindow.xaml.cs:214-216`):
  ```diff
  - AiChatRouter.ChatDelegate chatAdapter = WireAgentChat;       // bloat path
  + var directChat = _services.GetRequiredService<VELO.Core.AI.DirectChatAdapter>();
  + AiChatRouter.ChatDelegate chatAdapter = directChat.SendAsync; // stateless
  ```
  `WireAgentChat` stays defined and is still used by the **VeloAgent chat panel** (slash commands, agent actions, page priming) — that path uses history intentionally and is unaffected.
- **SmartBlock cache TTL 6h → 24h** (`DependencyConfig.cs:138-148`) — trackers rarely change host within a day; the model is now scarce enough that re-classifying every 6h was wasteful. Cuts model load on consecutive sessions on the same domains.

### Backend coverage

DirectChatAdapter only handles `AI Mode = Custom` (Ollama / LM Studio / any OpenAI-compatible local server). For `AI Mode = Claude` the security path was already direct (via `VELO.Security.AI.Adapters.ClaudeAdapter` in `AISecurityEngine`, never via this delegate); nothing regresses. For `AI Mode = Offline` the adapter returns "" and caller services fail-soft (Allow / Safe / empty).

### What stays the same

- VeloAgent chat panel (the "Agente" sidebar) keeps using `AgentLauncher` with per-tab history. Conversation flow there is intentional and works as before.
- `AISecurityEngine` Claude path unchanged — already direct.
- `LLamaSharpAdapter` path for users with no Custom AI Mode but a local GGUF — unchanged (still routes via AgentLauncher when used).

### Tests

- **14 new** unit tests in `tests/VELO.Core.Tests/DirectChatAdapterTests.cs`:
  - AI Mode gating (Offline / Claude / unknown → empty, zero HTTP calls).
  - **Payload shape regression test** — pins `messages` is an array of `{role:"system"|"user"}` objects, content fields contain ONLY the corresponding text (not the v2.4.41 pegoteado `system\n\nuser` shape).
  - Endpoint URL (`/v1/chat/completions`) + method (POST) + model-name override.
  - Reply parsing (happy path + four malformed shapes → empty).
  - Fail-soft (non-200 → empty, transport exception → empty, cancellation → throws OCE).
  - **Concurrency serialization** — two parallel callers, asserts max-in-flight = 1, both complete in sequence.
- VELO.Core.Tests: 153 → **167** (+14).
- Full suite: 442 → **456**. All green.

### Operational guidance for the maintainer

When upgrading to v2.4.42:
1. **Restart VELO** so the new DI registration takes effect.
2. **Restart LM Studio** (or whatever local server you use) to reset its internal queue.
3. **Existing SmartBlock cache will repopulate at 24h TTL** — initial hits on previously-visited domains will re-classify once, then sit in cache for a day.
4. Watch the developer logs: with v2.4.42 you should see at most ONE in-flight request to your model regardless of how busy the page is.

---

## [2.4.41] — 2026-05-14 — Phase 4.1 chunks A + B: Council DTOs + orchestrator (still inert)

First two chunks of Phase 4.1 (Bridge + capture). Pure backend — no UI, no callers in the production code path yet. Council Mode remains inert at runtime. The work lands incrementally so each chunk can be reviewed and tested in isolation; the user-visible activation of Council comes in Phase 4.1 chunks G + H.

### Added — chunk A: Council models + DTOs (`src/VELO.Core/Council/`)

- **`CouncilProvider`** enum — the four canonical slots (`Claude`, `ChatGpt`, `Grok`, `Local`). The fourth slot was renamed from "Ollama" to "Local" because v2.4.40 made the local moderator backend configurable (Ollama / LM Studio / OpenAI-compat). The container ID stays `council-ollama` for back-compat with rows already seeded in v2.4.38+ databases.
- **`CouncilProviderMap`** — bidirectional `ContainerId ↔ Provider` mapping, default home URLs (https://claude.ai/new, https://chat.openai.com/, https://grok.com/, about:blank for Local), and the matching `SettingKeys.CouncilEnabled*` constants. Pins the wire-up so a future renamed setting key is caught at test time, not at runtime.
- **`CouncilCaptureType`** enum — `Text` / `Code` / `Table` / `Citation`. Each kind has its own rendering and synthesis treatment.
- **`CouncilCapture`** record — immutable per-fragment capture (Id, panelProvider, type, content, sourceUrl, capturedAtUtc) with `Create()` factory that generates GUIDs and stamps UTC.
- **`CouncilMessage`** record — one entry in the linear transcript. `Role` ∈ {User, Panel, Moderator, System}; `SourceProvider` populated only for `Role.Panel`. Four convenience factories (`UserPrompt`, `PanelReply`, `Synthesis`, `System`).
- **`CouncilPanel`** class — runtime state per panel (TabId, CurrentUrl, IsAvailable, LatestReply, Captures). Mutable through internal setters; orchestrator drives the mutations.
- **`CouncilSession`** class — owns the four panels (always length 4, canonical-order-validated in constructor) and the transcript. Internal `AppendMessage`; public `GetPanel(provider)` lookup.

### Added — chunk B: `CouncilOrchestrator` (`src/VELO.Core/Council/CouncilOrchestrator.cs`)

Single-threaded coordinator that owns at most one active `CouncilSession`. Routes:

- **Session lifecycle**: `StartSession(enabledProviders)` / `EndSession()` / `HasActiveSession` / `CurrentSession`.
- **Captures**: `AddCapture(capture)` validates the panel is available and appends. `RemoveCapture(id)` searches all panels. Raises `CaptureReceived` synchronously.
- **Transcript**: `AppendUserPrompt(text)` / `RecordPanelReply(provider, text, captureRefs)` / `AppendSystemMessage(text)`. Each raises `MessageAppended`.
- **Synthesis**: `SynthesizeAsync(masterPrompt, ct)` builds the panel-replies block (skipping disabled + empty panels in canonical order), invokes the moderator via the wired `ChatDelegate` (same pattern as every other AI consumer since Sprint 10A), appends the result as `Role.Moderator`, raises `MessageAppended` then `SynthesisReady`. On synthesis failure, emits a System message AND re-throws so the Council Bar can render the error. Cancellation propagates verbatim without emitting a System message.
- **`SynthesisSystemPrompt`** moderator directive (visible in CHANGELOG so the maintainer can review): notes agreements, highlights disagreements, flags hallucinations, cites each source assistant in parentheses, replies in the user's prompt language (lesson #10).

### Backend-agnostic by design

The orchestrator does NOT know about Ollama, LM Studio, OpenAI-compat or HTTP. It calls `AiChatRouter.ChatDelegate` and the host (MainWindow) decides at startup whether that delegate routes through the configured Custom AI Mode adapter or a Council-specific adapter. Phase 4.0 + v2.4.40 already laid the configuration story for the moderator backend; chunk B reuses it as-is.

### Tests

- **`CouncilModelsTests`** — 34 assertions covering ProviderMap symmetry, capture/message factories (GUID + UTC + null content rejection), panel invariants (provider-mismatch rejection, capture remove), session constructor (canonical-order + length validation, default seed).
- **`CouncilOrchestratorTests`** — 17 tests with a FakeSynthesizer that records the prompts. Coverage: session lifecycle, capture flow (including disabled-panel rejection), transcript event ordering (`MessageAppended` strictly before `SynthesisReady` for the moderator row), synthesis failure → system message + re-throw, cancellation propagates clean, `BuildPanelReplyBlock` skips unavailable + empty panels in canonical order.
- VELO.Core.Tests: 102 → **153** (+51).
- Full suite: 391 → **442**. All green.

### Phase 4.1 progress

- ✅ Chunk A (DTOs)
- ✅ Chunk B (orchestrator)
- ⏳ Chunk C (bridge JS + capture protocol) — next
- ⏳ Chunk D (adapters JSON for claude.ai / chatgpt.com / grok.com / local)
- ⏳ Chunk E (BrowserTab bridge C# integration)
- ⏳ Chunk F (Council Bar + per-panel mini-toolbar UI)
- ⏳ Chunk G (enable provider toggles + activation flow) — first chunk that touches runtime
- ⏳ Chunk H (smoke + release v2.5.0)

---

## [2.4.40] — 2026-05-13 — Council preflight supports LM Studio / OpenAI-compat servers (not just Ollama)

Follow-up to v2.4.39. Maintainer's actual local-LLM setup is **LM Studio with Qwen3.6 35B A3B UD loaded**, not Ollama with `qwen3:32b`. The Phase 4 spec was Ollama-locked — `/api/tags` + `/api/show` Ollama-specific endpoints, hard-coded `qwen3:32b` model name, port 11434 default. That obligated users to install Ollama + download ~19 GB of qwen3:32b even when they already had a capable moderator model running in a different local-LLM server.

v2.4.40 makes the moderator backend **configurable** — Ollama OR LM Studio OR any generic OpenAI-compatible server (llama.cpp server, text-generation-webui, vllm, etc.). The model name is also configurable; the spec's `qwen3:32b` becomes a default, not a lock.

### Added

- **Backend selector** in Settings → 🤝 Council — three radio buttons:
  - **Ollama (canónico, /api/tags)** — default, preserves v2.4.39 behaviour.
  - **LM Studio (OpenAI-compat, /v1/models)** — talks the OpenAI wire format LM Studio exposes; defaults to `http://localhost:1234` + `qwen3.6-35b-a3b`.
  - **Otro servidor OpenAI-compat** — for llama.cpp server, text-generation-webui, vllm, etc.; defaults to `http://localhost:8000` + `qwen3:32b`.
- **Moderator model TextBox** — user can override the model name (no more hard-coded `qwen3:32b`). Tip line under the box explains how to find the correct identifier for LM Studio ("API Model Identifier" panel) vs Ollama (tag).
- **Inline "Guía rápida" expander** with step-by-step setup for the three backends:
  - Ollama: install from ollama.com → `ollama pull qwen3:32b` → endpoint 11434.
  - LM Studio: install from lmstudio.ai → download a qwen-family model → enable Local Server → copy "API Model Identifier".
  - Other OpenAI-compat: requirements (`/v1/models` + `/v1/chat/completions`) + privacy note that the server must stay 100% local for the Council privacy contract to hold.
- **New `SettingKeys`** (`src/VELO.Data/Models/AppSettings.cs`):
  - `CouncilBackendType` — string ("Ollama" / "LMStudio" / "OpenAICompat"). Default "Ollama".
  - `CouncilModeratorModel` — moderator model name. Default "qwen3:32b".

### Changed

- **`CouncilPreflightService.cs`** refactored to branch on backend type:
  - **Ollama path** keeps `/api/tags` + `/api/show` (still does the 16 k context-size check).
  - **OpenAI-compat path** (LM Studio + Other) hits `/v1/models`, looks up the configured model in the `data[]` array. Context-size check is skipped (OpenAI `/v1/models` doesn't expose it; user is trusted).
- `Result` record gains two fields: `BackendType` (`Backend` enum: Ollama / LMStudio / OpenAICompat) and `ModelName` (the model the probe actually looked for). The UI uses these to render backend-aware messaging.
- `RequiredModel` const kept as an alias of the new `DefaultModeratorModel` so any v2.4.39 caller still compiles.
- Button **"Verificar Ollama" → "Verificar conexión"**. Status banner header **"SÍNTESIS LOCAL (qwen3:32b)" → "ESTADO DEL SERVIDOR"** — both now backend-agnostic.
- LM Studio model matching is **lenient**: exact case-insensitive first, then a `Contains` fallback. Handles the variants LM Studio exposes (e.g. user types `qwen3.6-35b-a3b`, server reports `qwen3.6-35b-a3b@Q4_K_M`).

### Tests

- **4 new** tests in `CouncilPreflightServiceTests`:
  - `LMStudioBackend_probesV1Models_NotApiTags` — confirms backend routing.
  - `LMStudioBackend_modelMissing_reportsModelNotPresent` — error path with LM Studio-flavoured hint.
  - `OpenAICompatBackend_endpointDown_reportsUnreachable` — third backend covered.
  - `CustomModeratorModelName_isUsedInsteadOfDefault` — pins the override behaviour for the model name.
- VELO.Core.Tests: 98 → **102** (+4).
- Full suite: 387 → **391**. All green.

### Notes

- Disclaimer state, toggles disabled-until-v2.5.x, and inert runtime (no Council UI entry point) all unchanged from v2.4.38/4.39. v2.4.40 is purely about the moderator-backend story being usable for **any** local LLM stack, not Ollama-only.
- The Phase 4 spec at `docs/Phase4/COUNCIL_MODE_ANALYSIS.md` was Ollama-centric; the practical Phase 4.1 work will follow this v2.4.40 abstraction (a single backend-typed handle) rather than the spec's Ollama API verbatim.

---

## [2.4.39] — 2026-05-13 — Hotfix: Council preflight reuses Custom AI endpoint by mistake

First post-Phase-4.0 hotfix. The Settings → 🤝 Council panel landed in v2.4.38 with two issues maintainer testing surfaced:

1. **Bug** — `CouncilPreflightService` read the Ollama endpoint from `SettingKeys.AiCustomEndpoint` (the Custom AI Mode setting). Users with Custom AI Mode pointing at LM Studio (`http://localhost:1234`) or any non-Ollama OpenAI-compatible server saw the Council preflight probe that endpoint, fail, and report "No se pudo conectar a Ollama en http://127.0.0.1:1234. ¿Está corriendo?" — confusing, because the user *does* have Ollama running on its canonical port `11434`.
2. **UX (working as designed, clarified)** — the four provider toggles (Claude / ChatGPT / Grok / Ollama) are disabled by Phase 4.0 design. They become active in v2.5.0 once Phase 4.1 (bridge + capture) ships. The label "PROVEEDORES (activables en v2.5.x)" was on-screen but the disabled state read as a bug.

### Fixed

- **Council Ollama endpoint** now lives in its own `SettingKeys.CouncilOllamaEndpoint` setting, independent from `SettingKeys.AiCustomEndpoint`. Default: `http://localhost:11434`. Custom AI Mode keeps pointing wherever the user configured it; Council always probes its own endpoint.
- **`CouncilPreflightService.DefaultOllamaEndpoint`** promoted from `private` to `public` so the UI can pre-populate the new endpoint field with the same canonical default.

### Added

- **Council Ollama endpoint TextBox** in Settings → 🤝 Council, above the status banner. Lets the user redirect Council at a remote Ollama (e.g. `http://192.168.1.50:11434`) or a non-default port without leaving the Settings dialog. Help text under the box clarifies: *"Council usa su propio endpoint. Independiente de Custom AI Mode (que podés tener apuntando a LM Studio u otro servidor)."*
- **Persistence**: the endpoint is saved (a) when the user clicks "Verificar Ollama" — so the probe always uses the latest typed value — and (b) when the Settings dialog is saved via the bottom bar.

### Notes about the disabled toggles (not a bug)

The four "Proveedores" checkboxes (Claude / ChatGPT / Grok / Ollama) remain `IsEnabled="False"` in v2.4.39. This is intentional: Phase 4.0 ships only the UI shell + foundation services (containers, fingerprint policy, preflight, disclaimer). The toggles become active in **v2.5.0** when Phase 4.1 (the bridge JS + WebMessage protocol + paste/send/capture flows) lands. Until then, Council Mode is not openable from the command palette or any menu — the Settings panel is preview-only.

### Tests

- **2 new** regression tests under `tests/VELO.Core.Tests/CouncilPreflightServiceTests.cs`:
  - `DefaultEndpoint_isOllamaCanonical11434_NotLmStudio1234` — pins the default port at 11434 and verifies the probe hits it.
  - `CustomEndpoint_fromCouncilSpecificSetting_overridesDefault` — sets both `CouncilOllamaEndpoint` and `AiCustomEndpoint` to different hosts; asserts the probe uses the Council one and ignores the Custom AI one.
- VELO.Core.Tests: 96 → **98** (+2).
- Full suite: 385 → **387**. All green.

---

## [2.4.38] — 2026-05-13 — Phase 4.0 (Council Mode foundation, invisible to users)

Phase 4 (Council Mode) starts. This release lands the **complete Phase 4.0 foundation** — eight chunks of infrastructure that Council UI will sit on top of. No user-facing entry point yet: there is no command palette entry, no keyboard shortcut, no menu item to open Council Mode. The Settings → Council panel is shipped as a visible UI shell with the four provider toggles **disabled** until v2.5.x.

The intent (per the Phase 0 analysis): ship the foundation **invisible**, let it bake under normal 2-pane split-view usage to surface bugs, then layer the user-facing UI on top in Phase 4.1 → 4.4.

### Added

- **`CouncilLayoutController`** (`src/VELO.App/Controllers/CouncilLayoutController.cs`) — peer to BrowserTabHost / SessionPersistenceController / CommandPaletteController / KeyboardShortcutsController. Owns the 2×2 split-view layout on `BrowserContent` for Council sessions. Public API: `IsActive`, `PanelTabIds`, `ActivateAsync`, `DeactivateAsync`, `RefreshLayout`, `GetPanelCell(int)`. Internally manipulates `Grid.ColumnDefinitions` / `RowDefinitions` to a 3×3 (panel/splitter/panel) topology with two `GridSplitter` seams in a "+" pattern.
- **MainWindow wiring** (`MainWindow.xaml.cs`) — `_isCouncilMode` field, `_councilLayout` lazy init, `ActivateCouncilModeAsync` / `DeactivateCouncilModeAsync` / `RefreshCouncilLayout` methods. Mutually exclusive with `_isSplitMode` (split tears down first). No callers yet — methods are reachable from future Council UI without further plumbing.
- **`CouncilContainerPolicy`** (`src/VELO.Core/Containers/CouncilContainerPolicy.cs`) — policy hub for the four Council containers (`council-claude` / `-chatgpt` / `-grok` / `-ollama`). `ResolveFingerprintLevel(containerId, globalLevel)` downgrades Council slots to "Standard" so Cloudflare anti-bot at Anthropic/OpenAI/xAI doesn't trip. Banking mode keeps its own stricter policy via `BankingContainerPolicy`, untouched.
- **Per-container fingerprint override** wired in `MainWindow.OnTabCreated` — before `BrowserTabHost.BuildAndWire` the host resolves effective fingerprint level via the new policy.
- **Council container seed** in `VeloDatabase` — four new `Container.CouncilClaude/ChatGpt/Grok/Ollama` static rows. Idempotent `InsertOrReplaceAsync` on every launch so users upgrading from a pre-Council install get the rows automatically; existing v2.4 containers (Personal/Work/Banking/Shopping/None) untouched.
- **`CouncilPreflightService`** (`src/VELO.Core/AI/CouncilPreflightService.cs`) — pure HTTP probe verifying that Ollama is reachable, `qwen3:32b` (the synthesis moderator) is installed, and its context window is ≥ 16 k tokens. Never throws — every failure mode surfaces via the `Result` record (`IsHealthy` / `EndpointReachable` / `ModelPresent` / `ContextSize` / `FailureReason`). Constructed with a `Func<HttpClient>` factory for offline-testable unit tests.
- **`CouncilFirstRunDisclaimer` modal** (`src/VELO.UI/Dialogs/CouncilFirstRunDisclaimer.xaml(.cs)`) — explains the privacy contract, runs `CouncilPreflightService.CheckAsync` in the background, lets the user opt out per provider before activation. Persists the result via new `SettingKeys.CouncilDisclaimerAccepted` + `CouncilEnabledClaude/ChatGpt/Grok/Ollama`. Modal built in Phase 5 vocabulary (Card surfaces, BadgeXxx tokens, GhostButton + PrimaryButton action row, purple-glow shield-circle header).
- **Settings → Council page** (`SettingsWindow.xaml`) — new sidebar entry "🤝 Council" between IA and Idioma. UI shell with: live preflight status banner ("Verificar Ollama" button), four per-provider toggles **disabled until v2.5.x**, disclaimer status line + "Mostrar disclaimer otra vez" button for QA / first-run rehearsal.
- **New `SettingKeys`** (`src/VELO.Data/Models/AppSettings.cs`): `CouncilDisclaimerAccepted` + `CouncilEnabledClaude` + `CouncilEnabledChatGpt` + `CouncilEnabledGrok` + `CouncilEnabledOllama`.

### Tests

- **24 new** unit tests under `tests/VELO.Core.Tests/CouncilContainerPolicyTests.cs` — `Applies()` matrix, case-sensitivity, `ResolveFingerprintLevel` covers Council-vs-other and null containerId, list count + brand constant.
- **6 new** unit tests under `tests/VELO.Core.Tests/CouncilPreflightServiceTests.cs` — endpoint unreachable, /api/tags non-200, model missing (with friendly "ollama pull" hint), happy path, context too small, /api/show fails but model present.
- VELO.Core.Tests: 66 → **96** (+30).
- Full suite: 355 → **385**. All green.

### Phase 4 next steps

- **4.1 — Bridge + capture (2 weeks)**: domain models (`CouncilSession`, `CouncilRound`, `CouncilCapture`, block-type hierarchy), `CouncilOrchestrator`, `council-bridge.js`, `adapters.default.json` v0, mini-toolbar per panel, Council Bar overlay, end-to-end smoke. Toggles in Settings page get enabled once 4.1 ships.
- **4.2 — Moderator (1 week)**: token estimator, compressor, moderator + synthesis dialog, Ollama integration test.
- **4.3 — Advanced blocks (1.5 weeks)**: artifact / image / file extractors, conditional toolbar buttons.
- **4.4 — Polish & ship (1 week)**: markdown + JSON export, hotkeys, full settings wired, user docs. **v2.5.0 stable.**

### What didn't change

- 2-pane split-view (`_isSplitMode`) — untouched. Council activation just tears down split first, mutually exclusive.
- Existing tab activation flow — no Council branch added to `OnTabActivated` yet (chunk H's caveat: the foundation is reachable but not triggered).
- Phase 5 palette / Phase 3 / Phase 2 surfaces — untouched.

---

## [2.4.37] — 2026-05-12 — Phase 5.3 v3: vector icons + active-tab glow + focus ring

Iteration on Phase 5.3 after maintainer feedback on v2.4.36: icons still felt off, separators too flat, "metele amor wey algo que llame la atención". Pure visual polish — no layout or contract changes.

### Changed

- **URL bar nav icons → WPF `Path` geometry inside `Viewbox`.** No more dependency on a system icon font (Segoe Fluent Icons / Segoe MDL2 Assets), no more code-point guessing. Each icon is hand-traced SVG-style strokes drawn in a 24×24 canvas, scaled to 14–15 px via `Viewbox`. Look identical on Windows 10, Windows 11, and any DPI. Geometry inspired by Lucide (MIT):
  - Back: chevron-left `M 15,6 L 9,12 L 15,18`
  - Forward: chevron-right `M 9,6 L 15,12 L 9,18`
  - Reload: 3/4 circular arc + corner arrow `M 20,12 A 8,8 0 1 1 6.5,6 L 4,8 M 4,4 L 4,8 L 8,8`
  - Stop (loading state): clean X `M 6,6 L 18,18 M 6,18 L 18,6`
  - Menu: three stacked dots as `Ellipse` elements in a vertical `StackPanel`
  All strokes use `StrokeLineJoin="Round"`, `StrokeCap="Round"` with 2 px thickness — same look-and-feel as Edge / Settings nav icons.
- **`SetLoading` toggle**: instead of swapping `Button.Content` between two unicode glyphs, the XAML now ships *both* icons inside the Reload button and the code-behind flips `Visibility` of `ReloadIcon` ↔ `StopIcon`. Cleaner, no font lookup involved.
- **Active tab → purple glow.** TabSidebar's `IsActive=True` DataTrigger now also sets `Effect` to a soft `DropShadowEffect` (`#7C5CFF` @ 0.55 opacity, 16 px blur, 0 depth). Same treatment in collapsed sidebar mode (14 px blur). The active tab now reads as the *focus point* of the window, not just a tab with a coloured outline. Corner radius bumped 4 → 6 to match the new glow's soft feel.
- **URL pill → focus ring.** When keyboard focus is anywhere inside the pill (typing in the URL field), the border switches to `AccentPurpleBrush` and a soft purple `DropShadowEffect` activates (`#7C5CFF` @ 0.45 opacity, 14 px blur). Auto-revert on blur via WPF style triggers; no code-behind plumbing.
- **`WindowChromeHelper` → rounded corners (Windows 11).** Adds an explicit call to `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)` right after the dark title bar flip. Windows 10 returns `E_INVALIDARG` and we ignore; Windows 11 picks up the explicit "round" preference. Resolves the v2.4.36 maintainer ask "la barra puede redondearse".

### What didn't change

- Phase 5 palette / tokens — untouched.
- Existing surfaces re-skinned in v2.4.32/.33/.34 — untouched.
- Layout, bindings, event handlers, behaviour — zero changes.
- Settings dialog content — untouched (separate scope if maintainer wants it).

### Tests

- 355/355 still green (5 Smoke + 49 Core + 122 Security + 136 Agent + 18 Vault + 8 Import).
- Smoke test #1 confirms every new `{StaticResource X}` reference resolves.

---

## [2.4.36] — 2026-05-12 — Phase 5.3 hotfix (icons tofu + title bar fallback)

### Fixed

- **URL bar icons rendering as tofu.** v2.4.35 used `FontFamily="Segoe Fluent Icons,Segoe MDL2 Assets"` (comma-separated fallback) which WPF does not honour reliably for icon glyphs, AND used `U+E72A` for the Forward arrow which does not exist in either font. Hotfix: switch to a single `FontFamily="Segoe MDL2 Assets"` (universal across Windows 10+) and the correct documented code points: `E72B` Back, **`E72D`** Forward, `E72C` Refresh, `E712` More. Icons now render as Microsoft-designed glyphs instead of empty rectangles.
- **Dark title bar not applying.** v2.4.35 wired `DwmSetWindowAttribute` only through the implicit Window style's attached-property setter, which races with ModernWpfUI's own Window style and can silently lose. Hotfix: (a) `WindowChromeHelper.ApplyToWindow` is now a **public method** callable explicitly from a Window's constructor — `MainWindow.xaml.cs` invokes it right after `InitializeComponent()`. (b) The P/Invoke now retries the legacy attribute `19` if the modern attribute `20` returns a non-zero HRESULT (covers Windows 10 builds 18985–19041 in addition to 20H1+).

### Tests

- 355/355 still pass; no behavioural change beyond the corrected glyph code points + the new explicit chrome call.

---

## [2.4.35] — 2026-05-12 — Phase 5.3 polish (dark title bar + Fluent icons)

### Added

- **Dark Win32 title bar** (`WindowChromeHelper.cs`) — every VELO Window now flips `DWMWA_USE_IMMERSIVE_DARK_MODE` via P/Invoke so the system chrome (the strip with min/max/close) renders dark and matches the Phase 5 palette. Wired as an attached property + a single `Setter` on the global `<Style TargetType="Window">` in `DarkTheme.xaml`, so every window inherits it without per-class plumbing. Older Windows builds silently ignore the DWM attribute (no degradation). This was the single most visible v2.4-vs-v2.5 gap reported after the v2.4.34 release.
- **Segoe Fluent Icons** for the URL bar navigation glyphs. Back / Forward / Reload / Menu now consume `FontFamily="Segoe Fluent Icons,Segoe MDL2 Assets"` and Microsoft glyph code points (`U+E72B`, `U+E72A`, `U+E72C`, `U+E712`). When the loading state toggles the Reload button to Stop, it now switches to `U+E711` (Cancel glyph) instead of the unicode `✕`. Same font family Edge, Settings, Office, Start menu all use — VELO blends with Windows 11 native iconography instead of rendering "unicode arrows that look like nothing".

### Changed

- **`UrlBar.SetAiStatus`** — paints `AiDot.Fill` and `AiLabel.Foreground` from theme brush keys (`BadgeGreenBrush` / `BadgeAmberBrush` / `BadgeRedBrush` / `TextMutedBrush`) instead of hardcoded `#00E676 / #FFB300 / #F44336 / #555566`. Same semantic mapping (ready/connecting/error/offline) but the colours now match the rest of the dark palette.
- **`UrlBar.SetBookmarked`** — active-state amber painted from `BadgeAmberBrush` instead of the inline `#FFB300`. Inactive state already used `TextMutedBrush`.

### What didn't change

- The brand emojis (👾 mascot, 🛡 shield, 🔑 vault, ⚡ generator, 📖 reader, ☆ bookmark) stay as is. They identify features with personality and are intentionally NOT replaced with generic Segoe Fluent equivalents — the call between "blend with the OS" and "keep VELO's character" is per-glyph, not blanket.
- TLS indicator (🔒 / 🔓 / ⚠️) and AI robot (🤖) emojis stay — both are status signals where the emoji weight reads instantly.
- No layout or binding changes anywhere; this release is purely Phase 5 polish on the previously-unthemed Win32 chrome and the few remaining unicode arrows.

### Tests

- 355/355 tests pass (5 Smoke + 49 Core + 122 Security + 136 Agent + 18 Vault + 8 Import).
- Smoke test #1 confirms every new `{StaticResource X}` reference resolves; the new `WindowChromeHelper.DarkTitleBar` attached property setter resolves without missing references.

---

## [2.4.34] — 2026-05-12 — Phase 5.2 (MainWindow surfaces — Phase 5 complete)

### Added

- **Phase 5.2 lands → Phase 5 (UI Modernization) is complete.** v2.4.32 shipped the theme tokens + 3 simple dialogs (5.0); v2.4.33 migrated the 5 information-dense dialogs (5.1); this release re-skins the everyday browsing surfaces. End state: every dialog and main-surface XAML in the app speaks the Phase 5 palette.

### Changed

- **VaultWindow VaultScreen + EditScreen** — finishes the Vault catalogue started in v2.4.32. Header buttons use `PrimaryButton` (Agregar) + `GhostButton` (Bloquear); search bar gets the rounded `SurfaceLight` shell with purple caret used elsewhere; status bar adopts `BadgeGreen`. EditScreen background switches to `BackgroundDarkest`, the 3 hardcoded `#FFEE5555` error tints migrate to `BadgeRedBrush`, password-strength bar background becomes `SurfaceLight`, the generator panel adopts the `Card` style, action row uses `GhostButton` / `PrimaryButton`.
- **AIResultWindow** rewritten on the new palette. `SurfaceDark` header + action bar, loading panel becomes a rounded card with `SurfaceMid` background and `AccentPurpleLight` text, action buttons use `GhostButton` / `PrimaryButton` pair. AdapterChip keeps its dynamic hardcoded colors because code-behind repaints it based on adapter state (Claude vs local).
- **OnboardingWizard** — palette pass per Phase 5 directive (no layout change). `BackgroundDarkBrush` → `BackgroundDarkestBrush`, `AccentBlueBrush` → `AccentPurpleLightBrush` (VELO logo + step indicator + level line), inline `PasswordBox` styling replaced with `ModernPasswordBox` style, navigation buttons swap to `GhostButton` (Atrás) + `PrimaryButton` (Continuar). Step content unchanged.
- **AutofillToast** — `SurfaceDark` background, `AccentPurple` border (was a blue `#FF3A6CD8`), breach banner uses `BadgeRed` tokens, buttons adopt the `GhostButton` + `PrimaryButton` pair. The button named `x:Name="PrimaryButton"` now also wears `Style="{StaticResource PrimaryButton}"` — same label intentionally serving both name and style purposes.
- **BlockNarrationToast** — `SurfaceDark` background + `AccentPurple` border (the AI-narrated quality of this toast is now signalled by the purple frame rather than a one-off `#FF8E44AD`). Body text foreground migrates to `TextPrimary` with `Opacity="0.85"` for the secondary-text feel. Dismiss button uses `GhostButton`.
- **PrivacyReceiptToast** — `SurfaceDark` background + `BadgeGreen` border (kept green to mark a positive "session closed safely" signal). The three stat chips (rastreadores / anuncios / fingerprint) now consume `BadgeGreenBg+Bg / BadgeAmberBg+Bg / BadgeRedBg+Bg` token pairs from Phase 5.0 instead of inline `#1A2EB54F`-style alpha hexes. CornerRadius bumped from 8 to 12 to match the dialog catalogue.
- **TabSidebar** (palette only — **vertical layout preserved** per Phase 5 directive; the prototype's horizontal tab strip is explicitly rejected and that decision stays locked). Sidebar background becomes `SurfaceDark`; top / strip / bottom action bars get `BackgroundDarkest`; borders become `BorderSubtle`. **Active tab ring switches from cyan `#FF00E5FF` to `AccentPurple`** — the most visible single change of the v2.4 → v2.5 transition. Active-row overlay is now a semi-transparent purple wash `#A6261942` so the per-tab accent tint still bleeds through. Inactive title text moves to `TextMuted`, active title to `TextPrimary` for clearer contrast. Hover border emphasis uses `BorderEmphasis`.
- **UrlBar** — toolbar surface becomes `SurfaceDark`. URL pill background becomes `SurfaceMid`, border `BorderEmphasis`, corner radius 10. URL field gets purple caret + selection brush. Bookmark/reader/zoom icons unchanged shape but inherit the new `IconButton` hover. TL;DR badge migrates from the cyan `#1A00E5FF / #FF00E5FF` token to the `BadgeBlue` token pair (still distinct from primary purple — TL;DR is a feature signal, not the primary CTA). Loading sweep at the bottom of the toolbar swaps cyan gradient stops for purple ones.
- **NewTabPage** — `BackgroundDarkest` base, VELO logo migrates to `AccentPurpleLight` with bumped weight, search bar wears `SurfaceLight` with purple caret + 28-px pill radius, privacy stats bar adopts the `Card` style. Top sites tiles (built in code-behind) untouched.
- **VeloAgentPanel** — outer chrome to `BackgroundDarkest`, header + input area to `SurfaceDark`, input pill to `SurfaceLight` with `BorderEmphasis` and purple caret. Typing-indicator dots adopt `AccentPurpleLight`. **Send button** swaps from cyan disc to gradient-purple disc with purple glow effect — clearly signals AI as the centre of gravity.
- **MainWindow** — Window root gains explicit `Background="BackgroundDarkestBrush"` so the chrome between panels picks up the new base. FindBar (Ctrl+F) migrates from hardcoded blue-ink `#1E1E30 / #12122A / #A0A0C0` palette to `SurfaceDark` + `SurfaceLight` input pill + purple caret + `IconButton` action triplet.

### Tests

- 355/355 tests pass (49 Core + 122 Security + 136 Agent + 18 Vault + 8 Import + 5 Smoke).
- Smoke test #1 confirms every new `{StaticResource X}` reference resolves cleanly across the migrated surfaces.

### Migration scope at end of Phase 5

Every WPF dialog and main-surface UserControl in `src/VELO.UI/` and `src/VELO.App/` has been visited. Residual hardcoded `#color` references after this release are all **intentional** — they're either:

- **Dynamic colours** that the code-behind repaints based on runtime state (Shield level greens/yellows/reds in SecurityInspectorWindow, AdapterChip in AIResultWindow, AiDot in UrlBar).
- **DropShadowEffect Color attributes** — `Color` is a value type that doesn't bind to a `Brush` resource; the literal `#7C5CFF` etc. matches the `AccentPurple` token in spirit.
- **Gradient stops** (loading bar in UrlBar, gradient buttons in DarkTheme) where each stop needs a specific colour value.

Phase 6 (Bitwarden sync) inherits a fully-themed UI baseline. Phase 4 (Council Mode) panels will hereby pick up the palette out of the box.

### What didn't change

- Tab paradigm: vertical sidebar locked, per Phase 5 ground rule (and Phase 4 Council Mode's planned 2×2 layout assumption).
- Any layout, information density, navigation flow, or per-dialog structure.
- The 50+ remaining `BorderBrush` references in `SettingsWindow.xaml` body — those are existing theme refs (older palette token, but the v2.4 `BorderBrush` still produces an acceptable subtle separator under the new background). Future polish if anyone notices.

### Known follow-ups (out of Phase 5 scope)

- VaultWindow LockScreen eye-toggle for password visibility (deferred since v2.4.32 — needs a TextBox-swap + ~15 lines of code-behind).
- Malwaredex stars decision (drop vs. derive from `ThreatType`) — still deferred, doesn't block Phase 5 closure.
- Context menu polish + command palette result rows — touched only via inherited styles (ContextMenu/MenuItem base styles already palette-correct in DarkTheme.xaml); a dedicated polish pass is optional and can ride a future micro-release.

---

## [2.4.33] — 2026-05-12 — Phase 5.1 (info-dense dialogs re-skin)

### Added

- **Phase 5.1 lands** — 5 information-dense dialogs migrated to the new visual language adopted in v2.4.32 (Phase 5.0). All re-skinned per their corresponding prototype frames; same data, same code-behind contracts, same bindings, all `x:Name` preserved. Visual refresh only — no layout, paradigm or information-flow changes.

### Changed

- **HistoryWindow.xaml** re-skinned per frame 4. The 25 hardcoded hex colors (worst offender of the dialog catalogue) are all replaced with theme tokens. Header gains a larger title and uses `IconButton` + `GhostButton` for reload/clear actions. Search bar gets the rounded `SurfaceLight` shell with magnifier icon + purple caret. Each history row becomes a 12-px corner-radius card on `SurfaceMid`. Status badges (blocked / trackers / malware / no-threats) all consume the new `StatusPill` style + `BadgeXxxBgBrush`/`BadgeXxxBrush` token pairs.
- **BookmarksWindow.xaml** follows the History pattern (the analysis flagged this dialog had no prototype frame and instructed to inherit History's layout language). Background → `BackgroundDarkest`, search bar gets the same shell + magnifier as History, header title bumped to 22 px SemiBold.
- **MalwaredexWindow.xaml** re-skinned per frame 5. Header gets a circular icon surface (purple-tinted `BadgeVioletBg` with `AccentPurple` border) housing the 👾 mascot, with subtitle moved into the header for density. Empty state gets the same circular purple-glow icon treatment as Downloads (frame 7) — consistent visual language across empty states. Stars decision (drop vs. derive from `ThreatType`) deferred to implementation; the existing footer line covers it for now.
- **SecurityInspectorWindow.xaml** re-skinned per frame 6. Local `SectionBorder` / `SectionTitle` / `DataLabel` / `DataValue` / `ActionButton` styles migrated from hardcoded colors to theme tokens. `SectionBorder` now `BasedOn="{StaticResource Card}"` so every section inherits the 12-px corner radius and `SurfaceMid` background consistently. Shield badge becomes a pill (`CornerRadius=99`) — note that the green/yellow/red/gold colours stay hardcoded as defaults because the code-behind repaints them dynamically based on `ShieldLevel`. Header / action bar move from solid `#FF111111` to `SurfaceDarkBrush`. Action buttons now use the `GhostButton` aesthetic via `BasedOn`.
- **SettingsWindow.xaml** — palette + spacing pass only, no layout changes (per Phase 5 analysis directive). `BackgroundDarkBrush` → `BackgroundDarkestBrush`, `AccentBlueBrush` → `AccentPurpleLightBrush` (sidebar nav active state + VELO logo), `BackgroundLightBrush` → `SurfaceLightBrush`, `BorderBrush` → `BorderSubtleBrush` on the sidebar separator. Sidebar gains its own `SurfaceDark` background for a cleaner two-column visual split.

### Tests

- 355/355 tests pass (49 Core + 122 Security + 136 Agent + 18 Vault + 8 Import + 5 Smoke).
- Smoke test #1 (`Every_StaticResource_Reference_Has_A_Definition_Somewhere`) confirms every new `{StaticResource X}` reference resolves cleanly — that's the early-warning that the migration didn't miss any token.

### Migration scope

Hardcoded `#color` count across the now-migrated dialogs:

  * HistoryWindow.xaml: 25 → 0 (clean migration).
  * BookmarksWindow.xaml: 0 → 0 (clean from before — only key bindings updated).
  * MalwaredexWindow.xaml: 0 → 1 (legitimate DropShadowEffect `Color`).
  * SecurityInspectorWindow.xaml: ~26 hex inlined → 4 (Shield level defaults that code-behind overrides — kept by design).
  * SettingsWindow.xaml: 0 → 0 (palette refs updated, no hex changes).

Remaining v2.4-palette surfaces, all part of Phase 5.2:

  * NewTabPage, UrlBar, TabSidebar, AgentPanel (+ chat history), MainWindow chrome (title bar, splitter), AutofillToast, BlockNarrationToast, PrivacyReceiptToast, AIResultWindow, OnboardingWizard, command palette result rows, context menu styling polish.
  * VaultWindow EditScreen (add/edit form) — separate from Phase 5.1 because frame 3 only covers the unlock dialog; the add/edit form picks up a re-skin in 5.2.

### Known follow-ups

- Stars in MalwaredexWindow card grid (per prototype frame 5) — decision deferred. Default behaviour: drop them; if the maintainer asks for them, the cleanest mapping is `ThreatType` → stars (Malware = 3, Tracker = 1, etc.).
- VaultWindow `EditScreen` and `VaultScreen` (post-unlock) still on the v2.4 palette. Picked up in Phase 5.2.

---

## [2.4.32] — 2026-05-12 — Phase 5.0 (theme + Vault/ClearData/Downloads re-skin)

### Added

- **Phase 5 — UI Modernization v0.0 lands.** New visual language adopted from the prototype Phase 0 analysis (`docs/Phase5/UI_MODERNIZATION_ANALYSIS.md`): darker base surfaces (`#0A0A12` chrome, `#111118` surfaces), purple accent (`#7C5CFF` primary), gradient CTAs, status pills, rounded card panels.
- **Theme resources extended (additive, no breaking change).** `src/VELO.UI/Themes/Colors.xaml`: 11 new colors + 14 new brushes (surfaces, purple accent shades + glow, badges with semi-transparent backgrounds, gradient brushes for primary and danger CTAs).
- **8 new styles in `DarkTheme.xaml`** — `PrimaryButton` (gradient purple CTA with glow), `DangerButton` (gradient red CTA), `GhostButton` (transparent + purple outline), `StatusPill` (rounded chip Border), `Card` + `ElevatedCard` (rounded surfaces), `ModernTextBox` + `ModernPasswordBox` (purple focus). All opt-in via `Style="{StaticResource X}"`; unmigrated controls keep the v2.4 cyan-accent look.
- **`VaultWindow.xaml` LockScreen re-skinned per frame 3.** Circular shield icon with purple glow, large title, password input with purple focus, full-width gradient unlock button, "Local encrypted vault" footer with green dot indicator. VaultScreen + EditScreen left unchanged (different scope).
- **`ClearDataWindow.xaml` re-skinned per frame 7 left.** Trash icon in red badge surface, checklist with each option wrapped in a `Card` for clear tappable rows, ghost/danger action pair at the bottom.
- **`DownloadsWindow.xaml` re-skinned per frame 7 right.** New empty-state with circular icon + "No downloads yet" caption (visible when the list has no items), card-styled download rows, purple progress bar replacing the old cyan, ghost-button "Clear completed".
- **3 new localization keys in 8 languages** — `vault.unlock.footer`, `downloads.empty.title`, `downloads.empty.subtitle`.

### Changed

- `Microsoft.Extensions.*` packages remain pinned at 10.0.7 (carry-over from v2.4.31 fix, see `b18bee1` / `eeb3d01`).
- `BackgroundDarkBrush` (legacy `#0D0D0D`) is **untouched**. Dialogs not migrated continue to use it. Migrated dialogs reference `BackgroundDarkestBrush` (`#0A0A12`) for a slightly darker modal look.
- No breaking change. Smoke test #1 (XAML resources) still verifies every `{StaticResource X}` reference resolves.

### Tests

- 355/355 tests pass (49 Core + 122 Security + 136 Agent + 18 Vault + 8 Import + 5 Smoke).
- Build verified for Debug; smoke test #1 confirms no missing theme resources after the extension.

### Migration scope

- Hardcoded `#color` count post-Phase 5.0:
  - VaultWindow.xaml: 4 (3 in EditScreen — future scope; 1 in DropShadowEffect — legitimate, `Color` not `Brush`).
  - ClearDataWindow.xaml: 0 (clean migration).
  - DownloadsWindow.xaml: 1 (DropShadowEffect — legitimate).
- 17 dialogs / controls still on the v2.4 palette: HistoryWindow, MalwaredexWindow, SecurityInspectorWindow, BookmarksWindow, SettingsWindow → Phase 5.1; NewTabPage, UrlBar, TabSidebar, AgentPanel, MainWindow chrome, toasts → Phase 5.2.

### Known follow-ups

- VaultWindow `LockScreen` does not include the eye-toggle from prototype frame 3 (PasswordBox visibility requires a TextBox swap + 15 lines of code-behind). Functional gap, deferred to a follow-up.
- VaultWindow EditScreen (add/edit password entry) still on the v2.4 palette. Will adopt the new look in Phase 5.1.

---

## [2.4.31] — 2026-05-12 — Phase 3 / Sprint 10b chunk 6

### Changed

- **`BrowserTab.xaml.cs` split into four `partial class` files** (Phase 3 / Sprint 10b chunk 6):
  - `BrowserTab.xaml.cs` — core: UserControl declaration, public events, private state, DI setters, constructor, `Initialize`, `EnsureWebViewInitializedAsync` (subscribes the handlers). 1878 → 314 lines.
  - `BrowserTab.PublicApi.cs` (new, 390 lines) — host-facing methods: `NavigateAsync`, `GoBack/Forward/Reload/Stop`, `ZoomIn/Out/ResetZoom`, `FindAsync/Clear`, `CloseTab`, `AllowOnce`, `ExecuteScriptAsync`, `ClearBrowsingDataAsync`, `GetPageContentAsync`, `ToggleReaderModeAsync`, paste cluster (`HandlePasteRequest`/`PasteTextAsync`/`PasteTextIntoFocusedEditableAsync`), `FillCredentialAsync`, `OpenDevTools`, `SetContainer`, view-switching helpers (`ShowNewTabPage`/`ShowWebView`/`EnsureWebViewReadyAsync`).
  - `BrowserTab.Events.cs` (new, 765 lines) — WebView2 event handlers: `OnWebResourceRequested` + `ProcessRequestAsync`, `OnNavigationStarting`, `OnServerCertificateError`, `OnWebMessageReceived`, `OnNavigationCompleted`, `OnTitleChanged`, `OnLaunchingExternalUriScheme`, `OnNewWindowRequested`, `OnDownloadStarting`, `NewTabPage_NavigationRequested`, `OnContextMenuRequested` + the WPF dark-theme menu fallback colors.
  - `BrowserTab.Helpers.cs` (new, 492 lines) — pure helpers and constants: `IsExternalScheme`/`GetScheme`/`TryLaunchExternalUri` + the `_webSchemes`/`_allowedExternalSchemes`/`_lastLaunched*` external-launch cluster, `BuildAboutPage`/`BuildAboutPageTemplate`, `BuildReaderPage`, `IsSameEtld`/`GetEtld`, `ShortenUrl`, `GetHost`, `ConsentScript`, `LoadScriptResourceAsync`.
- **No behavioural changes.** All members keep the same access modifiers, signatures, and call-sites. WebView2 event subscription order in `EnsureWebViewInitializedAsync` is preserved.
- **`WiringSmokeTests.BrowserTab_setter_methods_must_be_called_from_host` widened** to scan every `BrowserTab*.cs` partial under `src/VELO.UI/Controls/`, not only `BrowserTab.xaml.cs` — a setter landing in any sibling partial (e.g. `SetContainer` in `BrowserTab.PublicApi.cs`) is still enumerated.

### Why

`BrowserTab.xaml.cs` had grown to 1878 lines and was the largest file in the repo. The split closes the Phase 3 / Sprint 10b refactor work (chunks 1, 2, 4, 5 had already been extracted from MainWindow.xaml.cs in v2.4.27–v2.4.30) and unblocks chunk 3 (TabEventController), which was previously dependent on the tighter shape the partials now expose. It is a prerequisite for Phase 4 (Council Mode) where four `BrowserTab` instances host the 2×2 panel layout.

### Tests

- 355/355 tests pass (49 Core + 122 Security + 136 Agent + 18 Vault + 8 Import + 5 Smoke).
- Build verified for Debug and Release self-contained (lesson #16 — Release publish catches FQN regressions that Debug incremental misses).

---

## [2.0.0] — 2026-04-19 — Fase 2

### Added

#### Sprint 1 — Trust & Transparency
- **Shield Score** — visual security badge (Red / Yellow / Green / Gold) in the URL bar; updated on every navigation and TLS event
- **Privacy Receipt** — per-session summary of trackers blocked, TLS status, AI verdict, and fingerprint attempts
- **GoldenList** — curated allow-list of trusted sites that bypasses aggressive blocking; auto-updated from GitHub

#### Sprint 2 — Shield Score + Privacy Receipt
- `SafetyScorer` service — deterministic shield-score computation from TLS, AI, tracker, and fingerprint signals
- `SafetyResult` / `SafetyContext` value types — immutable snapshots passed to URL bar and Security Inspector
- `PrivacyReceiptService` — persists per-session stats to SQLite; powers NewTab v2 lifetime counters

#### Sprint 3 — Container Advanced + Paste Guard
- **Container expiry** — containers can have a TTL; `ContainerExpiryService` auto-purges expired ones at startup
- **Banking mode** — `IsBankingMode` flag on Container activates anti-capture and clipboard protection
- **Paste Guard** — `PasteGuard` service detects and warns on sensitive clipboard content (passwords, card numbers, tokens)

#### Sprint 4 — AI seguro
- **VeloAgent chat panel** — sidebar AI assistant; adapters: LLamaSharp (local GGUF model) and Ollama HTTP
- `AgentActionSandbox` — restricts agent-initiated navigation and JS execution to safe operations
- `AgentActionExecutor` — translates agent intents into browser actions with sandbox enforcement
- `AISecurityEngine` — classifies every page with the configured AI adapter; result fed into Shield Score

#### Sprint 5 — UX moderna
- **Workspaces** — named tab groups with color labels; `WorkspaceRepository` + `WorkspaceEntry` SQLite model
- **Split View** — side-by-side tab layout controlled from the sidebar
- **Tab tear-off** — drag tab out to new window (Windows drag shell integration)
- **Command Bar** (`Ctrl+K`) — fuzzy search across open tabs, bookmarks, history, and registered commands
- **Glance link preview** — hover any link ≥600 ms to see a lightweight preview without full navigation

#### Sprint 6 — Inspector + NewTab v2
- **VELO Security Inspector** (`Ctrl+Shift+V`) — standalone non-modal window showing per-tab TLS details, tracker counts, AI verdict, fingerprint protection status, and block reasons; supports JSON export
- **NewTab v2** — redesigned new-tab page with real-time clock, top-sites mosaic (8 tiles, 2 rows), inline search bar, and lifetime privacy stats footer
- `TopSiteEntry` record + `HistoryRepository.GetTopSitesAsync()` — aggregates visits by host for top-sites tiles
- `HistoryRepository.GetLifetimeStatsAsync()` — returns total trackers blocked, requests blocked, and sites visited

#### Sprint 7 — Distribution
- **Portable mode** — `DataLocation` class; placing `portable.flag` next to the exe redirects all user data to `<exe-dir>\userdata\`; no registry writes in portable mode
- **Auto-updater** — `UpdateChecker` polls GitHub Releases API every 24 h (first check 3 min after startup); raises `UpdateAvailable` event; MainWindow shows a confirmation dialog — no silent downloads
- **Winget manifest** — `manifests/v/velo/velo/2.0.0/` — three-file schema 1.6.0 set (version + installer + defaultLocale); ready for `winget-pkgs` PR
- **Chocolatey package** — `chocolatey/velo.nuspec` + `tools/chocolateyInstall.ps1` + `tools/chocolateyUninstall.ps1` + `VERIFICATION.txt`; ready for community.chocolatey.org submission

### Changed
- `VeloDatabase` constructor now accepts optional `dataFolderPath` parameter for portable-mode support
- `DependencyConfig.Build()` uses `DataLocation.GetUserDataPath()` for all paths (logs, DB, model files, WebView2 profile)
- `VELO.UI` project now references `VELO.Data` to allow `NewTabPage` and `BrowserTab` to consume `HistoryRepository`
- History table: added `BlockedCount`, `TrackerCount`, `MalwareCount`, `MonsterCaptured` columns (ALTER TABLE migration, non-breaking)
- Containers table: added `ExpiresAt`, `IsBankingMode` columns (ALTER TABLE migration, non-breaking)

### Security
- All update checks use HTTPS; SHA-256 + Authenticode verification on downloaded installer before launch
- `AgentActionSandbox` prevents VeloAgent from navigating to `file://`, `javascript:`, or non-HTTP(S) schemes
- `PasteGuard` never logs or transmits clipboard contents

---

## [1.x.x] — Fase 1

See git history for Fase 1 changes (initial browser shell, blocklist engine, DNS-over-HTTPS, Vault, Cookie-wall bypass).
