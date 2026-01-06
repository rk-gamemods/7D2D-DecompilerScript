using System.Text;
using System.Text.Json;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the entity explorer page (entities.html) with fuzzy search and type filtering.
/// </summary>
public static class EntityPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData)
    {
        var body = new StringBuilder();

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Entity Explorer</h1>");
        body.AppendLine($@"  <p>Browse and search all {data.TotalDefinitions:N0} game entities</p>");
        body.AppendLine(@"</div>");

        // Stats bar with type counts
        body.AppendLine(@"<div class=""stats-bar"">");
        foreach (var (type, count) in data.DefinitionsByType.Take(8))
        {
            body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{count:N0}</span><span class=""stat-label"">{type}s</span></div>");
        }
        if (data.DefinitionsByType.Count > 8)
        {
            var otherCount = data.DefinitionsByType.Skip(8).Sum(kv => kv.Value);
            body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{otherCount:N0}</span><span class=""stat-label"">other</span></div>");
        }
        body.AppendLine(@"</div>");

        // Filter bar with fuzzy toggle
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""entity-search"" placeholder=""Search entities..."" oninput=""filterEntities()"">");
        body.AppendLine(@"  <select class=""filter-select"" id=""type-filter"" onchange=""filterEntities()"">");
        body.AppendLine(@"    <option value="""">All Types</option>");
        foreach (var (type, count) in data.DefinitionsByType)
        {
            body.AppendLine($@"    <option value=""{SharedAssets.HtmlEncode(type)}"">{type} ({count:N0})</option>");
        }
        body.AppendLine(@"  </select>");
        body.AppendLine(@"  <label class=""fuzzy-toggle""><input type=""checkbox"" id=""fuzzy-toggle"" checked onchange=""filterEntities()""> Fuzzy</label>");
        body.AppendLine(@"</div>");

        // Results container
        body.AppendLine(@"<div id=""entity-results""></div>");

        // Show more button
        body.AppendLine(@"<button class=""show-more"" id=""show-more-btn"" onclick=""showMore()"" style=""display: none;"">Show More</button>");

        // Entity detail panel (hidden by default)
        body.AppendLine(@"<div class=""entity-panel"" id=""entity-panel"" style=""display: none;""></div>");

        // Generate entity data as JSON
        var entityJson = GenerateEntityJson(extData.AllEntities);
        var refJson = GenerateReferenceJson(extData.AllReferences);

        var script = GenerateScript(entityJson, refJson);

        return SharedAssets.WrapPage("Entities", "entities.html", body.ToString(), script);
    }

    private static string GenerateScript(string entityJson, string refJson)
    {
        var sb = new StringBuilder();

        sb.AppendLine(SharedAssets.GenerateInlineDataScript("ENTITY_DATA", entityJson));
        sb.AppendLine(SharedAssets.GenerateInlineDataScript("REF_DATA", refJson));

        sb.AppendLine(@"
let filteredEntities = [];
let displayCount = 20;
let propsExpanded = false;

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}

function safeId(str) {
  if (!str) return '';
  return str.replace(/[^a-zA-Z0-9]/g, '_');
}

function filterEntities() {
  const query = document.getElementById('entity-search').value;
  const typeFilter = document.getElementById('type-filter').value;
  const useFuzzy = document.getElementById('fuzzy-toggle').checked;

  filteredEntities = ENTITY_DATA.filter(e => {
    if (typeFilter && e.type !== typeFilter) return false;
    return true;
  });

  if (query) {
    if (useFuzzy) {
      filteredEntities = filteredEntities
        .map(e => ({ entity: e, score: fuzzyScore(query, e.name) }))
        .filter(x => x.score > 0)
        .sort((a, b) => b.score - a.score)
        .map(x => x.entity);
    } else {
      const q = query.toLowerCase();
      filteredEntities = filteredEntities.filter(e => e.name.toLowerCase().includes(q));
    }
  }

  displayCount = 20;
  renderEntities();
}

function renderEntities() {
  const container = document.getElementById('entity-results');
  const showBtn = document.getElementById('show-more-btn');
  const toShow = filteredEntities.slice(0, displayCount);

  let html = '<table class=""data-table""><thead><tr>';
  html += '<th>Type</th><th>Name</th><th>Extends</th><th>Props</th><th>Refs</th>';
  html += '</tr></thead><tbody>';

  toShow.forEach(e => {
    html += '<tr onclick=""showEntity(\'' + escapeHtml(e.type) + '\', \'' + escapeHtml(e.name) + '\')""';
    html += ' id=""entity-' + safeId(e.type) + '-' + safeId(e.name) + '"">';
    html += '<td><span class=""tag tag-type"">' + escapeHtml(e.type) + '</span></td>';
    html += '<td>' + escapeHtml(e.name) + '</td>';
    html += '<td class=""text-muted"">' + (e.extends ? escapeHtml(e.extends) : '-') + '</td>';
    html += '<td>' + (e.props || 0) + '</td>';
    html += '<td>' + (e.refs || 0) + '</td>';
    html += '</tr>';
  });

  html += '</tbody></table>';
  html += '<p class=""text-muted"" style=""margin-top: 0.5rem; font-size: 12px;"">Showing ' + toShow.length + ' of ' + filteredEntities.length + ' entities</p>';

  container.innerHTML = html;

  if (filteredEntities.length > displayCount) {
    showBtn.style.display = 'inline-block';
    showBtn.textContent = 'Show More (' + (filteredEntities.length - displayCount) + ' remaining)';
  } else {
    showBtn.style.display = 'none';
  }
}

function showMore() {
  displayCount += 50;
  renderEntities();
}

function toggleProps() {
  propsExpanded = !propsExpanded;
  const list = document.getElementById('props-list');
  const btn = document.getElementById('props-toggle-btn');
  const entity = window.currentEntity;
  if (!entity || !entity.properties) return;

  if (propsExpanded) {
    renderPropsTable(entity.properties, list);
    btn.textContent = 'Show Less';
  } else {
    renderPropsTable(entity.properties.slice(0, 20), list);
    btn.textContent = 'Show All (' + entity.properties.length + ')';
  }
}

function renderPropsTable(props, container) {
  let html = '<table class=""data-table""><thead><tr><th>Name</th><th>Value</th><th>Class</th></tr></thead><tbody>';
  props.forEach(p => {
    html += '<tr><td>' + escapeHtml(p.name) + '</td>';
    html += '<td class=""text-muted"">' + (p.value ? escapeHtml(p.value) : '-') + '</td>';
    html += '<td class=""text-dim"">' + (p.class ? escapeHtml(p.class) : '') + '</td></tr>';
  });
  html += '</tbody></table>';
  container.innerHTML = html;
}

function showEntity(type, name) {
  const panel = document.getElementById('entity-panel');
  const entity = ENTITY_DATA.find(e => e.type === type && e.name === name);

  if (!entity) {
    panel.style.display = 'none';
    return;
  }

  window.currentEntity = entity;
  propsExpanded = false;

  const incoming = REF_DATA.filter(r => r.targetType === type && r.targetName === name);
  const outgoing = REF_DATA.filter(r => r.sourceType === type && r.sourceName === name);

  let html = '<h3><span class=""tag tag-type"">' + escapeHtml(type) + '</span> ' + escapeHtml(name) + '</h3>';
  html += '<div class=""meta"">';
  if (entity.file) html += '📁 ' + escapeHtml(entity.file) + ':' + (entity.line || '');
  if (entity.extends) html += ' • Extends: <a href=""?search=' + encodeURIComponent(entity.extends) + '"">' + escapeHtml(entity.extends) + '</a>';
  html += '</div>';

  // Properties section with expand option
  if (entity.properties && entity.properties.length > 0) {
    html += '<div class=""section""><div class=""section-title"" style=""display: flex; justify-content: space-between; align-items: center;"">';
    html += '<span>Properties (' + entity.properties.length + ')</span>';
    if (entity.properties.length > 20) {
      html += '<button class=""show-more"" id=""props-toggle-btn"" onclick=""toggleProps()"" style=""padding: 0.25rem 0.5rem; font-size: 11px;"">Show All (' + entity.properties.length + ')</button>';
    }
    html += '</div>';
    html += '<div id=""props-list""></div></div>';
  }

  // Incoming References section
  html += '<div class=""section""><div class=""section-title"" style=""display: flex; justify-content: space-between; align-items: center;"">';
  html += '<span>Incoming References (' + incoming.length + ')</span>';
  html += '<a href=""dependencies.html?search=' + encodeURIComponent(name) + '"" style=""font-size: 11px;"">View full chain →</a>';
  html += '</div>';
  if (incoming.length > 0) {
    html += '<div style=""max-height: 200px; overflow-y: auto;"">';
    incoming.slice(0, 20).forEach(r => {
      html += '<div class=""chain-step""><span class=""chain-arrow"">←</span>';
      html += '<a href=""?search=' + encodeURIComponent(r.sourceName) + '"">';
      html += '<span class=""tag tag-type"">' + escapeHtml(r.sourceType) + '</span> ' + escapeHtml(r.sourceName) + '</a>';
      html += '<span class=""chain-via"">' + escapeHtml(r.context) + '</span></div>';
    });
    if (incoming.length > 20) html += '<p class=""text-dim"">... and ' + (incoming.length - 20) + ' more</p>';
    html += '</div>';
  } else {
    html += '<p class=""text-dim"">No entities reference this</p>';
  }
  html += '</div>';

  // Outgoing References section
  html += '<div class=""section""><div class=""section-title"" style=""display: flex; justify-content: space-between; align-items: center;"">';
  html += '<span>Outgoing References (' + outgoing.length + ')</span>';
  html += '<a href=""dependencies.html?search=' + encodeURIComponent(name) + '"" style=""font-size: 11px;"">View full chain →</a>';
  html += '</div>';
  if (outgoing.length > 0) {
    html += '<div style=""max-height: 200px; overflow-y: auto;"">';
    outgoing.slice(0, 20).forEach(r => {
      html += '<div class=""chain-step""><span class=""chain-arrow"">→</span>';
      html += '<a href=""?search=' + encodeURIComponent(r.targetName) + '"">';
      html += '<span class=""tag tag-type"">' + escapeHtml(r.targetType) + '</span> ' + escapeHtml(r.targetName) + '</a>';
      html += '<span class=""chain-via"">' + escapeHtml(r.context) + '</span></div>';
    });
    if (outgoing.length > 20) html += '<p class=""text-dim"">... and ' + (outgoing.length - 20) + ' more</p>';
    html += '</div>';
  } else {
    html += '<p class=""text-dim"">This entity references nothing</p>';
  }
  html += '</div>';

  panel.style.display = 'block';
  panel.innerHTML = html;

  // Render initial properties (first 20)
  if (entity.properties && entity.properties.length > 0) {
    const propsList = document.getElementById('props-list');
    renderPropsTable(entity.properties.slice(0, 20), propsList);
  }

  panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

document.addEventListener('DOMContentLoaded', () => {
  filterEntities();
  const params = new URLSearchParams(window.location.search);
  const searchParam = params.get('search');
  if (searchParam) {
    document.getElementById('entity-search').value = searchParam;
    filterEntities();
  }
});
");
        return sb.ToString();
    }

    private static string GenerateEntityJson(List<EntityExport> entities)
    {
        var items = entities.Select(e => new
        {
            type = e.Type,
            name = e.Name,
            file = e.FilePath,
            line = e.Line,
            extends = e.Extends,
            props = e.PropertyCount,
            refs = e.ReferenceCount,
            properties = e.Properties?.Select(p => new { name = p.Name, value = p.Value, @class = p.Class })
        });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
    }

    private static string GenerateReferenceJson(List<ReferenceExport> refs)
    {
        var items = refs.Select(r => new
        {
            sourceType = r.SourceType,
            sourceName = r.SourceName,
            targetType = r.TargetType,
            targetName = r.TargetName,
            context = r.Context
        });
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
    }
}
