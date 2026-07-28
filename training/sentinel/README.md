# VELO Sentinel — training pipeline (S-B)

Fine-tunes a DistilBERT-class encoder to classify URLs into
`benign / phishing / tracker / ad`, exports int8 ONNX, and gates the
result before it can ship as a `model-vN` release.

**Status: scaffold written 2026-07-28 (S-B), not yet executed** — needs a
GPU (consumer card or Google Colab). S-A already validated the runtime
side on the target machine: 3.6–9.8 ms per inference, ~110 MB RAM.

## Steps

```bash
pip install -r requirements.txt

# 1. Download feeds + build the labeled dataset (CPU, ~10 min)
python prepare_data.py            # writes data/dataset.csv + splits

# 2. Fine-tune (GPU: ~1-2 h consumer card / Colab T4)
python train.py                   # writes out/model/

# 3. Export + int8 quantization (CPU, ~2 min)
python export_onnx.py             # writes out/velo-sentinel.onnx (int8)

# 4. Gates — MUST pass before publishing (CPU)
python evaluate.py                # exits non-zero on any gate failure
```

## Gates (from PLAN_VELO_IA_SEGURIDAD.md §4)

- AUC (one-vs-rest, macro) ≥ 0.98 on the held-out test split.
- FPR < 1% on the benign (Tranco) test slice at the shipped threshold.
- `regression_never_block.txt` — 0 of these may classify as anything but benign.
- `regression_must_catch.txt` — sampled held-out phishing that must stay caught.

## Publishing

Tag `model-v1` on GitHub, assets: `velo-sentinel.onnx` + `manifest.json`
(version, schema=1, sha256, size, trained date, gate metrics). The app
downloads it on user opt-in (S-D).

## Data sources (all public)

| Label | Source |
|---|---|
| phishing | OpenPhish feed + PhishTank online-valid |
| tracker | EasyPrivacy `||domain^` rules |
| ad | EasyList `||domain^` rules |
| benign | Tranco top-100k |
| (extra) phishing/malware | URLhaus online |

Colab note: upload this folder, `!pip install -r requirements.txt`, run the
four steps in order; download `out/velo-sentinel.onnx` + `out/metrics.json`.
