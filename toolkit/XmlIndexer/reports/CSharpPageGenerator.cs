using System.Text;
using System.Text.Json;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the C# analysis page (csharp.html) with Harmony patches, class extensions, and call graphs.
/// </summary>
public static class CSharpPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData)
    {
        var body = new StringBuilder();

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>C# Analysis</h1>");
        body.AppendLine(@"  <p>Harmony patches, class extensions, and C# mod dependencies</p>");
        body.AppendLine(@"</div>");

        // Stats bar
        body.AppendLine(@"<div class=""stats-bar"">");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.HarmonyPatches.Count}</span><span class=""stat-label"">Harmony Patches</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.ClassExtensions.Count}</span><span class=""stat-label"">Class Extensions</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.CSharpMods + data.HybridMods}</span><span class=""stat-label"">C# Mods</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"">{data.CSharpByType.Values.Sum()}</span><span class=""stat-label"">Total Dependencies</span></div>");
        body.AppendLine(@"</div>");

        // C# dependency type breakdown
        if (data.CSharpByType.Any())
        {
            body.AppendLine(@"<div class=""card"" style=""margin-bottom: 1.5rem;"">");
            body.AppendLine(@"<h3 style=""margin-bottom: 1rem;"">Dependency Types</h3>");
            body.AppendLine(@"<div class=""card-grid"" style=""grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));"">");
            foreach (var (type, count) in data.CSharpByType.OrderByDescending(kv => kv.Value).Take(12))
            {
                body.AppendLine($@"<div style=""padding: 0.5rem;"">");
                body.AppendLine($@"<div style=""font-size: 1.1rem; font-weight: 600; color: var(--accent);"">{count}</div>");
                body.AppendLine($@"<div class=""text-muted"" style=""font-size: 12px;"">{FormatCSharpDepType(type)}</div>");
                body.AppendLine(@"</div>");
            }
            body.AppendLine(@"</div>");
            body.AppendLine(@"</div>");
        }

        // Filter bar
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""csharp-search"" placeholder=""Search classes, methods, or mods..."" oninput=""filterCSharp()"">");
        body.AppendLine(@"</div>");

        // Harmony patches section
        body.AppendLine(@"<div style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<h2 style=""margin-bottom: 1rem;"">🔧 Harmony Patches</h2>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Mods that hook into game code using Harmony library.</p>");

        if (data.HarmonyPatches.Any())
        {
            // Group patches by target class
            var patchesByClass = data.HarmonyPatches
                .GroupBy(p => p.ClassName)
                .OrderByDescending(g => g.Count())
                .ToList();

            body.AppendLine(@"<div id=""harmony-patches"">");
            foreach (var classGroup in patchesByClass.Take(15))
            {
                body.AppendLine(@"<details>");
                body.AppendLine($@"<summary>");
                body.AppendLine($@"<code style=""flex: 1;"">{SharedAssets.HtmlEncode(classGroup.Key)}</code>");
                body.AppendLine($@"<span class=""text-dim"" style=""font-size: 12px;"">{classGroup.Count()} patches</span>");
                body.AppendLine(@"</summary>");
                body.AppendLine(@"<div class=""details-body"">");
                body.AppendLine(@"<table class=""data-table"">");
                body.AppendLine(@"<thead><tr><th>Mod</th><th>Method</th><th>Type</th></tr></thead>");
                body.AppendLine(@"<tbody>");
                foreach (var patch in classGroup)
                {
                    var patchClass = patch.PatchType switch
                    {
                        "Prefix" => "tag-medium",
                        "Postfix" => "tag-low",
                        "Transpiler" => "tag-high",
                        _ => "tag-info"
                    };
                    body.AppendLine($@"<tr>");
                    body.AppendLine($@"<td><a href=""mods.html?search={SharedAssets.UrlEncode(patch.ModName)}"">{SharedAssets.HtmlEncode(patch.ModName)}</a></td>");
                    body.AppendLine($@"<td><code>{SharedAssets.HtmlEncode(patch.MethodName)}</code></td>");
                    body.AppendLine($@"<td><span class=""tag {patchClass}"">{patch.PatchType}</span></td>");
                    body.AppendLine(@"</tr>");
                }
                body.AppendLine(@"</tbody></table>");
                body.AppendLine(@"</div></details>");
            }
            if (patchesByClass.Count > 15)
                body.AppendLine($@"<p class=""text-muted"" style=""margin-top: 0.5rem;"">... and {patchesByClass.Count - 15} more target classes</p>");
            body.AppendLine(@"</div>");
        }
        else
        {
            body.AppendLine(@"<p class=""text-muted"">No Harmony patches detected.</p>");
        }
        body.AppendLine(@"</div>");

        // Class extensions section
        body.AppendLine(@"<div style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<h2 style=""margin-bottom: 1rem;"">📦 Class Extensions</h2>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Mods that extend game classes (ItemAction, Block, Entity, etc.).</p>");

        if (data.ClassExtensions.Any())
        {
            // Group by base class
            var extByBase = data.ClassExtensions
                .GroupBy(e => e.BaseClass)
                .OrderByDescending(g => g.Count())
                .ToList();

            body.AppendLine(@"<div id=""class-extensions"">");
            foreach (var baseGroup in extByBase)
            {
                body.AppendLine(@"<details>");
                body.AppendLine($@"<summary>");
                body.AppendLine($@"<span class=""tag tag-type"">{SharedAssets.HtmlEncode(baseGroup.Key)}</span>");
                body.AppendLine($@"<span style=""flex: 1; margin-left: 0.5rem;"">{baseGroup.Count()} extensions</span>");
                body.AppendLine(@"</summary>");
                body.AppendLine(@"<div class=""details-body"">");
                body.AppendLine(@"<table class=""data-table"">");
                body.AppendLine(@"<thead><tr><th>Mod</th><th>Class</th></tr></thead>");
                body.AppendLine(@"<tbody>");
                foreach (var ext in baseGroup)
                {
                    body.AppendLine($@"<tr>");
                    body.AppendLine($@"<td><a href=""mods.html?search={SharedAssets.UrlEncode(ext.ModName)}"">{SharedAssets.HtmlEncode(ext.ModName)}</a></td>");
                    body.AppendLine($@"<td><code>{SharedAssets.HtmlEncode(ext.ChildClass)}</code></td>");
                    body.AppendLine(@"</tr>");
                }
                body.AppendLine(@"</tbody></table>");
                body.AppendLine(@"</div></details>");
            }
            body.AppendLine(@"</div>");
        }
        else
        {
            body.AppendLine(@"<p class=""text-muted"">No class extensions detected.</p>");
        }
        body.AppendLine(@"</div>");

        // Mods with C# dependencies table
        body.AppendLine(@"<div style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<h2 style=""margin-bottom: 1rem;"">C# Mods Overview</h2>");

        var csharpMods = data.ModSummary
            .Where(m => m.ModType == "C# Code" || m.ModType == "Hybrid")
            .OrderByDescending(m => m.CSharpDeps)
            .ToList();

        if (csharpMods.Any())
        {
            body.AppendLine(@"<table class=""data-table"">");
            body.AppendLine(@"<thead><tr><th>Mod</th><th>Type</th><th>C# Deps</th><th>XML Ops</th><th>Health</th></tr></thead>");
            body.AppendLine(@"<tbody>");
            foreach (var mod in csharpMods)
            {
                body.AppendLine($@"<tr onclick=""location.href='mods.html?search={SharedAssets.UrlEncode(mod.Name)}'"" style=""cursor: pointer;"">");
                body.AppendLine($@"<td>{SharedAssets.HtmlEncode(mod.Name)}</td>");
                body.AppendLine($@"<td><span class=""tag tag-info"">{mod.ModType}</span></td>");
                body.AppendLine($@"<td>{mod.CSharpDeps}</td>");
                body.AppendLine($@"<td>{mod.XmlOps}</td>");
                body.AppendLine($@"<td>{SharedAssets.HealthBadge(mod.Health)}</td>");
                body.AppendLine(@"</tr>");
            }
            body.AppendLine(@"</tbody></table>");
        }
        body.AppendLine(@"</div>");

        // Call graph section (if available)
        if (extData.CallGraphNodes.Any())
        {
            body.AppendLine(@"<div style=""margin-bottom: 2rem;"">");
            body.AppendLine(@"<h2 style=""margin-bottom: 1rem;"">📊 Call Graph</h2>");
            body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Method call relationships between mod code and game code.</p>");
            body.AppendLine(@"<div id=""call-graph""></div>");
            body.AppendLine(@"</div>");
        }

        // Event flow section (if available)
        if (extData.EventFlowData.Any())
        {
            body.AppendLine(@"<div style=""margin-bottom: 2rem;"">");
            body.AppendLine(@"<h2 style=""margin-bottom: 1rem;"">🎯 Event Flow</h2>");
            body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Event subscriptions and triggers.</p>");
            body.AppendLine(@"<table class=""data-table"">");
            body.AppendLine(@"<thead><tr><th>Event</th><th>Subscribers</th><th>Triggers</th></tr></thead>");
            body.AppendLine(@"<tbody>");
            foreach (var evt in extData.EventFlowData.Take(20))
            {
                body.AppendLine($@"<tr>");
                body.AppendLine($@"<td><code>{SharedAssets.HtmlEncode(evt.EventName)}</code></td>");
                body.AppendLine($@"<td>{evt.SubscriberCount}</td>");
                body.AppendLine($@"<td>{evt.TriggerCount}</td>");
                body.AppendLine(@"</tr>");
            }
            body.AppendLine(@"</tbody></table>");
            body.AppendLine(@"</div>");
        }

        var script = @"
