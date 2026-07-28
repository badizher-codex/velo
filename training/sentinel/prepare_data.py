"""VELO Sentinel S-B — build the labeled HOST dataset from public feeds.

Labels: 0=benign, 1=phishing, 2=tracker, 3=ad.
Output: data/dataset.csv (url,label) + train/val/test splits. The "url"
column holds HOSTS (model-v5 decision): with ~100k domains seen 1-2 times
each, a URL model generalises on path SHAPE and synthetic benign paths are
always distinguishable from real ones (github.com/<user>/<repo> hit
phishing 1.000). VELO's verdict pipeline is host-keyed anyway (RequestGuard
rules, whitelist, Allow-once — all per host), so the model classifies what
the product consumes. Path-borne phishing on legit domains is explicitly
out of scope for tier-1 (top-1000-wins-benign policy).
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
    """Extract UNCONDITIONAL ||domain^ blocking rules from an EasyList-syntax
    file. Rules with options ($third-party, domain=…) are contextual — the
    domain is NOT inherently a tracker (model-v1 lesson: google.com got
    labeled tracker via its $third-party rules and the model flagged it)."""
    out = []
    for line in text.splitlines():
        m = re.match(r"^\|\|([a-z0-9.-]+\.[a-z]{2,})\^$", line.strip(), re.I)
        if m and "*" not in m.group(1):
            out.append(m.group(1).lower())
    return list(dict.fromkeys(out))


# Model-v3 lesson: benign training hosts had no subdomains (Tranco is root
# domains) while real phishing is full of deep hosts — so outlook.live.com
# and accounts.google.com got flagged. Legit sites use subdomains too;
# 60% curated common labels, 40% arbitrary word (outlook.live.com,
# gist.github.com are brand-specific).
SUBDOMAINS = ["www", "m", "app", "api", "mail", "accounts", "login", "docs",
              "blog", "shop", "support", "news", "es", "en", "static", "store"]
WORDS = ["watch", "browse", "detail", "search", "article", "blog", "docs",
         "profile", "product", "category", "news", "video", "user", "item",
         "post", "help", "about", "login", "signin", "account", "mail",
         "settings", "outlook", "drive", "photos", "id", "auth", "sso",
         "portal", "my", "web", "secure", "cloud", "dev", "wiki"]


def benign_hosts(domain: str) -> list[str]:
    hosts = [domain, f"www.{domain}"]
    for _ in range(2):
        sub = random.choice(SUBDOMAINS) if random.random() < 0.6 else random.choice(WORDS)
        hosts.append(f"{sub}.{domain}")
    return list(dict.fromkeys(hosts))


def host_of(url: str) -> str:
    try:
        return url.split("//", 1)[1].split("/", 1)[0].split(":", 1)[0].lower()
    except Exception:
        return ""


def root_domain(host: str) -> str:
    return ".".join(host.split(".")[-2:]) if host else ""


rows: list[tuple[str, int]] = []

# ── trackers / ads first: their domain sets also filter the benign pool ─
print("easyprivacy…")
tracker_domains = adblock_domains(fetch("https://easylist.to/easylist/easyprivacy.txt").decode(errors="replace"))
print("easylist…")
ad_domains = adblock_domains(fetch("https://easylist.to/easylist/easylist.txt").decode(errors="replace"))
blocked = set(tracker_domains) | set(ad_domains)
for d in tracker_domains:
    rows += [(d, 2), (f"www.{d}", 2)]
for d in ad_domains:
    rows += [(d, 3), (f"www.{d}", 3)]

# ── benign: Tranco top-100k minus unconditionally-blocked domains ───────
print("tranco…")
z = zipfile.ZipFile(io.BytesIO(fetch("https://tranco-list.eu/top-1m.csv.zip")))
tranco = pd.read_csv(z.open(z.namelist()[0]), names=["rank", "domain"]).head(100_000)
# Top-10k hosts are exempt from the class cap's random sampling (model-v6:
# www.primevideo.com got sampled out of training and misclassified).
benign_top: list[tuple[str, int]] = []
for rank, d in zip(tranco["rank"], tranco["domain"]):
    if d in blocked:
        continue
    target = benign_top if rank <= 10_000 else rows
    target += [(h, 0) for h in benign_hosts(d)]

# Top-1000 root domains win benign: phishing hosted ON major platforms
# (github.com raw pages, drive.google.com, …) is real but unresolvable
# from the host alone — training on it teaches the model to flag the
# platform (model-v4: github.com classified phishing). Better a miss there
# (PhishingShield's other layers still apply) than flagging a top site.
top1k = set(tranco["domain"].head(1000))

# ── phishing hosts: OpenPhish + PhishTank + URLhaus ─────────────────────
print("openphish…")
rows += [(host_of(u), 1) for u in fetch("https://openphish.com/feed.txt").decode().splitlines() if u.startswith("http")]

print("phishtank…")
try:
    pt = pd.read_csv(io.BytesIO(fetch("http://data.phishtank.com/data/online-valid.csv.gz")), compression="gzip")
    rows += [(host_of(u), 1) for u in pt["url"].dropna()]
except Exception as e:  # feed sometimes requires registration — optional
    print(f"  phishtank skipped: {e}")

print("urlhaus…")
try:
    uh = pd.read_csv(io.BytesIO(fetch("https://urlhaus.abuse.ch/downloads/csv_online/")), skiprows=8)
    col = "url" if "url" in uh.columns else uh.columns[2]
    rows += [(host_of(u), 1) for u in uh[col].dropna()]
except Exception as e:
    print(f"  urlhaus skipped: {e}")

# ── dedupe, resolve conflicts, cap per class, split ─────────────────────
df = pd.DataFrame(rows, columns=["url", "label"])
df = df[df["url"].str.len() > 3].drop_duplicates("url")
before = len(df)
df = df[~((df["label"] == 1) & df["url"].map(root_domain).isin(top1k))]
print(f"dropped {before - len(df)} phishing hosts on top-1000 domains")
# IP-literal hosts can't be judged by name — drop them (RequestGuard's
# SSRF/rebinding rules own that space).
df = df[~df["url"].str.match(r"^\d+\.\d+\.\d+\.\d+$")]

# Benign gets a higher cap: over-representing it biases residual confusion
# away from benign false positives, which is the gate that matters most.
# benign_top (Tranco ≤10k) bypasses sampling entirely.
top_df = pd.DataFrame(benign_top, columns=["url", "label"]).drop_duplicates("url")
caps = {0: 200_000, 1: PER_CLASS_CAP, 2: PER_CLASS_CAP, 3: PER_CLASS_CAP}
parts = [g.sample(min(len(g), caps[label]), random_state=42) for label, g in df.groupby("label")]
# top_df first: on a host collision (e.g. a phishing feed entry on a
# rank-1001..10k domain) the benign label wins for top sites.
df = pd.concat([top_df] + parts).drop_duplicates("url").sample(frac=1, random_state=42).reset_index(drop=True)
print(df["label"].value_counts().rename({0: "benign", 1: "phishing", 2: "tracker", 3: "ad"}))

n = len(df)
df.iloc[: int(n * 0.8)].to_csv("data/train.csv", index=False)
df.iloc[int(n * 0.8): int(n * 0.9)].to_csv("data/val.csv", index=False)
df.iloc[int(n * 0.9):].to_csv("data/test.csv", index=False)
df.to_csv("data/dataset.csv", index=False)
print(f"total {n} -> data/train.csv / val.csv / test.csv")
