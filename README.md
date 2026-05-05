# VELO — Privacy Browser for Windows

**[🌐 Website](https://badizher-codex.github.io/velo) · [⬇️ Download](https://github.com/badizher-codex/velo/releases/latest)**

> A privacy-first Windows browser built with C# / .NET 8, WPF and Microsoft WebView2.  
> No telemetry. No tracking. Local AI threat detection.

[![Build](https://img.shields.io/badge/build-passing-brightgreen)](#building)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](#requirements)
[![License](https://img.shields.io/badge/license-AGPL--v3-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/tests-94%20passing-brightgreen)](#testing)
[![i18n](https://img.shields.io/badge/i18n-8%20languages-blue)](#languages)

---

## What's new in v2.1.x (Phase 3 — in progress)

Phase 3 turns VELO from "privacy browser" into "daily-driver privacy browser".
Five sprints landed so far; two remain.

| Sprint | Version | Feature |
|--------|---------|---------|
| 1 | `v2.1.0` | **Threats Panel v3** — live per-tab block list grouped by host, with on-demand AI explanations. **Context Menu IA** — right-click submenu with 20 AI actions across text, link, image and page contexts (Explain, Summarise, Translate, Fact-check, ELI5, Extract links/emails/phones, Describe image, OCR, Deep search…). |
| 2 | `v2.1.1` | **Secure auto-update** — downloads + verifies the next installer against the SHA256SUMS.txt published with each release, then runs Inno Setup silent flags. No Authenticode required. |
| 3 | `v2.1.2` | **Session restore + crash recovery** — 30-second snapshots of open tabs; restores cleanly after a normal close or unexpectedly after a crash. Banking + temporal containers excluded by design. Skipped entirely in Paranoid / Bunker security modes. |
| 4 | `v2.1.3` | **Import from Chrome / Edge / Brave / Vivaldi / Opera / Firefox** — bookmarks (folder tree preserved), history (last 90 days), passwords via DPAPI v10 + AES-GCM. Deduplicates against the existing Vault. |
| 4-polish | `v2.1.4` | Password import dedup, restore cap at 30 tabs, diff-aware heartbeat (skip session.json write when nothing changed). |

Sprints 5–7 in progress: password autofill + HIBP breach check, contextual VeloAgent v2 actions, MainWindow refactor + WebView2 integration tests.

---

## Features

### Privacy
- **Fingerprint Protection** — Canvas, WebGL, AudioContext and hardware noise injection
- **WebRTC Leak Prevention** — Force relay-only or disable WebRTC entirely
- **Tracker Blocker** — 250+ bundled tracker domains, auto-updates from EasyPrivacy
- **DNS-over-HTTPS** — Quad9, Cloudflare, NextDNS or custom endpoint
- **Identity Containers** — Isolate browsing sessions per context

### Security
- **AI Threat Detection** — Local LLM (Ollama/qwen3), Claude API, or offline heuristics
- **TLSGuard** — HSTS preload enforcement + Certificate Transparency log verification
- **DownloadGuard** — Blocks drive-by downloads, executable burst attacks, cross-origin executables
- **PopupGuard** — Stops script-initiated popup storms and malicious tab redirects
- **NavGuard** — Real-time navigation blocking for suspicious/malicious domains
- **Malwaredex** — Collectable threat tracker with 43 threat types across 6 categories

### Browser
- **Reader Mode** (F9) — Clean article view, no ads or clutter
- **Password Vault** — AES-256 encrypted, master password protected
- **Bookmarks, History, Downloads** — Full browser feature set
- **Find in Page** (Ctrl+F)
- **Zoom controls** (Ctrl +/-)
- **Dark theme** — Easy on the eyes, always
- **Session restore** — Tabs survive restarts and crashes (Phase 3)
- **Browser import** — One-click migration from Chrome/Edge/Firefox (Phase 3)
- **Workspaces + tear-off windows** — Vertical sidebar with per-workspace last-active tab memory

### AI assistant (VeloAgent)
- **Local-first** — LLamaSharp embedded or Ollama at `http://localhost:11434`. Cloud (Claude API) is opt-in and tagged with an amber "🌐 left your device" indicator.
- **Right-click → 🤖 IA** — 20+ context actions: explain selection, summarise page, translate, fact-check (with non-advice disclaimer), define, ELI5, extract links/emails/phones, describe image, OCR.
- **Threat explanations** — Click "Explicar" on any blocked request in the Threats Panel to get a 2-3 sentence rationale from the configured model. Falls back to a curated static template when the LLM is offline or slow (3-second timeout).

---

## Requirements

| Requirement | Version |
|---|---|
| Windows | 10 (1903+) or 11 |
| .NET SDK | 8.0+ |
| Microsoft Edge WebView2 Runtime | Latest |
| RAM | 4 GB minimum, 8 GB recommended |

---

## Building

```powershell
# Clone the repository
git clone https://github.com/badizher-codex/velo.git
cd velo

# Restore & build (x64 required)
dotnet build src/VELO.App/VELO.App.csproj -c Release -p:Platform=x64

# Run
.\src\VELO.App\bin\x64\Release\net8.0-windows\VELO.exe
```

---

## Optional: Local AI (Ollama)

VELO can use a local LLM for real-time threat analysis with zero data leaving your machine:

```powershell
# Install Ollama
# https://ollama.com/download

# Pull a model (choose based on your RAM)
ollama pull qwen3:8b      # 8 GB RAM
ollama pull llama3.2:3b   # 4 GB RAM

# Start the server
ollama serve
```

Then in VELO → Settings → AI → LLM Personalizado → `http://localhost:11434` / `qwen3:8b`

---

## Architecture

```
VELO/
├── src/
│   ├── VELO.App/                # WPF entry point, MainWindow, DI setup, App.xaml
│   ├── VELO.UI/                 # Controls (TabSidebar, UrlBar, BrowserTab,
│   │                            #   SecurityPanel, ThreatsPanelV2, VeloAgentPanel),
│   │                            #   dialogs (Settings, Vault, Onboarding, …),
│   │                            #   themes, converters
│   ├── VELO.Core/               # Navigation, tabs, downloads, events,
│   │   ├── Sessions/            #   session snapshot + restore
│   │   ├── Updates/             #   UpdateDownloader + UpdateInfo (v2.1.1+)
│   │   └── Localization/        #   8-language string table
│   ├── VELO.Security/           # AI engine, request/download/TLS guards, blocklist,
│   │   └── Threats/             #   BlockEntry/BlockGroup/ViewModel + ExplanationService
│   ├── VELO.Agent/              # Conversation orchestrator + LLM adapters
│   │                            #   (LLamaSharp / Ollama / Claude) + AIContextActions
│   ├── VELO.Import/             # Chrome/Edge/Brave/Vivaldi/Opera/Firefox importers
│   │                            #   (bookmarks JSON, places.sqlite, DPAPI v10 passwords)
│   ├── VELO.DNS/                # DNS-over-HTTPS providers
│   ├── VELO.Data/               # SQLite + SQLCipher repositories + AppSettings
│   └── VELO.Vault/              # AES-256 password vault
├── resources/
│   ├── scripts/                 # Fingerprint, reader, WebRTC JS
│   └── blocklists/              # Bundled tracker list (golden_list.json + EasyPrivacy)
├── tests/
│   ├── VELO.Core.Tests/         # 23 tests
│   ├── VELO.Security.Tests/     # 36 tests
│   ├── VELO.Agent.Tests/        # 27 tests
│   └── VELO.Import.Tests/       # 8 tests
└── docs/
    ├── Phase2/                  # Phase-2 spec (closed at v2.0.5.12)
    ├── Phase3/                  # Phase-3 spec (current — sprints 1-4 shipped)
    ├── THREAT_MODEL.md          # Adversaries, mitigations, residual risks
    └── PRIVACY.md               # Outbound-connection policy
```

### Outbound connections — full list

VELO is built around the rule "no data leaves your device without an explicit user
action." The exhaustive set of network calls VELO makes:

| Caller | Destination | Trigger | Setting |
|--------|-------------|---------|---------|
| `UpdateChecker` | `api.github.com` | Every 24 h | Opt-in: `updates.auto_check` (off by default) |
| `BlocklistManager` | EasyPrivacy CDN | Weekly | Off when `Settings.BlocklistsAutoUpdate = false` |
| `GoldenListService` | VELO release CDN | Weekly | Off when `Settings.BlocklistsAutoUpdate = false` |
| `IDoHProvider` | Quad9 / Cloudflare / NextDNS / custom | DNS lookups for navigation | `Settings.DnsProvider` |
| `ClaudeAdapter` | `api.anthropic.com` | AI threat analysis / context-menu IA | Opt-in: `Settings.AiMode = "Claude"` (Offline by default) |
| `OllamaAdapter` | User's local Ollama (`http://localhost:11434`) | AI threat analysis / context-menu IA | Opt-in: `Settings.AiMode = "Custom"` |
| WebView2 | Whatever sites the user visits | Navigation | — |

Everything else is local. No analytics, no crash reports, no "anonymous usage data".

---

## Testing

Run the full suite (94 tests across 4 projects):

```powershell
dotnet test
```

Per-project:

```powershell
dotnet test tests/VELO.Core.Tests/VELO.Core.Tests.csproj
dotnet test tests/VELO.Security.Tests/VELO.Security.Tests.csproj
dotnet test tests/VELO.Agent.Tests/VELO.Agent.Tests.csproj
dotnet test tests/VELO.Import.Tests/VELO.Import.Tests.csproj
```

VELO.UI and VELO.App don't currently have direct test projects — UI logic that
needs WPF runtime is integration-tested in Sprint 7 (planned). Pure logic from
those layers is moved into `VELO.Core` so the test projects can reach it
without loading WPF.

---

## Languages

In-app UI is fully localised in 8 languages: **Spanish, English, Portuguese,
French, German, Chinese (Simplified), Russian, Japanese**. Switch language in
Settings → 🌍 Idioma — every panel updates live (Settings, Onboarding,
Vault, Threats Panel, Context Menu IA, find bar, downloads, history badges).

The Inno Setup installer ships in 7 of those 8 (Chinese requires the
unofficial Inno Setup language pack and is omitted to keep the build CI-only).

---

## Third-Party Credits

| Library | License | Used for |
|---------|---------|----------|
| **Microsoft WebView2** | [Microsoft Edge WebView2 License](https://aka.ms/webview2eula) | Browser engine |
| **Microsoft.Extensions.DependencyInjection** | MIT | DI container |
| **Microsoft.Extensions.Logging** | MIT | Logging abstraction |
| **Serilog** + Serilog.Sinks.File | Apache 2.0 | File logging |
| **sqlite-net-pcl** | MIT | SQLite ORM |
| **SQLCipher** (via `SQLitePCLRaw.bundle_sqlcipher`) | BSD | Encrypted SQLite for Vault |
| **SQLitePCLRaw.bundle_e_sqlite3** | Apache 2.0 | Plain SQLite for browser-import temp DB |
| **System.Security.Cryptography.ProtectedData** | MIT | DPAPI wrapper (Chromium password import) |
| **LLamaSharp** + Backend.Cpu / Backend.Cuda12 | MIT | Embedded local LLM (VeloAgent) |
| **Anthropic.SDK** | MIT | Optional Claude integration (off by default) |
| **System.Text.Json** | MIT | All JSON parsing |

VELO ships compiled assemblies of the above plus the WebView2 native runtime.
None of these dependencies phone home; LLamaSharp / Ollama / Claude are only
contacted when the user explicitly activates AI mode.

---

## Contributing

Pull requests are welcome. For major changes, please open an issue first.

1. Fork the repo
2. Create your branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push and open a Pull Request

---

## License

[GNU Affero General Public License v3.0](LICENSE) © 2025-2026 VELO Browser Contributors

AGPL is intentional: any modified version run as a network service must publish
source. VELO is meant to stay end-user-installable forever; if anyone wants to
ship a fork as SaaS they have to share their changes back.

---

## Roadmap

- ✅ Phase 1 (`v1.0.0`) — base browser, security guards, Vault
- ✅ Phase 2 (`v2.0.x`) — AI-driven threat detection, containers, workspaces, Malwaredex
- 🟡 Phase 3 (`v2.1.x`) — Threats Panel v3 + Context Menu IA, secure auto-update,
  session restore, browser import; **password autofill + HIBP, contextual VeloAgent v2,
  refactor + integration tests** still pending
- ⏳ Phase 4 (`v2.2.0+`) — sync via Bitwarden API or similar (TBD); reading list

The full Phase 3 spec is in [docs/Phase3/VELO_FASE3_v1_DOCUMENTACION.md](docs/Phase3/VELO_FASE3_v1_DOCUMENTACION.md).
