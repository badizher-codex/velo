# UI Modernization — Codebase Analysis (Phase 0 output)

**Status:** Phase 0 (analysis) complete. Pre-implementation.
**Author:** Claude Code session, 2026-05-11
**Input:** 7 prototype screenshots dropped in `docs/ImgPrototipo/Imagen generada 1.png … 7.png`
**Repo state at analysis:** VELO v2.4.30, HEAD `5786c2d`, 355/355 tests green
**Purpose:** Mirror of `docs/Phase4/COUNCIL_MODE_ANALYSIS.md` (Council Mode Phase 0). Inventory the prototype, decide what is *visual polish* vs *structural change*, lock the policy of "preserve current layout and information flow — only the look changes," and produce an implementation plan.

---

## 0. Ground rule from the maintainer

> "Solo es prototipo. Si alguno no coincide con el orden que tenemos, ponle que es por prototipo pero debe quedar todo donde lo tenemos originalmente. Solo hay que ver el diseño."

This is the **single most important constraint**. Phase 5 is a **visual refresh**, not a re-architecture. Every prototype frame is evaluated against this rule. Anywhere the prototype contradicts VELO's current information architecture or workflow, the call is **"keep the current arrangement, apply only the prototype's visual language."**

The visual language being adopted from the prototype:

- Darker base (~`#0A0A12` for window chrome, `#111118` for surfaces).
- Purple accent (~`#7C5CFF` / `#8A6BFF` for primary CTAs, key icons, gradient highlights).
- More generous spacing, larger card padding, rounded corners (~10-12 px).
- Status badges with colored backgrounds (e.g. green "No threats" pill, red threat counts).
- Empty states with single large illustrated icon + caption (Downloads "No downloads yet").
- Gradient buttons for primary actions (Vault "Unlock", Clear data "Clear now").
- Icons feel sharper / more saturated; favicons gain rounded square backgrounds.

---

## 1. Frame-by-frame inventory

Each frame matches a real VELO component. The "preserve" column locks anything the prototype reorganises that would conflict with current behaviour.

### Frame 1 — Main window (`Imagen generada 1.png`)

What the prototype shows:
- Horizontal tab strip across the top (Chrome-style).
- Centered URL bar with shield to the left and inline action icons on the right.
- NewTab page styled like Arc/Brave: pinned shortcut cards in a 4-column grid, large recently-visited row underneath.
- Right-side Agent panel with a "Threat Insight" card preview.
- Bottom status bar with shield indicator, "Local" mode chip, zoom %.

VELO components hit:
- `src/VELO.UI/Controls/TabSidebar.xaml(.cs)` — **vertical** sidebar today.
- `src/VELO.UI/Controls/UrlBar.xaml(.cs)`.
- `src/VELO.UI/Controls/NewTabPage.xaml(.cs)` (existing, gets a re-skin).
- `src/VELO.UI/Controls/AgentPanel.xaml(.cs)`.
- The window chrome — `MainWindow.xaml` grid skeleton.

**Preserve (prototype-only items we are NOT adopting):**
- ⚠️ **Tab paradigm.** Prototype shows tabs horizontally on top. VELO uses a vertical sidebar (`TabSidebar`). **Keep the vertical sidebar.** Apply the prototype's palette/spacing/typography to it but keep tab list vertical on the left. Phase 4 Council Mode also assumes tabs don't move; flipping the paradigm here would invalidate that plan.
- ⚠️ The exact NewTab "4-card grid" layout is taken as inspiration. Existing NewTab has its own shortcut storage; we re-skin those shortcuts visually but don't rebuild storage/data.

