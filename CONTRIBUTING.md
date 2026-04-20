# Contributing to VELO

Thank you for your interest in contributing to VELO — a privacy-first browser for Windows.

## Table of Contents

- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Code Style](#code-style)
- [Submitting a Pull Request](#submitting-a-pull-request)
- [Reporting Bugs](#reporting-bugs)
- [Feature Requests](#feature-requests)
- [Security Vulnerabilities](#security-vulnerabilities)
- [License](#license)

---

## Getting Started

### Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| Windows | 10 / 11 (x64) | Required — WPF + WebView2 are Windows-only |
| .NET SDK | 8.x | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| WebView2 Runtime | Latest stable | Usually pre-installed on Win11; [download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) |
| Visual Studio | 2022 (any edition) or VS Code + C# Dev Kit | |
| Inno Setup | 6.x | Only needed if modifying the installer |

### Clone and build

```bash
git clone https://github.com/badizher-codex/velo.git
cd velo
dotnet build
dotnet run --project src/VELO.App
```

### Run tests

```bash
dotnet test
```

---

## Development Setup

### Project structure

```
velo/
├── src/
│   ├── VELO.App/          # Entry point, DI, MainWindow
│   ├── VELO.Core/         # Tab management, navigation, EventBus
│   ├── VELO.Data/         # SQLite models, repositories
│   ├── VELO.DNS/          # DNS over HTTPS
│   ├── VELO.Security/     # AI engine, guards, blocklists
│   ├── VELO.UI/           # WPF controls, dialogs, themes
│   └── VELO.Vault/        # Password manager crypto
├── resources/
│   ├── blocklists/        # Bundled EasyList + EasyPrivacy
│   └── scripts/           # JavaScript injected into WebView2
├── installer/             # Inno Setup scripts
├── docs/                  # Documentation
└── .github/workflows/     # CI/CD
```

### Environment variables (optional)

| Variable | Purpose |
|----------|---------|
| `VELO_CLAUDE_API_KEY` | Enable Claude API adapter (opt-in; never required) |
| `VELO_OLLAMA_ENDPOINT` | Override default Ollama endpoint (`http://localhost:11434`) |

---

## Code Style

VELO uses C# 12 with .NET 8. Key conventions:

- **Primary constructors** for services: `public class MyService(IDep dep, ILogger<MyService> logger)`
- **Records** for events and value objects: `public record TabCreatedEvent(string TabId)`
- **Async/await** throughout — no `.Result` or `.Wait()` (exception: `Dispose()`)
- **`ILogger<T>`** in every service — Debug for flow, Warning for anomalies, Error for failures
- **No trivial comments** — comment the *why*, not the *what*. Well-named identifiers speak for themselves
- **Nullable reference types** enabled — annotate all `?` correctly, no suppressions without justification
- **`SemaphoreSlim`** for async-safe locks — no `lock()` in async code
- **Accessibility** — every new UI control must have `AutomationProperties.Name` and `AutomationProperties.HelpText`

### EditorConfig

The `.editorconfig` at the repo root enforces formatting. Run before committing:

```bash
dotnet format
```

### Privacy rules (non-negotiable)

1. No new HTTP calls without explicit user consent
2. No telemetry, analytics, or crash reporters
3. AI runs local by default — cloud AI is opt-in only, clearly labeled
4. All downloaded content is SHA256-verified before use
5. User inputs from web pages are treated as untrusted

---

## Submitting a Pull Request

1. **Fork** the repository and create a branch: `git checkout -b feat/my-feature`
2. **Write tests** — every module in Fase 2 has a mandatory test list in `docs/Phase2/VELO_FASE2_v3_DOCUMENTACION.md`
3. **Run tests**: `dotnet test`
4. **Run format**: `dotnet format --verify-no-changes`
5. **Open a PR** against `main` with:
   - A clear description of *what* and *why*
   - Screenshots / GIFs if the PR affects UI
   - A note if new dependencies are added (all must be justified)

### PR checklist

- [ ] Tests pass (`dotnet test`)
- [ ] No new StyleCop warnings
- [ ] Accessibility: new controls have `AutomationProperties`
- [ ] New settings have defaults documented
- [ ] No hardcoded secrets or tokens
- [ ] Untrusted inputs (web content, downloaded JSON) are sanitized
- [ ] `CHANGELOG.md` updated

### No CLA required

VELO is licensed under AGPLv3. By submitting a PR you agree to license your contribution under AGPLv3. No Contributor License Agreement (CLA) is required.

---

## Reporting Bugs

Open a GitHub Issue with:
- VELO version (`Help → About`)
- Windows version
- Steps to reproduce
- Expected vs. actual behavior
- Logs from `%LOCALAPPDATA%\VELO\logs\` (redact any personal data)

---

## Feature Requests

Open a GitHub Issue with the `feature-request` label. For large features, open a Discussion first to align on design before writing code.

Fase 2 modules are already planned in `docs/Phase2/VELO_FASE2_v3_DOCUMENTACION.md`. Features not in that document require a separate design discussion before implementation.

---

## Security Vulnerabilities

**Do not open a public GitHub Issue for security vulnerabilities.**

See [SECURITY.md](SECURITY.md) for responsible disclosure guidelines.

---

## License

VELO is licensed under the [GNU Affero General Public License v3.0](LICENSE).

This means:
- You can use, study, modify, and distribute VELO freely
- If you distribute a modified version (including running it as a service), you must release your modifications under AGPLv3
- There is no warranty

Dependencies are listed in the SBOM attached to each release.
