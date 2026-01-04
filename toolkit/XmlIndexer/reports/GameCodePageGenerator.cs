using System.Text;
using Microsoft.Data.Sqlite;
using XmlIndexer.Analysis;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the game code analysis page (gamecode.html) showing potential bugs,
/// dead code, stubs, and hidden features found in the game codebase.
/// </summary>
public static class GameCodePageGenerator
{
    public static string Generate(SqliteConnection db)
    {
        var body = new StringBuilder();
        var summary = GameCodeAnalyzer.GetSummary(db);

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Game Code Analysis</h1>");
        body.AppendLine(@"  <p>Potential bugs, stubs, and hidden features in the game codebase</p>");
        body.AppendLine(@"</div>");

        // Stats bar
        body.AppendLine(@"<div class=""stats-bar"">");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: #dc3545;"">{summary.BugCount}</span><span class=""stat-label"">Potential Bugs</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: #fd7e14;"">{summary.WarningCount}</span><span class=""stat-label"">Warnings</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: #17a2b8;"">{summary.InfoCount}</span><span class=""stat-label"">Info</span></div>");
        body.AppendLine($@"<div class=""stat""><span class=""stat-value"" style=""color: #28a745;"">{summary.OpportunityCount}</span><span class=""stat-label"">Opportunities</span></div>");
        body.AppendLine(@"</div>");

        if (summary.TotalCount == 0)
        {
            body.AppendLine(@"<div class=""card"">");
            body.AppendLine(@"<p class=""text-muted"">No game code analysis data available. Run <code>XmlIndexer analyze-game</code> to analyze the game codebase.</p>");
            body.AppendLine(@"</div>");
            return SharedAssets.WrapPage("Game Code Analysis", "gamecode.html", body.ToString(), "");
        }

        // Filter bar
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""gamecode-search"" placeholder=""Search findings..."" oninput=""filterGameCode()"">");
        body.AppendLine(@"  <select id=""severity-filter"" onchange=""filterGameCode()"">");
        body.AppendLine(@"    <option value="""">All Severities</option>");
        body.AppendLine(@"    <option value=""BUG"">Bugs</option>");
        body.AppendLine(@"    <option value=""WARNING"">Warnings</option>");
        body.AppendLine(@"    <option value=""INFO"">Info</option>");
        body.AppendLine(@"    <option value=""OPPORTUNITY"">Opportunities</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"  <select id=""type-filter"" onchange=""filterGameCode()"">");
        body.AppendLine(@"    <option value="""">All Types</option>");
        body.AppendLine(@"    <option value=""unimplemented"">Unimplemented</option>");
        body.AppendLine(@"    <option value=""empty_catch"">Empty Catch</option>");
        body.AppendLine(@"    <option value=""todo"">TODO/FIXME</option>");
        body.AppendLine(@"    <option value=""stub_method"">Stub Methods</option>");
        body.AppendLine(@"    <option value=""unreachable"">Unreachable Code</option>");
        body.AppendLine(@"    <option value=""suspicious"">Suspicious Patterns</option>");
        body.AppendLine(@"    <option value=""secret"">Secrets/Debug</option>");
        body.AppendLine(@"  </select>");
        body.AppendLine(@"</div>");

        // Findings sections
        GenerateBugSection(body, db);
        GenerateWarningSection(body, db);
        GenerateOpportunitySection(body, db);
        GenerateInfoSection(body, db);

        // JavaScript for filtering
        var script = @"
function filterGameCode() {
  const query = document.getElementById('gamecode-search').value.toLowerCase();
  const severity = document.getElementById('severity-filter').value;
  const type = document.getElementById('type-filter').value;

  document.querySelectorAll('.finding-item').forEach(item => {
    const text = item.textContent.toLowerCase();
    const itemSeverity = item.dataset.severity;
    const itemType = item.dataset.type;

    const matchesQuery = !query || text.includes(query);
    const matchesSeverity = !severity || itemSeverity === severity;
    const matchesType = !type || itemType === type;

    item.style.display = (matchesQuery && matchesSeverity && matchesType) ? '' : 'none';
  });

  // Update section visibility
  document.querySelectorAll('.finding-section').forEach(section => {
    const visibleItems = section.querySelectorAll('.finding-item:not([style*=""display: none""])');
    section.style.display = visibleItems.length > 0 ? '' : 'none';
  });
}
";

        return SharedAssets.WrapPage("Game Code Analysis", "gamecode.html", body.ToString(), script);
    }

