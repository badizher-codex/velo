# BACKLOG VELO — deuda accionable v2.4.61+

**Actualizado:** 2026-07-06 (HEAD `e14ff15` = v2.4.60). Ordenado por retorno. Cada item es autocontenido: una sesión futura puede ejecutarlo sin re-derivar contexto.

---

## P0 — F-1: Widevine DRM no reproduce (ABIERTO, diagnóstico avanzado)

**Síntoma:** Prime/Netflix cuelgan en play. `Profile\EBWebView\WidevineCdm\` se crea en cada arranque pero queda **vacío**.

**Descartado con evidencia (sesión 2026-07-06):**
- ✅ Flags: `--disable-component-update`, `--disable-background-networking` (quitados v2.4.59), `--disable-plugins`, `--disable-logging` (quitados v2.4.60) — no alcanzó.
- ✅ Carpeta vacía shadowing el CDM bundled — borrarla no arregló.
- ✅ Red: endpoints del updater responden; el updater FUNCIONA (8 componentes en `component_crx_cache`, ninguno es Widevine).
- ✅ Políticas de registro (`HKLM\...\Policies\Microsoft\Edge*`): limpias (solo `RendererCodeIntegrityEnabled=0`, irrelevante).
- ✅ El CDM existe bundled en el runtime: `C:\Program Files (x86)\Microsoft\EdgeWebView\Application\<ver>\WidevineCdm\...\widevinecdm.dll`.

**Protocolo de diagnóstico (ya shippeado en v2.4.60):**
```powershell
# El hook VELO_EXTRA_BROWSER_ARGS anexa flags al WebView2 (WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
# se IGNORA cuando la app pasa AdditionalBrowserArguments explícito — aprendido a los golpes)
Stop-Process -Name VELO -Force; Start-Sleep 3
$env:VELO_EXTRA_BROWSER_ARGS='--enable-logging --v=1 --log-file=C:\wv2.log'
& 'C:\Program Files\VELO\VELO.exe'
# reproducir Prime → grep del log: widevine|cdm|component|KeySystem|crx
```
⚠️ En la sesión 2026-07-06 ni `--log-file` ni `chrome_debug.log` ni stderr redirect produjeron log (motivo no cerrado — probar `--enable-logging=stderr` + `--log-file` juntos, o `edge://components` visual).

**Próximos pasos en orden:**
1. `edge://components` dentro de VELO → versión de "Widevine Content Decryption Module" + botón "Comprobar actualización" (quedó pendiente del maintainer — puede ser el fix directo).
2. Probar en OTRA máquina Windows limpia con v2.4.60 → ¿reproduce? Aísla máquina-vs-producto.
3. Revisar cuarentena/Web-Protection de Malwarebytes por bloqueos a `widevinecdm.dll` o dominios `*.microsoft.com` del updater.
4. Último recurso: issue en WebView2Feedback con el log (cuando se consiga).

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

| Bug | Evidencia | Fix sugerido |
|---|---|---|
| **RequestGuard FP en primevideo.com** | Bloquea rutas first-party (`/detail/...`, `/movie`, `/-/es/collection/...`) como "trackers" — visto en runtime 2026-06-29 | En `RequestGuard.Evaluate`: si el host del recurso == eTLD+1 de la página (first-party), no aplicar reglas de tracker. Ya existe `GetRegistrableRoot` (RequestGuard.cs:253) |
| **SmartBlock classifier spam** | 1 request HTTP por recurso, TODAS timeout (`TaskCanceledException`), fail-soft "allowing" — llena el log | Cap de concurrencia + circuit breaker (si N timeouts seguidos → apagar por sesión) + cache negativo |
| **AS-2 UX del bloqueo de cert** | Página en blanco + toast (v2.4.60 arregló verdict/botones, pero sigue sin interstitial propio) | Página de error propia (HTML local estilo NewTab) con "Volver" + "Continuar bajo riesgo" cableado al override |
| **Back/forward bug** | Pendiente desde v2.4.5, sin diagnosticar | Lección #7: instrumentar primero (~30 líneas de logging en NavigationStateChanged) |

## P3 — Deuda de producto (docs en memoria del proyecto)
- Tear-off drag-back con reload (~100 líneas) — `feature_tearoff_drag_back.md`
- Command palette discoverability (URL hint + Ctrl+/ cheat-sheet) — `feature_command_palette_discoverability.md`
- CHANGELOG catch-up v2.0.0→v2.4.30
- Council Mode chunk H + verificación synthesis (PAUSADO — no retomar sin decisión explícita del maintainer)

## Verificación runtime pendiente del maintainer
- **v2.4.60:** login con Google (F-2) — el fix estrella, sin confirmar
- **v2.4.59:** AS-2 confirmado parcial; F-5/QW-3 corregidos en v2.4.60
- v2.4.58: H1 (`/resumen` sin freeze) + M1 (drag-back scroll)
