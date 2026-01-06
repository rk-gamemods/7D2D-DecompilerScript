using Microsoft.Data.Sqlite;
using XmlIndexer.Analysis;
using XmlIndexer.Database;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Orchestrates multi-page HTML report generation.
/// Creates a timestamped folder with all report pages and shared assets.
/// </summary>
public static class ReportSiteGenerator
{
    /// <summary>
    /// Generate the complete multi-page report site.
    /// </summary>
    /// <param name="db">Open database connection</param>
    /// <param name="outputDir">Base output directory</param>
    /// <param name="buildTimeMs">Build time in milliseconds (optional)</param>
    /// <param name="gameCodebasePath">Path to decompiled game code (optional, enables game code analysis)</param>
    /// <returns>Path to the generated folder</returns>
    public static string Generate(SqliteConnection db, string outputDir, long buildTimeMs = 0, string? gameCodebasePath = null)
    {
        // Ensure schema is up-to-date (adds new tables like relevance scoring)
        DatabaseBuilder.EnsureSchema(db);
        
        // Create timestamped folder
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var siteFolder = Path.Combine(outputDir, $"ecosystem_{timestamp}");
        var assetsFolder = Path.Combine(siteFolder, "assets");

        Directory.CreateDirectory(siteFolder);
        Directory.CreateDirectory(assetsFolder);

        Console.WriteLine($"  Generating multi-page report in: {siteFolder}");

        // Run Harmony conflict detection before report generation
        Console.WriteLine("    ▶ Detecting Harmony conflicts...");
        try
        {
            var harmonyReport = HarmonyConflictDetector.DetectAllConflicts(db);
            if (harmonyReport.TotalConflicts > 0)
            {
                Console.WriteLine($"      Found {harmonyReport.TotalConflicts} Harmony conflicts ({harmonyReport.CriticalCount} critical)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      Note: Harmony conflict detection skipped ({ex.Message})");
        }

        // Run game code analysis if codebase path provided
        if (!string.IsNullOrEmpty(gameCodebasePath) && Directory.Exists(gameCodebasePath))
        {
            Console.WriteLine("    ▶ Analyzing game code...");
            try
            {
                var gameAnalyzer = new GameCodeAnalyzer(db);
                gameAnalyzer.AnalyzeGameCode(gameCodebasePath);
                Console.WriteLine($"      Analyzed {gameAnalyzer.FilesAnalyzed} files, found {gameAnalyzer.FindingsTotal} issues");
                
                // Compute relevance scores for all findings
                var scorer = new RelevanceScorer(db);
                scorer.ComputeScores();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      Note: Game code analysis skipped ({ex.Message})");
            }
        }

        // Gather all report data once
        var reportData = ReportDataCollector.GatherReportData(db);

        // Gather extended data for individual pages
        var extendedData = ReportDataCollector.GatherExtendedData(db);

        // Write shared CSS
        var cssPath = Path.Combine(assetsFolder, "styles.css");
        File.WriteAllText(cssPath, SharedAssets.GetStylesCss());
        Console.WriteLine("    ✓ assets/styles.css");

        // Write Cytoscape.js for offline call graph visualization
        var cytoscapePath = Path.Combine(assetsFolder, "cytoscape.min.js");
        WriteCytoscapeJs(cytoscapePath);
        Console.WriteLine("    ✓ assets/cytoscape.min.js");

        // Generate each page
        GeneratePage(siteFolder, "index.html", () => IndexPageGenerator.Generate(reportData, extendedData, buildTimeMs));
        GeneratePage(siteFolder, "entities.html", () => EntityPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "mods.html", () => ModPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "conflicts.html", () => ConflictPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "dependencies.html", () => DependencyPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "csharp.html", () => CSharpPageGenerator.Generate(reportData, extendedData, db));
        GeneratePage(siteFolder, "glossary.html", () => GlossaryPageGenerator.Generate(reportData, extendedData));
        
        // Generate game code page (always generate, will show "no data" message if empty)
        // Pass codebase path for source context extraction during enrichment
        GeneratePage(siteFolder, "gamecode.html", () => GameCodePageGenerator.Generate(db, gameCodebasePath));

        return siteFolder;
    }

    private static void GeneratePage(string folder, string fileName, Func<string> generator)
    {
        var path = Path.Combine(folder, fileName);
        var content = generator();
        File.WriteAllText(path, content);
        Console.WriteLine($"    ✓ {fileName}");
    }

    /// <summary>
    /// Extracts Cytoscape.js from embedded resource and writes to output path.
    /// Falls back to a minimal error message if resource not found.
    /// </summary>
    private static void WriteCytoscapeJs(string outputPath)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = "XmlIndexer.Assets.cytoscape.min.js";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            using var fileStream = File.Create(outputPath);
            stream.CopyTo(fileStream);
        }
        else
        {
            // Fallback: write a stub that shows an error
            File.WriteAllText(outputPath, @"
// Cytoscape.js not found in embedded resources
// Call graph visualization will not work
console.error('Cytoscape.js embedded resource not found. Run: dotnet build to include Assets/cytoscape.min.js');
window.cytoscape = function() { return { on: function(){}, nodes: function(){ return { forEach: function(){}, length: 0 }; }, edges: function(){ return { length: 0 }; }, layout: function(){ return { run: function(){} }; }, destroy: function(){} }; };
");
        }
    }
}
