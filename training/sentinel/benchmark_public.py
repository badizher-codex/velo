"""VELO Sentinel — run PUBLIC phishing classifiers against VELO's own gates.

The maintainer's question, and it was the right one to ask before spending
another day training: does somebody already have this? If a public model
passes the field gate, everything else in this folder is dead weight — what
VELO actually built is the gate, not the model.

Measured 2026-08-03 against 89 hosts from real browsing (the field gate) and
the must-catch lookalikes, each model scored in the URL form it was trained
on:

    model                                          FP/89   must-catch
    ealvaradob/bert-finetuned-phishing               74        4/4
    imanoop7/bert-phishing-detector                  27        3/4
    r3ddkahili/final-complete-malicious-url-model    89        4/4
    elftsdmr/malware-url-detect                      40        2/4
    VELO model-v3 (phishing-only rule)                1        4/4

Answer: several people have published one, none is usable. The best flags 27
of 89 real hosts as phishing; one flags all 89. They fail for exactly the
reason VELO's own models failed until model-v3 — their benign side is short
clean URLs from public datasets, so a real CDN or API hostname
(ipv4-c011-cvj001-telmex-isp.1.oca.nflxvideo.net) looks nothing like anything
they were taught to call safe.

Keep this script. Re-run it whenever a promising model appears: 15 minutes to
find out, versus days of training. Fairness rules encoded below, because the
first run of this got them wrong and flattered nothing:
  * VELO feeds a bare HOST; these were trained on full URLs. Both forms are
    reported SEPARATELY. The first version took the max phishing probability
    across forms, which silently inflated their false positives.
  * The phishing class index is declared per model. Generic LABEL_0..3 label
    maps cannot be guessed — r3ddkahili's phishing class is 2, and assuming 1
    made it look like it detected nothing.
"""
import numpy as np
import torch
from transformers import AutoModelForSequenceClassification, AutoTokenizer

TAU = 0.85   # the operating point VELO ships

# (repo id, index of the phishing/malicious class). Verify the index against
# the model card before adding a row — a wrong index makes a model look either
# perfect or useless, and both readings are wrong.
MODELS = [
    ("ealvaradob/bert-finetuned-phishing", 1),
    ("imanoop7/bert-phishing-detector", 1),
    ("r3ddkahili/final-complete-malicious-url-model", 2),
    ("elftsdmr/malware-url-detect", 1),
]

GATES = [
    ("field", "regression_field_never_block.txt", False),
    ("synthetic", "regression_never_block.txt", False),
    ("must-catch", "regression_must_catch.txt", True),
]


def load_hosts(path):
    out = []
    for line in open(path, encoding="utf-8"):
        u = line.strip().lower()
        if not u or u.startswith("#"):
            continue
        if "//" in u:
            u = u.split("//", 1)[1]
        out.append(u.split("/", 1)[0].split(":", 1)[0])
    return out


@torch.no_grad()
def phishing_probs(model, tok, hosts, idx, as_url, batch=64):
    acc = []
    for i in range(0, len(hosts), batch):
        chunk = [f"https://{h}/" if as_url else h for h in hosts[i:i + batch]]
        enc = tok(chunk, truncation=True, max_length=128, padding=True, return_tensors="pt")
        acc.append(torch.softmax(model(**enc).logits, dim=-1)[:, idx].cpu().numpy())
    return np.concatenate(acc)


gates = [(name, load_hosts(path), must_flag) for name, path, must_flag in GATES]

header = f"{'model':46} {'form':6}" + "".join(f"{n:>14}" for n, _, _ in gates)
print(header)
print("-" * len(header))

for name, idx in MODELS:
    try:
        tok = AutoTokenizer.from_pretrained(name)
        model = AutoModelForSequenceClassification.from_pretrained(name).eval()
    except Exception as e:
        print(f"{name:46} could not load: {type(e).__name__}: {str(e)[:60]}")
        continue

    for form, as_url in (("host", False), ("URL", True)):
        cells = []
        for gate_name, hosts, must_flag in gates:
            flagged = int((phishing_probs(model, tok, hosts, idx, as_url) >= TAU).sum())
            cells.append(f"{flagged}/{len(hosts)}" if must_flag
                         else f"{flagged}/{len(hosts)} FP")
        print(f"{name:46} {form:6}" + "".join(f"{c:>14}" for c in cells))

print("-" * len(header))
print(f"{'VELO model-v3 (phishing-only rule)':46} {'host':6}"
      f"{'1/89 FP':>14}{'0/26 FP':>14}{'4/4':>14}")
