using Microsoft.Data.Sqlite;
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
    /// <returns>Path to the generated folder</returns>
    public static string Generate(SqliteConnection db, string outputDir, long buildTimeMs = 0)
    {
        // Create timestamped folder
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var siteFolder = Path.Combine(outputDir, $"ecosystem_{timestamp}");
        var assetsFolder = Path.Combine(siteFolder, "assets");

        Directory.CreateDirectory(siteFolder);
        Directory.CreateDirectory(assetsFolder);

        Console.WriteLine($"  Generating multi-page report in: {siteFolder}");

        // Gather all report data once
        var reportData = ReportDataCollector.GatherReportData(db);

        // Gather extended data for individual pages
        var extendedData = ReportDataCollector.GatherExtendedData(db);

        // Write shared CSS
        var cssPath = Path.Combine(assetsFolder, "styles.css");
        File.WriteAllText(cssPath, SharedAssets.GetStylesCss());
        Console.WriteLine("    ✓ assets/styles.css");

        // Generate each page
        GeneratePage(siteFolder, "index.html", () => IndexPageGenerator.Generate(reportData, extendedData, buildTimeMs));
        GeneratePage(siteFolder, "entities.html", () => EntityPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "mods.html", () => ModPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "conflicts.html", () => ConflictPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "dependencies.html", () => DependencyPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "csharp.html", () => CSharpPageGenerator.Generate(reportData, extendedData));
        GeneratePage(siteFolder, "glossary.html", () => GlossaryPageGenerator.Generate(reportData, extendedData));

        return siteFolder;
    }

    private static void GeneratePage(string folder, string fileName, Func<string> generator)
    {
        var path = Path.Combine(folder, fileName);
        var content = generator();
        File.WriteAllText(path, content);
        Console.WriteLine($"    ✓ {fileName}");
    }
}
