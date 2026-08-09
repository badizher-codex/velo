# Media Download — Codebase Analysis (Phase 0 output)

**Request:** let the user download media they are viewing in VELO, after a verification step, with a choice between audio and video where that distinction exists.

**Status:** analysis (§1–§5) and execution plan (§7), both complete. **No code written.** Decisions taken with the maintainer are in §6. Citations verified against HEAD `3e1a379`.

---

## 0. Executive summary

The feature is a good fit. VELO already owns the two things that normally make this hard: it sees every network request, and it can inject and talk to page-side script. What it does *not* have is a downloader — the existing `DownloadManager` only keeps a list; WebView2 moves the bytes.

Four findings change the shape of the work:

1. **The page-side detector beats the network sniffer.** VELO has a mature script-injection path (11 scripts, the `window.__veloBridge` message bridge, and scripts read from disk per tab so iteration is ~10 s with no rebuild). A detector living in the page can see `<video>` elements, hook `MediaSource`, read the player's manifest URL, *and* detect DRM — none of which a URL sniffer can do reliably.
2. **`DownloadGuard` will kill this on contact.** Its burst rule blocks the **2nd download within 3 seconds**. A single HLS download is hundreds of requests. A user-initiated lane is a prerequisite, not a nicety.
3. **There is no general-purpose downloader.** `DownloadManager.StartDownload` records an item and raises an event; the transfer belongs to WebView2's `DownloadOperation`. `UpdateDownloader` has `HttpClient` + SHA256 but is welded to the update flow. Something new is needed.
4. **The muxer decides the scope, and the scope is probably smaller than it looks.** Everything except "video **and** audio together, from adaptive streaming" is reachable with zero new dependencies.

**Two unknowns gate the plan, and neither can be answered by reading code:**

- **OPEN-1** — whether VELO sees media segments at all through `WebResourceRequested`. **This cannot be answered from the field logs**: `RequestGuard` only writes blocks and bypasses, so zero media URLs in three days of logs is the expected output whether or not it sees them.
- **OPEN-3** — whether an anonymous re-request distinguishes gated content from public, which decides how D-2's members-only gap is closed.

Both are measured in P0, before anything is built.

---

## 1. VALIDATE — resolved against real code

### V-1 — Script injection and a page→host bridge exist ✅

`BrowserTab.xaml.cs:250-268` injects via `AddScriptToExecuteOnDocumentCreatedAsync`, sourcing from `resources/scripts/` through `LoadScriptResourceAsync` (`BrowserTab.Helpers.cs:624`). Eleven scripts ship today. The return path lands on `OnWebMessageReceived` (`BrowserTab.Events.cs:376`), proven by `council-bridge.js`.

**Two constraints on the return path that are easy to get wrong:**

1. **The bridge is not `chrome.webview`.** `webview-cloak.js` (F-1, v2.4.68) **deletes** `chrome.webview` from the page — that is the whole point of the cloak — after stashing it as `window.__veloBridge`. Every injected script must resolve it the way the existing ones do:

   ```js
   const bridge = window.__veloBridge || (window.chrome && window.chrome.webview);
   ```

   Five scripts already follow this pattern (`autofill`, `council-bridge`, `dom-extractor`, `glance-hover`, `webview-cloak`).

2. **Injection order matters.** The cloak must be injected *first* so it can stash the bridge before hiding it (`BrowserTab.xaml.cs:246`). `media-detect.js` goes after it.

