"""VELO Sentinel S-B — release gates. Exits non-zero if the model may not ship.

Gates (PLAN_VELO_IA_SEGURIDAD.md §4):
  1. macro one-vs-rest AUC >= 0.98 on data/test.csv
  2. FPR < 1% on the benign test slice (anything-but-benign counts as FP)
  3. regression_never_block.txt: every URL must classify benign
  4. regression_must_catch.txt: every URL must classify phishing
"""
import json
import sys

import numpy as np
import onnxruntime as ort
import pandas as pd
from sklearn.metrics import roc_auc_score
from transformers import AutoTokenizer

MAX_LEN = 64
LABELS = ["benign", "phishing", "tracker", "ad"]

tok = AutoTokenizer.from_pretrained("out/model")
sess = ort.InferenceSession("out/velo-sentinel.onnx")


def predict(urls: list[str]) -> np.ndarray:
    probs = []
    for i in range(0, len(urls), 256):
        enc = tok(urls[i:i + 256], truncation=True, max_length=MAX_LEN,
                  padding="max_length", return_tensors="np")
        logits = sess.run(["logits"], {
            "input_ids": enc["input_ids"].astype(np.int64),
            "attention_mask": enc["attention_mask"].astype(np.int64),
        })[0]
        e = np.exp(logits - logits.max(axis=1, keepdims=True))
        probs.append(e / e.sum(axis=1, keepdims=True))
    return np.vstack(probs)


failures = []
test = pd.read_csv("data/test.csv")
probs = predict(test["url"].tolist())
preds = probs.argmax(axis=1)

auc = roc_auc_score(test["label"], probs, multi_class="ovr", average="macro")
if auc < 0.98:
    failures.append(f"AUC {auc:.4f} < 0.98")

benign = test["label"] == 0
fpr = float((preds[benign] != 0).mean())
if fpr >= 0.01:
    failures.append(f"benign FPR {fpr:.4%} >= 1%")

for path, want, name in (("regression_never_block.txt", 0, "never-block"),
                         ("regression_must_catch.txt", 1, "must-catch")):
    urls = [u.strip() for u in open(path, encoding="utf-8")
            if u.strip() and not u.startswith("#")]
    if not urls:
        continue
    bad = [u for u, p in zip(urls, predict(urls).argmax(axis=1)) if p != want]
    if bad:
        failures.append(f"{name}: {len(bad)} misclassified, e.g. {bad[:3]}")

metrics = {"auc_macro_ovr": round(float(auc), 4), "benign_fpr": round(fpr, 5),
           "test_size": len(test), "gates_passed": not failures}
json.dump(metrics, open("out/metrics.json", "w"), indent=2)
print(json.dumps(metrics, indent=2))

if failures:
    print("\nGATES FAILED:\n- " + "\n- ".join(failures))
    sys.exit(1)
print("\nall gates passed — ready to publish as model-vN")
