using System.Xml;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XmlIndexer.Analysis;
using XmlIndexer.Commands;
using XmlIndexer.Models;
using XmlIndexer.Parsers;
using XmlIndexer.Reports;
using XmlIndexer.Utils;

namespace XmlIndexer;

/// <summary>
/// Indexes 7 Days to Die XML configuration files into a SQLite database
/// for cross-reference analysis with C# code and mod conflict detection.
/// Fast in-memory processing with bulk database writes.
/// </summary>
public class Program
{
    private static string? _gamePath;
    private static string? _dbPath;
    private static bool _verbose = false;

    // In-memory data structures for batch processing
    private static readonly List<XmlDefinition> _definitions = new();
    private static readonly List<XmlProperty> _properties = new();
    private static readonly List<XmlReference> _references = new();
    private static readonly Dictionary<string, int> _stats = new();
    private static long _nextDefId = 1;

    // Data models defined in Models/DataModels.cs

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLower();

        switch (command)
        {
            case "build":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer build <game_path> <output_db>");
                    return 1;
                }
                _gamePath = args[1];
                _dbPath = args[2];
                _verbose = args.Contains("-v") || args.Contains("--verbose");
                return BuildDatabase();

            case "full-analyze":
                if (args.Length < 4)
                {
                    Console.WriteLine("Usage: XmlIndexer full-analyze <game_path> <mods_folder> <output_db>");
                    return 1;
                }
                _gamePath = args[1];
                var modsPath = args[2];
                _dbPath = args[3];
                _verbose = args.Contains("-v") || args.Contains("--verbose");
                return FullAnalyze(modsPath);

