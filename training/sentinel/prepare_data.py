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

model-v2 (2026-07-29) — the first shadow-mode session in the real browser
found the failure the offline gates could not: model-v1 called YouTube's
video servers PHISHING at p=0.965 and its image CDNs TRACKER at p>=0.98
(rr7---sn-0opoxu-j8we.googlevideo.com, i.ytimg.com, yt3.ggpht.com), plus
assets.grok.com and external-content.duckduckgo.com. AUC was 0.9907 and
benign FPR 0.74% — the aggregate said nothing about WHICH hosts were lost.

Two learned shortcuts, both caused by what the benign side never contained:

  1. "CDN-shaped subdomain = tracker". Benign hosts were generated with
     app-flavoured labels only (www/m/app/api/mail/login...), while the
     tracker+ad side is real EasyList domains, thick with cdn./static./
     assets./img.. So the SHAPE alone separated the classes.
  2. "machine-generated hostname = phishing". Benign labels were always
     dictionary words; the phishing feeds are full of random-looking hosts.
     Nothing benign in training ever looked like rr7---sn-0opoxu-j8we.

Fix below, in the data (raising tau does nothing — these arrive at p>=0.98):
real asset/media CDN domains with the host shapes they actually serve from,
including machine-generated labels, all on the benign side. Both changes are
about forcing the model to read the REGISTRABLE DOMAIN instead of the shape
of the subdomain.

model-v2 round 2 — round 1 fixed the CDNs (googlevideo/ytimg/ggpht/grok all
benign at p>=0.97) and broke the trackers: google-analytics.com came back
benign p=0.993, cdn.taboola.com 0.999, static.criteo.net 0.998. Two causes,
both measured rather than guessed:

  a. Those domains were labeled BENIGN, in v1 too. adblock_domains() takes
     only unconditional ||domain^ rules, and the biggest trackers are blocked
     WITH options ($third-party) because context matters — so they never
     entered `blocked`, and Tranco handed them over as benign. Fixed with
     adblock_domains_any(): a wider net used ONLY to exclude from the benign
     pool, never to label. 1,188 Tranco domains left training this way.
  b. Giving tracker/ad the same 4 host shapes as benign quadrupled their rows,
     and the 120k row cap then sampled away 36% of the tracker DOMAINS
     (27,085 of 42,692 survived). Domain coverage is the thing the model
     learns; shapes are only how each domain is presented. Cap raised to 220k
     so the whole EasyList/EasyPrivacy pool fits.

Round 2 also added the labels the first pass still missed: `code`
(code.jquery.com → tracker p=0.88) and hyphenated multi-word asset hosts
(external-content.duckduckgo.com → tracker p=0.9995).

model-v3 (2026-07-30) — replaying 75 hosts from a REAL shadow session through
model-v2 left 53 still blocked: cart-mf.cinepolis.com, myaccount.ea.com,
pin-river.data.ea.com, merchantpool1.linkedin.com, *.fastly.steamstatic.com,
*.w.hcaptcha.com, use.typekit.net, and checkout.steampowered.com actually got
WORSE than v1 (phishing 0.765 → 0.902, crossing the block threshold). Adding
more hand-written vocabulary was never going to fix it, because the problem is
the vocabulary itself.

Measured, not guessed: 7,465 real hostnames from Certificate Transparency
carry 6,232 distinct labels — 5,230 appearing exactly once, 71% hyphenated,
47% with a digit, median length 12. The curated lists in this file are ~90
short dictionary words, almost none hyphenated or numeric. Meanwhile the
tracker/ad side has always been REAL EasyList domains. So the label alone
separated the classes, and no amount of `cdn`/`static`/`assets` additions
could close a gap that wide.

model-v3 draws labels from the real distribution (harvest_ct.py → REAL_LABELS)
for benign and tracker/ad alike, and nests two levels a quarter of the time
because real hosts do. Note the harvest's own bias is an asset here: it is
dominated by internal/staging infrastructure a browser never requests, which
is useless as hostnames and ideal as label shapes.
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
# model-v2 — raised from 120k. The cap is on ROWS, and giving tracker/ad the
# same 4 host shapes as benign (shaped_hosts) quadrupled rows per domain, so
# the old cap silently sampled away 36% of the tracker DOMAINS: 27,085 of
# 42,692 survived, and google-analytics-class domains disappeared. Domain
# coverage is what the model learns from; the shapes are just how each domain
# is presented. Sized to hold the whole EasyPrivacy + EasyList pool
# (~43k + ~51k domains x ~4 shapes) without sampling.
PER_CLASS_CAP = 220_000


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


