# Council Mode — Codebase Analysis (Phase 0 output)

**Status:** Phase 0 (analysis) complete. Pre-implementation.
**Author:** Claude Code session, 2026-05-11
**Input doc:** `council-mode-spec.md` (working draft, 1440 lines, 18 sections)
**Repo state at analysis:** VELO v2.4.28, HEAD `8444e9e`, 355/355 tests green
**Purpose:** Fulfils section 0 of the spec ("Al terminar la lectura del documento + el codebase, Claude Code debe producir..."). Resolves VALIDATE-1..10 and OPEN-1..6 against real code. Produces an implementation plan grounded in actual VELO state instead of assumed infrastructure.

---

## 0. Executive summary

Council Mode is **technically feasible** in VELO with two real prerequisites that the spec assumed existed but don't:

1. **2×2 split-view layout** does not exist. Current `MainWindow` supports only a 2-pane horizontal split (`_isSplitMode` + `_primaryTabId` + `_splitTabId`). Extending to 2×2 requires modifying `BrowserContent` grid topology and the activation/refresh paths.
2. **Per-container fingerprint protection** does not exist. `_fingerprintLevel` is a single string read at startup and pushed to every `BrowserTab.Initialize()` call. To soften fingerprint only inside Council containers, the field has to become a lookup by container ID.

Plus three smaller-but-real corrections to the spec's assumptions:

3. The spec routes Council compressor/moderator through `IAgentAdapter` (which today is `LLamaSharpAdapter` only — local GGUF via llama.cpp, no HTTP-to-Ollama). The real path for HTTP-Ollama is the **AI Mode = Custom (Ollama)** flow that hits `IAIAdapter` + `ChatDelegate`. Council should reuse that, not invent a parallel agent path.
4. `AgentActionSandbox` is for *agent* actions (queueable propose/approve), not for the per-click confirms Council needs. Council's approval pattern (every action = explicit click) is simpler — no sandbox reuse, just normal command-bar style click handlers.
5. `LLamaSharpAdapter` runs with `ContextSize = 4096` baked in. The spec wants 16k context. Either bump the const, parameterise it, or — better — keep LLamaSharp at 4k for the embedded use case and route Council's qwen3:32b through Ollama HTTP (which doesn't need a constant bump in VELO).

**Total realistic effort for Council Mode v0.1:**

| Phase | Work | Duration |
|---|---|---|
| Phase 4.0 — Foundations | 2×2 layout, fingerprint per-container, Council prerequisites in MainWindow | ~1.5 weeks |
| Phase 4.1 — Bridge & capture | Sprint 1+2 of the spec (foundation + bridge + basic capture) | ~2 weeks |
| Phase 4.2 — Moderator | Sprint 3 of the spec (compressor + reasoner + UI) | ~1 week |
| Phase 4.3 — Advanced blocks | Sprint 4 (artifacts, images, files) | ~1.5 weeks |
| Phase 4.4 — Polish & ship | Sprint 5 (export, hotkeys, disclaimers, docs) | ~1 week |

**Total: ~7 weeks** of dedicated work. Matches the spec's own estimate (5-7 sprints × 5-10 days) once the unanticipated Phase 4.0 is added.

**Prerequisite outside Council:** Phase 3 must finish first (Sprint 10b chunks 2/3/4/6 still pending in v2.4.28). Council touches `MainWindow` heavily; doing it before MainWindow is broken into controllers bloats the host class past readability. **Council Mode is Phase 4. Phase 3 ships first.**

---

## 1. VALIDATE-1..10 resolved against real code

Each row references file:line evidence so this is not opinion.

### VALIDATE-1 — WebView2 + ExecuteScriptAsync + WebMessageReceived

**Status:** ✅ Confirmed.

**Evidence:**
- `src/VELO.UI/Controls/BrowserTab.xaml.cs:446-455` wires `WebResourceRequested`, `NavigationStarting`, `NavigationCompleted`, `DocumentTitleChanged`, `NewWindowRequested`, `DownloadStarting`, `ContextMenuRequested`, `WebMessageReceived`.
- `ExecuteScriptAsync` is used in `PasteTextIntoFocusedEditableAsync` (line 197), `GetPageContentAsync` (line 798), autofill flow, paste guard injection, etc.
- `AddScriptToExecuteOnDocumentCreatedAsync` pattern visible in `BrowserTab` script-injection sites (autofill.js, paste-guard.js, glance-hover.js).

