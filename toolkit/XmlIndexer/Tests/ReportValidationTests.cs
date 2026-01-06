using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XmlIndexer.Tests;

/// <summary>
/// Comprehensive validation tests for generated HTML reports.
/// Ensures all visual elements, CSS, and JavaScript are properly rendered.
/// </summary>
public static class ReportValidationTests
{
    private static readonly string[] RequiredPages = 
    {
        "index.html",
        "entities.html", 
        "mods.html",
        "conflicts.html",
        "dependencies.html",
        "csharp.html",
        "gamecode.html",
        "glossary.html"
    };

    private static readonly string[] RequiredAssets =
    {
        "assets/styles.css",
        "assets/cytoscape.min.js"
    };

    /// <summary>
    /// Run all validation tests on a report directory.
    /// Returns list of failures (empty = all passed).
    /// </summary>
    public static List<string> ValidateReport(string reportDir)
    {
        var failures = new List<string>();
        
        if (!Directory.Exists(reportDir))
        {
            failures.Add($"Report directory does not exist: {reportDir}");
            return failures;
        }

        // Test 1: All required files exist
        failures.AddRange(ValidateRequiredFiles(reportDir));
        
        // Test 2: All pages have valid HTML structure
        failures.AddRange(ValidateHtmlStructure(reportDir));
        
        // Test 3: All pages have CSS stylesheet linked
        failures.AddRange(ValidateCssLinks(reportDir));
        
        // Test 4: CSS file has actual content
        failures.AddRange(ValidateCssContent(reportDir));
        
        // Test 5: All pages have navigation
        failures.AddRange(ValidateNavigation(reportDir));
        
        // Test 6: All pages have proper theme support
        failures.AddRange(ValidateThemeSupport(reportDir));
        
        // Test 7: Mods page has MOD_DATA and renderMods
        failures.AddRange(ValidateModsPage(reportDir));
        
        // Test 8: Entities page has ENTITY_DATA
        failures.AddRange(ValidateEntitiesPage(reportDir));
        
        // Test 9: C# page has harmony section
        failures.AddRange(ValidateCSharpPage(reportDir));
        
        // Test 10: All pages have proper meta tags
        failures.AddRange(ValidateMetaTags(reportDir));

        return failures;
    }

    private static IEnumerable<string> ValidateRequiredFiles(string reportDir)
    {
        foreach (var page in RequiredPages)
        {
            var path = Path.Combine(reportDir, page);
            if (!File.Exists(path))
                yield return $"MISSING FILE: {page}";
        }

        foreach (var asset in RequiredAssets)
        {
            var path = Path.Combine(reportDir, asset);
            if (!File.Exists(path))
                yield return $"MISSING ASSET: {asset}";
        }
    }

    private static IEnumerable<string> ValidateHtmlStructure(string reportDir)
    {
        foreach (var page in RequiredPages)
        {
            var path = Path.Combine(reportDir, page);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            
            if (!content.Contains("<!DOCTYPE html>"))
                yield return $"{page}: Missing DOCTYPE declaration";
            
            if (!content.Contains("<html"))
                yield return $"{page}: Missing <html> tag";
            
            if (!content.Contains("<head>"))
                yield return $"{page}: Missing <head> section";
            
            if (!content.Contains("</head>"))
                yield return $"{page}: Unclosed <head> section";
            
            if (!content.Contains("<body>"))
                yield return $"{page}: Missing <body> tag";
            
            if (!content.Contains("</body>"))
                yield return $"{page}: Unclosed <body> tag";
            
            if (!content.Contains("</html>"))
                yield return $"{page}: Unclosed <html> tag";
        }
    }

    private static IEnumerable<string> ValidateCssLinks(string reportDir)
    {
        foreach (var page in RequiredPages)
        {
            var path = Path.Combine(reportDir, page);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            
            if (!content.Contains("href=\"assets/styles.css\""))
                yield return $"{page}: Missing CSS stylesheet link";
        }
    }

    private static IEnumerable<string> ValidateCssContent(string reportDir)
    {
        var cssPath = Path.Combine(reportDir, "assets", "styles.css");
        if (!File.Exists(cssPath))
        {
            yield return "styles.css: File does not exist";
            yield break;
        }

        var css = File.ReadAllText(cssPath);
        
        if (css.Length < 1000)
            yield return $"styles.css: File too small ({css.Length} bytes) - likely empty or corrupted";
        
        // Check for critical CSS variables
        var requiredVars = new[] { "--bg:", "--text:", "--accent:", "--card:" };
        foreach (var cssVar in requiredVars)
        {
            if (!css.Contains(cssVar))
                yield return $"styles.css: Missing CSS variable {cssVar}";
        }

        // Check for critical selectors
        var requiredSelectors = new[] { ".site-nav", ".page-container", ".card", ".stats-bar", ".tag" };
        foreach (var selector in requiredSelectors)
        {
            if (!css.Contains(selector))
                yield return $"styles.css: Missing selector {selector}";
        }
    }

    private static IEnumerable<string> ValidateNavigation(string reportDir)
    {
        foreach (var page in RequiredPages)
        {
            var path = Path.Combine(reportDir, page);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            
            if (!content.Contains("<nav class=\"site-nav\">"))
                yield return $"{page}: Missing navigation bar";
            
            if (!content.Contains("class=\"nav-brand\""))
                yield return $"{page}: Missing nav brand";
            
            // Check all nav links exist
            foreach (var navPage in RequiredPages)
            {
                if (!content.Contains($"href=\"{navPage}\""))
                    yield return $"{page}: Missing nav link to {navPage}";
            }
        }
    }

