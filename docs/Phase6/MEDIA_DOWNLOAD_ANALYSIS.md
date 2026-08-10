# Media Download — Codebase Analysis (Phase 0 output)

**Request:** let the user download media they are viewing in VELO, after a verification step, with a choice between audio and video where that distinction exists.

**Status:** analysis (§1–§5), decisions (§6), execution plan (§7) and **P0 measurement results (§8)**. No production code written; the P0 instrumentation was reverted. Citations verified against HEAD `3e1a379`.

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

### P0 — Measure OPEN-1, OPEN-2 and OPEN-3 🔴 gates everything ✅ **DONE — see §8**

Throwaway instrumented build, no production code, discarded afterwards.

- Log every `WebResourceRequested` URI + `ResourceContext`.
- Page-side probe logging `<video>`/`<audio>` elements, `MediaSource.addSourceBuffer` calls, and `requestMediaKeySystemAccess`.
- Browse a fixed reference set: a progressive `<video>` page · an HLS site · a DASH site · a free YouTube video · a YouTube rental (must show as DRM) · **a members-only video** · a podcast episode.
- Replay the captured URLs anonymously per the OPEN-3 table.

**Verification:** a table in this document — for each case, what the network layer saw, what the page layer saw, which layer would have been sufficient, and whether the anonymous check distinguished gated from public. Explicit decisions recorded on OPEN-1 and OPEN-3.

**Exit gate:** if the network layer does not see segments, P1 drops the sniffer and goes page-side only. If neither layer sees them, the feature stops here and we say so.

### P1 — Detection, read-only ✅ **DONE — see §9 (gate 0), §10 (gate 0.5), §11 (gates 1 & 3)**

`resources/scripts/media-detect.js` + inventory in C#. Nothing downloadable yet.

- Enumerate media elements; hook MSE; hook EME.
- Bridge via `window.__veloBridge` with the `chrome.webview` fallback, injected **after** `webview-cloak.js` (V-1) → `OnWebMessageReceived` → per-tab `MediaInventory`.
- Classify: progressive · HLS · DASH · **DRM-protected** · unknown.

**Verification:** unit tests on the classifier · smoke test that the script is valid and its bridge shape matches the C# parser (the `WiringSmokeTests` pattern) · runtime on the reference set, where the inventory must match what was measured. Iteration is ~10 s per change — but note `LoadScriptResourceAsync` runs inside `EnsureWebViewInitializedAsync`, so the loop is "open a **new tab**", not "reload the current one" (lesson #42).

⚠️ **Superseded in part by §9.** P1 opens with **Gate 0**, which measured the Content-Type rule §8 assumed and found it insufficient. The classifier signature is `(url, contentType, contentLength, contentRange, manifestProvenance, pageMseEvidence)` — *not* URL/MIME → class. Read §9 before writing it.

### P2 — Download engine ⚠️ **SPLIT — see §12.** P2a-1 done; P2a-2 (manifests) and P2b (MSE capture) open

`MediaDownloader` in `VELO.Core`: `HttpClient`, streaming to disk, progress, cancel, resume. `DownloadItem` extended to represent a multi-part job (N URLs, total unknown until the manifest is parsed).

**Verification:** unit tests against a local `HttpListener` fixture — byte-identical output vs source, cancel mid-transfer leaves no partial file in the visible list, resume completes. Run in both directions.

### P3 — Guard lane ✅ **DONE — see §13**

An explicit user-initiated lane in `DownloadGuard`, keyed to a job the user actually clicked. The drive-by burst rule stays exactly as strong for everything else.

