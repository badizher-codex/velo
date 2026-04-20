# Privacy Policy — VELO Browser

**Last updated: 2026-04-19**

VELO Browser is built on a single principle: your data stays on your device.
This document explains exactly what VELO does and does not do with your information.

---

## 1. Data we collect

**None.**

VELO does not collect, transmit, or share any personal data, browsing history,
search queries, crash reports, or usage statistics.
There are no analytics SDKs, no telemetry endpoints, and no background beacons of any kind.

---

## 2. Data stored locally

All data VELO stores lives exclusively on your device, in one of two locations:

| Mode | Location |
|------|----------|
| Installed | `%LocalAppData%\VELO\` |
| Portable  | `<exe folder>\userdata\` |

The following is stored locally:

| What | Why |
|------|-----|
| Browsing history | Powers the NewTab top-sites mosaic and Command Bar search |
| Bookmarks | Saved by you, readable only by you |
| Passwords (encrypted) | AES-256 encryption via the VELO Vault; never stored in plaintext |
| Per-site security cache | Avoids re-evaluating the same domain on every visit |
| Blocklist cache | Downloaded filter lists stored for offline use |
| Application settings | Your preferences (theme, search engine, container config, etc.) |
| Log files | Diagnostic logs rotated daily, kept for 7 days, never transmitted |

You can delete all local data at any time from **Settings → Clear Browsing Data**
or by deleting the data folder manually.

---

## 3. Network requests made by VELO itself

VELO makes the following outbound requests on your behalf — all are opt-out or user-initiated:

| Request | When | Can be disabled |
|---------|------|-----------------|
| DNS-over-HTTPS (Quad9) | Every navigation | Yes — Settings → DNS |
| Blocklist updates (EasyList, uBlock) | Once at startup, then weekly | Yes — Settings → Privacy |
| GoldenList update | Once at startup, then weekly | Yes — Settings → Privacy |
| GitHub Releases check | 3 min after startup, then every 24 h | Yes — Settings → Updates |
| VeloAgent (Ollama) | Only if you open the Agent panel and Ollama is running locally | Yes — leave Agent panel closed |

No request ever includes identifiers, tokens, or browsing data.
The update checker only sends your current version number in the User-Agent header
(`VELO-Browser/2.0.0 UpdateChecker`) — standard HTTP practice, contains no PII.

---

## 4. AI features

### VeloAgent (local)
When using VeloAgent with **LLamaSharp** or **Ollama**, all inference runs entirely
on your machine. No text, prompt, or page content is ever sent to an external server.

### AI Security Engine
Page classification uses the same local adapters by default.
The `OfflineAdapter` (default) uses heuristic rules only — zero network calls.

---

## 5. WebView2 (Chromium engine)

VELO uses Microsoft Edge WebView2 as its rendering engine.
WebView2 inherits some Chromium behaviors (e.g., Safe Browsing, OCSP checks).
These can be controlled via the WebView2 profile settings inside VELO.

Microsoft's privacy statement for WebView2:
https://privacy.microsoft.com/privacystatement

---

## 6. Third-party services

VELO does not integrate with any third-party advertising, analytics, or tracking service.

Filter lists are downloaded from:
- https://easylist.to (EasyList / EasyPrivacy)
- https://github.com/uBlockOrigin/uAssets (uBlock Origin filters)
- https://pgl.yoyo.org (Peter Lowe's ad server list)

These requests are anonymous HTTP GETs with no identifying information.

---

## 7. Children's privacy

VELO does not knowingly collect any information from anyone, including children.
Since no data is collected at all, COPPA and similar regulations are satisfied by design.

---

## 8. Changes to this policy

If this policy changes in a material way, the change will be noted in the
[CHANGELOG](CHANGELOG.md) and in the GitHub release notes.
The "Last updated" date at the top of this file will always reflect the most recent revision.

---

## 9. Contact

Questions or concerns about privacy in VELO?

- Open an issue: https://github.com/badizher-codex/velo/issues
- Email the maintainer: badizher@gmail.com

---

> VELO is free and open-source software licensed under the
> [GNU Affero General Public License v3.0](LICENSE).
> You are free to audit every line of code that handles your data.
