# Sprint 12 — AI Vision Pack (Spec)

**Status:** Spec (pre-implementation). Sprint 12 is **optional** for Phase 3 closure. Can ship anytime after Sprint 11 or in parallel; not a blocker for Phase 4 or Phase 5.
**Author:** Claude Code session, 2026-05-12.
**Repo state at analysis:** VELO v2.4.31, HEAD `eeb3d01`, 355/355 tests green.
**Purpose:** Concrete, code-aware plan for AI Vision Pack so it can be picked up cold and shipped without re-deriving architecture each time it surfaces in roadmap discussions.

---

## 0. Executive summary

Sprint 12 ships three image-related sub-features. Unlike Sprint 11 (which is mostly greenfield), Sprint 12 has partial infrastructure already: vision-capable adapters (Claude via Anthropic SDK, Ollama via user-installed multimodal models), an image context menu, and a `RequestImageAnalysis` event that fires but has **no handler today** — that gap is itself the headline finding.

| # | Feature | Effort | Installer impact | Independent? |
|---|---|---|---|---|
| 12.1 | CLIP reverse image search (right-click → similar images via DDG + semantic ranking) | 3–5 days (Claude path) / 5–8 days (local CLIP) | 0 MB (Claude) or +500 MB (local CLIP+ONNX) | Yes |
| 12.2 | Image UI polish (hover preview, loading placeholder, save-image flow) | 2–3 days | None | Yes |
| 12.3 | Anti-AI-slop detection (right-click → "Is this AI-generated?") | 4–6 days | None (Claude) or +500 MB (local CLIP) | Partially — shares CLIP with #12.1 |

**Total realistic effort:** 9–14 days (~1.5–2 working weeks). Most variance comes from the Claude-vs-local-CLIP decision.

**Prerequisites (verified against current code):**

- ✅ `AIContextMenuBuilder` already routes image right-clicks: `BuildImageMenu` in `src/VELO.UI/Controls/ContextMenuBuilder.cs:82-90` emits "Abrir imagen en nueva pestaña", "Copiar URL de imagen", and "🔒 Analizar imagen (local)" → fires `RequestImageAnalysis` event.
- ❌ `RequestImageAnalysis` event (declared at `ContextMenuBuilder.cs:187`) has **no subscriber** in MainWindow or BrowserTab. Wiring it up is the first move.
- ✅ `AIContextActions.DescribeImageAsync(byte[] imageBytes, …)` already exists in `src/VELO.Agent/AIContextActions.cs:265` and works through `ClaudeAdapter` (vision-capable) or any Ollama multimodal model the user has installed.
- ✅ `AIContextActions.OcrAsync(byte[] imageBytes, …)` exists at the same file. Same vision-adapter dependency.
- ✅ `AIContextActions.SupportsVision` boolean gates these features when no vision adapter is available.
- ❌ No CLIP runtime today (no ONNX Runtime, no CLIP.NET, no Hugging Face SDK).
- ❌ No reverse image search (DDG Images integration not wired).
- ❌ No image classification service for AI-slop detection.

**Recommendation:** ship the **Claude-only variant first** as v0.1 of Sprint 12 (Sprint 12.1.A in this spec). Local CLIP becomes a v0.2 follow-up if/when the maintainer commits to ~500 MB installer growth. The Claude variant has zero installer impact, leverages existing infrastructure, and ships in 3–5 days end-to-end including #12.1 + #12.3.

---

## 1. Repo state at analysis

### What exists today (verified)

**Image context menu:**
- `src/VELO.UI/Controls/ContextMenuBuilder.cs:82-90` — `BuildImageMenu` emits the menu visible on image right-click.
- `src/VELO.UI/Controls/ContextMenuBuilder.cs:187` — `event Action<string>? RequestImageAnalysis` declared. **No `+=` subscriber anywhere in `src/VELO.App/` or `src/VELO.UI/` outside the declaration itself.**
- `src/VELO.UI/Controls/AIContextMenuBuilder.cs:108-117` — image-action flow inside the AI submenu. Wraps `ContextMenuBuilder.BuildImageMenu`. Adds AI-specific items like "🤖 Describir imagen" gated on `SupportsVision`.