def adblock_domains_any(text: str) -> list[str]:
    """Every ||domain^ rule, options or not.

    model-v2 measurement: the unconditional filter above is right about not
    LABELING these as trackers, but keeping them out of the exclusion set was
    a hole. The biggest trackers on the web — google-analytics.com,
    doubleclick.net, criteo.net, taboola.com, cookielaw.org — are blocked with
    options precisely because context matters, so none of them appeared in
    `blocked`, and Tranco then handed every one of them to the model as
    BENIGN. model-v1 shipped believing google-analytics.com was benign at
    p=0.993. Nobody noticed because the exact blocklist catches them anyway —
    but teaching the classifier that analytics-shaped domains are fine is the
    opposite of what it exists for.

    These are used ONLY to keep such domains out of the benign pool. They do
    not become positive tracker labels: a $third-party rule on a legit domain
    is contextual, and labeling from it is what got google.com flagged in
    model-v1. Absent from training is the honest answer — the lists own these.
    """
    out = []
    for line in text.splitlines():
        m = re.match(r"^\|\|([a-z0-9.-]+\.[a-z]{2,})\^(\$.*)?$", line.strip(), re.I)
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


# model-v2 lesson 1 — the shape of a subdomain must not decide the class.
# Every one of these labels used to appear ONLY on the tracker/ad side.
CDN_SUBDOMAINS = ["cdn", "static", "assets", "img", "images", "i", "media",
                  "video", "stream", "files", "content", "edge", "cache",
                  "thumb", "thumbs", "uploads", "dl", "fonts", "js", "css",
                  "s", "c", "asset", "player", "download", "storage",
                  # model-v2 round 2: real labels the first pass still missed —
                  # code.jquery.com came back tracker p=0.88.
                  "code", "lib", "libs", "pkg", "npm", "cdn2", "static1",
                  "external-content", "media-cdn", "static-assets",
                  "cdn-images", "img-cdn", "asset-cache", "user-content",
                  "public-assets", "file-store"]


def machine_label(min_len: int = 4, max_len: int = 10) -> str:
    """A label that looks generated rather than written — the exact thing
    RequestGuard.LooksRandomGenerated flags, and that model-v1 had only ever
    seen on phishing hosts."""
    alphabet = "abcdefghijklmnopqrstuvwxyz0123456789"
    return "".join(random.choice(alphabet) for _ in range(random.randint(min_len, max_len)))


def shard_label() -> str:
    """Numbered shards: img3, s1, cdn02, static4 — extremely common on real
    sites and absent from the old benign generator."""
    base = random.choice(["s", "c", "i", "img", "cdn", "static", "media", "n", "e", "f"])
    return f"{base}{random.randint(1, 99)}"


# model-v3 — the real subdomain-label vocabulary, harvested from Certificate
# Transparency by harvest_ct.py.
#
# This replaces guessing. Measured on the first harvest: 7,465 real hostnames
# yielded 6,232 distinct labels, 5,230 of them appearing exactly once, 71% with
# a hyphen and 47% with a digit, median length 12. The hand-written vocabulary
# below it is ~90 short dictionary words, almost none hyphenated or numeric.
# That gap IS the bug model-v2 could not shake: the tracker/ad side is real
# EasyList domains, so the label alone separated the classes and the model
# learned "a label I recognise means benign, anything else means tracker" —
# which is why it blocked cart-mf.cinepolis.com, pin-river.data.ea.com and
# merchantpool1.linkedin.com at p>0.99.
#
# Only the LABELS are used, never the hostnames. The harvest is dominated by
# internal/staging infrastructure (corp., stg., crew-backend.) that a browser
# never requests — useless as hostnames, ideal as label shapes, because they
# are exactly the weird-but-benign labels the synthetic generator could not
# produce. Labels are drawn by frequency, so www/api/cdn stay common and the
# long tail still shows up.
CT_HOSTS_FILE = "data/ct_hosts.txt"