function filterCSharp() {
  const query = document.getElementById('csharp-search').value.toLowerCase();

  // Filter harmony patches
  document.querySelectorAll('#harmony-patches details').forEach(detail => {
    const text = detail.textContent.toLowerCase();
    detail.style.display = text.includes(query) ? '' : 'none';
  });

  // Filter class extensions
  document.querySelectorAll('#class-extensions details').forEach(detail => {
    const text = detail.textContent.toLowerCase();
    detail.style.display = text.includes(query) ? '' : 'none';
  });
}
";

        return SharedAssets.WrapPage("C# Analysis", "csharp.html", body.ToString(), script);
    }

    private static string FormatCSharpDepType(string type) => type switch
    {
        "harmony_class" => "Harmony: Target Class",
        "harmony_method" => "Harmony: Target Method",
        "harmony_prefix" => "Harmony: Prefix Patch",
        "harmony_postfix" => "Harmony: Postfix Patch",
        "harmony_transpiler" => "Harmony: Transpiler",
        "extends_itemaction" => "Extends ItemAction",
        "extends_block" => "Extends Block",
        "extends_entity" => "Extends Entity",
        "extends_mineventaction" => "Extends MinEventAction",
        "implements_imodapi" => "Implements IModApi",
        "item" => "Item Lookup",
        "block" => "Block Lookup",
        "entity_class" => "Entity Lookup",
        "buff" => "Buff Lookup",
        "sound" => "Sound Lookup",
        "recipe" => "Recipe Lookup",
        "quest" => "Quest Lookup",
        "localization" => "Localization Key",
        _ => type.Replace("_", " ")
    };
}