`LoadScriptResourceAsync` reads from `<AppDir>/resources/scripts/` **on disk** (verified: `File.ReadAllTextAsync`, no embedded resource), and is called per tab — so the edit→verify loop really is ~10 seconds with no rebuild (lesson #42).

### V-2 — Every request passes through VELO ✅ (with a caveat, see OPEN-1)

`BrowserTab.xaml.cs:410-411` registers `WebResourceRequested` with filter `"*"` and `CoreWebView2WebResourceContext.All`. `ProcessRequestAsync` (`BrowserTab.Events.cs:50`) has the URI, the Referer and the resource context for every one.

### V-3 — The "verification" the request asks for already exists ✅

`DownloadGuard.Evaluate(tabId, downloadUrl, fileName, pageUrl)` returns a `DownloadVerdict` (Allow / Warn / Block) with a reason. It is already wired into the download path. The media feature should reuse it, not invent a second gate.

### V-4 — `DownloadManager` does not download ❌

`DownloadManager.StartDownload` (`DownloadManager.cs:23`) creates a `DownloadItem`, de-dupes, inserts it into an `ObservableCollection` and raises `DownloadStarted`. That is all. The actual transfer is WebView2's, driven by a page-initiated `DownloadStarting`.

Consequence: a download **we** initiate has no engine behind it today.

### V-5 — No reusable general-purpose downloader ❌

`UpdateDownloader` (`VELO.Core/Updates/`) owns an `HttpClient`, streams to disk and verifies SHA256 — but its entry point is `DownloadAndVerifyAsync`, which derives a `SHA256SUMS.txt` URL from the release URL. It is an update installer, not a file fetcher. The *pattern* is reusable (and is the same one S-D uses for the Sentinel model, already proven against the real network); the class is not.

### V-6 — `DownloadGuard`'s burst rule blocks multi-segment downloads ⚠️

`DownloadGuard.cs:50` — `BurstWindow = 3s`. `DownloadGuard.cs:177` — `isBurst = queue.Count >= 1`, i.e. **the second download inside the window is already a burst**, and burst → Block (drive-by pattern).

An HLS download issues hundreds of segment requests. Without a user-initiated lane it dies on the second segment. This is a prerequisite, and the lane must be explicit — *not* achieved by weakening the drive-by rule, which is doing real work.

### V-7 — Media extensions are half-known to `RequestGuard` ✅/⚠️

`RequestGuard._downloadExtensions` already contains `.mp3 .mp4 .m4a .mkv .webm .avi .mov .flac .wav`. It does **not** contain `.m3u8 .mpd .ts .m4s`. And the bypass that uses it is gated on `resourceType == "Document"` (`RequestGuard.cs:162`) — navigation only, not media loaded inside a page.

Consequence: progressive media opened directly is already treated sanely. Streaming manifests are invisible to that logic.

### V-8 — DRM detection: no code exists today ⚠️ (feasible, unverified)

**Marked honestly:** there is no EME hook anywhere in the codebase. This is not a validation against real code — it is a design claim resting on the web platform, that a page-side hook on `navigator.requestMediaKeySystemAccess` reports EME use.

What *is* verified is that the maintainer has the domain knowledge and a working harness (`velo-drm-check.html`, plus the whole Prime Video investigation in `BACKLOG.md`), and that V-1 gives us the injection point to install such a hook.

It must be proven in P0 alongside everything else — a YouTube rental is in the reference set precisely so this claim is tested rather than assumed.

This matters for correctness, not only policy: without it, protected content would produce a downloaded file of encrypted, unplayable bytes — a bug report waiting to happen.

---

## 2. OPEN — genuinely unknown, must be measured

### OPEN-1 — Does `WebResourceRequested` actually surface media segments? 🔴 blocking

Chromium's media pipeline can fetch segments on paths that do not raise the WebView2 resource-requested event. If it does not fire for segments, a network-sniffer design is dead and the page-side detector is the *only* option.

**This is not answerable from the field logs.** `RequestGuard` logs only blocks (`LogInformation`, line 132) and download-extension bypasses (`LogDebug`, line 164); allowed requests are never written. Zero media URLs in three days of logs is therefore **not evidence of absence** — it is the expected output either way.

**How to measure:** a throwaway instrumented build that logs every `WebResourceRequested` URI, then browse three reference pages (a progressive `<video>`, an HLS site, a DASH site) and count what appears. One afternoon, no production code.

### OPEN-2 — What is the real mix of media on the sites the maintainer uses?

The value of a no-muxer v1 depends entirely on this. If most of what he watches is HLS video+audio, a v1 that only handles progressive files and audio-only is close to useless. If a meaningful share is progressive or audio-only, v1 ships value immediately.

**How to measure:** the same instrumented build, classifying what it sees per site.

### OPEN-3 — Does an anonymous fetch distinguish gated content? 🟡 shapes D-2

The maintainer's proposal: before offering a download, re-request the content **without the user's session** and decline if it is not reachable. Generic, site-agnostic, and it expresses the intent ("don't download what people pay for") far better than scraping a badge out of YouTube's DOM.

It works when **the session is what authorises**: a page behind a login wall, or media whose access depends on a cookie.

It is expected to fail when **the URL itself carries the authorisation** — signed/tokenised CDN URLs, where a token in the query string replaces the session. Large video CDNs work this way, YouTube's `googlevideo.com` included. If so, an anonymous fetch of a *segment* from a members-only video would return 200 and the check would not catch the exact case it was meant to catch.

**This is an expectation, not a measurement.** It must be tested, not assumed.

**How to measure (folds into P0, which already captures the URLs):**

| Test | Expectation if the signed-URL theory holds |
|---|---|
| Anonymous GET of a captured **segment** URL from a free YouTube video | 200 |
| Anonymous GET of a captured **segment** URL from a members-only video | 200 ← the check fails here |
| Anonymous GET of the **watch page** of a members-only video | no player / members-only response ← the check works here |
| Anonymous GET of a segment from a site that authorises by cookie | 401/403 ← the check works here |

**If the theory holds**, the check must be applied at the **page** level, not the media URL, and D-2's gap closes for members-only content while remaining generic.

### OPEN-4 — Does concatenating audio segments actually produce a playable file?

The claim "audio-only needs no muxer" holds for plain AAC-in-TS and for some fMP4 audio, and fails for others (fMP4 needs the init segment prepended, and some encoders need a real remux). Cheap to test once OPEN-1 gives real segment URLs.

---

## 3. Prerequisites the request did not list

| # | Prerequisite | Why |
|---|---|---|
| **P1** | A real downloader: `HttpClient`, streaming to disk, progress, cancel, resume | V-4/V-5 — nothing today moves bytes on our behalf |
| **P2** | A user-initiated lane through `DownloadGuard` | V-6 — otherwise the guard blocks segment 2 |
| **P3** | DRM detection *before* the option is offered | V-8 — otherwise we hand the user an unplayable file |
| **P4** | A muxer decision | §4 — determines whether "video + audio" is in scope at all |
| **P5** | `DownloadItem` needs to represent a multi-part job | Today it is one URL → one file with a known `TotalBytes`. A segmented download has N URLs and an unknown total until the manifest is parsed |

---

## 4. The muxer, and what it costs

A playable video file is a **container** holding a video track and an audio track, interleaved and time-aligned. Adaptive streaming (HLS/DASH) never sends that container: it sends a text index plus the two tracks cut into hundreds of separate segments, and the player feeds them to the decoder in memory without ever building a file.

So the work splits cleanly:

| Case | What it takes | New dependency |
|---|---|---|
| Progressive file (`.mp4`, `.webm`, a direct `<video src>`) | Fetch one URL | **none** |
| Audio only, from HLS/DASH | Fetch the audio segments, concatenate (see OPEN-3) | **none** (probably) |
| Video only | Same, for the video track — but a video-only file has no sound | **none** |
| **Video + audio** | Interleave two streams with correct timestamps | **a muxer — ffmpeg** |

**Why the dependency is expensive for VELO specifically:** the binary ships unsigned, SmartScreen already warns, and there is an open AV false-positive problem (`project_av_false_positive_codesigning.md`). Adding an ~80 MB `ffmpeg.exe` to an unsigned installer makes detection worse, not better. The maintainer's stated position is to build clean until the certificate is bought.

Three options, in increasing cost:

- **(A) No muxer.** Progressive + audio-only. Zero new dependencies, no AV impact. Scope depends on OPEN-2.
- **(B) On demand.** Fetch ffmpeg on first use, SHA256-verified, with an audit trail — reusing the S-D `SentinelModelInstaller` pattern, which is already built and has been exercised against the real network twice. No installer growth, no AV surface until the user opts in.
- **(C) Bundle.** Not viable before Authenticode.

---

## 5. Non-goals (settled)

- **No DRM circumvention.** Widevine/PlayReady content (Netflix, Prime Video, Disney+, Max) arrives encrypted. Making it playable means extracting keys, which is circumvention under DMCA §1201 and its EU equivalents. Out of scope permanently, by decision, not by omission.
- **Protected content is detected and declined**, with a clear message — not attempted and silently broken.

## 6. Decisions taken (2026-08-09)

**D-1 — Muxer: on demand, never bundled.** Option (B). ffmpeg is fetched on first use, SHA256-verified, with an audit trail, reusing the `SentinelModelInstaller` pattern. The installer does not grow and the AV surface does not change until the user opts in. Revisit bundling only after Authenticode.

Note this decision does **not** gate the early phases: everything up to and including audio-only is identical under (A) and (B). The muxer only appears in P5.

**D-2 — YouTube is in scope, except anything paid.** No downloading of content YouTube charges to watch.

This lands almost entirely on the DRM rule already taken: **rentals, purchases (Movies & TV) and pay-per-view live events are Widevine-protected**, so the EME detector declines them without a YouTube-specific rule. Policy and mechanism agree, which is the best sign the line is in the right place.

**One gap:** *members-only* and invite-only videos are paid or restricted but **not** DRM-protected — they are access-controlled by session. The EME detector will not stop them.

**Resolution (maintainer's proposal, refined): an anonymous accessibility check, plus a notice.**

1. **Anonymous check.** Before offering a download, re-request **the page** with a clean context — no cookies, no session. If the content is not obtainable without an account, decline. This is generic: it covers members-only, invite-only, Patreon and any login wall, without a single site-specific rule and without touching anyone's DOM.

   Applied at the **page** level rather than the media URL, because the media URL is expected to carry its own signed token and would answer 200 regardless — see **OPEN-3**, which measures exactly this before the design is committed to.

2. **A notice.** No automatic check can determine whether the user has the right to download a given item. Saying so plainly is more honest than a check that implies a guarantee it cannot give.

Rejected: reading the members-only badge out of YouTube's rendered DOM. The YouTube ad-block history (v0.1 never worked, v0.2 broke playback, v0.3 took three attempts) is the reason — that surface changes constantly and is the wrong thing to depend on.

**D-3 — Location.** `docs/Phase6/`, alongside the other phase analyses.

---

## 7. Execution plan

Each phase states how it is verified. **No phase begins before its predecessor's verification passes.**

### P0 — Measure OPEN-1, OPEN-2 and OPEN-3 🔴 gates everything

Throwaway instrumented build, no production code, discarded afterwards.

- Log every `WebResourceRequested` URI + `ResourceContext`.
- Page-side probe logging `<video>`/`<audio>` elements, `MediaSource.addSourceBuffer` calls, and `requestMediaKeySystemAccess`.
- Browse a fixed reference set: a progressive `<video>` page · an HLS site · a DASH site · a free YouTube video · a YouTube rental (must show as DRM) · **a members-only video** · a podcast episode.
- Replay the captured URLs anonymously per the OPEN-3 table.

**Verification:** a table in this document — for each case, what the network layer saw, what the page layer saw, which layer would have been sufficient, and whether the anonymous check distinguished gated from public. Explicit decisions recorded on OPEN-1 and OPEN-3.

**Exit gate:** if the network layer does not see segments, P1 drops the sniffer and goes page-side only. If neither layer sees them, the feature stops here and we say so.

### P1 — Detection, read-only

`resources/scripts/media-detect.js` + inventory in C#. Nothing downloadable yet.

- Enumerate media elements; hook MSE; hook EME.
- Bridge via `window.__veloBridge` with the `chrome.webview` fallback, injected **after** `webview-cloak.js` (V-1) → `OnWebMessageReceived` → per-tab `MediaInventory`.
- Classify: progressive · HLS · DASH · **DRM-protected** · unknown.

**Verification:** unit tests on the classifier (URL/MIME → class) · smoke test that the script is valid and its bridge shape matches the C# parser (the `WiringSmokeTests` pattern) · runtime on the P0 reference set, where the inventory must match what P0 measured. Iteration is ~10 s per change (scripts are re-read per tab, lesson #42).

### P2 — Download engine

`MediaDownloader` in `VELO.Core`: `HttpClient`, streaming to disk, progress, cancel, resume. `DownloadItem` extended to represent a multi-part job (N URLs, total unknown until the manifest is parsed).

**Verification:** unit tests against a local `HttpListener` fixture — byte-identical output vs source, cancel mid-transfer leaves no partial file in the visible list, resume completes. Run in both directions.

### P3 — Guard lane

An explicit user-initiated lane in `DownloadGuard`, keyed to a job the user actually clicked. The drive-by burst rule stays exactly as strong for everything else.

**Verification:** tests in both directions — a synthetic drive-by burst still Blocks; a user-initiated segmented job runs through hundreds of requests untouched. This is the guard that most needs a negative test (lesson #44).

### P4 — UI, and the DRM refusal

Chip in the URL bar when the inventory is non-empty (same pattern as the shield chip) → panel listing what was found, with the audio/video choice where it applies. DRM-protected items render as an explicit "protected content — cannot be downloaded", never as a disabled row with no explanation.

**Verification:** runtime on the reference set, screenshots in both themes, and the DRM case verified **on a YouTube rental** — the one that must refuse.

### P5 — Muxer, gated on P0's OPEN-2 result

Only if the measured mix says video+audio matters. ffmpeg on demand per D-1.

**Verification:** SHA256 match on the fetched binary · audit-log entry · an end-to-end download that produces a file that actually plays.

---

## 8. What is deliberately not in this plan

- Any form of DRM circumvention (§5).
- Bundling ffmpeg (D-1).
- A YouTube-specific extraction path. If the generic detector cannot see it, it is out of scope — a per-site extractor is a maintenance treadmill and the ad-block history says so.
