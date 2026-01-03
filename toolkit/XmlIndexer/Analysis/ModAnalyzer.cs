using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Analysis;

/// <summary>
/// Analyzes mod content: XML operations, C# dependencies, and cross-mod conflicts.
/// </summary>
public class ModAnalyzer
{
    private readonly string _dbPath;
    private int _conflictCount;
    private int _cautionCount;

    // Cache for decompiled mod code
    private static readonly Dictionary<string, string> _decompileCache = new();
    private static string? _decompiledModsDir;

    public ModAnalyzer(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Analyzes all mods in a folder, detecting conflicts and C# dependencies.
    /// </summary>
    public int AnalyzeAllMods(string modsFolder)
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

        var results = new List<ModResult>();
        var allRemovals = new List<XmlRemoval>();
        var allDependencies = new List<CSharpDependency>();

        // PHASE 1: Scan all mods
        Console.WriteLine("[Phase 1] Scanning mods...");
        foreach (var modDir in modDirs)
        {
            var modName = Path.GetFileName(modDir);
            var configPath = Path.Combine(modDir, "Config");

            _conflictCount = 0;
            _cautionCount = 0;

            var deps = ScanCSharpDependencies(modDir, modName);
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
            foreach (var xmlFile in xmlFiles)
            {
                AnalyzeModXmlSilent(xmlFile, db, modName, removals);
            }
            allRemovals.AddRange(removals);

            results.Add(new ModResult(modName, _conflictCount, _cautionCount, false, deps));
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
    /// </summary>
    public void PersistModAnalysis(string modsFolder, SqliteConnection db)
    {
        var modDirs = Directory.GetDirectories(modsFolder)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .ToList();

        Console.WriteLine($"Persisting analysis for {modDirs.Count} mods...");

        using var transaction = db.BeginTransaction();

        foreach (var modDir in modDirs)
        {
            var modName = Path.GetFileName(modDir);
            var configPath = Path.Combine(modDir, "Config");
            var hasXml = Directory.Exists(configPath) && Directory.GetFiles(configPath, "*.xml").Any();
            var hasDll = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
                .Any(f => !Path.GetFileName(f).StartsWith("0Harmony") && 
                          !Path.GetFileName(f).Contains("System.") &&
                          !Path.GetFileName(f).Contains("Microsoft."));

            // Read ModInfo.xml
            var modInfo = ReadModInfo(modDir);

            // Insert mod record
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"INSERT OR REPLACE INTO mods 
                    (name, has_xml, has_dll, display_name, description, author, version, website)
                    VALUES ($name, $xml, $dll, $displayName, $desc, $author, $version, $website)";
                cmd.Parameters.AddWithValue("$name", modName);
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
            var deps = ScanCSharpDependencies(modDir, modName);
            foreach (var dep in deps)
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"INSERT INTO mod_csharp_deps 
                    (mod_id, dependency_type, dependency_name, source_file, line_number, pattern)
                    VALUES ($modId, $type, $name, $file, $line, $pattern)";
                cmd.Parameters.AddWithValue("$modId", modId);
                cmd.Parameters.AddWithValue("$type", dep.Type);
                cmd.Parameters.AddWithValue("$name", dep.Name);
                cmd.Parameters.AddWithValue("$file", dep.SourceFile);
                cmd.Parameters.AddWithValue("$line", dep.Line);
                cmd.Parameters.AddWithValue("$pattern", dep.Pattern);
                cmd.ExecuteNonQuery();
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
        }

        transaction.Commit();
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
    // C# Dependency Scanning
    // ==========================================================================

    public List<CSharpDependency> ScanCSharpDependencies(string modDir, string modName)
    {
        var deps = new List<CSharpDependency>();
        var patterns = GetXmlDependencyPatterns();

        var modDlls = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
            .Where(dll => !Path.GetFileName(dll).StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Equals("Mono.Cecil.dll", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Contains("System."))
            .Where(dll => !Path.GetFileName(dll).Contains("Microsoft."))
            .ToList();

        if (modDlls.Count == 0)
            return deps;

        foreach (var dllPath in modDlls)
        {
            var csFiles = DecompileModDll(dllPath, modName);
            if (csFiles.Count == 0) continue;

            foreach (var csFile in csFiles)
            {
                try
                {
                    var content = File.ReadAllText(csFile);
                    var lines = content.Split('\n');
                    var fileName = Path.GetFileName(csFile);

                    foreach (var (pattern, type, nameGroup) in patterns)
                    {
                        var regex = new Regex(pattern, RegexOptions.Compiled);
                        
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var matches = regex.Matches(lines[i]);
                            foreach (Match match in matches)
                            {
                                var name = nameGroup > 0 ? match.Groups[nameGroup].Value : match.Value;
                                if (!string.IsNullOrEmpty(name) && !name.Contains("{") && !name.Contains("+"))
                                {
                                    deps.Add(new CSharpDependency(modName, type, name, fileName, i + 1, match.Value.Trim()));
                                }
                            }
                        }
                    }
                }
                catch { /* Skip unreadable files */ }
            }
        }

        return deps;
    }

    public static (string Pattern, string Type, int NameGroup)[] GetXmlDependencyPatterns()
    {
        return new (string Pattern, string Type, int NameGroup)[]
        {
            // Item lookups
            (@"ItemClass\.GetItem\s*\(\s*""([^""]+)""\s*[,\)]", "item", 1),
            (@"ItemClass\.GetItemClass\s*\(\s*""([^""]+)""\s*\)", "item", 1),
            (@"new\s+ItemValue\s*\(\s*ItemClass\.GetItem\s*\(\s*""([^""]+)""\s*\)", "item", 1),
            
            // Block lookups
            (@"Block\.GetBlockByName\s*\(\s*""([^""]+)""\s*\)", "block", 1),
            (@"Block\.GetBlockValue\s*\(\s*""([^""]+)""\s*\)", "block", 1),
            
            // Entity lookups
            (@"EntityClass\.FromString\s*\(\s*""([^""]+)""\s*\)", "entity_class", 1),
            (@"EntityFactory\.CreateEntity\s*\([^,]*,\s*""([^""]+)""\s*\)", "entity_class", 1),
            
            // Buff lookups
            (@"BuffManager\.GetBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"BuffClass\.GetBuffClass\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"\.AddBuff\s*\(\s*""([^""]+)""\s*[\),]", "buff", 1),
            (@"\.RemoveBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"\.HasBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            
            // Harmony patches
            (@"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)", "harmony_class", 1),
            (@"\[HarmonyPatch\s*\(\s*""([^""]+)""\s*\)", "harmony_method", 1),
            (@"\[HarmonyPrefix\]", "harmony_prefix", 0),
            (@"\[HarmonyPostfix\]", "harmony_postfix", 0),
            (@"\[HarmonyTranspiler\]", "harmony_transpiler", 0),
            
            // Inheritance patterns
            (@":\s*(ItemAction\w*)\b", "extends_itemaction", 1),
            (@":\s*(Block\w*)\b", "extends_block", 1),
            (@":\s*(EntityAlive|Entity\w*)\b", "extends_entity", 1),
            (@":\s*(MinEventAction\w*)\b", "extends_mineventaction", 1),
            (@":\s*(IModApi)\b", "implements_imodapi", 1),
        };
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    public static XPathTarget? ExtractTargetFromXPath(string xpath)
    {
        var patterns = new Dictionary<string, string>
        {
            { @"/items/item\[@name='([^']+)'\]", "item" },
            { @"/blocks/block\[@name='([^']+)'\]", "block" },
            { @"/entity_classes/entity_class\[@name='([^']+)'\]", "entity_class" },
            { @"/buffs/buff\[@name='([^']+)'\]", "buff" },
            { @"/recipes/recipe\[@name='([^']+)'\]", "recipe" },
            { @"/Sounds/SoundDataNode\[@name='([^']+)'\]", "sound" },
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(xpath, pattern.Key);
            if (match.Success)
                return new XPathTarget(pattern.Value, match.Groups[1].Value);
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

                    using var cmd = db.CreateCommand();
                    cmd.CommandText = @"INSERT INTO mod_xml_operations 
                        (mod_id, operation, xpath, target_type, target_name, property_name, new_value, element_content, file_path, line_number, impact_status)
                        VALUES ($modId, $op, $xpath, $targetType, $targetName, $propName, $newVal, $content, $file, $line, $status)";
                    cmd.Parameters.AddWithValue("$modId", modId);
                    cmd.Parameters.AddWithValue("$op", op);
                    cmd.Parameters.AddWithValue("$xpath", xpath);
                    cmd.Parameters.AddWithValue("$targetType", target?.Type ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$targetName", target?.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$propName", propertyName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$newVal", newValue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$content", elementContent ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$file", fileName);
                    cmd.Parameters.AddWithValue("$line", lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0);
                    cmd.Parameters.AddWithValue("$status", status.ToString());
                    cmd.ExecuteNonQuery();
                }
            }
        }
        catch { /* Skip unparseable files */ }

        return new XmlAnalysisResult(operations, conflicts, cautions);
    }

    private List<string> DecompileModDll(string dllPath, string modName)
    {
        var csFiles = new List<string>();
        
        if (_decompileCache.TryGetValue(dllPath, out var cachedDir))
        {
            if (Directory.Exists(cachedDir))
                return Directory.GetFiles(cachedDir, "*.cs", SearchOption.AllDirectories).ToList();
        }

        if (_decompiledModsDir == null)
        {
            _decompiledModsDir = Path.Combine(Path.GetTempPath(), "XmlIndexer_ModDecompile_" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(_decompiledModsDir);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupDecompiledMods();
        }

        var dllName = Path.GetFileNameWithoutExtension(dllPath);
        var outputDir = Path.Combine(_decompiledModsDir, modName, dllName);
        
        try
        {
            Directory.CreateDirectory(outputDir);
            
            var psi = new ProcessStartInfo
            {
                FileName = "ilspycmd",
                Arguments = $"\"{dllPath}\" -p -o \"{outputDir}\" -lv Latest",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return csFiles;

            process.WaitForExit(30000);
            
            if (process.ExitCode == 0 && Directory.Exists(outputDir))
            {
                csFiles = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories).ToList();
                _decompileCache[dllPath] = outputDir;
            }
        }
        catch { /* ilspycmd not installed or other error */ }

        return csFiles;
    }

    private static void CleanupDecompiledMods()
    {
        if (_decompiledModsDir != null && Directory.Exists(_decompiledModsDir))
        {
            try { Directory.Delete(_decompiledModsDir, true); }
            catch { /* Best effort */ }
        }
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
}
