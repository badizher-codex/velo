# VELO Browser — FASE 3 — DOCUMENTACIÓN TÉCNICA ULTRA-DETALLADA v1.0

**Privacy-First Browser for Windows — "El browser privado que se usa todos los días"**

---

## 📋 METADATA DEL DOCUMENTO

| Campo | Valor |
|---|---|
| Versión del documento | 1.0 |
| Sucede a | Fase 2 v3 (cerrada en v2.0.4, 2026-04-24) |
| Fase del proyecto | Fase 3 — Migración cómoda + IA productiva + Hardening |
| Stack | C# 12 / .NET 8 / WPF / WebView2 / SQLCipher / Ollama + LLamaSharp / Claude API (opt-in) |
| Licencia | AGPLv3 |
| Estado | PENDIENTE DE IMPLEMENTACIÓN |
| Prerequisito | Fase 2 completada (tag v2.0.4 en main, 56/56 tests) |
| Destinatario de ejecución | Claude Sonnet 4.6 en Claude Code construye; Claude Opus 4.7 revisa |
| Revisor obligatorio | Opus debe firmar cada sprint antes de considerarse done |

### Contexto de cierre de Fase 2

Fase 2 entregó:
- Shield Score, Security Panel v2, Context Menu enriquecido, Containers auto-destrucción, Paste Protection, Privacy Receipt
- Vertical Tabs + Workspaces + Split View + Tear-off, Command Bar, Glance
- AgentLauncher + LLamaSharp embebido, VeloAgent Chat Panel con sandbox
- Security Inspector, NewTab v2, Default Browser, Winget/Chocolatey, SBOM, PRIVACY.md
- i18n completo en 8 idiomas, release CI/CD con GitHub Actions, landing page con privacy policy

Tests al cierre de Fase 2: **56/56 pasando** (VELO.Core, VELO.Security, VELO.Agent).

Limitaciones asumidas (explicitadas al cierre):
- Sin firma Authenticode (SignPath Foundation pendiente; Certum pospuesto por presupuesto)
- Sin integration tests de WebView2 (unit tests únicamente)
- `MainWindow.xaml.cs` 1800+ líneas, `BrowserTab.xaml.cs` 1300+ líneas (refactor diferido a Fase 3)
- Auto-update es manual (usuario descarga nuevo installer desde Releases)

### Cambios vs Fase 2

- **Foco pivota:** seguridad y AI local estaban ya; ahora el objetivo es **que la gente use VELO todos los días**. Sin import + autofill + session restore, VELO no compite con Chrome/Edge para nadie que no sea un desarrollador curioso.
- **IA más agresiva en UX:** en Fase 2 el VeloAgent era un chat panel al costado. En Fase 3 la IA se inserta en el flujo diario: clic derecho → explicar/resumir/verificar; Threats Panel con justificación de cada bloqueo; Command Bar con "Q: ..." para preguntas abiertas.
- **Threats Panel rediseñado:** en Fase 2 la pestaña derecha mostraba solo el último bloqueo. En Fase 3 muestra la lista completa de la sesión con agrupación por dominio y botón "Explicar" por item.
- **Hardening estructural:** refactor `MainWindow` + `BrowserTab` en módulos pequeños para que Fase 4 arranque con cimientos limpios.

### ⚠️ REGLA DE ORO DE FASE 3 (inmutable)

> Todo lo nuevo respeta las reglas de Fase 2:
> 1. **Ningún dato sale del dispositivo sin consentimiento explícito**. Las features de IA consultan el LLM local por default. La IA en la nube (Claude) solo se usa si el usuario la activó en Settings y cada invocación muestra un indicador visible "🌐 Usando Claude (nube)".
> 2. **Telemetría cero.** Sin analytics, sin crash reports, sin "mejora del producto compartiendo datos anónimos".
> 3. **Import/autofill/session restore son locales.** Sin cuenta VELO. Sin sync. Sin servidor. Si el usuario quiere sync, usa su propia instancia de Bitwarden o similar.
> 4. **Toda función nueva es opt-out, y si tiene costo de privacidad es opt-in.** Autofill arranca activo. Consulta a Claude arranca apagada.

### Principios rectores para el ejecutor

1. **No rompas APIs de Fase 1 ni de Fase 2.** `AISecurityEngine`, `SafetyScorer`, `TabManager`, `VaultService`, `ContainerManager`, `EventBus`, `GoldenListService`, `ContextMenuBuilder`, `CommandBar` siguen funcionando sin cambios de API. Si necesitas extender, añade métodos nuevos, no modifiques los existentes.
2. **Refactor antes de feature.** Sprint 6 (refactor `MainWindow`/`BrowserTab`) va PRIMERO, antes de features. La deuda técnica de Fase 2 asfixia cualquier feature nueva que toque esas dos clases.
3. **Opus revisa cada sprint.** Al terminar un sprint, Sonnet escribe un PR mock y Opus lo revisa contra la sección correspondiente de este doc. Ningún sprint se da por cerrado sin la firma de Opus.
4. **Tests son bloqueantes.** Cada módulo nuevo entrega sus tests. Si no hay tests, el sprint no está done. Target de Fase 3: **120+ tests** (vs 56 al cierre de Fase 2).
5. **Accesibilidad intacta.** `AutomationProperties.Name`, keyboard navigation, contraste ≥ 4.5:1. No negociable.

---

## ÍNDICE

1. Resumen ejecutivo de Fase 3
2. **Threats Panel rediseñado** — lista completa de la sesión con explicación IA
3. **Context Menu IA** — suite de acciones sobre selección, enlaces e imágenes
4. Import desde Chrome / Firefox / Edge
5. Password Autofill + HaveIBeenPwned breach check
6. Session Restore + crash recovery
7. VeloAgent v2 — acciones contextuales (TL;DR, Q&A, traducir, explicar página)
8. Auto-update seguro (verificación SHA256 sin firma Authenticode)
9. Refactor `MainWindow` + `BrowserTab` — deuda técnica de Fase 2
10. Integration tests de WebView2
11. Settings nuevos de Fase 3
12. Cronograma y orden de sprints
13. Acceptance criteria globales y definición de "done"

---

## 1. RESUMEN EJECUTIVO DE FASE 3

### Tabla maestra de módulos

| # | Módulo | Prioridad | Estimado | Depende de |
|---|---|---|---|---|
| 2 | Threats Panel rediseñado | CRÍTICA | 4-5 días | EventBus, Malwaredex, AISecurityEngine, VeloAgent |
| 3 | Context Menu IA | CRÍTICA | 5-7 días | ContextMenuBuilder, VeloAgent, IAIAdapter |
| 4 | Import Chrome/Firefox/Edge | CRÍTICA | 6-8 días | HistoryRepository, BookmarkRepository, VaultService |
| 5 | Password Autofill + HIBP | CRÍTICA | 8-10 días | VaultService, WebView2 script injection |
| 6 | Session Restore | ALTA | 3-4 días | TabManager, SettingsRepository |
| 7 | VeloAgent v2 — acciones contextuales | CRÍTICA | 6-8 días | VeloAgent, IAIAdapter, Reader Mode |
| 8 | Auto-update seguro | ALTA | 3-4 días | UpdateChecker existente, SHA256SUMS |
| 9 | Refactor MainWindow + BrowserTab | CRÍTICA | 6-8 días | — (va primero) |
| 10 | Integration tests WebView2 | ALTA | 4-5 días | Refactor (9) |

