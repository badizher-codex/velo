# ─────────────────────────────────────────────────────────────────────────────
# VELO — Create GitHub Issues from Roadmap v2.1
# Usage:
#   $env:GITHUB_TOKEN = "ghp_xxxx"   # GitHub Personal Access Token (repo scope)
#   .\scripts\create-github-issues.ps1
# ─────────────────────────────────────────────────────────────────────────────

param(
    [string]$Token   = $env:GITHUB_TOKEN,
    [string]$Repo    = "badizher-codex/velo"
)

if (-not $Token) {
    Write-Error "Set `$env:GITHUB_TOKEN or pass -Token <ghp_...>"
    exit 1
}

$headers = @{
    Authorization = "Bearer $Token"
    Accept        = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}
$base = "https://api.github.com/repos/$Repo/issues"

function New-Issue($title, $body, $labels) {
    $payload = @{ title = $title; body = $body; labels = $labels } | ConvertTo-Json -Depth 5
    try {
        $r = Invoke-RestMethod -Uri $base -Method Post -Headers $headers -Body $payload -ContentType "application/json"
        Write-Host "✅ #$($r.number) — $title" -ForegroundColor Green
    } catch {
        Write-Host "❌ Failed: $title — $($_.Exception.Message)" -ForegroundColor Red
    }
    Start-Sleep -Milliseconds 500   # avoid rate-limit
}

# ── v2.1 — Polish & Usability ────────────────────────────────────────────────

New-Issue `
    "feat: auto-update — silent background update checker" `
    "## Summary
VELO should check for new releases automatically and notify the user.

## Implementation
- Poll GitHub Releases API on startup (max once per 24 h)
- Compare current ``AssemblyVersion`` against latest release tag
- Download installer in background, verify SHA256 from ``SHA256SUMS.txt``
- Prompt user with non-blocking banner; never block startup

## Acceptance criteria
- [ ] Update check on startup, at most once per 24 h
- [ ] User can disable in Settings → General
- [ ] SHA256 verified before offering install
- [ ] Silent on startup — only show banner if update available" `
    @("enhancement", "v2.1")

New-Issue `
    "feat: import bookmarks and history from Chrome / Firefox" `
    "## Summary
Allow users to migrate from Chrome or Firefox with one click.

## Implementation
- Chrome: read ``%LOCALAPPDATA%\Google\Chrome\User Data\Default\Bookmarks`` (JSON) and ``History`` (SQLite)
- Firefox: read ``%APPDATA%\Mozilla\Firefox\Profiles\*\places.sqlite``
- Import wizard UI: select browser → preview → import

## Acceptance criteria
- [ ] Detects installed Chrome / Firefox profiles automatically
- [ ] Imports bookmarks preserving folder structure
- [ ] Imports history (URL + title + last visit date)
- [ ] Handles duplicate URLs gracefully (skip or merge)" `
    @("enhancement", "v2.1")

New-Issue `
    "feat: password autofill — inject credentials on matching domains" `
    "## Summary
When the user navigates to a site that has saved credentials in the Vault, VELO should offer to autofill the login form.

## Implementation
- Match current URL domain against Vault entries
- Inject credentials via WebView2 ``ExecuteScriptAsync``
- Show a subtle banner (not a blocking popup)
- Respect user setting to disable autofill globally or per-site

## Acceptance criteria
- [ ] Autofill banner appears on matching login pages
- [ ] Credentials injected only after user confirms
- [ ] Works on most major sites (Gmail, GitHub, etc.)
- [ ] User can disable per-site or globally" `
    @("enhancement", "v2.1")

New-Issue `
    "feat: password breach check via HaveIBeenPwned k-anonymity API" `
    "## Summary
Warn users when a saved password appears in a known data breach, using the HIBP k-anonymity model (no full password ever leaves the device).

## Implementation
- SHA-1 hash the password
- Send first 5 chars to ``https://api.pwnedpasswords.com/range/{prefix}``
- Compare remaining hash suffix locally
- Show warning badge in Vault UI if breached

## Acceptance criteria
- [ ] Only the first 5 chars of the SHA-1 are sent (k-anonymity preserved)
- [ ] Warning shown in Vault list next to breached entry
- [ ] Check runs on-demand (button) — not automatic on every unlock
- [ ] Works offline gracefully (skip check, no error shown)" `
    @("enhancement", "v2.1")

New-Issue `
    "feat: tab groups — color-coded clusters in the sidebar" `
    "## Summary
Allow users to group related tabs with a color label so the sidebar stays organized.

## Acceptance criteria
- [ ] Right-click tab → Add to group / New group
- [ ] Groups shown as collapsible sections in sidebar
- [ ] Each group has a user-chosen color and optional name
- [ ] Groups persist across sessions" `
    @("enhancement", "v2.1")

New-Issue `
    "feat: session restore — reopen tabs after crash or restart" `
    "## Summary
VELO should restore the previous session (all open tabs) after a crash or when the user chooses 'Continue where I left off'.

## Implementation
- Serialize open tab URLs to ``%AppData%\VELO\session.json`` on every tab change
- On startup, detect unclean exit and offer restore
- Setting: always restore / ask / never

## Acceptance criteria
- [ ] Detects unclean shutdown and offers to restore
- [ ] All tabs from previous session restored in correct order
- [ ] User can choose restore behavior in Settings → Startup" `
    @("enhancement", "v2.1")