**Implication for Council:** the `council-bridge.js` injection mechanism is a straight reuse of the existing pattern — no new infrastructure needed.

---

### VALIDATE-2 — 2×2 Split View layout

**Status:** ❌ Not implemented. Spec assumption is wrong.

**Evidence:**
- `MainWindow.xaml.cs:70-71` declares `_isSplitMode` (bool) and `_primaryTabId` + `_splitTabId` (string). Only TWO panes.
- `ActivateSplit()` at line 2612-2656 sets up `BrowserContent.ColumnDefinitions` with 3 columns: `*` / `4px splitter` / `*`. Hard-coded 2-pane horizontal split.
- `_panesSplitter` is a single `GridSplitter`, not a 2D grid.

**Work required:**
- New layout mode: `CouncilLayoutMode` enum with `TwoByTwo` variant.
- Refactor `BrowserContent` grid to support 2 rows × 2 cols with TWO splitters (vertical + horizontal) in a `+` arrangement.
- New fields: `_panel1Id..._panel4Id` (or a `string[]` panel array indexed 0-3).
- `ActivateCouncilLayout()` separate from `ActivateSplit()`.
- Update `OnTabActivated`, `RefreshSplitLayout`, `UpdatePrimaryUiAsync`, `Window_Closing` to handle 4-panel mode.
- Goal: don't break the 2-pane split — Council adds a new layout, doesn't replace the existing one.

**Estimate:** 2-3 days. Tight integration with the Sprint 10b `BrowserTabHost` chunk (pending) — best done after that chunk lands so the per-tab wiring is already extracted.

---

### VALIDATE-3 — Named containers with isolated cookies

**Status:** ✅ Confirmed.

**Evidence:**
- `src/VELO.Data/Models/Container.cs` declares the `Container` class with `Id`, `Name`, `Color`, `IsBankingMode`, `ExpiresAt`. Built-ins: `Personal`, `Work`, `Banking`, `Shopping`, `None`.
- `ContainerExpiryService` (Phase 2) manages TTLs.
- BrowserTab takes `containerId` in `Initialize()` and routes to the matching WebView2 profile.

**Work required for Council:**
- 4 new built-in containers: `council-claude`, `council-chatgpt`, `council-grok`, `council-custom`. Add as `static readonly Container` fields like the existing ones.
- Seed them in `VeloDatabase.SeedDefaultContainersAsync` (today seeds only Personal/Work/Banking/Shopping/None).
- Cookies/cache/storage automatically isolate because WebView2 profile is keyed on user-data subfolder per container.

**Estimate:** half a day. Trivial.

---

### VALIDATE-4 — IAgentAdapter for Ollama

**Status:** ⚠️ Partially. Spec's assumption is **structurally wrong**.

**Evidence:**
- `IAgentAdapter` is implemented only by `LLamaSharpAdapter` (`src/VELO.Agent/Adapters/LLamaSharpAdapter.cs`). That adapter loads a **local GGUF file** via `LLama` library — no HTTP.
- `ContextSize = 4096` and `GpuLayers = 35` are baked-in constants (line 28-29). Qwen3 32B at 16k context would not fit through this adapter as-is.
- The path that DOES talk to Ollama HTTP today is `AI Mode = Custom (Ollama)` in Settings → `IAIAdapter`-side `CustomAdapter` that hits `http://localhost:11434/v1/chat/completions`.
- `ChatDelegate` pattern (Sprint 10A) routes `AIContextActions`, `BookmarkAIService`, `CodeActions`, `SmartBlockClassifier`, `PhishingShield`, etc. through a single host-supplied delegate that uses the configured AI Mode. The delegate goes through `AgentLauncher.SendAsync` → adapter → reply.

**Correct path for Council compressor/moderator:** reuse `ChatDelegate`. Council services receive the delegate via `AiChatRouter.Register(...)` just like every other AI consumer since v2.4.0. **No new adapter, no LLamaSharp ContextSize change.**

**OPEN-5 resolution implication:** the 4th panel as "native VeloAgent" is fine, but the **compressor/moderator** never run through `IAgentAdapter` — they run through `ChatDelegate`. The local model path is the AI Mode = Custom flow.

