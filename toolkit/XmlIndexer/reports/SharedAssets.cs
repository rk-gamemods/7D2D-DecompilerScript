using System.Text;
using System.Web;

namespace XmlIndexer.Reports;

/// <summary>
/// Centralized CSS, JavaScript, and HTML component generators shared across all report pages.
/// Obsidian theme (dark emerald/amber) is primary, with Graphite as alternate.
/// </summary>
public static class SharedAssets
{
    /// <summary>
    /// Get the complete CSS for the Obsidian/Graphite theme system.
    /// Written to assets/styles.css
    /// </summary>
    public static string GetStylesCss()
    {
        return @"/* 7D2D Ecosystem Report - Obsidian Theme */
:root {
  /* Backgrounds */
  --bg: #0d1117;
  --bg-secondary: #161b22;
  --card: #1c2128;
  --border: #30363d;

  /* Text */
  --text: #e6edf3;
  --text-muted: #8b949e;
  --text-dim: #6e7681;

  /* Accents - Emerald/Amber signature */
  --accent: #3fb950;
  --accent-secondary: #d29922;
  --info: #58a6ff;
  --danger: #f85149;

  /* Severity colors */
  --severity-high: #f85149;
  --severity-medium: #d29922;
  --severity-low: #58a6ff;

  /* Component tokens */
  --radius: 6px;
  --shadow: 0 1px 3px rgba(0,0,0,0.3);
}

[data-theme=""graphite""] {
  --bg: #1a1625;
  --bg-secondary: #231d2e;
  --card: #2d2640;
  --border: #3d3554;
  --accent: #a78bfa;
  --accent-secondary: #f472b6;
  --info: #c084fc;
  --danger: #fb7185;
  --severity-high: #fb7185;
  --severity-medium: #fbbf24;
  --severity-low: #c084fc;
}

* { margin: 0; padding: 0; box-sizing: border-box; }

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
  font-size: 14px;
  background: var(--bg);
  color: var(--text);
  line-height: 1.5;
  min-height: 100vh;
}

a { color: var(--accent); text-decoration: none; }
a:hover { text-decoration: underline; }

/* Navigation */
.site-nav {
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border);
  padding: 0 1.5rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  height: 48px;
  position: sticky;
  top: 0;
  z-index: 100;
}

.nav-brand {
  font-weight: 600;
  color: var(--accent);
  margin-right: 1rem;
  font-size: 15px;
}

.nav-item {
  padding: 0.5rem 0.75rem;
  color: var(--text-muted);
  border-radius: var(--radius);
  font-size: 13px;
  transition: all 0.15s;
}

.nav-item:hover {
  background: var(--card);
  color: var(--text);
  text-decoration: none;
}

.nav-item.active {
  background: var(--accent);
  color: #0d1117;
}

.theme-select {
  margin-left: auto;
  background: var(--card);
  border: 1px solid var(--border);
  color: var(--text);
  padding: 0.25rem 0.5rem;
  border-radius: var(--radius);
  font-size: 12px;
  cursor: pointer;
}

/* Main content area */
.page-container {
  max-width: 1400px;
  margin: 0 auto;
  padding: 1.5rem;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-header h1 {
  font-size: 1.5rem;
  font-weight: 600;
  margin-bottom: 0.25rem;
}

.page-header p {
  color: var(--text-muted);
  font-size: 14px;
}

/* Cards */
.card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 1rem;
}

.card-clickable {
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s;
}

.card-clickable:hover {
  border-color: var(--accent);
  transform: translateY(-2px);
}
a.card, a.card:hover,
a.card-clickable, a.card-clickable:hover,
.card a, .card a:hover {
  text-decoration: none;
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 1rem;
}

.feature-card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.feature-card .icon {
  font-size: 1.5rem;
}

.feature-card h3 {
  font-size: 1rem;
  font-weight: 600;
}

.feature-card p {
  color: var(--text-muted);
  font-size: 13px;
  flex: 1;
}

