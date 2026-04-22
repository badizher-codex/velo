# VELO Browser — Roadmap

> This document outlines the planned development direction for VELO. Items are subject to change based on community feedback and contributor availability.

## ✅ Released — v2.0.0 (April 2026)

- Built-in tracker & ad blocker (250+ domains, EasyPrivacy auto-update)
- Fingerprint noise injection (Canvas, WebGL, AudioContext, hardware)
- WebRTC leak prevention (force relay-only or full disable)
- DNS-over-HTTPS with multiple providers (Cloudflare, Quad9, NextDNS)
- AES-256 encrypted Password Vault
- VeloAgent AI assistant (Ollama + LM Studio, local models, multi-turn history)
- Malwaredex URL scanner (golden list, real-time check)
- Security Inspector panel (guards status at a glance)
- Portable mode (run from USB, no registry writes)
- Inno Setup installer (Windows 10/11 x64)
- Authenticode signing via SignPath Foundation
- GitHub Pages landing page (5 languages: EN/ES/FR/DE/PT)
- SBOM (CycloneDX JSON + XML) on every release

---

## 🚧 v2.1 — Polish & Usability (Q2 2026)

_Focus: make existing features feel finished and reduce friction for new users._

| Feature | Priority | Notes |
|---|---|---|
| **Auto-update** — silent background update checker | 🔴 High | Check GitHub Releases API, prompt user, download + verify SHA256 |
| **Import from Chrome/Firefox** — bookmarks + history | 🔴 High | Read Chrome `Bookmarks` JSON + `History` SQLite; Firefox `places.sqlite` |
| **Password autofill** — inject credentials on page load | 🔴 High | WebView2 script injection + match by domain |
| **Password breach check** — HaveIBeenPwned k-anonymity | 🟡 Medium | SHA-1 first 5 chars sent, compare locally |
| **Tab groups** — color-coded tab clusters | 🟡 Medium | Visual grouping in sidebar |
| **Session restore** — reopen tabs after crash | 🟡 Medium | Persist tab list to JSON on change |
| **Custom DoH provider** — user-defined resolver URL | 🟡 Medium | Free-text field in Settings → Network |
| **Full LICENSE text** — complete AGPLv3 in repo | 🟢 Low | Currently only the short header |
| **App icon polish** — higher res ico + taskbar badge | 🟢 Low | 256×256 PNG in ICO |

---

## 🔮 v2.2 — Security Hardening (Q3 2026)

_Focus: turn VELO into a credible privacy tool for security-conscious users._

| Feature | Priority | Notes |
|---|---|---|
| **Content Security Policy manager** — per-site CSP viewer/editor | 🔴 High | |
| **HTTPS-only mode** — block plaintext HTTP | 🔴 High | |
| **Certificate transparency viewer** — inspect cert chain | 🟡 Medium | |
| **Sandbox per tab** — isolate renderer processes | 🟡 Medium | WebView2 multi-profile approach |
| **Site-specific privacy exceptions** — whitelist domains | 🟡 Medium | e.g. allow WebRTC for video calls |
| **Threat model document** — formal security analysis | 🟡 Medium | Published in docs/ |
| **Security audit** — community code review | 🟢 Low | Issue open for volunteers |

---

## 🌐 v2.3 — Sync & Portability (Q4 2026)

_Focus: multi-device use without sacrificing privacy._

| Feature | Priority | Notes |
|---|---|---|
| **E2E encrypted sync** — bookmarks + vault (self-hosted) | 🔴 High | AES-256 + user-held key; no cloud dependency |
| **Import passwords from CSV** — 1Password, Bitwarden, LastPass format | 🔴 High | |
| **Export vault to CSV / encrypted JSON** | 🟡 Medium | |
| **Firefox/Chrome extension compatibility** — basic WebExtensions support | 🟡 Medium | Depends on WebView2 roadmap |
| **Localization** — in-app UI strings (not just landing page) | 🟡 Medium | ES, FR, DE, PT |

---

## 🤖 v3.0 — AI-First Browser (2027)

_Focus: VeloAgent becomes a first-class browser feature, not an addon._

| Feature | Priority | Notes |
|---|---|---|
| **VeloAgent page summarization** — one-click summary of any page | 🔴 High | |
| **VeloAgent form fill** — AI understands form context | 🔴 High | |
| **On-device threat detection** — classify phishing/malware with local model | 🔴 High | |
| **Smart blocklist** — AI-assisted tracker pattern detection | 🟡 Medium | |
| **Reading mode + AI annotations** | 🟡 Medium | |
| **Voice input** — Whisper-based local STT | 🟢 Low | |

---

## 💡 Community Ideas (Backlog — no ETA)

- Split-screen / side-by-side tabs
- Picture-in-picture video always-on-top
- Pomodoro / focus timer integration
- Built-in Tor routing (optional)
- Android companion app (sync only)

---

## How to contribute

See [CONTRIBUTING.md](CONTRIBUTING.md) · Open issues on [GitHub Issues](https://github.com/badizher-codex/velo/issues) · PRs welcome.