**Total estimado:** 45-60 días de trabajo. Full-time: 9-12 semanas. Part-time: 15-20 semanas.

### Orden recomendado

**Sprint 1 (refactor):** módulo 9 — desbloquea todo lo demás.
**Sprint 2:** módulos 2 y 3 (las dos features que el usuario pidió explícitamente).
**Sprint 3:** módulo 4 (import).
**Sprint 4:** módulo 5 (autofill + breach check).
**Sprint 5:** módulos 6 y 8 (session restore + auto-update).
**Sprint 6:** módulo 7 (VeloAgent v2 — aprovecha la infra IA ya consolidada).
**Sprint 7:** módulo 10 (integration tests) + hardening final + release v3.0.0.

---

## 2. THREATS PANEL REDISEÑADO

### 2.1 Problema actual (Fase 2)

El panel lateral derecho muestra solo el último bloqueo. Se desperdicia toda la mitad inferior. No hay forma de ver qué más se bloqueó en esta pestaña, ni por qué. El usuario no aprende nada sobre los trackers que VELO detiene.

### 2.2 Objetivo

Transformar el panel en un **registro vivo de bloqueos de la sesión**, agrupado por dominio bloqueante, con explicación bajo demanda generada por la IA local, y con acceso directo a Malwaredex para reportar o aprobar.

### 2.3 Layout visual

```
┌─────────────────────────────────────────┐
│ 🛡 Amenazas — 23 bloqueos  [ 🔄  ✕ ]   │  Header fijo
├─────────────────────────────────────────┤
│ 📊 Resumen                              │
│ Trackers: 18 · Malware: 2 · Ads: 3     │
├─────────────────────────────────────────┤
│ ▼ doubleclick.net            (9)        │  Grupo colapsable
│   🚫 Tracker — cross-site                │
│   /gampad/ads?iu=... · 12:03:15         │
│   [ Explicar ] [ Aprobar ] [ Reportar ] │
│   ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─  │
│   🚫 Tracker — fingerprint              │
│   /collect/metrics · 12:03:18           │
│   [ Explicar ] [ Aprobar ] [ Reportar ] │
│   ... +7 más [ Ver todos ]              │
├─────────────────────────────────────────┤
│ ▶ facebook.com              (4)        │
│ ▶ google-analytics.com      (5)        │
│ ▶ malware.badsite.ru        (2)  🔴    │  Rojo si Malwaredex
├─────────────────────────────────────────┤
│ [ Exportar sesión JSON ] [ Limpiar ]    │  Footer fijo
└─────────────────────────────────────────┘
```

### 2.4 Arquitectura — clases nuevas

```
ThreatsPanelV2 : UserControl (VELO.UI.Controls)
─ _viewModel: ThreatsPanelViewModel
─ _aiAdapter: IAIAdapter (local por default)
+ DependencyProperty TabId
+ Event ExplainRequested(BlockEntry)
+ Event ReportRequested(BlockEntry)
+ Event AllowRequested(BlockEntry)
+ Event ExportRequested()
+ void RefreshForTab(string tabId)
```

```
ThreatsPanelViewModel
─ _tabBlocks: ObservableDictionary<string, List<BlockEntry>>  // por tabId
─ _groupedView: ObservableCollection<BlockGroup>
─ _eventBus: IEventBus
+ void OnSecurityVerdictPublished(SecurityVerdictEvent e)
+ void GroupByHost()
+ void CollapseAllExcept(string host)
+ IReadOnlyList<BlockEntry> GetBlocksForHost(string host)
```

```
BlockEntry
─ Host: string                       // "doubleclick.net"
─ FullUrl: string                    // la URL bloqueada
─ Kind: BlockKind                    // Tracker | Malware | Ads | Fingerprint | Script | Social
─ SubKind: string                    // "cross-site", "fingerprint", "pixel", ...
─ BlockedAt: DateTime
─ Source: BlockSource                // GoldenList | Malwaredex | AIEngine | UserRule | StaticList
─ IsMalwaredexHit: bool              // → marca en rojo
─ Confidence: int                    // 0-100, solo si Source=AIEngine
─ Explanation: string?               // null hasta que el usuario pida explicación
```

```
BlockGroup
─ Host: string
─ Count: int
─ TopKind: BlockKind                 // la categoría dominante del grupo
─ IsExpanded: bool
─ IsMalwaredexHit: bool
─ Entries: ObservableCollection<BlockEntry>
```

```
BlockExplanationService (VELO.Security)
─ _aiAdapter: IAIAdapter
─ _cache: IMemoryCache               // evita regenerar explicaciones repetidas
+ async Task<string> ExplainAsync(BlockEntry entry, CancellationToken ct)
─ string BuildPrompt(BlockEntry entry)
─ string LookupStaticTemplate(BlockKind kind, string subKind)  // fallback sin IA
```

**Fallback sin IA:** si `IAIAdapter` es Offline o no responde en 3s, usar la tabla estática de Security Panel v2 (sección 3.4 de Fase 2, 43 templates).

### 2.5 Flujo de agrupación

1. Al navegar a nueva URL (`NavigationStarting`), `ThreatsPanelViewModel` vacía los blocks del tab anterior si el tab ID cambió. Si la navegación es misma página (hash change), mantiene.
2. Cada `SecurityVerdictEvent` que llega con `tab.Id == currentTab.Id` y `verdict.Action == Block` crea un `BlockEntry` y se añade al dict.
3. El `ObservableCollection<BlockGroup>` se recomputa en el Dispatcher UI con debounce de 250ms (para no thrashear el panel cuando llegan 50 verdicts seguidos en un page load).
4. Agrupación por `Host` (si `FullUrl` es URL válida) o `"system"` si es un bloqueo no-URL (p.ej. clipboard).
5. Ordenación: rojos (Malwaredex) arriba, luego por `Count` descendente.
6. Por default solo el primer grupo está expandido (el resto colapsado con badge numérico).

### 2.6 Botón "Explicar" (IA)

Al pulsar "Explicar" en un `BlockEntry`:

1. Si `entry.Explanation` ya está cacheado → mostrar inline expandido.
2. Si no: spinner inline + llamada async a `BlockExplanationService.ExplainAsync(entry)`.
3. El servicio construye un prompt estructurado:

