# VELO Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 2.x     | ✅ Active support |
| 1.x     | ⚠️ Critical fixes only |
| < 1.0   | ❌ No support |

## Reporting a Vulnerability

**Preferred:** GitHub Security Advisories (confidential):
👉 https://github.com/badizher-codex/velo/security/advisories/new

**Alternative:** Email `security@velo.app` (PGP key: https://velo.app/pgp.asc)

We commit to:
- **Acknowledge** receipt within 72 hours
- **Preliminary assessment** within 7 days
- **Fix critical vulnerabilities** within 30 days, high within 60 days
- **Credit** the reporter in release notes (unless anonymity is requested)
- **CVE assignment** for confirmed, fixed vulnerabilities

## Scope

### In scope
- `VELO.exe` and all bundled libraries
- Default configurations
- Update mechanism and integrity verification
- Bundled blocklists and Golden List
- Fingerprint protection bypasses
- AI agent prompt injection vulnerabilities
- WebView2 integration security issues
- Vault / password manager security
- Container isolation bypasses

### Out of scope
- Windows kernel issues
- WebView2 / Chromium upstream vulnerabilities (report to [Microsoft](https://msrc.microsoft.com/) or [Chrome](https://g.co/vulnz))
- Third-party Ollama / LLamaSharp vulnerabilities (report to those projects)
- Social engineering without code flaws
- Issues requiring physical access to the device
- DoS attacks with no privacy/security impact

## Safe Harbor

Security research conducted in good faith under this policy is:

- Authorized and not in violation of VELO's terms
- Exempt from DMCA restrictions on circumvention
- Eligible for public credit and listing in our Hall of Fame

We ask that you:
- Give us reasonable time to fix before public disclosure
- Make a good faith effort to avoid privacy violations, data destruction, or service interruption
- Only interact with accounts and data you own or have explicit permission to test

## Hall of Fame

Researchers who have responsibly disclosed vulnerabilities will be listed here with their permission.

*(No entries yet — be the first!)*