def load_real_labels() -> list[str]:
    """Flat list of labels (repeats kept, so sampling follows the real
    frequency distribution). Empty when the harvest hasn't run — the generator
    then falls back to the curated vocabulary alone."""
    if not os.path.exists(CT_HOSTS_FILE):
        return []
    out = []
    for line in open(CT_HOSTS_FILE, encoding="utf-8"):
        parts = line.strip().split(".")
        for label in parts[:-2]:
            if label and len(label) <= 40:
                out.append(label)
    return out


REAL_LABELS = load_real_labels()
print(f"vocabulario real: {len(REAL_LABELS)} etiquetas ({len(set(REAL_LABELS))} distintas)"
      if REAL_LABELS else "sin data/ct_hosts.txt — usando solo el vocabulario curado")


def random_sub() -> str:
    """One subdomain label, from the mix of shapes real hosts actually use."""
    roll = random.random()
    # The curated lists stay in the mix: they carry the labels that matter most
    # (www/api/cdn/static) at a rate the CT harvest under-represents, since CT
    # is skewed toward internal infrastructure.
    if roll < 0.25:
        return random.choice(SUBDOMAINS)
    if roll < 0.40:
        return random.choice(WORDS)
    if roll < 0.55:
        return random.choice(CDN_SUBDOMAINS)
    if roll < 0.62:
        return shard_label()
    if REAL_LABELS:
        return random.choice(REAL_LABELS)       # model-v3: real label shapes
    return machine_label()


def shaped_hosts(domain: str, n: int = 2) -> list[str]:
    """Bare + www + n randomly-shaped subdomains.

    model-v2: used for BOTH benign and tracker/ad domains, on purpose. Giving
    the CDN-ish and machine-generated shapes only to the benign side would
    just invert model-v1's shortcut — the model would learn "cdn.* is safe"
    and stop seeing cdn.taboola.com / static.criteo.net / pixel.*. The shape
    has to be uninformative on both sides so the registrable domain is the
    only thing left to learn from.
    """
    hosts = [domain, f"www.{domain}"]
    for _ in range(n):
        hosts.append(f"{random_sub()}.{domain}")
    # model-v3 — real hosts nest: p1-st-iad-ss-user-targeting.linkedin.com sits
    # beside orc3191.corpinternal.corp.linkedin.com. Two levels appeared in
    # every harvested domain and never once in the synthetic generator.
    if random.random() < 0.25:
        hosts.append(f"{random_sub()}.{random_sub()}.{domain}")
    return list(dict.fromkeys(hosts))


def benign_hosts(domain: str, n: int = 2) -> list[str]:
    return shaped_hosts(domain, n)


# model-v2 lesson 2 — real asset/media CDNs, with the host shapes they
# actually serve from. The names matter less than the shapes: this is the
# only place in the benign set where a host is allowed to look like a
# machine produced it. Each builder returns hosts modelled on traffic seen
# in the S-C shadow logs and in ordinary browsing.
CDN_DOMAINS = [
    # Google / YouTube — the ones that broke in shadow
    "googlevideo.com", "ytimg.com", "ggpht.com", "gstatic.com",
    "googleusercontent.com", "googleapis.com",
    # Social
    "fbcdn.net", "cdninstagram.com", "twimg.com", "licdn.com",
    "redditmedia.com", "redd.it", "tiktokcdn.com",
    # Generic CDN / cloud edges
    "akamaized.net", "akamai.net", "akamaihd.net", "cloudfront.net",
    "fastly.net", "fastlylb.net", "azureedge.net", "azurefd.net",
    "cdn77.org", "stackpathdns.com", "b-cdn.net", "kxcdn.com",
    "edgecastcdn.net", "llnwd.net", "cachefly.net",
    # Public script/asset CDNs (the jsdelivr family from the first finding)
    "jsdelivr.net", "unpkg.com", "bootstrapcdn.com", "jquery.com",
    # Object storage that serves site assets
    "amazonaws.com", "digitaloceanspaces.com", "backblazeb2.com",
    # Site platforms
    "wp.com", "wixstatic.com", "shopifycdn.com", "squarespace-cdn.com",
    "cloudinary.com", "imgix.net", "typekit.net",
    # Media / streaming
    "vimeocdn.com", "jwpcdn.com", "brightcove.com", "mzstatic.com",
    "nflxvideo.net", "nflximg.net", "nflxso.net", "scdn.co", "sndcdn.com",
    # Prime Video — the last host the field gate caught, and missing for the
    # same reason nflxso.net was: nobody had listed it. Not a special case,
    # a gap in a list that is supposed to hold real media CDNs.
    "pv-cdn.net", "aiv-cdn.net", "aiv-delivery.net", "media-amazon.com",
    "steamstatic.com", "imgur.com", "giphy.com",
]