```
Eres un experto en privacidad web explicando a un usuario técnico pero no
especialista. Explica en 2-3 oraciones por qué se bloqueó esta request.

Dominio: {entry.Host}
URL: {entry.FullUrl}
Categoría: {entry.Kind}
Subtipo: {entry.SubKind}
Fuente del bloqueo: {entry.Source}

Si el bloqueo es un tracker conocido (p.ej. Google Analytics, Facebook Pixel),
menciona qué datos típicamente captura. Si es un fingerprint, explica qué
técnica usa. Si es malware, enfatiza el peligro y qué se evitó.

NO inventes capacidades técnicas. Si no estás seguro del propósito exacto,
di "este dominio aparece en listas de {X}".
```

4. Respuesta se cachea en `_cache` con key = `"{entry.Host}:{entry.Kind}:{entry.SubKind}"` por 24h.
5. Si `IAIAdapter` es Claude y el usuario no lo activó explícitamente para esta request, fallback a template estático.
6. Un pie de explicación indica la fuente: `"Explicación generada por {adapterName}. No es asesoramiento legal ni técnico definitivo."`

### 2.7 Botones adicionales por entry

- **Aprobar para este sitio:** añade regla a `UserAllowlist` específica de `currentDomain → entry.Host`. Se publica `UserAllowRuleAdded` en el EventBus. Ya NO se volverá a bloquear en este sitio hasta que se elimine desde Settings.
- **Reportar:** abre `MalwaredexWindow` precargado con la URL y pide confirmación. Si se confirma, `malwaredex.CaptureAsync()` con `UserReported = true`.
- **Copiar URL:** clipboard con la URL completa.

### 2.8 Exportar sesión

Botón "Exportar sesión JSON" en footer. Genera JSON con:

```json
{
  "session_id": "uuid",
  "exported_at": "2026-06-15T14:32:10Z",
  "tab_url": "https://currentpage.com",
  "total_blocks": 23,
  "by_host": [
    {
      "host": "doubleclick.net",
      "count": 9,
      "entries": [
        { "url": "...", "kind": "Tracker", "sub_kind": "cross-site", "blocked_at": "...", "source": "GoldenList" }
      ]
    }
  ]
}
```

Guardar en `Downloads/velo-session-{timestamp}.json`. No subir a ningún lado.

### 2.9 Integración con Security Panel v2 de Fase 2

El Security Panel v2 (panel flotante arriba-izquierda) sigue existiendo sin cambios para el resumen global. El Threats Panel v3 (pestaña derecha) es complementario: muestra el flujo detallado de la sesión actual. Comparten `BlockExplanationService`.

### 2.10 Tests obligatorios

`VELO.Security.Tests/BlockExplanationServiceTests.cs`:
1. `Explain_ReturnsStaticTemplate_WhenAIAdapterIsOffline`
2. `Explain_CachesResult_OnSecondCall`
3. `Explain_TimesOutAt3Seconds_AndFallsBackToStatic`
4. `Explain_PromptIncludesAllEntryFields`

`VELO.UI.Tests/ThreatsPanelViewModelTests.cs` (nuevo proyecto):
5. `GroupByHost_SortsMalwaredexHitsFirst`
6. `GroupByHost_SortsByCountDescending_WithinSameClass`
7. `Debounce_BatchesMultipleVerdicts_IntoSingleUIUpdate`
8. `TabChange_ClearsBlocksForOldTabId`

---

## 3. CONTEXT MENU IA

### 3.1 Concepto

El context menu (clic derecho) ya existe desde Fase 2 con categorías LINK/IMAGE/TEXT/PAGE. Fase 3 añade un **submenú "🤖 IA"** visible en los 4 contextos, con acciones que invocan al LLM (local por default, nube si el usuario lo activó).

### 3.2 Estructura del submenú IA

```
🤖 IA ▶
├─ [Texto seleccionado]
│  ├─ 💬 Explicar selección
│  ├─ 📝 Resumir en 3 líneas
│  ├─ 🌐 Traducir al español         (detecta idioma fuente)
│  ├─ 🔍 Buscar a fondo              (abre panel con resultados DDG + análisis IA)
│  ├─ ✅ Verificar hecho              (fact-check básico con disclaimer)
│  ├─ 📚 Definir palabra/concepto
│  ├─ 🎯 Explicar como si tuviera 5 años
│  ├─ 🔗 Extraer enlaces / emails / teléfonos
│  ├─ 🧮 Resolver (si es matemática o código)
│  └─ 💬 Preguntar a VeloAgent sobre esto…
│
├─ [Enlace]
│  ├─ 🔍 Explicar a dónde va este enlace
│  ├─ 🛡 Verificar seguridad con IA       (complementa Shield Score)
│  ├─ 📄 Previsualizar contenido (resumen sin abrir)
│  └─ 💬 Preguntar a VeloAgent sobre este enlace…
│
├─ [Imagen]
│  ├─ 👁 Describir imagen (alt-text IA)
│  ├─ 📖 OCR — extraer texto de la imagen
│  ├─ 🔎 Verificar origen (reverse image search → DDG images)
│  └─ 💬 Preguntar a VeloAgent sobre esta imagen…
│
└─ [Página, sin selección]
   ├─ 📝 Resumir página (TL;DR)
   ├─ 🔑 Puntos clave (bullets)
   ├─ 🌐 Traducir página al español
   ├─ 🎯 Explicar nivel ELI5
   ├─ 🧠 Modo estudio (preguntas de repaso)
   └─ 💬 Preguntar a VeloAgent sobre esta página…
```

### 3.3 Arquitectura — clases nuevas

```
AIContextActions (VELO.Agent)
─ _adapter: IAIAdapter
─ _sanitizer: IContentSanitizer          (reutilizado de Fase 2 VeloAgent)
─ _readerExtractor: IReaderExtractor     (reutilizado de Reader Mode)
+ async Task<string> ExplainAsync(string text, Language targetLang, CancellationToken ct)
+ async Task<string> SummarizeAsync(string content, int maxLines, CancellationToken ct)
+ async Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct)
+ async Task<string> FactCheckAsync(string claim, CancellationToken ct)
+ async Task<string> DefineAsync(string term, CancellationToken ct)
+ async Task<string> SimplifyAsync(string text, CancellationToken ct)
+ async Task<IReadOnlyList<Extraction>> ExtractAsync(string text, ExtractionKind kind)
+ async Task<string> DescribeImageAsync(byte[] imageBytes, CancellationToken ct)
+ async Task<string> OcrAsync(byte[] imageBytes, CancellationToken ct)
+ async Task<string> DeepSearchAsync(string query, CancellationToken ct)
```

```
AIContextMenuBuilder : ContextMenuBuilder  (extiende el de Fase 2)
─ _actions: AIContextActions
─ _resultWindow: AIResultWindow (singleton per main window)
+ override void BuildLinkMenu(...)     — añade submenú IA
+ override void BuildImageMenu(...)
+ override void BuildTextMenu(...)
+ override void BuildPageMenu(...)
```

