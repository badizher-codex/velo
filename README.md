<img src="docs/velo-banner.png" alt="VELO Browser" width="100%"/>

# VELO — Privacy Browser for Windows

> A privacy-first Windows browser built with C# / .NET 8, WPF and Microsoft WebView2.  
> No telemetry. No tracking. Local AI threat detection.

[![Build](https://img.shields.io/badge/build-passing-brightgreen)](#building)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](#requirements)
[![License](https://img.shields.io/badge/license-AGPL--v3-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com)

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
│   ├── VELO.App/          # WPF entry point, MainWindow, DI setup
│   ├── VELO.UI/           # Controls, dialogs, themes
│   ├── VELO.Core/         # Navigation, tabs, downloads, events
│   ├── VELO.Security/     # AI engine, guards, blocklist
│   ├── VELO.DNS/          # DNS-over-HTTPS providers
│   ├── VELO.Data/         # SQLite + SQLCipher repositories
│   └── VELO.Vault/        # AES-256 password vault
└── resources/
    ├── scripts/           # Fingerprint, reader, WebRTC JS
    └── blocklists/        # Bundled tracker list
```

---

## Third-Party Credits

| Library | License |
|---|---|
| Microsoft WebView2 | [Microsoft Edge WebView2 License](https://aka.ms/webview2eula) |
| Microsoft.Extensions.DependencyInjection | MIT |
| SQLite-net-pcl | MIT |
| SQLCipher (via SQLitePCLRaw) | BSD |
| Serilog | Apache 2.0 |

---

## Contributing

Pull requests are welcome. For major changes, please open an issue first.

1. Fork the repo
2. Create your branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push and open a Pull Request

---

## License

[GNU Affero General Public License v3.0](LICENSE) © 2025 VELO Browser Contributors
