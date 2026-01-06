using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using XmlIndexer.Database;
using XmlIndexer.Models;
using XmlIndexer.Utils;

namespace XmlIndexer.Analysis;

/// <summary>
/// Analyzes mod content: XML operations, C# dependencies, and cross-mod conflicts.
/// </summary>
public class ModAnalyzer
{
    private readonly string _dbPath;
    private int _conflictCount;
    private int _cautionCount;

    public ModAnalyzer(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Analyzes all mods in a folder, detecting conflicts and C# dependencies.
    /// Supports incremental processing - skips unchanged mod folders.
    /// </summary>
    public int AnalyzeAllMods(string modsFolder, bool forceRebuild = false)
    {
        if (!Directory.Exists(modsFolder))
        {
            Console.WriteLine($"Error: Mods folder not found: {modsFolder}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var modDirs = Directory.GetDirectories(modsFolder)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .OrderBy(d => Path.GetFileName(d))
            .ToList();

        if (modDirs.Count == 0)
        {
            Console.WriteLine("No mods found in folder.");
            return 0;
        }

        Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  7D2D MOD ANALYZER - Scanning {modDirs.Count} mods");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝\n");

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Incremental processing: compute folder hashes
        Console.WriteLine("[Phase 0] Checking mod folder hashes...");
        var hashSw = Stopwatch.StartNew();
        var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modDir in modDirs)
        {
            currentHashes[modDir] = ContentHasher.HashFolder(modDir, "*");
        }
        
        List<string> changed, unchanged, newMods, deleted;
        Dictionary<string, string> storedHashes;
        
        if (forceRebuild)
        {
            storedHashes = new Dictionary<string, string>();
            changed = new List<string>();
            unchanged = new List<string>();
            newMods = modDirs;
            deleted = new List<string>();
            Console.WriteLine($"  Force rebuild - processing all {modDirs.Count} mods");
        }
        else
        {
            storedHashes = DatabaseBuilder.GetStoredHashes(db, "mod_folder");
            (changed, unchanged, newMods, deleted) = DatabaseBuilder.CompareHashes(storedHashes, currentHashes);
            Console.WriteLine($"  {unchanged.Count} unchanged, {changed.Count} modified, {newMods.Count} new, {deleted.Count} removed ({hashSw.ElapsedMilliseconds}ms)");
        }

        // Handle deleted mods
        foreach (var modPath in deleted)
        {
            Console.WriteLine($"  Removing deleted mod: {Path.GetFileName(modPath)}");
            DatabaseBuilder.DeleteModDataByPath(db, modPath);
            DatabaseBuilder.DeleteFileHashes(db, new[] { modPath });
        }

        // Determine which mods to process
        var toProcess = changed.Concat(newMods).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // If nothing to process and no deletions, skip
        if (toProcess.Count == 0 && deleted.Count == 0)
        {
            Console.WriteLine("\n✓ All mods unchanged - skipping analysis ({0}ms)", sw.ElapsedMilliseconds);
            return 0;
        }

        // Delete old data for changed mods
        foreach (var modPath in changed)
        {
            DatabaseBuilder.DeleteModDataByPath(db, modPath);
        }

        var results = new List<ModResult>();
        var allRemovals = new List<XmlRemoval>();
        var allDependencies = new List<CSharpDependency>();

        // PHASE 1: Scan mods (only process changed/new, but collect data from all for conflict detection)
        Console.WriteLine($"\n[Phase 1] Scanning mods ({toProcess.Count} to analyze)...");
        foreach (var modDir in modDirs)
        {
            var modName = Path.GetFileName(modDir);
            var configPath = Path.Combine(modDir, "Config");

            _conflictCount = 0;
            _cautionCount = 0;

            var deps = CSharpAnalyzer.ScanCSharpDependencies(modDir, modName);
            allDependencies.AddRange(deps);

            if (!Directory.Exists(configPath))
            {
                results.Add(new ModResult(modName, 0, 0, true, deps));
                continue;
            }

            var xmlFiles = Directory.GetFiles(configPath, "*.xml");
            if (xmlFiles.Length == 0)
            {
                results.Add(new ModResult(modName, 0, 0, true, deps));
                continue;
            }

            var removals = new List<XmlRemoval>();
            
            // Only write to DB for changed/new mods, but analyze all for conflict detection
            bool writeToDb = toProcess.Contains(modDir);
            
            foreach (var xmlFile in xmlFiles)
            {
                if (writeToDb)
                {
                    AnalyzeModXmlSilent(xmlFile, db, modName, removals);
                }
                else
                {
                    // Just collect removals for conflict detection
                    AnalyzeModXmlForConflicts(xmlFile, modName, removals);
                }
            }
            allRemovals.AddRange(removals);

            results.Add(new ModResult(modName, _conflictCount, _cautionCount, false, deps));
            
            // Update hash for processed mods
            if (writeToDb)
            {
                DatabaseBuilder.UpsertFileHash(db, modDir, currentHashes[modDir], "mod_folder");
            }
        }

        // PHASE 2: Cross-reference C# dependencies against XML removals
        Console.WriteLine("[Phase 2] Cross-referencing dependencies...\n");
        var crossModConflicts = new List<(string CSharpMod, string XmlMod, CSharpDependency Dep, XmlRemoval Removal)>();
        
        foreach (var dep in allDependencies)
        {
            foreach (var removal in allRemovals)
            {
                if (removal.Type == dep.Type && removal.Name == dep.Name)
                {
                    crossModConflicts.Add((dep.ModName, removal.ModName, dep, removal));
                }
            }
        }

        // Print results
        PrintAnalysisResults(results, allDependencies, crossModConflicts, sw.ElapsedMilliseconds, modsFolder);

        return (crossModConflicts.Any() || results.Any(r => r.Conflicts > 0)) ? 1 : 0;
    }

    /// <summary>
    /// Analyze mod XML for conflicts only (no DB writes) - for unchanged mods.
    /// </summary>
    private void AnalyzeModXmlForConflicts(string xmlPath, string modName, List<XmlRemoval> removals)
    {
        try
        {
            var doc = XDocument.Load(xmlPath, LoadOptions.SetLineInfo);
            foreach (var op in doc.Descendants().Where(e => 
                e.Name.LocalName is "remove" or "removeattribute" or "set" or "setattribute" or "append"))
            {
                var xpath = op.Attribute("xpath")?.Value;
                if (string.IsNullOrEmpty(xpath)) continue;
                
                var target = ExtractTargetFromXPath(xpath);
                if (target != null && op.Name.LocalName.StartsWith("remove"))
                {
                    removals.Add(new XmlRemoval(modName, target.Type, target.Name, Path.GetFileName(xmlPath)));
                }
            }
        }
        catch { /* Ignore parse errors for conflict detection */ }
    }

    /// <summary>
    /// Analyze a single mod's XML operations.
    /// </summary>
    public int AnalyzeMod(string modPath)
    {
        if (!Directory.Exists(modPath))
        {
            Console.WriteLine($"Error: Mod path not found: {modPath}");
            return 1;
        }

        var modName = Path.GetFileName(modPath);
        Console.WriteLine($"=== Analyzing Mod: {modName} ===\n");

        _conflictCount = 0;
        _cautionCount = 0;

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        var configPath = Path.Combine(modPath, "Config");
        if (!Directory.Exists(configPath))
        {
            Console.WriteLine("No Config folder found - this may be a code-only mod.");
            return 0;
        }

        foreach (var xmlFile in Directory.GetFiles(configPath, "*.xml"))
        {
            AnalyzeModXmlVerbose(xmlFile, db);
        }

        PrintModSummary();
        return _conflictCount > 0 ? 1 : 0;
    }

    /// <summary>
    /// Persist mod analysis to database for report generation.
    /// Uses hash-based caching to skip unchanged mods.
    /// </summary>
    public void PersistModAnalysis(string modsFolder, SqliteConnection db)
    {
        var modDirs = Directory.GetDirectories(modsFolder)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .OrderBy(d => Path.GetFileName(d)) // 7D2D loads mods alphabetically
            .ToList();

        // Get stored mod hashes for incremental processing
        var storedHashes = DatabaseBuilder.GetStoredHashes(db, "mod_folder");
        
        // Compute current hashes and determine what changed
        var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var modHashMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // modDir -> hash
        foreach (var modDir in modDirs)
        {
            var modName = Path.GetFileName(modDir);
            // Hash all XML and DLL files in the mod folder
            var xmlHash = Utils.ContentHasher.HashFolder(Path.Combine(modDir, "Config"), "*.xml");
            var dllHash = Utils.ContentHasher.HashFolder(modDir, "*.dll");
            var combinedHash = Utils.ContentHasher.HashString(xmlHash + dllHash);
            currentHashes[modName] = combinedHash;
            modHashMap[modDir] = combinedHash;
        }
        
        var (changed, unchanged, newMods, deleted) = DatabaseBuilder.CompareHashes(storedHashes, currentHashes);
        var toProcess = changed.Concat(newMods).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        int skipped = unchanged.Count;
        int processed = 0;
        
        Console.WriteLine($"Persisting analysis for {modDirs.Count} mods ({skipped} cached, {toProcess.Count} to analyze)...");

        using var transaction = db.BeginTransaction();

        // Clean up deleted mods
        foreach (var deletedMod in deleted)
        {
            using var delCmd = db.CreateCommand();
            delCmd.CommandText = "SELECT id FROM mods WHERE name = $name";
            delCmd.Parameters.AddWithValue("$name", deletedMod);
            var delId = delCmd.ExecuteScalar() as long?;
            if (delId != null)
            {
                DatabaseBuilder.DeleteModDataById(db, delId.Value);
                using var rmCmd = db.CreateCommand();
                rmCmd.CommandText = "DELETE FROM mods WHERE id = $id";
                rmCmd.Parameters.AddWithValue("$id", delId.Value);
                rmCmd.ExecuteNonQuery();
            }
        }

        int loadOrder = 0;
        foreach (var modDir in modDirs)
        {
            loadOrder++;
            var modName = Path.GetFileName(modDir);
            var folderName = Path.GetFileName(modDir);
            var configPath = Path.Combine(modDir, "Config");
            var hasXml = Directory.Exists(configPath) && Directory.GetFiles(configPath, "*.xml").Any();
            var hasDll = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
                .Any(f => !Path.GetFileName(f).StartsWith("0Harmony") &&
                          !Path.GetFileName(f).Contains("System.") &&
                          !Path.GetFileName(f).Contains("Microsoft."));

            // Read ModInfo.xml
            var modInfo = ReadModInfo(modDir);

            // Check if mod already exists
            long? existingModId = null;
            using (var checkCmd = db.CreateCommand())
            {
                checkCmd.CommandText = "SELECT id FROM mods WHERE name = $name";
                checkCmd.Parameters.AddWithValue("$name", modName);
                existingModId = checkCmd.ExecuteScalar() as long?;
            }
            
            // Skip unchanged mods (but always update load_order in case it changed)
            if (!toProcess.Contains(modName) && existingModId != null)
            {
                // Just update load order for cached mods
                using var updateCmd = db.CreateCommand();
                updateCmd.CommandText = "UPDATE mods SET load_order = $loadOrder WHERE id = $id";
                updateCmd.Parameters.AddWithValue("$loadOrder", loadOrder);
                updateCmd.Parameters.AddWithValue("$id", existingModId.Value);
                updateCmd.ExecuteNonQuery();
                continue; // Skip full re-analysis
            }

            if (existingModId != null)
            {
                // Delete old data for this mod (FK children first)
                DatabaseBuilder.DeleteModDataById(db, existingModId.Value);
                
                // Update existing mod record
                using var updateCmd = db.CreateCommand();
                updateCmd.CommandText = @"UPDATE mods SET 
                    folder_name = $folderName, load_order = $loadOrder, has_xml = $xml, has_dll = $dll,
                    display_name = $displayName, description = $desc, author = $author, version = $version, website = $website
                    WHERE id = $id";
                updateCmd.Parameters.AddWithValue("$id", existingModId.Value);
                updateCmd.Parameters.AddWithValue("$folderName", folderName);
                updateCmd.Parameters.AddWithValue("$loadOrder", loadOrder);
                updateCmd.Parameters.AddWithValue("$xml", hasXml ? 1 : 0);
                updateCmd.Parameters.AddWithValue("$dll", hasDll ? 1 : 0);
                updateCmd.Parameters.AddWithValue("$displayName", modInfo?.DisplayName ?? (object)DBNull.Value);
                updateCmd.Parameters.AddWithValue("$desc", modInfo?.Description ?? (object)DBNull.Value);
                updateCmd.Parameters.AddWithValue("$author", modInfo?.Author ?? (object)DBNull.Value);
                updateCmd.Parameters.AddWithValue("$version", modInfo?.Version ?? (object)DBNull.Value);
                updateCmd.Parameters.AddWithValue("$website", modInfo?.Website ?? (object)DBNull.Value);
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                // Insert new mod record
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"INSERT INTO mods
                    (name, folder_name, load_order, has_xml, has_dll, display_name, description, author, version, website)
                    VALUES ($name, $folderName, $loadOrder, $xml, $dll, $displayName, $desc, $author, $version, $website)";
                cmd.Parameters.AddWithValue("$name", modName);
                cmd.Parameters.AddWithValue("$folderName", folderName);
                cmd.Parameters.AddWithValue("$loadOrder", loadOrder);
                cmd.Parameters.AddWithValue("$xml", hasXml ? 1 : 0);
                cmd.Parameters.AddWithValue("$dll", hasDll ? 1 : 0);
                cmd.Parameters.AddWithValue("$displayName", modInfo?.DisplayName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$desc", modInfo?.Description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$author", modInfo?.Author ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$version", modInfo?.Version ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$website", modInfo?.Website ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // Get mod ID
            long modId;
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM mods WHERE name = $name";
                cmd.Parameters.AddWithValue("$name", modName);
                modId = (long)cmd.ExecuteScalar()!;
            }

            // Analyze XML and store operations
            int xmlOps = 0, conflicts = 0, cautions = 0;
            if (hasXml)
            {
                foreach (var xmlFile in Directory.GetFiles(configPath!, "*.xml"))
                {
                    var result = AnalyzeModXmlToDatabase(xmlFile, db, modId, modName);
                    xmlOps += result.Operations;
                    conflicts += result.Conflicts;
                    cautions += result.Cautions;
                }
            }

            // Scan and store C# dependencies
            var deps = CSharpAnalyzer.ScanCSharpDependencies(modDir, modName);
            foreach (var dep in deps)
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"INSERT INTO mod_csharp_deps
                    (mod_id, dependency_type, dependency_name, source_file, line_number, pattern, code_snippet)
                    VALUES ($modId, $type, $name, $file, $line, $pattern, $snippet)";
                cmd.Parameters.AddWithValue("$modId", modId);
                cmd.Parameters.AddWithValue("$type", dep.Type);
                cmd.Parameters.AddWithValue("$name", dep.Name);
                cmd.Parameters.AddWithValue("$file", dep.SourceFile);
                cmd.Parameters.AddWithValue("$line", dep.Line);
                cmd.Parameters.AddWithValue("$pattern", dep.Pattern);
                cmd.Parameters.AddWithValue("$snippet", (object?)dep.CodeSnippet ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // Scan and store detailed Harmony patches for conflict detection
            if (hasDll)
            {
                ScanAndStoreHarmonyPatches(modDir, modName, modId, db);
            }

            // Update mod summary
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"UPDATE mods SET 
                    xml_operations = $ops, csharp_dependencies = $deps, conflicts = $conflicts, cautions = $cautions
                    WHERE id = $id";
                cmd.Parameters.AddWithValue("$ops", xmlOps);
                cmd.Parameters.AddWithValue("$deps", deps.Count);
                cmd.Parameters.AddWithValue("$conflicts", conflicts);
                cmd.Parameters.AddWithValue("$cautions", cautions);
                cmd.Parameters.AddWithValue("$id", modId);
                cmd.ExecuteNonQuery();
            }

            var status = conflicts > 0 ? "⚠" : cautions > 0 ? "⚡" : "✓";
            Console.WriteLine($"  {status} {modName}: {xmlOps} XML ops, {deps.Count} C# deps");
            
            // Store hash for this mod so we can skip it next time
            if (modHashMap.TryGetValue(modDir, out var modHash))
            {
                DatabaseBuilder.UpsertFileHash(db, modName, modHash, "mod_folder");
            }
            processed++;
        }

        // Clean up hashes for deleted mods
        foreach (var deletedMod in deleted)
        {
            using var delHashCmd = db.CreateCommand();
            delHashCmd.CommandText = "DELETE FROM file_hashes WHERE file_path = $path AND file_type = 'mod_folder'";
            delHashCmd.Parameters.AddWithValue("$path", deletedMod);
            delHashCmd.ExecuteNonQuery();
        }

        transaction.Commit();
        
        if (skipped > 0)
        {
            Console.WriteLine($"  ✓ {skipped} mods unchanged (cached), {processed} mods analyzed");
        }
    }

    // ==========================================================================
    // XPath Impact Analysis
    // ==========================================================================

    public (ImpactStatus status, string details) AnalyzeXPathImpact(string operation, string xpath, SqliteConnection db)
    {
        var isDestructive = operation is "remove" or "removeattribute";
        
        var patterns = new Dictionary<string, string>
        {
            { @"/items/item\[@name='([^']+)'\]", "item" },
            { @"/blocks/block\[@name='([^']+)'\]", "block" },
            { @"/entity_classes/entity_class\[@name='([^']+)'\]", "entity_class" },
            { @"/buffs/buff\[@name='([^']+)'\]", "buff" },
            { @"/recipes/recipe\[@name='([^']+)'\]", "recipe" },
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(xpath, pattern.Key);
            if (match.Success)
            {
                var targetName = match.Groups[1].Value;
                var targetType = pattern.Value;

                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM xml_definitions WHERE definition_type = $type AND name = $name";
                cmd.Parameters.AddWithValue("$type", targetType);
                cmd.Parameters.AddWithValue("$name", targetName);
                var exists = (long)cmd.ExecuteScalar()! > 0;

                if (!exists)
                    return (ImpactStatus.Safe, $"Target: {targetType} '{targetName}' (not in base game)");

                if (isDestructive && operation == "remove")
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM xml_references WHERE target_type = $type AND target_name = $name";
                    var refCount = (long)cmd.ExecuteScalar()!;
                    
                    if (refCount > 0)
                        return (ImpactStatus.Conflict, $"BREAKS {refCount} references to {targetType} '{targetName}'!");
                    else
                        return (ImpactStatus.Caution, $"Removes {targetType} '{targetName}' (no base game refs, but may affect other mods)");
                }

                return (ImpactStatus.Safe, $"Target: {targetType} '{targetName}' (exists)");
            }
        }

        var propMatch = Regex.Match(xpath, @"property\[@name='([^']+)'\]");
        if (propMatch.Success)
        {
            var propName = propMatch.Groups[1].Value;

            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(DISTINCT definition_id) FROM xml_properties WHERE property_name = $name";
            cmd.Parameters.AddWithValue("$name", propName);
            var count = (long)cmd.ExecuteScalar()!;

            if (isDestructive)
            {
                if (count > 0)
                    return (ImpactStatus.Caution, $"Removes property '{propName}' from {count} definitions");
                return (ImpactStatus.Safe, $"Property '{propName}' not found in base game");
            }

            return (ImpactStatus.Safe, $"Modifies property '{propName}' on {count} definitions");
        }

        if (isDestructive)
            return (ImpactStatus.Caution, "Destructive operation - impact unknown");

        return (ImpactStatus.Safe, "");
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    public static XPathTarget? ExtractTargetFromXPath(string xpath)
    {
        // Primary patterns - select by @name (reliable)
        // Note: Patterns use (?:/|$) at the end to match either end of string or continuation
        var namePatterns = new Dictionary<string, string>
        {
            { @"/items/item\[@name='([^']+)'\](?:/|$)", "item" },
            { @"/blocks/block\[@name='([^']+)'\](?:/|$)", "block" },
            { @"/entity_classes/entity_class\[@name='([^']+)'\](?:/|$)", "entity_class" },
            { @"/buffs/buff\[@name='([^']+)'\](?:/|$)", "buff" },
            { @"/recipes/recipe\[@name='([^']+)'\](?:/|$)", "recipe" },
            { @"/Sounds/SoundDataNode\[@name='([^']+)'\](?:/|$)", "sound" },
            { @"/lootcontainers/lootcontainer\[@name='([^']+)'\](?:/|$)", "loot_container" },
            { @"/lootcontainers/lootgroup\[@name='([^']+)'\](?:/|$)", "loot_group" },
            { @"/quests/quest\[@id='([^']+)'\](?:/|$)", "quest" },
            { @"/progression/perks/perk\[@name='([^']+)'\](?:/|$)", "perk" },
            { @"/progression/skills/skill\[@name='([^']+)'\](?:/|$)", "skill" },
            { @"/vehicles/vehicle\[@name='([^']+)'\](?:/|$)", "vehicle" },
            { @"/windows/window\[@name='([^']+)'\](?:/|$)", "window" },
            { @"/controls/(\w+)\[@name='([^']+)'\](?:/|$)", "control" },
            { @"/gameevents/gameevent\[@name='([^']+)'\](?:/|$)", "game_event" },
            { @"/archetypes/archetype\[@name='([^']+)'\](?:/|$)", "archetype" },
        };

        // Check name-based patterns first (reliable matches)
        foreach (var pattern in namePatterns)
        {
            var match = Regex.Match(xpath, pattern.Key);
            if (match.Success)
            {
                // Handle special case for controls which has element name in group 1
                if (pattern.Value == "control")
                    return new XPathTarget(match.Groups[1].Value, match.Groups[2].Value, "name", match.Groups[2].Value, false);
                return new XPathTarget(pattern.Value, match.Groups[1].Value, "name", match.Groups[1].Value, false);
            }
        }

        // Secondary patterns - select by non-name attribute (fragile - flag as warning)
        // These xpaths work but may be harder to track or may break if base game changes
        var fragilePatterns = new (string pattern, string type, string attr)[]
        {
            (@"/lootcontainers/lootcontainer\[@size='([^']+)'\](?:/|$)", "loot_container", "size"),
            (@"/lootcontainers/lootcontainer\[@id='([^']+)'\](?:/|$)", "loot_container", "id"),
            (@"/windows/window\[@controller='([^']+)'\](?:/|$)", "window", "controller"),
            (@"/controls/(\w+)\[@controller='([^']+)'\](?:/|$)", "control", "controller"),
            (@"/items/item\[@Extends='([^']+)'\](?:/|$)", "item", "Extends"),
            (@"/blocks/block\[@Extends='([^']+)'\](?:/|$)", "block", "Extends"),
        };

        foreach (var (pattern, type, attr) in fragilePatterns)
        {
            var match = Regex.Match(xpath, pattern);
            if (match.Success)
            {
                var selectorValue = match.Groups[match.Groups.Count - 1].Value;
                // For fragile matches, use attr:value as the "name" so it's visible in reports
                var displayName = $"[{attr}='{selectorValue}']";
                return new XPathTarget(type, displayName, attr, selectorValue, true);
            }
        }

        // Try generic entity pattern - extract element type from path
        // (?:/|$) allows matching paths that continue or end after the selector
        var genericMatch = Regex.Match(xpath, @"/([\w_]+)/([\w_]+)\[@(\w+)='([^']+)'\](?:/|$)");
        if (genericMatch.Success)
        {
            var container = genericMatch.Groups[1].Value;
            var element = genericMatch.Groups[2].Value;
            var attr = genericMatch.Groups[3].Value;
            var value = genericMatch.Groups[4].Value;
            
            bool isFragile = attr != "name" && attr != "id";
            var name = attr == "name" ? value : $"[{attr}='{value}']";
            
            return new XPathTarget(element, name, attr, value, isFragile);
        }

        return null;
    }

    public static string? ExtractPropertyFromXPath(string xpath)
    {
        var patterns = new[]
        {
            @"passive_effect\[@name='([^']+)'\]",
            @"triggered_effect\[@name='([^']+)'\]",
            @"property\[@name='([^']+)'\]",
            @"effect_group\[@name='([^']+)'\]",
            @"@(\w+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(xpath, pattern);
            if (match.Success)
                return match.Groups[1].Value;
        }
        
        return null;
    }

    public static string? ExtractValueFromXPath(string xpath)
    {
        var match = Regex.Match(xpath, @"@\w+='([^']*)'");
        return match.Success ? match.Groups[1].Value : null;
    }

    private ModXmlInfo? ReadModInfo(string modDir)
    {
        var modInfoPath = Path.Combine(modDir, "ModInfo.xml");
        if (!File.Exists(modInfoPath)) return null;

        try
        {
            var doc = XDocument.Load(modInfoPath);
            var root = doc.Root;
            return new ModXmlInfo(
                root?.Element("DisplayName")?.Value ?? root?.Element("Name")?.Attribute("value")?.Value,
                root?.Element("Description")?.Value ?? root?.Element("Description")?.Attribute("value")?.Value,
                root?.Element("Author")?.Value ?? root?.Element("Author")?.Attribute("value")?.Value,
                root?.Element("Version")?.Value ?? root?.Element("Version")?.Attribute("value")?.Value,
                root?.Element("Website")?.Value ?? root?.Element("Website")?.Attribute("value")?.Value
            );
        }
        catch { return null; }
    }

    private void AnalyzeModXmlSilent(string filePath, SqliteConnection db, string modName, List<XmlRemoval> removals)
    {
        try
        {
            var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);
            var operations = new[] { "set", "append", "remove", "insertAfter", "insertBefore", "setattribute", "removeattribute", "csv" };

            foreach (var op in operations)
            {
                foreach (var element in doc.Descendants(op))
                {
                    var xpath = element.Attribute("xpath")?.Value;
                    if (string.IsNullOrEmpty(xpath)) continue;

                    var (status, _) = AnalyzeXPathImpact(op, xpath, db);
                    
                    if (status == ImpactStatus.Conflict)
                        _conflictCount++;
                    else if (status == ImpactStatus.Caution)
                        _cautionCount++;

                    if (op == "remove")
                    {
                        var extracted = ExtractTargetFromXPath(xpath);
                        if (extracted != null)
                            removals.Add(new XmlRemoval(modName, extracted.Type, extracted.Name, xpath));
                    }
                }
            }
        }
        catch { /* Skip unparseable files */ }
    }

    private void AnalyzeModXmlVerbose(string filePath, SqliteConnection db)
    {
        var fileName = Path.GetFileName(filePath);
        Console.WriteLine($"--- {fileName} ---");

        try
        {
            var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);
            var operations = new[] { "set", "append", "remove", "insertAfter", "insertBefore", "setattribute", "removeattribute", "csv" };

            foreach (var op in operations)
            {
                foreach (var element in doc.Descendants(op))
                {
                    var xpath = element.Attribute("xpath")?.Value;
                    if (string.IsNullOrEmpty(xpath)) continue;

                    var lineInfo = (IXmlLineInfo)element;
                    var isDestructive = op is "remove" or "removeattribute";
                    var (status, details) = AnalyzeXPathImpact(op, xpath, db);

                    ConsoleColor color;
                    string statusLabel;
                    
                    switch (status)
                    {
                        case ImpactStatus.Conflict:
                            color = ConsoleColor.Red;
                            statusLabel = "CONFLICT";
                            _conflictCount++;
                            break;
                        case ImpactStatus.Caution:
                            color = ConsoleColor.Yellow;
                            statusLabel = "CAUTION";
                            _cautionCount++;
                            break;
                        default:
                            color = ConsoleColor.Green;
                            statusLabel = "OK";
                            break;
                    }

                    Console.ForegroundColor = color;
                    Console.Write($"  [{statusLabel}] ");
                    Console.ResetColor();
                    Console.Write($"<{op}>");
                    if (isDestructive)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.Write(" (destructive)");
                        Console.ResetColor();
                    }
                    Console.WriteLine($" line {lineInfo.LineNumber}");
                    Console.WriteLine($"       xpath: {xpath}");
                    
                    if (!string.IsNullOrEmpty(details))
                        Console.WriteLine($"       {details}");
                    
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error parsing: {ex.Message}");
        }
    }

    private XmlAnalysisResult AnalyzeModXmlToDatabase(string filePath, SqliteConnection db, long modId, string modName)
    {
        int operations = 0, conflicts = 0, cautions = 0;

        try
        {
            var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);
            var ops = new[] { "set", "append", "remove", "insertAfter", "insertBefore", "setattribute", "removeattribute", "csv" };
            var fileName = Path.GetFileName(filePath);

            foreach (var op in ops)
            {
                foreach (var element in doc.Descendants(op))
                {
                    var xpath = element.Attribute("xpath")?.Value;
                    if (string.IsNullOrEmpty(xpath)) continue;

                    operations++;
                    var lineInfo = (IXmlLineInfo)element;
                    var (status, _) = AnalyzeXPathImpact(op, xpath, db);

                    if (status == ImpactStatus.Conflict) conflicts++;
                    else if (status == ImpactStatus.Caution) cautions++;

                    var target = ExtractTargetFromXPath(xpath);
                    var propertyName = ExtractPropertyFromXPath(xpath);
                    var newValue = element.Value?.Trim();
                    if (string.IsNullOrEmpty(newValue))
                        newValue = ExtractValueFromXPath(xpath);

                    var elementContent = element.ToString();
                    if (elementContent.Length > 500)
                        elementContent = elementContent.Substring(0, 500) + "...";

                    // Normalize XPath for conflict detection
                    var normResult = XPathNormalizer.Normalize(xpath);

                    // Extract effect context for passive_effect and triggered_effect analysis
                    var effectContext = XPathNormalizer.ExtractEffectContext(xpath);

                    // Parse effect operation type if this is setting an @operation attribute
                    string? effectOperation = null;
                    string? effectValueType = null;
                    int isSetOperation = 0;

                    if (effectContext.IsOperationChange && !string.IsNullOrEmpty(newValue))
                    {
                        var (parsedOp, parsedValueType, parsedIsSet) = XPathNormalizer.ParseEffectOperation(newValue);
                        effectOperation = parsedOp;
                        effectValueType = parsedValueType;
                        isSetOperation = parsedIsSet ? 1 : 0;
                    }

                    using var cmd = db.CreateCommand();
                    cmd.CommandText = @"INSERT INTO mod_xml_operations
                        (mod_id, operation, xpath, xpath_normalized, xpath_hash, target_type, target_name, property_name, new_value, element_content, file_path, line_number, impact_status,
                         effect_name, effect_operation, effect_value_type, is_set_operation, parent_entity, parent_entity_name,
                         is_triggered_effect, trigger_action, trigger_cvar, trigger_operation, modifies_operation, modifies_value)
                        VALUES ($modId, $op, $xpath, $xpathNorm, $xpathHash, $targetType, $targetName, $propName, $newVal, $content, $file, $line, $status,
                                $effectName, $effectOp, $effectValueType, $isSetOp, $parentEntity, $parentEntityName,
                                $isTriggered, $triggerAction, $triggerCvar, $triggerOp, $modifiesOp, $modifiesVal)";
                    cmd.Parameters.AddWithValue("$modId", modId);
                    cmd.Parameters.AddWithValue("$op", op);
                    cmd.Parameters.AddWithValue("$xpath", xpath);
                    cmd.Parameters.AddWithValue("$xpathNorm", normResult.Normalized);
                    cmd.Parameters.AddWithValue("$xpathHash", normResult.Hash);
                    cmd.Parameters.AddWithValue("$targetType", target?.Type ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$targetName", target?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$propName", propertyName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$newVal", newValue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$content", elementContent ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$file", fileName);
                    cmd.Parameters.AddWithValue("$line", lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0);
                    cmd.Parameters.AddWithValue("$status", status.ToString());
                    // Effect context columns
                    cmd.Parameters.AddWithValue("$effectName", effectContext.EffectName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$effectOp", effectOperation ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$effectValueType", effectValueType ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$isSetOp", isSetOperation);
                    cmd.Parameters.AddWithValue("$parentEntity", effectContext.ParentEntity ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$parentEntityName", effectContext.ParentEntityName ?? (object)DBNull.Value);
                    // Triggered effect columns
                    cmd.Parameters.AddWithValue("$isTriggered", effectContext.IsTriggeredEffect ? 1 : 0);
                    cmd.Parameters.AddWithValue("$triggerAction", effectContext.TriggerAction ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$triggerCvar", effectContext.TriggerCVar ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$triggerOp", effectContext.TriggerOperation ?? (object)DBNull.Value);
                    // Modification tracking
                    cmd.Parameters.AddWithValue("$modifiesOp", effectContext.IsOperationChange ? 1 : 0);
                    cmd.Parameters.AddWithValue("$modifiesVal", effectContext.IsValueChange ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        catch { /* Skip unparseable files */ }

        return new XmlAnalysisResult(operations, conflicts, cautions);
    }

    private void PrintModSummary()
    {
        Console.WriteLine("─────────────────────────────────────────");
        if (_conflictCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"⚠ CONFLICTS DETECTED: {_conflictCount} operation(s) break existing references!");
            Console.ResetColor();
        }
        else if (_cautionCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚡ CAUTION: {_cautionCount} destructive operation(s), but no conflicts with base game detected.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ SAFE: No conflicts detected with base game.");
            Console.ResetColor();
        }
    }

    private void PrintAnalysisResults(
        List<ModResult> results, 
        List<CSharpDependency> allDependencies,
        List<(string CSharpMod, string XmlMod, CSharpDependency Dep, XmlRemoval Removal)> crossModConflicts,
        long elapsedMs,
        string modsFolder)
    {
        var totalConflicts = results.Sum(r => r.Conflicts);
        var totalCautions = results.Sum(r => r.Cautions);
        var safeCount = 0;

        Console.WriteLine("┌────────────────────────────────────────────────────┬──────────┬──────────┐");
        Console.WriteLine("│ Mod Name                                           │  Status  │ C# Deps  │");
        Console.WriteLine("├────────────────────────────────────────────────────┼──────────┼──────────┤");

        foreach (var result in results)
        {
            var displayName = result.Name.Length > 50 ? result.Name.Substring(0, 47) + "..." : result.Name;
            var depCount = result.Dependencies.Count;
            string status;
            ConsoleColor color;

            if (result.Conflicts > 0)
            {
                status = "⚠ CONFLICT";
                color = ConsoleColor.Red;
            }
            else if (result.Cautions > 0)
            {
                status = "⚡ CAUTION";
                color = ConsoleColor.Yellow;
            }
            else if (result.IsCodeOnly)
            {
                status = depCount > 0 ? "◆ C# Only" : "□ Assets";
                color = depCount > 0 ? ConsoleColor.Cyan : ConsoleColor.DarkGray;
            }
            else
            {
                status = "✓ OK";
                color = ConsoleColor.Green;
                safeCount++;
            }

            Console.Write($"│ {displayName,-50} │ ");
            Console.ForegroundColor = color;
            Console.Write($"{status,8}");
            Console.ResetColor();
            Console.Write($" │ ");
            if (depCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{depCount,8}");
                Console.ResetColor();
            }
            else
            {
                Console.Write($"{"—",8}");
            }
            Console.WriteLine(" │");
        }

        Console.WriteLine("└────────────────────────────────────────────────────┴──────────┴──────────┘");
        Console.WriteLine();

        Console.WriteLine($"Analyzed {results.Count} mods in {elapsedMs}ms:");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ {safeCount} XML mods safe (no issues)");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ⚡ {results.Count(r => r.Cautions > 0 && r.Conflicts == 0)} with cautions ({totalCautions} destructive ops)");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ⚠ {results.Count(r => r.Conflicts > 0)} with base game conflicts ({totalConflicts} broken refs)");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  ◆ {results.Count(r => r.Dependencies.Count > 0)} C# mods with {allDependencies.Count} XML dependencies");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  □ {results.Count(r => r.IsCodeOnly && r.Dependencies.Count == 0)} code-only (no XML deps detected)");
        Console.ResetColor();

        if (crossModConflicts.Any())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  ⚠ CROSS-MOD CONFLICTS DETECTED: {crossModConflicts.Count}");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            foreach (var conflict in crossModConflicts)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  ⚠ ");
                Console.ResetColor();
                Console.Write($"C# mod ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(conflict.CSharpMod);
                Console.ResetColor();
                Console.Write($" needs {conflict.Dep.Type} ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"'{conflict.Dep.Name}'");
                Console.ResetColor();
                Console.WriteLine();
                
                Console.Write($"     but XML mod ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(conflict.XmlMod);
                Console.ResetColor();
                Console.WriteLine($" removes it!");
                Console.WriteLine();
            }
        }
    }

    /// <summary>
    /// Scans decompiled mod DLLs for detailed Harmony patch information and stores in database.
    /// </summary>
    private void ScanAndStoreHarmonyPatches(string modDir, string modName, long modId, SqliteConnection db)
    {
        var modDlls = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
            .Where(dll => !Path.GetFileName(dll).StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Equals("Mono.Cecil.dll", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Contains("System."))
            .Where(dll => !Path.GetFileName(dll).Contains("Microsoft."))
            .ToList();
        
        if (modDlls.Count == 0) return;

        int totalPatches = 0;
        foreach (var dllPath in modDlls)
        {
            var csFiles = CSharpAnalyzer.DecompileModDll(dllPath, modName);
            if (csFiles.Count == 0) continue;

            foreach (var csFile in csFiles)
            {
                try
                {
                    var content = File.ReadAllText(csFile);
                    var fileName = Path.GetFileName(csFile);

                    // Use detailed Harmony patch scanning
                    var patches = CSharpAnalyzer.ScanHarmonyPatchesDetailed(content, modId, fileName);

                    foreach (var patch in patches)
                    {
                        PersistHarmonyPatch(db, patch);
                        totalPatches++;
                    }
                }
                catch { /* Skip unreadable files */ }
            }
        }
        
        if (totalPatches > 0)
        {
            Console.WriteLine($"      Found {totalPatches} Harmony patches in {modName}");
        }
    }

    /// <summary>
    /// Persists a single Harmony patch to the database.
    /// </summary>
    private void PersistHarmonyPatch(SqliteConnection db, HarmonyPatchInfo patch)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO harmony_patches
            (mod_id, patch_class, target_class, target_method, patch_type,
             target_member_kind, target_arg_types, target_declaring_type,
             harmony_priority, harmony_before, harmony_after,
             returns_bool, modifies_result, modifies_state,
             is_guarded, guard_condition, is_dynamic,
             parameter_signature, code_snippet, source_file, line_number)
            VALUES ($modId, $patchClass, $targetClass, $targetMethod, $patchType,
                    $memberKind, $argTypes, $declaringType,
                    $priority, $before, $after,
                    $returnsBool, $modifiesResult, $modifiesState,
                    $isGuarded, $guardCondition, $isDynamic,
                    $paramSig, $snippet, $file, $line)";

        cmd.Parameters.AddWithValue("$modId", patch.ModId);
        cmd.Parameters.AddWithValue("$patchClass", patch.PatchClass);
        cmd.Parameters.AddWithValue("$targetClass", patch.TargetClass);
        cmd.Parameters.AddWithValue("$targetMethod", patch.TargetMethod);
        cmd.Parameters.AddWithValue("$patchType", patch.PatchType);
        cmd.Parameters.AddWithValue("$memberKind", patch.TargetMemberKind ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$argTypes", patch.TargetArgTypes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$declaringType", patch.TargetDeclaringType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", patch.HarmonyPriority);
        cmd.Parameters.AddWithValue("$before", patch.HarmonyBefore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$after", patch.HarmonyAfter ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$returnsBool", patch.ReturnsBool ? 1 : 0);
        cmd.Parameters.AddWithValue("$modifiesResult", patch.ModifiesResult ? 1 : 0);
        cmd.Parameters.AddWithValue("$modifiesState", patch.ModifiesState ? 1 : 0);
        cmd.Parameters.AddWithValue("$isGuarded", patch.IsGuarded ? 1 : 0);
        cmd.Parameters.AddWithValue("$guardCondition", patch.GuardCondition ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$isDynamic", patch.IsDynamic ? 1 : 0);
        cmd.Parameters.AddWithValue("$paramSig", patch.ParameterSignature ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$snippet", patch.CodeSnippet ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$file", patch.SourceFile ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$line", patch.LineNumber ?? (object)DBNull.Value);

        cmd.ExecuteNonQuery();
    }
}