.feature-card .stats {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.feature-card .stat {
  font-size: 12px;
  color: var(--text-dim);
}

.feature-card .stat b {
  color: var(--accent);
  margin-right: 0.25rem;
}

/* Tags/Badges */
.tag {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
}

.tag-high { background: var(--severity-high); color: white; }
.tag-medium { background: var(--severity-medium); color: black; }
.tag-low { background: var(--severity-low); color: white; }
.tag-type { background: var(--accent); color: #0d1117; }
.tag-info { background: var(--info); color: white; }

.tag-healthy { background: rgba(63, 185, 80, 0.15); color: var(--accent); }
.tag-review { background: rgba(210, 153, 34, 0.15); color: var(--accent-secondary); }
.tag-broken { background: rgba(248, 81, 73, 0.15); color: var(--danger); }

/* Load order badge */
.load-order-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  height: 20px;
  padding: 0 6px;
  margin-right: 8px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-muted);
  background: var(--bg-tertiary);
  border-radius: 10px;
  font-family: 'JetBrains Mono', monospace;
}

/* Tables */
.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.data-table th,
.data-table td {
  padding: 0.5rem 0.75rem;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

.data-table th {
  font-size: 11px;
  text-transform: uppercase;
  color: var(--text-dim);
  font-weight: 500;
  background: var(--bg-secondary);
}

.data-table tr:hover {
  background: var(--bg-secondary);
}

.data-table tbody tr {
  cursor: pointer;
}

/* Code viewer for Harmony patches */
.code-viewer {
  max-height: 400px;
  overflow-y: auto;
  background: #1a1a2e;
  padding: 12px;
  border-radius: 4px;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  line-height: 1.4;
  margin-top: 0.5rem;
  white-space: pre-wrap;
  word-break: break-word;
  color: #e0e0e0;
  border: 1px solid var(--border);
}

.code-expand summary {
  user-select: none;
}

/* Filter bar */
.filter-bar {
  display: flex;
  gap: 1rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.filter-search {
  flex: 1;
  min-width: 200px;
  background: var(--bg-secondary);
  border: 1px solid var(--border);
  color: var(--text);
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius);
  font-size: 13px;
}

.filter-search:focus {
  outline: none;
  border-color: var(--accent);
}

.filter-select {
  background: var(--bg-secondary);
  border: 1px solid var(--border);
  color: var(--text);
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius);
  font-size: 13px;
  cursor: pointer;
}

.fuzzy-toggle {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 13px;
  color: var(--text-muted);
  cursor: pointer;
  padding: 0.5rem 0.75rem;
  background: var(--bg-secondary);
  border: 1px solid var(--border);
  border-radius: var(--radius);
}
.fuzzy-toggle input {
  cursor: pointer;
}
.fuzzy-toggle:hover {
  border-color: var(--accent);
}

/* Documentation links */
.doc-link {
  color: var(--accent);
  text-decoration: none;
  border-bottom: 1px dotted var(--accent);
}
.doc-link:hover {
  border-bottom-style: solid;
}
.doc-link[data-confidence='low'] {
  opacity: 0.7;
}
body.hide-doc-links .doc-link {
  color: inherit;
  border-bottom: none;
  pointer-events: none;
}

/* Entity links (internal navigation) */
.entity-link {
  color: var(--accent-secondary);
  text-decoration: none;
  border-bottom: 1px dotted var(--accent-secondary);
}
.entity-link:hover {
  border-bottom-style: solid;
  color: var(--accent);
}

