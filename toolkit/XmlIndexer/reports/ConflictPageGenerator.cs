using System.Text;
using System.Text.Json;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the conflict center page (conflicts.html) with severity filtering and detailed conflict views.
/// </summary>
public static class ConflictPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData)
    {
        var body = new StringBuilder();

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Conflict Center</h1>");
        body.AppendLine(@"  <p>Identify entities modified by multiple mods and potential compatibility issues</p>");
        body.AppendLine(@"</div>");

        // Risk counts
        var riskCounts = data.ContestedEntities.GroupBy(c => c.RiskLevel).ToDictionary(g => g.Key, g => g.Count());
        var highCount = riskCounts.GetValueOrDefault("High", 0);
        var medCount = riskCounts.GetValueOrDefault("Medium", 0);
        var lowCount = riskCounts.GetValueOrDefault("Low", 0);
        var noneCount = riskCounts.GetValueOrDefault("None", 0);

        // Critical conflicts alert
        if (data.DangerZone.Any())
        {
            body.AppendLine(@"<div class=""card"" style=""margin-bottom: 1.5rem; border-color: var(--danger);"">");
            body.AppendLine(@"<h3 style=""color: var(--danger); margin-bottom: 0.75rem;"">🚨 Critical Conflicts</h3>");
            body.AppendLine(@"<p style=""margin-bottom: 1rem;"">These entities are removed by one mod but needed by C# code from another mod:</p>");
            body.AppendLine(@"<table class=""data-table"">");
            body.AppendLine(@"<thead><tr><th>Entity</th><th>Removed By</th><th>Needed By</th></tr></thead>");
            body.AppendLine(@"<tbody>");
            foreach (var (type, name, removedBy, dependedBy) in data.DangerZone)
            {
                body.AppendLine($@"<tr>
                    <td>{SharedAssets.EntityLink(type, name)}</td>
                    <td>{SharedAssets.ModLink(removedBy)}</td>
                    <td>{SharedAssets.ModLink(dependedBy)}</td>
                </tr>");
            }
            body.AppendLine(@"</tbody></table>");
            body.AppendLine(@"</div>");
        }

        // Severity tabs
        body.AppendLine(@"<div class=""severity-tabs"" id=""severity-tabs"">");
        body.AppendLine($@"<button class=""severity-tab active"" data-severity="""" onclick=""filterBySeverity('')"">ALL<span class=""count"">({data.ContestedEntities.Count})</span></button>");
        body.AppendLine($@"<button class=""severity-tab"" data-severity=""High"" onclick=""filterBySeverity('High')"" style=""border-color: var(--severity-high);"">HIGH<span class=""count"">({highCount})</span></button>");
        body.AppendLine($@"<button class=""severity-tab"" data-severity=""Medium"" onclick=""filterBySeverity('Medium')"" style=""border-color: var(--severity-medium);"">MEDIUM<span class=""count"">({medCount})</span></button>");
        body.AppendLine($@"<button class=""severity-tab"" data-severity=""Low"" onclick=""filterBySeverity('Low')"" style=""border-color: var(--severity-low);"">LOW<span class=""count"">({lowCount})</span></button>");
        if (noneCount > 0)
            body.AppendLine($@"<button class=""severity-tab"" data-severity=""None"" onclick=""filterBySeverity('None')"">NONE<span class=""count"">({noneCount})</span></button>");
        body.AppendLine(@"</div>");

        // Search filter with fuzzy toggle
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""conflict-search"" placeholder=""Search entities or mods..."" oninput=""filterConflicts()"">");
        body.AppendLine(@"  <label class=""fuzzy-toggle""><input type=""checkbox"" id=""fuzzy-toggle"" checked onchange=""filterConflicts()""> Fuzzy</label>");
        body.AppendLine(@"</div>");

        // Results container
        body.AppendLine(@"<div id=""conflict-results""></div>");

        // Property conflicts section
        if (data.PropertyConflicts.Any())
        {
            body.AppendLine(@"<div style=""margin-top: 2rem;"">");
            body.AppendLine(@"<h2 style=""margin-bottom: 1rem;"">Load Order Sensitive Properties</h2>");
            body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">These properties are set by multiple mods. The last mod in load order wins.</p>");
            body.AppendLine(@"<div id=""property-conflicts""></div>");
            body.AppendLine(@"</div>");
        }

        // Generate conflict data as JSON
        var conflictJson = GenerateConflictJson(data.ContestedEntities);
        var propConflictJson = GeneratePropertyConflictJson(data.PropertyConflicts);

        var script = GenerateScript(conflictJson, propConflictJson);

        return SharedAssets.WrapPage("Conflicts", "conflicts.html", body.ToString(), script);
    }

    private static string GenerateScript(string conflictJson, string propConflictJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SharedAssets.GenerateInlineDataScript("CONFLICT_DATA", conflictJson));
        sb.AppendLine(SharedAssets.GenerateInlineDataScript("PROP_CONFLICT_DATA", propConflictJson));

        sb.AppendLine(@"
let currentSeverity = '';

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}

function filterBySeverity(severity) {
  currentSeverity = severity;

  // Update tab active state
  document.querySelectorAll('.severity-tab').forEach(tab => {
    tab.classList.toggle('active', tab.dataset.severity === severity);
  });

  filterConflicts();
}

function filterConflicts() {
  const query = document.getElementById('conflict-search').value;
  const useFuzzy = document.getElementById('fuzzy-toggle').checked;

  let filtered = CONFLICT_DATA.filter(c => {
    if (currentSeverity && c.riskLevel !== currentSeverity) return false;
    return true;
  });

  if (query) {
    if (useFuzzy) {
      filtered = filtered
        .map(c => ({ conflict: c, score: fuzzyScore(query, c.entityName) }))
        .filter(x => x.score > 0)
        .sort((a, b) => b.score - a.score)
        .map(x => x.conflict);
    } else {
      const q = query.toLowerCase();
      filtered = filtered.filter(c => {
        const searchText = c.entityName + ' ' + c.entityType + ' ' + c.modActions.map(a => a.modName).join(' ');
        return searchText.toLowerCase().includes(q);
      });
    }
  }

  renderConflicts(filtered);
}

function renderConflicts(conflicts) {
  const container = document.getElementById('conflict-results');

  if (conflicts.length === 0) {
    container.innerHTML = '<p class=""text-muted"">No conflicts match the current filters.</p>';
    return;
  }

  let html = '';
  conflicts.forEach((c, idx) => {
    html += '<details>';
    html += '<summary>';
    html += getSeverityBadge(c.riskLevel);
    html += '<span class=""tag tag-type"">' + escapeHtml(c.entityType) + '</span>';
    html += '<span style=""flex: 1;"">' + escapeHtml(c.entityName) + '</span>';
    html += '<span class=""text-dim"" style=""font-size: 12px;"">' + c.modActions.length + ' mods</span>';
    html += '</summary>';
    html += '<div class=""details-body compact"">';
    html += '<p class=""text-muted compact-margin"">' + escapeHtml(c.riskReason) + '</p>';

    // Group actions by property
    const grouped = {};
    c.modActions.forEach(a => {
      const key = a.propertyName || a.operation || 'other';
      if (!grouped[key]) grouped[key] = [];
      grouped[key].push(a);
    });

    // Render grouped
    Object.keys(grouped).forEach(propKey => {
      const actions = grouped[propKey];
      html += '<div class=""conflict-group"">';
      html += '<div class=""group-header""><code>' + escapeHtml(propKey) + '</code> <span class=""text-dim"">(' + actions.length + ' mods)</span></div>';
      html += '<div class=""conflict-stack"">';
      actions.forEach(a => {
        html += '<div class=""conflict-item"">';
        html += '<div class=""conflict-item-header"">';
        html += '<span class=""tag ' + getOpClass(a.operation) + ' compact-tag"">' + escapeHtml(a.operation) + '</span>';
        html += '<a href=""mods.html?search=' + encodeURIComponent(a.modName) + '"" class=""mod-link"">' + escapeHtml(a.modName) + '</a>';
        html += '<code class=""source-ref"">' + escapeHtml(a.filePath || '') + ':' + (a.lineNumber || '') + '</code>';
        html += '</div>';
        if (a.xpath) html += '<pre class=""xpath-inline"">' + escapeHtml(a.xpath) + '</pre>';
        if (a.newValue) html += '<div class=""value-inline"">→ <code>' + escapeHtml(a.newValue) + '</code></div>';
        if (a.elementContent) html += '<details class=""xml-expand""><summary>XML</summary><pre class=""xml-inline"">' + escapeHtml(a.elementContent) + '</pre></details>';
        html += '</div>';
      });
      html += '</div></div>';
    });

    html += '<div class=""conflict-links"">';
    html += '<a href=""entities.html?search=' + encodeURIComponent(c.entityName) + '"">Entity details →</a>';
    html += '</div>';
    html += '</div></details>';
  });

  container.innerHTML = html;
}

function renderActionDetail(a) {
  let html = '<div class=""conflict-detail-compact"">';
  html += '<code class=""source-line"">' + escapeHtml(a.filePath || '') + (a.lineNumber ? ':' + a.lineNumber : '') + '</code>';
  if (a.xpath) html += '<pre class=""xpath-compact"">' + escapeHtml(a.xpath) + '</pre>';
  if (a.newValue) html += '<div class=""value-compact"">= <code>' + escapeHtml(a.newValue) + '</code></div>';
  if (a.elementContent) html += '<pre class=""xml-compact"">' + escapeHtml(a.elementContent) + '</pre>';
  if (!a.xpath && !a.newValue && !a.elementContent) html += '<span class=""text-dim"">No detail</span>';
  html += '</div>';
  return html;
}

function toggleActionDetail(btn, conflictIdx, actionIdx) {
  const row = document.getElementById('action-detail-' + conflictIdx + '-' + actionIdx);
  if (row.style.display === 'none') {
    row.style.display = 'table-row';
    btn.textContent = 'Hide Code';
  } else {
    row.style.display = 'none';
    btn.textContent = 'View Code';
  }
}

function getSeverityBadge(level) {
  const cls = level === 'High' ? 'tag-high' : level === 'Medium' ? 'tag-medium' : level === 'Low' ? 'tag-low' : 'tag-info';
  return '<span class=""tag ' + cls + '"">' + level + '</span>';
}

function getOpClass(op) {
  if (op === 'remove') return 'tag-high';
  if (op === 'set' || op === 'setattribute') return 'tag-medium';
  return 'tag-low';
}

function renderPropertyConflicts() {
  const container = document.getElementById('property-conflicts');
  if (!container || PROP_CONFLICT_DATA.length === 0) return;

  let html = '<table class=""data-table"">';
  html += '<thead><tr><th>Entity</th><th>Property</th><th>Mods (in order)</th></tr></thead>';
  html += '<tbody>';

  PROP_CONFLICT_DATA.slice(0, 50).forEach(p => {
    html += '<tr>';
    html += '<td><a href=""entities.html?search=' + encodeURIComponent(p.entityName) + '"">';
    html += '<span class=""tag tag-type"">' + escapeHtml(p.entityType) + '</span> ' + escapeHtml(p.entityName) + '</a></td>';
    html += '<td><code>' + escapeHtml(p.propertyName) + '</code></td>';
    html += '<td class=""text-muted"" style=""font-size: 12px;"">';
    html += p.setters.map(s => '<a href=""mods.html?search=' + encodeURIComponent(s.modName) + '"">' + escapeHtml(s.modName) + '</a>').join(' → ');
    html += '</td></tr>';
  });

  if (PROP_CONFLICT_DATA.length > 50) {
    html += '<tr><td colspan=""3"" class=""text-dim"">... and ' + (PROP_CONFLICT_DATA.length - 50) + ' more</td></tr>';
  }

  html += '</tbody></table>';
  container.innerHTML = html;
}

// Initialize
document.addEventListener('DOMContentLoaded', () => {
  filterConflicts();
  renderPropertyConflicts();
});
");
        return sb.ToString();
    }

    private static string GenerateConflictJson(List<ContestedEntity> conflicts)
    {
        var items = conflicts.Select(c => new
        {
            entityType = c.EntityType,
            entityName = c.EntityName,
            riskLevel = c.RiskLevel,
            riskReason = c.RiskReason,
            modActions = c.ModActions.Select(a => new
            {
                modName = a.ModName,
                operation = a.Operation,
                xpath = a.XPath,
                propertyName = a.PropertyName,
                newValue = a.NewValue,
                elementContent = a.ElementContent,
                filePath = a.FilePath,
                lineNumber = a.LineNumber
            })
        });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
    }

    private static string GeneratePropertyConflictJson(List<PropertyConflict> conflicts)
    {
        var items = conflicts.Select(c => new
        {
            entityType = c.EntityType,
            entityName = c.EntityName,
            propertyName = c.PropertyName,
            setters = c.Setters.Select(s => new { modName = s.ModName, value = s.Value })
        });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
    }
}