New-Issue `
    "feat: custom DoH provider — free-text resolver URL in Settings" `
    "## Summary
Currently the DoH provider is limited to a preset list. Power users should be able to enter any RFC 8484-compliant DoH endpoint.

## Acceptance criteria
- [ ] Free-text field in Settings → Network → DNS-over-HTTPS
- [ ] Validates URL format before saving
- [ ] Tests the endpoint with a sample query and shows latency
- [ ] Falls back to system DNS if custom endpoint fails" `
    @("enhancement", "v2.1")

New-Issue `
    "fix: replace short AGPLv3 header with complete license text" `
    "## Summary
The ``LICENSE`` file currently only contains the short AGPLv3 notice header (17 lines). It should contain the full GNU Affero General Public License v3.0 text as required by the FSF.

## Fix
Replace ``LICENSE`` with the complete text from https://www.gnu.org/licenses/agpl-3.0.txt

## Acceptance criteria
- [ ] LICENSE file contains the full AGPLv3 text (~700 lines)
- [ ] GitHub license detection shows 'AGPL-3.0'" `
    @("bug", "documentation", "good first issue")

# ── v2.2 — Security Hardening ────────────────────────────────────────────────

New-Issue `
    "feat: HTTPS-only mode — block or warn on plaintext HTTP navigation" `
    "## Summary
Add an HTTPS-only mode that either blocks HTTP navigation outright or upgrades to HTTPS automatically.

## Acceptance criteria
- [ ] Toggle in Settings → Privacy → HTTPS-only mode
- [ ] Attempts HTTPS upgrade first (307-style redirect)
- [ ] Shows interstitial warning if HTTPS is unavailable
- [ ] Per-site exception list" `
    @("enhancement", "v2.2", "privacy")

New-Issue `
    "feat: site-specific privacy exceptions — whitelist trusted domains" `
    "## Summary
Allow users to whitelist specific domains where privacy features (WebRTC, fingerprint noise, tracker blocking) should be relaxed (e.g. video-conferencing sites that need WebRTC).

## Acceptance criteria
- [ ] Exceptions manager UI in Settings → Privacy → Site exceptions
- [ ] Per-site toggles: tracker blocking, fingerprint noise, WebRTC
- [ ] Exceptions survive browser restart
- [ ] Easy to add from the address bar (padlock menu)" `
    @("enhancement", "v2.2", "privacy")

New-Issue `
    "feat: formal threat model document" `
    "## Summary
Document the security threat model for VELO: assets, threat actors, attack vectors, mitigations, and residual risks.

## Scope
- Password Vault (local encryption, unlock flow)
- WebView2 renderer isolation
- DNS-over-HTTPS (resolver trust)
- VeloAgent sandboxed actions
- Tracker blocker bypass scenarios

## Acceptance criteria
- [ ] Published as ``docs/THREAT_MODEL.md``
- [ ] Linked from README and landing page Security section" `
    @("documentation", "security", "v2.2")

# ── v2.3 — Sync & Portability ────────────────────────────────────────────────

New-Issue `
    "feat: import passwords from CSV (1Password / Bitwarden / LastPass format)" `
    "## Summary
Allow users to import saved passwords from exported CSVs of popular password managers.

## Supported formats
- 1Password (CSV export)
- Bitwarden (JSON/CSV export)
- LastPass (CSV export)
- Chrome passwords CSV

## Acceptance criteria
- [ ] Import wizard with format selector
- [ ] Duplicate detection (skip or overwrite)
- [ ] Preview before committing
- [ ] Imported entries encrypted same as native Vault entries" `
    @("enhancement", "v2.3")

New-Issue `
    "feat: export vault to encrypted JSON or CSV" `
    "## Summary
Allow users to export their saved passwords for backup or migration to another password manager.

## Acceptance criteria
- [ ] Export to encrypted JSON (AES-256, user-chosen password)
- [ ] Export to plain CSV (with warning about sensitivity)
- [ ] Export confirmation dialog with security warning
- [ ] Exported file includes all fields: site, username, password, notes" `
    @("enhancement", "v2.3")

New-Issue `
    "feat: in-app UI localization (ES / FR / DE / PT)" `
    "## Summary
The landing page is already localized in 5 languages. The browser UI itself (menus, settings, dialogs) should support the same languages.

## Implementation
- Use .NET resource files (``*.resx``) for string externalization
- Detect system locale; allow manual override in Settings → Language
- Priority: Spanish (ES), then French, German, Portuguese

## Acceptance criteria
- [ ] All UI strings externalized to .resx files
- [ ] Spanish translation complete
- [ ] Language selector in Settings → General
- [ ] RTL layout not required (v1 scope: LTR languages only)" `
    @("enhancement", "v2.3", "i18n")

New-Issue `
    "feat: reading list — save pages to read later (offline)" `
    "## Summary
Add a 'Save to reading list' action that saves the page content locally for offline reading.

## Acceptance criteria
- [ ] Save button in toolbar / right-click context menu
- [ ] Saved pages accessible from sidebar panel
- [ ] Pages readable offline (HTML snapshot or reader-mode extraction)
- [ ] Reading list survives browser restart" `
    @("enhancement", "backlog")

Write-Host ""
Write-Host "Done! All issues created at https://github.com/$Repo/issues" -ForegroundColor Cyan