/* Severity tabs */
.severity-tabs {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.severity-tab {
  padding: 0.4rem 0.8rem;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  cursor: pointer;
  font-size: 11px;
  font-weight: 500;
  color: var(--text-muted);
  transition: all 0.15s;
}

.severity-tab:hover {
  border-color: var(--accent);
}

.severity-tab.active {
  background: var(--accent);
  color: #0d1117;
  border-color: var(--accent);
}

.severity-tab .count {
  margin-left: 0.25rem;
  font-size: 10px;
  opacity: 0.8;
}

/* Expanders/Accordions */
details {
  margin-bottom: 0.5rem;
}

summary {
  cursor: pointer;
  padding: 0.75rem 1rem;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  font-size: 13px;
  list-style: none;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

summary::-webkit-details-marker { display: none; }

summary::before {
  content: '+';
  color: var(--accent);
  font-family: monospace;
  font-weight: bold;
}

details[open] > summary::before { content: '−'; }

details[open] > summary {
  border-radius: var(--radius) var(--radius) 0 0;
  border-bottom: none;
}

summary:hover {
  background: var(--bg-secondary);
}

.details-body {
  border: 1px solid var(--border);
  border-top: none;
  border-radius: 0 0 var(--radius) var(--radius);
  padding: 1rem;
  background: var(--bg-secondary);
}

/* Entity detail panel */
.entity-panel {
  background: var(--bg-secondary);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 1rem;
  margin-top: 1rem;
}

.entity-panel h3 {
  font-size: 1rem;
  margin-bottom: 0.75rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.entity-panel .meta {
  color: var(--text-dim);
  font-size: 12px;
  margin-bottom: 1rem;
}

.entity-panel .section {
  margin-bottom: 1rem;
}

.entity-panel .section-title {
  font-size: 12px;
  text-transform: uppercase;
  color: var(--text-dim);
  margin-bottom: 0.5rem;
}

/* Chain visualization */
.chain-step {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0;
  font-size: 13px;
}

.chain-arrow {
  color: var(--accent);
  font-family: monospace;
  min-width: 16px;
}

.chain-via {
  color: var(--text-dim);
  font-size: 11px;
  padding: 2px 6px;
  background: var(--card);
  border-radius: 3px;
}

/* Conflict details */
.conflict-detail {
  padding: 1rem;
  background: var(--bg-secondary);
  border-radius: var(--radius);
  margin: 0.5rem 0;
}
.detail-row {
  margin-bottom: 0.75rem;
}
.detail-row:last-child {
  margin-bottom: 0;
}
.detail-label {
  font-weight: 600;
  color: var(--text-muted);
  display: block;
  margin-bottom: 0.25rem;
  font-size: 12px;
}
.xpath-code, .value-code, .xml-code {
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 4px;
  padding: 0.5rem 0.75rem;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
  margin: 0;
}
.action-detail td {
  padding: 0 !important;
  background: transparent !important;
}

/* Stats bar */
.stats-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 1.5rem;
  padding: 1rem;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  margin-bottom: 1.5rem;
}

.stats-bar .stat {
  display: flex;
  flex-direction: column;
}

.stats-bar .stat-value {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--accent);
}

.stats-bar .stat-label {
  font-size: 12px;
  color: var(--text-muted);
}

/* Show more button */
.show-more {
  display: inline-block;
  padding: 0.5rem 1rem;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  color: var(--text-muted);
  cursor: pointer;
  font-size: 13px;
  margin-top: 0.5rem;
  transition: all 0.15s;
}

.show-more:hover {
  border-color: var(--accent);
  color: var(--text);
}

/* Load more button for lazy loading */
.load-more-btn {
  display: inline-block;
  padding: 0.5rem 1.5rem;
  background: var(--bg-secondary);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  color: var(--text-muted);
  cursor: pointer;
  font-size: 13px;
  transition: all 0.2s;
}

.load-more-btn:hover {
  background: var(--card);
  border-color: var(--accent);
  color: var(--text);
}

.load-more-container {
  border-top: 1px dashed var(--border);
  margin-top: 1rem;
}

/* Finding type groups */
.finding-type-group > summary {
  list-style: none;
  user-select: none;
}

.finding-type-group > summary::-webkit-details-marker {
  display: none;
}

.finding-type-group > summary::before {
  content: '▶';
  margin-right: 0.5rem;
  font-size: 10px;
  transition: transform 0.2s;
}

.finding-type-group[open] > summary::before {
  transform: rotate(90deg);
}

/* Findings summary bar */
.findings-summary {
  font-size: 13px;
}

/* Call Graph Explorer Modal */
.callgraph-modal {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0,0,0,0.85);
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem;
}

.callgraph-modal-content {
  background: var(--bg-secondary);
  border-radius: var(--radius);
  width: 100%;
  max-width: 1200px;
  max-height: 90vh;
  overflow: auto;
  padding: 1rem;
}

.callgraph-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.callgraph-header h3 {
  margin: 0;
  font-size: 1.1rem;
}

.callgraph-controls {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

/* Cytoscape node styles applied via JS */
.cy-node-selected {
  border-width: 3px;
  border-color: var(--accent);
}

/* Glossary styles */
.glossary-category {
  margin-bottom: 1.5rem;
}

.glossary-category h3 {
  font-size: 1rem;
  margin-bottom: 0.75rem;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--border);
}

.glossary-term {
  padding: 0.75rem;
  border-bottom: 1px solid var(--border);
}

.glossary-term:last-child {
  border-bottom: none;
}

.glossary-term .term-name {
  font-weight: 600;
  color: var(--accent);
  margin-bottom: 0.25rem;
}

.glossary-term .term-def {
  color: var(--text-muted);
  font-size: 13px;
}

