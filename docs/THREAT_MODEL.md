# VELO Threat Model

**Version:** 2.0 | **Updated:** 2026-04-18 | **Status:** Active

This document describes the assets VELO protects, the adversaries considered in our threat model, and the mitigations in place. It is a living document — updated with each major release.

---

## 1. Assets to Protect

| Asset | Sensitivity | Location |
|-------|------------|----------|
| Vault contents (passwords, TOTP secrets) | Critical | SQLCipher DB, AES-256 encrypted |
| Browsing history | High | SQLCipher DB, local only |
| Bookmarks | Medium | SQLCipher DB, local only |
| User identity vs. trackers | High | Never stored; protected in-session |
| Active sessions (cookies, auth tokens) | High | Isolated WebView2 partitions per container |
| Banking container sessions | Critical | Isolated + anti-capture + inactivity timeout |
| Binary integrity | Critical | Authenticode + SHA256 manifests |
| AI chat history | High | SQLCipher DB, retention-limited |
| AI model files (GGUF) | Low | Local disk, verified SHA256 on download |

---

## 2. Adversaries Considered

### 2.1 Malicious Website (Primary Adversary)

**Capabilities:** JavaScript execution, network requests, third-party resources, fingerprinting, phishing.

**Goals:** Track user identity across sessions, steal credentials, mine cryptocurrency, deliver malware.

**VELO Mitigations:**
- `BlocklistManager` — EasyList + EasyPrivacy + uBlock filters block tracker domains
- `TLSGuard` — HSTS preload, CT log verification, cert error detection
- `RequestGuard` — blocks known malicious request patterns
- `AISecurityEngine` — heuristic + AI analysis of suspicious content
- Fingerprint noise scripts — canvas, WebGL, AudioContext, HardwareConcurrency spoofing
- Container isolation — cookies/storage per container, no cross-container leakage
- `CookieWallBypassEngine` — rejects tracking cookies automatically
- `PasteGuardService` — detects credential phishing (domain mismatch on paste)
- Shield Score — visual indication of site trustworthiness
- Golden List — curated privacy-excellent domains

### 2.2 Network Adversary (ISP / Man-in-the-Middle)

**Capabilities:** DNS interception, TLS downgrade attempts, traffic inspection.

**Goals:** Profile user browsing, inject ads/malware, sell data.

**VELO Mitigations:**
- DoH (DNS over HTTPS) via `DoHResolver` — Cloudflare/Quad9, no plaintext DNS
- `TLSGuard` HSTS preload — forces HTTPS on ~200+ critical domains
- CT log verification — detects bogus certificates from rogue CAs
- TLS 1.3 enforcement where possible
- Post-quantum hybrid (ML-KEM/Kyber) — future-proofs against quantum decryption

### 2.3 Local Attacker (Post-Compromise on Same Machine)

**Capabilities:** File system read, memory dump, process inspection.

**Goals:** Extract stored passwords, session cookies, browsing history.

**VELO Mitigations:**
- SQLCipher encryption for all stored data (requires master password to unlock)
- `VaultCrypto` — AES-256-GCM with PBKDF2 key derivation for the password vault
- Banking container: `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` — window invisible to screenshot tools
- Inactivity timeout on Banking container — auto-closes sessions
- No plaintext credential storage anywhere in the codebase

**Accepted residual risk:** An attacker with root/admin who can dump process memory can read decrypted data while VELO is running. This is a fundamental limitation of any local application and is out of scope.

### 2.4 Dishonest VELO Developer (Supply Chain — Internal)

**Capabilities:** Code contributions, dependency additions.

**Goals:** Insert backdoors, telemetry, or malicious dependencies.

**VELO Mitigations:**
- AGPLv3 — all code is public and auditable
- SBOM generated on every release (CycloneDX)
- Code review required for all PRs to `main`
- Authenticode signing — only the designated signing identity can produce trusted binaries
- Reproducible builds documented in `docs/REPRODUCIBLE_BUILDS.md` (planned)
- All external downloads verified with SHA256 + Minisign signatures

### 2.5 Compromised Dependency (Supply Chain — External)

**Capabilities:** Malicious update to a NuGet/npm package.

**Goals:** Data exfiltration, backdoor execution.

**VELO Mitigations:**
- Dependabot / Snyk alerts on known CVEs
- SBOM allows rapid identification of affected versions
- External AI (Claude API) is opt-in only — disabled by default
- All downloaded assets (models, blocklists) verified before use

---

## 3. Adversaries Explicitly Out of Scope

| Adversary | Why out of scope |
|-----------|-----------------|
| Nation-state with physical device access | Requires OS-level or hardware-level controls |
| Attacker with pre-existing root/admin | Cannot protect against privileged local access |
| Practical quantum attacks against TLS | Partially mitigated by ML-KEM hybrid; full post-quantum TLS depends on ecosystem |
| WebView2/Chromium CVEs | Reported to Microsoft; VELO uses latest stable WebView2 |

---

## 4. Known Accepted Risks

| Risk | Severity | Mitigation | Status |
|------|----------|------------|--------|
| AI content can be used to attempt prompt injection | Medium | `AgentContentSanitizer`, system prompt hardening, action sandbox with user confirmation | Mitigated |
| WebView2 shares Chromium attack surface | High | VELO applies defense-in-depth layers on top; users should keep Windows updated | Accepted |
| Golden List maintained by a single maintainer | Low | Policy documented in `docs/golden_list_policy.md`; PRs welcome; Minisign-verified updates | Accepted |
| LLamaSharp GGUF model integrity | Medium | SHA256 + Minisign verification on download | Mitigated |

---

## 5. Trust Boundaries

```
[Internet] ──→ [WebView2 / Chromium sandbox]
                    │
                    │ (message bridge, sanitized)
                    ↓
[VELO C# process] ──→ [SQLCipher DB]
                    │
                    │ (no external calls without user consent)
                    ↓
[Local AI model / Ollama] (optional, 100% local by default)
```

- Content from the internet is **never trusted** as instructions
- The AI agent operates in a read-only context by default; actions require explicit user confirmation
- No telemetry, no analytics, no crash reports — zero data leaves the device automatically

---

## 6. Privacy Guarantees

VELO makes the following explicit guarantees:

1. **No telemetry** — not even anonymized crash reports are sent without explicit opt-in
2. **AI is local by default** — Ollama or LLamaSharp; Claude API is opt-in and clearly labeled
3. **No cloud sync** — all data stays on the device (Fase 3 may add opt-in E2E encrypted sync)
4. **History is local** — never exported without user action
5. **Golden List updates are verified** — Minisign signature checked before applying

---

*This threat model was last reviewed: 2026-04-18. Next scheduled review: 2026-10-18.*