            case "analyze-mod":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer analyze-mod <db_path> <mod_path>");
                    return 1;
                }
                _dbPath = args[1];
                var modPath = args[2];
                return AnalyzeMod(modPath);

            case "analyze-mods":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer analyze-mods <db_path> <mods_folder>");
                    return 1;
                }
                _dbPath = args[1];
                var modsFolder = args[2];
                return AnalyzeAllMods(modsFolder);

            case "stats":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer stats <db_path>");
                    return 1;
                }
                _dbPath = args[1];
                return StatsCommand.Execute(_dbPath);

            case "ecosystem":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer ecosystem <db_path>");
                    return 1;
                }
                _dbPath = args[1];
                return EcosystemCommand.Execute(_dbPath);

            case "refs":
                if (args.Length < 4)
                {
                    Console.WriteLine("Usage: XmlIndexer refs <db_path> <type> <name>");
                    return 1;
                }
                _dbPath = args[1];
                return FindReferences(args[2], args[3]);

            case "list":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer list <db_path> <type>");
                    return 1;
                }
                _dbPath = args[1];
                return ListDefinitions(args[2]);

            case "search":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer search <db_path> <pattern>");
                    return 1;
                }
                _dbPath = args[1];
                return SearchDefinitions(args[2]);

            case "report":
                if (args.Length < 4)
                {
                    Console.WriteLine("Usage: XmlIndexer report <game_path> <mods_folder> <output_dir> [--open] [--codebase <path>]");
                    return 1;
                }
                _gamePath = args[1];
                var reportModsPath = args[2];
                var outputDir = args[3];
                _dbPath = Path.Combine(outputDir, "ecosystem.db");
                var openAfter = !args.Contains("--no-open"); // Default to opening browser
                
                // Parse --codebase argument
                string? reportCodebasePath = null;
                for (int i = 4; i < args.Length - 1; i++)
                {
                    if (args[i] == "--codebase")
                    {
                        reportCodebasePath = args[i + 1];
                        break;
                    }
                }
                
                return GenerateFullReport(reportModsPath, outputDir, openAfter, reportCodebasePath);

            case "export-semantic-traces":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer export-semantic-traces <db_path> <output.jsonl>");
                    return 1;
                }
                _dbPath = args[1];
                return new Semantic.SemanticService(_dbPath).ExportSemanticTraces(args[2]);

            case "import-semantic-mappings":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer import-semantic-mappings <db_path> <mappings.jsonl>");
                    return 1;
                }
                _dbPath = args[1];
                return new Semantic.SemanticService(_dbPath).ImportSemanticMappings(args[2]);

            case "semantic-status":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer semantic-status <db_path>");
                    return 1;
                }
                _dbPath = args[1];
                return new Semantic.SemanticService(_dbPath).ShowSemanticStatus();

            case "mod-details":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer mod-details <db_path> <mod_name_pattern>");
                    return 1;
                }
                _dbPath = args[1];
                return ModDetailsCommand.Execute(_dbPath, args[2]);

            case "detect-conflicts":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer detect-conflicts <db_path> [--callgraph-db <path>]");
                    return 1;
                }
                _dbPath = args[1];
                string? callgraphDb = null;
                for (int i = 2; i < args.Length - 1; i++)
                {
                    if (args[i] == "--callgraph-db")
                    {
                        callgraphDb = args[i + 1];
                        break;
                    }
                }
                return DetectConflicts(callgraphDb);

            case "build-dependency-graph":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer build-dependency-graph <db_path>");
                    return 1;
                }
                _dbPath = args[1];
                return BuildDependencyGraph();

            case "impact-analysis":
                if (args.Length < 4)
                {
                    Console.WriteLine("Usage: XmlIndexer impact-analysis <db_path> <type> <name>");
                    Console.WriteLine("  Example: XmlIndexer impact-analysis eco.db buff buffCoffeeBuzz");
                    return 1;
                }
                _dbPath = args[1];
                return ImpactCommand.Execute(_dbPath, args[2], args[3]);

            case "query":
            case "sql":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer query <db_path> \"<sql_query>\"");
                    Console.WriteLine("  Example: XmlIndexer query eco.db \"SELECT * FROM mods LIMIT 5\"");
                    return 1;
                }
                _dbPath = args[1];
                return QueryCommand.Execute(_dbPath, string.Join(" ", args.Skip(2)));

            case "xpath-test":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer xpath-test \"<xpath>\"");
                    return 1;
                }
                return TestXPathParsing(string.Join(" ", args.Skip(1)));

            case "analyze-game":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer analyze-game <db_path> <game_codebase_path> [--force]");
                    Console.WriteLine("  Analyzes decompiled game code for potential bugs, stubs, and hidden features.");
                    Console.WriteLine("  --force  Force re-analysis of all files (skip incremental hash check)");
                    return 1;
                }
                _dbPath = args[1];
                var gameCodePath = args[2];
                var forceAnalyze = args.Contains("--force");
                return AnalyzeGameCode(gameCodePath, forceAnalyze);

            case "index-game":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer index-game <db_path> <game_codebase_path> [--force]");
                    Console.WriteLine("  Indexes method signatures and class inheritance from decompiled game code.");
                    Console.WriteLine("  --force  Force re-index of all files");
                    return 1;
                }
                _dbPath = args[1];
                var indexCodePath = args[2];
                var forceIndex = args.Contains("--force");
                return IndexGameCode(indexCodePath, forceIndex);

            case "detect-harmony-conflicts":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer detect-harmony-conflicts <db_path>");
                    Console.WriteLine("  Detects Harmony patch conflicts between mods.");
                    return 1;
                }
                _dbPath = args[1];
                return DetectHarmonyConflicts();

            // ═══════════════════════════════════════════════════════════════════════════
            // TESTING COMMANDS
            // ═══════════════════════════════════════════════════════════════════════════

            case "test-hasher":
                return Tests.ContentHasherTests.Run();

            case "test-jsclick":
                return Tests.JsClickBuilderTests.Run();

            case "test-all":
                Console.WriteLine("Running all unit tests...\n");
                var hasherResult = Tests.ContentHasherTests.Run();
                Console.WriteLine();
                var jsclickResult = Tests.JsClickBuilderTests.Run();
                Console.WriteLine($"\n=== OVERALL: {(hasherResult + jsclickResult == 0 ? "ALL PASSED" : "SOME FAILED")} ===");
                return (hasherResult + jsclickResult) > 0 ? 1 : 0;

            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("7D2D Mod Ecosystem Analyzer - Complete modding analysis toolkit");
        Console.WriteLine();
        Console.WriteLine("FULL WORKFLOW (recommended):");
        Console.WriteLine("  full-analyze <game> <mods> <db>    Complete analysis with timing breakdown");
        Console.WriteLine();
        Console.WriteLine("INDIVIDUAL COMMANDS:");
        Console.WriteLine("  build <game_path> <output_db>      Build XML index from game data");
        Console.WriteLine("  analyze-mod <db_path> <mod_path>   Analyze single mod for conflicts");
        Console.WriteLine("  analyze-mods <db_path> <mods_dir>  Analyze ALL mods in a folder");
        Console.WriteLine();
        Console.WriteLine("INSIGHTS:");
        Console.WriteLine("  stats <db_path>                    Fun statistics about game + mods");
        Console.WriteLine("  query <db_path> \"<sql>\"            Run ad-hoc SQL query on database");
        Console.WriteLine("  ecosystem <db_path>                Combined codebase+mods ecosystem view");
        Console.WriteLine("  refs <db_path> <type> <name>       Find all references to an entity");
        Console.WriteLine("  list <db_path> <type>              List all definitions of a type");
        Console.WriteLine("  search <db_path> <pattern>         Search definitions by name");
        Console.WriteLine();
        Console.WriteLine("REPORTS:");
        Console.WriteLine("  report <game> <mods> <output_dir>  Full rebuild + HTML report (single command)");
        Console.WriteLine("    --open                           Open report in browser after generation");
        Console.WriteLine();
        Console.WriteLine("CONFLICT DETECTION:");
        Console.WriteLine("  detect-conflicts <db_path>         Detect XPath-level conflicts (JSON output)");
        Console.WriteLine("    --callgraph-db <path>            Path to callgraph_full.db for C#/XML analysis");
        Console.WriteLine("  detect-harmony-conflicts <db>      Detect Harmony patch conflicts between mods");
        Console.WriteLine("  build-dependency-graph <db>        Build transitive references + indirect conflicts");
        Console.WriteLine("  impact-analysis <db> <type> <name> Show what depends on an entity");
        Console.WriteLine();
        Console.WriteLine("GAME CODE ANALYSIS:");
        Console.WriteLine("  analyze-game <db> <codebase_path>  Analyze game code for bugs and opportunities");
        Console.WriteLine("    --force                          Force re-analysis of all files");
        Console.WriteLine("  index-game <db> <codebase_path>    Index method signatures and inheritance");
        Console.WriteLine("    --force                          Force re-index of all files");
        Console.WriteLine();
        Console.WriteLine("SEMANTIC ANALYSIS (LLM-powered descriptions):");
        Console.WriteLine("  export-semantic-traces <db> <out>  Export traces for LLM analysis");
        Console.WriteLine("  import-semantic-mappings <db> <in> Import LLM-generated descriptions");
        Console.WriteLine("  semantic-status <db>               Show semantic mapping coverage");
        Console.WriteLine();
        Console.WriteLine("TYPES: item, block, entity_class, buff, recipe, sound, vehicle, quest");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  XmlIndexer report \"C:\\Steam\\...\\7 Days To Die\" \"...\\Mods\" ./reports --open");
        Console.WriteLine("  XmlIndexer query eco.db \"SELECT * FROM mods LIMIT 5\"");
        Console.WriteLine("  XmlIndexer refs eco.db item itemRepairKit");
    }

    private static int BuildDatabase()
    {
        var sw = Stopwatch.StartNew();
        var configPath = Path.Combine(_gamePath!, "Data", "Config");
        if (!Directory.Exists(configPath))
        {
            Console.WriteLine($"Error: Config directory not found at {configPath}");
            return 1;
        }

        Console.WriteLine($"Building XML index from: {configPath}");
        Console.WriteLine($"Output database: {_dbPath}");

        // Delete existing database
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);

        // PHASE 1: Read all XML files into memory in parallel
        Console.WriteLine("\n[Phase 1] Loading XML files into memory...");
        var readSw = Stopwatch.StartNew();
        var xmlFiles = new ConcurrentDictionary<string, XDocument>();
        var filesToLoad = new[]
        {
            "items.xml", "blocks.xml", "entityclasses.xml", "entitygroups.xml",
            "buffs.xml", "recipes.xml", "sounds.xml", "vehicles.xml",
            "quests.xml", "gameevents.xml", "progression.xml", "loot.xml"
        };

        Parallel.ForEach(filesToLoad, fileName =>
        {
            var filePath = Path.Combine(configPath, fileName);
            if (File.Exists(filePath))
            {
                // Read entire file to memory, then parse
                var content = File.ReadAllText(filePath);
                var doc = XDocument.Parse(content, LoadOptions.SetLineInfo);
                xmlFiles[fileName] = doc;
            }
        });
        Console.WriteLine($"  Loaded {xmlFiles.Count} files in {readSw.ElapsedMilliseconds}ms");

        // PHASE 2: Process all XML in memory
        Console.WriteLine("\n[Phase 2] Processing XML data...");
        var processSw = Stopwatch.StartNew();
        
        // Use XmlParsers class (single code path)
        var parsers = new XmlParsers(_definitions, _properties, _references, _stats);
        
        if (xmlFiles.TryGetValue("items.xml", out var itemsDoc))
            parsers.ParseItems(itemsDoc);
        if (xmlFiles.TryGetValue("blocks.xml", out var blocksDoc))
            parsers.ParseBlocks(blocksDoc);
        if (xmlFiles.TryGetValue("entityclasses.xml", out var entityDoc))
            parsers.ParseEntityClasses(entityDoc);
        if (xmlFiles.TryGetValue("entitygroups.xml", out var groupsDoc))
            parsers.ParseEntityGroups(groupsDoc);
        if (xmlFiles.TryGetValue("buffs.xml", out var buffsDoc))
            parsers.ParseBuffs(buffsDoc);
        if (xmlFiles.TryGetValue("recipes.xml", out var recipesDoc))
            parsers.ParseRecipes(recipesDoc);
        if (xmlFiles.TryGetValue("sounds.xml", out var soundsDoc))
            parsers.ParseSounds(soundsDoc);
        if (xmlFiles.TryGetValue("vehicles.xml", out var vehiclesDoc))
            parsers.ParseVehicles(vehiclesDoc);
        if (xmlFiles.TryGetValue("quests.xml", out var questsDoc))
            parsers.ParseQuests(questsDoc);
        if (xmlFiles.TryGetValue("gameevents.xml", out var eventsDoc))
            parsers.ParseGameEvents(eventsDoc);
        if (xmlFiles.TryGetValue("progression.xml", out var progDoc))
            parsers.ParseProgression(progDoc);
        if (xmlFiles.TryGetValue("loot.xml", out var lootDoc))
            parsers.ParseLoot(lootDoc);

        // Build extends cross-references in memory
        parsers.BuildExtendsReferences();

        Console.WriteLine($"  Processed {_definitions.Count} definitions, {_properties.Count} properties, {_references.Count} references in {processSw.ElapsedMilliseconds}ms");

        // PHASE 3: Bulk write to database
        Console.WriteLine("\n[Phase 3] Writing to database...");
        var writeSw = Stopwatch.StartNew();
        BulkWriteToDatabase();
        Console.WriteLine($"  Database written in {writeSw.ElapsedMilliseconds}ms");

        // Print stats
        Console.WriteLine("\n=== Index Statistics ===");
        foreach (var stat in _stats.OrderByDescending(s => s.Value))
        {
            Console.WriteLine($"  {stat.Key,-20} {stat.Value,6}");
        }
        Console.WriteLine($"\n  Total definitions:    {_definitions.Count}");
        Console.WriteLine($"  Total properties:     {_properties.Count}");
        Console.WriteLine($"  Total cross-refs:     {_references.Count}");

        Console.WriteLine($"\n✓ Database built successfully in {sw.ElapsedMilliseconds}ms total!");
        return 0;
    }

    private static void BulkWriteToDatabase()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Create complete schema (includes Harmony, GameCode analysis tables)
        Database.DatabaseBuilder.CreateSchema(db);

        // Bulk insert using transactions
        using var transaction = db.BeginTransaction();

        // Insert definitions
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO xml_definitions (id, definition_type, name, file_path, line_number, extends) VALUES ($id, $type, $name, $file, $line, $extends)";
            var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
            var pType = cmd.Parameters.Add("$type", SqliteType.Text);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pFile = cmd.Parameters.Add("$file", SqliteType.Text);
            var pLine = cmd.Parameters.Add("$line", SqliteType.Integer);
            var pExtends = cmd.Parameters.Add("$extends", SqliteType.Text);

            foreach (var def in _definitions)
            {
                pId.Value = def.Id;
                pType.Value = def.Type;
                pName.Value = def.Name;
                pFile.Value = def.File;
                pLine.Value = def.Line;
                pExtends.Value = def.Extends ?? (object)DBNull.Value;
                cmd.ExecuteNonQuery();
            }
        }

        // Insert properties
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO xml_properties (definition_id, property_name, property_value, property_class, line_number) VALUES ($defId, $name, $value, $class, $line)";
            var pDefId = cmd.Parameters.Add("$defId", SqliteType.Integer);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pValue = cmd.Parameters.Add("$value", SqliteType.Text);
            var pClass = cmd.Parameters.Add("$class", SqliteType.Text);
            var pLine = cmd.Parameters.Add("$line", SqliteType.Integer);

            foreach (var prop in _properties)
            {
                pDefId.Value = prop.DefId;
                pName.Value = prop.Name;
                pValue.Value = prop.Value ?? (object)DBNull.Value;
                pClass.Value = prop.Class ?? (object)DBNull.Value;
                pLine.Value = prop.Line;
                cmd.ExecuteNonQuery();
            }
        }

        // Insert references
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO xml_references (source_type, source_def_id, source_file, source_line, target_type, target_name, reference_context) VALUES ($srcType, $srcId, $srcFile, $line, $tgtType, $tgtName, $ctx)";
            var pSrcType = cmd.Parameters.Add("$srcType", SqliteType.Text);
            var pSrcId = cmd.Parameters.Add("$srcId", SqliteType.Integer);
            var pSrcFile = cmd.Parameters.Add("$srcFile", SqliteType.Text);
            var pLine = cmd.Parameters.Add("$line", SqliteType.Integer);
            var pTgtType = cmd.Parameters.Add("$tgtType", SqliteType.Text);
            var pTgtName = cmd.Parameters.Add("$tgtName", SqliteType.Text);
            var pCtx = cmd.Parameters.Add("$ctx", SqliteType.Text);

            foreach (var r in _references)
            {
                pSrcType.Value = r.SrcType;
                pSrcId.Value = r.SrcDefId ?? (object)DBNull.Value;
                pSrcFile.Value = r.SrcFile;
                pLine.Value = r.Line;
                pTgtType.Value = r.TgtType;
                pTgtName.Value = r.TgtName;
                pCtx.Value = r.Context;
                cmd.ExecuteNonQuery();
            }
        }

        // Insert stats
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO xml_stats (definition_type, count) VALUES ($type, $count)";
            var pType = cmd.Parameters.Add("$type", SqliteType.Text);
            var pCount = cmd.Parameters.Add("$count", SqliteType.Integer);

            foreach (var stat in _stats)
            {
                pType.Value = stat.Key;
                pCount.Value = stat.Value;
                cmd.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private static long AddDefinition(string type, string name, string file, int line, string? extends)
    {
        var id = _nextDefId++;
        _definitions.Add(new XmlDefinition(id, type, name, file, line, extends));
        return id;
    }

    private static void AddProperty(long defId, string name, string? value, string? propClass, int line)
    {
        _properties.Add(new XmlProperty(defId, name, value, propClass, line));
    }

    private static void AddReference(string srcType, long? srcDefId, string srcFile, int line, string tgtType, string tgtName, string ctx)
    {
        _references.Add(new XmlReference(srcType, srcDefId, srcFile, line, tgtType, tgtName, ctx));
    }

    private static void UpdateStats(string type, int count)
    {
        _stats[type] = count;
    }

    // Parse functions moved to Parsers/XmlParsers.cs - single code path

    private static int _conflictCount = 0;
    private static int _cautionCount = 0;

    // ModResult and XmlRemoval defined in Models/DataModels.cs

    private static int AnalyzeAllMods(string modsFolder)
    {
        if (!Directory.Exists(modsFolder))
        {
            Console.WriteLine($"Error: Mods folder not found: {modsFolder}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var modDirs = Directory.GetDirectories(modsFolder)
            .Where(d => !Path.GetFileName(d).StartsWith(".")) // Skip hidden folders
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

            // Scan for C# dependencies (DLLs and source files)
            var deps = CSharpAnalyzer.ScanCSharpDependencies(modDir, modName);
            allDependencies.AddRange(deps);

            // Check for XML config
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

            // Analyze XML and track removals
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
                // Check if this removal affects this dependency
                if (removal.Type == dep.Type && removal.Name == dep.Name)
                {
                    crossModConflicts.Add((dep.ModName, removal.ModName, dep, removal));
                }
            }
        }

        // Print summary table
        Console.WriteLine("┌────────────────────────────────────────────────────┬──────────┬──────────┐");
        Console.WriteLine("│ Mod Name                                           │  Status  │ C# Deps  │");
        Console.WriteLine("├────────────────────────────────────────────────────┼──────────┼──────────┤");

        int totalConflicts = 0, totalCautions = 0, safeCount = 0, codeOnlyCount = 0;

        foreach (var result in results)
        {
            var displayName = result.Name.Length > 50 ? result.Name[..47] + "..." : result.Name;
            var depCount = result.Dependencies.Count;
            
            string status;
            ConsoleColor color;

            if (result.IsCodeOnly && depCount == 0)
            {
                status = "CODE";
                color = ConsoleColor.DarkGray;
                codeOnlyCount++;
            }
            else if (result.IsCodeOnly && depCount > 0)
            {
                status = "C#";
                color = ConsoleColor.Cyan;
                codeOnlyCount++;
            }
            else if (result.Conflicts > 0)
            {
                status = $"⚠ {result.Conflicts}";
                color = ConsoleColor.Red;
                totalConflicts += result.Conflicts;
            }
            else if (result.Cautions > 0)
            {
                status = $"⚡ {result.Cautions}";
                color = ConsoleColor.Yellow;
                totalCautions += result.Cautions;
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

        // Summary
        Console.WriteLine($"Analyzed {results.Count} mods in {sw.ElapsedMilliseconds}ms:");
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

        // Cross-mod conflicts
        if (crossModConflicts.Any())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  ⚠ CROSS-MOD CONFLICTS DETECTED: {crossModConflicts.Count}");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

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
                
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     C# source: {conflict.Dep.SourceFile}:{conflict.Dep.Line}");
                Console.WriteLine($"     Pattern: {conflict.Dep.Pattern}");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        // Show C# mod dependencies with meaningful grouping
        var modsWithDeps = results.Where(r => r.Dependencies.Count > 0).ToList();
        if (modsWithDeps.Any())
        {
            Console.WriteLine("\n─── C# Mod Analysis ───\n");
            foreach (var mod in modsWithDeps)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"◆ {mod.Name}");
                Console.ResetColor();

                // Group into meaningful categories
                var harmonyPatches = mod.Dependencies.Where(d => d.Type.StartsWith("harmony_")).ToList();
                var entityLookups = mod.Dependencies.Where(d => new[] {"item", "block", "buff", "entity_class", "recipe", "sound", "quest", "lootcontainer", "progression", "trader_info", "workstation"}.Contains(d.Type)).ToList();
                var classExtensions = mod.Dependencies.Where(d => d.Type.StartsWith("extends_") || d.Type.StartsWith("implements_")).ToList();
                var localization = mod.Dependencies.Where(d => d.Type == "localization").ToList();

                if (harmonyPatches.Any())
                {
                    var prefixCount = harmonyPatches.Count(h => h.Type == "harmony_prefix");
                    var postfixCount = harmonyPatches.Count(h => h.Type == "harmony_postfix");
                    var transpilerCount = harmonyPatches.Count(h => h.Type == "harmony_transpiler");
                    var targets = harmonyPatches.Select(h => h.Name).Distinct().Take(5);

                    Console.Write("    ");
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("Harmony Patches: ");
                    Console.ResetColor();
                    Console.Write($"{harmonyPatches.Count} (");
                    if (prefixCount > 0) Console.Write($"{prefixCount} prefix");
                    if (postfixCount > 0) Console.Write($"{(prefixCount > 0 ? ", " : "")}{postfixCount} postfix");
                    if (transpilerCount > 0) Console.Write($"{((prefixCount + postfixCount) > 0 ? ", " : "")}{transpilerCount} transpiler");
                    Console.WriteLine(")");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"      Targets: {string.Join(", ", targets)}{(harmonyPatches.Select(h => h.Name).Distinct().Count() > 5 ? $" +{harmonyPatches.Select(h => h.Name).Distinct().Count() - 5} more" : "")}");
                    Console.ResetColor();
                }

                if (entityLookups.Any())
                {
                    var grouped = entityLookups.GroupBy(d => d.Type).OrderByDescending(g => g.Count());
                    Console.Write("    ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("XML Entity Lookups: ");
                    Console.ResetColor();
                    Console.WriteLine($"{entityLookups.Count} references");
                    foreach (var group in grouped.Take(3))
                    {
                        var typeLabel = group.Key switch
                        {
                            "item" => "Items",
                            "block" => "Blocks",
                            "buff" => "Buffs",
                            "entity_class" => "Entities",
                            "recipe" => "Recipes",
                            "sound" => "Sounds",
                            _ => group.Key
                        };
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"      {typeLabel}: {string.Join(", ", group.Select(d => d.Name).Distinct().Take(5))}{(group.Select(d => d.Name).Distinct().Count() > 5 ? " ..." : "")}");
                        Console.ResetColor();
                    }
                }

                if (classExtensions.Any())
                {
                    Console.Write("    ");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("Class Extensions: ");
                    Console.ResetColor();
                    Console.WriteLine(string.Join(", ", classExtensions.Select(d => d.Name).Distinct().Take(5)));
                }

                Console.WriteLine();
            }
        }

        // Show details for problematic XML mods
        var problemMods = results.Where(r => r.Conflicts > 0 || r.Cautions > 0).ToList();
        if (problemMods.Any())
        {
            Console.WriteLine("─── XML Mod Details ───\n");
            foreach (var mod in problemMods)
            {
                var modDir = Path.Combine(modsFolder, mod.Name);
                AnalyzeMod(modDir);
                Console.WriteLine();
            }
        }

        return (totalConflicts > 0 || crossModConflicts.Any()) ? 1 : 0;
    }

    private static void AnalyzeModXmlSilent(string filePath, SqliteConnection db, string modName, List<XmlRemoval> removals)
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

                    // Track removals for cross-mod analysis
                    if (op == "remove")
                    {
                        var extracted = ExtractTargetFromXPath(xpath);
                        if (extracted.HasValue)
                        {
                            removals.Add(new XmlRemoval(modName, extracted.Value.Type, extracted.Value.Name, xpath));
                        }
                    }
                }
            }
        }
        catch { /* Skip unparseable files */ }
    }

    private static (string Type, string Name)? ExtractTargetFromXPath(string xpath)
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
            {
                return (pattern.Value, match.Groups[1].Value);
            }
        }

        return null;
    }

    // Extract property name from xpath (e.g., "CarryCapacity" from ...passive_effect[@name='CarryCapacity']/@value)
    private static string? ExtractPropertyFromXPath(string xpath)
    {
        // Look for property/passive_effect/triggered_effect names
        var patterns = new[]
        {
            @"passive_effect\[@name='([^']+)'\]",
            @"triggered_effect\[@name='([^']+)'\]",
            @"property\[@name='([^']+)'\]",
            @"effect_group\[@name='([^']+)'\]",
            @"@(\w+)$" // Final attribute like /@value, /@count
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(xpath, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        
        return null;
    }

    // Extract value from xpath (for cases where the value is inline)
    private static string? ExtractValueFromXPath(string xpath)
    {
        // Look for @value='...' or @count='...' patterns
        var match = Regex.Match(xpath, @"@\w+='([^']*)'");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return null;
    }

    private static int AnalyzeMod(string modPath)
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
            AnalyzeModXml(xmlFile, db);
        }

        // Print summary
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

        return _conflictCount > 0 ? 1 : 0;
    }

    private static void AnalyzeModXml(string filePath, SqliteConnection db)
    {
        var fileName = Path.GetFileName(filePath);
        Console.WriteLine($"--- {fileName} ---");

        try
        {
            var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);

            // Find all XPath operations
            var operations = new[] { "set", "append", "remove", "insertAfter", "insertBefore", "setattribute", "removeattribute", "csv" };

            foreach (var op in operations)
            {
                foreach (var element in doc.Descendants(op))
                {
                    var xpath = element.Attribute("xpath")?.Value;
                    if (string.IsNullOrEmpty(xpath)) continue;

                    var lineInfo = (IXmlLineInfo)element;
                    var isDestructive = op is "remove" or "removeattribute";
                    
                    // Analyze impact and get conflict status
                    var (status, details) = AnalyzeXPathImpact(op, xpath, db);

                    // Determine display
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

    private enum ImpactStatus { Safe, Caution, Conflict }

    private static (ImpactStatus status, string details) AnalyzeXPathImpact(string operation, string xpath, SqliteConnection db)
    {
        var isDestructive = operation is "remove" or "removeattribute";
        
        // Extract entity type and name from xpath
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
            var match = System.Text.RegularExpressions.Regex.Match(xpath, pattern.Key);
            if (match.Success)
            {
                var targetName = match.Groups[1].Value;
                var targetType = pattern.Value;

                // Check if definition exists
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM xml_definitions WHERE definition_type = $type AND name = $name";
                cmd.Parameters.AddWithValue("$type", targetType);
                cmd.Parameters.AddWithValue("$name", targetName);
                var exists = (long)cmd.ExecuteScalar()! > 0;

                if (!exists)
                {
                    return (ImpactStatus.Safe, $"Target: {targetType} '{targetName}' (not in base game)");
                }

                if (isDestructive && operation == "remove")
                {
                    // Check for references - this is what determines CONFLICT vs CAUTION
                    cmd.CommandText = "SELECT COUNT(*) FROM xml_references WHERE target_type = $type AND target_name = $name";
                    var refCount = (long)cmd.ExecuteScalar()!;
                    
                    if (refCount > 0)
                    {
                        return (ImpactStatus.Conflict, $"BREAKS {refCount} references to {targetType} '{targetName}'!");
                    }
                    else
                    {
                        return (ImpactStatus.Caution, $"Removes {targetType} '{targetName}' (no base game refs, but may affect other mods)");
                    }
                }

                return (ImpactStatus.Safe, $"Target: {targetType} '{targetName}' (exists)");
            }
        }

        // Check for property-level modifications
        var propMatch = System.Text.RegularExpressions.Regex.Match(xpath, @"property\[@name='([^']+)'\]");
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
                {
                    // Property removal - check if any definition REQUIRES this property
                    // For now, mark as caution since we don't track property dependencies
                    return (ImpactStatus.Caution, $"Removes property '{propName}' from {count} definitions");
                }
                return (ImpactStatus.Safe, $"Property '{propName}' not found in base game");
            }

            return (ImpactStatus.Safe, $"Modifies property '{propName}' on {count} definitions");
        }

        // Generic xpath - can't determine specific impact
        if (isDestructive)
        {
            return (ImpactStatus.Caution, "Destructive operation - impact unknown");
        }

        return (ImpactStatus.Safe, "");
    }

    private static int FindReferences(string type, string name)
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        Console.WriteLine($"=== References to {type} '{name}' ===\n");

        // Find the definition
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT id, file_path, line_number, extends FROM xml_definitions 
                           WHERE definition_type = $type AND name = $name";
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$name", name);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            Console.WriteLine($"Definition: {reader.GetString(1)}:{reader.GetInt32(2)}");
            if (!reader.IsDBNull(3))
                Console.WriteLine($"Extends: {reader.GetString(3)}");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("Definition not found in base game XML.");
            return 1;
        }
        reader.Close();

        // Find references
        cmd.CommandText = @"SELECT source_type, source_file, source_line, reference_context 
                           FROM xml_references 
                           WHERE target_type = $type AND target_name = $name
                           ORDER BY source_file, source_line";

        using var refReader = cmd.ExecuteReader();
        int count = 0;
        while (refReader.Read())
        {
            Console.WriteLine($"  [{refReader.GetString(0)}] {refReader.GetString(1)}:{refReader.GetInt32(2)} ({refReader.GetString(3)})");
            count++;
        }

        if (count == 0)
            Console.WriteLine("  No references found.");
        else
            Console.WriteLine($"\n  Total: {count} references");

        return 0;
    }

    private static int ListDefinitions(string type)
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        Console.WriteLine($"=== All {type} definitions ===\n");

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT name, file_path, line_number FROM xml_definitions 
                           WHERE definition_type = $type ORDER BY name";
        cmd.Parameters.AddWithValue("$type", type);

        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read())
        {
            Console.WriteLine($"  {reader.GetString(0),-40} ({reader.GetString(1)}:{reader.GetInt32(2)})");
            count++;
        }

        Console.WriteLine($"\nTotal: {count}");

        return 0;
    }

    private static int SearchDefinitions(string pattern)
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        Console.WriteLine($"=== Search: '{pattern}' ===\n");

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT definition_type, name, file_path, line_number FROM xml_definitions 
                           WHERE name LIKE '%' || $pattern || '%' 
                           ORDER BY definition_type, name
                           LIMIT 100";
        cmd.Parameters.AddWithValue("$pattern", pattern);

        using var reader = cmd.ExecuteReader();
        int count = 0;
        string? lastType = null;
        while (reader.Read())
        {
            var defType = reader.GetString(0);
            if (defType != lastType)
            {
                if (lastType != null) Console.WriteLine();
                Console.WriteLine($"[{defType}]");
                lastType = defType;
            }
            Console.WriteLine($"  {reader.GetString(1),-40} ({reader.GetString(2)}:{reader.GetInt32(3)})");
            count++;
        }

        Console.WriteLine($"\nTotal: {count} matches");

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FULL ANALYSIS - Complete workflow with performance profiling
    // ═══════════════════════════════════════════════════════════════════════════

    private static int FullAnalyze(string modsFolder)
    {
        var totalSw = Stopwatch.StartNew();
        var timings = new Dictionary<string, long>();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  7D2D MOD ECOSYSTEM ANALYZER - Full Analysis                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        Console.WriteLine($"Game Path:  {_gamePath}");
        Console.WriteLine($"Mods Path:  {modsFolder}");
        Console.WriteLine($"Database:   {_dbPath}\n");

        // STEP 1: Build XML database
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("STEP 1: Indexing base game XML data");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        var step1Sw = Stopwatch.StartNew();
        var buildResult = BuildDatabase();
        if (buildResult != 0) return buildResult;
        timings["1. XML Indexing"] = step1Sw.ElapsedMilliseconds;

        // STEP 2: Discover and catalog mods
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("STEP 2: Discovering installed mods");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        var step2Sw = Stopwatch.StartNew();
        var modDirs = Directory.GetDirectories(modsFolder)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .OrderBy(d => Path.GetFileName(d))
            .ToList();

        int xmlMods = 0, dllMods = 0, hybridMods = 0;
        foreach (var modDir in modDirs)
        {
            var hasXml = Directory.Exists(Path.Combine(modDir, "Config")) && 
                         Directory.GetFiles(Path.Combine(modDir, "Config"), "*.xml").Length > 0;
            var hasDll = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
                         .Any(d => !Path.GetFileName(d).StartsWith("0Harmony") && 
                                   !Path.GetFileName(d).Contains("Mono.Cecil"));

            if (hasXml && hasDll) hybridMods++;
            else if (hasXml) xmlMods++;
            else if (hasDll) dllMods++;
        }

        Console.WriteLine($"  Found {modDirs.Count} mods:");
        Console.WriteLine($"    • {xmlMods} XML-only mods");
        Console.WriteLine($"    • {dllMods} C# code-only mods");
        Console.WriteLine($"    • {hybridMods} hybrid (XML + C#) mods");
        timings["2. Mod Discovery"] = step2Sw.ElapsedMilliseconds;

        // STEP 3: Decompile mod DLLs
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("STEP 3: Decompiling mod DLLs (using ilspycmd)");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        var step3Sw = Stopwatch.StartNew();
        int dllsDecompiled = 0;
        foreach (var modDir in modDirs)
        {
            var modName = Path.GetFileName(modDir);
            var dlls = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
                .Where(d => !Path.GetFileName(d).StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase))
                .Where(d => !Path.GetFileName(d).Contains("Mono.Cecil"))
                .Where(d => !Path.GetFileName(d).Contains("System."))
                .Where(d => !Path.GetFileName(d).Contains("Microsoft."))
                .ToList();

            foreach (var dll in dlls)
            {
                var files = CSharpAnalyzer.DecompileModDll(dll, modName);
                if (files.Count > 0)
                {
                    dllsDecompiled++;
                    Console.WriteLine($"  ✓ {modName}: {Path.GetFileName(dll)} ({files.Count} files)");
                }
            }
        }
        Console.WriteLine($"\n  Decompiled {dllsDecompiled} mod DLLs");
        timings["3. DLL Decompilation"] = step3Sw.ElapsedMilliseconds;

        // STEP 4: Analyze mod conflicts
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("STEP 4: Analyzing mod ecosystem");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        var step4Sw = Stopwatch.StartNew();
        // SINGLE CODE PATH: Delegate to ModAnalyzer for all mod analysis
        using (var db = new SqliteConnection($"Data Source={_dbPath}"))
        {
            db.Open();
            var analyzer = new ModAnalyzer(_dbPath!);
            analyzer.PersistModAnalysis(modsFolder, db);
        }
        timings["4. Mod Analysis"] = step4Sw.ElapsedMilliseconds;

        // STEP 5: Build ecosystem view
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("STEP 5: Building ecosystem view");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        var step5Sw = Stopwatch.StartNew();
        BuildEcosystemView();
        timings["5. Ecosystem Build"] = step5Sw.ElapsedMilliseconds;

        // PERFORMANCE SUMMARY
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PERFORMANCE PROFILE                                             ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");

        foreach (var timing in timings)
        {
            var bar = new string('█', (int)(timing.Value / 100.0));
            var pct = (timing.Value * 100.0 / totalSw.ElapsedMilliseconds);
            Console.WriteLine($"║  {timing.Key,-22} {timing.Value,6}ms  {pct,5:F1}% {bar,-20}║");
        }

        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  TOTAL                        {totalSw.ElapsedMilliseconds,6}ms  100.0%                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        // Show quick stats
        Console.WriteLine("\nRun 'XmlIndexer stats <db>' for detailed statistics");
        Console.WriteLine("Run 'XmlIndexer ecosystem <db>' for ecosystem health view\n");

        return 0;
    }

    // NOTE: AnalyzeAllModsToDatabase, ScanAndStoreHarmonyPatches, PersistHarmonyPatch, and AnalyzeModXmlToDatabase
    // have been removed - mod analysis now uses SINGLE CODE PATH via ModAnalyzer.PersistModAnalysis()

    private static void BuildEcosystemView()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Build ecosystem from base game + mod operations
        using var transaction = db.BeginTransaction();

        // Add all base game entities
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO ecosystem_entities (entity_type, entity_name, source, status)
                SELECT definition_type, name, 'base_game', 'active' FROM xml_definitions";
            var inserted = cmd.ExecuteNonQuery();
            Console.WriteLine($"  Added {inserted} base game entities");
        }

        // Track modifications from mods
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE ecosystem_entities SET 
                    modified_by = (
                        SELECT GROUP_CONCAT(m.name, ', ')
                        FROM mod_xml_operations mxo
                        JOIN mods m ON mxo.mod_id = m.id
                        WHERE mxo.target_type = ecosystem_entities.entity_type
                          AND mxo.target_name = ecosystem_entities.entity_name
                          AND mxo.operation IN ('set', 'setattribute', 'append', 'insertAfter', 'insertBefore')
                    )
                WHERE EXISTS (
                    SELECT 1 FROM mod_xml_operations mxo
                    WHERE mxo.target_type = ecosystem_entities.entity_type
                      AND mxo.target_name = ecosystem_entities.entity_name
                      AND mxo.operation IN ('set', 'setattribute', 'append', 'insertAfter', 'insertBefore')
                )";
            var modified = cmd.ExecuteNonQuery();
            Console.WriteLine($"  Marked {modified} entities as modified by mods");
        }

        // Track removals
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE ecosystem_entities SET 
                    status = 'removed',
                    removed_by = (
                        SELECT GROUP_CONCAT(m.name, ', ')
                        FROM mod_xml_operations mxo
                        JOIN mods m ON mxo.mod_id = m.id
                        WHERE mxo.target_type = ecosystem_entities.entity_type
                          AND mxo.target_name = ecosystem_entities.entity_name
                          AND mxo.operation IN ('remove', 'removeattribute')
                    )
                WHERE EXISTS (
                    SELECT 1 FROM mod_xml_operations mxo
                    WHERE mxo.target_type = ecosystem_entities.entity_type
                      AND mxo.target_name = ecosystem_entities.entity_name
                      AND mxo.operation IN ('remove', 'removeattribute')
                )";
            var removed = cmd.ExecuteNonQuery();
            Console.WriteLine($"  Marked {removed} entities as removed by mods");
        }

        // Track C# dependencies
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE ecosystem_entities SET 
                    depended_on_by = (
                        SELECT GROUP_CONCAT(m.name, ', ')
                        FROM mod_csharp_deps mcd
                        JOIN mods m ON mcd.mod_id = m.id
                        WHERE mcd.dependency_type = ecosystem_entities.entity_type
                          AND mcd.dependency_name = ecosystem_entities.entity_name
                    )
                WHERE EXISTS (
                    SELECT 1 FROM mod_csharp_deps mcd
                    WHERE mcd.dependency_type = ecosystem_entities.entity_type
                      AND mcd.dependency_name = ecosystem_entities.entity_name
                )";
            var depended = cmd.ExecuteNonQuery();
            Console.WriteLine($"  Marked {depended} entities as depended upon by C# mods");
        }

        transaction.Commit();
    }

    // NOTE: RunQuery has been moved to Commands/QueryCommand.cs

    private static int TestXPathParsing(string xpath)
    {
        Console.WriteLine($"Testing XPath: {xpath}\n");
        
        var target = ModAnalyzer.ExtractTargetFromXPath(xpath);
        var property = ModAnalyzer.ExtractPropertyFromXPath(xpath);
        
        if (target != null)
        {
            Console.WriteLine($"✓ Target extracted:");
            Console.WriteLine($"  Type:      {target.Type}");
            Console.WriteLine($"  Name:      {target.Name}");
            Console.WriteLine($"  Selector:  [{target.SelectorAttribute}='{target.SelectorValue}']");
            Console.WriteLine($"  Fragile:   {target.IsFragile}");
        }
        else
        {
            Console.WriteLine("✗ No target extracted");
        }
        
        if (property != null)
        {
            Console.WriteLine($"\n✓ Property: {property}");
        }
        else
        {
            Console.WriteLine("\n✗ No property extracted");
        }
        
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STATISTICS - Fun and useful insights
    // NOTE: ShowStats has been moved to Commands/StatsCommand.cs

    // NOTE: ShowEcosystem has been moved to Commands/EcosystemCommand.cs

    // ═══════════════════════════════════════════════════════════════════════════
    // REPORT GENERATION - Export to HTML, Markdown, JSON
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate multi-page HTML report site.
    /// Creates a timestamped folder with index.html, entities.html, mods.html, etc.
    /// </summary>
    /// <summary>
    /// Complete single-command operation: rebuild database + generate report with profiling.
    /// </summary>
    private static int GenerateFullReport(string modsFolder, string outputDir, bool openAfter, string? explicitCodebasePath = null)
    {
        var totalSw = Stopwatch.StartNew();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  7D2D MOD ECOSYSTEM REPORT - Full Build                          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        Console.WriteLine($"Game Path:  {_gamePath}");
        Console.WriteLine($"Mods Path:  {modsFolder}");
        Console.WriteLine($"Output:     {outputDir}\n");

        // STEP 1: Build/rebuild the database
        var analyzeResult = FullAnalyze(modsFolder);
        if (analyzeResult != 0)
        {
            Console.WriteLine("\n✗ Database build failed");
            return analyzeResult;
        }

        // STEP 2: Build dependency graph
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("BUILDING DEPENDENCY GRAPH");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        BuildDependencyGraph();  // Return code indicates conflict severity, not failure

        // STEP 3: Generate report
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("GENERATING REPORT");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        Directory.CreateDirectory(outputDir);

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Try to find decompiled game code for analysis
        // Use explicit path if provided, otherwise try common locations
        string? gameCodebasePath = explicitCodebasePath;
        
        if (string.IsNullOrEmpty(gameCodebasePath))
        {
            var possibleCodebasePaths = new[]
            {
                Path.Combine(Path.GetDirectoryName(outputDir) ?? "", "7D2DCodebase", "Assembly-CSharp"),
                Path.Combine(outputDir, "..", "7D2DCodebase", "Assembly-CSharp"),
                Path.Combine(_gamePath ?? "", "..", "7D2DCodebase", "Assembly-CSharp"),
            };

            foreach (var path in possibleCodebasePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    gameCodebasePath = fullPath;
                    break;
                }
            }
        }
        
        if (!string.IsNullOrEmpty(gameCodebasePath))
        {
            Console.WriteLine($"  Game codebase: {gameCodebasePath}");
        }

        // Generate the multi-page site (time will be injected after)
        var siteFolder = ReportSiteGenerator.Generate(db, outputDir, 0, gameCodebasePath);
        var indexPath = Path.Combine(siteFolder, "index.html");

        totalSw.Stop();
        var totalTimeMs = totalSw.ElapsedMilliseconds;

        // Inject final build time into index.html
        var indexContent = File.ReadAllText(indexPath);
        var timeStr = $"{totalTimeMs / 1000.0:F1}s";
        indexContent = indexContent.Replace("<!--BUILD_TIME_PLACEHOLDER-->", $" • Built in {timeStr}");
        indexContent = indexContent.Replace("<!--BUILD_TIME_STAT_PLACEHOLDER-->",
            $"<div class=\"stat\"><span class=\"stat-value\">{timeStr}</span><span class=\"stat-label\">Total Time</span></div>");
        File.WriteAllText(indexPath, indexContent);

        Console.WriteLine($"\n✓ Report generated: {siteFolder}");
        Console.WriteLine($"  Total time: {timeStr}");

        // Open in browser if requested
        if (openAfter)
        {
            try
            {
                Process.Start(new ProcessStartInfo(indexPath) { UseShellExecute = true });
                Console.WriteLine("  ✓ Opened in browser");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ℹ️  Could not auto-open report: {ex.Message}");
            }
        }

        return 0;
    }

    private static int GenerateMultiPageReport(string outputDir, bool openAfter)
    {
        Directory.CreateDirectory(outputDir);

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  GENERATING MULTI-PAGE REPORT                                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Generate the multi-page site
        var siteFolder = ReportSiteGenerator.Generate(db, outputDir);
        var indexPath = Path.Combine(siteFolder, "index.html");

        Console.WriteLine($"\n✓ Multi-page report generated: {siteFolder}");

        // Open in browser if requested
        if (openAfter)
        {
            try
            {
                Process.Start(new ProcessStartInfo(indexPath) { UseShellExecute = true });
                Console.WriteLine("  ✓ Opened in browser");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ℹ️  Could not auto-open report: {ex.Message}");
            }
        }

        return 0;
    }

    // Legacy single-file report generation (kept for backwards compatibility)
    private static int GenerateReports(string outputDir, HashSet<string> formats)
    {
        Directory.CreateDirectory(outputDir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  GENERATING REPORTS                                              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Gather all data using ReportDataCollector module
        var reportData = ReportDataCollector.GatherReportData(db);

        if (formats.Contains("html"))
        {
            var htmlPath = Path.Combine(outputDir, $"ecosystem_report_{timestamp}.html");
            HtmlReportGenerator.Generate(htmlPath, reportData);
            Console.WriteLine($"  ✓ HTML Report: {htmlPath}");

            // Auto-open in browser
            try 
            {
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ℹ️  Could not auto-open report: {ex.Message}");
            }
        }

        if (formats.Contains("md"))
        {
            var mdPath = Path.Combine(outputDir, $"ecosystem_report_{timestamp}.md");
            Reports.MarkdownJsonExporter.GenerateMarkdownReport(mdPath, reportData);
            Console.WriteLine($"  ✓ Markdown Report: {mdPath}");
        }

        if (formats.Contains("json"))
        {
            var jsonPath = Path.Combine(outputDir, $"ecosystem_data_{timestamp}.json");
            Reports.MarkdownJsonExporter.GenerateJsonExport(jsonPath, reportData);
            Console.WriteLine($"  ✓ JSON Export: {jsonPath}");
        }

        Console.WriteLine("\n✓ Reports generated successfully!");
        return 0;
    }

    // Report generators moved to Reports/MarkdownJsonExporter.cs
    // EscapeJson kept for legacy usage

    private static string EscapeJson(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    // Semantic functions moved to Semantic/SemanticService.cs

    // NOTE: ShowModDetails has been moved to Commands/ModDetailsCommand.cs

    private static int DetectConflicts(string? callgraphDbPath)
    {
        if (!File.Exists(_dbPath))
        {
            Console.Error.WriteLine($"Error: Database not found: {_dbPath}");
            return 1;
        }

        try
        {
            var detector = new ConflictDetector(_dbPath!, callgraphDbPath);
            var report = detector.DetectAllConflicts();
            detector.OutputJson(report);
            return report.Summary.High > 0 ? 2 : (report.Summary.Medium > 0 ? 1 : 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error detecting conflicts: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Builds the transitive reference graph and detects indirect conflicts.
    /// This enables impact analysis ("what depends on X?") and finds mod conflicts
    /// that span multiple entities through inheritance/buff chains.
    /// </summary>
    private static int BuildDependencyGraph()
    {
        if (!File.Exists(_dbPath))
        {
            Console.Error.WriteLine($"Error: Database not found: {_dbPath}");
            return 1;
        }

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  BUILDING ENTITY DEPENDENCY GRAPH                                ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        var totalSw = Stopwatch.StartNew();

        try
        {
            // Step 1: Build transitive references
            Console.WriteLine("[Step 1] Computing transitive references...");
            Console.WriteLine("         (following extends, buffs, loot entries, recipes, etc.)");
            var transBuilder = new TransitiveReferenceBuilder(_dbPath!);
            var transResult = transBuilder.BuildTransitiveReferences();
            Console.WriteLine($"         ✓ {transResult}");
            Console.WriteLine();

            // Step 2: Detect indirect conflicts
            Console.WriteLine("[Step 2] Detecting indirect mod conflicts...");
            Console.WriteLine("         (finding mod interactions through shared dependencies)");
            var conflictDetector = new IndirectConflictDetector(_dbPath!);
            var conflictResult = conflictDetector.DetectIndirectConflicts();
            Console.WriteLine($"         ✓ {conflictResult}");
            Console.WriteLine();

            // Summary
            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"  Total time: {totalSw.ElapsedMilliseconds}ms");
            Console.WriteLine();
            Console.WriteLine("  Now you can query:");
            Console.WriteLine($"    XmlIndexer impact-analysis {Path.GetFileName(_dbPath)} buff buffCoffeeBuzz");
            Console.WriteLine($"    sqlite3 {Path.GetFileName(_dbPath)} \"SELECT * FROM v_inheritance_hotspots LIMIT 10\"");
            Console.WriteLine($"    sqlite3 {Path.GetFileName(_dbPath)} \"SELECT * FROM v_conflict_summary\"");
            Console.WriteLine();

            return conflictResult.HighCount > 0 ? 2 : (conflictResult.MediumCount > 0 ? 1 : 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error building dependency graph: {ex.Message}");
            if (_verbose)
                Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // NOTE: ImpactAnalysis has been moved to Commands/ImpactCommand.cs

    // =========================================================================
    // Game Code Analysis Commands
    // =========================================================================

    private static int AnalyzeGameCode(string codebasePath, bool force)
    {
        Console.WriteLine("=== Game Code Analysis ===");
        Console.WriteLine($"Codebase: {codebasePath}");
        Console.WriteLine($"Database: {_dbPath}");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Ensure schema exists
        Database.DatabaseBuilder.CreateSchema(db);

        var analyzer = new Analysis.GameCodeAnalyzer(db);
        analyzer.AnalyzeGameCode(codebasePath, force);

        Console.WriteLine();
        Console.WriteLine($"Completed in {sw.Elapsed.TotalSeconds:F2}s");

        return 0;
    }

    private static int IndexGameCode(string codebasePath, bool force)
    {
        Console.WriteLine("=== Game Code Indexing ===");
        Console.WriteLine($"Codebase: {codebasePath}");
        Console.WriteLine($"Database: {_dbPath}");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Ensure schema exists
        Database.DatabaseBuilder.CreateSchema(db);

        var indexer = new Utils.GameCodeIndexer(db);
        indexer.IndexGameCode(codebasePath, force);

        Console.WriteLine();
        Console.WriteLine($"Completed in {sw.Elapsed.TotalSeconds:F2}s");

        return 0;
    }

    private static int DetectHarmonyConflicts()
    {
        Console.WriteLine("=== Harmony Conflict Detection ===");
        Console.WriteLine($"Database: {_dbPath}");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Check if harmony_patches table has data
        using var checkCmd = db.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM harmony_patches";
        long patchCount;
        try
        {
            patchCount = (long)checkCmd.ExecuteScalar()!;
        }
        catch
        {
            Console.WriteLine("  Error: No Harmony patch data found. Run analyze-mods first.");
            return 1;
        }

        Console.WriteLine($"  Found {patchCount} Harmony patches to analyze");

        var report = Analysis.HarmonyConflictDetector.DetectAllConflicts(db);

        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"  Collisions:           {report.Collisions.Count}");
        Console.WriteLine($"  Transpiler Conflicts: {report.TranspilerConflicts.Count}");
        Console.WriteLine($"  Skip Conflicts:       {report.SkipConflicts.Count}");
        Console.WriteLine($"  Inheritance Overlaps: {report.InheritanceOverlaps.Count}");
        Console.WriteLine($"  Order Conflicts:      {report.OrderConflicts.Count}");
        Console.WriteLine();

        if (report.CriticalCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  CRITICAL: {report.CriticalCount} conflicts require immediate attention!");
            Console.ResetColor();
        }

        if (report.HighCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  HIGH: {report.HighCount} conflicts likely to cause issues");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine($"Completed in {sw.Elapsed.TotalSeconds:F2}s");

        return 0;
    }
}
