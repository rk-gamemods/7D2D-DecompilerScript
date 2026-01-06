using System.Text;
using System.Text.Json;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the mods page (mods.html) with per-mod deep-dives and health indicators.
/// </summary>
public static class ModPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData)
    {
        var body = new StringBuilder();

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Mod Analysis</h1>");
        body.AppendLine($@"  <p>Detailed view of {data.TotalMods} installed mods</p>");
        body.AppendLine(@"</div>");

        // Health summary stats
        var healthCounts = data.ModSummary.GroupBy(m => m.Health).ToDictionary(g => g.Key, g => g.Count());
        body.AppendLine(@"<div class=""stats-bar"">");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--accent);"">{healthCounts.GetValueOrDefault("Healthy", 0)}</span><span class=""stat-label"">Healthy</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--accent-secondary);"">{healthCounts.GetValueOrDefault("Review", 0)}</span><span class=""stat-label"">Need Review</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: var(--danger);"">{healthCounts.GetValueOrDefault("Broken", 0)}</span><span class=""stat-label"">Broken</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.XmlMods}</span><span class=""stat-label"">XML Only</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.CSharpMods}</span><span class=""stat-label"">C# Only</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.HybridMods}</span><span class=""stat-label"">Hybrid</span></div>");
        body.AppendLine(@"</div>");

        // Filter bar with fuzzy toggle
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""mod-search"" placeholder=""Search mods..."" oninput=""filterMods()"">");
        body.AppendLine(@"  <select class=""filter-select"" id=""health-filter"" onchange=""filterMods()"">");
        body.AppendLine(@"    <option value="""">All Health</option>");
        body.AppendLine(@"    <option value=""Healthy"">Healthy</option>");
        body.AppendLine(@"    <option value=""Review"">Need Review</option>");
        body.AppendLine(@"    <option value=""Broken"">Broken</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"  <select class=""filter-select"" id=""type-filter"" onchange=""filterMods()"">");
        body.AppendLine(@"    <option value="""">All Types</option>");
        body.AppendLine(@"    <option value=""XML"">XML Only</option>");
        body.AppendLine(@"    <option value=""C#"">C# Only</option>");
        body.AppendLine(@"    <option value=""Hybrid"">Hybrid</option>");
        body.AppendLine(@"    <option value=""Config"">Config Only</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"  <label class=""fuzzy-toggle""><input type=""checkbox"" id=""fuzzy-toggle"" checked onchange=""filterMods()""> Fuzzy</label>");
        body.AppendLine(@"</div>");

        // Results container
        body.AppendLine(@"<div id=""mod-results""></div>");

        // Generate mod data as JSON
        var modJson = GenerateModJson(data.ModSummary, data.ModBehaviors, extData.ModDetails);

        var script = $@"
{SharedAssets.GenerateInlineDataScript("MOD_DATA", modJson)}

function filterMods() {{
  const query = document.getElementById('mod-search').value;
  const healthFilter = document.getElementById('health-filter').value;
  const typeFilter = document.getElementById('type-filter').value;
  const useFuzzy = document.getElementById('fuzzy-toggle').checked;

  let filtered = MOD_DATA.filter(m => {{
    if (healthFilter && m.health !== healthFilter) return false;
    if (typeFilter && m.modType !== typeFilter) return false;
    return true;
  }});

  if (query) {{
    if (useFuzzy) {{
      filtered = filtered
        .map(m => ({{ mod: m, score: fuzzyScore(query, m.name) }}))
        .filter(x => x.score > 0)
        .sort((a, b) => b.score - a.score)
        .map(x => x.mod);
    }} else {{
      const q = query.toLowerCase();
      filtered = filtered.filter(m => m.name.toLowerCase().includes(q));
    }}
  }}

  renderMods(filtered);
}}

function renderMods(mods) {{
  const container = document.getElementById('mod-results');

  container.innerHTML = mods.map(m => `
    <details id=""mod-${{m.name.replace(/[^a-zA-Z0-9]/g, '_')}}"">
      <summary>
        <span class=""load-order-badge"" title=""Load Order"">#${{m.loadOrder}}</span>
        <span style=""flex: 1;"">${{m.name}}</span>
        ${{getModTypeBadge(m.modType)}}
        ${{getHealthBadge(m.health)}}
        <span class=""text-dim"" style=""font-size: 12px; margin-left: 0.5rem;"">${{m.healthNote || ''}}</span>
      </summary>
      <div class=""details-body"">
        <div style=""margin-bottom: 1rem; display: flex; gap: 1rem; flex-wrap: wrap; font-size: 12px; color: var(--text-muted);"">
          <span title=""Load Order"">📋 Load Order: <strong>#${{m.loadOrder}}</strong></span>
          ${{m.folderName ? `<span title=""Folder Name"">📁 ${{m.folderName}}</span>` : ''}}
        </div>
        
        ${{m.xmlInfo ? `
          <div style=""margin-bottom: 1rem; color: var(--text-muted);"">
            ${{m.xmlInfo.author ? `by <strong>${{m.xmlInfo.author}}</strong>` : ''}}
            ${{m.xmlInfo.version ? ` • v${{m.xmlInfo.version}}` : ''}}
            ${{m.xmlInfo.website ? ` • <a href=""${{m.xmlInfo.website}}"" target=""_blank"">${{m.xmlInfo.website}}</a>` : ''}}
          </div>
          ${{m.xmlInfo.description ? `<p style=""font-style: italic; margin-bottom: 1rem;"">${{m.xmlInfo.description}}</p>` : ''}}
        ` : ''}}

        ${{m.oneLiner && !m.oneLiner.includes('no detectable') ? `<p style=""margin-bottom: 1rem;"">${{m.oneLiner}}</p>` : ''}}

        ${{m.features && m.features.length > 0 ? `
          <div style=""margin-bottom: 1rem;"">
            <strong class=""text-dim"">Features:</strong>
            <span id=""features-${{m.name.replace(/[^a-zA-Z0-9]/g, '_')}}-short"">
              ${{m.features.slice(0, 5).join(' • ')}}
              ${{m.features.length > 5 ? ` <button onclick=""toggleFeatures('${{m.name.replace(/[^a-zA-Z0-9]/g, '_')}}')"" style=""background: none; border: none; color: var(--accent); cursor: pointer; font-size: 12px;"">[+${{m.features.length - 5}} more]</button>` : ''}}
            </span>
            ${{m.features.length > 5 ? `<span id=""features-${{m.name.replace(/[^a-zA-Z0-9]/g, '_')}}-full"" style=""display: none;"">
              ${{m.features.join(' • ')}}
              <button onclick=""toggleFeatures('${{m.name.replace(/[^a-zA-Z0-9]/g, '_')}}')"" style=""background: none; border: none; color: var(--accent); cursor: pointer; font-size: 12px;"">[show less]</button>
            </span>` : ''}}
          </div>
        ` : ''}}

        ${{m.systems && m.systems.length > 0 ? `
          <div style=""margin-bottom: 1rem;"">
            <strong class=""text-dim"">Systems Affected:</strong> ${{m.systems.join(', ')}}
          </div>
        ` : ''}}

        ${{m.warnings && m.warnings.length > 0 ? `
          <div style=""color: var(--accent-secondary); margin-bottom: 1rem;"">
            ⚠️ ${{m.warnings.join(' • ')}}
          </div>
        ` : ''}}

        ${{m.xmlOps > 0 ? `
          <div style=""margin-bottom: 1rem;"">
            <strong class=""text-dim"">XML Operations:</strong> ${{m.xmlOps}} operations
            ${{m.removes > 0 ? `<span class=""text-warning""> (${{m.removes}} removals)</span>` : ''}}
          </div>
        ` : ''}}

        ${{(m.patches && m.patches.length > 0) || (m.extensions && m.extensions.length > 0) || (m.entityDeps && m.entityDeps.length > 0) ? `
          <div style=""margin-bottom: 1rem;"">
            <strong class=""text-dim"">C# Analysis:</strong>
            <div style=""display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.5rem;"">
              ${{m.patches && m.patches.length > 0 ? '<span class=""tag tag-info"">Harmony Patches <span style=""opacity: 0.7;"">(' + m.patches.length + ')</span></span>' : ''}}
              ${{m.extensions && m.extensions.length > 0 ? '<span class=""tag tag-type"">Class Extensions <span style=""opacity: 0.7;"">(' + m.extensions.length + ')</span></span>' : ''}}
              ${{m.entityDeps && m.entityDeps.length > 0 ? '<span class=""tag tag-medium"">Entity Refs <span style=""opacity: 0.7;"">(' + m.entityDeps.length + ')</span></span>' : ''}}
            </div>
          </div>
        ` : ''}}

        ${{m.entityTypes && Object.keys(m.entityTypes).length > 0 ? `
          <div style=""margin-bottom: 1rem;"">
            <strong class=""text-dim"">Entity Types Modified:</strong>
            <div style=""display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.5rem;"">
              ${{Object.entries(m.entityTypes).map(function(e) {{ 
                return '<span class=""tag tag-type"">' + e[0] + ' <span style=""opacity: 0.7;"">(' + e[1] + ')</span></span>';
              }}).join('')}}
            </div>
          </div>
        ` : ''}}

        ${{m.topEntities && m.topEntities.length > 0 ? `
          <div style=""margin-bottom: 1rem;"">
            <strong class=""text-dim"">Top Targeted Entities:</strong>
            <div style=""display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.5rem;"">
              ${{m.topEntities.slice(0, 6).map(function(entity) {{
                var name = entity.split(':')[1] || entity;
                return '<a href=""entities.html?search=' + encodeURIComponent(name) + '"" class=""tag"" style=""background: var(--bg-tertiary);"">' + name + '</a>';
              }}).join('')}}
              ${{m.topEntities.length > 6 ? '<span class=""text-dim"">+' + (m.topEntities.length - 6) + ' more</span>' : ''}}
            </div>
          </div>
        ` : ''}}

        ${{m.operations && m.operations.length > 0 ? `
          <details style=""margin-top: 1rem;"">
            <summary>XML Operations Detail (${{m.operations.length}})</summary>
            <div class=""details-body"" style=""max-height: 400px; overflow-y: auto;"">
              <table class=""data-table"">
                <thead><tr><th>Operation</th><th>Target</th><th>Details</th></tr></thead>
                <tbody>
                  ${{m.operations.slice(0, 50).map(function(op) {{
                    var hasTarget = op.targetType && op.targetName;
                    var targetCell = hasTarget 
                      ? '<a href=""entities.html?search=' + encodeURIComponent(op.targetName || '') + '""><span class=""tag tag-type"">' + (op.targetType || '') + '</span> ' + (op.targetName || '') + '</a>'
                      : (op.xpath ? '<code class=""text-muted"" style=""font-size: 11px; word-break: break-all;"">' + linkifyXPath(op.xpath) + '</code>' : '<span class=""text-dim"">-</span>');
                    
                    var detailsCell;
                    if (op.property) {{
                      detailsCell = '<span class=""text-muted"">' + escapeHtml(op.property) + '</span>';
                    }}
                    if (op.elementContent) {{
                      var xmlContent = '<details class=""xml-expand""><summary style=""cursor: pointer; font-size: 11px; color: var(--accent-secondary);"">' + (op.property ? '' : '📄 ') + 'View XML</summary><pre style=""background: var(--bg-secondary); padding: 0.5rem; border-radius: 4px; margin-top: 0.5rem; font-size: 10px; overflow-x: auto; white-space: pre-wrap; max-height: 200px; overflow-y: auto;"">' + linkifyXml(op.elementContent) + '</pre></details>';
                      detailsCell = (detailsCell || '') + xmlContent;
                    }}
                    if (!detailsCell) {{
                      detailsCell = '<span class=""text-dim"">-</span>';
                    }}
                    
                    return '<tr><td><span class=""tag ' + getOpClass(op.operation) + '"">' + op.operation + '</span></td><td>' + targetCell + '</td><td>' + detailsCell + '</td></tr>';
                  }}).join('')}}
                  ${{m.operations.length > 50 ? `<tr><td colspan=""3"" class=""text-dim"">... and ${{m.operations.length - 50}} more</td></tr>` : ''}}
                </tbody>
              </table>
            </div>
          </details>
        ` : ''}}

        ${{m.patches && m.patches.length > 0 ? `
          <details style=""margin-top: 1rem;"">
            <summary>Harmony Patches (${{m.patches.length}})</summary>
            <div class=""details-body"">
              <table class=""data-table"">
                <thead><tr><th>Class</th><th>Method</th><th>Type</th><th>Code</th></tr></thead>
                <tbody>
                  ${{m.patches.map((p, idx) => `
                    <tr>
                      <td><code>${{p.className}}</code></td>
                      <td><code>${{p.methodName}}</code></td>
                      <td><span class=""tag tag-info"">${{p.patchType}}</span></td>
                      <td>${{p.codeSnippet ? `<details class=""code-expand""><summary style=""cursor: pointer; font-size: 11px; color: var(--accent);"">View Code</summary><pre class=""code-viewer"">${{escapeHtml(p.codeSnippet)}}</pre></details>` : '<span class=""text-dim"">-</span>'}}</td>
                    </tr>
                  `).join('')}}
                </tbody>
              </table>
            </div>
          </details>
        ` : ''}}

        ${{m.extensions && m.extensions.length > 0 ? `
          <details style=""margin-top: 1rem;"">
            <summary>Class Extensions (${{m.extensions.length}})</summary>
            <div class=""details-body"">
              <table class=""data-table"">
                <thead><tr><th>Base Class</th><th>Extended By</th></tr></thead>
                <tbody>
                  ${{m.extensions.map(e => `
                    <tr>
                      <td><span class=""tag tag-type"">${{e.baseClass}}</span></td>
                      <td><code>${{e.childClass}}</code></td>
                    </tr>
                  `).join('')}}
                </tbody>
              </table>
            </div>
          </details>
        ` : ''}}

        ${{m.entityDeps && m.entityDeps.length > 0 ? `
          <details style=""margin-top: 1rem;"">
            <summary>C# Entity Dependencies (${{m.entityDeps.length}})</summary>
            <div class=""details-body"" style=""max-height: 400px; overflow-y: auto;"">
              <table class=""data-table"">
                <thead><tr><th>Type</th><th>Entity</th><th>Source</th></tr></thead>
                <tbody>
                  ${{m.entityDeps.map(e => `
                    <tr>
                      <td><span class=""tag tag-type"">${{e.entityType}}</span></td>
                      <td><a href=""entities.html?search=${{encodeURIComponent(e.entityName)}}""><code>${{e.entityName}}</code></a></td>
                      <td><span class=""text-muted"" style=""font-size: 11px;"">${{e.sourceFile}}</span></td>
                    </tr>
                  `).join('')}}
                </tbody>
              </table>
            </div>
          </details>
        ` : ''}}
      </div>
    </details>
  `).join('');
}}

function getModTypeBadge(type) {{
  const typeMap = {{
    'XML': 'tag-low',
    'C#': 'tag-info',
    'Hybrid': 'tag-medium',
    'Config': 'tag-type'
  }};
  const cls = typeMap[type] || 'tag-type';
  return `<span class=""tag ${{cls}}"">${{type}}</span>`;
}}

function escapeHtml(str) {{
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}}

// XPath operators to link to W3C docs
const XPATH_LINKS = {{
  '//': 'https://www.w3.org/TR/xpath/#path-abbrev',
  '/': 'https://www.w3.org/TR/xpath/#location-paths',
  '@': 'https://www.w3.org/TR/xpath/#attribute-nodes',
  '..': 'https://www.w3.org/TR/xpath/#path-abbrev',
  '.': 'https://www.w3.org/TR/xpath/#path-abbrev',
  'ancestor::': 'https://www.w3.org/TR/xpath/#axes',
  'ancestor-or-self::': 'https://www.w3.org/TR/xpath/#axes',
  'child::': 'https://www.w3.org/TR/xpath/#axes',
  'descendant::': 'https://www.w3.org/TR/xpath/#axes',
  'descendant-or-self::': 'https://www.w3.org/TR/xpath/#axes',
  'following::': 'https://www.w3.org/TR/xpath/#axes',
  'following-sibling::': 'https://www.w3.org/TR/xpath/#axes',
  'parent::': 'https://www.w3.org/TR/xpath/#axes',
  'preceding::': 'https://www.w3.org/TR/xpath/#axes',
  'preceding-sibling::': 'https://www.w3.org/TR/xpath/#axes',
  'self::': 'https://www.w3.org/TR/xpath/#axes',
  'contains(': 'https://www.w3.org/TR/xpath/#function-contains',
  'starts-with(': 'https://www.w3.org/TR/xpath/#function-starts-with',
  'not(': 'https://www.w3.org/TR/xpath/#function-not',
  'last()': 'https://www.w3.org/TR/xpath/#function-last',
  'position()': 'https://www.w3.org/TR/xpath/#function-position'
}};

// Linkify XPath expression (link operators to W3C docs)
function linkifyXPath(xpath) {{
  if (!xpath) return '';
  let html = escapeHtml(xpath);
  const linked = new Set();
  // Sort by length descending so longer matches take precedence
  const ops = Object.keys(XPATH_LINKS).sort((a, b) => b.length - a.length);
  for (const op of ops) {{
    if (linked.has(op)) continue;
    const escaped = op.replace(/[.*+?^${{}}()|[\\]\\\\]/g, '\\\\$&');
    const pattern = new RegExp('(' + escaped + ')', 'g');
    if (pattern.test(html)) {{
      const url = XPATH_LINKS[op];
      html = html.replace(pattern, '<a href=""' + url + '"" class=""doc-link"" target=""_blank"" title=""XPath: ' + escapeHtml(op) + '"">$1</a>');
      linked.add(op);
    }}
  }}
  return html;
}}

// Linkify XML content (link entity names to entities page, XPath operators to docs)
function linkifyXml(xml) {{
  if (!xml) return '';
  let html = escapeHtml(xml);
  
  // Link name="" attributes to entities page (common pattern in 7D2D XML)
  html = html.replace(/name=&quot;([^&]+)&quot;/g, function(match, name) {{
    return 'name=&quot;<a href=""entities.html?search=' + encodeURIComponent(name) + '"" class=""entity-link"" title=""View entity: ' + name + '"">' + name + '</a>&quot;';
  }});
  
  // Also link ingredient/extends/parent/lootgroup references
  html = html.replace(/(?:ingredient|extends|parent|group|item|block|buff|entity_class)=&quot;([^&]+)&quot;/gi, function(match, name) {{
    return match.replace(name, '<a href=""entities.html?search=' + encodeURIComponent(name) + '"" class=""entity-link"" title=""View entity: ' + name + '"">' + name + '</a>');
  }});
  
  // Link xpath="" attributes (with XPath operator highlighting)
  html = html.replace(/xpath=&quot;([^&]+)&quot;/g, function(match, xpath) {{
    return 'xpath=&quot;' + linkifyXPathInline(xpath) + '&quot;';
  }});
  
  return html;
}}

// Inline XPath linking (for use within XML attributes, already escaped)
function linkifyXPathInline(xpath) {{
  const ops = Object.keys(XPATH_LINKS).sort((a, b) => b.length - a.length);
  let html = xpath;
  const linked = new Set();
  for (const op of ops) {{
    if (linked.has(op)) continue;
    const escaped = op.replace(/[.*+?^${{}}()|[\\]\\\\]/g, '\\\\$&');
    const pattern = new RegExp('(' + escaped + ')', 'g');
    if (pattern.test(html)) {{
      const url = XPATH_LINKS[op];
      html = html.replace(pattern, '<a href=""' + url + '"" class=""doc-link"" target=""_blank"" title=""XPath: ' + op.replace(/</g, '&lt;') + '"">$1</a>');
      linked.add(op);
    }}
  }}
  return html;
}}

function getHealthBadge(health) {{
  const cls = health === 'Healthy' ? 'tag-healthy' : health === 'Review' ? 'tag-review' : 'tag-broken';
  return `<span class=""tag ${{cls}}"">${{health}}</span>`;
}}

function getOpClass(op) {{
  if (op === 'remove') return 'tag-high';
  if (op === 'set' || op === 'setattribute') return 'tag-medium';
  return 'tag-low';
}}

function toggleFeatures(modId) {{
  const shortEl = document.getElementById('features-' + modId + '-short');
  const fullEl = document.getElementById('features-' + modId + '-full');
  if (shortEl && fullEl) {{
    const isExpanded = fullEl.style.display !== 'none';
    shortEl.style.display = isExpanded ? 'inline' : 'none';
    fullEl.style.display = isExpanded ? 'none' : 'inline';
  }}
}}

// Initialize
document.addEventListener('DOMContentLoaded', () => {{
  filterMods();

  // Check for search param
  const params = new URLSearchParams(window.location.search);
  const searchParam = params.get('search');
  if (searchParam) {{
    document.getElementById('mod-search').value = searchParam;
    filterMods();

    // Open the matching mod details
    const modEl = document.getElementById('mod-' + searchParam.replace(/[^a-zA-Z0-9]/g, '_'));
    if (modEl) {{
      modEl.open = true;
      modEl.scrollIntoView({{ behavior: 'smooth' }});
    }}
  }}
}});
";

        return SharedAssets.WrapPage("Mods", "mods.html", body.ToString(), script);
    }

    private static string GenerateModJson(List<ModInfo> mods, List<ModBehavior> behaviors, List<ModDetailExport> details)
    {
        var behaviorLookup = behaviors.ToDictionary(b => b.ModName, StringComparer.OrdinalIgnoreCase);
        var detailLookup = details.ToDictionary(d => d.ModName, StringComparer.OrdinalIgnoreCase);

        var items = mods.Select(m =>
        {
            behaviorLookup.TryGetValue(m.Name, out var behavior);
            detailLookup.TryGetValue(m.Name, out var detail);

            return new
            {
                name = m.Name,
                loadOrder = m.LoadOrder,
                folderName = m.FolderName,
                modType = m.ModType,
                health = m.Health,
                healthNote = m.HealthNote,
                xmlOps = m.XmlOps,
                csharpDeps = m.CSharpDeps,
                removes = m.Removes,
                oneLiner = behavior?.OneLiner,
                features = behavior?.KeyFeatures,
                systems = behavior?.SystemsAffected,
                warnings = behavior?.Warnings,
                xmlInfo = behavior?.XmlInfo != null ? new
                {
                    displayName = behavior.XmlInfo.DisplayName,
                    description = behavior.XmlInfo.Description,
                    author = behavior.XmlInfo.Author,
                    version = behavior.XmlInfo.Version,
                    website = behavior.XmlInfo.Website
                } : null,
                operations = detail?.XmlOperations?.Select(op => new
                {
                    operation = op.Operation,
                    targetType = op.TargetType,
                    targetName = op.TargetName,
                    property = op.PropertyName,
                    xpath = op.XPath,
                    elementContent = op.ElementContent
                }),
                patches = detail?.HarmonyPatches?.Select(p => new
                {
                    className = p.ClassName,
                    methodName = p.MethodName,
                    patchType = p.PatchType,
                    codeSnippet = p.CodeSnippet
                }),
                extensions = detail?.ClassExtensions?.Select(e => new
                {
                    baseClass = e.BaseClass,
                    childClass = e.ChildClass
                }),
                entityDeps = detail?.EntityDependencies?.Select(e => new
                {
                    entityType = e.EntityType,
                    entityName = e.EntityName,
                    sourceFile = e.SourceFile,
                    pattern = e.Pattern
                }),
                entityTypes = detail?.EntityTypeBreakdown,
                topEntities = detail?.TopTargetedEntities
            };
        });

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
    }
}