def cdn_hosts(domain: str) -> list[str]:
    """Realistic hostnames for an asset/media CDN, including the generated
    shapes. Deliberately over-samples the machine-y forms — those are what
    model-v1 got wrong."""
    hosts = [domain, f"www.{domain}"]

    # The literal shapes from the shadow logs, generalised.
    if domain == "googlevideo.com":
        for _ in range(12):
            hosts.append(
                f"rr{random.randint(1, 14)}---sn-{machine_label(5, 7)}-{machine_label(4, 4)}.{domain}")
    elif domain in ("ytimg.com", "ggpht.com"):
        hosts += [f"i.{domain}", f"i9.{domain}", f"s.{domain}"]
        hosts += [f"yt{n}.{domain}" for n in range(1, 5)]
    elif domain == "googleusercontent.com":
        hosts += [f"lh{n}.{domain}" for n in range(1, 7)]
        hosts += [f"{machine_label(6, 10)}.{domain}"]
    elif domain == "fbcdn.net":
        for _ in range(6):
            place = random.choice(["lax", "sjc", "iad", "ams", "cdg", "gru", "hkg"])
            kind = random.choice(["scontent", "video", "static"])
            hosts.append(f"{kind}-{place}{random.randint(1, 5)}-{random.randint(1, 3)}.xx.{domain}")
    elif domain == "cloudfront.net":
        hosts += [f"d{machine_label(12, 13)}.{domain}" for _ in range(8)]
    elif domain in ("akamaized.net", "akamaihd.net", "akamai.net"):
        hosts += [f"{machine_label(5, 8)}.{domain}" for _ in range(6)]
        hosts += [f"a{random.randint(1, 999)}.{random.choice('gwx')}.{domain}" for _ in range(3)]
    elif domain == "amazonaws.com":
        for _ in range(6):
            region = random.choice(["us-east-1", "us-west-2", "eu-west-1", "sa-east-1", "ap-south-1"])
            hosts.append(f"s3.{region}.{domain}")
            hosts.append(f"{machine_label(6, 12)}.s3.{region}.{domain}")
    elif domain in ("b-cdn.net", "kxcdn.com", "cdn77.org", "stackpathdns.com"):
        hosts += [f"{machine_label(6, 10)}.{domain}" for _ in range(5)]
    elif domain == "nflxvideo.net":
        # Netflix Open Connect appliances live INSIDE the ISP and put the ISP's
        # name in the hostname: ipv4-c011-cvj001-telmex-isp.1.oca.nflxvideo.net.
        # model-v3 called these trackers at p=0.999 — they are the servers that
        # stream the video, so that verdict is "Netflix does not play".
        for _ in range(10):
            isp = random.choice(["telmex", "totalplay", "izzi", "comcast", "vodafone",
                                 "movistar", "claro", "telefonica", "orange", "att"])
            hosts.append(f"ipv{random.choice(['4', '6'])}-c{random.randint(1, 300):03d}-"
                         f"{machine_label(3, 4)}{random.randint(1, 999):03d}-{isp}-isp."
                         f"{random.randint(1, 4)}.oca.{domain}")
    elif domain in ("pv-cdn.net", "aiv-cdn.net", "aiv-delivery.net"):
        # Prime Video buries a long opaque token in the leftmost label:
        # ablxdzpaaaaaaaammczrxmvbk4agc.ta.pop-vod-dash.main.amazon.pv-cdn.net
        # — 29 random characters, which is precisely the shape the model reads
        # as phishing until it has seen benign examples of it.
        for _ in range(10):
            mid = random.choice(["ta.pop-vod-dash.main.amazon", "aux", "xp-assets",
                                 "cf-trickplay.aux", "us-east-1"])
            hosts.append(f"{machine_label(24, 30)}.{mid}.{domain}")
        hosts += [f"{machine_label(5, 9)}-draper{random.randint(1, 9)}."
                  f"us-east-{random.randint(1, 2)}.{domain}" for _ in range(4)]
    elif domain == "nflxso.net":
        # Netflix's image/asset shard: occ-0-8407-2218.1.nflxso.net
        for _ in range(10):
            hosts.append(f"occ-{random.randint(0, 9)}-{random.randint(100, 9999)}-"
                         f"{random.randint(30, 9999)}.{random.randint(1, 3)}.{domain}")
        hosts.append(f"occ.a.{domain}")
        hosts += [f"{machine_label(18, 22)}-us-west-{random.randint(1, 2)}.r.{domain}"
                  for _ in range(4)]

    # Every CDN also gets the ordinary shapes, so the domain itself is what
    # carries the benign signal rather than any one pattern.
    for _ in range(6):
        hosts.append(f"{random.choice(CDN_SUBDOMAINS)}.{domain}")
    for _ in range(3):
        hosts.append(f"{shard_label()}.{domain}")
    for _ in range(3):
        hosts.append(f"{machine_label()}.{domain}")

    # model-v3 — the CDN provider as a MIDDLE label: cdn.fastly.steamstatic.com,
    # video.akamai.steamstatic.com. An extremely common convention that the
    # generator never produced, and the reason Steam's whole asset CDN was still
    # coming back tracker at p>0.99 after the vocabulary fix.
    for provider in ("fastly", "akamai", "cloudfront", "edge", "cdn"):
        for _ in range(2):
            hosts.append(f"{random.choice(CDN_SUBDOMAINS)}.{provider}.{domain}")

    return list(dict.fromkeys(hosts))


