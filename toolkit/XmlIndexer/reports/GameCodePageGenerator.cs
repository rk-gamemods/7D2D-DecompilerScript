using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using XmlIndexer.Analysis;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the game code analysis page (gamecode.html) showing potential bugs,
/// dead code, stubs, and hidden features found in the game codebase.
/// This enhanced version uses client-side JSON rendering for better interactivity.
/// </summary>
public static class GameCodePageGenerator
{
    // Codebase path for source context extraction
    private static string? _codebasePath;
    
    public static string Generate(SqliteConnection db, string? codebasePath = null)
    {
        _codebasePath = codebasePath;
        
        var body = new StringBuilder();
        var summary = GameCodeAnalyzer.GetSummary(db);

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Game Code Analysis</h1>");
        body.AppendLine(@"  <p>Potential bugs, stubs, and moddable patterns found in the decompiled game code</p>");
        body.AppendLine(@"</div>");

        // Stats bar
        body.AppendLine(@"<div class=""stats-bar"">");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--danger);"">{summary.BugCount}</span><span class=""stat-label"">Potential Bugs</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--accent-secondary);"">{summary.WarningCount}</span><span class=""stat-label"">Warnings</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--accent);"">{summary.OpportunityCount}</span><span class=""stat-label"">Opportunities</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--info);"">{summary.InfoCount}</span><span class=""stat-label"">Info</span></div>");
        body.AppendLine(@"</div>");

        if (summary.TotalCount == 0)
        {
            body.AppendLine(@"<div class=""card"">");
            body.AppendLine(@"<p class=""text-muted"">No game code analysis data available. Run <code>XmlIndexer analyze-game</code> to analyze the game codebase.</p>");
            body.AppendLine(@"</div>");
            return SharedAssets.WrapPage("Game Code Analysis", "gamecode.html", body.ToString(), "");
        }

        // Severity tabs
        body.AppendLine(@"<div class=""severity-tabs"" id=""severity-tabs"">");
        body.AppendLine($@"<button class=""severity-tab active"" data-severity="""" onclick=""filterBySeverity('')"">ALL<span class=""count"">({summary.TotalCount})</span></button>");
        if (summary.BugCount > 0)
            body.AppendLine($@"<button class=""severity-tab"" data-severity=""BUG"" onclick=""filterBySeverity('BUG')"" style=""border-color: var(--danger);"">BUGS<span class=""count"">({summary.BugCount})</span></button>");
        body.AppendLine($@"<button class=""severity-tab"" data-severity=""WARNING"" onclick=""filterBySeverity('WARNING')"" style=""border-color: var(--accent-secondary);"">WARNINGS<span class=""count"">({summary.WarningCount})</span></button>");
        body.AppendLine($@"<button class=""severity-tab"" data-severity=""OPPORTUNITY"" onclick=""filterBySeverity('OPPORTUNITY')"" style=""border-color: var(--accent);"">OPPORTUNITIES<span class=""count"">({summary.OpportunityCount})</span></button>");
        body.AppendLine($@"<button class=""severity-tab"" data-severity=""INFO"" onclick=""filterBySeverity('INFO')"" style=""border-color: var(--info);"">INFO<span class=""count"">({summary.InfoCount})</span></button>");
        body.AppendLine(@"</div>");

        // Filter bar
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""gamecode-search"" placeholder=""Search classes, methods, buffs, items..."" oninput=""filterGameCode()"">");
        body.AppendLine(@"  <label class=""fuzzy-toggle""><input type=""checkbox"" id=""fuzzy-toggle"" checked onchange=""filterGameCode()""> Fuzzy</label>");
        body.AppendLine(@"  <select class=""filter-select"" id=""type-filter"" onchange=""filterGameCode()"">");
        body.AppendLine(@"    <option value="""">All Types</option>");
        body.AppendLine(@"    <option value=""hookable_event"">Hookable Events</option>");
        body.AppendLine(@"    <option value=""hardcoded_entity"">Hardcoded Entities</option>");
        body.AppendLine(@"    <option value=""console_command"">Console Commands</option>");
        body.AppendLine(@"    <option value=""singleton_access"">Singleton Access</option>");
        body.AppendLine(@"    <option value=""secret"">Hidden Features</option>");
        body.AppendLine(@"    <option value=""stub_method"">Stub Methods</option>");
        body.AppendLine(@"    <option value=""unimplemented"">Not Implemented</option>");
        body.AppendLine(@"    <option value=""empty_catch"">Empty Catch</option>");
        body.AppendLine(@"    <option value=""todo"">TODO/FIXME</option>");
        body.AppendLine(@"    <option value=""suspicious"">Suspicious Patterns</option>");
        body.AppendLine(@"    <option value=""unreachable"">Unreachable Code</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"  <select class=""filter-select"" id=""usage-filter"" onchange=""filterGameCode()"">");
        body.AppendLine(@"    <option value="""">All Usage Levels</option>");
        body.AppendLine(@"    <option value=""active"">Active (5+ callers)</option>");
        body.AppendLine(@"    <option value=""moderate"">Moderate (2-4 callers)</option>");
        body.AppendLine(@"    <option value=""low"">Low (1 caller)</option>");
        body.AppendLine(@"    <option value=""internal"">Internal only</option>");
        body.AppendLine(@"    <option value=""unused"">Unused</option>");
        body.AppendLine(@"    <option value=""unknown"">Unknown</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"</div>");

        // Results container (client-side rendered)
        body.AppendLine(@"<div id=""gamecode-results""></div>");

        // Generate inline JSON data
        var findings = GetAllFindings(db);
        var jsonData = SerializeFindingsToJson(findings);

        // Client-side JavaScript
        var script = GenerateJavaScript(jsonData);

        return SharedAssets.WrapPage("Game Code Analysis", "gamecode.html", body.ToString(), script);
    }

    private static string GenerateJavaScript(string jsonData)
    {
        return $@"
// Inline JSON - simple, works offline
const FINDINGS_DATA = {jsonData};

let currentSeverity = '';

function filterBySeverity(severity) {{
  currentSeverity = severity;
  document.querySelectorAll('.severity-tab').forEach(tab => {{
    tab.classList.toggle('active', tab.dataset.severity === severity);
  }});
  filterGameCode();
}}

// Fuzzy search scoring - matches characters in order, not necessarily adjacent
function fuzzyScore(query, target) {{
  if (!query || !target) return target ? 0 : 1;
  query = query.toLowerCase();
  target = target.toLowerCase();
  let qi = 0, score = 0, lastMatch = -1;
  for (let ti = 0; ti < target.length && qi < query.length; ti++) {{
    if (target[ti] === query[qi]) {{
      score += (ti === lastMatch + 1) ? 2 : 1; // Bonus for consecutive
      lastMatch = ti;
      qi++;
    }}
  }}
  return qi === query.length ? score : 0;
}}

function escapeHtml(text) {{
  if (!text) return '';
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}}

// Escape text for use in HTML attributes (handles quotes and special chars)
function escapeAttr(text) {{
  if (!text) return '';
  return text
    .replace(/&/g, '&amp;')
    .replace(/'/g, '&#39;')
    .replace(/""/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}}

function filterGameCode() {{
  const query = document.getElementById('gamecode-search').value;
  const typeFilter = document.getElementById('type-filter').value;
  const usageFilter = document.getElementById('usage-filter').value;
  const useFuzzy = document.getElementById('fuzzy-toggle').checked;

  let filtered = FINDINGS_DATA.slice();

  // Severity filter - normalize to uppercase for case-insensitive comparison
  if (currentSeverity) {{
    filtered = filtered.filter(f => (f.severity || '').toUpperCase() === currentSeverity.toUpperCase());
  }}

  // Type filter
  if (typeFilter) {{
    filtered = filtered.filter(f => f.type === typeFilter);
  }}

  // Usage level filter
  if (usageFilter) {{
    filtered = filtered.filter(f => f.usageLevel === usageFilter);
  }}

  // Search filter
  if (query) {{
    if (useFuzzy) {{
      filtered = filtered
        .map(f => ({{ finding: f, score: Math.max(
          fuzzyScore(query, f.className || ''),
          fuzzyScore(query, f.methodName || ''),
          fuzzyScore(query, f.description || ''),
          fuzzyScore(query, f.entityName || '')
        )}}))
        .filter(x => x.score > 0)
        .sort((a, b) => b.score - a.score)
        .map(x => x.finding);
    }} else {{
      const q = query.toLowerCase();
      filtered = filtered.filter(f => {{
        const searchText = [f.className, f.methodName, f.description, f.entityName, f.type].filter(Boolean).join(' ').toLowerCase();
        return searchText.includes(q);
      }});
    }}
  }}

  renderFindings(filtered);
}}

// Throttled rendering state
let renderQueue = [];
let isRendering = false;
const RENDER_BATCH_SIZE = 30;  // Render 30 items at a time
const RENDER_DELAY = 16;       // ~60fps

function renderFindings(findings) {{
  const container = document.getElementById('gamecode-results');

  if (findings.length === 0) {{
    container.innerHTML = '<p class=""text-muted"">No findings match the current filters.</p>';
    return;
  }}

  // Group by type for better organization
  const byType = {{}};
  findings.forEach(f => {{
    if (!byType[f.type]) byType[f.type] = [];
    byType[f.type].push(f);
  }});

  // Type order and labels - sorted by importance, then alphabetical for unlisted types
  const typeOrder = [
    'hookable_event', 'hardcoded_entity', 'console_command', 'singleton_access',
    'secret', 'stub_method', 'unimplemented', 'empty_catch', 'todo',
    'suspicious', 'unreachable', 'item', 'buff', 'block', 'sound',
    'entity_class', 'entity_group', 'recipe'
  ];
  const typeLabels = {{
    'hookable_event': {{ label: 'Hookable Events', desc: 'Virtual methods that can be patched with Harmony or overridden' }},
    'hardcoded_entity': {{ label: 'Hardcoded Entities', desc: 'Items, buffs, and entities referenced in code' }},
    'console_command': {{ label: 'Console Commands', desc: 'Commands available via F1 console' }},
    'singleton_access': {{ label: 'Singleton Access Points', desc: 'Entry points for accessing game systems' }},
    'secret': {{ label: 'Hidden Features', desc: 'Debug flags, hidden commands, and developer-only features' }},
    'stub_method': {{ label: 'Stub Methods', desc: 'Methods that return null/default - potential hook points' }},
    'unimplemented': {{ label: 'Not Implemented', desc: 'Methods that throw NotImplementedException' }},
    'empty_catch': {{ label: 'Empty Catch Blocks', desc: 'Exception handlers that silently swallow errors' }},
    'todo': {{ label: 'TODO/FIXME Comments', desc: 'Developer notes about incomplete or problematic code' }},
    'suspicious': {{ label: 'Suspicious Patterns', desc: 'Code patterns that may indicate bugs or issues' }},
    'unreachable': {{ label: 'Unreachable Code', desc: 'Code that appears to never execute' }},
    'item': {{ label: 'Item References', desc: 'Hardcoded item names in code' }},
    'buff': {{ label: 'Buff References', desc: 'Hardcoded buff names in code' }},
    'block': {{ label: 'Block References', desc: 'Hardcoded block names in code' }},
    'sound': {{ label: 'Sound References', desc: 'Hardcoded sound names in code' }},
    'entity_class': {{ label: 'Entity Class References', desc: 'Hardcoded entity class names' }},
    'entity_group': {{ label: 'Entity Group References', desc: 'Hardcoded entity group names' }},
    'recipe': {{ label: 'Recipe References', desc: 'Hardcoded recipe names in code' }}
  }};

  // Get all types from data (handles any new types not in typeOrder)
  const allTypes = Object.keys(byType);
  const orderedTypes = typeOrder.filter(t => allTypes.includes(t));
  const remainingTypes = allTypes.filter(t => !typeOrder.includes(t)).sort();
  const renderTypes = [...orderedTypes, ...remainingTypes];

  // Build structure with lazy-load placeholders for large groups
  let html = '';
  html += '<div class=""findings-summary"" style=""margin-bottom:1rem;padding:0.75rem;background:var(--bg-secondary);border-radius:4px;"">';
  html += '<span style=""font-size:13px;"">Showing <strong>' + findings.length + '</strong> findings across <strong>' + renderTypes.length + '</strong> categories</span>';
  html += '</div>';

  renderTypes.forEach(type => {{
    if (!byType[type] || byType[type].length === 0) return;

    const info = typeLabels[type] || {{ label: type, desc: '' }};
    const items = byType[type];
    const shouldStartOpen = items.length <= 20;
    const typeId = 'type-' + type.replace(/[^a-zA-Z0-9]/g, '_');

    html += '<details id=""' + typeId + '"" class=""finding-type-group""' + (shouldStartOpen ? ' open' : '') + '>';
    html += '<summary style=""padding:0.75rem;cursor:pointer;background:var(--bg-secondary);border-radius:4px;margin-bottom:0.5rem;display:flex;align-items:center;"">';
    html += '<span style=""flex:1;font-weight:600;"">' + escapeHtml(info.label) + '</span>';
    html += '<span class=""text-dim"" style=""font-size:12px;margin-right:0.5rem;"">' + items.length + ' findings</span>';
    html += '</summary>';
    html += '<div class=""details-body finding-container"" data-type=""' + escapeAttr(type) + '"" data-total=""' + items.length + '"" data-rendered=""0"">';
    html += '<p class=""text-muted"" style=""margin-bottom:1rem;font-size:12px;"">' + escapeHtml(info.desc) + '</p>';
    
    // Render first batch immediately, rest lazily
    const initialCount = Math.min(items.length, RENDER_BATCH_SIZE);
    for (let i = 0; i < initialCount; i++) {{
      html += renderFinding(items[i], i, type);
    }}
    
    // Add load-more button if there are more items
    if (items.length > RENDER_BATCH_SIZE) {{
      html += '<div class=""load-more-container"" data-remaining=""' + (items.length - RENDER_BATCH_SIZE) + '"" style=""text-align:center;padding:1rem;"">';
      html += '<button class=""load-more-btn"" onclick=""loadMoreFindings(this, \'' + escapeAttr(type) + '\')"">';
      html += 'Load ' + Math.min(RENDER_BATCH_SIZE, items.length - RENDER_BATCH_SIZE) + ' more';
      html += ' <span class=""text-dim"">(' + (items.length - RENDER_BATCH_SIZE) + ' remaining)</span></button>';
      html += '</div>';
    }}

    html += '</div></details>';
  }});

  container.innerHTML = html;
  
  // Store findings by type for lazy loading
  window._findingsByType = byType;
}}

// Lazy load more findings when button is clicked
function loadMoreFindings(btn, type) {{
  const container = btn.closest('.finding-container');
  const byType = window._findingsByType;
  if (!byType || !byType[type]) return;
  
  const items = byType[type];
  const rendered = parseInt(container.dataset.rendered || '0', 10) + RENDER_BATCH_SIZE;
  const nextBatch = items.slice(rendered, rendered + RENDER_BATCH_SIZE);
  
  if (nextBatch.length === 0) return;
  
  // Render the next batch
  let html = '';
  nextBatch.forEach((f, i) => {{
    html += renderFinding(f, rendered + i, type);
  }});
  
  // Insert before the load-more button
  const loadMoreContainer = container.querySelector('.load-more-container');
  const temp = document.createElement('div');
  temp.innerHTML = html;
  while (temp.firstChild) {{
    container.insertBefore(temp.firstChild, loadMoreContainer);
  }}
  
  // Update counter
  container.dataset.rendered = rendered + nextBatch.length;
  
  // Update or remove button
  const remaining = items.length - (rendered + nextBatch.length);
  if (remaining <= 0) {{
    loadMoreContainer.remove();
  }} else {{
    loadMoreContainer.dataset.remaining = remaining;
    btn.innerHTML = 'Load ' + Math.min(RENDER_BATCH_SIZE, remaining) + ' more <span class=""text-dim"">(' + remaining + ' remaining)</span>';
  }}
}}

function renderFinding(f, idx, typeContext) {{
  // === CONTEXT BREADCRUMB (depth trace indicator) ===
  const breadcrumb = [typeContext || f.type, f.className, f.methodName].filter(Boolean);
  
  // === SEVERITY STYLING (prominent, used for left border and badge) ===
  const severityColors = {{
    'BUG':         {{ bg: 'rgba(231, 76, 60, 0.15)', border: '#e74c3c', text: '#e74c3c' }},
    'WARNING':     {{ bg: 'rgba(243, 156, 18, 0.15)', border: '#f39c12', text: '#f39c12' }},
    'OPPORTUNITY': {{ bg: 'rgba(46, 204, 113, 0.15)', border: '#2ecc71', text: '#27ae60' }},
    'INFO':        {{ bg: 'rgba(52, 152, 219, 0.15)', border: '#3498db', text: '#3498db' }}
  }};
  
  const sc = severityColors[f.severity] || severityColors['INFO'];
  
  // === USAGE LEVEL STYLING (MUTED gray colors - NO bright colors, NO RED for unused) ===
  const usageLevelStyle = {{
    'active':   {{ label: 'Active',   color: '#7f8c8d' }},
    'moderate': {{ label: 'Moderate', color: '#7f8c8d' }},
    'low':      {{ label: 'Low',      color: '#95a5a6' }},
    'internal': {{ label: 'Internal', color: '#95a5a6' }},
    'unused':   {{ label: 'Unused',   color: '#bdc3c7' }},
    'unknown':  {{ label: 'Unknown',  color: '#7f8c8d' }}
  }};
  
  const ul = usageLevelStyle[f.usageLevel] || usageLevelStyle['unknown'];

  const fileName = f.filePath ? f.filePath.split(/[/\\\\]/).pop() : '';

  // Card with severity-colored left border
  let html = '<div class=""finding-card"" style=""border-left: 3px solid ' + sc.border + '; margin-bottom: 1rem; padding: 1rem; background: var(--bg-secondary); border-radius: 4px;"">';

  // === HEADER ROW ===
  html += '<div class=""finding-header"" style=""display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap;"">';
  
  // Severity badge (prominent)
  html += '<span class=""tag"" style=""background:' + sc.bg + ';color:' + sc.text + ';border:1px solid ' + sc.border + ';padding:2px 8px;border-radius:3px;font-size:11px;font-weight:bold;"">' + (f.severity || 'INFO') + '</span>';
  
  // Usage level badge (MUTED - just gray text)
  if (f.usageLevel && f.usageLevel !== 'unknown') {{
    html += '<span style=""color:' + ul.color + ';font-size:10px;padding:2px 6px;"">' + ul.label + '</span>';
  }}
  
  // Class.Method name (clickable - filters to show all findings in this class)
  // Uses data attributes to avoid JavaScript escaping complexity
  html += '<span class=""finding-title"" style=""font-weight:600;font-size:14px;"">';
  html += '<a href=""#"" class=""js-click"" data-action=""filterByClass"" data-arg0=""' + escapeAttr(f.className) + '"" style=""color:inherit;text-decoration:none;border-bottom:1px dashed currentColor;"" title=""Show all findings in this class"">' + escapeHtml(f.className) + '</a>';
  html += '.<span>' + escapeHtml(f.methodName || '?') + '</span>';
  html += '</span>';
  html += '</div>';

  // File location
  html += '<div class=""finding-location"" style=""font-size:12px;color:#7f8c8d;margin:0.25rem 0;"">' + escapeHtml(fileName) + ':' + (f.lineNumber || '?') + '</div>';

  // === XML STATUS & DESCRIPTION ===
  html += '<div style=""margin: 0.5rem 0;"">';
  if (f.xmlStatus) {{
    const statusText = f.xmlStatus === 'in_xml' || f.xmlStatus === 'found' ? 'In XML' : 'Code Only';
    const statusBg = f.xmlStatus === 'in_xml' || f.xmlStatus === 'found' ? 'rgba(46, 204, 113, 0.2)' : 'rgba(243, 156, 18, 0.2)';
    const statusColor = f.xmlStatus === 'in_xml' || f.xmlStatus === 'found' ? '#27ae60' : '#f39c12';
    html += '<span class=""tag"" style=""background:' + statusBg + ';color:' + statusColor + ';padding:2px 6px;border-radius:3px;font-size:10px;margin-right:0.5rem;"">' + statusText + '</span>';
  }}
  html += '<span class=""finding-description"">' + escapeHtml(f.description || '') + '</span>';
  html += '</div>';

  // Entity link for cross-reference
  if (f.entityName && (f.xmlStatus === 'found' || f.xmlStatus === 'in_xml')) {{
    html += '<div style=""margin-bottom: 0.5rem; font-size: 12px;"">';
    html += '<a href=""entities.html?search=' + encodeURIComponent(f.entityName) + '"" class=""mod-link"">View ' + escapeHtml(f.entityName) + ' in Entities</a>';
    html += '</div>';
  }}

  // === REACHABILITY SECTION (collapsible, subtle background) ===
  if (f.reachability) {{
    const r = f.reachability;
    const usageLevel = r.usage_level || r.usageLevel || 'unknown';
    const confidence = r.confidence || 0;
    const reasoning = r.reasoning || '';
    const callerCount = r.caller_count || r.callerCount || 0;
    const externalCallerCount = r.external_caller_count || r.externalCallerCount || 0;
    const callChains = r.call_chains || r.callChains || [];
    
    html += '<details class=""reachability-section"" style=""background:rgba(149,165,166,0.05);border-radius:4px;margin:0.75rem 0;border:1px solid rgba(149,165,166,0.1);"">';
    html += '<summary style=""padding:0.75rem;cursor:pointer;font-size:13px;"">';
    html += '<strong>Reachability Analysis</strong> ';
    html += '<span style=""color:#95a5a6;font-size:12px;"">(' + Math.round(confidence * 100) + '% confidence, ' + callerCount + ' callers)</span>';
    html += '</summary>';
    html += '<div style=""padding:0 0.75rem 0.75rem 0.75rem;"">';
    
    // Reasoning text
    if (reasoning) {{
      html += '<p style=""margin:0.25rem 0;color:#7f8c8d;font-size:12px;"">' + escapeHtml(reasoning) + '</p>';
    }}
    
    // Call chains visualization with proper tree connectors
    if (callChains.length > 0) {{
      html += '<div class=""call-chains"" style=""margin-top:0.75rem;"">';
      html += '<strong style=""font-size:12px;color:#7f8c8d;"">Call Chains:</strong>';
      
      callChains.slice(0, 3).forEach(function(chain) {{
        html += '<div class=""call-chain"" style=""font-family:monospace;font-size:11px;padding:0.5rem;margin-top:0.25rem;background:var(--bg-primary);border-radius:3px;"">';
        
        chain.forEach(function(node, j) {{
          const entryType = node.entry_point_type || node.entryPointType;
          const methodClass = node['class'] || node.className || '';
          const methodName = node.method || node.methodName || '';
          const isTarget = j === chain.length - 1;
          const indent = '&nbsp;&nbsp;'.repeat(j);
          const connector = j === 0 ? '' : '└─ ';
          
          // Entry point badge for first node (prominent colors)
          let badge = '';
          if (entryType) {{
            const badgeColors = {{
              'console': {{ bg: '#9b59b6', label: 'CONSOLE CMD' }},
              'unity':   {{ bg: '#27ae60', label: 'UNITY' }},
              'event':   {{ bg: '#3498db', label: 'EVENT' }},
              'main':    {{ bg: '#f1c40f', label: 'MAIN' }}
            }};
            const bc = badgeColors[entryType] || {{ bg: '#7f8c8d', label: entryType.toUpperCase() }};
            const textColor = entryType === 'main' ? '#333' : '#fff';
            badge = '<span style=""background:' + bc.bg + ';color:' + textColor + ';padding:1px 4px;border-radius:2px;font-size:9px;font-weight:bold;margin-right:4px;"">' + bc.label + '</span>';
          }}
          
          // Make non-target nodes clickable for navigation using data attributes
          let methodLink = escapeHtml(methodClass + '.' + methodName);
          if (!isTarget && methodClass) {{
            methodLink = '<a href=""#"" class=""js-click"" data-action=""filterByClassMethod"" data-arg0=""' + escapeAttr(methodClass) + '"" data-arg1=""' + escapeAttr(methodName) + '"" style=""color:inherit;text-decoration:underline;text-decoration-style:dotted;"">' + escapeHtml(methodClass + '.' + methodName) + '</a>';
          }}
          
          html += '<div style=""' + (isTarget ? 'font-weight:bold;color:var(--accent);' : '') + '"">';
          html += indent + connector + badge + methodLink;
          if (isTarget) html += ' <span style=""color:var(--accent);"">◀ THIS CODE</span>';
          html += '</div>';
        }});
        html += '</div>';
      }});
      if (callChains.length > 3) {{
        html += '<div class=""text-dim"" style=""font-size:10px;margin-top:0.25rem;"">...and ' + (callChains.length - 3) + ' more execution paths</div>';
      }}
      html += '</div>';
    }}
    
    html += '</div></details>';
  }}
  
  // === METHOD CALLERS SECTION (collapsible, subtle) ===
  if (f.deadCodeAnalysis) {{
    const dca = f.deadCodeAnalysis;
    const callers = dca.method_callers || dca.methodCallers || [];
    
    if (callers.length > 0) {{
      const methodLabel = f.methodName ? ' for ' + escapeHtml(f.methodName) + '()' : '';
      html += '<details class=""callers-section"" style=""margin:0.75rem 0;"">';
      html += '<summary style=""padding:0.5rem;cursor:pointer;font-size:13px;background:rgba(149,165,166,0.05);border-radius:4px;border:1px solid rgba(149,165,166,0.1);"">';
      html += '<strong>Method Callers' + methodLabel + '</strong> ';
      html += '<span style=""color:#95a5a6;font-size:12px;"">(' + callers.length + ' locations)</span>';
      html += '</summary>';
      html += '<div style=""padding:0.5rem;"">';
      
      callers.slice(0, 5).forEach(function(c) {{
        const callerClass = c.caller_class || c.callerClass || '';
        const callerMethod = c.caller_method || c.callerMethod || '';
        const filePath = c.file_path || c.filePath || '';
        const lineNum = c.line_number || c.lineNumber || 0;
        const snippet = c.code_snippet || c.codeSnippet || '';
        
        html += '<div style=""border-left:2px solid #95a5a6;padding-left:0.5rem;margin:0.5rem 0;"">';
        // Make caller clickable for navigation using data attributes
        html += '<a href=""#"" class=""js-click"" data-action=""filterByClassMethod"" data-arg0=""' + escapeAttr(callerClass) + '"" data-arg1=""' + escapeAttr(callerMethod) + '"" style=""color:var(--accent);text-decoration:none;"" title=""Find findings in ' + escapeHtml(callerClass) + '"">';
        html += '<code style=""font-size:11px;"">' + escapeHtml(callerClass + '.' + callerMethod) + '()</code></a>';
        if (filePath || lineNum) {{
          html += '<span style=""color:#95a5a6;font-size:10px;margin-left:0.5rem;"">' + escapeHtml(filePath) + (lineNum ? ':' + lineNum : '') + '</span>';
        }}
        if (snippet) {{
          html += '<pre style=""font-size:10px;margin:0.25rem 0 0 0;padding:0.4rem;background:var(--bg-primary);border-radius:3px;overflow-x:auto;white-space:pre-wrap;color:#7f8c8d;"">' + escapeHtml(snippet) + '</pre>';
        }}
        html += '</div>';
      }});
      
      if (callers.length > 5) {{
        html += '<div style=""color:#95a5a6;font-size:10px;padding-top:0.25rem;"">...and ' + (callers.length - 5) + ' more callers</div>';
      }}
      
      html += '</div></details>';
    }}
  }}

  // === SEMANTIC CONTEXT SECTION (collapsible, subtle blue/purple gradient) ===
  if (f.semanticContext) {{
    const sc = f.semanticContext;
    const category = sc.category || 'Unknown';
    const reasoning = sc.reasoning || '';
    const moddability = sc.moddability_level || sc.moddabilityLevel || '';
    const concerns = sc.related_concerns || sc.relatedConcerns || [];
    
    html += '<details class=""semantic-context"" style=""margin:0.75rem 0;"">';
    html += '<summary style=""padding:0.5rem;cursor:pointer;font-size:13px;background:linear-gradient(135deg,rgba(52,152,219,0.08) 0%,rgba(155,89,182,0.08) 100%);border-radius:4px;border:1px solid rgba(52,152,219,0.15);"">';
    html += '<strong>Semantic Analysis</strong> ';
    html += '<span style=""color:#3498db;font-size:12px;"">' + escapeHtml(category) + '</span>';
    html += '</summary>';
    html += '<div style=""padding:0.5rem;"">';
    
    if (moddability) {{
      const moddColors = {{
        'high':   {{ color: '#27ae60', label: 'High - Easy to mod' }},
        'medium': {{ color: '#f39c12', label: 'Medium - Possible' }},
        'low':    {{ color: '#e74c3c', label: 'Low - Difficult' }}
      }};
      const mc = moddColors[moddability.toLowerCase()] || {{ color: '#95a5a6', label: moddability }};
      html += '<div style=""margin-bottom:0.5rem;font-size:12px;""><strong>Moddability:</strong> <span style=""color:' + mc.color + ';"">' + mc.label + '</span></div>';
    }}
    
    if (reasoning) {{
      html += '<p style=""font-size:12px;color:#7f8c8d;margin:0.25rem 0;"">' + escapeHtml(reasoning) + '</p>';
    }}
    
    if (concerns.length > 0) {{
      html += '<div style=""margin-top:0.5rem;display:flex;flex-wrap:wrap;gap:0.25rem;"">';
      concerns.forEach(function(c) {{
        html += '<span style=""background:rgba(52,152,219,0.1);color:#3498db;font-size:10px;padding:2px 6px;border-radius:3px;"">' + escapeHtml(c) + '</span>';
      }});
      html += '</div>';
    }}
    
    html += '</div></details>';
  }}

  // === FUZZY MATCHES SECTION (collapsible, subtle) ===
  if (f.fuzzyMatches && Array.isArray(f.fuzzyMatches) && f.fuzzyMatches.length > 0) {{
    html += '<details class=""fuzzy-matches"" style=""margin:0.75rem 0;"">';
    html += '<summary style=""padding:0.5rem;cursor:pointer;font-size:13px;background:rgba(155,89,182,0.05);border-radius:4px;border:1px solid rgba(155,89,182,0.1);"">';
    html += '<strong>Similar Entities</strong> ';
    html += '<span style=""color:#95a5a6;font-size:12px;"">(' + f.fuzzyMatches.length + ' potential matches)</span>';
    html += '</summary>';
    html += '<div style=""padding:0.5rem;"">';
    f.fuzzyMatches.forEach(function(m) {{
      const scorePercent = Math.round((m.score || 0) * 100);
      const scoreColor = scorePercent >= 80 ? '#27ae60' : scorePercent >= 60 ? '#f39c12' : '#95a5a6';
      html += '<div style=""display:flex;align-items:center;gap:0.5rem;font-size:12px;padding:0.25rem 0;"">';
      html += '<span style=""color:' + scoreColor + ';font-weight:bold;font-size:11px;min-width:35px;"">' + scorePercent + '%</span>';
      html += '<a href=""entities.html?search=' + encodeURIComponent(m.name || '') + '"" style=""color:var(--accent);text-decoration:none;"">' + escapeHtml(m.name || '') + '</a>';
      if (m.reason) {{
        html += '<span style=""color:#95a5a6;font-size:10px;"">(' + escapeHtml(m.reason) + ')</span>';
      }}
      html += '</div>';
    }});
    html += '</div></details>';
  }}

  // === CALLEES SECTION (methods this method calls - from cg_calls) ===
  if (f.callees && f.callees.callees && f.callees.callees.length > 0) {{
    const calleeList = f.callees.callees;
    html += '<details class=""callees-section"" style=""margin:0.75rem 0;"">';
    html += '<summary style=""padding:0.5rem;cursor:pointer;font-size:13px;background:rgba(46,204,113,0.05);border-radius:4px;border:1px solid rgba(46,204,113,0.1);"">';
    html += '<strong>Methods Called</strong> ';
    html += '<span style=""color:#95a5a6;font-size:12px;"">(' + calleeList.length + ' calls)</span>';
    html += '</summary>';
    html += '<div style=""padding:0.5rem;"">';
    
    calleeList.slice(0, 10).forEach(function(c) {{
      html += '<div style=""font-size:12px;padding:0.25rem 0;display:flex;align-items:center;gap:0.5rem;"">';
      html += '<span style=""color:#27ae60;font-size:10px;"">→</span>';
      html += '<a href=""#"" class=""js-click"" data-action=""filterByClassMethod"" data-arg0=""' + escapeAttr(c.callee_class) + '"" data-arg1=""' + escapeAttr(c.callee_method) + '"" style=""color:var(--accent);text-decoration:none;"">';
      html += escapeHtml(c.callee_class + '.' + c.callee_method) + '()</a>';
      if (c.line_number) {{
        html += '<span style=""color:#95a5a6;font-size:10px;"">line ' + c.line_number + '</span>';
      }}
      html += '</div>';
    }});
    
    if (calleeList.length > 10) {{
      html += '<div style=""color:#95a5a6;font-size:10px;padding-top:0.25rem;"">...and ' + (calleeList.length - 10) + ' more calls</div>';
    }}
    
    html += '</div></details>';
  }}

  // === TYPE HIERARCHY SECTION (base classes and interfaces - from cg_types/cg_implements) ===
  if (f.typeHierarchy) {{
    const th = f.typeHierarchy;
    const hasBaseClasses = th.base_classes && th.base_classes.length > 0;
    const hasInterfaces = th.interfaces && th.interfaces.length > 0;
    
    if (hasBaseClasses || hasInterfaces) {{
      html += '<details class=""type-hierarchy-section"" style=""margin:0.75rem 0;"">';
      html += '<summary style=""padding:0.5rem;cursor:pointer;font-size:13px;background:rgba(155,89,182,0.05);border-radius:4px;border:1px solid rgba(155,89,182,0.1);"">';
      html += '<strong>Type Hierarchy</strong> ';
      const totalCount = (th.base_classes?.length || 0) + (th.interfaces?.length || 0);
      html += '<span style=""color:#95a5a6;font-size:12px;"">(' + totalCount + ' types)</span>';
      html += '</summary>';
      html += '<div style=""padding:0.5rem;"">';
      
      // Inheritance chain
      if (hasBaseClasses) {{
        html += '<div style=""margin-bottom:0.5rem;"">';
        html += '<span style=""font-weight:bold;font-size:11px;color:#9b59b6;"">Inherits from:</span>';
        html += '<div style=""display:flex;flex-wrap:wrap;gap:0.25rem;margin-top:0.25rem;"">';
        th.base_classes.forEach(function(bc, i) {{
          html += '<span style=""background:rgba(155,89,182,0.1);color:#9b59b6;padding:2px 6px;border-radius:3px;font-size:11px;"">';
          html += (i > 0 ? '→ ' : '') + escapeHtml(bc);
          html += '</span>';
        }});
        html += '</div></div>';
      }}
      
      // Interfaces
      if (hasInterfaces) {{
        html += '<div>';
        html += '<span style=""font-weight:bold;font-size:11px;color:#3498db;"">Implements:</span>';
        html += '<div style=""display:flex;flex-wrap:wrap;gap:0.25rem;margin-top:0.25rem;"">';
        th.interfaces.forEach(function(iface) {{
          html += '<span style=""background:rgba(52,152,219,0.1);color:#3498db;padding:2px 6px;border-radius:3px;font-size:11px;"">';
          html += escapeHtml(iface);
          html += '</span>';
        }});
        html += '</div></div>';
      }}
      
      html += '</div></details>';
    }}
  }}

  // === FULL METHOD SOURCE SECTION (uses sourceContext when available) ===
  const methodBody = (f.sourceContext && f.sourceContext.method_body) ? f.sourceContext.method_body : f.codeSnippet;
  const lineCount = (f.sourceContext && f.sourceContext.method_end_line && f.sourceContext.method_start_line)
    ? (f.sourceContext.method_end_line - f.sourceContext.method_start_line + 1)
    : (methodBody ? methodBody.split('\\n').length : 0);

  if (methodBody) {{
    html += '<details class=""code-section"" open style=""margin:0.75rem 0;"">';
    html += '<summary style=""padding:0.5rem;cursor:pointer;font-size:13px;background:rgba(52,73,94,0.1);border-radius:4px;border:1px solid rgba(52,73,94,0.2);"">';
    html += '<strong>Full Method Source</strong>';
    html += ' <span style=""color:#95a5a6;font-size:11px;"">(' + lineCount + ' lines)</span>';
    if (f.methodName) {{
      html += ' <code style=""font-size:11px;color:#3498db;"">' + escapeHtml(f.methodName) + '()</code>';
    }}
    html += '</summary>';
    
    // Container with max-height for scrolling
    html += '<div style=""max-height:500px;overflow:auto;border:1px solid var(--border);border-radius:4px;margin:0.5rem 0;"">';
    
    // Highlight the entity name in the code
    let codeHtml = escapeHtml(methodBody);
    if (f.entityName) {{
      const entityEscaped = f.entityName.replace(/[.*+?^${{}}()|[\\]\\\\]/g, '\\\\$&');
      const regex = new RegExp('(' + entityEscaped + ')', 'gi');
      codeHtml = codeHtml.replace(regex, '<mark style=""background:rgba(241,196,15,0.5);padding:0 2px;border-radius:2px;color:#000;"">$1</mark>');
    }}
    html += '<pre style=""font-size:11px;line-height:1.4;padding:0.75rem;background:var(--bg-primary);margin:0;white-space:pre;overflow-x:auto;"">' + codeHtml + '</pre>';
    html += '</div>';
    html += '</details>';
  }}

  // === POTENTIAL FIX / SUGGESTION ===
  if (f.potentialFix) {{
    html += '<div style=""margin:0.75rem 0;padding:0.75rem;background:rgba(46,204,113,0.08);border-left:3px solid #27ae60;border-radius:0 4px 4px 0;"">';
    html += '<strong style=""color:#27ae60;font-size:12px;"">💡 Suggestion:</strong> ';
    html += '<span style=""font-size:12px;color:#7f8c8d;"">' + escapeHtml(f.potentialFix) + '</span>';
    html += '</div>';
  }}

  // === WHY THIS MATTERS (collapsed reasoning) ===
  if (f.reasoning && f.reasoning !== f.description) {{
    html += '<details style=""margin:0.5rem 0;"">';
    html += '<summary style=""cursor:pointer;font-size:11px;color:#95a5a6;"">Why this matters</summary>';
    html += '<div style=""padding:0.5rem;font-size:12px;color:#7f8c8d;background:var(--bg-secondary);border-radius:4px;margin-top:0.25rem;"">' + escapeHtml(f.reasoning) + '</div>';
    html += '</details>';
  }}

  html += '</div></div>';
  return html;
}}

// === NAVIGATION HELPER FUNCTIONS ===
function filterByClass(className) {{
  // Clear other filters and search for this class
  document.getElementById('gamecode-search').value = className;
  document.getElementById('type-filter').value = '';
  document.getElementById('usage-filter').value = '';
  currentSeverity = '';
  // Update URL hash for bookmarking
  history.pushState(null, '', '#class=' + encodeURIComponent(className));
  filterGameCode();
  // Scroll to results
  document.getElementById('gamecode-results').scrollIntoView({{ behavior: 'smooth' }});
}}

function filterByClassMethod(className, methodName) {{
  // Search for specific class.method
  document.getElementById('gamecode-search').value = className + '.' + methodName;
  document.getElementById('type-filter').value = '';
  document.getElementById('usage-filter').value = '';
  currentSeverity = '';
  // Update URL hash for bookmarking  
  history.pushState(null, '', '#class=' + encodeURIComponent(className) + '&method=' + encodeURIComponent(methodName));
  filterGameCode();
  document.getElementById('gamecode-results').scrollIntoView({{ behavior: 'smooth' }});
}}

// Initialize on page load with deep linking support
document.addEventListener('DOMContentLoaded', function() {{
  // Check for hash-based deep linking
  const hash = window.location.hash.substring(1);
  if (hash) {{
    const params = new URLSearchParams(hash);
    const className = params.get('class');
    const methodName = params.get('method');
    if (className) {{
      document.getElementById('gamecode-search').value = methodName ? (className + '.' + methodName) : className;
    }}
  }}
  filterGameCode();
}});

// Event delegation for js-click elements (handles dynamically created links)
// This eliminates the need for complex JavaScript string escaping in onclick handlers
document.addEventListener('click', function(e) {{
  const el = e.target.closest('.js-click');
  if (!el) return;
  
  e.preventDefault();
  const action = el.dataset.action;
  
  // Collect arguments from data-arg0, data-arg1, etc.
  const args = [];
  let i = 0;
  while (el.dataset['arg' + i] !== undefined) {{
    args.push(el.dataset['arg' + i]);
    i++;
  }}
  
  // Call the function if it exists
  if (typeof window[action] === 'function') {{
    window[action].apply(null, args);
  }} else {{
    console.error('Unknown action:', action, 'with args:', args);
  }}
}});
";
    }

    private static List<FindingData> GetAllFindings(SqliteConnection db)
    {
        var findings = new List<FindingData>();

        // Create enricher for adding context to findings
        EntityEnricher? enricher = null;
        if (!string.IsNullOrEmpty(_codebasePath))
        {
            enricher = new EntityEnricher(db, _codebasePath);
        }

        using var cmd = db.CreateCommand();
        // Use only the columns that exist in the current schema
        cmd.CommandText = @"
            SELECT analysis_type, class_name, method_name, severity, confidence,
                   description, reasoning, code_snippet, file_path, line_number, potential_fix,
                   related_entities
            FROM game_code_analysis
            ORDER BY
                CASE severity WHEN 'BUG' THEN 1 WHEN 'WARNING' THEN 2 WHEN 'OPPORTUNITY' THEN 3 ELSE 4 END,
                CASE confidence WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                class_name, line_number";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var analysisType = reader.GetString(0);
            var className = reader.GetString(1);
            var methodName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var severity = reader.GetString(3);
            var confidence = reader.GetString(4);
            var description = reader.IsDBNull(5) ? null : reader.GetString(5);
            var reasoning = reader.IsDBNull(6) ? null : reader.GetString(6);
            var codeSnippet = reader.IsDBNull(7) ? null : reader.GetString(7);
            var filePath = reader.IsDBNull(8) ? null : reader.GetString(8);
            var lineNumber = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9);
            var potentialFix = reader.IsDBNull(10) ? null : reader.GetString(10);
            var relatedEntities = reader.IsDBNull(11) ? null : reader.GetString(11);

            // Enrich the finding with additional context
            string? entityName = null;
            string? xmlStatus = null;
            string? xmlFile = null;
            string? fuzzyMatches = null;
            string? deadCodeAnalysis = null;
            string? semanticContext = null;
            string? reachability = null;
            string? sourceContext = null;
            string? usageLevel = null;
            string? callees = null;
            string? typeHierarchy = null;

            if (enricher != null)
            {
                var enrichment = enricher.Enrich(
                    analysisType,
                    className,
                    methodName,
                    filePath,
                    lineNumber,
                    codeSnippet,
                    relatedEntities);

                entityName = enrichment.EntityName;
                xmlStatus = enrichment.XmlStatus;
                xmlFile = enrichment.XmlFile;
                fuzzyMatches = enrichment.FuzzyMatches;
                deadCodeAnalysis = enrichment.DeadCodeAnalysis;
                semanticContext = enrichment.SemanticContext;
                reachability = enrichment.Reachability;
                sourceContext = enrichment.SourceContext;
                usageLevel = enrichment.UsageLevel;
                callees = enrichment.Callees;
                typeHierarchy = enrichment.TypeHierarchy;
            }

            findings.Add(new FindingData(
                Type: analysisType,
                ClassName: className,
                MethodName: methodName,
                Severity: severity,
                Confidence: confidence,
                Description: description,
                Reasoning: reasoning,
                CodeSnippet: codeSnippet,
                FilePath: filePath,
                LineNumber: lineNumber,
                PotentialFix: potentialFix,
                EntityName: entityName,
                XmlStatus: xmlStatus,
                XmlFile: xmlFile,
                FuzzyMatches: fuzzyMatches,
                DeadCodeAnalysis: deadCodeAnalysis,
                SemanticContext: semanticContext,
                Reachability: reachability,
                SourceContext: sourceContext,
                UsageLevel: usageLevel,
                Callees: callees,
                TypeHierarchy: typeHierarchy
            ));
        }

        return findings;
    }

    private static string SerializeFindingsToJson(List<FindingData> findings)
    {
        var jsonObjects = new List<string>();

        foreach (var f in findings)
        {
            var obj = new StringBuilder();
            obj.Append("{");
            obj.Append($"\"type\":{JsonString(f.Type)},");
            obj.Append($"\"className\":{JsonString(f.ClassName)},");
            obj.Append($"\"methodName\":{JsonString(f.MethodName)},");
            obj.Append($"\"severity\":{JsonString(f.Severity)},");
            obj.Append($"\"confidence\":{JsonString(f.Confidence)},");
            obj.Append($"\"description\":{JsonString(f.Description)},");
            obj.Append($"\"reasoning\":{JsonString(f.Reasoning)},");
            obj.Append($"\"codeSnippet\":{JsonString(f.CodeSnippet)},");
            obj.Append($"\"filePath\":{JsonString(f.FilePath)},");
            obj.Append($"\"lineNumber\":{(f.LineNumber.HasValue ? f.LineNumber.Value.ToString() : "null")},");
            obj.Append($"\"potentialFix\":{JsonString(f.PotentialFix)},");
            obj.Append($"\"entityName\":{JsonString(f.EntityName)},");
            obj.Append($"\"xmlStatus\":{JsonString(f.XmlStatus)},");
            obj.Append($"\"xmlFile\":{JsonString(f.XmlFile)},");
            obj.Append($"\"fuzzyMatches\":{(f.FuzzyMatches != null ? f.FuzzyMatches : "null")},");
            obj.Append($"\"deadCodeAnalysis\":{(f.DeadCodeAnalysis != null ? f.DeadCodeAnalysis : "null")},");
            obj.Append($"\"semanticContext\":{(f.SemanticContext != null ? f.SemanticContext : "null")},");
            obj.Append($"\"reachability\":{(f.Reachability != null ? f.Reachability : "null")},");
            obj.Append($"\"sourceContext\":{(f.SourceContext != null ? f.SourceContext : "null")},");
            obj.Append($"\"usageLevel\":{JsonString(f.UsageLevel)},");
            obj.Append($"\"callees\":{(f.Callees != null ? f.Callees : "null")},");
            obj.Append($"\"typeHierarchy\":{(f.TypeHierarchy != null ? f.TypeHierarchy : "null")}");
            obj.Append("}");
            jsonObjects.Add(obj.ToString());
        }

        return "[" + string.Join(",", jsonObjects) + "]";
    }

    private static string JsonString(string? value)
    {
        if (value == null) return "null";
        // Use proper JSON escaping
        return JsonSerializer.Serialize(value);
    }

    private record FindingData(
        string Type,
        string ClassName,
        string? MethodName,
        string Severity,
        string Confidence,
        string? Description,
        string? Reasoning,
        string? CodeSnippet,
        string? FilePath,
        int? LineNumber,
        string? PotentialFix,
        string? EntityName,
        string? XmlStatus,
        string? XmlFile,
        string? FuzzyMatches,
        string? DeadCodeAnalysis,
        string? SemanticContext,
        string? Reachability,
        string? SourceContext,
        string? UsageLevel,
        // New fields from consolidated call graph
        string? Callees,       // Methods this method calls (from cg_calls)
        string? TypeHierarchy  // Base classes and interfaces (from cg_types/cg_implements)
    );
}