```
AIResultWindow : Window (VELO.UI.Dialogs)
─ _viewModel: AIResultViewModel
+ property ResultText: string
+ property SourceText: string        (el contexto original, para referencia)
+ property AdapterUsed: string       (Ollama / Claude / LLamaSharp)
+ button "Copiar"
+ button "Preguntar seguimiento"   (abre VeloAgent con contexto)
+ button "Regenerar"
```

### 3.4 Flujo por acción

**Texto seleccionado → Explicar selección:**

1. Usuario selecciona `"posit-to-posit floating point arithmetic"` → clic derecho → IA → Explicar selección.
2. `AIContextMenuBuilder` obtiene `target.HasSelection == true` y `target.SelectionText`.
3. Llama `AIContextActions.ExplainAsync(text, targetLang: UI.CurrentLang, ct)`.
4. El prompt enviado al LLM:

```
Eres un tutor experto. Explica en 3-4 oraciones el siguiente término o
concepto para alguien que entiende tecnología pero no es especialista.

Texto: "{selection}"

Si es un término muy técnico, da también un ejemplo concreto. Si la selección
es una frase larga, identifica el concepto central y explica ese.

Responde en {targetLang}. No inventes si no lo conoces — di explícitamente
"no tengo información confiable sobre X".
```

5. Se abre `AIResultWindow` con spinner. Tras la respuesta, se muestra + botón "Preguntar seguimiento" que abre VeloAgent con el contexto precargado.

**Enlace → Explicar a dónde va:**

1. Obtiene `target.LinkUri` y `target.LinkText`.
2. Hace HEAD request al dominio (con WebView2, sin ejecutar scripts) para obtener status y Content-Type.
3. Consulta `SafetyScorer` para obtener Shield Level.
4. Envía al LLM contexto enriquecido:

```
Analiza este enlace y explica al usuario a dónde le lleva y qué es.

URL: {linkUri}
Texto del enlace: "{linkText}"
Dominio: {host}
TLS: {tlsStatus}
Shield Score de VELO: {level}
En Golden List: {yesNo}
En Malwaredex: {yesNo}

Explica en 2 oraciones:
1. Qué es este sitio (si es conocido)
2. Si es seguro ir (basado en los datos de arriba, NO inventes)
```

5. Respuesta en `AIResultWindow` con un enlace "Abrir en nueva pestaña" y "Abrir en container temporal".

**Imagen → Describir imagen:**

1. Descarga la imagen al stream en memoria (límite 5 MB).
2. Si `_adapter` no soporta visión (`adapter.Capabilities.HasFlag(Vision) == false`), muestra mensaje "Tu adaptador IA actual no soporta imágenes. Activa Claude en Settings o usa un modelo local con visión (llava, moondream)".
3. Si sí soporta: envía como base64 con prompt `"Describe esta imagen en 2-3 oraciones. Identifica texto si lo hay, objetos principales y contexto. No inventes."`

**Página → TL;DR:**

1. Llama `ReaderExtractor.ExtractAsync(currentUrl)` para obtener el `article.content` limpio (reutiliza el Reader Mode de Fase 2).
2. Si el contenido supera el context window del modelo local (típicamente 4k-8k tokens para llama3-8b), aplica map-reduce: divide en chunks de 2000 tokens, resume cada uno, luego resume los resúmenes.
3. Resultado en `AIResultWindow` con toggle "Expandir → 10 puntos clave / Colapsar → 3 líneas".

### 3.5 Indicador visual del adaptador usado

Cada invocación IA muestra en el header de `AIResultWindow` un chip:

- `🖥 Local (Ollama · llama3:8b)` — verde
- `🖥 Local (LLamaSharp embebido)` — verde
- `🌐 Claude (nube — API Anthropic)` — amarillo con tooltip "Esta consulta salió de tu dispositivo. Puedes desactivar Claude en Settings → IA."

### 3.6 Política de uso en la nube

- Si `Settings.AiMode == "Offline"` o `"Custom"` (= Ollama local): todas las acciones IA van al adaptador local.
- Si `Settings.AiMode == "Claude"`: todas las acciones IA van a Claude, con el chip amarillo visible.
- **Las acciones que tocan contenido potencialmente sensible** (descripción de imagen de página, OCR, fact-check sobre texto seleccionado) muestran confirmación la primera vez con Claude: `"Esta acción enviará {N} caracteres a la API de Anthropic. ¿Continuar? [Sí] [Sí y no preguntar más] [No]"`.
- Hay un botón global en la top bar: `"🖥 Modo IA: Local"` / `"🌐 Modo IA: Claude"` que alterna el modo para la sesión actual sin tocar Settings.

### 3.7 Tests obligatorios

`VELO.Agent.Tests/AIContextActionsTests.cs`:
1. `Explain_UsesLocalAdapter_WhenModeOffline`
2. `Explain_UsesClaudeAdapter_WhenModeClaude`
3. `Summarize_MapReduceKicks_WhenContentExceedsContextWindow`
4. `DescribeImage_ReturnsCapabilityError_WhenAdapterLacksVision`
5. `Extract_Links_ParsesCorrectlyFromPlainText`
6. `FactCheck_IncludesDisclaimerInResponse`
7. `Translate_DetectsSourceLang_WhenNotSpecified`

`VELO.UI.Tests/AIContextMenuBuilderTests.cs`:
8. `BuildTextMenu_AddsAISubmenu_WhenSelectionHasMinLength`
9. `BuildTextMenu_DoesNotAddAISubmenu_WhenSelectionIsEmpty`
10. `BuildImageMenu_DisablesVisionActions_WhenAdapterLacksVision`

---

## 4. IMPORT CHROME / FIREFOX / EDGE

### 4.1 Concepto

Wizard de primera ejecución (o accesible desde Settings → Import) que detecta los navegadores instalados, lee sus bases de datos locales, y migra:
- Bookmarks (completo)
- Historial (últimos 90 días)
- Contraseñas (con confirmación usuario + Windows Data Protection API)
- Cookies (opt-in, advertencia de riesgo)
- Search engines personalizados

### 4.2 Arquitectura

```
BrowserImportService (VELO.Import — proyecto nuevo)
─ _detectors: IBrowserDetector[]
+ Task<IReadOnlyList<DetectedBrowser>> DetectInstalledAsync()
+ Task<ImportResult> ImportAsync(DetectedBrowser browser, ImportOptions opts, IProgress<int> progress)
```

```
IBrowserDetector
+ string Name
+ Task<DetectedBrowser?> DetectAsync()
```

