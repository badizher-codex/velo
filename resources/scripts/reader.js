// VELO — Reader Mode Extractor
// Called via ExecuteScriptAsync — returns JSON, never modifies the page.
// C# receives the JSON, builds the full HTML, and calls NavigateToString().
(function () {
    'use strict';

    // ── 1. Find article node ─────────────────────────────────────────────

    function findArticleNode() {
        const SELECTORS = [
            'article',
            '[role="article"]',
            '.article-body', '.article-content', '.article-text',
            '.post-body', '.post-content', '.post-text',
            '.entry-content', '.entry-body',
            '.story-body', '.story-content',
            '.content-body', '.content-article',
            '.news-body', '.news-content',
            '.body-content', '.main-content',
            '#article-body', '#article-content',
            '#story-body', '#main-content',
            'main article', 'main [role="main"]',
        ];
        for (const sel of SELECTORS) {
            try {
                const el = document.querySelector(sel);
                if (el && el.innerText.trim().length > 200) return el;
            } catch (_) {}
        }
        // Heuristic: highest text-density block
        let best = null, bestScore = 0;
        document.querySelectorAll('div, section, main').forEach(el => {
            const text    = el.innerText.trim();
            const links   = el.querySelectorAll('a').length;
            const density = links > 0 ? text.length / links : text.length;
            if (text.length > 300 && density > bestScore) {
                bestScore = density;
                best = el;
            }
        });
        return best;
    }

    // ── 2. Meta helpers ──────────────────────────────────────────────────

    function findMeta(selectors) {
        for (const sel of selectors) {
            try {
                const el = document.querySelector(sel);
                if (el) return el.textContent.trim();
            } catch (_) {}
        }
        return '';
    }

    // ── 3. Extract ───────────────────────────────────────────────────────

    const node = findArticleNode();
    if (!node) return JSON.stringify({ found: false });

    const clone = node.cloneNode(true);

    // Strip junk from clone (not from the live page)
    ['script', 'style', 'noscript', 'iframe', 'form', 'button', 'input',
     'aside', 'nav', 'header', 'footer',
     '.ad', '.ads', '.advertisement', '.social', '.share',
     '.related', '.comments', '.newsletter', '.subscribe', '.sidebar',
     '[class*="promo"]', '[class*="banner"]', '[id*="sidebar"]',
     '[id*="ad-"]', '[class*="ad-"]'
    ].forEach(sel => {
        try { clone.querySelectorAll(sel).forEach(e => e.remove()); } catch (_) {}
    });

    const title   = document.title.replace(/ [|\-–—·•] .+$/, '').trim();
    const byline  = findMeta(['.author', '.byline', '[rel="author"]', '.author-name', 'address']);
    const dateStr = findMeta(['time[datetime]', '.date', '.pub-date', '.article-date', '.timestamp']);

    return JSON.stringify({
        found:  true,
        title:  title,
        byline: byline,
        date:   dateStr,
        html:   clone.innerHTML
    });

})();
