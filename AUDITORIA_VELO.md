# AUDITORÍA VELO — Consolidada (4 lentes)

> **Estado 2026-07-06 (post v2.4.61):** los 6 hallazgos 🔴 fueron atacados en v2.4.59/v2.4.60 (F-2/AS-1/AS-2/C-1/C-2 cerrados; **F-1 sigue abierto** → `BACKLOG.md` P0). v2.4.61 cerró los 🟠 de piso usable: **F-3** (PermissionRequested per-site), **R-1** (ProcessFailed auto-recovery), **AS-3** (denylist RCE + sin session-grant) y **QW-6/DEAD-1**. Pendientes: AS-4 (JS interpolación, mitigado por aprobación humana), A-2/A-3 estructura (refactors §6.2), F-4/R-3/R-4 menores. Plan vivo: `PLAN_VELO.md`.

**Fecha:** 2026-06-06 · **HEAD:** `1c83e96` (v2.4.58) · **Auditor:** Claude (Fable 5)
**Tesis (vara de medir):** *"VELO es el navegador Windows donde tu IA vive localmente."*

## Índice
1. [Tabla maestra de hallazgos](#tabla-maestra) ← empezá por acá
2. [Lente A — Arquitectura y limpieza](#lente-a)
3. [Lente B — Funcional / uso diario real](#lente-b)
4. [Lente C — Seguridad / superficie de ataque](#lente-c)
5. [Lente D — Resiliencia y recursos](#lente-d)
6. [Plan de acción consolidado](#plan)
7. [Decisiones que necesito de BADIZ](#decisiones)

> Nota: H1/M1/A1/T1 ya se arreglaron en v2.4.58 (`memory/audit_2026-06.md`). Este doc parte del código ya parchado.

---

<a name="tabla-maestra"></a>
## 1. TABLA MAESTRA DE HALLAZGOS

Ordenada por severidad × frecuencia de uso real. Esta es la única lista que importa para "qué arreglar primero".

| ID | Sev | Lente | Hallazgo | Ubicación | Rompe |
|---|---|---|---|---|---|
| **F-1** | 🔴 | Funcional | Streaming DRM no reproduce (`--disable-component-update` mata el CDM Widevine) | MainWindow.xaml.cs:405 | Prime/Netflix/Disney/HBO |
| **F-2** | 🔴 | Funcional | Logins OAuth/SSO cuelgan (popup → tab nueva, rompe `window.opener`) | BrowserTab.Events.cs:618 | "Iniciar sesión con Google/MS/Apple" |
| **AS-1** | 🔴 | Seguridad | Bridge WebMessage confía en la página; `autofill-submit` usa `host` del mensaje | BrowserTab.Events.cs:370 | Envenenamiento del vault |
| **AS-2** | 🔴 | Seguridad | Errores de cert TLS → `AlwaysAllow` + solo WARN (sin bloqueo duro) | BrowserTab.Events.cs:296 | Garantía anti-MitM |
| **C-1** | 🔴 | Privacidad | `crt.sh` recibe CADA dominio visitado, sin opt-in | TLSGuard + Events.cs:258 | La tesis privacy-first |
| **C-2** | 🔴 | Calidad | LLamaSharp infiere sobre un `LLamaContext` único sin lock | LLamaSharpAdapter.cs:86 | Corrupción de inferencia |
| **F-3** | 🟠 | Funcional | Sin handler `PermissionRequested` (cámara/mic/geo/notif) | (ausente) | Meet/Zoom/WhatsApp/Maps |
| **AS-3** | 🟠 | Seguridad | `ShellExecute` de URI externa + grant de esquema por sesión | Helpers.cs:95 | Vector RCE-adyacente |
| **AS-4** | 🟠 | Seguridad | JS por interpolación desde salida del modelo (mitigado por aprobación) | AgentActionExecutor.cs:81 | Inyección si se aprueba |
| **R-1** | 🟠 | Resiliencia | Sin handler `ProcessFailed` → no hay recovery de crash de WebView2 | (ausente) | Sesión larga, OOM, GPU crash |
| **A-2** | 🟠 | Arquitectura | `MainWindow.xaml.cs` 3,471 loc (clase-dios) | MainWindow.xaml.cs | Mantenibilidad |
| **A-3/R-2** | 🟠 | Arq/Resil | 11 subs WebView2, 0 unsubs, sin `Dispose` (fuga) | BrowserTab.xaml.cs:363 | Memoria en sesión larga |
| **F-4** | 🟡 | Funcional | WebRTC modo `Disabled` corta todas las videollamadas | MainWindow.xaml.cs:415 | Calls si el user lo apaga |
| **F-5** | 🟡 | Funcional | Fingerprint `Aggressive` por defecto rompe reCAPTCHA/banca | MainWindow.xaml.cs:414 | Captchas, algunos bancos |
| **F-6** | 🟡 | Funcional | 3× `--disable-features` separados: Chromium solo respeta el último | MainWindow.xaml.cs:387 | Intención de privacidad a medias |
| **M-2** | 🟡 | Privacidad | NewTab carga el logo desde GitHub (asset ya bundleado) | Helpers.cs:243 | Phone-home por pestaña |
| **M-4** | 🟡 | Arquitectura | 3 abstracciones de adapter IA + Ollama implementado 2 veces | Agent/Security/Core | Acoplamiento/duplicación |
| **M-5/R-4** | 🟡 | Arq/Resil | `GpuLayers=35` hardcoded, sin detección de VRAM ni fallback CPU | LLamaSharpAdapter.cs:29 | Hardware modesto |
| **DEAD-1** | 🟡 | Limpieza | Menú "🔒 Analizar imagen (local)" invoca evento con 0 subscribers | ContextMenuBuilder.cs:95 | Feature muerta IA-local |
| **R-3** | 🟡 | Recursos | LLM residente + 4 WebViews (Council) sin techo de memoria | varios | Laptops modestas |
| **CUDA** | 🟡 | Tamaño | `LLamaSharp.Backend.Cuda12` bundleado (>1 GB nativo) | VELO.Agent.csproj:17 | Peso del instalador |
| **BF** | 🟡 | Funcional | Bug back/forward pendiente desde v2.4.5 | (sin diagnosticar) | Navegación básica |
| **B-1** | ⚪ | Calidad | `catch {}` de telemetría sin `LogDebug` | MainWindow:1115 | Diagnóstico |
| **B-4** | ⚪ | Limpieza | `LocalizationService` 958 loc inline, `MalwaredexEntry` 515 loc | varios | Mantenibilidad |
| **F-7** | ⚪ | Funcional | `async void` no-handler (`GlancePopup.ShowPreview` público) | GlancePopup.xaml.cs:27 | Crash de dispatcher si throw |

**6 hallazgos 🔴 — 2 de funcional (streaming, login), 2 de seguridad (vault, cert), 1 de privacidad (crt.sh), 1 de calidad (lock LLM).** Esos seis son el corte que decide si VELO "sirve para lo básico y es confiable".

---

<a name="lente-a"></a>
## 2. LENTE A — Arquitectura y limpieza

### Mapa del repo (9 proyectos · 186 .cs · 28,846 loc prod · 8,064 loc test)

| Proyecto | .cs | loc | Rol |
|---|---|---|---|
| VELO.Core | 37 | 4,535 | Dominio, navegación, AI router, Council, localización |
| VELO.Data | 20 | 1,485 | SQLite/SQLCipher + repos + SettingKeys (39) |
| VELO.Security | 39 | 4,319 | AISecurityEngine + guards + blocklists |
| VELO.Agent | 19 | 2,274 | VeloAgent (chat IA) + adapters (LLamaSharp/Ollama) |
| VELO.UI | 43 | 9,408 | Controles + diálogos + temas (capa más grande) |
| VELO.App | 11 | 5,194 | Host WPF + DI + bootstrap (MainWindow 3,471 loc) |
| VELO.Vault | 4 | 556 | Passwords + autofill + HIBP |
| VELO.Import | 8 | 905 | Import Chrome/Edge/Firefox |
| VELO.DNS | 5 | 170 | DNS-over-HTTPS |

### Dependencias NuGet (post pin v2.4.58)
WebView2 1.0.3967.48 · LLamaSharp 0.20.0 + **Backend.Cuda12** + Backend.Cpu · Anthropic.SDK 5.10.0 · ModernWpfUI 0.9.6 · Serilog 4.3.1 · sqlite-net-pcl 1.9.172 · SQLitePCLRaw sqlcipher 1.1.14 / e_sqlite3 3.0.3 · M.E.Logging.Abstractions 10.0.7 · System.Text.Json 10.0.7 · System.Security.Cryptography.ProtectedData 10.0.7.
**Driver de tamaño #1:** Backend.Cuda12 (nativos CUDA >1 GB).

### Flujo de arranque
`App.OnStartup → DependencyConfig.Build() (Serilog file-only + DI) → AppBootstrapper.InitializeAsync()`:
DB init → Malwaredex dedupe → blocklists (cache→bundled) → restore whitelist → idioma → ConfigureAIAdapter → **ConfigureAgentAdapters (resuelve LLamaSharpAdapter SIEMPRE, AppBootstrapper.cs:170)** → blocklist bg update → GoldenList staleness → MainWindow visible.
**Lazy candidates:** carga del GGUF (2-10 s, debería ser al primer uso del agente); Malwaredex dedupe (one-shot, gatear con flag).

### Hallazgos
- **A-2** MainWindow 3,471 loc — clase-dios; Council sumó ~700. Causa raíz del patrón "se enchufa a mano y nadie lo prueba".
- **A-3** `BrowserTab`: 11 `+=` a `CoreWebView2`, 0 `-=`, sin `Dispose` → fuga de handlers (también R-2).
- **M-4** Tres abstracciones de adapter IA (`IAgentAdapter`, `IAIAdapter`, `DirectChatAdapter`) + Ollama implementado dos veces (`OllamaAgentAdapter`, `OllamaAdapter`).
- **M-5** `GpuLayers=35` hardcoded sin detección de VRAM (también R-4).
- **DEAD-1** `ContextMenuBuilder.RequestImageAnalysis` (línea 193) se invoca desde el menú (línea 95) pero **tiene 0 subscribers** → el item "🔒 Analizar imagen (local)" no hace nada. Feature IA-local muerta. Patrón "shipped-but-never-wired" (lecciones #8/#11/#15/#21).
- **F-7** `async void` que no son event handlers (`GlancePopup.ShowPreview` es `public async void`, `ShowUpdateToast`, `TrySend`, `ShowStatus`) → si lanzan, tumban el dispatcher.
- **Privacidad C-1 / M-2** ver Lente C y B.
- **B-4** `LocalizationService` 958 loc + `MalwaredexEntry` 515 loc: candidatos a externalizar/adelgazar.

---

<a name="lente-b"></a>
## 3. LENTE B — Funcional / uso diario real

> "Lo que la gente realmente hace con un navegador no debería romperse en uno nuevo."

### F-1 🔴 · Streaming premium no reproduce
`MainWindow.xaml.cs:405` pasa `--disable-component-update` (+ `--disable-background-networking`, :394). El **CDM de Widevine** se entrega/actualiza por el component updater de Chromium; deshabilitarlo deja a WebView2 sin descifrador → `requestMediaKeySystemAccess('com.widevine.alpha')` falla → Prime/Netflix/Disney+/HBO no reproducen. **Fix:** quitar ambos flags (la telemetría sigue apagada por `--disable-breakpad/-crash-reporter/-domain-reliability/-sync/-metrics-recording-only`). Verificar entrando a Prime.

### F-2 🔴 · Logins OAuth/SSO cuelgan
`BrowserTab.Events.cs:618` — todo popup user-initiated se convierte en tab nueva (`__newtab:`). Los flujos "Iniciar sesión con Google/Microsoft/Apple/Facebook" abren `window.open()` y dependen de `window.opener.postMessage(token)` + `window.close()`. Una tab nueva no tiene relación `opener` → el login completa pero la página original nunca recibe el token → cuelga. **Fix:** en `NewWindowRequested`, asignar `e.NewWindow` a un `CoreWebView2` real (con deferral) para popups legítimos, preservando la cadena `opener`.

### F-3 🟠 · Sin gestión de permisos
No existe handler `PermissionRequested` (cámara/mic/geo/notificaciones/clipboard). VELO depende del default de WebView2, inconsistente para cámara/mic. **Probable:** Meet/Zoom/WhatsApp Web (cámara), Maps (ubicación) no piden permiso → no funcionan. **Fix:** handler `PermissionRequested` con prompt + persistencia per-site. *Requiere verificación runtime.*

### F-4 🟡 · WebRTC `Disabled` corta calls
Default `Relay` (MainWindow:415). Si el usuario elige `Disabled`, ninguna videollamada funciona. **Fix:** dejar claro en Settings el costo + default seguro.

### F-5 🟡 · Fingerprint `Aggressive` por defecto
MainWindow:414. El spoofing agresivo de canvas/WebGL rompe reCAPTCHA y algunas webs de banca. **Fix:** evaluar `Balanced` como default + allowlist (ya existe `ShieldsAllowlist`).

### F-6 🟡 · `--disable-features` triplicado
MainWindow:387-389 — Chromium solo respeta el ÚLTIMO `--disable-features`; `msEdgeSidebarV2` y `EdgeShoppingAssistant` NO se deshabilitan. **Fix:** un solo flag con comas.

### BF 🟡 · Bug back/forward pendiente (desde v2.4.5, sin diagnosticar)

---

<a name="lente-c"></a>
## 4. LENTE C — Seguridad / superficie de ataque

> Para un navegador que vende protección, esta es la sección de credibilidad.

### AS-1 🔴 · Bridge WebMessage sin validación de origen
`BrowserTab.Events.cs:308` — `OnWebMessageReceived` se dispara para cualquier página; `chrome.webview.postMessage` es invocable por el JS de la web (`IsWebMessageEnabled=true`, xaml.cs:202). El switch por `kind` rutea a features privilegiadas. **`autofill-submit` (línea 372) toma `host` del cuerpo del mensaje** (controlado por la página), no de `_currentPageUrl`. Una página en `evil.com` puede forjar `{"kind":"autofill-submit","host":"banco.com","password":"x"}` y envenenar el vault. (El caso `pasteguard` lo hace bien usando `GetHost(_currentPageUrl)` — la inconsistencia lo confirma.) **Fix:** derivar SIEMPRE host/origen host-side; tratar el bridge como input no confiable; nunca decidir seguridad con datos del mensaje.

### AS-2 🔴 · Errores de certificado siempre permitidos
`BrowserTab.Events.cs:296` — `e.Action = AlwaysAllow` para cualquier host no-local con error de cert (self-signed/vencido/mismatch/autoridad), mostrando solo un WARN dismissible. Chrome/Edge/Firefox hacen interstitial DURO. Para un navegador de seguridad esto invierte la garantía core: un MitM con cert inválido carga su página igual. **Fix:** bloqueo duro (no-`AlwaysAllow`) para errores serios; permitir solo override explícito per-site con interstitial.

### AS-3 🟠 · ShellExecute de URI externa + grant por sesión
`Helpers.cs:95` — `Process.Start(uri, UseShellExecute=true)` tras un prompt Yes/No para esquemas desconocidos; al primer "Sí", el esquema entra a `_allowedExternalSchemes` y todo URI futuro de ese esquema lanza sin prompt. La URI se muestra truncada a 200 chars. Vector RCE-adyacente (`ms-msdt:`, `search-ms:`, handlers con inyección de argumentos). **Fix:** allowlist estricta de esquemas conocidos-buenos; nunca grant ciego por-sesión; mostrar la URI completa.

### AS-4 🟠 · JS por interpolación desde salida del modelo
`AgentActionExecutor.cs:81` (`FillForm`/`ClickElement`/`ScrollTo`) — selector/valor vienen del modelo y se incrustan en JS con escape de comilla simple. **Mitigado** por aprobación humana obligatoria (`AgentActionSandbox`, sin auto-approve). Cadena tesis-relevante: contenido web → prompt-injection a la IA local (`ReadPage`/`Summarize` mete `innerText` al prompt) → el modelo emite `FillForm` malicioso → el usuario aprueba de reflejo. **Fix:** pasar selector/valor como args de `ExecuteScriptAsync`, no interpolar; endurecer el disclaimer cuando la acción nació de contenido de página.

### Positivos verificados
- `__veloCouncil` solo se inyecta si `IsCouncilPanel` → páginas normales no tienen el bridge Council.
- `AreDevToolsEnabled=false`, `IsPasswordAutosave=false`, `IsGeneralAutofill=false` por defecto.
- Aprobación humana en acciones del agente (sin auto-approve).
- Hosts locales en cert error → `AlwaysAllow` razonable para dev.

---

<a name="lente-d"></a>
## 5. LENTE D — Resiliencia y recursos

### R-1 🟠 · Sin recovery de crash de WebView2
No hay handler `CoreWebView2.ProcessFailed` en todo el repo. Si el proceso browser/render crashea (OOM, driver GPU, página hostil), la tab queda en blanco/muerta sin reload ni aviso. **Fix:** handler `ProcessFailed` → reload de la tab + toast; para crash del browser process, recrear el entorno.

### R-2 🟠 · Fuga de handlers en sesión larga (= A-3)
Sin `Dispose`/desuscripción, cada tab cerrada deja handlers vivos sobre su `CoreWebView2`. En sesiones largas con muchas tabs (+ 4 WebViews de Council) la memoria crece monótona.

### R-3 🟡 · Techo de memoria con IA local + Council
LLM residente (GGUF 2-4 GB en RAM/VRAM) + 4 WebViews simultáneos del 2×2 de Council. Sin gating de "¿hay RAM suficiente?". En laptop de 8 GB, Council + modelo = swap/OOM. **Fix:** documentar requisito (≥16 GB) + gate de preflight; lazy-load del modelo.

### R-4 🟡 · Carga del modelo sin fallback CPU (= M-5)
`GpuLayers=35` hardcoded → en hardware sin NVIDIA o <6 GB VRAM, falla la carga sin degradar a CPU ni avisar.

### Positivo
- Session restore / crash recovery de tabs existe (Sprint 3 / `SessionPersistenceController`) — los tabs se recuperan al reabrir. Cubre el caso "VELO se cerró", NO el caso "WebView2 crasheó en vivo" (R-1).
- Auto-update con verificación SHA256 (Sprint 2).

---

<a name="plan"></a>
## 6. PLAN DE ACCIÓN CONSOLIDADO

### 6.1 Bundle de hotfix funcional/seguridad (PRIORIDAD — lo que rompe el producto)
Orden seguro, cada paso compila:
1. **F-1** quitar `--disable-component-update` + `--disable-background-networking` → streaming. *(toca WebView2 env → lección #22 clean publish)*
2. **F-6** fusionar los `--disable-features` en uno. *(mismo archivo, gratis)*
3. **AS-2** cert errors: bloqueo duro para errores serios. *(seguridad core)*
4. **AS-1** `autofill-submit/detect`: derivar `host` de `_currentPageUrl`, ignorar el del mensaje. *(seguridad core)*
5. **F-2** popups OAuth: `e.NewWindow` real con deferral. *(más delicado, testear con login Google)*
6. **C-2** `SemaphoreSlim(1,1)` en `LLamaSharpAdapter`. *(barato)*

### 6.2 Refactors estructurales (máx 5, por retorno)
| # | Qué | Por qué | Riesgo | Esfuerzo |
|---|---|---|---|---|
| R1 | Extraer `CouncilModeController` de MainWindow | A-2 clase-dios | Bajo | M |
| R2 | `BrowserTab : IDisposable` + desuscripción + recovery | A-3/R-1/R-2 | Medio | M |
| R3 | Unificar adapters IA en `VELO.AI` (1 cliente OpenAI-compat) | M-4 | Medio | L |
| R4 | Handler `PermissionRequested` con persistencia per-site | F-3 | Bajo | M |
| R5 | Lazy-load del modelo LLamaSharp | R-3/R-4/arranque | Bajo | S |

### 6.3 Quick wins (<30 min c/u)
QW-1 NewTab logo → `data:` URI (M-2) · QW-2 SemaphoreSlim LLamaSharp (C-2) · QW-3 gatear `crt.sh` opt-in OFF (C-1) · QW-4 `GpuLayers` configurable + fallback CPU (M-5) · QW-5 `--disable-features` merge (F-6) · QW-6 borrar item de menú muerto o cablear `RequestImageAnalysis` (DEAD-1) · QW-7 `LogDebug` en catches de telemetría (B-1).

### 6.4 Arquitectura objetivo (≤8 módulos)
```
1 VELO.App      host WPF + DI + bootstrap (MainWindow adelgazado)
2 VELO.UI       controles + diálogos + temas
3 VELO.Core     dominio + navegación + Council + localización
4 VELO.AI       ★ fusión de los 3 sistemas de IA (Agent + Security adapters + DirectChat)
5 VELO.Security guards + blocklists + GoldenList
6 VELO.Data     SQLite/SQLCipher + repos
7 VELO.Vault    passwords + autofill + HIBP
8 VELO.Import   import (evaluar merge a Data)
  VELO.DNS → absorbido en Security/Core · VELO.Agent → desaparece en VELO.AI
```

### 6.5 Métricas antes/después (estimadas)
| Métrica | Antes | Después |
|---|---|---|
| LOC producción | 28,846 | ~26,000 |
| Proyectos | 9 | 8 |
| Abstracciones adapter IA | 3 | 1 |
| MainWindow.xaml.cs | 3,471 | <2,000 |
| Tamaño instalador | base + CUDA (>1 GB) | base (–>1 GB si Decisión #1=A) |
| Conexiones no consentidas | 2 (crt.sh, logo) | 0 |
| Streaming / OAuth / cert MitM | rotos/inseguros | funcionando/seguros |
| Recovery de crash WebView2 | no | sí |

---

<a name="decisiones"></a>
## 7. DECISIONES QUE NECESITO DE BADIZ

Binarias. Nada de código hasta tu OK, empezando por las 🔴.

1. **¿Apruebo el bundle de hotfix §6.1 (F-1 streaming + F-2 OAuth + AS-1 + AS-2 cert + F-6 + C-2)?** Es lo que hace que VELO sirva para lo básico y sea confiable. *(Sí / No / solo algunos)*
2. **AS-2 (cert errors):** ¿bloqueo duro estilo Chrome (recomendado) o mantenemos "cargar con WARN"? *(Bloqueo / WARN)*
3. **C-1 (crt.sh):** ¿opt-in default OFF (recomendado) o eliminar? *(Opt-in / Eliminar)*
4. **EL camino de IA local:** (A) LM Studio/Ollama-HTTP → eliminar LLamaSharp + CUDA (instalador –>1 GB) · (B) LLamaSharp GGUF, pero sacar CUDA del bundle · (C) ambos, sacar CUDA igual. *(A / B / C)*
5. **¿Mantenemos modo Claude-nube (Anthropic.SDK)?** Contradice "local" pero es buen fallback. *(Sí / No)*
6. **F-5 (fingerprint):** ¿default `Balanced` en vez de `Aggressive` para no romper captchas/banca? *(Sí / No)*
7. **Council Mode:** ¿sigue PAUSADO y la versión reducida (2-3 modelos lado a lado) es el alcance final de v0.1? *(Sí / No)*
8. **R3 (fusión de adapters en VELO.AI):** mayor retorno, mayor riesgo (L). *(Después de hotfixes / Congelar)*

---

*Fin. Esperando tu aprobación sobre §6.1 y §6.3 antes de modificar una línea.*