**Vision adapter capabilities:**
- `src/VELO.Security/AI/Adapters/ClaudeAdapter.cs` — uses `Anthropic.SDK`. Claude 3.5 Sonnet is multimodal. Image bytes are sent as base64-encoded `image` content blocks.
- `src/VELO.Security/AI/Adapters/OllamaAdapter.cs` — generic Ollama HTTP. Multimodal only if user has installed `llava:7b`, `moondream`, or similar via `ollama pull`. Adapter exposes vision through the same `/api/chat` endpoint with `images` array.
- `src/VELO.Agent/Adapters/LLamaSharpAdapter.cs` — GGUF inference via llama.cpp. **No vision support** in current build (loaded models like Mistral-7B, Llama-3.2-3B, Phi-3.5-mini are text-only).

**Image action plumbing:**
- `src/VELO.Agent/AIContextActions.cs:265` — `DescribeImageAsync(byte[] imageBytes, CancellationToken ct)`. Checks `SupportsVision`; if false, returns error string. If true, sends base64 + prompt "Describe esta imagen en 2-3 oraciones…".
- Same file: `OcrAsync` for "📖 OCR — extraer texto" referenced in Phase 3 doc but **not wired in the menu today** (the gate condition is `SupportsVision && currentAdapter.HasOcrCapability` — neither adapter sets `HasOcrCapability` to true at runtime).
- `BrowserTab.xaml.cs` (now `BrowserTab.Events.cs:OnContextMenuRequested`) extracts `target.SourceUri` into `ContextMenuContext.ImageUrl`. Bytes are downloaded **on demand** when an image action fires — never preemptively.

### What does NOT exist (gaps to build)

| Gap | Verified by | Effort impact |
|---|---|---|
| `RequestImageAnalysis` subscriber | grep across `src/` for `RequestImageAnalysis +=` → 0 hits | New handler in MainWindow downloads image bytes + invokes `AIContextActions.DescribeImageAsync` |
| Reverse image search via DDG | grep `DDG\|duckduckgo.*image\|ReverseImage` → 0 hits | New service: download image, POST to DDG reverse endpoint or open DDG Images URL with embedded base64 |
| CLIP runtime | grep `CLIP\|Hugging|ONNX` → 0 hits | If pursuing local path: add `Microsoft.ML.OnnxRuntime` 1.20.x + CLIP ViT-B/32 weights (~150 MB ONNX + ~150 MB tokenizer) |
| Image classifier service | grep `ImageClassifier\|ContentFilter\|AISlop` → 0 hits | New `ImageClassificationService` |
| Image hover preview | grep `hover.*preview\|GlanceImage` → 0 hits | New visual element in BrowserTab or context menu builder |
| Loading placeholder for image AI actions | grep `image.*placeholder\|image.*loading` → 0 hits | XAML-level addition to `AIResultWindow` |

### Reference architecture (reuse-friendly)

- `src/VELO.Security/Guards/SmartBlockClassifier.cs` — `Classify(host, resourceType, …)` returns `BLOCK | TRACKER | SAFE`. Pattern for a similar `ClassifyImage(bytes, ctx)` returning `AI_SLOP | NATURAL | UNKNOWN` with confidence.
- `src/VELO.Agent/Adapters/IAgentAdapter.cs` — adapter abstraction. `AIContextActions` is the LLM-facing facade. Sprint 12 should extend `AIContextActions` with `IsAIGeneratedAsync(byte[] imageBytes)` rather than introduce a parallel service hierarchy.

---

## 2. Sub-feature breakdown

### 2.1 CLIP reverse image search