Implementaciones:
- `ChromeDetector` — `%LOCALAPPDATA%\Google\Chrome\User Data\Default\`
- `EdgeDetector` — `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\`
- `FirefoxDetector` — `%APPDATA%\Mozilla\Firefox\Profiles\*.default*\`
- `BraveDetector`, `OperaDetector`, `VivaldiDetector` — bonus, todos Chromium-based

### 4.3 Lectura de bases de datos

**Chrome/Edge (Chromium):**
- Bookmarks: `Bookmarks` JSON file — parsear directamente
- History: `History` SQLite — `SELECT url, title, visit_time FROM urls ORDER BY visit_time DESC LIMIT 1000`
- Passwords: `Login Data` SQLite — cifrado con `CryptProtectData` (DPAPI); descifrar con `CryptUnprotectData` de la key en `Local State` → `encrypted_key`, que a su vez se descifra con DPAPI del usuario actual
- Cookies: `Cookies` SQLite — misma cifra DPAPI

**Firefox:**
- Bookmarks + History: `places.sqlite` — tabla `moz_places`, `moz_bookmarks`
- Passwords: `logins.json` + `key4.db`. Usa `NSS` (libnss3.dll) para descifrar — requiere copiar las DLLs de Firefox al temp y cargar via P/Invoke. **Complicado; considerar usar `firefox_decrypt` como referencia y reescribir en C#.**

### 4.4 UI del wizard

```
┌────────────────────────────────────────────┐
│ 📥 Importar de otro navegador              │
├────────────────────────────────────────────┤
│ Detectamos los siguientes navegadores:     │
│                                            │
│  ⦿ Google Chrome   (perfil "Default")     │
│  ○ Microsoft Edge  (perfil "Default")     │
│  ○ Firefox         (perfil "default-XX")  │
│                                            │
│ ¿Qué quieres importar?                     │
│  ☑ Marcadores          (127 items)         │
│  ☑ Historial           (últimos 90 días)   │
│  ☐ Contraseñas         (23 items) 🔒       │
│  ☐ Cookies             (⚠ riesgo)          │
│  ☑ Motores de búsqueda (3)                 │
│                                            │
│ [ Cancelar ]       [ Importar →  ]         │
└────────────────────────────────────────────┘
```

### 4.5 Advertencias al usuario

- **Contraseñas:** "Las contraseñas se leerán del almacén cifrado de Chrome/Edge/Firefox. VELO nunca las envía a ningún servidor; se guardan en tu Vault local encriptado. ¿Continuar?"
- **Cookies:** "Importar cookies mantiene tus sesiones abiertas (Gmail, Netflix…) pero también puede importar trackers de los sitios. No recomendado salvo que lo necesites. ¿Continuar?"
- **Chrome abierto:** si Chrome está en ejecución, mostrar "Cierra Chrome antes de importar para evitar bases bloqueadas." (o copiar el `Login Data` a temp primero para evitar el lock).

### 4.6 Mapeo a tablas VELO

- `bookmarks.Bookmark` → `BookmarkRepository.SaveAsync`
- `history.urls` → `HistoryRepository.SaveAsync` (con `importedFrom: "chrome"` marker)
- `login_data.logins` → `VaultService.AddAsync`
- Cookies → inyectar en `CoreWebView2.CookieManager.AddOrUpdateCookie()`

### 4.7 Tests obligatorios

`VELO.Import.Tests/`:
1. `ChromeDetector_FindsProfile_WhenInstalled`
2. `ChromeDetector_ReturnsNull_WhenNotInstalled`
3. `BookmarksImporter_ParsesNestedFolders`
4. `HistoryImporter_ConvertsChromeTimestamp_ToUtc` (Chrome usa microsegundos desde 1601)
5. `PasswordImporter_DecryptsViaDPAPI_WhenSameUser`
6. `PasswordImporter_ReturnsError_WhenDifferentUser`

---

## 5. PASSWORD AUTOFILL + HIBP BREACH CHECK

### 5.1 Concepto

Inyectar script en cada página que:
1. Detecta formularios de login (`<input type="password">`).
2. Busca credenciales del dominio actual en `VaultService`.
3. Si hay match, muestra overlay "VELO: Autofill disponible ▾" encima del campo.
4. Al clicar, rellena usuario + password.
5. Al guardar nueva contraseña (submit del form), ofrece añadir al Vault.
6. Verifica periódicamente contra HIBP k-anonymity API y avisa de contraseñas comprometidas.

### 5.2 Arquitectura

```
AutofillService (VELO.Vault)
─ _vault: VaultService
─ _hibp: HibpClient
+ async Task<IReadOnlyList<AutofillSuggestion>> GetSuggestionsAsync(string domain)
+ async Task SaveNewCredentialAsync(string domain, string username, string password, bool autoDetected)
+ async Task<BreachStatus> CheckBreachAsync(string password, CancellationToken ct)
```

```
HibpClient (VELO.Vault.Security)
+ async Task<int> GetBreachCountAsync(string password, CancellationToken ct)
  // k-anonymity: SHA1(password) → envía los primeros 5 chars al API de HIBP;
  // servidor responde con todos los hashes que empiezan igual; comparamos localmente.
  // La password NUNCA sale del dispositivo en ninguna forma.
```

```
AutofillBridge (VELO.UI.Scripts)
─ WebMessage bridge entre JS inyectado y WPF
  [input detectado] → "autofill-request" → WPF consulta Vault → devuelve sugerencias
  [form submit]     → "autofill-capture" → WPF ofrece guardar
```

### 5.3 Script inyectado (`autofill-bridge.js`)

```javascript
(function() {
  'use strict';
  const VELO_API = window.chrome?.webview?.postMessage;
  if (!VELO_API) return;

  // Detecta inputs password cada 500ms (para SPA que los añaden dinámicamente)
  setInterval(scanForPasswordInputs, 500);

  function scanForPasswordInputs() {
    const inputs = document.querySelectorAll('input[type="password"]:not([data-velo-hooked])');
    inputs.forEach(hookInput);
  }

  function hookInput(input) {
    input.setAttribute('data-velo-hooked', '1');
    const form = input.closest('form');
    const usernameField = findUsernameField(form);

    // Overlay "VELO Autofill"
    const overlay = buildOverlay(input);
    positionOverlay(overlay, input);
    overlay.addEventListener('click', () => requestAutofill(usernameField, input));

    // Captura submit
    if (form) form.addEventListener('submit', () => captureCredentials(usernameField, input));
  }

  function requestAutofill(userField, passField) {
    VELO_API({
      type: 'autofill-request',
      domain: location.hostname,
      userFieldId: userField?.id || userField?.name,
      passFieldId: passField.id || passField.name,
    });
  }

  // WPF responde via postMessage con { type: 'autofill-fill', username, password }
  window.chrome.webview.addEventListener('message', (e) => {
    if (e.data.type !== 'autofill-fill') return;
    applyFill(e.data);
  });

  function captureCredentials(userField, passField) {
    VELO_API({
      type: 'autofill-capture',
      domain: location.hostname,
      username: userField?.value,
      password: passField.value,
    });
  }

  // ...helpers...
})();
```

### 5.4 UI del overlay

Pill flotante alineada al borde derecho del input password, no interfiere con el diseño del sitio:

```
┌─────────────────────────────────┐
│ 🔑 Autofill (2) ▾               │  ← al hover
└─────────────────────────────────┘
     ▼ dropdown
     ┌─────────────────────────────┐
     │ 👤 pedro@miempresa.com     │
     │ 👤 pedro.personal@gmail    │
     │ ─────                       │
     │ ⚙ Gestionar Vault           │
     └─────────────────────────────┘
