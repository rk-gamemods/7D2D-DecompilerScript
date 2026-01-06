using System.Text;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the landing page (index.html) with feature cards linking to section pages.
/// </summary>
public static class IndexPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData, long buildTimeMs = 0)
    {
        var body = new StringBuilder();

        // Page header (placeholder for build time - injected after report generation)
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Mod Ecosystem Dashboard</h1>");
        body.AppendLine($@"  <p>Generated {DateTime.Now:yyyy-MM-dd HH:mm} • {data.TotalMods} mods analyzed<!--BUILD_TIME_PLACEHOLDER--></p>");
        body.AppendLine(@"</div>");

        // Stats bar
        body.AppendLine(@"<div class=""stats-bar"">");
        body.AppendLine(StatItem(data.TotalDefinitions.ToString("N0"), "Game Entities"));
        body.AppendLine(StatItem(data.TotalProperties.ToString("N0"), "Properties"));
        body.AppendLine(StatItem(data.TotalReferences.ToString("N0"), "Cross-References"));
        body.AppendLine(StatItem(data.TotalTransitiveRefs.ToString("N0"), "Dependency Chains"));
        body.AppendLine(StatItem(data.TotalMods.ToString(), "Mods Installed"));
        body.AppendLine("<!--BUILD_TIME_STAT_PLACEHOLDER-->");
        body.AppendLine(@"</div>");

        // Feature cards grid
        body.AppendLine(@"<div class=""card-grid"">");

        // Entities card
        var topTypes = data.DefinitionsByType.Take(3).Select(kv => $"{kv.Value:N0} {kv.Key}s");
        body.AppendLine(FeatureCard(
            "entities.html",
            "📦",
            "Entities",
            "Browse all game definitions: items, blocks, buffs, recipes, and more. Search by name or filter by type.",
            "Why useful: Quickly find any entity and see what references it.",
            new[] {
                ($"{data.TotalDefinitions:N0}", "entities indexed"),
                ($"{data.DefinitionsByType.Count}", "entity types")
            }.Concat(topTypes.Select(t => ("", t))).Take(4).ToArray()
        ));

        // Mods card
        var healthyCounts = data.ModSummary.GroupBy(m => m.Health).ToDictionary(g => g.Key, g => g.Count());
        body.AppendLine(FeatureCard(
            "mods.html",
            "🔧",
            "Mods",
            "Detailed view of each installed mod: XML operations, Harmony patches, and health status.",
            "Why useful: Understand exactly what each mod changes in the game.",
            new[] {
                ($"{data.TotalMods}", "mods installed"),
                ($"{healthyCounts.GetValueOrDefault("Healthy", 0)}", "healthy"),
                ($"{healthyCounts.GetValueOrDefault("Review", 0)}", "need review"),
                ($"{healthyCounts.GetValueOrDefault("Broken", 0)}", "broken")
            }
        ));

        // Conflicts card
        var riskCounts = data.ContestedEntities.GroupBy(c => c.RiskLevel).ToDictionary(g => g.Key, g => g.Count());
        body.AppendLine(FeatureCard(
            "conflicts.html",
            "⚠️",
            "Conflicts",
            "Identify entities modified by multiple mods. Filter by severity to focus on critical issues first.",
            "Why useful: Resolve mod conflicts before they cause game problems.",
            new[] {
                ($"{riskCounts.GetValueOrDefault("High", 0)}", "HIGH risk"),
                ($"{riskCounts.GetValueOrDefault("Medium", 0)}", "MEDIUM risk"),
                ($"{riskCounts.GetValueOrDefault("Low", 0)}", "LOW risk"),
                ($"{data.ContestedEntities.Count}", "total contested")
            }
        ));

        // Dependencies card
        var topHotspot = data.InheritanceHotspots.FirstOrDefault();
        body.AppendLine(FeatureCard(
            "dependencies.html",
            "🔗",
            "Dependencies",
            "Explore inheritance chains and impact analysis. See which entities are most dangerous to modify.",
            "Why useful: Understand ripple effects before modifying shared entities.",
            new[] {
                ($"{data.InheritanceHotspots.Count}", "inheritance hotspots"),
                ($"{data.TotalTransitiveRefs:N0}", "dependency chains"),
                ("", topHotspot != null ? $"Top: {topHotspot.EntityName} ({topHotspot.DependentCount} deps)" : "No hotspots")
            }
        ));

        // C# Analysis card
        var patchCount = data.HarmonyPatches.Count;
        var extCount = data.ClassExtensions.Count;
        body.AppendLine(FeatureCard(
            "csharp.html",
            "💻",
            "C# Analysis",
            "View Harmony patches, class extensions, and C# dependencies. Understand how mods hook into game code.",
            "Why useful: Debug code conflicts and understand mod compatibility.",
            new[] {
                ($"{patchCount}", "Harmony patches"),
                ($"{extCount}", "class extensions"),
                ($"{data.CSharpMods + data.HybridMods}", "C# mods")
            }
        ));

        // Game Code Analysis card
        body.AppendLine(FeatureCard(
            "gamecode.html",
            "🔬",
            "Game Code Analysis",
            "Discover potential bugs, stubs, dead code, and hidden features in the base game codebase.",
            "Why useful: Find opportunities to improve or understand game internals.",
            new[] {
                ($"{data.GameCodeBugs}", "bugs"),
                ($"{data.GameCodeWarnings}", "warnings"),
                ($"{data.GameCodeInfo}", "info"),
                ($"{data.GameCodeOpportunities}", "opportunities")
            }
        ));

        // Glossary card
        body.AppendLine(FeatureCard(
            "glossary.html",
            "📖",
            "Glossary",
            "Reference guide for all terms: reference types, XPath operations, severity patterns, and entity types.",
            "Why useful: Understand report terminology and learn about game systems.",
            new[] {
                ("4", "categories"),
                ("", "Reference types, XPath ops"),
                ("", "Severity patterns, Entity types")
            }
        ));

        body.AppendLine(@"</div>");

        // Quick alerts section
        if (data.DangerZone.Any() || data.PropertyConflicts.Any())
        {
            body.AppendLine(@"<div class=""card"" style=""margin-top: 1.5rem; border-color: var(--danger);"">");
            body.AppendLine(@"<h3 style=""color: var(--danger); margin-bottom: 0.75rem;"">⚠️ Attention Required</h3>");

            if (data.DangerZone.Any())
            {
                body.AppendLine($@"<p style=""margin-bottom: 0.5rem;""><strong>{data.DangerZone.Count}</strong> critical conflicts: Entities removed by one mod but needed by C# code. <a href=""conflicts.html"">View conflicts →</a></p>");
            }

            if (data.PropertyConflicts.Any())
            {
                body.AppendLine($@"<p><strong>{data.PropertyConflicts.Count}</strong> load-order sensitive properties: Multiple mods set different values (last mod wins). <a href=""conflicts.html"">View details →</a></p>");
            }

            body.AppendLine(@"</div>");
        }

        return SharedAssets.WrapPage("Dashboard", "index.html", body.ToString());
    }

    private static string StatItem(string value, string label)
    {
        return $@"<div class=""stat""><span class=""stat-value"">{value}</span><span class=""stat-label"">{label}</span></div>";
    }

    private static string FeatureCard(string href, string icon, string title, string description, string whyUseful, (string value, string label)[] stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($@"<a href=""{href}"" class=""card card-clickable feature-card"">");
        sb.AppendLine($@"  <span class=""icon"">{icon}</span>");
        sb.AppendLine($@"  <h3>{SharedAssets.HtmlEncode(title)}</h3>");
        sb.AppendLine($@"  <p>{SharedAssets.HtmlEncode(description)}</p>");
        sb.AppendLine($@"  <p class=""text-dim"" style=""font-size: 12px;"">{SharedAssets.HtmlEncode(whyUseful)}</p>");
        sb.AppendLine(@"  <div class=""stats"">");
        foreach (var (value, label) in stats)
        {
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($@"    <span class=""stat""><b>{value}</b> {SharedAssets.HtmlEncode(label)}</span>");
            else if (!string.IsNullOrEmpty(label))
                sb.AppendLine($@"    <span class=""stat"">{SharedAssets.HtmlEncode(label)}</span>");
        }
        sb.AppendLine(@"  </div>");
        sb.AppendLine(@"</a>");
        return sb.ToString();
    }
}
