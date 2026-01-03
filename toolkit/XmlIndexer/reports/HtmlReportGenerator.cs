using System.Text;
using System.Web;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates HTML reports for mod ecosystem analysis.
/// </summary>
public static class HtmlReportGenerator
{
    public static void Generate(string path, ReportData data)
    {
        var html = GenerateFullHtml(data);
        File.WriteAllText(path, html);
    }

    private static string GenerateFullHtml(ReportData data)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"" data-theme=""obsidian"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>7D2D Mod Ecosystem Report</title>
    <style>
{GenerateCss()}
    </style>
</head>
<body>
<div class=""wrap"">
{GenerateNav(data)}
<main>
    {GenerateHeader()}
    {GenerateStatsBar(data)}
    {GenerateDefinitionsByType(data)}
    {GenerateXmlOperations(data)}
    {GenerateCSharpAnalysis(data)}
    {GenerateModsSection(data)}
    {GenerateHealthSection(data)}
    {GeneratePropertyConflicts(data.PropertyConflicts)}
    {GenerateContestedEntities(data.ContestedEntities)}
    <footer>7D2D Mod Ecosystem Analyzer</footer>
</main>
<aside class=""side"" id=""dashboard"">
    {GenerateDashboard(data)}
</aside>
</div>
</body>
</html>";
    }

    private static string GenerateCss()
    {
        return @"        :root { --s1: 2px; --s2: 4px; --s3: 6px; --s4: 10px; --s5: 14px; --fs-xs: 10px; --fs-sm: 11px; --fs-base: 12px; --fs-md: 13px; }
        /* Obsidian: Deep black with emerald/amber accents */
        [data-theme=""obsidian""] { --bg: #0a0a0c; --bg2: #121215; --card: #1a1a1f; --border: #2d2d35; --text: #f4f4f6; --muted: #c8c8d0; --dim: #888899; --green: #10b981; --yellow: #f59e0b; --red: #ef4444; --cyan: #6ee7b7; --purple: #a78bfa; }
        /* Graphite: Dark grey with purple/violet accents */
        [data-theme=""slate""] { --bg: #18181b; --bg2: #1f1f23; --card: #27272a; --border: #3f3f46; --text: #fafafa; --muted: #d4d4d8; --dim: #a1a1aa; --green: #a855f7; --yellow: #e879f9; --red: #f472b6; --cyan: #c084fc; --purple: #a78bfa; }
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Segoe UI', -apple-system, sans-serif; font-size: var(--fs-base); background: var(--bg); color: var(--text); line-height: 1.4; }
        [data-theme=""obsidian""], [data-theme=""slate""] { color-scheme: dark; }
        a { color: var(--cyan); }
        
        /* Three-column layout: nav + main + dashboard */
        .wrap { display: grid; grid-template-columns: 80px 1fr 30%; min-height: 100vh; }
        nav { background: var(--bg2); border-right: 1px solid var(--border); padding: var(--s3); position: sticky; top: 0; height: 100vh; font-size: var(--fs-xs); }
        nav a { display: block; padding: var(--s2) var(--s3); color: var(--muted); text-decoration: none; border-radius: 2px; }
        nav a:hover { background: var(--card); color: var(--text); }
        nav .t { font-size: var(--fs-xs); text-transform: uppercase; color: var(--dim); margin: var(--s4) 0 var(--s2); }
        nav select { width: 100%; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 2px; padding: var(--s2); font-size: var(--fs-xs); margin-top: var(--s5); }
        main { padding: var(--s4); }
        aside.side { background: var(--bg2); border-left: 1px solid var(--border); padding: var(--s4); position: sticky; top: 0; height: 100vh; overflow: auto; }
        .side .t { font-size: var(--fs-xs); text-transform: uppercase; color: var(--dim); margin-bottom: var(--s2); }
        .panel { border: 1px solid var(--border); background: transparent; border-radius: 2px; padding: var(--s3); }
        .panel + .panel { margin-top: var(--s3); }
        
        /* Header */
        .hdr { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: var(--s3); margin-bottom: var(--s4); }
        .hdr h1 { font-size: var(--fs-md); font-weight: 600; }
        .hdr span { font-size: var(--fs-xs); color: var(--dim); }
        
        /* Stat row */
        .stats { display: flex; gap: var(--s3); flex-wrap: wrap; font-size: var(--fs-sm); margin-bottom: var(--s4); padding-bottom: var(--s3); border-bottom: 1px solid var(--border); }
        .stats > div { display: inline-flex; gap: 4px; align-items: baseline; }
        .stats b { color: var(--cyan); margin-right: 2px; }
        .stats span { color: var(--dim); }
        
        /* Unified details/collapsible */
        details { margin-bottom: var(--s2); }
        summary { cursor: pointer; padding: var(--s2) var(--s3); background: var(--card); border: 1px solid var(--border); border-radius: 2px; font-size: var(--fs-sm); list-style: none; display: flex; align-items: center; gap: var(--s3); }
        summary::-webkit-details-marker { display: none; }
        summary::before { content: '+'; color: var(--dim); font-size: 10px; font-family: monospace; }
        details[open] > summary::before { content: '-'; }
        details[open] > summary { border-radius: 2px 2px 0 0; background: var(--card); }
        summary:hover { background: var(--border); }
        .body { border: 1px solid var(--border); border-top: none; border-radius: 0 0 2px 2px; padding: var(--s3); background: var(--bg2); font-size: var(--fs-sm); }
        summary:focus-visible { outline: 2px solid var(--cyan); outline-offset: 1px; }
        
        /* Tables */
        table { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
        th, td { padding: var(--s2) var(--s3); text-align: left; border-bottom: 1px solid var(--border); }
        th { font-size: var(--fs-xs); text-transform: uppercase; color: var(--dim); font-weight: 500; background: var(--bg); }
        tr:hover { background: var(--card); }
        
        /* Badges */
        .tag { display: inline-block; padding: 0 4px; border-radius: 2px; font-size: var(--fs-xs); }
        .tag-y { background: rgba(209,154,34,0.15); color: var(--yellow); }
        .tag-p { background: rgba(188,140,255,0.15); color: var(--purple); }
        .tag-c { background: rgba(88,166,255,0.15); color: var(--cyan); }
        .ok { color: var(--green); } .warn { color: var(--yellow); } .err { color: var(--red); }
        
        /* Progress bar */
        .bar { display: inline-block; height: 3px; background: var(--cyan); border-radius: 1px; vertical-align: middle; }
        
        /* Alerts */
        .alert { padding: var(--s3); border-radius: 2px; margin-bottom: var(--s3); font-size: var(--fs-sm); border-left: 2px solid var(--green); background: rgba(63,185,80,0.08); }
        .alert.e { border-color: var(--red); background: rgba(248,81,73,0.08); }
        .alert b { display: block; margin-bottom: 2px; }
        
        /* Section header */
        .sec { font-size: var(--fs-sm); font-weight: 600; margin: var(--s4) 0 var(--s3); padding-bottom: var(--s2); border-bottom: 1px solid var(--border); display: flex; align-items: center; gap: var(--s3); }
        .sec .n { font-size: var(--fs-xs); color: var(--dim); background: var(--card); padding: 0 5px; border-radius: 8px; }
        
        /* Grid facts */
        .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: var(--s2); font-size: var(--fs-sm); }
        .grid > div { padding: var(--s2) var(--s3); border: 1px solid var(--border); background: transparent; border-radius: 2px; }

        /* Dashboard mini-bars */
        .bars { display: grid; gap: var(--s2); }
        .bars .r { display: grid; grid-template-columns: 54px 1fr 42px; gap: var(--s2); align-items: center; }
        .bars .k { color: var(--muted); }
        .bars .v { color: var(--dim); text-align: left; }
        .bars .b { height: 6px; background: var(--border); border-radius: 2px; overflow: hidden; }
        .bars .b i { display: block; height: 100%; background: var(--cyan); }
        
        /* Code */
        code { font-family: Consolas, monospace; font-size: var(--fs-xs); background: var(--border); padding: 0 3px; border-radius: 2px; }
        
        /* Inline list */
        .il { display: inline; padding: 0; margin: 0; }
        .il li { display: inline; }
        .il li::after { content: ' • '; color: var(--dim); }
        .il li:last-child::after { content: ''; }
        
        footer { margin-top: var(--s5); padding-top: var(--s3); border-top: 1px solid var(--border); font-size: var(--fs-xs); color: var(--dim); text-align: center; }
        @media (max-width: 980px) { .wrap { grid-template-columns: 1fr; } nav { display: none; } aside.side { position: relative; height: auto; border-left: none; border-top: 1px solid var(--border); } }";
    }

    private static string GenerateNav(ReportData data)
    {
        return $@"<nav>
    <div class=""t"">Sections</div>
    <a href=""#dashboard"">Dashboard</a>
    <a href=""#mods"">Mods ({data.TotalMods})</a>
    <a href=""#health"">Health</a>
    <a href=""#conflicts"">Conflicts</a>
    <select onchange=""document.documentElement.dataset.theme=this.value"">
        <option value=""obsidian"">Obsidian</option>
        <option value=""slate"">Graphite</option>
    </select>
</nav>";
    }

    private static string GenerateHeader()
    {
        return $@"<div class=""hdr""><h1>7D2D Mod Ecosystem</h1><span>{DateTime.Now:yyyy-MM-dd HH:mm}</span></div>";
    }

    private static string GenerateStatsBar(ReportData data)
    {
        return $@"<div class=""stats"" id=""stats"">
        <div><b>{data.TotalDefinitions:N0}</b><span>Game Definitions</span></div>
        <div><b>{data.TotalProperties:N0}</b><span>Properties</span></div>
        <div><b>{data.TotalReferences:N0}</b><span>Cross-References</span></div>
        <div><b>{data.TotalMods}</b><span>Mods</span></div>
        <div><b>{data.XmlMods}</b><span>XML-Only</span></div>
        <div><b>{data.CSharpMods}</b><span>C# Code</span></div>
        <div><b>{data.HybridMods}</b><span>Hybrid</span></div>
    </div>";
    }

    private static string GenerateDefinitionsByType(ReportData data)
    {
        var rows = string.Join("\n", data.DefinitionsByType.Select(kv =>
        {
            var pct = data.TotalDefinitions > 0 ? kv.Value * 100 / data.TotalDefinitions : 0;
            var barWidth = data.TotalDefinitions > 0 ? Math.Max(kv.Value * 80 / data.TotalDefinitions, 1) : 1;
            return $@"<tr><td>{kv.Key}</td><td>{kv.Value:N0}</td><td><span class=""bar"" style=""width:{barWidth}px""></span> {pct}%</td></tr>";
        }));

        return $@"<details>
        <summary>Definitions by Type <span class=""n"">{data.DefinitionsByType.Count}</span></summary>
        <div class=""body""><table><tr><th>Type</th><th>Count</th><th>%</th></tr>
        {rows}
        </table></div>
    </details>";
    }

    private static string GenerateXmlOperations(ReportData data)
    {
        var rows = string.Join("\n", data.OperationsByType.Select(kv => $"<tr><td>{kv.Key}</td><td>{kv.Value}</td></tr>"));
        return $@"<details>
        <summary>XML Operations <span class=""n"">{data.OperationsByType.Values.Sum()}</span></summary>
        <div class=""body""><table><tr><th>Op</th><th>Count</th></tr>
        {rows}
        </table></div>
    </details>";
    }

    private static string GenerateCSharpAnalysis(ReportData data)
    {
        var sb = new StringBuilder();
        
        if (data.CSharpByType.Any())
        {
            var rows = string.Join("\n", data.CSharpByType.Select(kv =>
                $"<tr><td>{FormatCSharpDepType(kv.Key)}</td><td>{kv.Value}</td></tr>"));
            sb.AppendLine($@"<table><tr><th>Type</th><th>Count</th></tr>{rows}</table>");
        }
        else
        {
            sb.AppendLine("<span style='color:var(--dim);'>None</span>");
        }

        if (data.HarmonyPatches.Any())
        {
            var patchRows = string.Join("\n", data.HarmonyPatches.Select(p =>
                $@"<tr><td>{p.ModName}</td><td><code>{p.ClassName}</code></td><td><code>{p.MethodName}</code></td><td><span class=""tag tag-p"">{p.PatchType}</span></td></tr>"));
            sb.AppendLine($@"<div style='margin-top:var(--s3);'><b style='color:var(--purple);font-size:var(--fs-xs);'>HARMONY PATCHES</b></div><table><tr><th>Mod</th><th>Class</th><th>Method</th><th>Type</th></tr>{patchRows}</table>");
        }

        if (data.ClassExtensions.Any())
        {
            var extRows = string.Join("\n", data.ClassExtensions.Select(e =>
                $@"<tr><td>{e.ModName}</td><td><span class=""tag tag-c"">{e.BaseClass}</span></td><td><code>{e.ChildClass}</code></td></tr>"));
            sb.AppendLine($@"<div style='margin-top:var(--s3);'><b style='color:var(--cyan);font-size:var(--fs-xs);'>CLASS EXTENSIONS</b></div><table><tr><th>Mod</th><th>Base</th><th>Class</th></tr>{extRows}</table>");
        }

        return $@"<details>
        <summary>C# Analysis <span class=""n"">{data.CSharpByType.Values.Sum()}</span></summary>
        <div class=""body"">
        {sb}
        </div>
    </details>";
    }

    private static string GenerateModsSection(ReportData data)
    {
        var behaviorLookup = data.ModBehaviors.ToDictionary(b => b.ModName, b => b, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        foreach (var m in data.ModSummary)
        {
            behaviorLookup.TryGetValue(m.Name, out var behavior);
            
            sb.AppendLine($@"<details>
                <summary>{m.Name} <span class=""{GetModTypeBadgeClass(m.ModType)}"">{m.ModType}</span> <span class=""{GetHealthClass(m.Health)}"">{m.Health}</span> {(string.IsNullOrEmpty(m.HealthNote) ? "" : $"<span style='color:var(--dim);'>— {m.HealthNote}</span>")}</summary>
                <div class=""body"">");
            
            if (behavior?.XmlInfo != null)
            {
                var info = behavior.XmlInfo;
                var meta = new List<string>();
                if (!string.IsNullOrEmpty(info.Author)) meta.Add($"by <b>{HttpUtility.HtmlEncode(info.Author)}</b>");
                if (!string.IsNullOrEmpty(info.Version)) meta.Add($"v{HttpUtility.HtmlEncode(info.Version)}");
                if (!string.IsNullOrEmpty(info.Website)) meta.Add($"<a href='{HttpUtility.HtmlEncode(info.Website)}' target='_blank'>{HttpUtility.HtmlEncode(info.Website)}</a>");
                if (meta.Count > 0)
                    sb.AppendLine($@"<div style='color:var(--dim);margin-bottom:var(--s2);'>{string.Join(" • ", meta)}</div>");
                if (!string.IsNullOrEmpty(info.Description))
                    sb.AppendLine($@"<div style='color:var(--muted);font-style:italic;margin-bottom:var(--s3);'>{HttpUtility.HtmlEncode(info.Description)}</div>");
            }
            
            if (behavior != null && !string.IsNullOrEmpty(behavior.OneLiner) && !behavior.OneLiner.Contains("no detectable"))
                sb.AppendLine($@"<div style='margin-bottom:var(--s2);'>{behavior.OneLiner}</div>");
            
            if (behavior?.KeyFeatures.Count > 0)
            {
                sb.AppendLine($@"<div style='margin-bottom:var(--s2);'><b style='color:var(--dim);'>Features:</b> ");
                var features = behavior.KeyFeatures.Take(5).Select(f => HttpUtility.HtmlEncode(f));
                sb.AppendLine(string.Join(" • ", features));
                if (behavior.KeyFeatures.Count > 5)
                    sb.AppendLine($@" <span style='color:var(--dim);'>+{behavior.KeyFeatures.Count - 5} more</span>");
                sb.AppendLine("</div>");
            }
            
            if (behavior?.SystemsAffected.Count > 0)
                sb.AppendLine($@"<div style='color:var(--dim);'><b>Systems:</b> {string.Join(", ", behavior.SystemsAffected)}</div>");
            
            if (behavior?.Warnings.Count > 0)
                sb.AppendLine($@"<div class='warn' style='margin-top:var(--s2);'>Warning: {string.Join(" • ", behavior.Warnings)}</div>");
            
            if (behavior == null && behavior?.XmlInfo == null)
                sb.AppendLine($@"<span style='color:var(--dim);'>No analysis data.</span>");
            
            sb.AppendLine(@"</div></details>");
        }

        return $@"<div class=""sec"" id=""mods"">Installed Mods <span class=""n"">{data.ModSummary.Count}</span></div>
    {sb}";
    }

    private static string GenerateHealthSection(ReportData data)
    {
        var dangerZone = data.DangerZone.Any()
            ? $@"<div class=""alert e""><b>Critical Conflicts</b><table><tr><th>Entity</th><th>Removed By</th><th>Needed By</th></tr>{string.Join("\n", data.DangerZone.Select(d => $"<tr><td>{d.Type}/{d.Name}</td><td>{d.RemovedBy}</td><td>{d.DependedBy}</td></tr>"))}</table></div>"
            : @"<div class=""alert""><b>No Critical Conflicts</b>All C# dependencies satisfied.</div>";

        return $@"<div class=""sec"" id=""health"">Ecosystem Health</div>
    <div class=""stats"">
        <div><b>{data.ActiveEntities:N0}</b><span>Base Game Entities</span></div>
        <div><b>{data.ModifiedEntities}</b><span>Modified by Mods</span></div>
        <div><b>{data.RemovedEntities}</b><span>Removed by Mods</span></div>
        <div><b>{data.DependedEntities}</b><span>C# Dependencies</span></div>
    </div>
    {dangerZone}";
    }

    private static string GeneratePropertyConflicts(List<PropertyConflict> conflicts)
    {
        if (!conflicts.Any())
            return "";

        var rows = string.Join("\n", conflicts.Select(c =>
        {
            var modsStr = string.Join(" → ", c.Setters.Select(s => $"<b>{s.ModName}</b>"));
            return $@"<tr><td><span class='tag'>{c.EntityType}</span> {c.EntityName}</td><td><code>{c.PropertyName}</code></td><td style='font-size:var(--fs-xs);'>{modsStr}</td></tr>";
        }));

        return $@"<details style='margin-top:var(--s3);'>
            <summary><span class=""warn"">Load Order Sensitive</span> Properties set by multiple mods <span class=""n"">{conflicts.Count}</span></summary>
            <div class=""body"">
                <div style='color:var(--dim);margin-bottom:var(--s3);font-size:var(--fs-xs);'>These properties are overwritten by multiple mods. The last mod in load order wins.</div>
                <div style='max-height:300px;overflow-y:auto;'>
                <table><tr><th>Entity</th><th>Property</th><th>Mods (in order)</th></tr>
                {rows}
                </table></div></div></details>";
    }

    private static string GenerateContestedEntities(List<ContestedEntity> entities)
    {
        if (!entities.Any())
            return $@"<div class=""sec"" id=""conflicts"">Entities Modified by Multiple Mods <span class=""n"">0</span></div>
            <div class=""alert""><b>No Shared Entities</b>No game entities are modified by multiple mods.</div>";

        var sb = new StringBuilder();
        foreach (var c in entities)
        {
            var riskClass = c.RiskLevel switch { "High" => "err", "Medium" => "warn", _ => "ok" };
            sb.AppendLine($@"<details>
                <summary><span class=""{riskClass}"">{c.RiskLevel}</span> {c.EntityType}/{c.EntityName}</summary>
                <div class=""body"">
                    <div style='color:var(--dim);margin-bottom:var(--s2);'>{c.RiskReason}</div>
                    <div><b>{c.ModActions.Count} mods:</b> {string.Join(", ", c.ModActions.Select(a => $"{a.ModName} ({a.Operation})"))}</div>
                </div>
            </details>");
        }

        return $@"<div class=""sec"" id=""conflicts"">Entities Modified by Multiple Mods <span class=""n"">{entities.Count}</span></div>
    {sb}";
    }

    private static string GenerateDashboard(ReportData data)
    {
        var sb = new StringBuilder();

        // Health counts
        var healthCounts = data.ModSummary
            .GroupBy(m => m.Health)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        int healthy = healthCounts.TryGetValue("Healthy", out var h) ? h : 0;
        int review = healthCounts.TryGetValue("Review", out var r) ? r : 0;
        int broken = healthCounts.TryGetValue("Broken", out var b) ? b : 0;

        // Risk counts
        var riskCounts = data.ContestedEntities
            .GroupBy(e => e.RiskLevel)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        int riskHigh = riskCounts.TryGetValue("High", out var rh) ? rh : 0;
        int riskMed = riskCounts.TryGetValue("Medium", out var rm) ? rm : 0;
        int riskLow = riskCounts.TryGetValue("Low", out var rl) ? rl : 0;

        // Mod Health panel
        sb.AppendLine(@"<div class=""panel""><div class=""t"">Mod Health</div><div class=""bars"">");
        sb.AppendLine(GenerateBarRow("Healthy", healthy, data.TotalMods, "var(--green)"));
        sb.AppendLine(GenerateBarRow("Review", review, data.TotalMods, "var(--yellow)"));
        sb.AppendLine(GenerateBarRow("Broken", broken, data.TotalMods, "var(--red)"));
        sb.AppendLine(@"</div></div>");

        // Risk Distribution panel
        sb.AppendLine(@"<div class=""panel""><div class=""t"">Entity Conflict Risk</div><div class=""bars"">");
        sb.AppendLine(GenerateBarRow("High", riskHigh, Math.Max(data.ContestedEntities.Count, 1), "var(--red)"));
        sb.AppendLine(GenerateBarRow("Medium", riskMed, Math.Max(data.ContestedEntities.Count, 1), "var(--yellow)"));
        sb.AppendLine(GenerateBarRow("Low", riskLow, Math.Max(data.ContestedEntities.Count, 1), "var(--cyan)"));
        sb.AppendLine(@"</div></div>");

        // Most Invasive Mods
        if (data.MostInvasiveMods.Any())
        {
            sb.AppendLine(@"<div class=""panel""><div class=""t"">Most Invasive Mods</div>");
            sb.AppendLine(@"<div style='font-size:var(--fs-xs);color:var(--dim);margin-bottom:var(--s2);'>Ranked by total changes (XML + C# patches)</div>");
            
            var topMods = data.MostInvasiveMods.Take(3).ToList();
            var restMods = data.MostInvasiveMods.Skip(3).ToList();
            
            sb.AppendLine(@"<table style='width:100%;font-size:var(--fs-xs);'>");
            sb.AppendLine(@"<thead><tr><th>Mod</th><th style='width:40px;'>XML</th><th style='width:40px;'>C#</th><th style='width:40px;'>Total</th></tr></thead>");
            sb.AppendLine(@"<tbody>");
            foreach (var m in topMods)
            {
                sb.AppendLine($@"<tr><td>{m.ModName}</td><td>{m.XmlChanges}</td><td>{m.HarmonyPatches}</td><td><b>{m.TotalChanges}</b></td></tr>");
            }
            
            if (restMods.Any())
            {
                sb.AppendLine($@"<tr class='expand-row'><td colspan='4'><details><summary style='font-size:var(--fs-xs);background:transparent;border:none;padding:var(--s1) 0;color:var(--cyan);cursor:pointer;list-style:none;'>+ Show {restMods.Count} more</summary><div style='max-height:200px;overflow-y:auto;'><table style='width:100%;font-size:var(--fs-xs);border:none;'>");
                foreach (var m in restMods)
                {
                    sb.AppendLine($@"<tr><td>{m.ModName}</td><td style='width:40px;'>{m.XmlChanges}</td><td style='width:40px;'>{m.HarmonyPatches}</td><td style='width:40px;'><b>{m.TotalChanges}</b></td></tr>");
                }
                sb.AppendLine(@"</table></div></details></td></tr>");
            }
            sb.AppendLine(@"</tbody></table>");
            sb.AppendLine(@"</div>");
        }

        // Top Interconnected Entities
        if (data.TopInterconnected.Any())
        {
            sb.AppendLine(@"<div class=""panel""><div class=""t"">Most Connected Entities</div>");
            sb.AppendLine(@"<div style='font-size:var(--fs-xs);color:var(--dim);margin-bottom:var(--s2);'>By reference count (in + out)</div>");
            
            var topEnts = data.TopInterconnected.Take(3).ToList();
            var restEnts = data.TopInterconnected.Skip(3).ToList();
            
            sb.AppendLine(@"<table style='width:100%;font-size:var(--fs-xs);'>");
            sb.AppendLine(@"<thead><tr><th>Entity</th><th style='width:35px;'>In</th><th style='width:35px;'>Out</th></tr></thead>");
            sb.AppendLine(@"<tbody>");
            foreach (var e in topEnts)
            {
                sb.AppendLine($@"<tr><td><span class='tag'>{e.EntityType}</span> {e.EntityName}</td><td>{e.IncomingRefs}</td><td>{e.OutgoingRefs}</td></tr>");
            }
            
            if (restEnts.Any())
            {
                sb.AppendLine($@"<tr class='expand-row'><td colspan='3'><details><summary style='font-size:var(--fs-xs);background:transparent;border:none;padding:var(--s1) 0;color:var(--cyan);cursor:pointer;list-style:none;'>+ Show {restEnts.Count} more</summary><div style='max-height:200px;overflow-y:auto;'><table style='width:100%;font-size:var(--fs-xs);border:none;'>");
                foreach (var e in restEnts)
                {
                    sb.AppendLine($@"<tr><td><span class='tag'>{e.EntityType}</span> {e.EntityName}</td><td style='width:35px;'>{e.IncomingRefs}</td><td style='width:35px;'>{e.OutgoingRefs}</td></tr>");
                }
                sb.AppendLine(@"</table></div></details></td></tr>");
            }
            sb.AppendLine(@"</tbody></table>");
            sb.AppendLine(@"</div>");
        }

        return sb.ToString();
    }

    private static string GenerateBarRow(string label, int value, int max, string color)
    {
        if (max <= 0) max = 1;
        int pct = (int)((double)value / max * 100);
        return $@"<div class=""r""><div class=""k"">{label}</div><div class=""b""><i style=""width:{pct}%;background:{color}""></i></div><div class=""v"">{value}</div></div>";
    }

    // Helper methods
    private static string GetModTypeBadgeClass(string modType) => modType.ToLower() switch
    {
        "xml" => "tag tag-y",
        "c#" or "csharp" => "tag tag-p",
        "hybrid" => "tag tag-c",
        _ => "tag"
    };

    private static string GetHealthClass(string health) => health switch
    {
        "Healthy" => "ok",
        "Review" => "warn",
        "Broken" => "err",
        _ => ""
    };

    public static string FormatCSharpDepType(string type) => type switch
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
        _ => type.Replace("_", " ").ToUpperInvariant()
    };
}