```

### 5.5 Modal de captura (post-submit)

Tras detectar un submit con credenciales nuevas:

```
┌─────────────────────────────────────┐
│ 🔐 Guardar en VELO Vault            │
├─────────────────────────────────────┤
│ ¿Guardar esta contraseña?           │
│                                     │
│ Sitio: github.com                   │
│ Usuario: pedro@miempresa.com        │
│ Contraseña: ●●●●●●●●●●● [👁]        │
│                                     │
│ 🛡 Verificación HIBP:                │
│ ✅ Esta contraseña NO aparece       │
│    en bases de datos filtradas.     │
│                                     │
│ ☑ Avisar si esta contraseña se     │
│   filtra en el futuro               │
│                                     │
│ [ No guardar ]   [ Guardar ]        │
└─────────────────────────────────────┘
```

Si HIBP reporta filtraciones:

```
│ 🛡 Verificación HIBP:                │
│ ⚠ Esta contraseña aparece en        │
│   4,238 filtraciones públicas.      │
│   Considera usarla solo si no la   │
│   has usado en otros sitios.       │
```

### 5.6 Políticas y privacidad

- HIBP se consulta con **k-anonymity**: se envían solo los primeros 5 caracteres del hash SHA1 de la password. La password nunca sale en claro. La API de HIBP responde con una lista de sufijos; la comparación se hace en local.
- El check HIBP es opt-in al guardar una contraseña. Hay un toggle global "Verificar contraseñas contra HIBP" en Settings que, si está off, desactiva toda consulta al API.
- Verificación periódica de todo el Vault: al abrir VaultWindow, muestra badge 🔴 sobre las entradas comprometidas. Esto consulta HIBP por cada entrada, serializado con rate limit.

### 5.7 Tests obligatorios

`VELO.Vault.Tests/AutofillServiceTests.cs`:
1. `GetSuggestions_ReturnsAllCredentialsForDomain`
2. `GetSuggestions_IncludesSubdomains_WhenPolicyAllows`
3. `GetSuggestions_DoesNotLeakAcrossUnrelatedDomains`
4. `SaveNewCredential_DeduplicatesIdenticalUsernamePassword`
5. `HibpClient_OnlySendsFirst5CharsOfHash`
6. `HibpClient_ParsesResponseCorrectly`
7. `HibpClient_ReturnsZero_WhenPasswordNotInResponse`

---

## 6. SESSION RESTORE + CRASH RECOVERY

### 6.1 Concepto

Las pestañas abiertas sobreviven el cierre de la aplicación. Al reabrir, VELO restaura el estado anterior (opcional, preguntado la primera vez). También recupera tras crash.

### 6.2 Arquitectura

```
SessionService (VELO.App.Session)
─ _settings: SettingsRepository
─ _sessionFile: string  // %LocalAppData%\VELO\session.json
+ async Task SnapshotAsync(SessionSnapshot snap)
+ async Task<SessionSnapshot?> LoadLastAsync()
+ async Task ClearAsync()
```

```
SessionSnapshot
─ Version: int                          // para migrations futuras
─ SavedAt: DateTime
─ WasCleanShutdown: bool                // false = crash recovery
─ Windows: List<WindowSnapshot>
```

```
WindowSnapshot
─ Bounds: Rect
─ WorkspaceStrip: List<WorkspaceSnapshot>
─ Tabs: List<TabSnapshot>
─ ActiveTabId: string
```

```
TabSnapshot
─ Id: string
─ Url: string
─ Title: string
─ ContainerId: string
─ WorkspaceId: string
─ ScrollY: int
─ LastActiveAt: DateTime
```

### 6.3 Flujo

- Cada 30s, `SessionService.SnapshotAsync()` escribe el estado actual al `session.json` con `WasCleanShutdown = false`.
- En `Window_Closing` (cierre normal), escribe un snapshot final con `WasCleanShutdown = true`.
- Al arrancar, `App.OnStartup` mira el archivo:
  - Si `WasCleanShutdown = false` → muestra diálogo "VELO se cerró inesperadamente la última vez. ¿Restaurar las {N} pestañas abiertas?"
  - Si `WasCleanShutdown = true` y Setting `session.restore_always = true` → restaura sin preguntar.
  - Si no → ignora el snapshot y arranca limpio.
- **Modo Paranoid/Bunker:** nunca guarda snapshot. La sesión muere con la app.

### 6.4 Optimizaciones

- Si hay >30 tabs, solo hidrata los primeros 5 en `EnsureWebViewInitializedAsync`; el resto quedan como "placeholder" hasta que el usuario haga clic. Esto evita que al restaurar VELO consuma 3 GB RAM al arrancar.
- El snapshot omite tabs en container "banking" o "temporal-TTL" (seguridad).

### 6.5 Tests

1. `Snapshot_IncludesAllOpenTabs`
2. `Snapshot_OmitsBankingContainerTabs`
3. `Snapshot_OmitsTemporalContainerTabs`
4. `LoadLast_ReturnsNull_WhenFileMissing`
5. `LoadLast_ReturnsNull_WhenFileCorrupt`
6. `LoadLast_DetectsUncleanShutdown`

---

## 7. VELOAGENT v2 — ACCIONES CONTEXTUALES

### 7.1 Concepto

El panel VeloAgent de Fase 2 (chat libre) se amplía con **comandos de página**:
- `/tldr` — resume la página activa
- `/explicar` — explica la página en nivel básico
- `/preguntas N` — genera N preguntas de comprensión sobre la página
- `/traducir <lang>` — traduce la página activa
- `/buscar <query>` — busca en la página activa
- `/extraer emails|links|phones|code`
- `/analizar` — análisis crítico (sesgo, tono, credibilidad de fuentes)

Además: botón flotante "💬 Preguntar sobre esta página" en la toolbar que pre-carga el contexto.

### 7.2 Implementación

`VeloAgentPanel` (existe desde Fase 2) añade:

```csharp
private static readonly Dictionary<string, Func<string[], Task<string>>> _slashCommands = new()
{
    ["/tldr"]     = args => _actions.SummarizeAsync(GetCurrentPageContent(), maxLines: 5, CT.None),
    ["/explicar"] = args => _actions.SimplifyAsync(GetCurrentPageContent(), CT.None),
    ["/traducir"] = args => _actions.TranslateAsync(GetCurrentPageContent(), args[0], CT.None),
    // ...
};
```

El `GetCurrentPageContent()` reusa `ReaderExtractor` de Fase 2.

### 7.3 Modo conversacional con memoria de página

Si el usuario abre el panel con "Preguntar sobre esta página" precargado, la primera interacción injecta en el system prompt:

```
Eres VeloAgent. El usuario está viendo la siguiente página:

URL: {url}
Título: {title}
Contenido extraído (Reader Mode):
---
{first 4000 tokens}
---