.glossary-term .term-example {
  margin-top: 0.5rem;
  padding: 0.5rem;
  background: var(--card);
  border-radius: 4px;
  font-family: monospace;
  font-size: 12px;
  color: var(--text-dim);
}

/* Compact conflict view */
.compact { padding: 0.5rem; }
.compact-margin { margin-bottom: 0.5rem; }
.compact-tag { padding: 1px 5px; font-size: 10px; }

.conflict-group {
  margin-bottom: 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
}

.group-header {
  background: var(--card);
  padding: 0.35rem 0.6rem;
  font-size: 12px;
  border-bottom: 1px solid var(--border);
}

.conflict-stack {
  background: var(--bg);
}

.conflict-item {
  padding: 0.35rem 0.6rem;
  border-bottom: 1px solid var(--border);
  font-size: 12px;
}

.conflict-item:last-child {
  border-bottom: none;
}

.conflict-item-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.mod-link {
  color: var(--accent);
  font-weight: 500;
}

.source-ref {
  color: var(--text-dim);
  font-size: 11px;
  margin-left: auto;
}

.xpath-inline {
  margin: 0.25rem 0 0;
  padding: 0.25rem 0.5rem;
  background: var(--card);
  border-radius: 3px;
  font-size: 11px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

.value-inline {
  margin-top: 0.2rem;
  font-size: 11px;
  color: var(--text-muted);
}

.value-inline code {
  color: var(--accent);
}

.xml-expand {
  margin-top: 0.25rem;
}

.xml-expand summary {
  padding: 0.2rem 0.4rem;
  font-size: 11px;
  background: var(--card);
  border: 1px solid var(--border);
  cursor: pointer;
  display: inline-block;
}

.xml-expand summary::before {
  content: '+';
  margin-right: 0.25rem;
}

.xml-expand[open] summary::before {
  content: '−';
}

.xml-inline {
  margin: 0.25rem 0 0;
  padding: 0.35rem 0.5rem;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 3px;
  font-size: 11px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 150px;
}

.conflict-links {
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border);
  font-size: 12px;
}

.conflict-links a {
  margin-right: 1rem;
}

.conflict-detail-compact {
  padding: 0.25rem 0.5rem;
  font-size: 11px;
}

.source-line {
  color: var(--text-dim);
  display: block;
  margin-bottom: 0.15rem;
}