**Verification:** tests in both directions — a synthetic drive-by burst still Blocks; a user-initiated segmented job runs through hundreds of requests untouched. This is the guard that most needs a negative test (lesson #44).

### P4 — UI, and the DRM refusal ✅ **DONE — see §14**

Chip in the URL bar when the inventory is non-empty (same pattern as the shield chip) → panel listing what was found, with the audio/video choice where it applies. DRM-protected items render as an explicit "protected content — cannot be downloaded", never as a disabled row with no explanation.

**Verification:** runtime on the reference set, screenshots in both themes, and the DRM case verified **on a YouTube rental** — the one that must refuse.

### P5 — Muxer, gated on P0's OPEN-2 result

Only if the measured mix says video+audio matters. ffmpeg on demand per D-1.

**Verification:** SHA256 match on the fetched binary · audit-log entry · an end-to-end download that produces a file that actually plays.

---

## 8. P0 results — measured 2026-08-09

Throwaway instrumented build (network log on every `WebResourceRequested` + a page-side probe hooking MSE, EME and media elements). **370 requests and 51 probe events captured, then fully reverted** — the tree is clean and no probe code survives.

Reference set, all public, no accounts touched: a YouTube video · the dash.js reference player · a w3schools `<video>` page · the maintainer's own `velo-drm-check.html` · bitmovin's DRM demo.

### OPEN-1 — RESOLVED ✅ Yes, but not where you would look

| Case | Seen on the network? | `ResourceContext` |
|---|---|---|
| Progressive `<video>` (w3schools `mov_bbb.mp4`) | **yes**, the real URL | `Media` |
| YouTube adaptive (`googlevideo.com/videoplayback?…`) | **yes**, the real stream URL | **`XmlHttpRequest`** |

Two traps this measurement exposed, both of which would have produced a broken v1:

1. **`ResourceContext == Media` is not the filter.** The only requests tagged `Media` on YouTube were four UI sound effects (`open.mp3`, `success.mp3`…). The actual video stream arrived as `XmlHttpRequest`.
2. **URL extensions find nothing.** Zero requests in the whole capture matched `.m4s`, `.ts`, `.mpd` or `.m3u8`. YouTube serves a single `videoplayback?…` endpoint driven by HTTP Range headers, not discrete numbered segments. A classifier keyed on extension would report "no media" on the single most important site.

Classification must therefore be by **response `Content-Type`**, not by extension and not by resource context.

### Correction to §0 finding #1 — it is both layers, not one

The analysis said the page-side detector *beats* the network sniffer. The measurement says it **needs** it:

- The page tells you **what**: MSE in use, and the codecs — YouTube asked for `audio/webm; codecs="opus"` and `video/mp4; codecs="av01.0.09M.08"` as **two separate SourceBuffers**. This is the audio/video split the feature is built on, confirmed live.
- The page cannot tell you **where**: the `<video>` element's `src` was `blob:https://www.youtube.com/80715dcd-…`. A blob URL is opaque; the real addresses only exist on the network layer.

P1 must correlate the two: MSE codecs from the page, stream URLs from the network.

### V-8 — VALIDATED ✅, with a refinement that matters

The EME hook fired 34 times and correctly reported `com.widevine.alpha`, `com.microsoft.playready`, `com.apple.fairplay` and `org.w3.clearkey`.

**But the refusal rule cannot be "the page called `requestMediaKeySystemAccess`".** bitmovin's page probed **13 different key systems** on load — pure capability detection, nothing protected playing. That rule would refuse downloads on any site that merely feature-detects.

The signal must be *actual use*: a resolved access **plus** `setMediaKeys()` on the element, or an `encrypted` event. To be settled in P1.

### OPEN-3 — RESOLVED ✅ The signed-URL theory holds, and there is a second failure mode

| Test | Result |
|---|---|
| Anonymous GET of the captured YouTube stream URL | **HTTP 200** — the token in the URL replaces the session |
| Anonymous GET of a public `.mp4`, no headers | **HTTP 403** |
| Same, with a browser `User-Agent` | **HTTP 206** |

So the check fails in *both* directions if applied naively to the media URL: it lets gated content through (200 on a signed URL) **and** it refuses ordinary public content (403 from hotlink/UA protection).

**Confirms the design in D-2** — the check belongs on the **page**, not the media URL — and adds a hard requirement: **it must send browser-realistic headers**, or it will refuse content that is perfectly downloadable. The 403→206 flip came from the `User-Agent` alone.

### OPEN-2 — partial

Sample too small for a real mix (three content sites). What is established: progressive is trivially reachable, and YouTube's separate audio/video SourceBuffers make audio-only genuinely meaningful rather than a conversion. A wider sample should ride along with P1 rather than justify another instrumented build.

### OPEN-4 — not tested

Needs discrete audio segment URLs. YouTube does not expose them as separate files, so this waits for a real HLS/DASH source. The dash.js reference player loaded no stream without interaction.

### Exit gate

**Passed.** Both layers see what they need to, DRM is detectable, and the gating check has a workable shape. P1 may start.

---

## 9. P1 Gate 0 — the Content-Type table, measured 2026-08-10

### Why this gate exists

§8 concluded "classification must be by response `Content-Type`". That conclusion was reached **by elimination** — URL extensions matched nothing, `ResourceContext` reported the video stream as `XmlHttpRequest` — and not by measurement: P0 instrumented `WebResourceRequested`, which carries the request and therefore **no response headers**. Not one Content-Type was ever observed. Lesson #40 says measure the hypothesis before coding against it, so this gate measured it.

Two things had to be built first, both verified against real code:

- **There was no response hook anywhere.** A grep for `WebResourceResponseReceived|ResponseReceived|ContentType|Content-Type` over `src/` returned zero matches. The only network hook was `WebResourceRequested` (`BrowserTab.xaml.cs:410` → `ProcessRequestAsync`, `BrowserTab.Events.cs:50`). `CoreWebView2.WebResourceResponseReceived` is new event surface that the P1 plan did not list. It is now registered next to the request hook and **stays** — the Gate 1 classifier hangs off it.
- **The probe itself is temporary.** `MediaProbeLog` (`src/VELO.UI/Utilities/MediaProbeLog.cs`) is off unless `VELO_MEDIA_PROBE=1`, writes `%LOCALAPPDATA%\VELO\logs\media-probe.tsv`, and is deleted once this section is settled. The switch was run in **both directions** before any capture was trusted: with it unset, no file is created at all; with it set, rows appear.

One API correction worth recording: `CoreWebView2WebResourceResponseReceivedEventArgs` exposes only `Request` and `Response` — **no `ResourceContext`**. P0's "the stream arrives tagged `XmlHttpRequest`" cannot be re-confirmed on this event, so the probe records `Content-Range` in that column instead.

### What was captured

765 responses across three sites: the w3schools `<video>` page (progressive), a YouTube video, and the hls.js demo (real HLS — the source P0 never got, since dash.js loaded nothing without interaction).

### The result: the Content-Type rule fails, in four different ways

**1. A `video/*` or `audio/*` filter over the whole capture matches exactly seven rows — and four of them are wrong.**

| Content-Type | URL | What it actually is |
|---|---|---|
| `video/mp4` ×3 | `w3schools.com/html/mov_bbb.mp4` | ✅ the real progressive video |
| `audio/mpeg` ×4 | `youtube.com/s/search/audio/{open,success,failure,no_input}.mp3` | ❌ UI sound effects |

Those are **the same four false positives** P0 found under `ResourceContext == Media`. The naive Content-Type rule reproduces P0's failure exactly: it finds the four beeps and misses the video.

**2. YouTube's stream is not a media type at all.**

```
200  application/vnd.yt-ump   rr10---sn-0opoxu-j8we.googlevideo.com/videoplayback?expire=…
```

`vnd.yt-ump` is YouTube's proprietary UMP framing. No `Content-Length`, no `Range` request header, and the URL carries **no `mime=` and no `range=` parameter** — the itag-style parameters the older delivery used are gone. Only **two** `videoplayback` responses appeared for the whole session, i.e. the media arrives over a long-lived multiplexed connection rather than as discrete segment fetches.

This is bigger than a classification problem, and it lands on §0/§4: the audio-video split P0 observed at the MSE layer (two SourceBuffers, `audio/webm opus` + `video/mp4 av01`) is demultiplexed **client-side out of one UMP stream**.

**Confirmed with real playback.** A second pass with the video actually playing produced 9 more `videoplayback` responses — a burst of 5 in 3 s (buffer fill) then a steady top-up every 12–15 s. Every one of them `application/vnd.yt-ump`, no `Content-Length`, no `Range`, no `Content-Range`. Comparing all 12 URLs parameter by parameter:

- all 12 URLs are **distinct**, but the **only parameter that varies is `rn`** — a request counter;
- `id` is identical across all of them — one stream identifier, not one per track;
- there is **no `itag`, no `mime`, no `range`** parameter anywhere in the URL;
- the URL carries **`sabr`**.

So YouTube addresses everything through **one URL**, and track selection plus byte ranges travel in the request body (SABR), not the address. There is no audio URL and no video URL to capture — "grab the audio stream" has nothing to grab, and no amount of response-header classification will change that. (The HTTP method was not recorded by the probe; that SABR uses POST with a protobuf body is inference, not measurement.)

**3. The HLS manifest lies about being audio.**

```
200  audio/mpegurl   test-streams.mux.dev/x36xhzz/x36xhzz.m3u8          (master)
200  audio/mpegurl   …/url_8/193039199_mp4_h264_aac_fhd_7.m3u8          (variant)
```

Not `application/vnd.apple.mpegurl`, and it says **audio** for an H.264+AAC *video* stream. A classifier mapping `audio/*` → "audio track" would offer an audio-only download for a video.

**4. HLS segments are invisible, and share their type with fonts.**

```
200  application/octet-stream   …/url_591/193039199_mp4_h264_aac_fhd_7.ts   13184440
200  application/octet-stream   …/url_592/…                                  6507432
…   (591 → 601 sequential — playback genuinely ran here)
200  application/octet-stream; charset=utf-8   cdnjs.cloudflare.com/…/fa-solid-900.woff2
```

Segments answer `application/octet-stream`. So does Font Awesome. Content-Type cannot separate them.

### Decision — classification is multi-signal, and Content-Type is only one axis

The P0 exit-gate conclusion is **necessary but not sufficient**. Content-Type is authoritative for exactly one case and useless or actively misleading for the rest:

| Case | What identifies it | Content-Type's role |
|---|---|---|
| Progressive file | `video/*` (excluding known-UI audio), plus `Content-Range`/`Content-Length` | **authoritative** |
| HLS/DASH **manifest** | `*mpegurl` / `dash+xml` **and** parsing the body to confirm it is a playlist | necessary, not sufficient — the type lies about audio-vs-video |
| HLS/DASH **segment** | **being referenced by a manifest we already parsed** | none — `octet-stream` is shared with fonts |
| YouTube / UMP | page-layer MSE evidence + the `videoplayback` URL family | none — `vnd.yt-ump` is in no registry |

So the segment rule is **provenance, not headers**: a response is a segment because a manifest we fetched names it. That also settles the audio/video split for real HLS — it comes from the manifest's `EXT-X-MEDIA` / `AdaptationSet` declarations, not from any header.

**Consequence for the plan:** Gate 1's classifier takes `(url, contentType, contentLength, contentRange, manifestProvenance, pageMseEvidence)`, not `(url, contentType)`. The negative tests listed for Gate 1 now have measured strings behind them: `audio/mpegurl` must **not** classify as an audio track, `application/octet-stream` must **not** classify as media on its own, and `youtube.com/s/search/audio/open.mp3` must **not** appear in an inventory.

### Open, carried forward

- **OPEN-2** — the mix is now three sites, still small, but the shape is clearer: progressive is trivial, standards-compliant HLS is fully addressable via manifest provenance, and YouTube is its own problem.
- **OPEN-4** — now unblocked for the first time: `test-streams.mux.dev` gives real discrete `.ts` segments to test concatenation against.
- **YouTube/UMP — resolved as a measurement, open as a decision.** UMP/SABR holds under real playback, so **D-2 ("YouTube is in scope, except anything paid") and §10 ("no YouTube-specific extraction path") are now in direct conflict** and one of them has to give. Three ways out, none of them free:

  **(a) Drop YouTube from scope.** Amend D-2. The generic detector sees one opaque `vnd.yt-ump` stream from one URL and can honestly offer nothing. Consistent with §10, costs the site the request named first.

  **(b) A YouTube-specific SABR path.** Parse the request body, reconstruct per-track fetches. This is precisely the per-site treadmill §10 forbids, against a protocol Google changes at will. The ad-block precedent is the argument against: v0.1 never worked, v0.2 broke playback, v0.3 took three attempts — and that was against the DOM, which is *more* stable than a private wire protocol.

  **(c) Capture at the MSE layer instead of the network.** Hook `SourceBuffer.appendBuffer` and take the bytes the player has already demultiplexed in memory. This never touches UMP or SABR — it is generic MSE and would work on any adaptive site, YouTube included, with no per-site code. P0 already proved the hook point fires and reports the codecs per SourceBuffer.

  **(c) is unmeasured** — a design claim of the same kind V-8 was before P0 tested it, and it must be treated that way. Its known costs: the capture is real-time (you get the video no faster than you watch it), it buffers the media in page memory, and it yields only what was actually played. Its known bonus: on DRM content the buffers are encrypted, so it declines protected content by construction rather than by policy — the same place §5 already draws the line.

### Exit gate

**Not passed as originally written.** The rule this gate existed to check does not survive contact: single-signal Content-Type classification fails on three of the four cases measured. Gate 1 may start on the revised multi-signal design above; it may **not** start on the §8 rule.

---

## 10. P1 Gate 0.5 — the MSE capture claim, measured 2026-08-10

Gate 0 left option **(c)** — capture at the MSE layer instead of the network — as the only route that keeps YouTube in scope without per-site code, and explicitly flagged it as **unmeasured**. This gate measured it.

**Instrument:** `resources/scripts/media-detect.js`, injected after `webview-cloak.js`, gated on `VELO_MEDIA_PROBE` (wrapping the media path is what broke playback in YouTube ad-block v0.2, so it stays opt-in until proven inert). It wraps `MediaSource.addSourceBuffer` and `SourceBuffer.appendBuffer` and reports **structure only** — MIME strings, container box names, append counts, byte totals. No media bytes cross the bridge.

**One bug found on the way in, worth recording because it is invisible:** the script first posted its payload as an object, copying `council-bridge.js`. Nothing arrived and nothing was logged. `TryGetWebMessageAsString()` (`BrowserTab.Events.cs:380`) **throws `ArgumentException`** for a non-string message — it does not return null — and `OnWebMessageReceived` wraps its body in a `try/catch` that swallows it. Every page→host message must be `JSON.stringify`'d, as `autofill.js:28` and `glance-hover.js:24` do. `council-bridge.js` does not, which is filed separately.

### Result — the claim holds, and the MSE layer normalises what the network obfuscates

**hls.js demo** (network layer: `audio/mpegurl` manifests, `application/octet-stream` segments):

| SourceBuffer | MIME | First append | Later appends |
|---|---|---|---|
| 0 | `audio/mp4;codecs=mp4a.40.2` | `ftyp,moov` (628 B) | `moof+mdat` ×14, `ftyp+moov` ×1 |
| 1 | `video/mp4;codecs=avc1.64001f` | `ftyp,moov` (729 B) | `moof+mdat` ×14, `ftyp+moov` ×1 |

Two cleanly separated tracks, each a textbook fMP4 stream: initialisation segment followed by media segments. hls.js **transmuxes the MPEG-TS segments into fMP4 in the browser** before appending — so the bytes at this layer are standard and per-track, even though the network delivered opaque `.ts` blobs. The stray mid-stream `ftyp+moov` is a re-initialisation on a quality switch, and it is a design constraint: a capture must handle a mid-stream init change.

**YouTube** (network layer: one `videoplayback` URL, `application/vnd.yt-ump`, SABR):

| SourceBuffer | MIME | First append | Later appends |
|---|---|---|---|
| 0 | `audio/webm; codecs="opus"` | `EBML` (259 B) | `Cluster` ×3, unaligned ×40 |
| 1 | `video/mp4; codecs="av01.0.08M.08"` | `ftyp,moov` (700 B) | `moof+mdat` ×5, unaligned ×162 |

**The UMP/SABR opacity is completely bypassed.** The page receives YouTube's audio and video as two separate, labelled, unencrypted byte streams — the exact audio/video split the feature is built on, on the site the network layer could say nothing about.

The "unaligned" appends are the important nuance: YouTube feeds the SourceBuffer **arbitrary byte slices** as they arrive from the UMP stream, so an individual append does not start on a container boundary. That is not corruption — it is a continuous per-track byte stream cut at arbitrary offsets. **A capture must concatenate in append order and must never assume an append boundary is a segment boundary.**

### What this does and does not prove

**Proves:** the tracks are separable, labelled with codecs, unencrypted on free content, and arrive as a coherent per-track byte stream on both a standards-compliant HLS source and on YouTube. The audio track alone is a standalone Opus-in-WebM stream — concatenated, that is a playable audio file with **no muxer**, which is what §4 hoped for and D-1 deferred.

**Does not prove:** that a concatenation actually plays. This gate measured **structure, not validity**. Writing bytes to disk and playing the result is P2's job, and OPEN-4 stays open until it does.

**Also measured:** playback ran normally on both sites with the hooks installed (hls.js 16 appends per track, YouTube 168 video appends), so the wrapper is inert on the media path so far. That is the v2.4.53 failure mode checked, not assumed.

### Consequences for the plan

1. **The D-2 / §11 conflict dissolves.** YouTube stays in scope with **zero** YouTube-specific code — the detector is generic MSE, and it would work identically on any adaptive site. Neither decision has to give.
2. **The network layer is demoted.** After Gate 0 it looked load-bearing; it is not. Its remaining jobs are the progressive case (`video/mp4` direct fetch, which needs no MSE at all) and manifest provenance for HLS/DASH. It cannot see YouTube and no longer needs to.
3. **Capture cannot buffer in the page.** The hls.js video track alone reached **91 MB in about 40 seconds**. A full film would be gigabytes of page memory. Whatever P2 builds must stream appended bytes to the host as they arrive, chunked, not accumulate them in JS. This is a firm constraint discovered here, not a preference.
4. **Capture is real-time by construction.** You get what was played, at the speed it was played. That is a product decision to state in the UI, not a bug to fix.
5. **The DRM rule gets its correct shape.** `pssh`/`sinf` in the initialisation segment, plus `setMediaKeys` and `encrypted` events, are all recorded separately from mere `requestMediaKeySystemAccess` probes. Both free sources measured clean on all counters. The positive case still needs a protected source — that is Gate 3's YouTube-rental check.

### Exit gate

**Passed.** Option (c) is real. Gate 1 proceeds on the multi-signal classifier with MSE evidence as the primary axis for adaptive streams and Content-Type as the authority for progressive files.

---

## 11. P1 Gate 1 — the classifier and the inventory, built 2026-08-10

**Shipped:** `MediaClassifier`, `MediaPageReport`, `MediaInventory` (`src/VELO.Core/Media/`), fed from both layers by `BrowserTab`, reset on navigation, with the measurement log as its consumer. Nothing downloads. 30 new tests, 769 total.

### The classifier is three separate rules, on purpose

Splitting them is what makes them arguable individually:

- `ClassifyResponse` — normalised Content-Type → class. Manifests are matched **before** audio, because two of the four HLS types begin with `audio/`. `octet-stream` is media only with manifest provenance. `vnd.yt-ump` is deliberately not media at this layer.
- `IsUserContent` — the size floor separating content from furniture. **This is a heuristic and it is named as one**: YouTube's four UI beeps are 6.1–7.0 KB, the smallest real content measured was 788 KB, so the floor sits at 100 KB. The principled discriminator (is this attached to a media element the user can see) needs page-layer coverage that today only exists for adaptive streams.
- `IsProtected` — use, never capability. `setMediaKeys`, an `encrypted` event, or `pssh`/`sinf` in the init segment. Probing 13 key systems is not protection.

### Verification

**Unit tests (26).** Every string in them is measured, not invented — the negatives come first because three of the four failures Gate 0 found are false *positives*, which a happy-path suite goes green on. Covered: font-as-octet-stream is not media · a segment is media only by provenance · `vnd.yt-ump` is not recognisable on the network · an `.mp4` URL serving `text/html` is not media · all four HLS manifest types are manifests and not audio tracks · the four UI beeps classify as audio but not as content · `Content-Range` beats `Content-Length` for total size · the four measured SourceBuffer MIME strings yield the right track and codecs · probing is not protection, each of the four use signals is.

**The tests were run red before being trusted.** Reordering `ClassifyResponse` to test `audio/` before manifests — the exact mistake the measurement warned about — turns 2 of them red. Restored, 26 green.

**Smoke tests (4), file-scan, following `WiringSmokeTests`:**

1. Every script loaded by `LoadScriptResourceAsync` has a `<Content Include>`. Verified red by removing `media-detect.js`'s entry.
2. Every page→host `postMessage` stringifies. **This one was decorative on the first attempt** — it asked whether the file contained `JSON.stringify` anywhere, and `council-bridge.js` passed on line 177, an unrelated return value, while still posting a raw object. Now checked at the call site, which does catch it. `council-bridge.js` is parked in a documented allowlist rather than silently tolerated.
3. `media-detect.js`'s field names match `MediaPageReport.TryParse`'s, in both directions, plus the `kind` discriminator across script, parser and switch.
4. The inventory has both producers, the navigation reset, and a consumer — the failure mode lesson #21 exists for.

**Runtime, against what §9–§10 measured:**

| Site | Inventory | Matches |
|---|---|---|
| w3schools | `progressive=1 manifests=0 tracks=[]` | ✅ one `video/mp4`; the four beeps excluded by the size floor |
| hls.js demo | `manifests=3 tracks=[Audio:mp4a.40.2, Video:avc1.64001f]` | ✅ |
| YouTube | `progressive=0 manifests=0 tracks=[Audio:opus, Video:av01.0.08M.08]` | ✅ network blind, page sees both tracks |
| example.com | empty — no entry at all | ✅ the negative |

Per-tab isolation held: with several tabs open, each inventory described its own page and none leaked.

### Two bugs found on the way, both silent

- A page→host message posted as an object is **lost with no trace**. `TryGetWebMessageAsString()` throws for non-strings and `OnWebMessageReceived`'s `try/catch` swallows it. Cost an hour of "the script runs but nothing arrives". Now guarded.
- A guard that matches inside comments guards nothing. The stringify test first tripped on `webview-cloak.js`, which only *mentions* `postMessage` in prose — the same trap already documented in `Every_path_that_hands_a_tab_to_the_user`. Comments are stripped now.

### Still open, deliberately

- **The DRM rule is verified negatively only.** Free content measured clean, probing-is-not-protection is unit-tested, but no protected stream has been through it at runtime. The positive case needs a YouTube rental and stays on Gate 3's list, unticked.
- **`media-detect.js` is still gated on `VELO_MEDIA_PROBE`.** Two sites is thin evidence that wrapping the media path is inert, and v2.4.53 is the reason to be careful. Un-gating needs a wider pass.
- **Segment provenance is not implemented.** The classifier takes the flag, nothing sets it yet; manifest parsing is P2's, and the MSE layer covers adaptive streams for detection purposes today.
- **OPEN-2** — the mix is now four sites.

### Exit gate

**Passed.** Detection is real, measured against the reference set, and read-only. P2 may start.

---

## 12. P2 — the engine, and why it is two engines

### P2 as written no longer describes the problem

§7 planned one `MediaDownloader`: HttpClient, streaming to disk, progress, cancel, resume. That covers the paths where media has a URL. §10 measured that one of the three paths has **no URL at all** — YouTube's SABR transport addresses everything through a single `videoplayback` endpoint whose only varying parameter is a request counter, and the per-track bytes exist only inside the page.

So the engine splits, and the split is not a preference:

| Path | How the bytes are obtained | Engine |
|---|---|---|
| Progressive file | one HTTP GET | **P2a** — HTTP downloader |
| Standards-compliant HLS/DASH | parse manifest → one GET per segment | **P2a** + a manifest parser |
| MSE-only (YouTube, and any SABR-style site) | no URL exists; bytes arrive at `appendBuffer` | **P2b** — a capture sink over the bridge |

### P2a-1 — done 2026-08-10

`MediaDownloader` (`src/VELO.Core/Media/`). Streams one URL to disk with progress, cancel and resume. 13 tests, 782 total.

Bytes land in a sibling `.part` and are renamed into place only on success, so the destination never holds a half-written file. A cancelled transfer **keeps** its `.part` — that is the resume point — which differs from `UpdateDownloader` deliberately: an interrupted update is worthless, an interrupted film is not.

Two behaviours exist because they were measured, not because they are good practice in the abstract:

- **Browser-realistic headers.** OPEN-3 measured a public `.mp4` answering **403 with no User-Agent and 206 with one**. Every other `HttpClient` in VELO announces itself as `VELO-Browser/…` (`TLSGuard.cs:38`, `BlocklistManager.cs:88`, `SentinelModelInstaller.cs:56`, and three more) — correct for VELO's own APIs, exactly wrong here. The caller should pass `CoreWebView2Settings.UserAgent` from the tab that is playing the media; the constant is a fallback, not the intended path. Tested in both directions: a browser UA gets the file, a VELO-branded one gets 403.
- **Resume must survive a server that ignores `Range`.** Asking for `bytes=100000-` and getting `200` means the body starts at zero. Appending it produces a corrupt file **of exactly the expected length** — length checks pass, the transfer reports success, and only playback fails later on the user's machine. The downloader detects the `200` and restarts. Verified red: disabling that branch fails exactly that test and no other.

### Not done in P2a, and named rather than implied

- **Manifest parsing and multi-segment jobs.** The original P2 included both. `MediaDownloader` fetches one URL; nothing yet parses an `.m3u8`/`.mpd` to produce the segment list, and `DownloadItem` still models one URL → one file with a known `TotalBytes` (P5 in §3). That is **P2a-2**.
- **Nothing is wired to the UI or to `DownloadManager`.** The engine exists and is tested; no call-site uses it yet. That is deliberate — P3's guard lane has to exist before anything issues hundreds of segment requests, or `DownloadGuard` blocks the second one (V-6).

### P2b — needs its own gate before it is designed

**The unmeasured assumption is the same shape as the one Gate 0 caught:** that the page→host bridge can carry media-rate data without wrecking playback.

What is known: one track reached **91 MB in about 40 seconds** (~2.3 MB/s), `postMessage` carries **strings only** — a non-string message is thrown away silently (§11) — so bytes need base64, which is +33%, and every message is marshalled and parsed on the **UI thread**, through the same `OnWebMessageReceived` that runs every other feature. That is roughly 3 MB/s of string marshalling on the thread that also draws the browser. Wrapping the media path is what broke playback in YouTube ad-block v0.2.

**Gate P2b-0 must measure, before any sink is written:** actual sustained throughput of the bridge with base64 chunks; whether playback degrades while it runs; and whether an alternative channel is needed. Candidate alternatives, all unmeasured: a loopback HTTP endpoint the page POSTs to (much higher bandwidth, off the UI thread, but opens a local port on a privacy browser and needs an origin-bound token), or a host object. **No sink design is committed to until that measurement exists.**

Also carried into P2b from §10: the capture is **real-time by construction** (you get what you watch, at the speed you watch it), it must never accumulate in page memory, and it must concatenate in append order without assuming append boundaries are segment boundaries.

---

## 13. P3 — the guard lane, done 2026-08-10

### The guard had no tests

Before anything was changed: `DownloadGuard` shipped with **zero test coverage**. 273 security tests, and none of them touched the rule that blocks the second download in three seconds — the rule V-6 identified as the thing that kills this feature.

That made P3's own success criterion unmeasurable. "The drive-by burst rule stays exactly as strong for everything else" cannot be demonstrated against a baseline nobody ever wrote down. So P3 began by writing 13 characterization tests and **running them green against the unmodified guard**: the burst rule and its per-tab scoping, `ResetBurst`, cross-origin executables, same-origin warnings, the subdomain case, the no-parent-page case from v2.0.5.5, safe extensions, and `.ts` falling through as an unknown extension. They live in `DownloadGuardTests.cs`, separate from the lane tests on purpose — a characterization suite that only ever ran against the changed code proves nothing about what the change preserved.

After the lane landed, all 13 still pass unchanged. That is the claim, demonstrated rather than asserted.

### The lane

`BeginUserInitiatedJob(tabId, expectedDownloads, maxDuration?)` returns a token; `Evaluate(...)` takes it as a new optional last parameter; `EndUserInitiatedJob(token)` and `EndJobsForTab(tabId)` close it.

Four properties, each of which has a test that fails when it is removed:

- **Unforgeable.** The token is 128 bits from the CSPRNG, minted in the guard, and it never travels to the renderer — nothing a page can put in a WebMessage can name it. A forged token leaves the caller on the normal path.
- **Bound to one tab.** A token issued for tab A does nothing in tab B. Verified red by deleting the tab comparison.
- **Bounded.** A budget (clamped to 20 000) and an expiry (default 30 min, capped at 2 h). When either runs out the request is evaluated as if no lane existed. A lane with no ceiling is a permanent hole, not a lane.
- **Narrow.** It bypasses **Rule 1 only**. A cross-origin executable inside an open lane still blocks; a dangerous extension still warns. Verified red by making the lane return `Allow` outright — exactly two tests fail, and they are the two that describe the scope.

Existing callers pass no token, so their behaviour is bit-for-bit unchanged.

### One design decision worth stating

**Lane requests are not recorded in the burst tracker at all**, rather than recorded-and-ignored. The tracker measures *unrequested* downloads; counting our own job into it would make the first ordinary download after a capture look like an attack. The reason this is safe rather than a blind spot is that page-initiated downloads arriving *during* a job are still counted on their own — there is a test for exactly that, in which a drive-by interleaved with lane traffic is still blocked on its second attempt.

### Verification

27 tests (13 characterization + 14 lane), 809 total. Both directions throughout, and three separate red-checks run and restored: the lane scope, the tab binding, and — from P2a — the ignore-Range branch.

### Still open

**Nothing calls `BeginUserInitiatedJob` yet.** The lane exists and is proven; the caller arrives with P4's UI, which is where a user click first exists to justify opening one. This is the same deliberate staging as P2a: the engine and the lane are both ready before anything is allowed to issue hundreds of requests.

---

## 14. P4 — the panel and the refusal, done 2026-08-10

### Shape

A chip in the URL pill (same pattern as the TL;DR badge, `Collapsed` until there is something to show) opens a popup listing what VELO found. The chip carries the DRM verdict in its colour — green when media is available, amber when the page is playing something VELO will decline — so the refusal is visible before the panel is even opened.

**The decisions do not live in the control.** `MediaInventory.BuildOffers()` is a pure function returning the rows; `MediaPanel` only renders them. That split is the reason the DRM refusal and the "no unactionable row without a reason" rule are testable at all (lesson #55), and it is why 13 of P4's tests need no WPF.

Rules encoded in `BuildOffers`, in order:

1. **Protected content short-circuits everything.** If the page is actually using encryption, *no* row is offered — not even a progressive file that was otherwise downloadable. §5 settles that we decline rather than attempt; offering a download that yields encrypted, unplayable bytes is worse than offering nothing.
2. Progressive files are offered for real.
3. Adaptive tracks and manifests are listed **with an explicit reason**, never as a dead control. The user can see VELO found the audio and the video separately even while neither can be fetched yet.

### This is where the click finally exists

P2a's engine and P3's lane both shipped with nothing calling them, on purpose. P4 is the first place a real user action exists, so it is where the lane is opened and the downloader is called:

`Download` → save dialog → `BeginUserInitiatedJob` → `DownloadGuard.Evaluate(..., job)` → `MediaDownloader.DownloadAsync` → `EndUserInitiatedJob` in a `finally`. The downloader is handed `CoreWebView2Settings.UserAgent` from the tab, not the fallback constant — OPEN-3 measured a public `.mp4` answering 403 without a browser UA, and this is the exact string the site already served to.

### Verification

**Unit tests (13, in Core).** Protected content offers nothing while probing-only does not suppress the offers · every unactionable row carries a reason, asserted across every kind · audio and video appear as separate rows with their codecs · larger files first · navigation clears what the panel would show · filename extraction including percent-decoding and the host fallback.

**Smoke test.** Every brush in the media UI is a `DynamicResource` role token and there are no hex literals — the mechanical property that makes both themes work, which is more durable than catching it in a screenshot. Also pins the chip's `AutomationProperties`.

**Both themes, captured.** Lesson #57 says a trait must be verified in the theme it appears in, not inferred from the other one, so the panel was captured in dark *and* in light on the same page: identical content, every colour swapped correctly. Worth recording how the light pass nearly went wrong — the first attempt reported "chip not visible" and the window title said the right page, but `Get-Process VELO | Select Path` showed the process was `C:\Program Files\VELO\VELO.exe`, the installed v2.5.0. The dev build's launch had simply handed the URL off to it through the single-instance path. **Checking the window title is not checking which binary is running** — the process path is (lesson #56, new flavour).

**Runtime, driven through UI Automation** (which the chip's `AutomationProperties.AutomationId` made possible):

| Page | Chip | Panel |
|---|---|---|
| w3schools progressive | `⬇ 1`, green | `mov_bbb.mp4 · video/mp4 · 770 KB` with a live Download button |
| YouTube | `⬇ 2`, green | `Audio track · opus · 321 KB buffered` and `Video track · av01.0.08M.08 · 2.8 MB buffered`, each with its reason |
| a tab with no media | hidden | — |

Both match §9–§10 exactly: 788 493 bytes reads as 770 KB, and the two YouTube SourceBuffers are the Opus/AV1 pair measured live.

822 tests.

### Open, and none of it hidden

- **The DRM refusal has still never run against protected content.** Verified negatively (free content clean, probing-is-not-protection unit-tested) and the refusal path is unit-tested at the offer level, but no real protected stream has produced that amber chip. It needs a rental. This is now the oldest open item in the phase.
- **`media-detect.js` is still gated on `VELO_MEDIA_PROBE`.** Consequence for P4 specifically: **without the probe the panel only ever shows progressive files**, because the page layer is what sees adaptive tracks. Un-gating is the single highest-value next step for this feature, and it needs the wider playback pass §11 asked for.
- **Only progressive downloads work.** HLS needs P2a-2, MSE needs P2b. Both are named in the panel rather than implied.

---

## 15. What is deliberately not in this plan

- Any form of DRM circumvention (§5).
- Bundling ffmpeg (D-1).
- A YouTube-specific extraction path. If the generic detector cannot see it, it is out of scope — a per-site extractor is a maintenance treadmill and the ad-block history says so. **§10 removed the pressure on this rule**: the generic MSE detector sees YouTube's tracks perfectly well, so nothing site-specific is needed and this stays intact.
