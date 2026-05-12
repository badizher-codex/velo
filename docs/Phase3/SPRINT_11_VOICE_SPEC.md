# Sprint 11 — AI Conversation & Voice (Spec)

**Status:** Spec (pre-implementation). Sprint 11 is **optional** for Phase 3 closure (Sprint 10b chunks 1/2/4/5/6 already shipped in v2.4.27–v2.4.31). Can ship anytime; not a blocker for Phase 4 or Phase 5.
**Author:** Claude Code session, 2026-05-12.
**Repo state at analysis:** VELO v2.4.31, HEAD `eeb3d01`, 355/355 tests green.
**Purpose:** Concrete, code-aware plan for AI Conversation & Voice so it can be picked up cold and shipped without re-deriving the architecture each time it surfaces in roadmap discussions.

---

## 0. Executive summary

Sprint 11 ships four sub-features that share a common theme — making VELO's AI conversational and audible — but each is independently shippable. They group into one sprint because they share infrastructure (a future `LocalTTSService` is used by both feature #3 and feature #4) and because they bundle the audio/voice story for marketing.

| # | Feature | Effort | Installer impact | Independent? |
|---|---|---|---|---|
| 11.1 | Chat persistence (VeloAgent conversations survive restart) | 2–3 days | None | Yes |
| 11.2 | Whisper local STT (mic → text in AgentPanel) | 3–4 days | +40 MB (tiny) or +140 MB (base) | Yes |
| 11.3 | Local TTS (agent reads its own responses aloud) | 1–2 days | None (System.Speech) or +60 MB (Piper) | Yes |
| 11.4 | SmartShield rationale narration (TTS the BlockNarrationService text) | 0.5 days | None (reuses #11.3) | No — depends on #11.3 |

**Total realistic effort:** 6.5–9.5 days (~1 working week, with a buffer).

**Prerequisites (verified against current code):**

- ✅ `LLamaSharp 0.20.0` already in `VELO.Agent.csproj` (does NOT bundle Whisper — separate package needed).
- ✅ SQLite + sqlite-net-pcl repository pattern in place (`HistoryRepository`, `BookmarkRepository`).
- ✅ `BlockNarrationService` already firing `NarrationReady` events; `BlockNarrationToast` already consuming them; adding a TTS subscriber is one line.
- ❌ No existing audio recording infrastructure (no NAudio, no Whisper.net, no mic button).
- ❌ No existing TTS infrastructure (no `System.Speech.Synthesis` references).
- ❌ No `ChatRepository` or `ChatMessage` model — all conversation history lives in `AgentLauncher._history` as `List<(string Role, string Content)>` and dies on app close.

**Recommendation:** ship #11.1 (chat persistence) first as a standalone v2.4.x patch — it's the highest-value sub-feature with zero installer impact and reuses an existing repository pattern. #11.2 + #11.3 + #11.4 ship together as a "Voice Pack" minor release because they share testing surface (audio device permissions, model loading paths).

---

## 1. Repo state at analysis

### What exists today (verified)

**AgentPanel:**
- `src/VELO.UI/Controls/VeloAgentPanel.xaml(.cs)` — the chat UI (UserControl). Renders messages via `AppendUserBubble`, `AppendAgentBubble`, `AppendActionCard`. **No backing data model;** the UI is the source of truth, which is why history dies on close.
- `src/VELO.Agent/AgentLauncher.cs` — orchestrates conversation. Holds `Dictionary<string, List<(string Role, string Content)>> _history` keyed by tab ID. Trimmed to 40 entries (20 turns) per tab after each turn.

**SQLite repository pattern (reference for #11.1):**
- `src/VELO.Data/Repositories/BookmarkRepository.cs` — `GetAllAsync`, `GetRecentAsync`, `SaveAsync`, `DeleteAsync`. Injected `VeloDatabase db`.
- `src/VELO.Data/Repositories/HistoryRepository.cs` — same shape plus `SearchAsync`, `ClearAllAsync`, `DeleteOlderThanAsync`, optional `ILogger<T>`.
- `src/VELO.App/Startup/DependencyConfig.cs:60-67` — every repository is `services.AddSingleton<T>()` and consumes `VeloDatabase` (line 56).

**BlockNarrationService (reference for #11.4):**
- `src/VELO.Security/Threats/BlockNarrationService.cs` — exposes `event Action<Narration>? NarrationReady`. `Narration` is a record: `(string TabId, string Host, string Kind, string Source, string Text)`. Throttled per-host (5 min cooldown) + global cap (6 narrations/min default).
- `src/VELO.App/MainWindow.xaml.cs` subscribes and dispatches to `BlockNarrationToastControl.ShowNarration(host, source, text)`. Toast auto-hides after 10 s.
- **Implication:** adding TTS is `narrationSvc.NarrationReady += async n => await _tts.SpeakAsync(n.Text);` — one extra subscriber, no event refactor.

### What does NOT exist (gaps to build)

| Gap | Verified by | Effort impact |
|---|---|---|
| Audio recording (NAudio, WaveIn, microphone capture) | grep `NAudio|WaveIn|Microphone|audio` across `src/` → 0 hits | Need to add `NAudio` 2.2.x NuGet (or equivalent) |
| Whisper inference | grep `Whisper` → 0 hits; LLamaSharp 0.20.0 verified no built-in Whisper | Need `Whisper.net` 1.5.x NuGet + GGML model file |
| TTS | grep `SpeechSynthesizer|System\.Speech` → 0 hits | Either reference `System.Speech` (built-in Windows DLL, no NuGet) or add `Piper` (smaller voices, more polished) |
| Chat persistence model | grep `ChatRepository|ChatMessage|ConversationRepository|AgentChat` → 0 hits | Greenfield — see #11.1 design |
| Mic button in AgentPanel | `VeloAgentPanel.xaml` header has only "page context", "clear", "close" buttons | New UI element + click handler |
| Settings for voice/audio | `SettingsWindow.xaml` has 7 panels; no audio/voice panel | New `Voice` panel (or settings inside existing AI panel) |

---

## 2. Sub-feature breakdown

### 2.1 Chat persistence

**Goal:** every user/assistant message in any AgentPanel survives VELO restart. On launch, the most-recent N turns per tab are restored.

**Model:**

```csharp
public sealed record AgentChatMessage(
    int    Id,           // PK auto-increment
    string TabId,        // FK to BrowserTab (loose — tab IDs are session-local strings)
    string Role,         // "user" | "assistant" | "system"
    string Content,
    DateTime CreatedAt);
```

Table name: `agent_chats`. Primary key auto-increment integer (same as `HistoryEntry`).

**Repository (new file):**

`src/VELO.Data/Repositories/ChatRepository.cs` — copy the shape of `HistoryRepository.cs`. Methods:

- `Task SaveAsync(string tabId, string role, string content)`
- `Task<IReadOnlyList<AgentChatMessage>> GetForTabAsync(string tabId, int limit = 20)` — newest first, capped.
- `Task DeleteOlderThanAsync(DateTime cutoff)` — periodic GC (e.g. 30 days).
- `Task ClearAllAsync()` — used by Settings → "Clear all conversation history".
- `Task ClearForTabAsync(string tabId)` — used by the existing AgentPanel "Clear conversation" button.

**Integration points:**

1. `AgentLauncher.SendAsync` — after `_history[tabId].Add((role, content))`, also `await _chatRepo.SaveAsync(tabId, role, content)`. Fire-and-forget acceptable (this isn't security-critical).
2. `AgentLauncher` constructor — accept optional `ChatRepository? chatRepo = null`; if non-null, restore history on first access per tab: `_history[tabId] = (await _chatRepo.GetForTabAsync(tabId, 20)).Select(m => (m.Role, m.Content)).ToList()`.
3. `MainWindow.OnTabCreated` (now `BrowserTabHost`) — after tab is constructed, trigger a one-shot history fetch and call `VeloAgentPanel.RestoreMessages(messages)`.
4. `VeloAgentPanel` — new `public void RestoreMessages(IEnumerable<AgentChatMessage> msgs)` that calls `AppendUserBubble` / `AppendAgentBubble` for each, **without** triggering the chat-send pipeline.
5. `DependencyConfig.cs` — register `AddSingleton<ChatRepository>()` alongside other repos.

**Settings impact:**

- Add toggle "Persist VeloAgent conversations" (default ON).
- Add button "Clear all conversation history".
- Add slider for retention (7 / 30 / 90 / forever days; default 30).

**Tests (mandatory):**

1. `ChatRepository_SaveAsync_persists_message_round_trip`
2. `ChatRepository_GetForTabAsync_returns_newest_first_capped`
3. `ChatRepository_DeleteOlderThanAsync_respects_cutoff`
4. `AgentLauncher_constructor_with_repo_restores_history_on_first_use`
5. `AgentLauncher_constructor_without_repo_falls_back_to_in_memory_history`

**Risks:**

- **DB bloat over time:** even with retention, 100 tabs × 100 messages × 1 KB = 10 MB. Acceptable.
- **Tab ID volatility:** tab IDs are session-local strings generated each launch (verified in `BrowserTab.TabId` / `Initialize`). Restored messages belong to "the previous tab with this ID" which no longer exists. **Decision:** restore the most recent N messages from the *most recently active tab* on launch, OR drop the per-tab key and merge all restored history into a single "previous session" thread. The second is simpler and matches how most chat UIs work. Spec defaults to **per-tab restore** because the original spec is per-tab; if implementation reveals UX confusion, downgrade to merged-thread.

---

### 2.2 Whisper local STT

**Goal:** user clicks (or holds) a microphone button in AgentPanel, speaks, releases. Audio is transcribed locally via Whisper, the transcript replaces the AgentPanel input box value, and Send is enabled.

**Library choice:** **Whisper.net 1.5.x** (the most active .NET binding for whisper.cpp). NuGet: `Whisper.net`. Native runtime bundled per platform.

**Recording library:** **NAudio 2.2.x**. NuGet: `NAudio`. Cross-platform-ish for our needs (Windows-only VELO).

**Model choice:** `ggml-small.en.bin` (~480 MB) is the sweet spot for quality. For sub-installer mass, options:

- **`ggml-tiny.bin`** — 75 MB, multilingual, lower accuracy. Recommended default for VELO bundle.
- **`ggml-tiny.en.bin`** — 40 MB, English-only, slightly better than tiny multilingual for English. Recommended for English-only users.
- **`ggml-base.bin`** — 142 MB, multilingual. Better accuracy. Recommended as opt-in upgrade from Settings.

**Spec defaults to `ggml-tiny.bin` (75 MB)** bundled with installer. Settings exposes "Use higher-accuracy model (downloads 142 MB on first use)" toggle that fetches `ggml-base.bin` on demand into `%LocalAppData%\VELO\models\`.

**UX (push-to-talk, not toggle):**

- Mic button to the left of the text input. Idle state: 🎤 grey.
- Press-and-hold to record. Releases trigger transcription.
- During recording: button turns red 🔴, brief waveform animation (visual feedback that audio is being captured).
- During transcription: button shows ⏳ briefly.
- On error (no mic, mic denied, transcription failed): toast with reason; button returns to idle.

Rationale for push-to-talk vs toggle: toggle requires UI state tracking ("am I recording right now?") and risks silent over-recording if the user forgets. Push-to-talk is unambiguous, has a natural release point, and matches Discord / Slack patterns.

**Architecture:**

`src/VELO.Agent/Voice/LocalWhisperSTTService.cs`:

```csharp
public sealed class LocalWhisperSTTService : IDisposable
{
    private readonly string  _modelPath;
    private readonly ILogger _log;
    private WhisperFactory?  _factory;       // lazy
    private WaveInEvent?     _waveIn;

    public LocalWhisperSTTService(string modelPath, ILogger<LocalWhisperSTTService> log) { ... }

    public Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct);

    public RecordingSession StartRecording();  // returns a Session with StopAndTranscribe()
}
```

`RecordingSession` is `IAsyncDisposable` — disposing it commits the audio buffer and triggers transcription. This makes push-to-talk in the UI clean:

```csharp
private RecordingSession? _activeRec;

private void MicButton_PreviewMouseDown(...)   => _activeRec = _stt.StartRecording();
private async void MicButton_PreviewMouseUp(...)
{
    if (_activeRec is null) return;
    var text = await _activeRec.StopAndTranscribeAsync();
    InputBox.Text = text;
    _activeRec = null;
}
```

**Permissions:**

Windows microphone permission is dialog-driven the first time `WaveInEvent.StartRecording()` runs on .NET 8. No app-manifest change required. We will show a friendly explanation toast *before* the OS dialog so users understand why VELO is asking.

**Tests (mandatory):**

1. `LocalWhisperSTTService_TranscribesKnownGoodWav_ReturnsExpectedText` (uses a 3-second sample WAV in test resources)
2. `LocalWhisperSTTService_HandlesEmptyAudio_ReturnsEmptyString`
3. `LocalWhisperSTTService_HandlesCorruptedAudio_ReturnsEmptyAndLogsWarning`
4. `RecordingSession_StopWithoutStart_NoOp`

**Risks:**

- **First-launch latency:** Whisper.net loads the model on first call (~500 ms for tiny, ~2 s for base). Mitigation: preload on AgentPanel construction; show a one-time "Initializing voice recognition…" toast.
- **No microphone:** detect via `WaveIn.DeviceCount == 0` at service construction; hide the mic button entirely.
- **Mic permission denied:** detect via `NAudio` exception in `StartRecording`; show "Microphone access denied — enable in Windows Settings → Privacy → Microphone" link.
- **Background noise / wrong language:** ship with tiny multilingual; let users upgrade to `base` model in Settings if accuracy is insufficient.

---

### 2.3 Local TTS

**Goal:** the agent's responses can be spoken aloud. Used by #11.4 for SmartShield narration; available as a per-message "🔊 Read aloud" affordance in AgentPanel.

**Library choice:** **`System.Speech.Synthesis.SpeechSynthesizer`** (Windows built-in, no NuGet, no installer impact).

Why not Piper TTS:

| Trait | System.Speech.Synthesis | Piper TTS |
|---|---|---|
| Setup | None (Windows built-in) | NuGet + ~60 MB model |
| Voice quality | Robotic but intelligible | Near-natural |
| Latency | <100 ms cold | ~500 ms cold |
| Voices | Whatever Windows has installed (typically EN + locale) | Bundled multi-locale |
| Installer impact | Zero | +60 MB |

Spec defaults to **System.Speech.Synthesis** for v0.1 of Sprint 11 (zero installer impact, ships immediately). Piper opens as a follow-up if user feedback says voices are too robotic.

**Architecture:**

`src/VELO.Agent/Voice/LocalTTSService.cs`:

```csharp
public sealed class LocalTTSService : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly ILogger           _log;

    public LocalTTSService(ILogger<LocalTTSService> log)
    {
        _synth.SetOutputToDefaultAudioDevice();
        // Default voice = first installed; user can pick in SettingsWindow later
    }

    public Task SpeakAsync(string text, CancellationToken ct = default);
    public void Stop();                       // cancels in-flight speech
    public IReadOnlyList<string> AvailableVoices();
    public void SetVoice(string voiceName);
}
```

DI: `AddSingleton<LocalTTSService>()` in `DependencyConfig.cs`.

**UX hooks:**

- AgentPanel: each assistant bubble gets a 🔊 button on hover. Click to speak that response.
- AgentPanel: a settings toggle "Read responses aloud automatically" (default OFF). When ON, every assistant bubble triggers TTS on appear.

**Settings impact:**

- Voice selection dropdown (populated from `SpeechSynthesizer.GetInstalledVoices()`).
- Rate slider (0.5×–2.0×).
- "Read responses aloud automatically" toggle (default OFF).
- "Read security alerts aloud" toggle (default OFF) — controls #11.4.

**Tests (mandatory):**

1. `LocalTTSService_SpeakAsync_completesWithoutThrowing` (smoke; actual audio impossible to assert in unit tests)
2. `LocalTTSService_Stop_cancelsInProgressSpeech_idempotent`
3. `LocalTTSService_SetVoice_invalidName_logsAndKeepsCurrent`

**Risks:**

- **Headless CI:** `SpeechSynthesizer` requires the Windows SAPI subsystem. The CI smoke test only checks construction + Stop, never actual playback.
- **Audio device unplugged:** `SetOutputToDefaultAudioDevice` throws if no audio device exists. Wrapped in try/catch; service degrades to no-op with a logged warning.
- **Locale mismatch:** Windows ships with EN-US by default; non-English users may get an English voice reading Spanish text awkwardly. Workaround: voice dropdown in Settings + recommendation to install OS language packs.

---

### 2.4 SmartShield rationale narration

**Goal:** when `SmartBlock` / `PhishingShield` / `DownloadGuard` / etc. block something and `BlockNarrationService` fires `NarrationReady`, the agent narrates the reason via TTS (in addition to the existing toast).

**Implementation:**

In `MainWindow.xaml.cs`, the existing subscriber to `narrationSvc.NarrationReady` extends from one to two side effects:

```csharp
narrationSvc.NarrationReady += narration =>
{
    Dispatcher.Invoke(() =>
        BlockNarrationToastControl.ShowNarration(
            narration.Host, narration.Source, narration.Text));

    // NEW (Sprint 11.4): also speak if the user enabled it.
    if (_settings.Get<bool>(SettingKeys.NarrationTtsEnabled, false))
        _ = _tts.SpeakAsync(narration.Text);
};
```

**Settings impact:** one toggle "Read security alerts aloud" (the same toggle from #11.3 settings; not a new one).

**Tests (mandatory):**

1. `MainWindow_NarrationReady_invokesTts_whenSettingEnabled`
2. `MainWindow_NarrationReady_skipsTts_whenSettingDisabled`

(Both via test doubles; no actual audio playback asserted.)

**Risks:**

- **TTS queue backup:** if multiple narrations fire in rapid succession (3 blocked trackers in 2 seconds), TTS could queue 30 s of speech. Mitigation: `LocalTTSService.SpeakAsync` calls `Stop()` first if a previous speech is in-flight. This means the *most recent* narration wins, which matches the existing toast UX (newer toast replaces older).

---

## 3. Dependencies & installer impact

| Item | Source | Size |
|---|---|---|
| `Whisper.net` NuGet | nuget.org | ~5 MB (binary) |
| `NAudio` NuGet | nuget.org | ~2 MB |
| `ggml-tiny.bin` model | huggingface.co/ggerganov/whisper.cpp | ~75 MB |
| `System.Speech.Synthesis` | Windows built-in | 0 MB |
| **TOTAL bundled** | | **~82 MB** |

**Distribution decision:** bundle `ggml-tiny.bin` in the installer (one-time decision; user expects "voice features work out of the box"). Larger models (`base`, `small`) download on demand from Hugging Face when the user opts in via Settings.

Updates `installer/velo-setup.iss` (auto-rewritten by CI) — no manual change needed; the script's existing pattern handles bundled resources.

**.NET 8 / WPF interaction:** `System.Speech.Synthesis` lives in the `System.Speech.dll` ref assembly. On `net8.0-windows` it's a NuGet `System.Speech` (Microsoft maintained). Single line in `VELO.Agent.csproj`:

```xml
<PackageReference Include="System.Speech" Version="10.0.7" />
```

(Pinned to 10.0.7 per lesson #18, see MEMORY.md.)

---

## 4. Acceptance criteria

### At sprint close

- [ ] All 4 sub-features implemented and behind a unified Settings → Voice panel.
- [ ] Chat persistence: a fresh user can have a conversation, close VELO, reopen, and see the messages in the AgentPanel.
- [ ] Whisper STT: pressing the mic button and speaking "test test test" transcribes to "test test test" (or close — Whisper-tiny is imperfect).
- [ ] TTS: opening any assistant message and clicking 🔊 reads it aloud through default speakers.
- [ ] Narration: with "Read security alerts aloud" enabled, blocking a tracker results in audible narration matching the toast text.
- [ ] No regression in existing 355/355 tests.
- [ ] New tests (5 for persistence + 4 for STT + 3 for TTS + 2 for narration = 14 minimum) all green.
- [ ] CHANGELOG.md updated with `## [2.5.0] — Sprint 11 — AI Conversation & Voice` entry.
- [ ] Installer size delta < 100 MB (target ~82 MB).
- [ ] Smoke test #1 (WiringSmokeTests) still passes with new DI registrations.

### Settings panel (new)

- [ ] Voice section in SettingsWindow with 4 subsections: Chat history, Speech input, Speech output, Voice selection.
- [ ] All settings persist across restart (uses existing `SettingsService`).

---

## 5. GitHub issues proposed

Filable at sprint kickoff. Numbering preserves the sub-feature mapping.

| # | Title | Sub-feature |
|---|---|---|
| 11.1.1 | Add `ChatRepository` + `AgentChatMessage` model (SQLite) | 11.1 |
| 11.1.2 | Wire `ChatRepository` through `AgentLauncher` (save + restore) | 11.1 |
| 11.1.3 | Add Settings → "Persist VeloAgent conversations" toggle + retention | 11.1 |
| 11.1.4 | Add `VeloAgentPanel.RestoreMessages(IEnumerable<AgentChatMessage>)` UI path | 11.1 |
| 11.2.1 | Add `Whisper.net` + `NAudio` NuGet refs; bundle `ggml-tiny.bin` | 11.2 |
| 11.2.2 | Create `LocalWhisperSTTService` (transcribe + RecordingSession) | 11.2 |
| 11.2.3 | Add mic button (push-to-talk) to `VeloAgentPanel.xaml` | 11.2 |
| 11.2.4 | Settings → "Use higher-accuracy model (downloads 142 MB)" toggle + on-demand fetch | 11.2 |
| 11.3.1 | Add `System.Speech 10.0.7` NuGet ref | 11.3 |
| 11.3.2 | Create `LocalTTSService` (System.Speech.Synthesis wrapper) | 11.3 |
| 11.3.3 | Add 🔊 button to assistant bubbles in AgentPanel | 11.3 |
| 11.3.4 | Settings → voice / rate / "read responses aloud" | 11.3 |
| 11.4.1 | Subscribe `NarrationReady` to `LocalTTSService` in MainWindow | 11.4 |
| 11.4.2 | Settings → "Read security alerts aloud" toggle | 11.4 |
| 11.4.3 | Single-flight TTS for narration (cancel previous, speak latest) | 11.4 |

**Total: 14 issues.** Roughly half-day each. Realistic for one focused week.

---

## 6. Risks & open questions

| Risk / question | Mitigation / decision |
|---|---|
| Whisper.net binary size on Windows ARM64? | VELO is x64-only today. ARM64 considered out of scope. |
| First-time microphone permission UX | Show pre-flight explanation toast before triggering OS dialog. |
| TTS lag on slow machines | Cap text length per `SpeakAsync` call to ~500 chars; longer narrations truncated with "…". |
| Tab ID volatility breaking restore (see #11.1 risks) | Default to per-tab; downgrade to merged-thread if real-world UX confusion. |
| Should chat persistence be opt-in? | Default ON. User can clear or disable from Settings. Same model as History (default ON, user controls retention). |
| Multi-language Whisper vs English-only | Default `ggml-tiny.bin` is multilingual. Locale-aware override via Settings if needed. |
| Bundle vs download-on-demand for Whisper model | Bundle tiny (75 MB). Download on demand for base/small. Decision driven by "voice should work out of the box". |

### Open questions for the maintainer at kickoff

- **OQ-1:** Should chat persistence be a global setting (one toggle for all tabs) or per-tab (some tabs persist, others incognito)? Spec defaults to global. Per-tab adds complexity.
- **OQ-2:** Should the mic button be hidden when no microphone is detected (clean UX) or always shown with disabled state (discoverable)? Spec defaults to hidden + a Settings explanation.
- **OQ-3:** Push-to-talk vs toggle vs both? Spec defaults to push-to-talk; toggle as Settings opt-in.

---

## 7. Sequencing within the sprint

```
Day 1-2 :  #11.1  Chat persistence  (lowest risk, highest value)
Day 3-5 :  #11.2  Whisper STT
Day 5-6 :  #11.3  Local TTS
Day 6   :  #11.4  Narration wiring  (half-day, builds on #11.3)
Day 7   :  Settings polish, tests, smoke runs, release prep
```

**Release strategy:**

- Option A: ship sub-features as separate point releases (v2.5.0 chat persistence, v2.5.1 voice in/out, v2.5.2 narration). Lower risk, more user touchpoints.
- Option B: ship Sprint 11 as a single v2.5.0 minor release. Cleaner changelog, one CI cycle.

Spec recommends **Option A** if the timeline allows — chat persistence is independently useful and lower-risk; shipping it alone tests the new `ChatRepository` infrastructure before audio joins the equation.

---

## 8. Phase 4 / Phase 5 alignment

- **Phase 4 (Council Mode):** Sprint 11 is not a prerequisite. Council can synthesize results aloud post-shipping if `LocalTTSService` exists. Council Mode v0.2 could integrate voice for "Council reads me its synthesis" demos.
- **Phase 5 (UI Modernization):** the mic button and voice settings panel inherit the new palette automatically once Phase 5.0 (theme resources) ships. No re-skin work required if Sprint 11 ships after Phase 5.0 — preferable order.

**Conclusion:** ship Sprint 11 after Phase 5.0 (theme tokens land) but before Phase 5.2 (MainWindow surfaces) so the new AgentPanel chrome is already palette-correct.

---

## 9. End of spec

This spec is the source of truth for Sprint 11. Updates require a new revision (`SPRINT_11_VOICE_SPEC_v2.md`) — do not edit in place. Memory file `project_phase3_state.md` references this doc as the canonical scope.
