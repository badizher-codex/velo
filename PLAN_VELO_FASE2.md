# PLAN VELO — FASE 2: Foco Ambient

**Creado:** 2026-07-06 · **Estado al crear:** HEAD `e14ff15` (v2.4.60), 559/559 verde, Council PAUSADO, Fase 1 ejecutada (v2.4.59 + v2.4.60).
**Docs hermanos:** [`PLAN_VELO.md`](PLAN_VELO.md) (plan maestro) · [`AUDITORIA_VELO.md`](AUDITORIA_VELO.md) (hallazgos) · [`BACKLOG.md`](BACKLOG.md) (deuda v2.4.61+).

> **Para la sesión que ejecute esto:** Fase 2 arranca SOLO cuando (a) la Decisión #4 esté tomada y (b) el maintainer haya verificado runtime v2.4.60 (login Google). No re-derivar el diseño — está acá. Ejecutar por chunks, cada chunk compila y se verifica.

---

## 1. NORTE (la vara contra la que se mide todo)

VELO no compite en inteligencia — compite en **estar-ahí**:

> *"Tu IA vive localmente — y está siempre ahí, sin abrir otra pestaña, a veces sin que la pidas."*

Fase 2 = convertir las piezas ambient dispersas en **una superficie instantánea única** + resolver el **first-run del modelo** (existencial: IA ambient sin modelo configurado = cero).

**Regla anti-Clippy:** proactivo SÍ, bloqueante NUNCA. Nudges chicos, dismissables, ganados por contexto.

**Es ejercicio de resta:** unificar lo que existe, no construir features nuevas.

---

## 2. INVENTARIO — las piezas ambient existentes (verificado 2026-07-06)

| # | Pieza | Ancla | Fricción actual (pasos hasta valor) |
|---|---|---|---|
| 1 | **TL;DR badge** | `UrlBar.xaml.cs:98,205` (`TldrBadge`) | 1 clic — LA referencia de UX ambient. Pero solo resume página completa |
| 2 | **AI context menu** | `AIContextMenuBuilder.cs:27` | 2 pasos (selección → right-click → acción) |
| 3 | **Slash commands** | `SlashCommandRouter.cs:14` (`/tldr /resumen /explicar /preguntas /traducir`) | 3+ pasos (abrir panel agente → tipear /cmd → enter) |
| 4 | **Agent panel (chat)** | `VeloAgentPanel.xaml.cs:19` | 3+ pasos (abrir panel → tipear → esperar sin streaming visible) |
| 5 | **BookmarkAI auto-tag** | `BookmarkAIService.cs:29` | 0 pasos (automático al guardar bookmark) ✓ ya es ambient |
| 6 | **SmartShield narration** | `BlockNarrationService.cs:35` | 0 pasos (toast automático) ✓ ya es ambient |
| 7 | **Command Palette** | `CommandPaletteController.cs:29` (Ctrl+K) | Poco descubrible (deuda conocida), orientado a comandos no a preguntas |
| 8 | **Glance popup** | `GlancePopup.xaml.cs` (hover preview) | 0 pasos ✓ ya es ambient |

**Diagnóstico:** las piezas 5/6/8 ya son ambient (dejarlas). Las piezas 1-4+7 son **cuatro entradas distintas al mismo motor** — el usuario tiene que saber cuál usar y dónde está cada una. La fricción no es la IA, es la **superficie fragmentada**.

---

## 3. DISEÑO — "Velo Instant" (la superficie única)

### Concepto
**Un atajo global → un input flotante sobre la página actual → respuesta streaming in-place.** Sin cambio de contexto, sin panel lateral, sin tab nueva.

### Especificación
- **Atajo:** `Ctrl+Space` (nuevo, dedicado). `Ctrl+K` sigue siendo palette de comandos; Velo Instant es para *preguntas*. (Alternativa si colisiona con IMEs: `Alt+Space` está tomado por Windows → fallback `Ctrl+Shift+Space`.)
- **UI:** overlay centrado-superior (~640px), fondo blur, un TextBox + zona de respuesta streaming debajo. `Esc` cierra siempre. NO es ventana — es un control WPF sobre `BrowserContent` (⚠️ HWND airspace de WebView2: usar row dedicada del grid o `Popup` WPF real — lección v2.4.56 del Council Bar).
- **Contexto implícito:** el contenido de la página activa viaja automático (`GetPageContentAsync()` ya existe en `BrowserTab.PublicApi.cs`). Si hay texto seleccionado, la selección pesa más que la página.
- **Chips contextuales** (sugerencias de 1 clic, ganadas por contexto — anti-Clippy: son estáticas, no saltan solas): `TL;DR` · `Explicar selección` (solo si hay selección) · `Traducir` · `Preguntas clave`. Los chips invocan los slash commands existentes vía `SlashCommandRouter`.
- **Input libre:** cualquier texto → pregunta sobre la página vía `DirectChatAdapter` (stateless, 1 request in-flight, ya existe desde v2.4.42). Prefijo `/` → router de slash commands.
- **Streaming:** tokens visibles a medida que llegan (percepción de instantáneo > velocidad real). `DirectChatAdapter` hoy es request/response — extenderlo a streaming SSE es parte del chunk B.
- **Salida:** respuesta con 2 acciones: `Copiar` · `Continuar en panel` (abre VeloAgentPanel con el hilo precargado, para la minoría que quiere conversación).

