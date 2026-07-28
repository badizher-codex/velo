"""VELO Sentinel S-B — build the labeled URL dataset from public feeds.

Labels: 0=benign, 1=phishing, 2=tracker, 3=ad.
Output: data/dataset.csv (url,label) + stratified train/val/test splits.
"""
import io
import os
import random
import re
import zipfile

import pandas as pd
import requests

random.seed(42)
os.makedirs("data", exist_ok=True)

UA = {"User-Agent": "velo-sentinel-training/1.0"}
PER_CLASS_CAP = 120_000


def fetch(url: str) -> bytes:
    r = requests.get(url, headers=UA, timeout=120)
    r.raise_for_status()
    return r.content


def adblock_domains(text: str) -> list[str]:
    """Extract ||domain^ blocking rules from an EasyList-syntax file."""
    out = []
    for line in text.splitlines():
        m = re.match(r"^\|\|([a-z0-9.-]+\.[a-z]{2,})\^", line.strip(), re.I)
        if m and "*" not in m.group(1):
            out.append(m.group(1).lower())
    return list(dict.fromkeys(out))


def synth_url(domain: str) -> str:
    paths = ["/", "/index.html", "/assets/app.js", "/p/track?id=1234", "/img/1.gif"]
    return f"https://{domain}{random.choice(paths)}"


rows: list[tuple[str, int]] = []

# ── benign: Tranco top-100k ─────────────────────────────────────────────
print("tranco…")
z = zipfile.ZipFile(io.BytesIO(fetch("https://tranco-list.eu/top-1m.csv.zip")))
tranco = pd.read_csv(z.open(z.namelist()[0]), names=["rank", "domain"]).head(100_000)
rows += [(synth_url(d), 0) for d in tranco["domain"]]

# ── phishing: OpenPhish + PhishTank + URLhaus ───────────────────────────
print("openphish…")
rows += [(u.strip(), 1) for u in fetch("https://openphish.com/feed.txt").decode().splitlines() if u.startswith("http")]

print("phishtank…")
try:
    pt = pd.read_csv(io.BytesIO(fetch("http://data.phishtank.com/data/online-valid.csv.gz")), compression="gzip")
    rows += [(u, 1) for u in pt["url"].dropna()]
except Exception as e:  # feed sometimes requires registration — optional
    print(f"  phishtank skipped: {e}")

print("urlhaus…")
try:
    uh = pd.read_csv(io.BytesIO(fetch("https://urlhaus.abuse.ch/downloads/csv_online/")), skiprows=8)
    col = "url" if "url" in uh.columns else uh.columns[2]
    rows += [(u, 1) for u in uh[col].dropna()]
except Exception as e:
    print(f"  urlhaus skipped: {e}")

# ── trackers / ads: EasyPrivacy / EasyList ──────────────────────────────
print("easyprivacy…")
for d in adblock_domains(fetch("https://easylist.to/easylist/easyprivacy.txt").decode(errors="replace")):
    rows.append((synth_url(d), 2))

print("easylist…")
for d in adblock_domains(fetch("https://easylist.to/easylist/easylist.txt").decode(errors="replace")):
    rows.append((synth_url(d), 3))

# ── dedupe, cap per class, split ────────────────────────────────────────
df = pd.DataFrame(rows, columns=["url", "label"]).drop_duplicates("url")
df = (
    df.groupby("label", group_keys=False)
    .apply(lambda g: g.sample(min(len(g), PER_CLASS_CAP), random_state=42))
    .sample(frac=1, random_state=42)
    .reset_index(drop=True)
)
print(df["label"].value_counts().rename({0: "benign", 1: "phishing", 2: "tracker", 3: "ad"}))

n = len(df)
df.iloc[: int(n * 0.8)].to_csv("data/train.csv", index=False)
df.iloc[int(n * 0.8): int(n * 0.9)].to_csv("data/val.csv", index=False)
df.iloc[int(n * 0.9):].to_csv("data/test.csv", index=False)
df.to_csv("data/dataset.csv", index=False)
print(f"total {n} → data/train.csv / val.csv / test.csv")