**Estimate:** zero work. Reuse what's there.

---

### VALIDATE-5 — Ollama running with qwen3:32b loaded at 16k context

**Status:** ⚠️ Partially.

**Evidence:**
- Settings → AI → Custom mode has a `/v1/models` ping test (`SettingsWindow.OnTestOllamaClick`, lines 366+). That confirms Ollama is up and a specific model name is listed.
- No check today for context window or for the specific model name `qwen3:32b`. The test accepts any model the user typed.

**Work required for Council:**
- Pre-flight check before enabling Council: query `/v1/models`, search for any qwen3:32b-class model, warn if missing or if the user's typed model differs.
- Document in the disclaimer that qwen3:32b 16k is the recommended config (no enforcement; the user can swap).
- Setting "Council reasoner model" + "Council compressor model" already in the spec's settings page (section 11.1). Implement as additional fields that **default to the global AI Custom model**.

**Estimate:** half a day.

---

### VALIDATE-6 — Remote blocklists with refresh pattern

**Status:** ✅ Confirmed and excellent fit for adapters.json updates.

**Evidence:**
- `src/VELO.Security/GoldenList/GoldenListUpdater.cs` fetches a JSON file over HTTPS, validates an `X-Content-SHA256` response header, and applies the update only if the hash matches.
- `BlocklistManager` follows a similar refresh pattern.
- `UpdateChecker` (Sprint 7) handles app-update fetches with SHA verification.

**Work required for Council:**
- New `CouncilAdaptersUpdater` cloning the `GoldenListUpdater` shape (~150 lines).
- Endpoint: `https://raw.githubusercontent.com/badizher-codex/velo/main/council/adapters.json` per the spec.
- Manifest with `adaptersSha256` and `signedBy` per spec section 5.3 — defer signature verification to v0.2 (per OPEN-1 resolution below).

**OPEN-1 resolution:** keep `adapters.default.json` bundled-only in v0.1. The remote refresh adds 2-3 days of supply-chain work (manifest, hash chain, diff UI, kill-switch) that v0.1 doesn't need. Add to v0.2 after the bundled flow proves stable.

**Estimate:** zero for v0.1 (bundled), 3 days for v0.2 remote.

---

### VALIDATE-7 — Command Bar extensible

**Status:** ✅ Confirmed.

**Evidence:**
- `MainWindow.BuiltInCommands()` returns a `List<CommandResult>` with `Icon`, `Title`, `Badge`, and a `Tag` carrying the `Action`. The Settings entry is already there; adding `council` is a one-line addition.
- Command bar UI (`CommandBarControl`) renders the list and dispatches the tag.

**Work required for Council:** add one entry — `{ Icon = "�️", Title = "Council Mode", Tag = (Action)(() => OpenCouncilWorkspace()) }`. Pending Sprint 10b chunk 4 (`CommandPaletteController`) — once the command list moves to a controller, the council entry goes there.

**Estimate:** 15 minutes.

---

### VALIDATE-8 — Settings pages categorised

**Status:** ✅ Confirmed.

**Evidence:**
- `SettingsWindow.xaml` has 7 nav buttons (Privacidad, DNS, IA, Búsqueda, Vault, Idioma, General).
- Adding an 8th nav button + corresponding `<StackPanel x:Name="PanelCouncil">` follows the same pattern as `PanelGeneral` and `PanelIdioma`.
- Localisation keys for the new strings go in `LocalizationService.cs` (~25-30 strings × 8 languages).

**Estimate:** 1 day (the settings panel for Council is substantial — section 11.1 of the spec lists ~20 controls).

---

### VALIDATE-9 — Sandboxed-actions approval pattern

**Status:** ❌ Pattern exists but is **not the right shape** for Council.

**Evidence:**
- `src/VELO.Agent/AgentActionSandbox.cs` is a propose/approve/reject queue keyed on `actionId`. Designed for agent actions that need user confirmation BEFORE execution.
- Council's "approval" model is fundamentally different: every Council action *is* a user click. There's no queue, no pending state — the click IS the approval.