### Qué se ABSORBE (resta, no suma)
- **TL;DR badge** → queda, pero su clic abre Velo Instant con el chip TL;DR pre-disparado (una sola implementación de resumen).
- **Context menu AI actions** → quedan, pero invocan Velo Instant con la acción pre-cargada (hoy abren ventanas/paneles propios — `AIResultWindow`).
- **Slash commands** → el router se comparte; el agent panel deja de ser la única puerta.
- **`AIResultWindow`** → candidata a morir cuando todo pase por la superficie única.

### Qué NO se toca
Council (pausado) · piezas ya-ambient (BookmarkAI, narration, Glance) · Security stack.

---

## 4. FIRST-RUN DEL MODELO (existencial — depende Decisión #4)

El blocker: *"si conseguir el modelo es 'bajá un GGUF a mano', la tesis muere en el minuto 1"*.

### Si Decisión #4 = A (LM Studio/Ollama vía HTTP, matar LLamaSharp+CUDA)
1. **Detección silenciosa al arranque:** ping a `localhost:11434` (Ollama) y `localhost:1234` (LM Studio) — ya existe `RefreshAiStatusAsync` en MainWindow como base.
2. **Si no hay servidor:** el PRIMER uso de Velo Instant (no el arranque — anti-Clippy) muestra el wizard: "VELO usa IA local. Instalá Ollama (1 clic)" → `winget install Ollama.Ollama` → `ollama pull llama3.2:3b` (~2 GB) con barra de progreso → dot verde.
3. **Fallback sin instalar:** chips no-IA siguen funcionando (nada crashea); el input muestra "IA local no configurada — [Configurar]".

### Si Decisión #4 = B (LLamaSharp GGUF sin CUDA)
1. Primer uso de Velo Instant → wizard de descarga de GGUF chico (Llama-3.2-3B Q4 ~2 GB / Phi-3.5-mini ~2.2 GB) con SHA256 + barra de progreso → carga lazy (NO al startup — hoy `AppBootstrapper.cs:170` resuelve LLamaSharpAdapter SIEMPRE; eso se invierte en Fase 3).
2. CPU-only por default (`GpuLayers=0`); detección de VRAM queda para Fase 3.

### Si Decisión #4 = C (ambos)
A si detecta servidor; si no, ofrecer B como "sin instalar nada extra". Doble mantenimiento — solo si el maintainer lo justifica.

**Métrica de éxito first-run:** usuario nuevo → IA respondiendo en **< 5 minutos sin leer docs**, con progreso visible todo el tiempo.

---

## 5. PLAN CHUNKED (estilo Fase 4.1 — cada chunk compila + se verifica)

| Chunk | Contenido | Verificación runtime |
|---|---|---|
| **A** | Overlay shell + atajo global + Esc/focus + echo (sin IA). Riesgo HWND airspace se resuelve acá | Ctrl+Space abre/cierra sobre cualquier página |
| **B** | Contexto de página + `DirectChatAdapter` + **streaming** (extender adapter a SSE) | Pregunta libre sobre página responde con tokens visibles |
| **C** | Chips contextuales + `SlashCommandRouter` compartido + selección-como-contexto | Chips TL;DR/Traducir funcionan; `/tldr` desde el overlay |
| **D** | First-run wizard (según Decisión #4) + status dot integrado | Máquina limpia → IA funcionando < 5 min |
| **E** | Absorción: TL;DR badge → overlay · context menu actions → overlay · retirar `AIResultWindow` | Badge y right-click abren la misma superficie |
| **F** | Polish (i18n 8 idiomas, temas, a11y) + smoke tests wiring (lecciones #8/#15/#23) + **gate runtime del maintainer** → release **v2.6.0** | Checklist completo del maintainer |

**Estimación honesta:** A+B son el núcleo (~2 sesiones); C-E una sesión c/u; D depende de la decisión. No empezar C sin B verificado runtime (lección: 16 releases sin verificar).

---

## 6. FASE 3 — LIGERO (cae sola después de Fase 2, anotada para no perderla)

1. Matar `LLamaSharp.Backend.Cuda12` del bundle (`VELO.Agent.csproj:17`, >1 GB) — automática si Decisión #4 = A.
2. Fusionar las 3 abstracciones de adapter (`IAgentAdapter` + `IAIAdapter` + `DirectChatAdapter`) en **VELO.AI** con un solo cliente OpenAI-compat (audit R3; Ollama está implementado DOS veces: `OllamaAgentAdapter.cs` y `OllamaAdapter.cs`).
3. Lazy-load del modelo si sobrevive LLamaSharp (audit R5).
4. Target: 8 módulos, MainWindow < 2.000 loc (audit §6.4).
