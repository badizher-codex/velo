# ─────────────────────────────────────────────────────────────────────────────
# VELO — Create GitHub Issues from Roadmap v2.1
# Usage:
#   $env:GITHUB_TOKEN = "ghp_xxxx"
#   .\scripts\create-github-issues.ps1
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$Repo  = "badizher-codex/velo"
)
if (-not $Token) { Write-Error "Set GITHUB_TOKEN first"; exit 1 }

$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}
$base = "https://api.github.com/repos/$Repo/issues"

function New-Issue {
    param([string]$Title, [string]$Body, [string[]]$Labels)
    $payload = @{ title = $Title; body = $Body; labels = $Labels } | ConvertTo-Json -Depth 5
    try {
        $r = Invoke-RestMethod -Uri $base -Method Post -Headers $headers `
             -Body $payload -ContentType "application/json"
        Write-Host "OK  #$($r.number) $Title" -ForegroundColor Green
    } catch {
        Write-Host "ERR $Title  ->  $($_.Exception.Message)" -ForegroundColor Red
    }
    Start-Sleep -Milliseconds 600
}

# ── 1 ────────────────────────────────────────────────────────────────────────
$b1 = @'
## Summary
VELO should check for new releases automatically and notify the user.

## Implementation
- Poll GitHub Releases API on startup (max once per 24 h)
- Compare current AssemblyVersion against latest release tag
- Download installer in background, verify SHA256 from SHA256SUMS.txt
- Prompt user with non-blocking banner; never block startup

## Acceptance criteria
- [ ] Update check on startup, at most once per 24 h
- [ ] User can disable in Settings > General
- [ ] SHA256 verified before offering install
- [ ] Silent on startup, only show banner if update available
'@
New-Issue "feat: auto-update - silent background update checker" $b1 @("enhancement","v2.1")

# ── 2 ────────────────────────────────────────────────────────────────────────
$b2 = @'
## Summary
Allow users to migrate from Chrome or Firefox with one click.

## Implementation
- Chrome: read Bookmarks JSON and History SQLite from User Data\Default
- Firefox: read places.sqlite from the default profile
- Import wizard: select browser > preview > import

## Acceptance criteria
- [ ] Detects installed Chrome / Firefox profiles automatically
- [ ] Imports bookmarks preserving folder structure
- [ ] Imports history with URL, title and last visit date
- [ ] Handles duplicate URLs gracefully (skip or merge)
'@
New-Issue "feat: import bookmarks and history from Chrome / Firefox" $b2 @("enhancement","v2.1")

# ── 3 ────────────────────────────────────────────────────────────────────────
$b3 = @'
## Summary
When the user navigates to a site with saved credentials in the Vault, VELO should offer to autofill the login form.

## Implementation
- Match current URL domain against Vault entries
- Inject credentials via WebView2 ExecuteScriptAsync after user confirms
- Show a subtle banner, not a blocking popup
- Respect a per-site or global disable setting

## Acceptance criteria
- [ ] Autofill banner appears on matching login pages
- [ ] Credentials injected only after user confirms
- [ ] Works on most major sites (Gmail, GitHub, etc.)
- [ ] User can disable autofill per-site or globally
'@
New-Issue "feat: password autofill — inject credentials on matching domains" $b3 @("enhancement","v2.1")

# ── 4 ────────────────────────────────────────────────────────────────────────
$b4 = @'
## Summary
Warn users when a saved password appears in a known data breach using the HIBP k-anonymity model. No full password ever leaves the device.

## Implementation
- SHA-1 hash the password
- Send only the first 5 chars to api.pwnedpasswords.com/range/{prefix}
- Compare the remaining suffix locally
- Show a warning badge in the Vault UI next to breached entries

## Acceptance criteria
- [ ] Only the first 5 chars of SHA-1 are sent (k-anonymity preserved)
- [ ] Warning badge shown in Vault list
- [ ] Check is on-demand (button), not automatic on every unlock
- [ ] Works offline gracefully (skip check, no error shown)
'@
New-Issue "feat: password breach check via HaveIBeenPwned k-anonymity API" $b4 @("enhancement","v2.1")

# ── 5 ────────────────────────────────────────────────────────────────────────
$b5 = @'
## Summary
Allow users to group related tabs with a color label so the sidebar stays organized.

## Acceptance criteria
- [ ] Right-click tab > Add to group / New group
- [ ] Groups shown as collapsible sections in the sidebar
- [ ] Each group has a user-chosen color and optional name
- [ ] Groups persist across sessions
'@
New-Issue "feat: tab groups - color-coded clusters in the sidebar" $b5 @("enhancement","v2.1")

# ── 6 ────────────────────────────────────────────────────────────────────────
$b6 = @'
## Summary
VELO should restore the previous session (all open tabs) after a crash or when the user chooses "Continue where I left off".

## Implementation
- Serialize open tab URLs to %AppData%\VELO\session.json on every tab change
- On startup detect unclean exit and offer restore
- Setting: always restore / ask / never

## Acceptance criteria
- [ ] Detects unclean shutdown and offers to restore
- [ ] All tabs restored in correct order
- [ ] User can choose restore behavior in Settings > Startup
'@
New-Issue "feat: session restore - reopen tabs after crash or restart" $b6 @("enhancement","v2.1")

# ── 7 ────────────────────────────────────────────────────────────────────────
$b7 = @'
## Summary
Currently the DoH provider is limited to a preset list. Power users should be able to enter any RFC 8484-compliant DoH endpoint.

## Acceptance criteria
- [ ] Free-text field in Settings > Network > DNS-over-HTTPS
- [ ] Validates URL format before saving
- [ ] Tests the endpoint with a sample query and shows latency
- [ ] Falls back to system DNS if custom endpoint fails
'@
New-Issue "feat: custom DoH provider - free-text resolver URL in Settings" $b7 @("enhancement","v2.1")

# ── 8 ────────────────────────────────────────────────────────────────────────
$b8 = @'
## Summary
The LICENSE file currently contains only the short AGPLv3 notice header (17 lines). It must contain the full GNU Affero General Public License v3.0 text as required by the FSF.

## Fix
Replace LICENSE with the complete text from https://www.gnu.org/licenses/agpl-3.0.txt

## Acceptance criteria
- [ ] LICENSE file contains the full AGPLv3 text (~700 lines)
- [ ] GitHub license detection shows AGPL-3.0
'@
New-Issue "fix: replace short AGPLv3 header with complete license text" $b8 @("bug","documentation","good first issue")

# ── 9 ────────────────────────────────────────────────────────────────────────
$b9 = @'
## Summary
Add an HTTPS-only mode that upgrades HTTP navigation to HTTPS automatically or shows a warning.

## Acceptance criteria
- [ ] Toggle in Settings > Privacy > HTTPS-only mode
- [ ] Attempts HTTPS upgrade first
- [ ] Shows interstitial warning if HTTPS is unavailable
- [ ] Per-site exception list
'@
New-Issue "feat: HTTPS-only mode — block or warn on plaintext HTTP navigation" $b9 @("enhancement","v2.2","privacy")

# ── 10 ───────────────────────────────────────────────────────────────────────
$b10 = @'
## Summary
Allow users to whitelist specific domains where privacy features should be relaxed (e.g. video-conferencing sites that need WebRTC).

## Acceptance criteria
- [ ] Exceptions manager UI in Settings > Privacy > Site exceptions
- [ ] Per-site toggles: tracker blocking, fingerprint noise, WebRTC
- [ ] Exceptions survive browser restart
- [ ] Easy to add from the address bar padlock menu
'@
New-Issue "feat: site-specific privacy exceptions — whitelist trusted domains" $b10 @("enhancement","v2.2","privacy")

# ── 11 ───────────────────────────────────────────────────────────────────────
$b11 = @'
## Summary
Document the security threat model for VELO: assets, threat actors, attack vectors, mitigations and residual risks.

## Scope
- Password Vault (local encryption, unlock flow)
- WebView2 renderer isolation
- DNS-over-HTTPS (resolver trust)
- VeloAgent sandboxed actions
- Tracker blocker bypass scenarios

## Acceptance criteria
- [ ] Published as docs/THREAT_MODEL.md
- [ ] Linked from README and landing page Security section
'@
New-Issue "docs: formal threat model document" $b11 @("documentation","security","v2.2")

# ── 12 ───────────────────────────────────────────────────────────────────────
$b12 = @'
## Summary
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
- [ ] Imported entries encrypted same as native Vault entries
'@
New-Issue "feat: import passwords from CSV (1Password / Bitwarden / LastPass / Chrome)" $b12 @("enhancement","v2.3")

# ── 13 ───────────────────────────────────────────────────────────────────────
$b13 = @'
## Summary
Allow users to export their saved passwords for backup or migration.

## Acceptance criteria
- [ ] Export to encrypted JSON (AES-256, user-chosen password)
- [ ] Export to plain CSV with security warning
- [ ] Confirmation dialog before export
- [ ] Exported file includes: site, username, password, notes
'@
New-Issue "feat: export Vault to encrypted JSON or CSV" $b13 @("enhancement","v2.3")

# ── 14 ───────────────────────────────────────────────────────────────────────
$b14 = @'
## Summary
The landing page is already localized in 5 languages. The browser UI itself (menus, settings, dialogs) should support the same languages.

## Implementation
- Externalize all UI strings to .resx resource files
- Detect system locale; allow manual override in Settings > Language
- Priority: Spanish (ES), then French, German, Portuguese

## Acceptance criteria
- [ ] All UI strings in .resx files (no hardcoded English)
- [ ] Spanish translation complete
- [ ] Language selector in Settings > General
'@
New-Issue "feat: in-app UI localization (ES / FR / DE / PT)" $b14 @("enhancement","v2.3","i18n")

# ── 15 ───────────────────────────────────────────────────────────────────────
$b15 = @'
## Summary
Add a Save to reading list action that saves page content locally for offline reading.

## Acceptance criteria
- [ ] Save button in toolbar and right-click context menu
- [ ] Saved pages accessible from sidebar panel
- [ ] Pages readable offline (HTML snapshot or reader-mode extraction)
- [ ] Reading list persists across restarts
'@
New-Issue "feat: reading list - save pages to read later (offline)" $b15 @("enhancement","backlog")

Write-Host ""
Write-Host "All done! View issues at https://github.com/badizher-codex/velo/issues" -ForegroundColor Cyan