Responde preguntas sobre este contenido. Si la pregunta requiere información
que no está en el contenido, di "no lo puedo responder solo con esta página"
y sugiere buscar. NUNCA inventes hechos sobre la página.
```

Las siguientes preguntas mantienen el contexto. Al cambiar de tab, se añade un separador "── Contexto cambiado a {newUrl} ──" en vez de resetear el chat.

### 7.4 Tests

1. `SlashCommand_Tldr_InvokesSummarize`
2. `SlashCommand_Unknown_FallsBackToGeneralChat`
3. `PageContext_InjectedAsSystemPrompt_OnFirstMessage`
4. `PageContext_MarksSwitchOnTabChange`

---

## 8. AUTO-UPDATE SEGURO (sin firma Authenticode)

### 8.1 Concepto

Sin certificado Authenticode, no podemos firmar el instalador. Pero podemos:
1. Publicar `SHA256SUMS.txt` en la release (ya lo hace CI).
2. El cliente descarga el installer nuevo, verifica el hash contra el publicado en la API de GitHub.
3. Pide confirmación al usuario antes de ejecutar.

### 8.2 Flujo

1. `UpdateChecker` (con opt-in de Fase 2) detecta versión nueva.
2. `ShowUpdateToast` ahora tiene botón "Descargar e instalar".
3. Al hacer clic:
   - Descarga el `.exe` a `%TEMP%\velo-update-{version}.exe`
   - Descarga `SHA256SUMS.txt` de la misma release
   - Calcula SHA256 del exe y verifica que coincide con el publicado
   - Si coincide → muestra modal:
     ```
     ✅ Descarga verificada.
     Hash SHA256: a1b2c3...
     
     ⚠ Este instalador NO tiene firma Authenticode.
        Windows SmartScreen mostrará una advertencia.
        Esto es normal mientras VELO no tenga un certificado comercial.
     
     [ Instalar ahora ]  [ Cancelar ]
     ```
   - Al confirmar: ejecuta el exe con `/SILENT /CLOSEAPPLICATIONS` (Inno Setup flags) y hace `Shutdown()`.
4. Si el hash NO coincide → elimina el archivo, muestra error "Descarga corrupta. Intenta de nuevo desde el sitio."

### 8.3 Tests

1. `UpdateDownloader_VerifiesHashBeforeExecuting`
2. `UpdateDownloader_DeletesFileOnHashMismatch`
3. `UpdateDownloader_RespectsUserCancel`

---

## 9. REFACTOR `MainWindow` + `BrowserTab`

### 9.1 Problema actual

- `MainWindow.xaml.cs`: 1830 líneas. Mezcla de:
  - Gestión de tabs (CreateTab, ActivateTab, CloseTab handlers, tear-off, split view, tab sidebar hookup)
  - Lógica de seguridad (verdict subscribers, shield score sync, inspector launch, privacy receipt)
  - Comandos (CommandBar builder, keyboard shortcuts, main menu)
  - Agente (panel wiring, mode switching)
  - Settings / bootstrapping
  - WebView2 environment setup
- `BrowserTab.xaml.cs`: 1300 líneas. Mezcla de:
  - WebView init (settings, scripts, hooks)
  - Guard pipeline (nav, download, resource, cert)
  - Context menu
  - Paste/Glance/AboutPage
  - Zoom, find, print

### 9.2 Propuesta de split

**MainWindow se divide en:**

```
MainWindow.xaml.cs (~350 líneas, solo shell y wiring)
├─ TabOrchestrator.cs (~400)    — gestión de tabs (create/activate/close/move)
├─ SecurityController.cs (~350) — suscripciones a verdicts, shield score, inspector
├─ CommandController.cs (~200)  — CommandBar, keyboard shortcuts
├─ AgentController.cs (~150)    — wiring del VeloAgent panel
└─ WindowLifecycle.cs (~200)    — bootstrapping, session restore, closing
```

**BrowserTab se divide en:**

```
BrowserTab.xaml.cs (~300 líneas, solo UserControl wiring)
├─ BrowserTabWebViewHost.cs (~250) — init de WebView2, settings, scripts
├─ BrowserTabGuardPipeline.cs (~300) — todos los handlers de guards
├─ BrowserTabContextMenu.cs (~200)   — OnContextMenuRequested + WPF menu building
├─ BrowserTabNavigation.cs (~200)    — Go/Back/Forward/Reload, NavigateAsync
└─ BrowserTabUtilities.cs (~100)     — zoom, find, print, about page
```

### 9.3 Reglas del refactor

- **Test tras cada extracción.** Tras mover cada clase, correr los 56 tests existentes. Si alguno falla, rollback y retry.
- **No cambiar comportamiento.** Nada de "ya que estoy, arreglo esto". Los bugs se arreglan en commits separados.
- **APIs públicas intactas.** `MainWindow` y `BrowserTab` siguen exponiendo las mismas propiedades/eventos/métodos públicos que antes.
- **Campos compartidos → interfaces.** Si `TabOrchestrator` y `SecurityController` necesitan lo mismo, se crea una interfaz pequeña, no se pasa `MainWindow` como "god object".
- **Commit granular.** Un commit por clase extraída. Mensaje: `refactor: extract TabOrchestrator from MainWindow`.

### 9.4 Tests de regresión

Antes del refactor, se añaden tests de caracterización (characterization tests):
- `MainWindow_CanOpenAndCloseTabs_Lifecycle`
- `MainWindow_SplitView_TogglesCorrectly`
- `BrowserTab_NavigatesToUrl`
- `BrowserTab_ContextMenuAppearsOnRightClick`

Estos tests NO se borran tras el refactor — se mantienen como guard.

---

## 10. INTEGRATION TESTS DE WEBVIEW2

### 10.1 Concepto

Los unit tests cubren lógica pura. Los bugs reales de Fase 2 (clic derecho no funcionaba, tab tearoff cerraba todo) no hubieran sido detectados por unit tests — son bugs de integración con WebView2.

### 10.2 Enfoque

Usar **Microsoft.Web.WebView2 en modo headless** con un `HttpListener` local sirviendo páginas HTML de prueba. Cada test:

1. Arranca `HttpListener` en puerto dinámico.
2. Crea un `CoreWebView2Environment` con user-data temp dir.
3. Crea un `CoreWebView2Controller` asociado a un `Window` oculto.
4. Navega a `http://127.0.0.1:{port}/test-{name}.html`.
5. Verifica comportamiento esperado.
6. Cleanup: dispose controller, cierra listener, borra temp dir.

### 10.3 Tests iniciales