**Goal:** user right-clicks an image → "🔎 Buscar imágenes similares" → VELO downloads the image, performs reverse search (and optionally semantically ranks the candidates), opens results in a new tab.

**Two implementation paths:**

#### Path A — Claude vision API (recommended for v0.1)

No CLIP runtime. Vision = "send image + 'describe this image' to Claude → receive 2-3 sentence description → DDG Images search for the description". Reverse search by semantic description, not by visual embedding.

- Pros: 0 installer impact, leverages already-shipped Claude vision, ~3 days from kickoff to merge.
- Cons: requires API key + cloud round-trip (privacy concern, ~1-2 second latency); Ollama users with multimodal models get a local equivalent.
- Quality: Claude 3.5 Sonnet's descriptions are good enough for descriptive reverse search ("brown dog on grass" → DDG returns similar images of brown dogs on grass).

#### Path B — Local CLIP via ONNX Runtime (v0.2 follow-up)

Embed the image locally with CLIP ViT-B/32, embed candidate result thumbnails the same way, sort by cosine similarity.

- Pros: fully local, faster after model load, no API key.
- Cons: +~500 MB installer (ONNX Runtime ~50 MB, CLIP weights ~150 MB image + ~150 MB text encoder + tokenizer ~150 MB), 1–2 s embedding latency cold on CPU, complex build (ONNX native binaries per platform).
- Quality: superior to description-based search for "visually similar" intent.

**Spec defaults to Path A for Sprint 12 v0.1.** Spec acknowledges Path B as the right long-term answer but defers it.

**Architecture (Path A):**

`src/VELO.Agent/Vision/ReverseImageSearchService.cs`:

```csharp
public sealed class ReverseImageSearchService
{
    private readonly AIContextActions _ai;
    private readonly ILogger          _log;

    public ReverseImageSearchService(AIContextActions ai, ILogger<ReverseImageSearchService> log) { ... }

    /// <returns>A DDG Images search URL ready to navigate to.</returns>
    public async Task<string?> SearchByImageAsync(byte[] imageBytes, CancellationToken ct);
}
```

Implementation:

1. `await _ai.DescribeImageAsync(imageBytes, ct)` — returns a short description.
2. URL-encode the description, build `https://duckduckgo.com/?q={description}&iax=images&ia=images`.
3. Return the URL; caller (MainWindow) navigates a new tab to it.

**Subscriber wiring:**

In `MainWindow.xaml.cs`, in the `Initialize` block:

```csharp
_aiContextMenuBuilder.RequestImageAnalysis += async (imageUrl) =>
{
    if (string.IsNullOrEmpty(imageUrl)) return;
    var bytes = await DownloadImageBytesAsync(imageUrl);
    if (bytes == null) return;
    var searchUrl = await _reverseSearch.SearchByImageAsync(bytes, CancellationToken.None);
    if (searchUrl != null)
        await Dispatcher.InvokeAsync(() => _tabManager.OpenInNewTab(searchUrl));
};
```

The `DownloadImageBytesAsync` helper already has a partial implementation pattern in `AIContextActions` (used by `DescribeImageAsync`). Extract it to a small `ImageDownloader` static if not already shared.

**Context menu changes:**

- `BuildImageMenu` adds a new entry "🔎 Buscar imágenes similares" (or German/English variants per locale). Fires `RequestImageAnalysis` event with the image URL.
- Existing "🔒 Analizar imagen (local)" entry remains; it'll get a real handler too (calls `DescribeImageAsync` directly + opens `AIResultWindow` with the description). This is a separate fix — but it's the natural place to wire it now since Sprint 12 is the first time anyone touches this code path.

**Tests (mandatory):**