# model-v2 — a site's OWN asset hosts (assets.grok.com came back tracker
# p=0.982). Applied to the top of Tranco, where first-party asset hosts are
# both most common and most expensive to get wrong.
FIRST_PARTY_ASSET_SUBS = ["assets", "static", "cdn", "img", "images", "media",
                          "content", "files", "video", "uploads", "s1", "i",
                          # model-v2 round 2: hyphenated multi-word asset hosts
                          # were absent entirely, and external-content.duckduckgo.com
                          # came back tracker p=0.9995.
                          "external-content", "static-content", "user-content",
                          "media-assets", "cdn-static", "img-proxy", "asset-host"]


def host_of(url: str) -> str:
    try:
        return url.split("//", 1)[1].split("/", 1)[0].split(":", 1)[0].lower()
    except Exception:
        return ""


def root_domain(host: str) -> str:
    return ".".join(host.split(".")[-2:]) if host else ""


# model-v3 — shared infrastructure the model CANNOT judge, at any label.
#
# Measured: cloudfront.net had 5,319 rows in the v3 validation dataset — 4,731
# ad, 561 tracker, 27 benign — because EasyList lists thousands of individual
# customer distributions (d123abc.cloudfront.net) as ad rules. The model duly
# learned "*.cloudfront.net is an ad" at 175:1, and then blocked Duolingo's
# d35aaqx5ub95lt.cloudfront.net at p=0.954.
#
# There is no amount of data that fixes this. On these CDNs every customer gets
# an opaque generated subdomain, so an ad network's distribution and a
# language app's are the same string shape on the same root: the information
# needed to tell them apart is not in the hostname. Training on either label
# teaches a rule that must be wrong half the time.
#
# So they are dropped from training entirely — no benign rows, no tracker/ad
# rows. The model then has no opinion, lands below the block threshold and
# allows, and the exact blocklists (which hold the specific distributions) keep
# owning them. Same reasoning as the `contextual` exclusion above: absent is
# the honest answer when the host cannot carry the decision.
SHARED_INFRA = {
    "cloudfront.net", "amazonaws.com", "azureedge.net", "azurefd.net",
    "b-cdn.net", "kxcdn.com", "cdn77.org", "stackpathdns.com",
    "akamaized.net", "akamaihd.net", "akamai.net", "edgecastcdn.net",
    "fastly.net", "fastlylb.net", "llnwd.net", "cachefly.net",
    "digitaloceanspaces.com", "backblazeb2.com", "herokuapp.com",
    "appspot.com", "cloudfunctions.net", "workers.dev", "pages.dev",
    "netlify.app", "vercel.app", "web.app", "firebaseapp.com",
    "githubusercontent.com", "blob.core.windows.net", "trafficmanager.net",
}


