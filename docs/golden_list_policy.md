# VELO Golden List — Inclusion Policy

The Golden List is a curated set of domains that meet VELO's highest privacy standards. Sites on this list display the ⭐ Gold shield score.

## Inclusion Criteria

A domain must satisfy **all** of the following:

1. **Zero detectable third-party trackers** — verified with VELO's own blocklists and manual inspection
2. **No analytics CDNs** from Google, Meta, Amazon, Microsoft, or Cloudflare Analytics
3. **Clear, auditable privacy policy** — publicly accessible, written in plain language
4. **Domain registered for >2 years** — reduces risk of newly-registered phishing/impersonation domains
5. **No ownership conflict of interest** — not owned by an advertising conglomerate or data broker
6. **Manual review by a project maintainer** — automated tools alone are not sufficient

## Exclusion Criteria (automatic disqualification)

- Any Google Analytics, Meta Pixel, or similar cross-site tracking script detected
- Opaque or missing privacy policy
- Domain registered <2 years ago (exceptions considered case-by-case for well-known OSS projects)
- Parent company with a history of privacy violations

## How to Propose an Addition

1. Open a GitHub Issue with the label `golden-list-proposal`
2. Include: domain, category, evidence of zero trackers (e.g., uBlock Origin report), privacy policy link
3. A maintainer will verify and, if approved, add it to `resources/blocklists/golden_list.json`

## How to Request Removal

If you are the owner of a listed domain and believe your site was incorrectly included or has changed, open an Issue with label `golden-list-removal`. Include the domain and reason.

## Update Mechanism

`GoldenListUpdater` fetches the latest list from the repository once per day using `If-None-Match`. Every update is verified against a Minisign detached signature (`.sig` file). If signature verification fails, the update is silently discarded and the previous list remains active.

## Current List

See `resources/blocklists/golden_list.json`. Initial seed: ~200 curated domains across categories: search, email, privacy tools, reference, dev platforms, and communications.
