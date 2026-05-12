# Changelog

All notable changes to VELO Browser are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions follow [Semantic Versioning](https://semver.org/).

> **Note:** Releases v2.1.0 through v2.4.30 are summarised in
> [`memory/project_phase3_state.md`](memory/project_phase3_state.md) and the
> GitHub release notes; per-release CHANGELOG entries resume from v2.4.31
> (Phase 3 / Sprint 10b chunk 6 — partial-class refactor).

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
