# VELO — Fase 2 · Documentación

Carpeta de specs técnicas para la Fase 2 del navegador VELO.

Documento principal: **[`VELO_FASE2_v3_DOCUMENTACION.md`](./VELO_FASE2_v3_DOCUMENTACION.md)** (2.771 líneas, 112 KB).

---

## Cómo usar este doc con Claude Code (o cualquier LLM agente)

1. Abre Claude Code en la raíz del repo.
2. Primera instrucción, literal:

   > Lee `docs/phase2/VELO_FASE2_v3_DOCUMENTACION.md` completo. Es la spec autoritativa de Fase 2. Después lee el código del repo para entender el estado de Fase 1. Dame un resumen de: (1) qué módulos de Fase 1 existen y funcionan, (2) qué archivos y clases nuevas hacen falta para el Sprint 1, (3) qué tocás de lo existente. **No escribas código todavía.**

3. Cuando arranque a implementar, remítelo siempre a secciones del doc: *"está en la §5.2"*, *"revisá el acceptance criteria de §2.10"*. Si algo ambiguo no está en el doc, actualízalo **antes** de que improvise.
4. El orden del roadmap de **§19** no es sugerencia. Sprint 1 (Trust & Transparency) va primero aunque sea menos divertido que Vertical Tabs.

---

## Regla de oro inmutable de Fase 2

> Ningún módulo nuevo envía datos fuera del dispositivo sin consentimiento explícito. El AI corre 100% local por default. Toda feature funciona en Offline Mode. Toda acción del agente requiere preview + confirmación antes de ejecutar.

Si un PR viola esto, se rechaza. No importa cuán "útil" sea la feature.

---

## Los 16 módulos de Fase 2

| # | Módulo | Sección | Esfuerzo |
|---|---|---|---|
| 2 | Shield Score en URL bar (4 niveles, Golden List) | §2 | 6-8 d |
| 3 | Security Panel v2 + ExplanationGenerator bilingüe | §3 | 4-5 d |
| 4 | Context Menu enriquecido (URL cleaner, OCR local) | §4 | 2-3 d |
| 5 | Container Self-Destruct + Banca Anti-Capture + Paste Guard | §5 | 4-5 d |
| 6 | Privacy Receipt + stats acumulados | §6 | 2 d |
| 7 | Vertical Tabs + Workspaces + Split View + Tear-off | §7 | 10-14 d |
| 8 | Command Bar (Ctrl+K) con fuzzy search | §8 | 3-4 d |
| 9 | Glance preview modal | §9 | 2-3 d |
| 10 | Agent Launcher + LLamaSharp embebido (one-click AI) | §10 | 4-5 d |
| 11 | VeloAgent con sandbox de acciones + anti-injection | §11 | 8-10 d |
| 12 | VELO Security Inspector standalone | §12 | 3-4 d |
| 13 | NewTab v2 con mosaico + folders + modo Bunker | §13 | 2-3 d |
| 14 | Default Browser registration (Windows 10/11) | §14 | 1-2 d |
| 15 | Distribución: Authenticode, Winget, Chocolatey, MSIX, portable | §15 | 3-4 d |
| 16 | SECURITY.md + Threat Model + SBOM | §16 | 2-3 d |

**Total:** 60-75 días full-time · 12-15 semanas.

---

## Orden de ejecución (sprints)

1. **Sprint 1 — Trust & Transparency** (sem. 1-2): SECURITY.md, Threat Model, Authenticode signing, Default Browser registration, Security Panel v2, Context Menu enriquecido.
2. **Sprint 2 — Shield Score + Privacy Receipt** (sem. 3): SafetyScorer, Golden List, ShieldScoreControl, PrivacyReceiptService.
3. **Sprint 3 — Container Advanced + Paste Guard** (sem. 4): Containers temporales, Banca anti-capture, Paste Protection.
4. **Sprint 4 — AI seguro** (sem. 5-6): Agent Launcher, LLamaSharp embebido, VeloAgent con sandbox completo.
5. **Sprint 5 — UX moderna** (sem. 7-9): Vertical Tabs, Workspaces, Split View, Tear-off, Command Bar, Glance.
6. **Sprint 6 — Inspector + NewTab** (sem. 10): VELO Security Inspector, NewTab v2.
7. **Sprint 7 — Distribución** (sem. 11): Winget, Chocolatey, portable, auto-updater.

No se cambia el orden sin PR de discusión.

---

## Cambios explícitos respecto a v2.0

- **Mascota Zorrillo flotante** → eliminada del sprint principal. `mascot.enabled=false` por default. Solo queda el logo estático del NewTab.
- **DevTools custom completas** → eliminadas. Se usa `CoreWebView2.OpenDevToolsWindow()` nativa y se construye solo el VELO Security Inspector como ventana standalone (§12).
- **Shield Score** → 4 niveles (rojo/amarillo/verde/dorado), no 3. El amarillo evita falsos positivos de seguridad en sitios nuevos.
- **VeloAgent** → todas las acciones pasan por sandbox: preview + confirmación + undo 10s. System prompt endurecido contra prompt injection. Sanitizer obligatorio del contenido de páginas.
- **Default Browser registration** → añadido (faltaba en v2.0). Entradas de registry vía Inno Setup + botón en Settings que abre `ms-settings:defaultapps`.
- **Distribución** → añadida (faltaba en v2.0). Authenticode via SignPath Foundation (gratis para OSS), Winget, Chocolatey, portable.

---

## Definición de "done" para cada módulo

Un módulo **no se mergea a `main`** si no cumple:

- Tests unitarios ≥75 % cobertura en lógica nueva.
- Tests pasan en CI en Windows 10 y 11.
- StyleCop sin warnings nuevos.
- XML doc comments en toda API pública.
- Entrada en `CHANGELOG.md`.
- Accesibilidad: teclado + `AutomationProperties` + contraste ≥4.5:1.
- Performance: cold start +<100 ms, RAM +<30 MB.
- Privacy: cumple la Regla de Oro. Opt-out disponible y visible.
- Release firmado con Authenticode + SBOM adjunto.

Detalle completo en **§20** del doc.

---

## Dependencias nuevas aprobadas

Todas compatibles con AGPLv3:

- `LLamaSharp` (MIT) — fallback AI local embebido.
- `FuzzySharp` (MIT) — Command Bar fuzzy search.
- `TesseractOCR` (Apache 2.0) — OCR local en context menu.
- `CycloneDX` (Apache 2.0) — SBOM generation en CI.
- `Velopack` (MIT) — auto-updater firmado.
- `Minisign` (binario, ISC) — verificación de firmas de manifests.

Cualquier dependencia nueva fuera de esta lista requiere discusión en PR.

---

## Contacto y contribución

- Issues: https://github.com/badizher-codex/velo/issues
- Security: `SECURITY.md` (a crear en Sprint 1).
- Licencia: AGPLv3.

---

*VELO — Privacy-First Browser for Windows — Fase 2 v3.0 — 2026*