.xpath-compact, .xml-compact {
  margin: 0.15rem 0;
  padding: 0.2rem 0.4rem;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 3px;
  font-size: 10px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

.value-compact {
  font-size: 10px;
  color: var(--text-muted);
}

/* Utility classes */
.text-muted { color: var(--text-muted); }
.text-dim { color: var(--text-dim); }
.text-accent { color: var(--accent); }
.text-danger { color: var(--danger); }
.text-warning { color: var(--accent-secondary); }

.flex { display: flex; }
.flex-wrap { flex-wrap: wrap; }
.gap-1 { gap: 0.5rem; }
.gap-2 { gap: 1rem; }
.mb-1 { margin-bottom: 0.5rem; }
.mb-2 { margin-bottom: 1rem; }

/* Smooth scroll and target highlight */
html { scroll-behavior: smooth; }

:target {
  animation: highlight 2s ease;
}

@keyframes highlight {
  0%, 30% { background: rgba(63, 185, 80, 0.2); }
  100% { background: transparent; }
}

/* Responsive */
@media (max-width: 768px) {
  .site-nav {
    flex-wrap: wrap;
    height: auto;
    padding: 0.75rem;
  }

  .nav-item {
    padding: 0.4rem 0.5rem;
    font-size: 12px;
  }

  .card-grid {
    grid-template-columns: 1fr;
  }
}
";
    }

    /// <summary>
    /// Get the shared JavaScript for fuzzy search and interactivity.
    /// Embedded in each page's script section.
    /// </summary>
    public static string GetSharedJavaScript()
    {
        return @"
// Fuzzy search scoring - matches characters in order, not necessarily adjacent
function fuzzyScore(query, target) {
  if (!query) return 1;
  query = query.toLowerCase();
  target = target.toLowerCase();
  let qi = 0, score = 0, lastMatch = -1;
  for (let ti = 0; ti < target.length && qi < query.length; ti++) {
    if (target[ti] === query[qi]) {
      score += (ti === lastMatch + 1) ? 2 : 1; // Bonus for consecutive
      lastMatch = ti;
      qi++;
    }
  }
  return qi === query.length ? score : 0;
}

// Generic search helper
function searchItems(query, items, getSearchText) {
  if (!query) return items.slice(0, 20);
  return items
    .map(item => ({ item, score: fuzzyScore(query, getSearchText(item)) }))
    .filter(x => x.score > 0)
    .sort((a, b) => b.score - a.score)
    .map(x => x.item);
}

// Theme persistence
function initTheme() {
  const saved = localStorage.getItem('7d2d-theme') || 'obsidian';
  document.documentElement.dataset.theme = saved;
  const select = document.querySelector('.theme-select');
  if (select) select.value = saved;
  
  // Doc links toggle
  const hideDocs = localStorage.getItem('7d2d-hide-doc-links') === 'true';
  if (hideDocs) document.body.classList.add('hide-doc-links');
  const docToggle = document.getElementById('doc-link-toggle');
  if (docToggle) docToggle.checked = !hideDocs;
}

function toggleDocLinks(checked) {
  document.body.classList.toggle('hide-doc-links', !checked);
  localStorage.setItem('7d2d-hide-doc-links', !checked);
}

function setTheme(theme) {
  document.documentElement.dataset.theme = theme;
  localStorage.setItem('7d2d-theme', theme);
}

// Initialize on load
document.addEventListener('DOMContentLoaded', initTheme);

// Debounce helper for search
function debounce(fn, delay) {
  let timer;
  return function(...args) {
    clearTimeout(timer);
    timer = setTimeout(() => fn.apply(this, args), delay);
  };
}

// Show/hide more items
function toggleShowMore(btn, containerId, allItems, renderFn) {
  const container = document.getElementById(containerId);
  const showAll = btn.dataset.expanded !== 'true';

  if (showAll) {
    container.innerHTML = allItems.map(renderFn).join('');
    btn.textContent = 'Show Less';
    btn.dataset.expanded = 'true';
  } else {
    container.innerHTML = allItems.slice(0, 20).map(renderFn).join('');
    btn.textContent = `Show All (${allItems.length})`;
    btn.dataset.expanded = 'false';
  }
}
";
    }

    /// <summary>
    /// Generate the navigation HTML with active page highlighting.
    /// </summary>
    public static string GenerateNavigation(string activePage)
    {
        var pages = new[]
        {
            ("index.html", "Dashboard"),
            ("entities.html", "Entities"),
            ("mods.html", "Mods"),
            ("conflicts.html", "Conflicts"),
            ("dependencies.html", "Dependencies"),
            ("csharp.html", "C# Analysis"),
            ("gamecode.html", "Game Code"),
            ("glossary.html", "Glossary")
        };

        var sb = new StringBuilder();
        sb.AppendLine(@"<nav class=""site-nav"">");
        sb.AppendLine(@"  <div class=""nav-brand"">7D2D Ecosystem</div>");

        foreach (var (href, label) in pages)
        {
            var activeClass = href == activePage ? " active" : "";
            sb.AppendLine($@"  <a href=""{href}"" class=""nav-item{activeClass}"">{label}</a>");
        }

        sb.AppendLine(@"  <select class=""theme-select"" onchange=""setTheme(this.value)"">");
        sb.AppendLine(@"    <option value=""obsidian"">Obsidian</option>");
        sb.AppendLine(@"    <option value=""graphite"">Graphite</option>");
        sb.AppendLine(@"  </select>");
        sb.AppendLine(@"</nav>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate the full HTML document wrapper for a page.
    /// </summary>
    public static string WrapPage(string title, string activePage, string bodyContent, string? extraScript = null, string? extraHead = null)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"" data-theme=""obsidian"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{HttpUtility.HtmlEncode(title)} - 7D2D Ecosystem</title>
    <link rel=""stylesheet"" href=""assets/styles.css"">
    {extraHead ?? ""}
</head>
<body>
{GenerateNavigation(activePage)}
<div class=""page-container"">
{bodyContent}
</div>
<script>
{GetSharedJavaScript()}
{extraScript ?? ""}
</script>
</body>
</html>";
    }

    /// <summary>
    /// Generate an inline JSON script block for data embedding.
    /// Includes comment about future lazy-load migration.
    /// </summary>
    public static string GenerateInlineDataScript(string varName, string jsonData)
    {
        return $@"
// Inline JSON - simple, works offline
// FUTURE LAZY LOADING: Replace with fetch() call to ./data/{varName.ToLower()}.json
const {varName} = {jsonData};
";
    }

    /// <summary>
    /// Encode a string for safe HTML output.
    /// </summary>
    public static string HtmlEncode(string? text) => HttpUtility.HtmlEncode(text ?? "");

    /// <summary>
    /// Encode a string for use in a URL.
    /// </summary>
    public static string UrlEncode(string? text) => HttpUtility.UrlEncode(text ?? "");

    /// <summary>
    /// Generate a severity tag span.
    /// </summary>
    public static string SeverityTag(string level)
    {
        var cssClass = level.ToUpper() switch
        {
            "HIGH" or "CRITICAL" => "tag-high",
            "MEDIUM" => "tag-medium",
            "LOW" => "tag-low",
            _ => "tag-info"
        };
        return $@"<span class=""tag {cssClass}"">{HtmlEncode(level)}</span>";
    }

    /// <summary>
    /// Generate a health badge span.
    /// </summary>
    public static string HealthBadge(string health)
    {
        var cssClass = health.ToLower() switch
        {
            "healthy" => "tag-healthy",
            "review" => "tag-review",
            "broken" => "tag-broken",
            _ => "tag-info"
        };
        return $@"<span class=""tag {cssClass}"">{HtmlEncode(health)}</span>";
    }

    /// <summary>
    /// Generate an entity type tag.
    /// </summary>
    public static string TypeTag(string type)
    {
        return $@"<span class=""tag tag-type"">{HtmlEncode(type)}</span>";
    }

    /// <summary>
    /// Generate a link to an entity on the entities page.
    /// </summary>
    public static string EntityLink(string type, string name)
    {
        return $@"<a href=""entities.html?search={UrlEncode(name)}"">{TypeTag(type)} {HtmlEncode(name)}</a>";
    }

    /// <summary>
    /// Generate a link to a mod on the mods page.
    /// </summary>
    public static string ModLink(string modName)
    {
        return $@"<a href=""mods.html?search={UrlEncode(modName)}"">{HtmlEncode(modName)}</a>";
    }

    // Static resolver instance (lazy-loaded)
    private static DocumentationResolver? _docResolver;
    private static CodeTokenizer? _tokenizer;

    /// <summary>
    /// Get the shared documentation resolver, loading tooltips on first use.
    /// </summary>
    public static DocumentationResolver GetDocResolver()
    {
        if (_docResolver == null)
        {
            _docResolver = new DocumentationResolver();
            // Tooltips loaded separately if JSON file exists
        }
        return _docResolver;
    }

    /// <summary>
    /// Get the shared code tokenizer.
    /// </summary>
    public static CodeTokenizer GetTokenizer()
    {
        return _tokenizer ??= new CodeTokenizer();
    }

    /// <summary>
    /// Linkify C# keywords in a code snippet (first occurrence only).
    /// </summary>
    public static string LinkifyCSharp(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return HtmlEncode(code);

        var resolver = GetDocResolver();
        var tokenizer = GetTokenizer();
        var result = code;

        // Get linkable tokens sorted by position descending (so we can replace from end)
        var tokens = tokenizer.ExtractLinkable(code, isCSharp: true)
            .OrderByDescending(t => t.StartIndex)
            .ToList();

        foreach (var token in tokens)
        {
            var link = resolver.FormatAsLink(token.Value, DocumentationResolver.TokenContext.CSharp);
            if (link != token.Value) // Only if we got an actual link
            {
                result = result.Substring(0, token.StartIndex) + link + result.Substring(token.StartIndex + token.Length);
            }
        }

        return result;
    }

    /// <summary>
    /// Linkify XPath operators in an expression (first occurrence only).
    /// </summary>
    public static string LinkifyXPath(string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return HtmlEncode(xpath);

        var resolver = GetDocResolver();
        var tokenizer = GetTokenizer();
        var result = xpath;

        var tokens = tokenizer.ExtractLinkable(xpath, isCSharp: false)
            .OrderByDescending(t => t.StartIndex)
            .ToList();

        foreach (var token in tokens)
        {
            var link = resolver.FormatAsLink(token.Value, DocumentationResolver.TokenContext.XPath);
            if (link != token.Value)
            {
                result = result.Substring(0, token.StartIndex) + link + result.Substring(token.StartIndex + token.Length);
            }
        }

        return result;
    }
}