**Adopt (visual changes that don't disturb layout):**
- Palette: window background, surface tones, accent purple.
- URL bar: shield icon styling, inline action icon polish, focus-state rounded outline.
- Status bar at the bottom of the main viewport: VELO today has bottom-right shield chips scattered; we consolidate into a clean status row matching the prototype.
- Agent panel: "Threat Insight" card layout — re-skin of the existing `AgentPanelControl` content area.

### Frame 2 — Agent panel expanded with AI tools menu

Prototype shows:
- "Hola, ¿en qué puedo ayudarte?" greeting.
- Vertical list of tool entries (Resumir, Buscar bugs, Optimizar, …) each as a tappable row.

VELO components hit:
- `AgentPanel.xaml(.cs)` — re-skin of the tool list area only.

**Preserve:** Tool set itself (which slash commands / AI actions VELO exposes today is determined by VELO.Agent services — we don't change that catalog).

**Adopt:** Row styling (icon + title + subtitle pattern), spacing, hover state.

### Frame 3 — Password Vault (`Imagen generada 3.png`)

Prototype shows:
- Modal dialog centered on the dimmed main window.
- Large purple shield icon at top.
- Title "Password Vault" + subtitle "Enter your master password to continue."
- Password input with eye toggle on the right.
- Full-width gradient "Unlock" button.
- Footer: green dot + "Local encrypted vault — Your data stays on this device."

VELO components hit:
- `src/VELO.UI/Dialogs/VaultWindow.xaml(.cs)` — direct re-skin candidate.

**Preserve:** Unlock flow, master-password validation, key derivation, all of `VELO.Vault`'s logic.

**Adopt:** Entire visual structure of this dialog. Direct map.

### Frame 4 — History (`Imagen generada 4.png`)

Prototype shows:
- Large "History" title.
- Search bar.
- Card list with favicon + title/URL + visit date + status badge (No threats / Trackers blocked / All checked).
- "500 entries · Loaded 500 from DB" footer.

VELO components hit:
- `src/VELO.UI/Dialogs/HistoryWindow.xaml(.cs)` — **rewritten in v2.4.10**, current shape works but is sparser visually.

**Preserve:** Data binding (we already feed `BlockedCount`/`TrackerCount`/`MalwareCount` per row since v2.4.11). The status badge surfaces data we already have.

**Adopt:** Card layout, favicon bubble, status badges with colored backgrounds, "Loaded N from DB" diagnostic line in the footer (already present, tighten styling).

### Frame 5 — Malwaredex (`Imagen generada 5.png`)

Prototype shows:
- Title "Malwaredex" with counter "6 of 43 captured · 1,375 threats blocked".
- Search + category filter.
- Grid of cards (4 per row): tracker icon, name, category, captured badge with count, three stars below.

VELO components hit:
- `src/VELO.UI/Dialogs/MalwaredexWindow.xaml(.cs)`.

**Preserve:** Capture model (already tracks `ThreatType`, `SubType`, capture date, hit count via `MalwaredexRepository`).

**Adopt:** Grid card visual, captured/not-captured states, category filter row.

⚠️ **"Stars" question (3 stars per card).** The prototype shows 3 stars on each captured card. **VELO doesn't have a star/rating concept today.** Two options:
1. **Treat stars as a severity indicator** (low/medium/high) derived from `ThreatType` (Malware = 3 stars, Tracker = 1 star, Fingerprint = 2, etc.). Cheap.
2. **Drop the stars.** Stars in the prototype may just be visual decoration; if so, do not add a data concept just to fill the slot.

**Decision deferred to implementation.** Recommend option 2 (drop stars) unless option 1's mapping feels natural to the user.

### Frame 6 — Security Inspector (`Imagen generada 6.png`)

Prototype shows:
- Header: green "Safe (Green)" status pill, Score +20, URL, timestamp, "Overall protection: Safe."
- Three columns of sections:
  - TLS / Certificate (with confidence bar)
  - Blocks in This Session (counts)
  - Artificial Intelligence Analysis
  - Fingerprint Protection
  - WebRTC Connections
  - Privacy Receipt
- Bottom action row: "Open native DevTools", "Export JSON", "Re-scan".

VELO components hit:
- `src/VELO.UI/Dialogs/SecurityInspectorWindow.xaml(.cs)`.

**Preserve:** The information VELO collects today is what the prototype shows. Same columns, same data — re-skin.

**Adopt:** Pill header, confidence bars, denser two-column section grid, bottom button row styling.

### Frame 7 — Clear Data + Downloads (`Imagen generada 7.png`)

Prototype shows:
- **Clear Data**: trash icon header, "Select what data you want to delete from this device", checklist with descriptions per item, red "Clear now" primary button.
- **Downloads**: title, search bar, large empty-state icon + "No downloads yet" caption, "Recent activity" subsection with an example download row, "0 files · Clear completed" footer.

VELO components hit:
- `src/VELO.UI/Dialogs/ClearDataWindow.xaml(.cs)`.
- `src/VELO.UI/Dialogs/DownloadsWindow.xaml(.cs)`.

**Preserve:** Both dialogs' existing behaviour and data flows.

**Adopt:** Trash icon header for Clear Data, checklist with description rows (today's checkboxes are bare). Empty-state illustration for Downloads. Red destructive button styling.

---

## 2. What is *not* in the prototype but matters

The prototype is incomplete by design — it's seven frames out of ~20+ VELO dialogs. Phase 5 must also re-skin (or explicitly not re-skin) these:

| Component | Decision |
|---|---|
| `SettingsWindow.xaml` (7 nav panels) | **In scope** — same visual language, but no prototype frame. Style per the palette/spacing rules from frames 3-7. |
| `BookmarksWindow.xaml` | **In scope** — apply History-like card list pattern from frame 4. |
| `AgentPanel.xaml` chat history | **In scope** — frame 2 covers tools menu; chat scrollback needs the same dark-card treatment. |
| `OnboardingWizard.xaml` | **In scope** — first-impression matters, apply Vault-dialog (frame 3) modal pattern. |
| `AIResultWindow.xaml` | **In scope** — the IA output panel, same card pattern as History/Malwaredex. |
| `AutofillToast.xaml`, `BlockNarrationToast.xaml` | **In scope** — already toast-shaped, just palette tweak. |
| `MainWindow.xaml` chrome (title bar, splitter chrome) | **In scope** — palette and spacing. |
| Context menu items, command palette result rows | **In scope** — palette + hover-state polish. |
| Status indicators in URL bar (zoom %, reader-mode badge, TL;DR badge, ★ bookmark) | **In scope** — polish, no functional change. |

---

## 3. Tab paradigm — the one piece we are explicitly NOT changing

The prototype shows **horizontal tabs on top**. VELO uses a **vertical sidebar** (`TabSidebar` control). This document locks the decision: **VERTICAL SIDEBAR STAYS.**

Reasons:

1. Maintainer directive: "solo hay que ver el diseño, dejar todo en su orden actual."
2. Phase 4 Council Mode's Phase 4.0 plan extends the existing split-view to 2×2 within the current `BrowserContent` grid. A horizontal tab strip on top would alter where the panel rows live and break Phase 4's plan.
3. Vertical tab sidebars are a *positive* differentiator for VELO (Arc-like — power users prefer it). Removing it for visual consistency with a Chrome-shaped prototype is a regression.
4. The vertical sidebar is the home of the per-tab status (loading spinner, security shield color, etc.) — a horizontal strip cramps that.

**What we adopt from the prototype's "tabs" area:** the palette and spacing of the tab row. The sidebar gets darker background, sharper hover, accent color when active, more breathing room. Same control, prettier.

---

## 4. Implementation plan

Phase 5 splits into 3 sub-phases. Each ends with a real release; nothing waits to ship.

### Phase 5.0 — Theme foundation + simple dialogs (~1 week)

Goal: establish the new visual language and prove it on the cheapest-to-touch surfaces.

- New theme resources in `src/VELO.UI/Themes/DarkTheme.xaml` (extend, don't replace; keep backwards compat for any control that didn't get migrated yet).
  - New colors: `BackgroundDarkestBrush`, `AccentPurpleBrush`, `AccentPurpleGradient`, `BadgeGreenBrush`, `BadgeRedBrush`, `BadgeBlueBrush`.
  - New control styles: gradient primary button, ghost button, status pill, card.
- Re-skin **3 simple dialogs** that directly map prototype frames:
  - `VaultWindow.xaml` (frame 3).
  - `ClearDataWindow.xaml` (frame 7 left).
  - `DownloadsWindow.xaml` (frame 7 right).
- Smoke check: visual QA in 8 locales, smoke-test XAML resources still green.

Release: v2.5.0-pre1 (or stay in v2.4.x with `.31`/`.32` if we prefer not to bump minor until 5.2 ships).

### Phase 5.1 — Information-dense dialogs (~1 week)

- Re-skin `HistoryWindow.xaml` (frame 4 — card list with status badges).
- Re-skin `MalwaredexWindow.xaml` (frame 5 — card grid; stars decision per section 1).
- Re-skin `SecurityInspectorWindow.xaml` (frame 6 — pill header + columns).
- Re-skin `BookmarksWindow.xaml` (follow History pattern from frame 4 even though no prototype frame exists).
- Re-skin `SettingsWindow.xaml` (palette + spacing only, no layout changes).

Release: v2.5.0-pre2.

### Phase 5.2 — MainWindow surfaces (~1.5-2 weeks)

The biggest sub-phase. Touches the chrome the user sees most.

- `NewTabPage.xaml` — pinned shortcuts in card grid (frame 1, NewTab area).
- `UrlBar.xaml` — frame 1 URL row: shield icon, inline action icons, focus state.
- `TabSidebar.xaml` — palette + spacing only. **Vertical layout preserved.**
- `AgentPanel.xaml` — Threat Insight card + tools list (frames 1 and 2).
- Status bar at the bottom of the viewport — consolidate the existing scattered indicators into one clean row (frame 1 bottom).
- `MainWindow.xaml` chrome — title bar, splitter, panel borders.
- Misc: `AutofillToast`, `BlockNarrationToast`, `AIResultWindow`, `OnboardingWizard`, context menus, command palette rows.

Release: **v2.5.0** stable. Phase 5 ships.

---

## 5. Issues to file under Phase 5

**Phase 5.0:**
- `[UI/5.0] Theme resources — palette + accent + new control styles`
- `[UI/5.0] Re-skin VaultWindow per frame 3`
- `[UI/5.0] Re-skin ClearDataWindow per frame 7 left`
- `[UI/5.0] Re-skin DownloadsWindow per frame 7 right`
- `[UI/5.0] Visual QA pass in 8 locales`

**Phase 5.1:**
- `[UI/5.1] Re-skin HistoryWindow per frame 4`
- `[UI/5.1] Re-skin MalwaredexWindow per frame 5`
- `[UI/5.1] Re-skin SecurityInspectorWindow per frame 6`
- `[UI/5.1] Re-skin BookmarksWindow (no prototype, follow History pattern)`
- `[UI/5.1] Re-skin SettingsWindow (palette + spacing only, layout preserved)`
- `[UI/5.1] Decision: Malwaredex stars (drop vs derive from ThreatType)`

**Phase 5.2:**
- `[UI/5.2] NewTabPage card grid re-skin`
- `[UI/5.2] UrlBar visual polish per frame 1`
- `[UI/5.2] TabSidebar palette + spacing (VERTICAL LAYOUT PRESERVED)`
- `[UI/5.2] AgentPanel Threat Insight card + tools list per frames 1+2`
- `[UI/5.2] Bottom status bar consolidation`
- `[UI/5.2] MainWindow chrome polish`
- `[UI/5.2] AIResultWindow, OnboardingWizard, toasts, context menu, command palette polish`
- `[UI/5.2] Final visual QA + screenshot regen for landing`

---

## 6. Risks

- **Hardcoded colors in existing XAML.** A lot of VELO's `.xaml` files inline colors instead of binding to theme resources. Phase 5.0 needs to migrate everything to theme bindings before 5.1/5.2 land, or the new palette won't propagate. **Audit pass first.**
- **Localisation.** Most prototype text is English/Spanish prose that's already localised. No new strings expected unless we reword for the new visual hierarchy. Re-screenshot the landing's 6 hero shots after Phase 5.2 ships (landing currently shows v2.4.x visuals).
- **Coupling with Phase 4 Council Mode.** Both phases touch `MainWindow.xaml`. If Phase 4 starts before Phase 5.2, the 2×2 layout will be done in the *old* palette and then re-skinned in 5.2. If Phase 5.2 starts first, the new palette flows naturally into Council's panels. **Recommend Phase 5 before Phase 4** for this reason.
- **Smoke test #1 (`Every_StaticResource_Reference_Has_A_Definition_Somewhere`)** already catches missing theme resources. Phase 5 leans on it heavily.
- **No UI tests.** Every dialog needs a manual visual QA pass after re-skin. Phase 5.0 should establish a "visual checklist" doc that 5.1/5.2 reuse.

---

## 7. Roadmap impact

Phase 5 lands between Phase 3 closure (Sprint 10b chunks 3+6, Sprint 11, Sprint 12) and Phase 4 Council Mode. New order:

```
Phase 3 (closing)
  Sprint 10b chunk 6 — BrowserTab.xaml.cs split
  Sprint 10b chunk 3 — TabEventController (post chunk 6)
  Sprint 11 — Voice/Whisper          ← optional pre-Phase 5
  Sprint 12 — Vision Pack            ← optional pre-Phase 5

Phase 5 — UI Modernization (~3-5 weeks, NEW)
  5.0 — theme + simple dialogs
  5.1 — info-dense dialogs
  5.2 — MainWindow surfaces

Phase 4 — Council Mode (~7 weeks, analysed)

Phase 6 — Bitwarden sync   (was Phase 5 in older roadmap, re-numbered)
```

**Rationale for putting Phase 5 *before* Phase 4:**

1. Council's panel UI gets the new palette out of the box.
2. The 2×2 layout work in Phase 4.0 doesn't need to be re-skinned later.
3. Phase 5 doesn't touch MainWindow structurally, so it doesn't block on Sprint 10b chunk 6 the way Phase 4 does.

Alternative order: **Phase 5 in parallel with Sprint 11/12** — sprints 11 and 12 touch the Agent panel and add the Voice/Vision capabilities, which need the new palette anyway. Doable if maintainer has bandwidth.

---

## 8. Acceptance for Phase 0

This document covers the spec inputs for Phase 5:

1. ✅ Frame-by-frame inventory mapping prototype → VELO components → preserve/adopt decisions (section 1).
2. ✅ Tab paradigm decision locked: vertical sidebar stays (section 3).
3. ✅ Implementation plan in 3 sub-phases (section 4).
4. ✅ Issues proposed per sub-phase (section 5).
5. ✅ Risk surface mapped (section 6).
6. ✅ Roadmap placement justified (section 7).

**Next action:** maintainer chooses Phase 5 ordering relative to Phase 3 closure, Phase 4, and Phase 6. The proposal in section 7 is the default.