    private static void GenerateBugSection(StringBuilder body, SqliteConnection db)
    {
        var findings = GetFindings(db, "BUG");
        if (findings.Count == 0) return;

        body.AppendLine(@"<div class=""finding-section"" style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<h2 style=""color: #dc3545; margin-bottom: 1rem;"">🐛 Potential Bugs</h2>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">High-confidence issues that likely indicate bugs in the game code.</p>");

        foreach (var finding in findings.Take(50))
        {
            RenderFinding(body, finding);
        }

        if (findings.Count > 50)
            body.AppendLine($@"<p class=""text-muted"">... and {findings.Count - 50} more</p>");

        body.AppendLine(@"</div>");
    }

    private static void GenerateWarningSection(StringBuilder body, SqliteConnection db)
    {
        var findings = GetFindings(db, "WARNING");
        if (findings.Count == 0) return;

        body.AppendLine(@"<div class=""finding-section"" style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<h2 style=""color: #fd7e14; margin-bottom: 1rem;"">⚠️ Warnings</h2>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Code patterns that may indicate issues worth investigating.</p>");

        foreach (var finding in findings.Take(75))
        {
            RenderFinding(body, finding);
        }

        if (findings.Count > 75)
            body.AppendLine($@"<p class=""text-muted"">... and {findings.Count - 75} more</p>");

        body.AppendLine(@"</div>");
    }

    private static void GenerateOpportunitySection(StringBuilder body, SqliteConnection db)
    {
        var findings = GetFindings(db, "OPPORTUNITY");
        if (findings.Count == 0) return;

        body.AppendLine(@"<div class=""finding-section"" style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<h2 style=""color: #28a745; margin-bottom: 1rem;"">🎯 Modding Opportunities</h2>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Hidden features, debug flags, and console commands that could be useful for modding.</p>");

        foreach (var finding in findings.Take(100))
        {
            RenderFinding(body, finding);
        }

        if (findings.Count > 100)
            body.AppendLine($@"<p class=""text-muted"">... and {findings.Count - 100} more</p>");

        body.AppendLine(@"</div>");
    }

    private static void GenerateInfoSection(StringBuilder body, SqliteConnection db)
    {
        var findings = GetFindings(db, "INFO");
        if (findings.Count == 0) return;

        body.AppendLine(@"<div class=""finding-section"" style=""margin-bottom: 2rem;"">");
        body.AppendLine(@"<details>");
        body.AppendLine(@"<summary style=""color: #17a2b8; font-weight: 600; cursor: pointer;"">ℹ️ Informational ({findings.Count} items)</summary>");
        body.AppendLine(@"<div class=""details-body"">");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Lower-priority findings like TODOs and code style notes.</p>");

        foreach (var finding in findings.Take(50))
        {
            RenderFinding(body, finding);
        }

        if (findings.Count > 50)
            body.AppendLine($@"<p class=""text-muted"">... and {findings.Count - 50} more</p>");

        body.AppendLine(@"</div></details>");
        body.AppendLine(@"</div>");
    }

    private static void RenderFinding(StringBuilder body, FindingRow finding)
    {
        var severityClass = finding.Severity switch
        {
            "BUG" => "tag-high",
            "WARNING" => "tag-medium",
            "OPPORTUNITY" => "tag-low",
            _ => "tag-info"
        };

        var typeLabel = finding.AnalysisType switch
        {
            "unimplemented" => "Not Implemented",
            "empty_catch" => "Empty Catch",
            "todo" => "TODO",
            "stub_method" => "Stub",
            "unreachable" => "Unreachable",
            "suspicious" => "Suspicious",
            "secret" => "Secret/Debug",
            _ => finding.AnalysisType
        };

        body.AppendLine($@"<div class=""finding-item card"" data-severity=""{finding.Severity}"" data-type=""{finding.AnalysisType}"" style=""margin-bottom: 0.75rem; padding: 0.75rem;"">");

        // Header row
        body.AppendLine(@"<div style=""display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem;"">");
        body.AppendLine($@"<span class=""tag {severityClass}"">{typeLabel}</span>");
        body.AppendLine($@"<code style=""font-size: 12px;"">{SharedAssets.HtmlEncode(finding.ClassName)}{(finding.MethodName != null ? $".{finding.MethodName}" : "")}</code>");
        if (finding.LineNumber.HasValue)
        {
            var fileName = Path.GetFileName(finding.FilePath ?? "");
            body.AppendLine($@"<span class=""text-muted"" style=""font-size: 11px; margin-left: auto;"">{fileName}:{finding.LineNumber}</span>");
        }
        body.AppendLine(@"</div>");

        // Description
        body.AppendLine($@"<div style=""margin-bottom: 0.5rem;"">{SharedAssets.HtmlEncode(finding.Description ?? "")}</div>");

        // Code snippet
        if (!string.IsNullOrEmpty(finding.CodeSnippet))
        {
            body.AppendLine(@"<details style=""margin-bottom: 0.5rem;"">");
            body.AppendLine(@"<summary class=""text-muted"" style=""font-size: 12px; cursor: pointer;"">Show code</summary>");
            body.AppendLine($@"<pre style=""background: var(--bg-secondary); padding: 0.5rem; border-radius: 4px; margin-top: 0.5rem; font-size: 11px; overflow-x: auto;"">{SharedAssets.HtmlEncode(finding.CodeSnippet)}</pre>");
            body.AppendLine(@"</details>");
        }

        // Fix suggestion
        if (!string.IsNullOrEmpty(finding.PotentialFix))
        {
            body.AppendLine($@"<div class=""text-muted"" style=""font-size: 11px;"">💡 {SharedAssets.HtmlEncode(finding.PotentialFix)}</div>");
        }

        body.AppendLine(@"</div>");
    }

    private static List<FindingRow> GetFindings(SqliteConnection db, string severity)
    {
        var findings = new List<FindingRow>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT analysis_type, class_name, method_name, severity, confidence,
                   description, reasoning, code_snippet, file_path, line_number, potential_fix
            FROM game_code_analysis
            WHERE severity = $severity
            ORDER BY
                CASE confidence WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                class_name, line_number";

        cmd.Parameters.AddWithValue("$severity", severity);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            findings.Add(new FindingRow(
                AnalysisType: reader.GetString(0),
                ClassName: reader.GetString(1),
                MethodName: reader.IsDBNull(2) ? null : reader.GetString(2),
                Severity: reader.GetString(3),
                Confidence: reader.GetString(4),
                Description: reader.IsDBNull(5) ? null : reader.GetString(5),
                Reasoning: reader.IsDBNull(6) ? null : reader.GetString(6),
                CodeSnippet: reader.IsDBNull(7) ? null : reader.GetString(7),
                FilePath: reader.IsDBNull(8) ? null : reader.GetString(8),
                LineNumber: reader.IsDBNull(9) ? null : reader.GetInt32(9),
                PotentialFix: reader.IsDBNull(10) ? null : reader.GetString(10)
            ));
        }

        return findings;
    }

    private record FindingRow(
        string AnalysisType,
        string ClassName,
        string? MethodName,
        string Severity,
        string Confidence,
        string? Description,
        string? Reasoning,
        string? CodeSnippet,
        string? FilePath,
        int? LineNumber,
        string? PotentialFix
    );
}