1. `ReverseImageSearchService_validBytes_returnsDDGUrl`
2. `ReverseImageSearchService_emptyBytes_returnsNull`
3. `ReverseImageSearchService_descriptionFailed_returnsNull_logsWarning`
4. `MainWindow_RequestImageAnalysis_invokesReverseSearch_andOpensNewTab` (integration)
5. `BuildImageMenu_includesSearchSimilarEntry_inMenuFromAIBuilder`

**Risks:**

- **5 MB image cap** — already enforced in Phase 3 doc (line 430). Reaffirm: download caps at 5 MB; larger images skip with a toast.
- **DDG Images URL stability** — DDG's image search URL parameters could change. Mitigation: a config-driven URL template (`SettingKeys.ReverseImageSearchUrl`) defaulting to the current pattern; user-overridable.
- **Path A doesn't surface visual-only matches** — purely description-driven. Acceptable for v0.1; document for v0.2 upgrade trigger.

---

### 2.2 Image UI polish

**Goal:** soften the image experience across VELO. Three quick wins.

**12.2.A — Image hover preview**

When the user hovers over an image link (`<a href="…image.jpg">`), show a small preview popup with the image. Differs from `Glance` (which previews link pages) — this previews image URLs specifically.

- Reuse `glance-hover.js` script-injection infrastructure (`src/VELO.UI/Controls/BrowserTab.xaml.cs:264` injects `glance-hover.js` on doc-created).
- Extend the JS to detect `<img>` and `<a>` linking to image MIME types; send `glance-image-show` web message with URL.
- BrowserTab.Events.cs `OnWebMessageReceived` switch gains a `glance-image-show` case that surfaces a new `ImagePreviewPopup` WPF control near the cursor.

**12.2.B — Loading placeholder for image AI actions**

When the user clicks "Analizar imagen" / "🔎 Buscar imágenes similares", the action is async (download + LLM call = 2–8 s). Show a skeleton overlay in `AIResultWindow` instead of an empty window.

- `AIResultWindow` already used elsewhere (`src/VELO.UI/Dialogs/AIResultWindow.xaml`).
- Add a constructor variant `AIResultWindow(string title, Task<string> contentTask)` that displays a loading skeleton (shimmer or spinner) until the task completes, then renders the result.
- Existing call sites stay unchanged; the new variant is opt-in.

**12.2.C — Save-image-as menu entry**

`BuildImageMenu` today emits "Abrir imagen en nueva pestaña" + "Copiar URL". Missing: "Guardar imagen…" / "Save image as…".

- Add `RequestSaveImage` event to `ContextMenuBuilder` mirroring `RequestImageAnalysis`.
- Handler in MainWindow downloads bytes, shows native `SaveFileDialog` with suggested filename derived from URL last segment + Content-Disposition heuristics, writes to disk.

**Tests (mandatory):**

1. `ImagePreview_glanceImageShowMessage_displaysPopup`
2. `AIResultWindow_taskVariant_showsSkeletonUntilCompleted`
3. `SaveImage_validUrl_writesToDisk_andUpdatesDownloads` (integration; tests against a local HTTP fixture)
4. `SaveImage_dataUriEmbeddedImage_writesToDisk` (data: URIs are common; spec covers them)

**Risks:**

- **Glance image preview false-positives** — some images are 1×1 tracking pixels. Filter URLs by size hint from response Content-Length or skip if dimensions < 100×100 once loaded.
- **Cross-origin image downloads** — some images require referrer or cookies. Use `WebView.CoreWebView2.CookieManager` + image's host as referrer when downloading.

---

### 2.3 Anti-AI-slop detection

**Goal:** user right-clicks an image → "🤖 ¿Es generada por IA?" → VELO assesses whether the image is AI-generated and shows verdict + confidence.

