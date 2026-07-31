"""VELO Sentinel model-v3 — harvest REAL benign hostnames from Certificate
Transparency (crt.sh).

Why this exists: through model-v2 the benign side of the dataset was ~4
SYNTHETIC hosts per Tranco root, generated from a small vocabulary of labels
(www/app/api/cdn/static/...), while the tracker+ad side was real EasyList
domains. So the shape of the subdomain separated the classes, and the model
learned "a label I recognise means benign, anything else means tracker".

The first real shadow session proved it: model-v2 blocked cart-mf.cinepolis.com,
myaccount.ea.com, pin-river.data.ea.com, merchantpool1.linkedin.com,
duolingo-achievements-prod.duolingo.com — 53 of 75 hosts from ordinary
browsing. No vocabulary I invent will contain "pin-river.data" or
"merchantpool1". The fix is to stop inventing.

Every host in a public CT log is a hostname someone actually provisioned a
certificate for, which is as close to "real benign hostname" as a public
dataset gets for the head of the web.

Usage:
    python harvest_ct.py [limit] [--workers N]

Politeness: crt.sh is a free public service run by Sectigo. Modest concurrency,
a delay between requests, retries with backoff, and an on-disk cache so a
re-run never re-queries a domain that already answered. Interrupting is safe —
progress is in the cache.

Output: data/ct_hosts.txt (one hostname per line).
"""
import json
import os
import random
import sys
import time
import zipfile
from concurrent.futures import ThreadPoolExecutor

import requests

CACHE_DIR = "data/ct_cache"
OUT = "data/ct_hosts.txt"
UA = {"User-Agent": "velo-sentinel-training/1.0 (+https://github.com/badizher-codex/velo)"}

LIMIT = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 5000
WORKERS = 4
if "--workers" in sys.argv:
    WORKERS = int(sys.argv[sys.argv.index("--workers") + 1])

os.makedirs(CACHE_DIR, exist_ok=True)
os.makedirs("data", exist_ok=True)


def cache_path(domain: str) -> str:
    return os.path.join(CACHE_DIR, domain.replace("/", "_") + ".json")


def harvest(domain: str) -> list[str]:
    """Hostnames under `domain` from crt.sh. Cached; [] on any failure —
    a domain we cannot reach is one we simply do not learn from."""
    path = cache_path(domain)
    if os.path.exists(path):
        try:
            with open(path, encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass  # corrupt cache entry — re-fetch

    url = f"https://crt.sh/?q=%25.{domain}&output=json&exclude=expired"
    hosts: list[str] = []
    for attempt in range(3):
        try:
            r = requests.get(url, headers=UA, timeout=90)
            if r.status_code == 429 or r.status_code >= 500:
                time.sleep(5 * (attempt + 1))
                continue
            r.raise_for_status()
            entries = r.json()
            seen = set()
            for entry in entries:
                # name_value can hold several SANs separated by newlines.
                for raw in (entry.get("name_value") or "").split("\n"):
                    h = raw.strip().lower().lstrip("*.").rstrip(".")
                    if not h or h in seen:
                        continue
                    # Keep only real hostnames under this domain. Wildcards and
                    # e-mail SANs are not hosts a browser ever requests.
                    if "@" in h or " " in h or not h.endswith("." + domain):
                        continue
                    seen.add(h)
                    hosts.append(h)
            break
        except Exception:
            time.sleep(3 * (attempt + 1))

    with open(path, "w", encoding="utf-8") as f:
        json.dump(hosts, f)
    # Be a good citizen: crt.sh is free and shared.
    time.sleep(random.uniform(0.4, 0.9))
    return hosts


print("tranco…")
z = zipfile.ZipFile(__import__("io").BytesIO(
    requests.get("https://tranco-list.eu/top-1m.csv.zip", headers=UA, timeout=180).content))
import pandas as pd  # noqa: E402  (after the download so the error surfaces first)

tranco = pd.read_csv(z.open(z.namelist()[0]), names=["rank", "domain"])

# Sample across the whole rank range, not the head.
#
# The first probe took the top 20 and got apple/google/linkedin — three
# big-tech naming conventions, and the largest domains are also the ones whose
# crt.sh query times out. What this harvest is actually for is the LABEL
# vocabulary (see prepare_data.py), so breadth of naming convention beats depth
# on any single domain: a mid-sized company names hosts differently than Apple.
SAMPLE_CEILING = 50_000
pool = tranco.head(SAMPLE_CEILING)
domains = pool.sample(min(LIMIT, len(pool)), random_state=42)["domain"].tolist()

cached = sum(1 for d in domains if os.path.exists(cache_path(d)))
print(f"{len(domains)} dominios (top-{LIMIT}), {cached} ya en cache, {WORKERS} workers")

start = time.time()
done = 0
all_hosts: list[str] = []

with ThreadPoolExecutor(max_workers=WORKERS) as pool:
    for hosts in pool.map(harvest, domains):
        all_hosts.extend(hosts)
        done += 1
        if done % 25 == 0 or done == len(domains):
            elapsed = time.time() - start
            rate = done / elapsed if elapsed else 0
            eta = (len(domains) - done) / rate / 60 if rate else 0
            print(f"  {done}/{len(domains)}  {len(all_hosts)} hosts  "
                  f"{rate:.1f} dom/s  ETA {eta:.0f} min", flush=True)

unique = sorted(set(all_hosts))
with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(unique))

with_hosts = sum(1 for d in domains if os.path.exists(cache_path(d))
                 and os.path.getsize(cache_path(d)) > 2)
print(f"\n{len(unique)} hostnames unicos de {with_hosts} dominios -> {OUT}")
print(f"tiempo: {(time.time() - start) / 60:.1f} min")
