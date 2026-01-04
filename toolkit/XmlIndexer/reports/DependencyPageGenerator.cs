using System.Text;
using System.Text.Json;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the dependency page (dependencies.html) with inheritance chains and impact analysis.
/// Formatted like the entity explorer with searchable, expandable list.
/// </summary>
public static class DependencyPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData)
    {
        var body = new StringBuilder();

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Dependency Analysis</h1>");
        body.AppendLine(@"  <p>Explore inheritance chains, impact analysis, and understand ripple effects of changes</p>");
        body.AppendLine(@"</div>");

        // Check if dependency data exists
        if (data.TotalTransitiveRefs == 0)
        {
            body.AppendLine(@"<div class=""card"" style=""border-color: var(--accent-secondary);"">");
            body.AppendLine(@"<h3 style=""color: var(--accent-secondary); margin-bottom: 0.75rem;"">⚠️ Dependency Graph Not Built</h3>");
            body.AppendLine(@"<p>Run the following command to compute transitive dependencies:</p>");
            body.AppendLine(@"<pre style=""background: var(--bg-secondary); padding: 1rem; border-radius: var(--radius); margin-top: 0.75rem;""><code>XmlIndexer build-dependency-graph ecosystem.db</code></pre>");
            body.AppendLine(@"</div>");
            return SharedAssets.WrapPage("Dependencies", "dependencies.html", body.ToString());
        }

        // Stats bar
        body.AppendLine(@"<div class=""stats-bar"">");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.TotalTransitiveRefs:N0}</span><span class=""stat-label"">Dependency Chains</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.InheritanceHotspots.Count}</span><span class=""stat-label"">Hotspots</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.InheritanceHotspots.Where(h => h.RiskLevel == "CRITICAL").Count()}</span><span class=""stat-label"">Critical Risk</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.InheritanceHotspots.Where(h => h.RiskLevel == "HIGH").Count()}</span><span class=""stat-label"">High Risk</span></div>");
        body.AppendLine(@"</div>");

        // Filter bar with fuzzy toggle
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""dep-search"" placeholder=""Search entities..."" oninput=""filterDependencies()"">");
        body.AppendLine(@"  <select class=""filter-select"" id=""risk-filter"" onchange=""filterDependencies()"">");
        body.AppendLine(@"    <option value="""">All Risk Levels</option>");
        body.AppendLine(@"    <option value=""CRITICAL"">Critical Only</option>");
        body.AppendLine(@"    <option value=""HIGH"">High & Critical</option>");
        body.AppendLine(@"    <option value=""MEDIUM"">Medium+</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"  <label class=""fuzzy-toggle""><input type=""checkbox"" id=""fuzzy-toggle"" checked onchange=""filterDependencies()""> Fuzzy</label>");
        body.AppendLine(@"</div>");

        // Results container
        body.AppendLine(@"<div id=""dep-results""></div>");

        // Show more button
        body.AppendLine(@"<button class=""show-more"" id=""show-more-btn"" onclick=""showMore()"" style=""display: none;"">Show More</button>");

        // Entity detail panel
        body.AppendLine(@"<div class=""entity-panel"" id=""dep-panel"" style=""display: none;""></div>");

        // Generate data as JSON
        var chainJson = GenerateChainJson(data.SampleDependencyChains, data.InheritanceHotspots);

        var script = GenerateScript(chainJson);

        return SharedAssets.WrapPage("Dependencies", "dependencies.html", body.ToString(), script);
    }

    private static string GenerateScript(string chainJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SharedAssets.GenerateInlineDataScript("DEP_DATA", chainJson));

        sb.AppendLine(@"
let filteredDeps = [];
let displayCount = 20;

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}

function filterDependencies() {
  const query = document.getElementById('dep-search').value;
  const riskFilter = document.getElementById('risk-filter').value;
  const useFuzzy = document.getElementById('fuzzy-toggle').checked;

  filteredDeps = DEP_DATA.filter(d => {
    if (riskFilter) {
      if (riskFilter === 'CRITICAL' && d.risk !== 'CRITICAL') return false;
      if (riskFilter === 'HIGH' && d.risk !== 'CRITICAL' && d.risk !== 'HIGH') return false;
      if (riskFilter === 'MEDIUM' && d.risk !== 'CRITICAL' && d.risk !== 'HIGH' && d.risk !== 'MEDIUM') return false;
    }
    return true;
  });

  if (query) {
    if (useFuzzy) {
      filteredDeps = filteredDeps
        .map(d => ({ dep: d, score: fuzzyScore(query, d.name) }))
        .filter(x => x.score > 0)
        .sort((a, b) => b.score - a.score)
        .map(x => x.dep);
    } else {
      const q = query.toLowerCase();
      filteredDeps = filteredDeps.filter(d => d.name.toLowerCase().includes(q));
    }
  }

  displayCount = 20;
  renderDependencies();
}

function renderDependencies() {
  const container = document.getElementById('dep-results');
  const showBtn = document.getElementById('show-more-btn');
  const toShow = filteredDeps.slice(0, displayCount);

  let html = '<table class=""data-table""><thead><tr>';
  html += '<th>Type</th><th>Entity</th><th>Extends</th><th>Dependents</th><th>Risk</th>';
  html += '</tr></thead><tbody>';

  toShow.forEach(d => {
    const riskClass = d.risk === 'CRITICAL' ? 'tag-high' : d.risk === 'HIGH' ? 'tag-medium' : 'tag-low';
    html += '<tr onclick=""showDepDetail(\'' + escapeHtml(d.type) + '\', \'' + escapeHtml(d.name) + '\')""';
    html += ' style=""cursor: pointer;"">';
    html += '<td><span class=""tag tag-type"">' + escapeHtml(d.type) + '</span></td>';
    html += '<td>' + escapeHtml(d.name) + '</td>';
    html += '<td class=""text-muted"">' + (d.extends || '-') + '</td>';
    html += '<td><strong>' + (d.dependentCount || 0) + '</strong></td>';
    html += '<td><span class=""tag ' + riskClass + '"">' + (d.risk || 'LOW') + '</span></td>';
    html += '</tr>';
  });

  html += '</tbody></table>';
  html += '<p class=""text-muted"" style=""margin-top: 0.5rem; font-size: 12px;"">Showing ' + toShow.length + ' of ' + filteredDeps.length + ' entities</p>';

  container.innerHTML = html;

  if (filteredDeps.length > displayCount) {
    showBtn.style.display = 'inline-block';
    showBtn.textContent = 'Show More (' + (filteredDeps.length - displayCount) + ' remaining)';
  } else {
    showBtn.style.display = 'none';
  }
}

function showMore() {
  displayCount += 50;
  renderDependencies();
}

function showDepDetail(type, name) {
  const panel = document.getElementById('dep-panel');
  const dep = DEP_DATA.find(d => d.type === type && d.name === name);

  if (!dep) {
    panel.style.display = 'none';
    return;
  }

  let html = '<h3><span class=""tag tag-type"">' + escapeHtml(type) + '</span> ' + escapeHtml(name) + '</h3>';
  html += '<div class=""meta"">';
  if (dep.file) html += '📁 ' + escapeHtml(dep.file);
  if (dep.extends) html += ' • Extends: <a href=""entities.html?search=' + encodeURIComponent(dep.extends) + '"">' + escapeHtml(dep.extends) + '</a>';
  html += '</div>';

  // Depends On section
  html += '<div class=""section""><div class=""section-title"" style=""color: var(--accent);"">↓ This Entity Depends On (' + (dep.dependsOn ? dep.dependsOn.length : 0) + ')</div>';
  if (dep.dependsOn && dep.dependsOn.length > 0) {
    html += '<div class=""dep-list"" id=""depends-on-list"">';
    dep.dependsOn.forEach((d, i) => {
      const indent = (d.depth || 0) * 16;
      html += '<div class=""chain-step"" style=""margin-left: ' + indent + 'px;"">';
      html += '<span class=""chain-arrow"">→</span>';
      html += '<a href=""entities.html?search=' + encodeURIComponent(d.name) + '"">';
      html += '<span class=""tag tag-type"">' + escapeHtml(d.type) + '</span> ' + escapeHtml(d.name);
      html += '</a>';
      html += '<span class=""chain-via"">' + escapeHtml(d.refTypes || '') + '</span>';
      html += '</div>';
    });
    html += '</div>';
  } else {
    html += '<p class=""text-dim"">No dependencies</p>';
  }
  html += '</div>';

  // Depended On By section
  html += '<div class=""section""><div class=""section-title"" style=""color: var(--info);"">↑ Entities That Depend On This (' + (dep.dependedBy ? dep.dependedBy.length : 0) + ')</div>';
  if (dep.dependedBy && dep.dependedBy.length > 0) {
    html += '<div class=""dep-list"" id=""depended-by-list"">';
    dep.dependedBy.forEach((d, i) => {
      html += '<div class=""chain-step"">';
      html += '<span class=""chain-arrow"" style=""color: var(--info);"">←</span>';
      html += '<a href=""entities.html?search=' + encodeURIComponent(d.name) + '"">';
      html += '<span class=""tag tag-type"">' + escapeHtml(d.type) + '</span> ' + escapeHtml(d.name);
      html += '</a>';
      html += '<span class=""chain-via"">' + escapeHtml(d.refTypes || '') + '</span>';
      html += '</div>';
    });
    html += '</div>';
  } else {
    html += '<p class=""text-dim"">Nothing depends on this</p>';
  }
  html += '</div>';

  html += '<p style=""margin-top: 1rem;"">';
  html += '<a href=""entities.html?search=' + encodeURIComponent(name) + '"">View full entity details →</a>';
  html += '</p>';

  panel.style.display = 'block';
  panel.innerHTML = html;
  panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

document.addEventListener('DOMContentLoaded', () => {
  filterDependencies();
  const params = new URLSearchParams(window.location.search);
  const searchParam = params.get('search');
  if (searchParam) {
    document.getElementById('dep-search').value = searchParam;
    filterDependencies();
  }
});
");
        return sb.ToString();
    }

    private static string GenerateChainJson(List<EntityDependencyInfo> chains, List<InheritanceHotspot> hotspots)
    {
        // Merge chains and hotspots into a single list
        var hotspotDict = hotspots.ToDictionary(h => (h.EntityType, h.EntityName), h => h);

        var items = chains.Select(c => {
            hotspotDict.TryGetValue((c.EntityType, c.EntityName), out var hotspot);
            return new
            {
                type = c.EntityType,
                name = c.EntityName,
                file = c.FilePath,
                extends = c.ExtendsFrom,
                dependentCount = hotspot?.DependentCount ?? c.DependedOnBy.Count,
                risk = hotspot?.RiskLevel ?? (c.DependedOnBy.Count > 50 ? "HIGH" : c.DependedOnBy.Count > 20 ? "MEDIUM" : "LOW"),
                dependsOn = c.DependsOn.Select(d => new
                {
                    type = d.EntityType,
                    name = d.EntityName,
                    depth = d.Depth,
                    refTypes = d.ReferenceTypes
                }),
                dependedBy = c.DependedOnBy.Select(d => new
                {
                    type = d.EntityType,
                    name = d.EntityName,
                    refTypes = d.ReferenceTypes
                })
            };
        });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
    }
}