**Two implementation paths (mirroring #12.1):**

#### Path A — Claude vision API (recommended for v0.1)

Send the image to Claude with the prompt "Is this image likely AI-generated? Look for hands with wrong fingers, melted backgrounds, asymmetric eyes, plastic skin, illegible text. Reply with one line: 'AI_GENERATED' or 'NATURAL' followed by a 0–100 confidence percentage and a one-sentence reason." Parse the response.

- Pros: 0 installer impact, leverages Claude vision, ~2 days end-to-end.
- Cons: API call per check (privacy concern), description-based (no embedding comparison).
- Quality: Claude is decent at this — not perfect, but well above random for obvious cases (SD memes, AI art).

#### Path B — Local CLIP + reference embedding set (v0.2 follow-up)

- Compute CLIP embedding of the input image.
- Compare against pre-computed embeddings from a small reference set of known AI-generated images (Stable Diffusion outputs, MidJourney samples, etc.) — embeddings shipped as `resources/ai_slop_refs.bin`.
- Score = max cosine similarity across reference set. Threshold = 0.85.

Pros / cons: same as #12.1 Path B (privacy, installer growth, complexity).

**Spec defaults to Path A for Sprint 12 v0.1.**

**Architecture (Path A):**

`src/VELO.Agent/Vision/AIImageClassifier.cs`:

```csharp
public sealed class AIImageClassifier
{
    private readonly AIContextActions _ai;
    private readonly ILogger          _log;

    public AIImageClassifier(AIContextActions ai, ILogger<AIImageClassifier> log) { ... }

    public Task<AIImageVerdict> ClassifyAsync(byte[] imageBytes, CancellationToken ct);
}

public sealed record AIImageVerdict(bool IsAIGenerated, int Confidence, string Reason);
```

Implementation:
- Build prompt with image + classification instruction.
- Call `_ai.ClassifyImageAsync(bytes, prompt, ct)` — new method on `AIContextActions` modeled after `DescribeImageAsync`.
- Parse response with simple regex / startswith.

**Context menu changes:**

- `BuildImageMenu` adds "🤖 ¿Es generada por IA?" entry. Fires new event `RequestIsAIGenerated` carrying the image URL.
- Handler in MainWindow downloads bytes, calls classifier, opens `AIResultWindow` with verdict + reason + "Reportar como falsa detección" button (no-op v0.1; logs to telemetry-disabled local file).

**Settings impact:**

- "Mostrar entrada 'Es generada por IA?' en menú contextual de imágenes" toggle (default ON).
- "Confianza mínima para marcar como IA-generada" slider (default 70%; v0.2 only — v0.1 just shows raw verdict).

**Tests (mandatory):**

1. `AIImageClassifier_validBytes_returnsVerdict`
2. `AIImageClassifier_emptyBytes_returnsUnknownVerdict`
3. `AIImageClassifier_adapterFailed_returnsUnknownVerdict_logsError`
4. `BuildImageMenu_includesIsAIGeneratedEntry_inAIBuilder`
5. `MainWindow_RequestIsAIGenerated_invokesClassifier_andShowsVerdict`

**Risks:**

- **Confidence calibration** — Claude's "confidence percentage" is the model's own opinion, not a calibrated probability. Don't surface as "scientific". UX copy: "Probably AI-generated" / "Likely natural" / "Unsure". Not a percentage in the UI unless the user opens advanced details.
- **Adversarial images** — sophisticated AI images (recent SDXL, Flux, etc.) can fool Claude. Document as a known limitation; v0.2 (local CLIP) won't fully solve it either.
- **Privacy on user images** — sending personal images to Claude is a real concern. Default to OFF if `SettingKeys.AIMode == "Custom (Ollama local)"` and the local Ollama model has vision; otherwise show a one-time disclaimer.

---

## 3. Dependencies & installer impact

### Path A (recommended v0.1)

| Item | Source | Size |
|---|---|---|
| `Anthropic.SDK` | Already in `VELO.Security.csproj` | 0 (already shipped) |
| Image-related code (services, classifiers) | This sprint | <100 KB |
| **TOTAL bundled** | | **0 MB net** |

### Path B (v0.2 upgrade, NOT this sprint)

| Item | Source | Size |
|---|---|---|
| `Microsoft.ML.OnnxRuntime` NuGet | nuget.org | ~50 MB |
| CLIP ViT-B/32 image encoder (ONNX) | huggingface.co (openai/clip-vit-base-patch32) | ~150 MB |
| CLIP ViT-B/32 text encoder (ONNX) | same | ~150 MB |
| CLIP tokenizer files | same | ~10 MB |
| Reference embeddings for anti-slop (precomputed) | this repo, computed once | ~5 MB |
| **TOTAL bundled** | | **~365 MB** |

Path B is significantly larger than Sprint 11's voice models (82 MB combined). For installer hygiene, Path B should default to download-on-demand even when the maintainer commits to it.

---

## 4. Acceptance criteria

### Sprint close (Path A v0.1)

- [ ] Right-clicking any image and choosing "🔎 Buscar imágenes similares" opens a new tab with DDG Images results for the AI-generated description.
- [ ] Right-clicking any image and choosing "🤖 ¿Es generada por IA?" opens `AIResultWindow` with verdict + reason.
- [ ] Hover preview shows for image links and `<img>` elements (12.2.A).
- [ ] `AIResultWindow` shows a skeleton while waiting for the AI response (12.2.B).
- [ ] "Save image as…" context menu entry works for all image types (12.2.C).
- [ ] Existing "🔒 Analizar imagen (local)" entry now actually opens results (was orphan event before this sprint).
- [ ] No regression in 355/355 tests.
- [ ] New tests (5 + 4 + 5 = 14 minimum) green.
- [ ] CHANGELOG.md updated with `## [2.6.0] — Sprint 12 — AI Vision Pack` entry.
- [ ] Installer size delta: 0 MB (Path A only).

### Settings additions

- [ ] "Ofrecer 'Es generada por IA?' en menú contextual" toggle (default ON).
- [ ] "Ofrecer 'Buscar imágenes similares'" toggle (default ON).
- [ ] "Enviar imágenes a Claude para análisis" toggle (default ON, with disclaimer dialog on first enable).

---

## 5. GitHub issues proposed

| # | Title | Sub-feature |
|---|---|---|
| 12.0.1 | Wire `RequestImageAnalysis` subscriber for existing "🔒 Analizar imagen" menu item (housekeeping) | 12.1 dep |
| 12.1.1 | Create `ReverseImageSearchService` (Path A — Claude description → DDG Images URL) | 12.1 |
| 12.1.2 | Add "🔎 Buscar imágenes similares" entry to `BuildImageMenu` | 12.1 |
| 12.1.3 | Wire `RequestReverseImageSearch` handler in MainWindow | 12.1 |
| 12.1.4 | `ImageDownloader` helper (5 MB cap, follow CSP/referrer rules) | 12.1 + 12.3 |
| 12.2.1 | `ImagePreviewPopup` WPF control + glance-hover.js extension for `<img>` | 12.2 |
| 12.2.2 | `AIResultWindow(string title, Task<string> task)` overload with skeleton | 12.2 |
| 12.2.3 | "Guardar imagen…" entry + handler (SaveFileDialog + Content-Disposition heuristics) | 12.2 |
| 12.3.1 | Add `AIContextActions.ClassifyImageAsync` for "is AI-generated?" prompt | 12.3 |
| 12.3.2 | Create `AIImageClassifier` service | 12.3 |
| 12.3.3 | Add "🤖 ¿Es generada por IA?" entry to `BuildImageMenu` | 12.3 |
| 12.3.4 | Wire handler in MainWindow + show verdict in `AIResultWindow` | 12.3 |
| 12.3.5 | Settings → image AI toggles + privacy disclaimer | 12.3 |

**Total: 13 issues.** Half-day each on average. ~1.5 weeks total.

---

## 6. Risks & open questions

| Risk / question | Mitigation / decision |
|---|---|
| Orphan `RequestImageAnalysis` event (Lesson #11) | Wire it now in 12.0.1 even if it's housekeeping; smoke test catches future orphans. |
| Claude API key requirement | Settings → AI Mode = Claude already mandates an API key. If the user is on local-only mode, surface a friendly "this feature needs vision; enable Claude or install a vision model in Ollama" message. |
| Privacy: sending user images to Anthropic | One-time disclaimer dialog on first use of any vision feature with Claude. Settings → "Always send images to Claude" toggle. |
| DDG URL parameter stability | Config-driven URL template. |
| CLIP-via-Ollama for fully-local? | Some Ollama vision models (llava, moondream) can describe and classify but don't expose CLIP embeddings. Treat them as functionally equivalent to Claude for Path A. |
| Should anti-slop have a "warn me" mode? | v0.1: user-initiated only (right-click). Auto-warn (e.g. badge on AI images) deferred to v0.2. |
| Vision-capability detection accuracy | `SupportsVision` is a static bool on AIContextActions. Today set manually based on chosen adapter. Future: derive from adapter probe on first use. |

### Open questions for the maintainer at kickoff

- **OQ-1:** Path A (Claude) vs Path B (local CLIP) for Sprint 12 v0.1? Spec defaults to A. Maintainer decides.
- **OQ-2:** Should the anti-slop verdict show as binary "AI / Natural" or include the model's reason text? Spec defaults to both with the reason in a collapsible.
- **OQ-3:** Save-image: VELO downloads dir default location vs. last-used location? Spec defaults to VELO's existing Downloads location to match the rest of the app.

---

## 7. Sequencing within the sprint

```
Day 1   :  #12.0.1 + #12.1.4   Housekeeping + ImageDownloader (foundation)
Day 2-3 :  #12.1.1, #12.1.2, #12.1.3   Reverse image search end-to-end
Day 4-5 :  #12.3.1, #12.3.2, #12.3.3, #12.3.4   Anti-AI-slop detection
Day 6-7 :  #12.2.1, #12.2.2, #12.2.3   UI polish trio
Day 8   :  #12.3.5 settings + tests + smoke + release prep
```

**Release strategy:**

- Option A: ship #12.1 as v2.6.0, #12.2 + #12.3 as v2.6.1 a week later. Smaller blast radius.
- Option B: ship all of Sprint 12 as v2.6.0. Simpler changelog.

Spec recommends **Option B** because the sub-features share infrastructure (ImageDownloader, `AIResultWindow` skeleton) and they're all small.

---

## 8. Phase 4 / Phase 5 alignment

- **Phase 4 (Council Mode):** Sprint 12 is not a prerequisite. Council Mode's panels can reuse `ImageDownloader` + `AIResultWindow` for ad-hoc image discussions if it ships first; otherwise Phase 4 just doesn't surface image-specific Council features. No coupling.
- **Phase 5 (UI Modernization):** image hover preview, AIResultWindow skeleton, and the save-image dialog all use existing WPF controls that inherit Phase 5's theme tokens. Ship Sprint 12 after Phase 5.0 (theme tokens) so visual polish is in the new palette. Critical Path 5.1 (info-dense dialogs) re-skins `AIResultWindow` — order: Phase 5.0 → Phase 5.1 → Sprint 12 to avoid double-touching `AIResultWindow.xaml`.

**Conclusion:** ship Sprint 12 after Phase 5.1.

---

## 9. End of spec

This spec is the source of truth for Sprint 12. Updates require a new revision (`SPRINT_12_VISION_SPEC_v2.md`) — do not edit in place. Memory file `project_phase3_state.md` references this doc as the canonical scope.

When the maintainer commits to Path B (local CLIP), append `SPRINT_12_VISION_SPEC_LOCAL_CLIP.md` with the Phase 0 analysis for that path: ONNX Runtime binding choice, model quantization, reference embedding curation, etc.
