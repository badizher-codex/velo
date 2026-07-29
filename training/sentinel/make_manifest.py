"""VELO Sentinel — build out/manifest.json from the exported artifacts.

The manifest is the contract between a published model and the runtime: the
app reads max_len, the label order and conf_threshold_block from it rather
than hard-coding them, so a retrained model brings its own operating point
(see SentinelManifest / SentinelClassifier in VELO.Security).

model-v1's manifest was written by hand. It is generated here instead
because S-D verifies the SHA256 before activating a downloaded model — a
hash typed by a human is a hash nobody can trust.

Refuses to run unless evaluate.py's gates passed, so a manifest can never
exist for a model that may not ship.
"""
import hashlib
import json
import os
import sys
from datetime import date

MODEL = "out/velo-sentinel.onnx"
TOKENIZER = "out/tokenizer.json"
METRICS = "out/metrics.json"
OUT = "out/manifest.json"

VERSION = int(sys.argv[1]) if len(sys.argv) > 1 else 1
SCHEMA = 1          # input contract: one lowercase host, WordPiece, max_len 32
MAX_LEN = 32
LABELS = ["benign", "phishing", "tracker", "ad"]
CONF_THRESHOLD_BLOCK = 0.85


def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


if not os.path.exists(METRICS):
    sys.exit("out/metrics.json missing — run evaluate.py first")

metrics = json.load(open(METRICS))
if not metrics.get("gates_passed"):
    sys.exit("evaluate.py gates did not pass — this model may not be published")

# The tokenizer travels with the model: the C# WordPieceTokenizer reads this
# exact file, and its parity tests pin the encoding it produces.
if not os.path.exists(TOKENIZER):
    import shutil
    shutil.copy("out/model/tokenizer.json", TOKENIZER)

manifest = {
    "model": "velo-sentinel",
    "version": VERSION,
    "schema": SCHEMA,
    "trained": date.today().isoformat(),
    "base": "distilbert-base-uncased int8",
    "input": "host (lowercase, no scheme/path/port)",
    "max_len": MAX_LEN,
    "labels": LABELS,
    "conf_threshold_block": CONF_THRESHOLD_BLOCK,
    "semantics": ("BLOCK requires p>=conf_threshold_block; argmax==phishing below "
                  "that is FLAG (signal to PhishingShield, never blocks alone)"),
    "metrics": metrics,
    "files": {
        os.path.basename(p): {"sha256": sha256(p), "bytes": os.path.getsize(p)}
        for p in (MODEL, TOKENIZER)
    },
}

json.dump(manifest, open(OUT, "w"), indent=2)
print(json.dumps(manifest, indent=2))
print(f"\n-> {OUT}")
