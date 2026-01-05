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
        body.AppendLine(@"    <option value=""stub_method"">Stub Methods</option>");
        body.AppendLine(@"    <option value=""unimplemented"">Not Implemented</option>");
        body.AppendLine(@"    <option value=""empty_catch"">Empty Catch</option>");
        body.AppendLine(@"    <option value=""todo"">TODO/FIXME</option>");
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

  let html = '';

  // Type order and labels
  const typeOrder = ['hookable_event', 'hardcoded_entity', 'console_command', 'singleton_access', 'stub_method', 'unimplemented', 'empty_catch', 'todo'];
  const typeLabels = {{
    'hookable_event': {{ label: 'Hookable Events', desc: 'Virtual methods that can be patched with Harmony or overridden' }},
    'hardcoded_entity': {{ label: 'Hardcoded Entities', desc: 'Items, buffs, and entities referenced in code' }},
    'console_command': {{ label: 'Console Commands', desc: 'Commands available via F1 console' }},
    'singleton_access': {{ label: 'Singleton Access Points', desc: 'Entry points for accessing game systems' }},
    'stub_method': {{ label: 'Stub Methods', desc: 'Methods that return null/default - potential hook points' }},
    'unimplemented': {{ label: 'Not Implemented', desc: 'Methods that throw NotImplementedException' }},
    'empty_catch': {{ label: 'Empty Catch Blocks', desc: 'Exception handlers that silently swallow errors' }},
    'todo': {{ label: 'TODO/FIXME Comments', desc: 'Developer notes about incomplete or problematic code' }}
  }};

  typeOrder.forEach(type => {{
    if (!byType[type] || byType[type].length === 0) return;

    const info = typeLabels[type] || {{ label: type, desc: '' }};
    const items = byType[type];

    html += '<details' + (items.length <= 20 ? ' open' : '') + '>';
    html += '<summary>';
    html += '<span style=""flex: 1;"">' + info.label + '</span>';
    html += '<span class=""text-dim"" style=""font-size: 12px;"">' + items.length + ' findings</span>';
    html += '</summary>';
    html += '<div class=""details-body"">';
    html += '<p class=""text-muted"" style=""margin-bottom: 1rem;"">' + info.desc + '</p>';

    // Render ALL items - no artificial limit
    items.forEach((f, idx) => {{
      html += renderFinding(f, idx);
    }});

    html += '</div></details>';
  }});

  container.innerHTML = html;
}}

