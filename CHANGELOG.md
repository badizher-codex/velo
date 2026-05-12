# Changelog

All notable changes to VELO Browser are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions follow [Semantic Versioning](https://semver.org/).

> **Note:** Releases v2.1.0 through v2.4.30 are summarised in
> [`memory/project_phase3_state.md`](memory/project_phase3_state.md) and the
> GitHub release notes; per-release CHANGELOG entries resume from v2.4.31
> (Phase 3 / Sprint 10b chunk 6 — partial-class refactor).

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