```
VELO.Integration.Tests (proyecto nuevo)
├─ ContextMenuTests
│  ├─ RightClick_FiresContextMenuRequestedEvent
│  ├─ RightClick_ShowsCustomMenu_WhenAreDefaultContextMenusEnabledIsTrue
│  └─ RightClick_OnLink_BuildsLinkContextMenu
├─ NavigationGuardTests
│  ├─ Navigate_ToMalwaredexHit_BlocksAndShowsInterstitial
│  ├─ Navigate_ToGoldenList_SetsGoldShield
│  └─ Navigate_ToHttp_ShowsMixedContentWarning
├─ AutofillTests
│  ├─ Autofill_InjectsOverlay_OnPasswordInput
│  ├─ Autofill_CapturesCredentials_OnFormSubmit
│  └─ Autofill_DoesNotTriggerOnCrossDomainIframe
└─ SessionTests
   ├─ TearOffTab_OpensNewWindow_WithSameTab
   ├─ TearOffTab_LastTab_DoesNothing
   └─ CloseTornOffWindow_DoesNotShutdownApp
```

### 10.4 Target de cobertura Fase 3

**56 unit tests (Fase 2) + 60 nuevos unit tests + 20 integration tests = 136 tests al cierre de Fase 3.**

---

## 11. SETTINGS NUEVOS DE FASE 3

Añadir a `SettingKeys.cs`:

```csharp
public const string SessionRestoreMode     = "session.restore_mode";  // "ask" | "always" | "never"
public const string AutofillEnabled        = "autofill.enabled";      // default true
public const string AutofillAutoSubmit     = "autofill.auto_submit";  // default false
public const string HibpEnabled            = "hibp.enabled";          // default true
public const string AiModeOverride         = "ai.session_override";   // "local" | "cloud" | null
public const string ImportConfirmedOnce    = "import.shown_wizard";   // bool
public const string AutoUpdateDownload     = "updates.auto_download"; // default false
public const string ThreatsPanelGroupMode  = "threats.group_mode";    // "host" | "kind" | "time"
```

Todos los nuevos settings se exponen en `SettingsWindow` en una nueva sección "Fase 3" (temporal, se reorganizará al final).

---

## 12. CRONOGRAMA Y ORDEN DE SPRINTS

| Sprint | Módulos | Días | Entregable |
|---|---|---|---|
| 1 | Refactor MainWindow + BrowserTab (#9) + characterization tests | 6-8 | v3.0.0-alpha1: sin features nuevos, código mantenible |
| 2 | Threats Panel v3 (#2) + Context Menu IA (#3) | 9-12 | v3.0.0-alpha2: las dos features que pidió el usuario |
| 3 | Import Chrome/Firefox/Edge (#4) | 6-8 | v3.0.0-alpha3: migración real desde Chrome |
| 4 | Autofill + HIBP (#5) | 8-10 | v3.0.0-beta1: la killer feature para uso diario |
| 5 | Session Restore (#6) + Auto-update (#8) | 6-8 | v3.0.0-beta2: VELO sobrevive crashes y se actualiza solo |
| 6 | VeloAgent v2 acciones contextuales (#7) | 6-8 | v3.0.0-rc1: IA útil por todas partes |
| 7 | Integration tests WebView2 (#10) + polish final + docs | 6-8 | v3.0.0: release público |

**Total:** 47-62 días. Full-time: 10-12 semanas. Part-time: 18-24 semanas.

### Checkpoints con Opus

Al final de cada sprint, Sonnet:
1. Prepara un "PR de cierre" con diff resumido + changelog del sprint
2. Genera tabla "módulos especificados vs implementados" con enlaces a los commits
3. Ejecuta los tests y pega el output
4. Pide a Opus que revise contra la sección correspondiente del spec

Opus responde con:
- ✅ Aprobado para merge
- ⚠ Aprobado con observaciones (fix en sprint siguiente)
- 🚫 Rechazado (fix antes de avanzar)

---

## 13. ACCEPTANCE CRITERIA GLOBALES

### 13.1 Al cierre de cada sprint

- [ ] Todos los tests del sprint verdes
- [ ] No se rompió ningún test existente
- [ ] Review de Opus firmada
- [ ] CHANGELOG.md actualizado con las entradas del sprint
- [ ] Si hay settings nuevos, aparecen en SettingsWindow con i18n

### 13.2 Al cierre de Fase 3 (v3.0.0)

- [ ] Los 136+ tests pasando
- [ ] 0 telemetría (grep por `http`, `api.`, `analytics` en src/ → solo endpoints en config injectable)
- [ ] Import Chrome/Firefox/Edge funciona end-to-end
- [ ] Autofill funciona en los top 20 sitios del mundo (manual QA checklist)
- [ ] Session Restore sobrevive `taskkill /F` a VELO
- [ ] Threats Panel muestra historia completa, no solo último
- [ ] Context Menu IA funciona con Ollama local + Claude (con indicador visible)
- [ ] Auto-update verifica SHA256 y pide confirmación
- [ ] Refactor MainWindow/BrowserTab completo — ningún archivo >500 líneas en src/VELO.App o src/VELO.UI/Controls
- [ ] Release con CI: `v3.0.0`, installer + portable + SBOM + SHA256SUMS publicados
- [ ] Landing page actualizada a v3.0.0 en 5 idiomas
- [ ] `docs/Phase3/README.md` con la historia de cada sprint
- [ ] Memory file actualizado

### 13.3 Reglas de escape

Si en cualquier sprint Sonnet se queda atascado >2 días en un bloqueo técnico:
1. Documenta el bloqueo en `docs/Phase3/blockers.md`
2. Pide revisión a Opus
3. Si no hay camino claro, **propone recortar scope del módulo** antes que bajar calidad
4. Nunca "envolver con try-catch y seguir" para sortear un bug real

---

## 14. PENDIENTES DE FASE 2 QUE NO SE HEREDAN

Estos ítems NO son parte de Fase 3 pero se documentan para tenerlos en mente:

- **Firma Authenticode (SignPath Foundation):** pendiente de aprobación. Cuando llegue, añadir `SIGNPATH_API_TOKEN` secret + `SIGNPATH_ORG_ID` variable en GitHub. El workflow `release.yml` ya tiene el job `sign` preparado.
- **Screenshots de la landing:** 4 imágenes a añadir en `docs/images/` (hero, inspector, tabs, malwaredex). Tarea no-código, no bloqueante.
- **Traducción de la landing a más idiomas:** actualmente EN/ES/FR/DE/PT. Posibles añadidos: ZH, RU, JA.

---

## 15. FIN DEL DOCUMENTO

Este documento sucede a `docs/Phase2/VELO_FASE2_v3_DOCUMENTACION.md` y mantiene el mismo contrato con el ejecutor:

- Sonnet construye siguiendo este spec al pie de la letra
- Opus revisa y firma cada sprint
- El usuario valida cada beta y dirige ajustes
- Ninguna feature se merge sin tests + review de Opus
- La privacidad y la ausencia de telemetría son inviolables

**Siguiente paso:** crear `docs/Phase3/README.md` con el índice de sprints y el estado de cada uno. Al arrancar Sprint 1, abrir rama `phase3/sprint1-refactor` desde `main`.

---

> VELO es software libre bajo GNU AGPLv3. Este documento es parte del proyecto y también AGPLv3.
