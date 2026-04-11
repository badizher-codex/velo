This directory contains bundled blocklist snapshots included in the build.

VELO will load these at startup. Background updates pull fresh versions from:
- EasyList (easylist.to)
- EasyPrivacy (easylist.to)
- uBlock Origin Filters (github.com/uBlockOrigin/uAssets)
- uBlock Badware (github.com/uBlockOrigin/uAssets)
- Peter Lowe's Ad List (pgl.yoyo.org)

If no internet is available at startup, these bundled files are used.
The update date is shown in Settings → Avanzado → Blocklists.

Format: ABP (Adblock Plus) or HOSTS — both parsed by BlocklistManager.
