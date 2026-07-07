# PLAN VELO — Plan maestro

**Creado:** 2026-06-06 · **Actualizado:** 2026-07-06 (HEAD `e14ff15` = v2.4.60) · **Council PAUSADO.**

> **Para una sesión nueva:** leé esto + `memory/MEMORY.md`. No re-auditar (ya está: [`AUDITORIA_VELO.md`](AUDITORIA_VELO.md)). El trabajo vivo está en [`PLAN_VELO_FASE2.md`](PLAN_VELO_FASE2.md) (bloqueado por Decisión #4) y [`BACKLOG.md`](BACKLOG.md) (v2.4.61+).

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
| **4** | **Camino IA local: (A) LM Studio/Ollama HTTP, matar LLamaSharp+CUDA · (B) LLamaSharp GGUF sin CUDA · (C) ambos** | 🔴 **PENDIENTE — BLOQUEA FASE 2.** Recomendación del análisis 2026-07-06: **A** (el setup real del maintainer es LM Studio; DirectChatAdapter ya hace el 90% del tráfico stateless vía HTTP; CUDA es >1 GB del instalador y un imán para heurísticas de AV; first-run guiado con winget+ollama pull es viable — ver FASE2 §4). B solo si "instalar Ollama" se considera fricción inaceptable; C solo con justificación explícita de doble mantenimiento |
| 5 | ¿Mantener Claude-nube (Anthropic.SDK)? | Pendiente (no bloquea; decidir en Fase 3 al fusionar adapters) |

## 5. GATES OBLIGATORIOS (toda release)

1. `dotnet publish -c Release -r win-x64 --self-contained true` local ANTES de push que toque WebView2 (lección #22).
2. `dotnet test` completo **contando los 6 proyectos** (Core/Security/Agent/Vault/Import/Smoke); exit code + presencia, no grep (lección #25).
3. Versionado: csproj (3 strings) + docs/index.html (~11 refs) + CHANGELOG.md.
4. Release via workflow 259455799. Co-author trailer del modelo de la sesión.
5. **Verificación runtime del maintainer antes de la siguiente fase** — 16 releases sin verificar fue el patrón que motivó todo esto.