**What Council actually needs:**
- Per-provider one-time disclaimer (per spec section 11.2). Modal shown on first-ever enable, recorded in a settings key like `council.acknowledged.claude=2026-05`.
- Per-action visual confirmation (the mini-toolbar buttons are visible affirmations of intent — no separate confirm).
- The disclaimer flow has nothing to do with `AgentActionSandbox`.

**Work required:**
- New `CouncilFirstRunDisclaimer.xaml(.cs)` modal.
- Per-provider acknowledgement keys in `SettingKeys`: `Council.AcknowledgedClaude`, `Council.AcknowledgedChatgpt`, `Council.AcknowledgedGrok` storing the version string per spec (`requiresUserAcknowledgementVersion`).

**Estimate:** 1 day.

---

### VALIDATE-10 — Fingerprint protection configurable per container

**Status:** ❌ Not implemented. Global only.

**Evidence:**
- `_fingerprintLevel` is a single string field in `MainWindow` (line 49) and `BrowserTab` (line 44). Read from `SettingKeys.FingerprintLevel` at startup, pushed into each tab via `Initialize()`.
- No path to override per container or per tab today.

**Work required:**
- Two options:
  1. **Override field at tab level**: `BrowserTab` accepts an optional `fingerprintLevelOverride: string?` in `Initialize()` (or a setter). Council uses `"Off"` (softened) for its 4 panels.
  2. **Per-container map**: `Dictionary<containerId, string>` for fingerprint level. More general but more refactor.
- Recommended: option 1 (cheaper, minimal blast radius). Council can be the only consumer for v0.1; option 2 if a future feature wants per-container too.
- Apply override in the `fingerprint-noise.js` injection gate (`BrowserTab.xaml.cs:391`).

**Estimate:** 1-2 days.

---

## 2. OPEN-1..6 resolved with codebase justification

| ID | Question | Decision | Justification |
|---|---|---|---|
| OPEN-1 | Remote adapters.json refresh in v0.1 or v0.2 | **v0.2** | Pattern exists (`GoldenListUpdater`) but the manifest+SHA+kill-switch+diff-UI chain adds 2-3 days. v0.1 ships bundled-only; v0.2 adds remote refresh once the bundled flow is stable in production. |
| OPEN-2 | Bridge in isolated world or main world | **Main world (start), isolated if collisions** | WebView2's `AddScriptToExecuteOnDocumentCreatedAsync` runs in main world by default; isolated worlds aren't first-class in WebView2 yet. VELO's existing injection scripts (autofill, paste guard, glance) all use main world and have not collided with Anthropic / OpenAI / xAI sites. Window-namespace under `__veloCouncil` is sufficient isolation in practice. |
| OPEN-3 | HTML→Markdown: own / turndown / C# | **Turndown embedded in bridge** | Spec's recommendation stands. ~15kb minified, robust, no CSP issues observed when injected via `AddScriptToExecuteOnDocumentCreatedAsync` (it runs as part of the host's script context, not page-supplied). C# conversion would require shipping the raw HTML through `postMessage`, doubling the data crossing the boundary. |
| OPEN-4 | Tokenizer real vs char/4 | **char/4 for v0.1** | The estimator only decides direct vs pre-compress; ±20% precision is fine for that gate. Adding `tiktoken-sharp` is +2 MB to installer and licence complications (BSD, but binding to a Python-origin tokenizer). Defer to v0.2 if compression accuracy becomes a real issue. |
| OPEN-5 | 4th panel: Ollama-webui vs native VeloAgent | **Native VeloAgent (extend)** | `AgentPanelControl` already exists with the chat UX VELO users know. Reusing it avoids embedding a 2nd web app and keeps the local-only path consistent. Requires extending `AgentPanelControl` to accept the Council mini-toolbar button set (`Paste Master`, `Capture`, `Add to Summary`). Estimate: 1-2 days extra over the spec's assumption. |
| OPEN-6 | WebView2 panel suspension | **No suspend in v0.1** | WebView2 doesn't expose a clean suspend API. Closing & reopening the WebView loses cookies/state inside Council's session. v0.1 keeps all 4 panels live and documents the 16 GB RAM requirement. Track in v0.2 if memory complaints surface. |

---

## 3. Real prerequisites that the spec didn't list

Beyond the items above, the analysis found three pieces of foundation work that are not in the spec but are required:

