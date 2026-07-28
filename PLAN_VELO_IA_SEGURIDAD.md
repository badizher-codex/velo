# PLAN — VELO Sentinel: clasificador de seguridad incrustado (Piso 1)

**Creado:** 2026-07-28 · **Estado:** APROBADO por el maintainer (resuelve Decisión #4 como opción C) · **Prerrequisito de:** Fase 2 ambient.

> **La decisión (2026-07-28):** VELO incorpora un **clasificador tiny incrustado** (no generativo) como cerebro de seguridad always-on. Los conectores existentes (Claude/GPT/Grok/local HTTP) **se quedan como opción para power users** ("alguien quisquilloso") — dejan de ser el camino crítico. LLamaSharp+CUDA se elimina en Fase 3 como ya preveía el plan.

## 1. Por qué

- La tesis de VELO es *estar-ahí*. Hoy la IA de seguridad depende de un server HTTP local (LM Studio/Ollama) que los logs de campo muestran **caído la mayor parte del tiempo** — P2-B (v2.4.62) tuvo que agregar circuit breaker precisamente porque el camino crítico dependía de algo que casi nunca está.
- Un encoder clase **MobileBERT fine-tuneado** para clasificación de URLs/dominios logra ~0.96 accuracy en detección de phishing con **74 MB de RAM y <400 ms en CPU** (MDPI 2624-800X/6/2/48, 2026). Sin GPU, sin CUDA, sin servidor: in-process.
- "No sabe el origen del universo pero es una super verga en lo web" = **especialización por diseño**. Un clasificador no tiene conocimiento general que recortar: solo hace su tarea, rápido y siempre.

## 2. Arquitectura

```
RequestGuard / NavGuard / PhishingShield
        │ (host, URL, contexto first/third-party)
        ▼
SentinelClassifier (VELO.Security)          ← NUEVO, in-process
  - ONNX Runtime CPU (Microsoft.ML.OnnxRuntime)
  - modelo: encoder tiny (MobileBERT-class) int8, objetivo ≤100 MB, <50 ms/inferencia
  - salida: {label: phishing|tracker|ad|benign, confidence}
  - contrato fail-soft idéntico a SmartBlock: sin modelo cargado → Allow + log 1×
        │ (solo si el usuario lo activó — opt-in "quisquilloso")
        ▼
DirectChatAdapter HTTP (LM Studio/Ollama/Claude/GPT/Grok)   ← se queda, deja de ser crítico
```

- **Ubicación del modelo:** `%LOCALAPPDATA%\VELO\models\sentinel\<version>\model.onnx` + `manifest.json`.
- **El instalador NO lo incluye** (instalador liviano + menos superficie para heurísticas de AV). Se descarga post-install — mismo patrón que el CDM de Widevine.
- **Prioridad de verdicts:** blocklists exactas primero (rápidas y precisas), Sentinel para la cola desconocida (dominios nuevos, lookalikes, zero-day phishing). El modelo NO reemplaza las listas — cubre lo que las listas no vieron.

## 3. Distribución y actualización (versionado con releases, como pidió el maintainer)

1. Tags de modelo independientes del app: `model-v1`, `model-v2`… en GitHub Releases, assets: `velo-sentinel-vN.onnx` + `manifest.json` (versión, schema, SHA256, tamaño, fecha de entrenamiento, métricas).
2. La app declara `MinModelSchema`/`MaxModelSchema` — un modelo nuevo con schema compatible se puede adoptar **sin release del app**.
3. Descarga: botón en Settings → AI ("Download security model, ~XX MB") + ofrecimiento en first-run. **Nunca automática sin opt-in** (coherente con `updates.auto_check` privacy-first). Verificación SHA256 obligatoria antes de activar.
4. Chequeo de versión de modelo: junto al UpdateChecker existente (mismo gate de privacidad).

## 4. Pipeline de entrenamiento (carpeta `training/sentinel/` en el repo)

- **Datos (todos públicos, refrescables):**
  - Phishing: PhishTank + OpenPhish (feeds vivos).
  - Trackers/ads: EasyPrivacy + EasyList + Disconnect (dominios y patrones → labels).
  - Benignos: Tranco top-100k (+ rutas sintéticas).
  - Malware URLs: URLhaus.
  - Balance objetivo ~1M ejemplos; script de descarga+etiquetado reproducible.
- **Entrenamiento:** fine-tune de MobileBERT (o DistilBERT si MobileBERT complica el export) sobre el texto de la URL, 2-4 épocas. GPU de consumo o Colab: **horas, no semanas**.
- **Export:** ONNX + cuantización int8 (`onnxruntime.quantization`).
- **Gates de calidad (bloquean publicar el modelo):**
  - AUC ≥ 0.98 en held-out; FPR < 1% sobre el set benigno Tranco al threshold elegido.
  - Latencia < 50 ms CPU en la máquina del maintainer.
  - Suite de regresión: lista fija de URLs que NUNCA deben bloquearse (bancos, gobiernos, los streaming de F-1) y que SIEMPRE deben detectarse (phishing conocido del held-out).
- **Cadencia:** re-entrenar trimestral o cuando los feeds muestren drift; publicar como `model-vN+1`.
- **Distilación (mejora futura, no v1):** teacher grande etiqueta la cola ambigua (agentic KD, arXiv 2602.10869).

## 5. Chunks de ejecución

| Chunk | Contenido | Estimación | Gate |
|---|---|---|---|
| **S-A** | ✅ **EJECUTADO 2026-07-28 — GO.** ONNX Runtime **1.28.0** + DistilBERT SST-2 int8 (67.5 MB, proxy de la clase Sentinel) en la máquina del maintainer: **p50 3.6 ms** (seq 32, multi-thread) · **9.8 ms single-thread** · seq 128 single-thread 34.9 ms p95 39 — todo bajo el gate de 50 ms · carga 90-113 ms · working set ~110 MB sobre baseline. Conclusión: MobileBERT/DistilBERT-class viable con margen; single-thread (browser-friendly) alcanza y sobra para URLs (seq ≤64). Benchmark en scratchpad `sentinel-spike/` (no commiteado) | 1 día | ✅ medido |
| **S-B** | `training/sentinel/`: scripts de datos + fine-tune + export + eval. Primer `model-v1` publicado. **✅ EJECUTADO 2026-07-28 — model-v1 PUBLICADO** (tag `model-v1`, prerelease: velo-sentinel.onnx 67.4 MB int8 + tokenizer.json + manifest.json con SHA256). 7 rondas de iteración en la 4090 (~5-7 min c/u). **Decisión de arquitectura clave: el modelo clasifica HOSTS, no URLs** (con ~100k dominios el modelo generaliza por forma del path y los paths sintéticos siempre son distinguibles; el pipeline de VELO es host-keyed de todos modos). **Semántica de dos niveles: BLOCK con p≥0.85 / FLAG=argmax phishing como señal a PhishingShield (nunca bloquea sola)**. Gates finales: AUC 0.9907 · FPR benigno 0.74% @ τ=0.85 · never-block 0 fallos · must-catch 4/4 flaggeados. Lecciones por ronda documentadas en los docstrings de prepare_data.py. Scaffold original: (`prepare_data.py` feeds públicos → dataset 4 clases · `train.py` DistilBERT MAX_LEN=64 · `export_onnx.py` int8 · `evaluate.py` gates ejecutables + listas de regresión never-block/must-catch). **Pendiente: ejecutarlo en GPU/Colab** (README paso a paso; los scripts NO se corrieron aún — verificar de punta a punta en la primera corrida) | 1-2 semanas | gates de calidad §4 |
| **S-C** | `SentinelClassifier` + wiring en el pipeline de verdicts (detrás de blocklists, delante del HTTP opcional) + Settings → AI (estado del modelo, botón download) + 8 idiomas | 1 semana | 6 proyectos verdes + smoke de wiring (lección #21: producer Y consumer) |
| **S-D** | Canal de descarga/actualización del modelo (manifest + SHA256 + schema check) | 3-4 días | descarga real end-to-end |
| **S-E** | **Shadow mode primero**: 1 release donde Sentinel solo loguea sus verdicts sin aplicarlos → comparar contra bloqueos reales en los logs de campo del maintainer (lección #30) → recién después release que aplica | 2 releases | cero FP en shadow logs del maintainer |

**Piso 2 (generativo 0.5–1.7B destilado para explicaciones) — DEFERIDO explícitamente.** No arrancar hasta que S-E esté verificado en campo. Las explicaciones estáticas de v2.4.63 (`WhyBlocked`) cubren la necesidad mientras tanto.

## 6. Riesgos conocidos

- **AV/heurísticas:** descargar un binario post-install es patrón que los AV miran. Mitigación: el .onnx no es ejecutable, SHA256 publicado, y el fix real sigue siendo el code signing (BACKLOG P1).
- **Drift de YouTube-clase:** un modelo de URLs no se rompe con cambios de DOM (a diferencia del adblock cosmético) — los feeds de phishing sí envejecen; por eso la cadencia trimestral.
- **Tamaño:** si int8 no alcanza el objetivo ≤100 MB, bajar a BERT-tiny/DistilBERT-small antes que subir el tamaño.