function renderFinding(f, idx) {{
  const severityClass = {{
    'BUG': 'tag-high',
    'WARNING': 'tag-medium',
    'OPPORTUNITY': 'tag-low',
    'INFO': 'tag-info'
  }}[f.severity] || 'tag-info';
  
  const usageLevelColors = {{
    'active': {{ bg: 'rgba(46, 204, 113, 0.2)', border: 'rgba(46, 204, 113, 0.5)', text: '#27ae60', label: 'Active' }},
    'moderate': {{ bg: 'rgba(52, 152, 219, 0.2)', border: 'rgba(52, 152, 219, 0.5)', text: '#3498db', label: 'Moderate' }},
    'low': {{ bg: 'rgba(241, 196, 15, 0.2)', border: 'rgba(241, 196, 15, 0.5)', text: '#f39c12', label: 'Low Usage' }},
    'internal': {{ bg: 'rgba(149, 165, 166, 0.2)', border: 'rgba(149, 165, 166, 0.5)', text: '#7f8c8d', label: 'Internal' }},
    'unused': {{ bg: 'rgba(231, 76, 60, 0.2)', border: 'rgba(231, 76, 60, 0.5)', text: '#e74c3c', label: 'Unused' }},
    'unknown': {{ bg: 'rgba(155, 89, 182, 0.2)', border: 'rgba(155, 89, 182, 0.5)', text: '#9b59b6', label: 'Unknown' }}
  }};

  const fileName = f.filePath ? f.filePath.split(/[/\\\\]/).pop() : '';

  let html = '<div class=""conflict-group"" style=""margin-bottom: 0.75rem;"">';

  // Header with usage level badge
  html += '<div class=""group-header"" style=""display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap;"">';
  html += '<span class=""tag ' + severityClass + ' compact-tag"">' + f.type.replace(/_/g, ' ') + '</span>';
  
  // Usage level badge
  if (f.usageLevel) {{
    const ul = usageLevelColors[f.usageLevel] || usageLevelColors['unknown'];
    html += '<span class=""tag"" style=""background: ' + ul.bg + '; border: 1px solid ' + ul.border + '; color: ' + ul.text + '; font-size: 10px;"">' + ul.label + '</span>';
  }}
  
  html += '<code style=""font-size: 12px;"">' + escapeHtml(f.className) + (f.methodName ? '.' + escapeHtml(f.methodName) : '') + '</code>';
  if (fileName && f.lineNumber) {{
    html += '<span class=""text-dim"" style=""font-size: 11px; margin-left: auto;"">' + escapeHtml(fileName) + ':' + f.lineNumber + '</span>';
  }}
  html += '</div>';

  // Body
  html += '<div class=""conflict-stack"" style=""padding: 0.75rem;"">';

  // Description with XML status indicator
  if (f.description) {{
    html += '<div style=""margin-bottom: 0.5rem;"">';
    if (f.xmlStatus === 'found') {{
      html += '<span class=""tag tag-healthy"" style=""font-size: 10px; margin-right: 0.5rem;"">In XML</span>';
    }} else if (f.xmlStatus === 'code_only') {{
      html += '<span class=""tag tag-review"" style=""font-size: 10px; margin-right: 0.5rem;"">Code Only</span>';
    }}
    html += escapeHtml(f.description);
    html += '</div>';
  }}

  // Entity link for cross-reference
  if (f.entityName && f.xmlStatus === 'found') {{
    html += '<div style=""margin-bottom: 0.5rem; font-size: 12px;"">';
    html += '<a href=""entities.html?search=' + encodeURIComponent(f.entityName) + '"" class=""mod-link"">View ' + escapeHtml(f.entityName) + ' in Entities</a>';
    html += '</div>';
  }}

  // REACHABILITY SECTION - Comprehensive usage analysis
  if (f.reachability) {{
    const r = f.reachability;
    const usageLevel = r.usage_level || r.usageLevel || 'unknown';
    const confidence = r.confidence || 0;
    const reasoning = r.reasoning || '';
    const callerCount = r.caller_count || r.callerCount || 0;
    const externalCallerCount = r.external_caller_count || r.externalCallerCount || 0;
    const callChains = r.call_chains || r.callChains || [];
    
    const ul = usageLevelColors[usageLevel] || usageLevelColors['unknown'];
    
    html += '<details class=""reachability-section"" style=""margin-bottom: 0.5rem;"">';
    html += '<summary style=""cursor: pointer; padding: 0.5rem; background: ' + ul.bg + '; border: 1px solid ' + ul.border + '; border-radius: 4px;"">';
    html += '<span style=""font-weight: 600; color: ' + ul.text + ';"">Reachability: ' + ul.label + '</span>';
    html += '<span class=""text-dim"" style=""margin-left: 0.5rem; font-size: 11px;"">(' + Math.round(confidence * 100) + '% confidence, ' + callerCount + ' callers)</span>';
    html += '</summary>';
    html += '<div style=""padding: 0.75rem; border: 1px solid ' + ul.border + '; border-top: none; border-radius: 0 0 4px 4px; background: var(--bg-secondary);"">';
    
    // Reasoning
    if (reasoning) {{
      html += '<div style=""font-size: 12px; margin-bottom: 0.5rem;"">' + escapeHtml(reasoning) + '</div>';
    }}
    
    // Call chains - render as visual execution tree
    if (callChains.length > 0) {{
      html += '<div style=""margin-top: 0.5rem;"">';
      html += '<div style=""font-size: 11px; font-weight: 600; margin-bottom: 0.5rem;"">Execution Path (how this code gets called):</div>';
      callChains.slice(0, 3).forEach((chain, i) => {{
        html += '<div style=""font-family: monospace; font-size: 11px; background: var(--bg-primary); border-radius: 4px; padding: 0.5rem; margin-bottom: 0.5rem;"">';
        chain.forEach((node, j) => {{
          const entryType = node.entry_point_type || node.entryPointType;
          const methodClass = node['class'] || node.className || '';
          const methodName = node.method || node.methodName || '';
          const isTarget = j === chain.length - 1;
          const indent = '&nbsp;&nbsp;'.repeat(j);
          const connector = j === 0 ? '' : '└─ ';
          
          // Entry point badges with colors
          let badge = '';
          if (entryType) {{
            const badgeColors = {{
              'console': {{ bg: 'rgba(155, 89, 182, 0.3)', text: '#9b59b6', label: 'CONSOLE CMD' }},
              'unity': {{ bg: 'rgba(46, 204, 113, 0.3)', text: '#27ae60', label: 'UNITY' }},
              'event': {{ bg: 'rgba(52, 152, 219, 0.3)', text: '#3498db', label: 'EVENT' }},
              'main': {{ bg: 'rgba(241, 196, 15, 0.3)', text: '#f39c12', label: 'MAIN' }}
            }};
            const bc = badgeColors[entryType] || {{ bg: 'rgba(149, 165, 166, 0.3)', text: '#7f8c8d', label: entryType.toUpperCase() }};
            badge = '<span style=""background: ' + bc.bg + '; color: ' + bc.text + '; padding: 1px 4px; border-radius: 3px; font-size: 9px; margin-right: 4px;"">' + bc.label + '</span>';
          }}
          
          html += '<div style=""' + (isTarget ? 'font-weight: bold; color: var(--accent-primary);' : '') + '"">';
          html += indent + connector + badge + escapeHtml(methodClass + '.' + methodName);
          if (isTarget) html += ' ◀ THIS CODE';
          html += '</div>';
        }});
        html += '</div>';
      }});
      if (callChains.length > 3) {{
        html += '<div class=""text-dim"" style=""font-size: 10px;"">...and ' + (callChains.length - 3) + ' more execution paths</div>';
      }}
      html += '</div>';
    }}
    
    html += '</div></details>';
  }}
  
  // CALLERS TABLE - Show who calls the method containing this entity
  if (f.deadCodeAnalysis) {{
    const dca = f.deadCodeAnalysis;
    const callers = dca.method_callers || dca.methodCallers || [];
    
    if (callers.length > 0) {{
      const methodLabel = f.methodName ? ' (' + escapeHtml(f.methodName) + ')' : '';
      html += '<details class=""callers-section"" style=""margin-bottom: 0.5rem;"">';
      html += '<summary style=""cursor: pointer; font-size: 12px;"">Method Callers' + methodLabel + ' (' + callers.length + ' locations)</summary>';
      html += '<div style=""padding: 0.5rem; background: var(--bg-secondary); border-radius: 4px; margin-top: 0.25rem;"">';
      html += '<div style=""font-size: 10px; color: var(--text-muted); margin-bottom: 0.5rem; font-style: italic;"">These call the method containing this reference - see highlighted entity in method body below</div>';
      
      callers.slice(0, 5).forEach(c => {{
        const callerClass = c.caller_class || c.callerClass || '';
        const callerMethod = c.caller_method || c.callerMethod || '';
        const filePath = c.file_path || c.filePath || '';
        const lineNum = c.line_number || c.lineNumber || 0;
        const snippet = c.code_snippet || c.codeSnippet || '';
        
        html += '<div style=""border-bottom: 1px solid var(--border); padding: 0.5rem 0;"">';
        html += '<div style=""display: flex; justify-content: space-between; align-items: center;"">';
        html += '<code style=""font-size: 11px;"">' + escapeHtml(callerClass + '.' + callerMethod) + '</code>';
        html += '<span class=""text-dim"" style=""font-size: 10px;"">' + escapeHtml(filePath) + ':' + lineNum + '</span>';
        html += '</div>';
        if (snippet) {{
          html += '<pre style=""font-size: 10px; margin: 0.25rem 0 0 0; padding: 0.5rem; background: var(--bg-primary); border-radius: 4px; overflow-x: auto; white-space: pre-wrap;"">' + escapeHtml(snippet) + '</pre>';
        }}
        html += '</div>';
      }});
      
      if (callers.length > 5) {{
        html += '<div class=""text-dim"" style=""font-size: 10px; padding-top: 0.5rem;"">...and ' + (callers.length - 5) + ' more callers</div>';
      }}
      
      html += '</div></details>';
    }}
  }}

  // SEMANTIC CONTEXT - Show semantic analysis for all hardcoded entities
  if (f.semanticContext) {{
    const sc = f.semanticContext;
    const category = sc.category || '';
    const reasoning = sc.reasoning || '';
    const moddability = sc.moddability_level || sc.moddabilityLevel || '';
    const concerns = sc.related_concerns || sc.relatedConcerns || [];
    
    html += '<details class=""semantic-context"" style=""margin-bottom: 0.5rem;"">';
    html += '<summary style=""cursor: pointer; font-size: 12px;"">Semantic Analysis: ' + escapeHtml(category) + '</summary>';
    html += '<div style=""background: linear-gradient(135deg, rgba(52, 152, 219, 0.1) 0%, rgba(155, 89, 182, 0.1) 100%); border: 1px solid rgba(52, 152, 219, 0.3); border-radius: 4px; padding: 0.75rem; margin-top: 0.25rem;"">';
    
    // Moddability
    if (moddability) {{
      html += '<div style=""font-size: 11px; margin-bottom: 0.5rem;""><strong>Moddability:</strong> ' + escapeHtml(moddability) + '</div>';
    }}
    
    // Semantic reasoning
    if (reasoning) {{
      html += '<div style=""font-size: 12px; color: var(--text-secondary); margin-bottom: 0.5rem;"">' + escapeHtml(reasoning) + '</div>';
    }}
    
    // Related concerns
    if (concerns.length > 0) {{
      html += '<div style=""display: flex; flex-wrap: wrap; gap: 0.25rem;"">';
      concerns.forEach(c => {{
        html += '<span class=""tag tag-info"" style=""font-size: 9px; padding: 0.15rem 0.4rem;"">' + escapeHtml(c) + '</span>';
      }});
      html += '</div>';
    }}
    
    html += '</div></details>';
  }}

  // Fuzzy matches for code-only entities
  if (f.fuzzyMatches && Array.isArray(f.fuzzyMatches) && f.fuzzyMatches.length > 0) {{
    html += '<details class=""fuzzy-matches"" style=""margin-bottom: 0.5rem;"">';
    html += '<summary style=""cursor: pointer; font-size: 12px;"">Similar Entities Found (' + f.fuzzyMatches.length + ')</summary>';
    html += '<div style=""background: var(--bg-secondary); border-radius: 4px; padding: 0.5rem; margin-top: 0.25rem;"">';
    f.fuzzyMatches.forEach(m => {{
      const scorePercent = Math.round((m.score || 0) * 100);
      html += '<div style=""display: flex; align-items: center; gap: 0.5rem; font-size: 12px; margin-top: 0.25rem;"">';
      html += '<a href=""entities.html?search=' + encodeURIComponent(m.name || '') + '"" class=""mod-link"">' + escapeHtml(m.name || '') + '</a>';
      html += '<span class=""text-dim"">(' + scorePercent + '% match)</span>';
      if (m.reason) {{
        html += '<span class=""text-dim"" style=""font-size: 10px;"">- ' + escapeHtml(m.reason) + '</span>';
      }}
      html += '</div>';
    }});
    html += '</div></details>';
  }}

  // Code snippet (collapsible) - now shows full method body with entity highlighted
  if (f.codeSnippet) {{
    html += '<details class=""xml-expand"" open>';
    html += '<summary>Show Method Body</summary>';
    // Highlight the entity name in the code if we have one
    let codeHtml = escapeHtml(f.codeSnippet);
    if (f.entityName) {{
      // Escape regex special chars in entity name, then highlight all occurrences
      const entityEscaped = f.entityName.replace(/[.*+?^${{}}()|[\\]\\\\]/g, '\\\\$&');
      const regex = new RegExp('(' + entityEscaped + ')', 'gi');
      codeHtml = codeHtml.replace(regex, '<mark style=""background: rgba(241, 196, 15, 0.4); padding: 0 2px; border-radius: 2px;"">$1</mark>');
    }}
    html += '<pre class=""xml-inline"" style=""max-height: 400px; overflow: auto;"">' + codeHtml + '</pre>';
    html += '</details>';
  }}

  // Potential fix / suggestion
  if (f.potentialFix) {{
    html += '<div class=""text-muted"" style=""font-size: 11px; margin-top: 0.5rem; padding: 0.5rem; background: var(--bg-secondary); border-radius: 4px;"">' + escapeHtml(f.potentialFix) + '</div>';
  }}

  // Reasoning (additional context, collapsed by default)
  if (f.reasoning && f.reasoning !== f.description) {{
    html += '<details class=""xml-expand"" style=""margin-top: 0.25rem;"">';
    html += '<summary style=""font-size: 11px;"">Why this matters</summary>';
    html += '<div style=""padding: 0.5rem; font-size: 12px; color: var(--text-muted);"">' + escapeHtml(f.reasoning) + '</div>';
    html += '</details>';
  }}

  html += '</div></div>';
  return html;
}}

// Initialize on page load
document.addEventListener('DOMContentLoaded', function() {{
  filterGameCode();
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
                UsageLevel: usageLevel
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
            obj.Append($"\"usageLevel\":{JsonString(f.UsageLevel)}");
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
        string? UsageLevel
    );
}