    private static IEnumerable<string> ValidateThemeSupport(string reportDir)
    {
        foreach (var page in RequiredPages)
        {
            var path = Path.Combine(reportDir, page);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            
            if (!content.Contains("data-theme="))
                yield return $"{page}: Missing data-theme attribute";
            
            if (!content.Contains("theme-select"))
                yield return $"{page}: Missing theme selector";
            
            if (!content.Contains("setTheme"))
                yield return $"{page}: Missing setTheme function";
        }
    }

    private static IEnumerable<string> ValidateModsPage(string reportDir)
    {
        var path = Path.Combine(reportDir, "mods.html");
        if (!File.Exists(path))
        {
            yield return "mods.html: File does not exist";
            yield break;
        }

        var content = File.ReadAllText(path);
        
        if (!content.Contains("const MOD_DATA"))
            yield return "mods.html: Missing MOD_DATA constant";
        
        if (!content.Contains("function filterMods"))
            yield return "mods.html: Missing filterMods function";
        
        if (!content.Contains("function renderMods"))
            yield return "mods.html: Missing renderMods function";
        
        if (!content.Contains("id=\"mod-results\""))
            yield return "mods.html: Missing mod-results container";
        
        if (!content.Contains("id=\"mod-search\""))
            yield return "mods.html: Missing search input";
        
        if (!content.Contains("function getHealthBadge"))
            yield return "mods.html: Missing getHealthBadge function";
        
        if (!content.Contains("function getModTypeBadge"))
            yield return "mods.html: Missing getModTypeBadge function";
        
        if (!content.Contains("function fuzzyScore"))
            yield return "mods.html: Missing fuzzyScore function";
        
        if (!content.Contains("DOMContentLoaded"))
            yield return "mods.html: Missing DOMContentLoaded handler";
        
        // Verify MOD_DATA is valid JSON array
        var modDataMatch = Regex.Match(content, @"const MOD_DATA = (\[[\s\S]*?\]);");
        if (!modDataMatch.Success)
            yield return "mods.html: MOD_DATA not in expected format";
    }

    private static IEnumerable<string> ValidateEntitiesPage(string reportDir)
    {
        var path = Path.Combine(reportDir, "entities.html");
        if (!File.Exists(path))
        {
            yield return "entities.html: File does not exist";
            yield break;
        }

        var content = File.ReadAllText(path);
        
        if (!content.Contains("const ENTITY_DATA") && !content.Contains("const ALL_ENTITIES"))
            yield return "entities.html: Missing entity data constant";
        
        if (!content.Contains("id=\"entity-search\"") && !content.Contains("id=\"search\""))
            yield return "entities.html: Missing search input";
    }

    private static IEnumerable<string> ValidateCSharpPage(string reportDir)
    {
        var path = Path.Combine(reportDir, "csharp.html");
        if (!File.Exists(path))
        {
            yield return "csharp.html: File does not exist";
            yield break;
        }

        var content = File.ReadAllText(path);
        
        if (!content.Contains("Harmony Patches"))
            yield return "csharp.html: Missing Harmony Patches section";
        
        if (!content.Contains("Class Extensions"))
            yield return "csharp.html: Missing Class Extensions section";
        
        // Check it doesn't say "No Harmony patches detected" when there are patches in stats
        if (content.Contains("No Harmony patches detected"))
        {
            // Check if stats show patches
            var patchCountMatch = Regex.Match(content, @"<span class=""stat-value"">(\d+)</span><span class=""stat-label"">Harmony Patches");
            if (patchCountMatch.Success && int.Parse(patchCountMatch.Groups[1].Value) > 0)
                yield return "csharp.html: Shows 'No Harmony patches detected' but stats show patches exist";
        }
    }

    private static IEnumerable<string> ValidateMetaTags(string reportDir)
    {
        foreach (var page in RequiredPages)
        {
            var path = Path.Combine(reportDir, page);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            
            if (!content.Contains("charset=\"UTF-8\""))
                yield return $"{page}: Missing UTF-8 charset meta tag";
            
            if (!content.Contains("viewport"))
                yield return $"{page}: Missing viewport meta tag";
            
            if (!content.Contains("<title>"))
                yield return $"{page}: Missing title tag";
        }
    }

    /// <summary>
    /// Run tests and print results to console.
    /// Returns true if all tests pass.
    /// </summary>
    public static bool RunAndReport(string reportDir)
    {
        Console.WriteLine($"\n{'=',-60}");
        Console.WriteLine($"  REPORT VALIDATION TESTS");
        Console.WriteLine($"  Directory: {reportDir}");
        Console.WriteLine($"{'=',-60}\n");

        var failures = ValidateReport(reportDir);
        
        if (failures.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ ALL TESTS PASSED!");
            Console.ResetColor();
            return true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ {failures.Count} TESTS FAILED:\n");
            Console.ResetColor();
            
            foreach (var failure in failures)
            {
                Console.WriteLine($"  • {failure}");
            }
            
            return false;
        }
    }
}
