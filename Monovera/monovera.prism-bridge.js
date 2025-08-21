// Normalize Confluence export <pre class="code-xxx"> (and .code.panel) into Prism <pre class="language-xxx"><code>…</code></pre>
(function () {
  // Map Confluence "code-xxx" to Prism languages
  const langMap = {
    json: 'json',
    xml: 'markup', html: 'markup',
    yaml: 'yaml', yml: 'yaml',
    js: 'javascript', javascript: 'javascript',
    ts: 'typescript', typescript: 'typescript',
    cs: 'csharp', csharp: 'csharp',
    sql: 'sql',
    gherkin: 'gherkin',
    ini: 'ini',
    diff: 'diff'
  };

  function normalizePre(pre) {
    // Try class "code-xxx"
    let match = pre.className.match(/(?:^|\s)code-([a-z0-9_-]+)/i);
    // Or infer from nested container classes
    if (!match) {
      const container = pre.closest('.code.panel, .preformatted.panel');
      if (container) {
        const m2 = Array.from(pre.classList).map(c => c.match(/^code-([a-z0-9_-]+)/i)).find(Boolean);
        match = m2 || null;
      }
    }

    const key = (match && match[1] || '').toLowerCase();
    const prismLang = langMap[key] || (key || 'markup');

    // Extract plain source text (remove Confluence spans)
    const raw = pre.textContent || '';

    // Rewrite <pre> classes
    pre.className = pre.className
      .replace(/\bcode-[a-z0-9_-]+\b/ig, '')
      .replace(/\s+/g, ' ')
      .trim();
    pre.classList.add('language-' + prismLang);

    // Ensure child <code> with proper class and plain text
    let code = pre.querySelector('code');
    if (!code) {
      code = document.createElement('code');
      pre.appendChild(code);
    }
    code.className = 'language-' + prismLang;
    code.textContent = raw; // critical: no innerHTML

    return pre;
  }

  function run() {
    // Handle Confluence code panels
    document.querySelectorAll('.code.panel pre, .preformatted.panel pre').forEach(normalizePre);
    // Handle standalone Confluence pre.code-xxx
    document.querySelectorAll('pre[class*="code-"]').forEach(normalizePre);
    // Let Prism (already loaded) do its work
    if (window.Prism && Prism.highlightAllUnder) {
      Prism.highlightAllUnder(document);
    } else if (window.Prism && Prism.highlightAll) {
      Prism.highlightAll();
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', run);
  } else {
    run();
  }
})();