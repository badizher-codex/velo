# PLAN VELO — Plan maestro

**Creado:** 2026-06-06 · **Actualizado:** 2026-07-28 (HEAD = v2.4.67) · **Council PAUSADO.**

> **Para una sesión nueva:** leé esto + `memory/MEMORY.md`. No re-auditar (ya está: [`AUDITORIA_VELO.md`](AUDITORIA_VELO.md)). El trabajo vivo está en la **Ruta 2026-07-28 (§6 abajo)** + [`PLAN_VELO_IA_SEGURIDAD.md`](PLAN_VELO_IA_SEGURIDAD.md) (VELO Sentinel) + [`PLAN_VELO_FASE2.md`](PLAN_VELO_FASE2.md) (desbloqueada: Decisión #4 RESUELTA) + [`BACKLOG.md`](BACKLOG.md).

---

## 1. NORTE — la tesis

VELO no compite con Grok/Claude/GPT en inteligencia. **Compite en estar-ahí.**

> *"Tu IA vive localmente — y está siempre ahí, sin abrir otra pestaña, a veces sin que la pidas."*

- **Council Mode es lo opuesto** (deliberado, pesado) → PAUSADO.
- **Las piezas ambient ya existen dispersas** → el trabajo es unificar + hacer instantáneo (resta, no suma).
- **First-run del modelo = existencial.**
- **Regla anti-Clippy:** proactivo sí, bloqueante nunca.

## 2. CAMINO — 3 fases EN ORDEN

`usable (piso) → útil (foco ambient) → ligero (consecuencia)`

| Fase | Estado | Doc |
|---|---|---|
| **1 — Piso usable** | ✅ **EJECUTADA** — v2.4.59 (streaming flags, AS-1 vault, AS-2 cert, C-2 lock, F-5, QW-1, QW-3) + v2.4.60 (F-2 OAuth, auditoría A1-A5, hook diagnóstico) + v2.4.61 (F-3 permisos, R-1 crash recovery, AS-3 hardening, QW-6). **Abierto:** F-1 Widevine (ver BACKLOG P0) | §3 abajo (histórico) |
| **2 — Foco ambient** | 📋 Especificada, **bloqueada por Decisión #4** + verificación runtime v2.4.60 | [`PLAN_VELO_FASE2.md`](PLAN_VELO_FASE2.md) |
| **3 — Ligero** | Cae sola tras Fase 2 (matar CUDA, fusionar adapters en VELO.AI) | FASE2 §6 |

## 3. FASE 1 — registro de ejecución (histórico)

| ID | Fix | Release |
|---|---|---|
| F-1 | Flags del CDM quitados (`--disable-component-update`, `--disable-background-networking`, luego `--disable-plugins`, `--disable-logging`) — **insuficiente, sigue abierto** (BACKLOG P0) | v2.4.59 + v2.4.60 |
| F-6 | `--disable-features` fusionado (Chromium solo honra el último) | v2.4.59 |
| AS-1 | Autofill host host-side; v2.4.60 lo perfecciona con `e.Source` (sin carrera) | v2.4.59 + v2.4.60 |
| AS-2 | Cert inválido → bloqueo duro; v2.4.60 arregla verdict + botones override | v2.4.59 + v2.4.60 |
| C-2 | `SemaphoreSlim(1,1)` en LLamaSharpAdapter + OCE fail-soft | v2.4.59 + v2.4.60 |
| F-5 | Fingerprint default Balanced — v2.4.60 lo completa (3 read-sites + wizard + labels) | v2.4.59 + v2.4.60 |
| QW-1 | Logo NewTab → data: URI (sin phone-home) | v2.4.59 |
| QW-3 | crt.sh opt-in OFF + toggle (v2.4.60 lo mueve a Privacy) | v2.4.59 + v2.4.60 |
| F-2 | **OAuth popups con `window.opener` real** (`e.NewWindow` + deferral + `window.close()`) | v2.4.60 |

## 4. DECISIONES

| # | Pregunta | Estado |
|---|---|---|
| 2 | Cert: ¿bloqueo duro? | ✅ Bloqueo duro (v2.4.59) |
| 3 | crt.sh: ¿opt-in OFF? | ✅ Opt-in OFF (v2.4.59) |
| 6 | Fingerprint: ¿Balanced default? | ✅ Balanced (v2.4.59/60) |
| 7 | Council: ¿pausado? | ✅ Pausado |
| **4** | **Camino IA local: (A) LM Studio/Ollama HTTP, matar LLamaSharp+CUDA · (B) LLamaSharp GGUF sin CUDA · (C) ambos** | ✅ **RESUELTA 2026-07-28 — opción C reinterpretada: clasificador incrustado (VELO Sentinel) como cerebro always-on + HTTP como opt-in para power users.** El maintainer aprobó: (1) el camino crítico de seguridad pasa a un encoder tiny ONNX in-process (sin server, sin CUDA, sin GPU — ver [`PLAN_VELO_IA_SEGURIDAD.md`](PLAN_VELO_IA_SEGURIDAD.md)); (2) los conectores Claude/GPT/Grok/local HTTP **se quedan** como opción "para alguien quisquilloso"; (3) LLamaSharp+CUDA se elimina en Fase 3 como preveía la opción A. Fundamento de campo: el server local del propio maintainer está caído la mayor parte del tiempo (logs 2026-07) — cualquier camino que dependa de él contradice la tesis *estar-ahí* |
| 5 | ¿Mantener Claude-nube (Anthropic.SDK)? | Pendiente (no bloquea; decidir en Fase 3 al fusionar adapters) |

## 5. GATES OBLIGATORIOS (toda release)

1. `dotnet publish -c Release -r win-x64 --self-contained true` local ANTES de push que toque WebView2 (lección #22).
2. `dotnet test` completo **contando los 6 proyectos** (Core/Security/Agent/Vault/Import/Smoke); exit code + presencia, no grep (lección #25).
3. Versionado: csproj (3 strings) + docs/index.html (**~17 refs**, incluye `hero_badge` ×6 — se olvidó de mayo a julio) + CHANGELOG.md.
4. Release via workflow 259455799. Co-author trailer del modelo de la sesión. **Disparar el release apenas CI dé verde** — Pages deploya los links nuevos al instante y el botón de descarga da 404 hasta que los assets existan.
5. **Verificación runtime del maintainer antes de la siguiente fase** — 16 releases sin verificar fue el patrón que motivó todo esto. Refuerzo lección #34: toda feature con switch ON necesita UNA verificación del efecto visible (el adblock de YouTube estuvo 2 meses muerto con el toggle en ON).

## 6. RUTA APROBADA 2026-07-28 (por el maintainer, en orden)

| # | Ítem | Qué es | Tamaño | Doc |
|---|---|---|---|---|
| R-1 | **Spike extensiones WebView2** | `AreBrowserExtensionsEnabled=true` + `Profile.AddBrowserExtensionAsync` con **uBlock Origin Lite** (MV3) descomprimido → probar en sitio con ads. Si carga: adblock general con filterlists de la comunidad, se acabó mantener selectores a mano (el drift de YouTube v0.1→v0.2 es el argumento). Si no carga: documentar qué API faltó y evaluar motor EasyList propio | 1 día (spike) | resultado decide R-1b (bundle + toggle Settings → Privacy) |
| R-2 | **DNS-over-HTTPS** | Toggle Settings → Privacy (Off / Cloudflare / Quad9 / Custom) via args `--dns-over-https-mode=secure --dns-over-https-templates=<url>` — el hook de args ya existe (v2.4.60). 8 idiomas | 1 tarde | — |
| R-3 | **HTTPS-only mode** | Upgrade http→https en NavigationStarting (top-level); si falla, interstitial local (patrón `BuildCertErrorPage` de P2-C) con "Continuar con HTTP" allow-once. Toggle Settings → Privacy, default ON | 1-2 días | — |
| R-4 | **VELO Sentinel** (Piso 1) | Clasificador de seguridad incrustado — chunks S-A→S-E | 3-5 semanas | [`PLAN_VELO_IA_SEGURIDAD.md`](PLAN_VELO_IA_SEGURIDAD.md) |
| R-5 | **Polish** | Favicons en sidebar (`TabInfo.FaviconData` dormant desde Phase 1, lección #21: cablear producer `CoreWebView2.FaviconChanged` + consumer XAML) · command palette discoverability (URL hint + Ctrl+/) | 2-3 días | `feature_command_palette_discoverability.md` |
| R-6 | **Code signing** | El maintainer hace el gasto del certificado **cuando declaremos VELO "listo full"** = R-1..R-5 shipped + verificados runtime. Plan técnico ya escrito en BACKLOG P1 (Azure Trusted Signing) | trámite + 1 día CI | BACKLOG P1 |

**Piso 2 del Sentinel (generativo destilado) y Fase 2 ambient (Ctrl+Space)**: después de R-4 verificado. Council sigue PAUSADO.
