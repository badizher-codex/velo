# BACKLOG VELO — deuda accionable v2.4.67+

**Actualizado:** 2026-07-28 (HEAD = v2.4.67, **publicada con assets**; v2.4.66 también). Ordenado por retorno. Cada item es autocontenido: una sesión futura puede ejecutarlo sin re-derivar contexto.

> **P2 cerrado en v2.4.62.** **P0 reescrito**: el CDM de Widevine YA FUNCIONA, la causa de F-1 es otra (abajo). Siguen abiertos P1 (code signing — el maintainer confirmó 2026-07-28 que hace el gasto cuando VELO esté "listo full", ver PLAN_VELO §6 R-6), el back/forward bug y P3.
>
> 🗺️ **La ruta de features nueva (aprobada 2026-07-28) vive en `PLAN_VELO.md` §6**: R-1 spike extensiones/uBO Lite · R-2 DoH · R-3 HTTPS-only · R-4 VELO Sentinel (`PLAN_VELO_IA_SEGURIDAD.md`, resuelve Decisión #4) · R-5 polish (favicons + palette) · R-6 code signing al final. Este BACKLOG mantiene bugs y deuda; la ruta manda en prioridad de features.

---

## ✅ P0 — F-1: **RESUELTO en v2.4.68** (2026-07-28, verificado en campo)

**Causa raíz final: Prime Video detecta la mera existencia de `window.chrome.webview` y confunde a VELO con la app de escritorio de Prime para Windows (que es WebView2).** Toma el code path de app, llama al bridge nativo de esa app (que VELO no tiene) y muere con `0x80070490` en su `RemoteMessenger` antes de arrancar el player — botón de play con spinner infinito.

**Fix:** `resources/scripts/webview-cloak.js` (inyectado primero) — guarda el bridge real como `window.__veloBridge` (non-enumerable) y borra `chrome.webview` de toda página. Los 6 callsites page→host de VELO resuelven `__veloBridge || chrome.webview`. Verificado: Prime reproduce con DRM activo (los frames protegidos salen negros en screenshots — prueba de Widevine funcionando).

**Descartes del camino (por si un sitio repite el patrón):** CDM ✓ instalado y licenciando · fingerprint-noise ✗ no era · RequestGuard ✗ solo telemetría · `Sec-CH-UA` limpio solo ✗ no cura · esconder solo `hostObjects` ✗ no cura → **el probe era la existencia del objeto**. Método que funcionó: cambiar UNA variable por test.

El historial del diagnóstico de junio-julio queda abajo como referencia.

---

### (histórico) P0 — F-1: Prime no reproduce — el CDM ya NO es la causa (2026-07-27)

**Todo el diagnóstico anterior quedó obsoleto.** Se creía que el CDM de Widevine nunca se descargaba (`Profile\EBWebView\WidevineCdm\` vacío). Eso **se resolvió**: quitar `--disable-component-update` (v2.4.59) sí funcionó, solo tardó semanas en bajar.

**Evidencia dura, 2026-07-27:**
- `%LOCALAPPDATA%\VELO\Profile\EBWebView\WidevineCdm\4.10.3050.1\_platform_specific\win_x64\widevinecdm.dll` = **22.695.928 bytes**, con `widevinecdm.dll.sig` (1389 B) y manifest con `cenc`+`cbcs` y códecs `vp8,vp09,avc1,av01`.
- Runtime WebView2 actual: **150.0.4078.99**.
- Command line viva del proceso: limpia, sin `--disable-component-update`, `--disable-plugins` ni `--disable-logging`.
- **Test EME dentro de VELO** (`C:\Users\badiz\Downloads\velo-drm-check.html`, servible con `python -m http.server 8765` desde Downloads):

  | Key system | Resultado |
  |---|---|
  | Widevine sin robustness | acceso + `createMediaKeys` **OK** |
  | Widevine `SW_SECURE_CRYPTO` | **OK** |
  | Widevine `SW_SECURE_DECODE` | **OK** |
  | Widevine `HW_SECURE_ALL` | `NotSupportedError` — **normal**, es DRM por hardware (L1); Prime no lo requiere |
  | PlayReady (recommendation) | **OK** |
  | ClearKey | **OK** |
  | MSE `avc1.640028` | **OK** |

- UA que ve la página: `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36 Edg/150.0.0.0` — UA de Edge legítima, no debería molestar a Prime.
- Log de VELO durante la navegación en Prime (con el logging de reglas de v2.4.62): **lo único bloqueado es telemetría** `fls-na.amazon.com` (uedata/CSM). Ni licencias, ni manifiestos, ni segmentos.

**→ Widevine funciona. El problema está en el reproductor de Prime, no en DRM.**

**Sospechoso #1: el script anti-fingerprinting de VELO.** El mismo test reporta `Canvas toDataURL parcheado: SÍ`. `fingerprint-noise.js` parchea `HTMLCanvasElement.toDataURL/toBlob`, `WebGLRenderingContext.getParameter` (devuelve un renderer ANGLE falso) y `AudioContext.createAnalyser`. El perfil del maintainer tiene guardado **Aggressive**. El script ya tiene una excepción para PerimeterX/Imperva (ver comentario en su línea 9) porque esos anti-bot rechazan el canvas falseado — Prime hace verificación de dispositivo del mismo tipo antes de pedir la licencia.

**❌ TEST EJECUTADO 2026-07-28 — fingerprint DESCARTADO.** El maintainer puso Fingerprint protection = Off (verificado en DB: `privacy.fingerprint_level='Off'`), reinició VELO (arranques 13:54:44 y 13:54:58 en el log) y Prime siguió igual: botón "Reanudar" con spinner infinito, sin llegar siquiera al player. El log de esa sesión (v2.4.67, con logging de reglas) confirma que RequestGuard solo bloqueó telemetría `fls-na.amazon.com` [BLOCKLIST] — cero bloqueos de licencias/manifiestos/API del player.

**Rama B (activa) — candidatos en orden:**
- (a) **Consola del renderer durante el play** (el de mayor señal): Ctrl+Shift+I → Security Inspector → "🔧 Open native DevTools" → pestaña Console → click en Reanudar → copiar errores. El player de Prime loguea su código de error; el spinner en el botón sugiere que una llamada pre-playback (GetPlaybackResources/entitlement) se queda colgada — la consola y la pestaña Network dicen cuál.
- (b) **Aislar si es Prime-específico**: demo de Shaka Player (`https://shaka-player-demo.appspot.com`, asset "Angel One (Widevine)") — si reproduce, el pipeline DRM completo funciona y el problema es del lado Prime (device check / cookies / algo del app JS).
- (c) `edge://components` dentro de VELO (abrible desde v2.4.64) para la versión del CDM que el browser reporta.
- (d) Revisar si otro script inyectado de VELO (cookie-bypass, paste-guard, webrtc-spoof con `Relay only`) interfiere con el flujo del player — probar con un perfil/container limpio.

---

## P1 — Code signing / falsos positivos de AV (blocker de distribución)

**Problema:** Malwarebytes cuarentena `VELO.Core.dll` como `Trojan.Injector.MSIL` (binarios .NET sin firmar + patrón de inyección JS = heurística). Le pasó al maintainer con la app INSTALADA y con cada build local. Le va a pasar a cada usuario.

**Plan (en orden):**
1. **Certificado:** para un proyecto indie, la opción 2026 con mejor relación costo/beneficio es **Azure Trusted Signing** (~USD 10/mes, valida identidad individual, firma en la nube integrable a GitHub Actions). Alternativas: Certum Open Source (~€70/año, requiere perfil open-source), SSL.com eSigner. EV clásico (~USD 300+/año) solo si SmartScreen-reputación-inmediata importa.
2. **Integración workflow** (259455799): paso de firma post-build para `VELO.exe` + DLLs propias + `Setup.exe`. Con Azure Trusted Signing es la action `azure/trusted-signing-action`; con cert clásico:
   ```yaml
   - run: signtool sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /f cert.pfx /p ${{ secrets.CERT_PWD }} <archivos>
   ```
3. **Reportar FP** (con binario ya firmado): Malwarebytes (form "False Positive"), Microsoft Defender (portal Security Intelligence), y subir el release a VirusTotal para medir cuántos motores flaggean antes/después.
4. **NO hacer:** ofuscar el patrón de inyección para esquivar heurística — es lo que hace el malware real, empeora la reputación.

**Workaround local del maintainer (ya aplicado):** exclusiones MBAM "todas las detecciones" sobre `D:\VELO\velo` + `C:\Program Files\VELO`.

---

## P2 — Bugs conocidos con diagnóstico hecho

| Bug | Evidencia | Fix |
|---|---|---|
| ~~**RequestGuard FP en primevideo.com**~~ | Bloqueaba rutas first-party (`/detail/`, `/movie`, `/collection`) como "trackers" | ✅ **v2.4.62 (P2-A)** — first-party salta las reglas heurísticas; verdict de SmartBlock acotado a sub-recursos third-party; `GetRootDomain` respeta sufijos de 2º nivel; **toda regla que no sea Allow ahora loguea cuál fue** (7 de 9 eran mudas) |
| ~~**SmartBlock classifier spam**~~ | Log 2026-07-27: ~30 WRN con stack trace para **un solo host** en 200 ms (LM Studio caído) | ✅ **v2.4.62 (P2-B)** — dedup in-flight + cap de concurrencia + cache negativo 5 min + circuit breaker (3 fallos) + niveles de log. `DirectChatAdapter` loguea endpoint caído 1×/5 min |
| ~~**AS-2 UX del bloqueo de cert**~~ | Página en blanco + toast de 5 s. Causa: `IsBuiltInErrorPageEnabled=false` (BrowserTab.xaml.cs:223) | ✅ **v2.4.62 (P2-C)** — interstitial local con "Volver" + "Continuar de todos modos" cableado al allow-once, autenticado por nonce de un solo uso, 8 idiomas |
| **Back/forward bug** | Pendiente desde v2.4.5, sin diagnosticar | Lección #7: instrumentar primero (~30 líneas de logging en NavigationStateChanged) |
| ~~**El veredicto de DRM se queda pegado en navegación SPA**~~ ✅ **ARREGLADO 2026-08-14** (sin release aún) — se dejó de contar y se pasó a observar: `mediaKeysAttached` se lee de `el.mediaKeys` (estado del navegador, no contabilidad nuestra) y los eventos `encrypted` se anotan en el elemento; ambos se leen sólo del elemento que respalda el `MediaSource` vivo, igual que `liveBuffers()` elige las pistas. `SetMediaKeysCalls` → `MediaKeysAttached`. 2 tests nuevos (873 total) + guards de string en el smoke, verificados contra el fichero anterior. **Falta verificación runtime:** reproducir algo protegido y navegar el catálogo sin recargar — el chip debe pasar de ámbar a verde. Detalle en `docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md` §23. | (histórico) | Medido 2026-08-14 en Prime Video: `10:24:32 /detail/… protected=true` → `10:24:36 /storefront protected=true` → `10:24:38 /movie protected=true`, siendo las dos últimas páginas de catálogo sin reproducir nada. Causa: `Media.Reset()` sólo corre en `OnNavigationStarting` (`BrowserTab.Events.cs:301`) y una ruta SPA no levanta navegación; el objeto `eme` de `media-detect.js` es acumulativo por **documento** y `setMediaKeys` nunca decrementa. Efecto: tras ver un título protegido el chip ámbar se queda el resto de la sesión en ese sitio y **suprime todas las ofertas**, porque el contenido protegido cortocircuita `BuildOffers` entero (`MediaInventory.cs:177`). Afecta a Netflix, Prime, YouTube y cualquier player SPA. Detalle en `docs/Phase6/MEDIA_DOWNLOAD_ANALYSIS.md` §21. | El rechazo es correcto; lo que está mal es su **duración**. Distinguir "hay cifrado en uso **ahora**" de "hubo cifrado alguna vez en este documento" — lo más barato es atar los contadores EME al `MediaSource` vivo, igual que `liveBuffers()` ya hace con las pistas (`media-detect.js:426`). Verificar en las dos direcciones (#44): lo protegido debe seguir rechazándose, y salir de ahí debe limpiar. |
| **Cada descarga abre la ventana entera de Downloads** | Reportado en campo 2026-08-13 sobre grok.com/imagine, corriendo el **release v2.5.0 instalado** (`C:\Program Files\VELO`, commit `3e1a379`), no el build de desarrollo. Causa en fuente: `MainWindow.xaml.cs:151` — `DownloadStarted += (_, _) => Dispatcher.Invoke(OpenDownloads)`, y `OpenDownloads()` (`:2461`) construye un `DownloadsWindow` con `Owner = this` y hace `Show()`. **La línea es idéntica en `3e1a379` y en HEAD**, así que el binario equivocado no explica nada aquí. Efecto secundario: se percibe como "tarda en salir", porque el evento no es el clic sino `DownloadStarting` — en Grok media una ida y vuelta al servidor antes del blob, y la primera vez además se construye la `Window` desde cero. | Ningún navegador abre una ventana por descarga. Sustituir por un indicador/badge en la barra (hoy no existe: el único acceso es el menú, `:1480`) y dejar la ventana para cuando se pida. Es trabajo de UI, no de ciclo de vida. |
| **(sin confirmar) Pestaña en blanco al descargar vía popup** | **Observación NO verificada** — no se llegó a ver el rail de pestañas en la captura. Hipótesis a partir de código: un clic con `target=_blank` que acaba en descarga entra por `NewWindowRequested` (`BrowserTab.Events.cs:730`) → Regla 4 (`:836`) → `MainWindow.xaml.cs:706` materializa una **pestaña real** y `AttachAsPopupAsync` (`BrowserTab.PublicApi.cs:29`) llama a `ShowWebView()`; luego `DownloadStarting` (`:951`) registra la descarga y **nadie cierra la pestaña**. `WindowCloseRequested` está cableado (`:853`) pero Chromium no lo dispara en este caso. `PublicApi.cs` no tiene ni una línea de diferencia entre `3e1a379` y HEAD. | Chrome cierra la pestaña cuando una navegación **sin commit** se convierte en descarga; en WebView2 lo tiene que hacer el host. Condición correcta: "popup que nunca confirmó navegación", **no** "es un popup" — si no, se rompen los popups de OAuth y los popups que cargan y descargan después (#44, las dos direcciones). **Primero reproducir y confirmar que la pestaña existe.** |

## P3 — Deuda de producto (docs en memoria del proyecto)
- Tear-off drag-back con reload (~100 líneas) — `feature_tearoff_drag_back.md`
- Command palette discoverability (URL hint + Ctrl+/ cheat-sheet) — `feature_command_palette_discoverability.md`
- CHANGELOG catch-up v2.0.0→v2.4.30
- Council Mode chunk H + verificación synthesis (PAUSADO — no retomar sin decisión explícita del maintainer)

### La landing repite la versión 20 veces en cada release (anotado 2026-08-17)

**Problema:** publicar una versión obliga a tocar **20 referencias** a mano en `docs/index.html`. Hoy se cubre con un `Edit replace_all`, así que no se rompe — pero es un paso manual del checklist que solo existe porque la versión está incrustada por todas partes.

**Desglose medido (2026-08-17, sobre v2.7.0):**

| Dónde | Refs | Qué es |
|---|---|---|
| URLs de descarga | **6** | 3 enlaces × 2 (el `/v2.7.0/` de la ruta + el `VELO-v2.7.0-…` del nombre de fichero) |
| Texto visible en HTML | **4** | badge, etiqueta del botón, y los 2 `<span>` con el nombre del asset |
| Tabla i18n | **10** | 5 idiomas × 2 claves (`hero_badge`, `hero_cta_installer`) |

**El fix son dos mitades, y hace falta la segunda.** Lo obvio es apuntar a `releases/latest/download/<asset>`, que GitHub resuelve solo — pero **eso solo mata 6 de 20**, y además hoy ni siquiera funciona: los assets llevan la versión en el nombre, así que `latest/download/VELO-v2.7.0-Setup.exe` deja de existir en cuanto salga la 2.8.0.

1. **Assets con nombre estable** (mata las 6 de URL). Que el workflow de release (id `259455799`) suba *además* una copia con nombre fijo — `VELO-Setup.exe` y `VELO-win-x64-portable.zip` — y cambiar los enlaces de la landing a `releases/latest/download/VELO-Setup.exe`. Los nombres versionados se quedan para quien quiera una versión concreta.
2. **Una sola fuente para la versión mostrada** (mata las 14 restantes). Un `const VELO_VERSION` en la landing y un marcador `{v}` en las cadenas i18n, interpolado al aplicar el idioma. El release pasaría a tocar **1 string** en `docs/index.html`.

**No** resolverlo pidiéndoselo a `api.github.com` desde la landing: añade una llamada de red en runtime a una página cuyo argumento entero es "cero llamadas ocultas", y encima falla si GitHub tiene un outage.

**Al cerrarlo hay que actualizar el checklist de release** (`feedback_release_checklist.md` en memoria dice hoy "20 refs").

## Bugs encontrados y arreglados el 2026-07-27 (en main, SIN release)

| Bug | Release | Detalle |
|---|---|---|
| Omnibox destrozaba todo esquema desconocido | v2.4.64 | `file:///C:/x.html` → `https://file:///C:/x.html`. VELO no podía abrir archivos locales. Ahora pasa `file/ftp/about/edge/chrome/view-source`; `data:` y `javascript:` siguen tratándose como búsqueda a propósito. **Efecto colateral útil: `edge://components` ya es abrible.** |
| `localhost` bloqueado como "DNS rebinding" | v2.4.64 | RequestGuard bloqueaba `localhost`, `0.0.0.0` y `*.local` sin importar quién pidiera → no se podía abrir un dev server ni un NAS. Fusionado con la regla SSRF: ahora se mira **quién pide** (referrer público = bloqueo; navegación tipeada = pasa). |
| Panel de amenazas tiraba 29 de cada 30 bloqueos | v2.4.65 | `BlockedRequestEvent` se publicaba **debajo** del gate de severidad ("un popup por navegación"), regla correcta para un toast y fatal para una lista acumulativa. En Prime (~30 balizas por carga) se registraba una sola. |
| Los bloqueos nombraban la página, no el dominio bloqueado | v2.4.66 | `AIVerdict.Host` sin setear en 5 emisores (RequestGuard sub-recursos, NavGuard, 3× PopupGuard). El panel decía "THREAT BLOCKED — www.youtube.com"; **"Allow once"/"Whitelist always" no hacían nada** (apuntaban al host de la página, y el bloqueo se evalúa contra el host del request); y el panel de amenazas no podía agrupar. |

## Verificación runtime pendiente del maintainer
- **v2.4.62 ✅ parcialmente confirmada (2026-07-27):** Prime navega sin prompts de tracker (P2-A) y el spam de SmartBlock desapareció (P2-B). **Falta P2-C**: `https://self-signed.badssl.com` debe mostrar el interstitial y "Continuar de todos modos" debe cargar el sitio.
- **v2.4.66:** reinstalar y confirmar que el panel nombra el tracker real (no la página) y que "Allow once" ahora surte efecto.
- **v2.4.60:** login con Google (F-2) — el fix estrella, sin confirmar
- **v2.4.59:** AS-2 confirmado parcial; F-5/QW-3 corregidos en v2.4.60
- v2.4.58: H1 (`/resumen` sin freeze) + M1 (drag-back scroll)