### P1. Phase 3 must complete first

Council Mode adds ~20-25 new classes and at least 4-6 sections of new logic in MainWindow (workspace activation, panel-state event handling, master prompt dispatcher, capture buffer UI, synthesis dialog, hotkeys). At v2.4.28, MainWindow is 2883 lines; adding Council on top without refactoring inflates it past readable bounds.

**Required Phase 3 finalisation before Phase 4 starts:**
- Sprint 10b chunk 2 — `BrowserTabHost` extracted.
- Sprint 10b chunk 3 — `TabEventController` extracted.
- Sprint 10b chunk 4 — `CommandPaletteController` extracted (Council's `Open Council Mode` command lives here).
- Sprint 10b chunk 6 — BrowserTab.xaml.cs split (TabState/TabUI/TabEvents).
- Sprint 11 (Voice/Whisper) — optional but desirable if scheduled before Phase 4.
- Sprint 12 (Vision Pack) — optional.

### P2. New `CouncilLayoutController` (Phase 4.0 foundation)

A peer to `SessionPersistenceController` and `KeyboardShortcutsController`, owning the 2×2 layout activation/teardown and panel-state events. Lives in `src/VELO.App/Controllers/`. Required before any Council UI lands so the layout is testable in isolation.

### P3. `BrowserTab` extensions for Council

- Public method `InjectCouncilBridgeAsync(adapterId, sourceUrl)` that calls `AddScriptToExecuteOnDocumentCreatedAsync` with the council-bridge.js bundle.
- Public event `CouncilBridgeMessage` raising the bridge's `postMessage` payloads to the orchestrator.
- Optional `fingerprintLevelOverride` parameter in `Initialize()` (VALIDATE-10 fix).

---

## 4. Phase 4 implementation plan

Reordered from the spec's 5-sprint plan after factoring real prerequisites.

### Phase 4.0 — Foundations (~1.5 weeks)

Must happen before any Council code lands.

- 2×2 layout extension (`MainWindow` + new `CouncilLayoutController`).
- Per-container fingerprint override (VALIDATE-10 fix).
- 4 new built-in containers seeded in `VeloDatabase`.
- Pre-flight check for Ollama qwen3:32b availability.
- One-time disclaimer modal (`CouncilFirstRunDisclaimer`).
- Settings → Council page (UI shell only, toggles disabled until 4.1 lands).

**Output:** VELO ships v2.5.0-pre with the foundation invisible to users (no entry point yet). Goal is to surface real-world bugs in the new layout under normal split-view usage before Council UI lands on top.

### Phase 4.1 — Bridge & basic capture (~2 weeks)

Maps to spec sprints 1 + 2.

- All domain models (`CouncilSession`, `CouncilRound`, `CouncilCapture`, `CaptureBlock` hierarchy, etc.).
- `CouncilOrchestrator` stub + real `PasteIntoPanelAsync`, `CapturePanelAsync`, `AddCaptureToSummaryAsync`.
- `CouncilBridge` (C#) ↔ `council-bridge.js`.
- `adapters.default.json` v0 for Claude, ChatGPT, Grok, Ollama-WebUI fallback.
- Block extractors for `text`, `code`, `table`, `citation` (the easy half).
- Mini-toolbar per panel (native WPF, not injected).
- Council Bar with master prompt textarea.
- Smoke test: end-to-end master → paste → capture text → display.

**Output:** v2.5.1 with Council usable for text-only responses. Artifacts/images/files still out.

### Phase 4.2 — Moderator & synthesis (~1 week)

Maps to spec sprint 3.

- `CouncilTokenEstimator`, `CouncilContextStrategist`.
- `CouncilCompressorService` + prompt.
- `CouncilModeratorService` + prompt + tolerant JSON extractor.
- Synthesis dialog UI.
- Integration test with Ollama qwen3:32b.

**Output:** v2.5.2 with usable synthesis for text-only sessions.

### Phase 4.3 — Advanced blocks (~1.5 weeks)

Maps to spec sprint 4.

- Artifact extractor (Claude + GPT Canvas).
- Image extractor with local cache (settings-gated).
- File link extractor (metadata only).
- `+Artifact`, `+Img(N)`, `+Files(N)` conditional toolbar buttons.
- `UnknownBlock` fallback path.

**Output:** v2.5.3 with full block-type matrix per spec section 13.

### Phase 4.4 — Polish & ship (~1 week)

Maps to spec sprint 5.

- Markdown + JSON export.
- All global hotkeys per spec section 10.3.
- Full settings page wired.
- Health check polished.
- User docs with the spec's support matrix.
- README of VELO updated.
- Acceptance checklist (spec section 16) green.

**Output:** v2.5.0 stable. Phase 4 ships. v0.2 backlog (remote adapters refresh, suspension, multimodal) opens.

---

## 5. Issues to file under Phase 4

Suggested GitHub issue titles. Each one is a self-contained chunk of 1-3 days.

**Phase 4.0:**
- `[Council/4.0] Add 2×2 split-view layout in MainWindow + CouncilLayoutController`
- `[Council/4.0] Per-container fingerprint override on BrowserTab.Initialize`
- `[Council/4.0] Seed council-* containers in VeloDatabase`
- `[Council/4.0] Ollama qwen3:32b pre-flight check + warning UI`
- `[Council/4.0] First-run disclaimer modal + per-provider ack settings`
- `[Council/4.0] Settings → Council page UI shell`

**Phase 4.1:**
- `[Council/4.1] VELO.Core/Council domain models + tests`
- `[Council/4.1] CouncilOrchestrator interface + stub impl`
- `[Council/4.1] council-bridge.js: inspect, pasteIntoInput, captureQuick (text+code+table+citation)`
- `[Council/4.1] adapters.default.json v0 for 4 providers`
- `[Council/4.1] CouncilBridge C# call/receive layer`
- `[Council/4.1] Mini-toolbar per panel`
- `[Council/4.1] Council Bar overlay`
- `[Council/4.1] End-to-end smoke test`

**Phase 4.2:**
- `[Council/4.2] CouncilTokenEstimator + CouncilContextStrategist`
- `[Council/4.2] CouncilCompressorService + prompt`
- `[Council/4.2] CouncilModeratorService + prompt + JSON extractor`
- `[Council/4.2] Synthesis dialog`
- `[Council/4.2] Ollama integration test`

**Phase 4.3:**
- `[Council/4.3] Artifact extractor (Claude + GPT Canvas)`
- `[Council/4.3] Image extractor + local cache (settings-gated)`
- `[Council/4.3] File link extractor`
- `[Council/4.3] Conditional toolbar buttons (+Artifact / +Img / +Files)`

**Phase 4.4:**
- `[Council/4.4] Markdown export`
- `[Council/4.4] JSON export`
- `[Council/4.4] Workspace hotkeys`
- `[Council/4.4] Health check polish`
- `[Council/4.4] User docs + README update`

---

## 6. Risks (additions to the spec)

Beyond the risks listed in spec section 14, the analysis surfaces:

- **Refactor coupling:** Phase 4 depends on Sprint 10b chunks 2/3/4/6 being done first. If Phase 4 starts before those chunks, MainWindow bloat will force a halt mid-Phase 4.
- **Adapter drift cost without remote updates (v0.1):** Council ships with bundled adapters only. Every selector drift on Claude/ChatGPT/Grok requires a VELO release. Realistic cadence: 1-2 releases per quarter to keep adapters fresh. **Decision:** the v0.2 remote refresh path is not really optional once the feature has users; treat as a 4-month follow-up.
- **AI Mode coupling:** Council compressor/moderator reuse the global `ChatDelegate`. If a user has AI Mode = Claude (cloud), the **moderator runs in Claude**, not locally. The spec assumes local always. Council needs either: (a) force AI Mode = Custom (Ollama) while Council is active and warn the user, or (b) a Council-specific AI Mode setting separate from the global. **Decision deferred to Phase 4.0 design.**

---

## 7. Acceptance for Phase 0

This analysis itself counts as the Phase 0 deliverable. It satisfies the spec's section 0 outputs:

1. ✅ List of VALIDATE confirmed or corrected → section 1.
2. ✅ List of OPEN resolved with justification → section 2.
3. ✅ Implementation plan divided into sprints → section 4.
4. ✅ Issues proposed per sprint → section 5.

**Next action:** decision from the maintainer on the Phase 4 start date relative to Phase 3 closure. The plan above is the default proposal.