def is_shared_infra(host: str) -> bool:
    parts = host.split(".")
    for i in range(len(parts) - 1):
        if ".".join(parts[i:]) in SHARED_INFRA:
            return True
    return False


# model-v3 — access-critical third parties, forced benign.
#
# EasyPrivacy lists hCaptcha, and it is not wrong to: bot-protection works by
# fingerprinting. But blocking a CAPTCHA does not degrade a page the way
# blocking an analytics beacon does — it locks the user out of their own
# account, with no error a person could connect to a security setting. A
# browser that cannot log you in is not protecting you.
#
# This is a product judgement, not a data-quality fix, and it is deliberately a
# short list: bot-protection and CAPTCHA only. Anything whose absence merely
# costs the site some telemetry does not belong here.
FUNCTIONAL_EXEMPT = {
    "hcaptcha.com", "recaptcha.net", "arkoselabs.com", "funcaptcha.com",
    "geetest.com", "friendlycaptcha.com", "turnstile.com",
}


def is_functional(host: str) -> bool:
    parts = host.split(".")
    for i in range(len(parts) - 1):
        if ".".join(parts[i:]) in FUNCTIONAL_EXEMPT:
            return True
    return False


rows: list[tuple[str, int]] = []

# ── trackers / ads first: their domain sets also filter the benign pool ─
print("easyprivacy…")
easyprivacy = fetch("https://easylist.to/easylist/easyprivacy.txt").decode(errors="replace")
tracker_domains = adblock_domains(easyprivacy)
print("easylist…")
easylist = fetch("https://easylist.to/easylist/easylist.txt").decode(errors="replace")
ad_domains = adblock_domains(easylist)
blocked = set(tracker_domains) | set(ad_domains)
# model-v2 — wider net, used only to exclude from benign (see adblock_domains_any).
contextual = (set(adblock_domains_any(easyprivacy)) | set(adblock_domains_any(easylist))) - blocked
print(f"  labeled tracker {len(tracker_domains)} / ad {len(ad_domains)}; "
      f"{len(contextual)} more excluded from benign as contextual")
# model-v2 — trackers and ads get the same shape distribution as benign (see
# shaped_hosts). Before this they only ever appeared as `d` and `www.d`, so
# once the benign side gained cdn./static./random. subdomains the shape would
# have become a giveaway in the other direction.
skipped_infra = skipped_functional = 0
for domain_list, label in ((tracker_domains, 2), (ad_domains, 3)):
    for d in domain_list:
        if is_shared_infra(d):
            skipped_infra += 1
            continue
        if is_functional(d):
            skipped_functional += 1
            continue
        rows += [(h, label) for h in shaped_hosts(d)]
print(f"  {skipped_infra} reglas descartadas por infra compartida, "
      f"{skipped_functional} por ser acceso-criticas (CAPTCHA/bot-protection)")

