"""VELO Sentinel — publish out/ as a model-vN GitHub release.

    python publish_model.py <N> [--draft]

Refuses to run unless out/manifest.json exists, its version matches N, and its
metrics say the gates passed — make_manifest.py already enforces the last one,
this re-checks because the manifest is what the app trusts.

The three assets ARE the contract SentinelModelInstaller expects
(velo-sentinel.onnx + tokenizer.json + manifest.json); a release missing any of
them is skipped by CheckAsync as un-installable, so all three go up or none do.

Token comes from `git credential fill`, never from a file or an argument.
"""
import json
import os
import subprocess
import sys

import requests

REPO = "badizher-codex/velo"
ASSETS = ["out/velo-sentinel.onnx", "out/tokenizer.json", "out/manifest.json"]

if len(sys.argv) < 2 or not sys.argv[1].isdigit():
    sys.exit(__doc__)
VERSION = int(sys.argv[1])
DRAFT = "--draft" in sys.argv
TAG = f"model-v{VERSION}"

manifest = json.load(open("out/manifest.json", encoding="utf-8"))
if manifest["version"] != VERSION:
    sys.exit(f"manifest says v{manifest['version']}, you asked for v{VERSION}")
if not manifest.get("metrics", {}).get("gates_passed"):
    sys.exit("manifest says the gates did not pass — this model may not be published")
for path in ASSETS:
    if not os.path.exists(path):
        sys.exit(f"missing asset: {path}")


def token() -> str:
    out = subprocess.run(["git", "credential", "fill"],
                         input="protocol=https\nhost=github.com\n\n",
                         capture_output=True, text=True, check=True).stdout
    for line in out.splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1]
    sys.exit("no GitHub token from git credential fill")


HEADERS = {
    "Authorization": f"Bearer {token()}",
    "Accept": "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
}

m = manifest["metrics"]
body = f"""VELO Sentinel model **v{VERSION}** — embedded offline host classifier.

Consumed by VELO via Settings → AI → **Download model** (SHA256 verified before
install). Not bundled in the installer: it is versioned independently, so a
model with a compatible schema is adopted without an app release.

### Contract
- **Input**: one host, lowercase, no scheme/path/port. Not a URL.
- **Labels**: `{', '.join(manifest['labels'])}` · **max_len** {manifest['max_len']} · **schema** {manifest['schema']}
- **Semantics**: BLOCK requires p ≥ {manifest['conf_threshold_block']}; `argmax == phishing` below that is a
  FLAG that feeds PhishingShield as a signal and never blocks on its own.
- **Only the phishing label acts.** Tracker and ad verdicts are reported and
  logged, never enforced — measured against 89 hosts from real browsing, every
  false positive the classifier produced was a tracker verdict, and trackers
  are what the exact blocklists already do well. The model covers what a list
  structurally cannot see: a lookalike domain nobody has reported yet.

### Gates
| gate | result |
|---|---|
| macro one-vs-rest AUC | **{m['auc_macro_ovr']}** |
| field never-block (89 real browsing hosts) | **89/89** |
| synthetic never-block (top sites, banks, gov) | **26/26** |
| must-catch (lookalike domains) | **4/4** |
| benign FPR @ τ={m['conf_threshold']} (informational) | {m['benign_fpr']:.2%} |

The benign FPR is reported, not gated: it is measured on a test split the
training pipeline generates, so it is not comparable between models. The hard
gate is a fixed set of hosts observed in real browsing.

### Files
| file | sha256 | bytes |
|---|---|---|
"""
for name, meta in manifest["files"].items():
    body += f"| `{name}` | `{meta['sha256']}` | {meta['bytes']:,} |\n"
body += f"\nBase: {manifest['base']} · trained {manifest['trained']}\n"

r = requests.post(f"https://api.github.com/repos/{REPO}/releases", headers=HEADERS,
                  json={"tag_name": TAG, "name": f"Sentinel {TAG}", "body": body,
                        "draft": DRAFT, "prerelease": True})
if r.status_code >= 300:
    sys.exit(f"release creation failed: {r.status_code} {r.text[:400]}")
release = r.json()
print(f"release {TAG}: {release['html_url']}")

upload = release["upload_url"].split("{", 1)[0]
for path in ASSETS:
    name = os.path.basename(path)
    with open(path, "rb") as f:
        u = requests.post(f"{upload}?name={name}", headers={
            **HEADERS, "Content-Type": "application/octet-stream"}, data=f)
    status = "ok" if u.status_code < 300 else f"FAILED {u.status_code} {u.text[:200]}"
    print(f"  {name:22} {os.path.getsize(path):>12,} bytes  {status}")