# ── benign: Tranco top-100k minus unconditionally-blocked domains ───────
print("tranco…")
z = zipfile.ZipFile(io.BytesIO(fetch("https://tranco-list.eu/top-1m.csv.zip")))
tranco = pd.read_csv(z.open(z.namelist()[0]), names=["rank", "domain"]).head(100_000)
# Top-10k hosts are exempt from the class cap's random sampling (model-v6:
# www.primevideo.com got sampled out of training and misclassified).
benign_top: list[tuple[str, int]] = []
skipped_contextual = 0
for rank, d in zip(tranco["rank"], tranco["domain"]):
    if d in blocked:
        continue
    # model-v2 — a domain the lists block only in context is not evidence of
    # "tracker", but it is definitely not evidence of "benign" either. Drop it
    # from training and let the exact blocklist own it. Top-1000 still wins
    # benign, same policy as phishing-on-major-platforms: google.com carries
    # $third-party rules and must not be lost.
    if d in contextual and rank > 1000:
        skipped_contextual += 1
        continue
    # model-v3 — shared infra gets no benign rows either (see SHARED_INFRA).
    if is_shared_infra(d):
        continue
    target = benign_top if rank <= 10_000 else rows
    # model-v3 round 2 — depth per domain, not just breadth across domains.
    #
    # Measured on the field gate: every failure sat on a domain with almost no
    # representation — ea.com had SEVEN rows, pv-cdn.net eight, commercetools
    # four. A real site has dozens of subdomains; four synthetic ones teach the
    # model the domain exists but nothing about the shape of its host space, so
    # an unfamiliar label like pin-river.data.ea.com falls back on the prior —
    # and the prior is "tracker", because the tracker side is ~45k domains of
    # unfamiliar-looking names.
    target += [(h, 0) for h in benign_hosts(d, n=12 if rank <= 20_000 else 3)]
    # model-v2 — first-party asset hosts for the top of the list. Every site
    # of that size serves its own assets from somewhere, and model-v1 called
    # assets.grok.com a tracker at p=0.982.
    if rank <= 20_000:
        target += [(f"{sub}.{d}", 0)
                   for sub in random.sample(FIRST_PARTY_ASSET_SUBS, 6)]

# model-v2 — the asset/media CDNs themselves, exempt from the class cap for
# the same reason Tranco's top-10k is: these are precisely the hosts whose
# misclassification the user experiences as "the site is broken", so they may
# not be sampled out. A CDN that EasyList blocks unconditionally is left
# alone — the lists are the authority there, and a conflict would teach noise.
cdn_conflicts = [d for d in CDN_DOMAINS if d in blocked]
if cdn_conflicts:
    print(f"  CDN domains skipped (unconditionally blocked by the lists): {cdn_conflicts}")
for d in CDN_DOMAINS:
    # model-v3 — a shared-infra CDN gets no benign rows either. Injecting them
    # would just move the 175:1 imbalance to the other side; the point is that
    # the model must have NO opinion about these roots.
    if d in blocked or is_shared_infra(d):
        continue
    benign_top += [(h, 0) for h in cdn_hosts(d)]

# The access-critical providers are taught as benign rather than merely
# dropped: the model should be confident about them, not undecided. Exempt from
# the cap for the same reason Tranco's top-10k is — being sampled out is how a
# CAPTCHA provider quietly becomes blockable again.
for d in FUNCTIONAL_EXEMPT:
    benign_top += [(h, 0) for h in shaped_hosts(d, n=4)]
    benign_top += [(f"{sub}.{d}", 0)
                   for sub in ("js", "api", "assets", "newassets", "challenges", "cdn", "hcaptcha")]
    benign_top += [(f"{machine_label(8, 12)}.w.{d}", 0) for _ in range(6)]
print(f"  benign_top after CDN injection: {len(benign_top)} hosts")
print(f"  Tranco domains dropped as contextually blocked: {skipped_contextual}")

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
#
# model-v3 — the tracker/ad caps are now tied to how much phishing the feeds
# actually gave us. Raising them to 220k in v2 round 2 (to stop the cap eating
# tracker DOMAINS) pushed phishing down to 4.1% of the dataset, and the
# must-catch gate failed for the first time: paypal-secure-verification.net
# stopped being recognised. The feeds decide how much phishing exists, so they
# have to decide the ceiling for everything else.
top_df = pd.DataFrame(benign_top, columns=["url", "label"]).drop_duplicates("url")
phishing_rows = int((df["label"] == 1).sum())
class_cap = min(PER_CLASS_CAP, max(60_000, phishing_rows * 5))
print(f"  phishing disponible: {phishing_rows} → cap por clase {class_cap}")
caps = {0: max(200_000, class_cap), 1: PER_CLASS_CAP, 2: class_cap, 3: class_cap}
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
