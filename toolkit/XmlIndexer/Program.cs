using System.Xml;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;
using XmlIndexer.Reports;

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
                return ShowStats();

            case "ecosystem":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer ecosystem <db_path>");
                    return 1;
                }
                _dbPath = args[1];
                return ShowEcosystem();

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
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer report <db_path> <output_dir> [--html] [--md] [--json]");
                    return 1;
                }
                _dbPath = args[1];
                var outputDir = args[2];
                var formats = new HashSet<string>();
                if (args.Contains("--html")) formats.Add("html");
                if (args.Contains("--md")) formats.Add("md");
                if (args.Contains("--json")) formats.Add("json");
                if (formats.Count == 0) formats.Add("html"); // Default to HTML
                return GenerateReports(outputDir, formats);

            case "export-semantic-traces":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer export-semantic-traces <db_path> <output.jsonl>");
                    return 1;
                }
                _dbPath = args[1];
                return ExportSemanticTraces(args[2]);

            case "import-semantic-mappings":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer import-semantic-mappings <db_path> <mappings.jsonl>");
                    return 1;
                }
                _dbPath = args[1];
                return ImportSemanticMappings(args[2]);

            case "semantic-status":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XmlIndexer semantic-status <db_path>");
                    return 1;
                }
                _dbPath = args[1];
                return ShowSemanticStatus();

            case "mod-details":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: XmlIndexer mod-details <db_path> <mod_name_pattern>");
                    return 1;
                }
                _dbPath = args[1];
                return ShowModDetails(args[2]);

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
        Console.WriteLine("  ecosystem <db_path>                Combined codebase+mods ecosystem view");
        Console.WriteLine("  refs <db_path> <type> <name>       Find all references to an entity");
        Console.WriteLine("  list <db_path> <type>              List all definitions of a type");
        Console.WriteLine("  search <db_path> <pattern>         Search definitions by name");
        Console.WriteLine();
        Console.WriteLine("REPORTS:");
        Console.WriteLine("  report <db_path> <output_dir>      Generate HTML/MD/JSON reports");
        Console.WriteLine("    --html                           Generate HTML report (default)");
        Console.WriteLine("    --md                             Generate Markdown report");
        Console.WriteLine("    --json                           Generate JSON data export");
        Console.WriteLine();
        Console.WriteLine("SEMANTIC ANALYSIS (LLM-powered descriptions):");
        Console.WriteLine("  export-semantic-traces <db> <out>  Export traces for LLM analysis");
        Console.WriteLine("  import-semantic-mappings <db> <in> Import LLM-generated descriptions");
        Console.WriteLine("  semantic-status <db>               Show semantic mapping coverage");
        Console.WriteLine();
        Console.WriteLine("TYPES: item, block, entity_class, buff, recipe, sound, vehicle, quest");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  XmlIndexer full-analyze \"C:\\Steam\\...\\7 Days To Die\" \"...\\Mods\" eco.db");
        Console.WriteLine("  XmlIndexer report eco.db ./reports --html --md");
        Console.WriteLine("  XmlIndexer refs eco.db item itemRepairKit");
        Console.WriteLine("  XmlIndexer export-semantic-traces eco.db traces.jsonl");
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
        
        if (xmlFiles.TryGetValue("items.xml", out var itemsDoc))
            ParseItems(itemsDoc);
        if (xmlFiles.TryGetValue("blocks.xml", out var blocksDoc))
            ParseBlocks(blocksDoc);
        if (xmlFiles.TryGetValue("entityclasses.xml", out var entityDoc))
            ParseEntityClasses(entityDoc);
        if (xmlFiles.TryGetValue("entitygroups.xml", out var groupsDoc))
            ParseEntityGroups(groupsDoc);
        if (xmlFiles.TryGetValue("buffs.xml", out var buffsDoc))
            ParseBuffs(buffsDoc);
        if (xmlFiles.TryGetValue("recipes.xml", out var recipesDoc))
            ParseRecipes(recipesDoc);
        if (xmlFiles.TryGetValue("sounds.xml", out var soundsDoc))
            ParseSounds(soundsDoc);
        if (xmlFiles.TryGetValue("vehicles.xml", out var vehiclesDoc))
            ParseVehicles(vehiclesDoc);
        if (xmlFiles.TryGetValue("quests.xml", out var questsDoc))
            ParseQuests(questsDoc);
        if (xmlFiles.TryGetValue("gameevents.xml", out var eventsDoc))
            ParseGameEvents(eventsDoc);
        if (xmlFiles.TryGetValue("progression.xml", out var progDoc))
            ParseProgression(progDoc);
        if (xmlFiles.TryGetValue("loot.xml", out var lootDoc))
            ParseLoot(lootDoc);

        // Build extends cross-references in memory
        foreach (var def in _definitions.Where(d => !string.IsNullOrEmpty(d.Extends)))
        {
            _references.Add(new XmlReference("xml", def.Id, def.File, def.Line, def.Type, def.Extends!, "extends"));
        }

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

        // Create schema
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE xml_definitions (
                    id INTEGER PRIMARY KEY,
                    definition_type TEXT NOT NULL,
                    name TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    line_number INTEGER,
                    extends TEXT
                );
                CREATE INDEX idx_def_type_name ON xml_definitions(definition_type, name);
                CREATE INDEX idx_def_name ON xml_definitions(name);

                CREATE TABLE xml_properties (
                    id INTEGER PRIMARY KEY,
                    definition_id INTEGER,
                    property_name TEXT NOT NULL,
                    property_value TEXT,
                    property_class TEXT,
                    line_number INTEGER
                );
                CREATE INDEX idx_prop_def ON xml_properties(definition_id);
                CREATE INDEX idx_prop_name ON xml_properties(property_name);

                CREATE TABLE xml_references (
                    id INTEGER PRIMARY KEY,
                    source_type TEXT NOT NULL,
                    source_def_id INTEGER,
                    source_file TEXT NOT NULL,
                    source_line INTEGER,
                    target_type TEXT NOT NULL,
                    target_name TEXT NOT NULL,
                    reference_context TEXT
                );
                CREATE INDEX idx_ref_target ON xml_references(target_type, target_name);

                CREATE TABLE xml_stats (
                    definition_type TEXT PRIMARY KEY,
                    count INTEGER
                );

                -- MOD ANALYSIS TABLES --
                CREATE TABLE mods (
                    id INTEGER PRIMARY KEY,
                    name TEXT UNIQUE NOT NULL,
                    has_xml INTEGER DEFAULT 0,
                    has_dll INTEGER DEFAULT 0,
                    xml_operations INTEGER DEFAULT 0,
                    csharp_dependencies INTEGER DEFAULT 0,
                    conflicts INTEGER DEFAULT 0,
                    cautions INTEGER DEFAULT 0,
                    -- ModInfo.xml fields
                    display_name TEXT,
                    description TEXT,
                    author TEXT,
                    version TEXT,
                    website TEXT
                );
                CREATE INDEX idx_mod_name ON mods(name);

                CREATE TABLE mod_xml_operations (
                    id INTEGER PRIMARY KEY,
                    mod_id INTEGER,
                    operation TEXT NOT NULL,
                    xpath TEXT NOT NULL,
                    target_type TEXT,
                    target_name TEXT,
                    property_name TEXT,
                    new_value TEXT,
                    element_content TEXT,
                    file_path TEXT,
                    line_number INTEGER,
                    impact_status TEXT
                );
                CREATE INDEX idx_modxml_mod ON mod_xml_operations(mod_id);
                CREATE INDEX idx_modxml_target ON mod_xml_operations(target_type, target_name);

                CREATE TABLE mod_csharp_deps (
                    id INTEGER PRIMARY KEY,
                    mod_id INTEGER,
                    dependency_type TEXT NOT NULL,
                    dependency_name TEXT NOT NULL,
                    source_file TEXT,
                    line_number INTEGER,
                    pattern TEXT
                );
                CREATE INDEX idx_csdep_mod ON mod_csharp_deps(mod_id);
                CREATE INDEX idx_csdep_target ON mod_csharp_deps(dependency_type, dependency_name);

                -- ECOSYSTEM VIEW (materialized after mod analysis) --
                CREATE TABLE ecosystem_entities (
                    id INTEGER PRIMARY KEY,
                    entity_type TEXT NOT NULL,
                    entity_name TEXT NOT NULL,
                    source TEXT NOT NULL,
                    status TEXT DEFAULT 'active',
                    modified_by TEXT,
                    removed_by TEXT,
                    depended_on_by TEXT
                );
                CREATE INDEX idx_eco_type ON ecosystem_entities(entity_type);
                CREATE INDEX idx_eco_status ON ecosystem_entities(status);

                -- SEMANTIC MAPPINGS (LLM-generated or manual descriptions) --
                CREATE TABLE semantic_mappings (
                    id INTEGER PRIMARY KEY,
                    entity_type TEXT NOT NULL,        -- 'xml_property', 'csharp_class', 'csharp_method', 'game_system'
                    entity_name TEXT NOT NULL,        -- 'CarryCapacity', 'XUiM_PlayerInventory', 'HasItems', etc.
                    parent_context TEXT,              -- For methods: the class. For properties: the XML element type
                    layman_description TEXT,          -- 'How many items you can carry in your backpack'
                    technical_description TEXT,       -- 'Integer property on buff/entity passive_effect'
                    player_impact TEXT,               -- 'increase', 'decrease', 'enable', 'disable', 'modify'
                    related_systems TEXT,             -- 'Inventory,Crafting' (comma-separated)
                    example_usage TEXT,               -- 'Used by backpack mods to increase carry slots'
                    generated_by TEXT DEFAULT 'pending', -- 'llm', 'manual', 'heuristic', 'pending'
                    confidence REAL DEFAULT 0.0,      -- 0.0-1.0 confidence score
                    llm_model TEXT,                   -- 'llama-13b', 'mistral-7b', etc.
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(entity_type, entity_name, parent_context)
                );
                CREATE INDEX idx_semantic_type ON semantic_mappings(entity_type);
                CREATE INDEX idx_semantic_name ON semantic_mappings(entity_name);
            ";
            cmd.ExecuteNonQuery();
        }

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

    private static void ParseItems(XDocument doc)
    {
        Console.Write("  items.xml... ");
        int count = 0;
        foreach (var item in doc.Descendants("item"))
        {
            var name = item.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)item;
            var extends = item.Elements("property")
                .FirstOrDefault(p => p.Attribute("name")?.Value == "Extends")?.Attribute("value")?.Value
                ?? item.Attribute("parent")?.Value;

            var defId = AddDefinition("item", name, "items.xml", lineInfo.LineNumber, extends);
            ParseProperties(defId, item, "items.xml");
            count++;
        }
        UpdateStats("item", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseBlocks(XDocument doc)
    {
        Console.Write("  blocks.xml... ");
        int count = 0;
        foreach (var block in doc.Descendants("block"))
        {
            var name = block.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)block;
            var extends = block.Elements("property")
                .FirstOrDefault(p => p.Attribute("name")?.Value == "Extends")?.Attribute("value")?.Value;

            var defId = AddDefinition("block", name, "blocks.xml", lineInfo.LineNumber, extends);
            ParseProperties(defId, block, "blocks.xml");
            count++;
        }
        UpdateStats("block", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseEntityClasses(XDocument doc)
    {
        Console.Write("  entityclasses.xml... ");
        int count = 0;
        foreach (var entity in doc.Descendants("entity_class"))
        {
            var name = entity.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)entity;
            var extends = entity.Attribute("extends")?.Value;

            var defId = AddDefinition("entity_class", name, "entityclasses.xml", lineInfo.LineNumber, extends);
            ParseProperties(defId, entity, "entityclasses.xml");
            count++;
        }
        UpdateStats("entity_class", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseEntityGroups(XDocument doc)
    {
        Console.Write("  entitygroups.xml... ");
        int count = 0;
        foreach (var group in doc.Descendants("entitygroup"))
        {
            var name = group.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)group;
            var defId = AddDefinition("entity_group", name, "entitygroups.xml", lineInfo.LineNumber, null);

            var content = group.Value;
            if (!string.IsNullOrWhiteSpace(content))
            {
                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var entityName = line.Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(entityName))
                        AddReference("xml", defId, "entitygroups.xml", lineInfo.LineNumber, "entity_class", entityName, "group_member");
                }
            }
            count++;
        }
        UpdateStats("entity_group", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseBuffs(XDocument doc)
    {
        Console.Write("  buffs.xml... ");
        int count = 0;
        foreach (var buff in doc.Descendants("buff"))
        {
            var name = buff.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)buff;
            var defId = AddDefinition("buff", name, "buffs.xml", lineInfo.LineNumber, null);
            ParseProperties(defId, buff, "buffs.xml");
            ParseTriggeredEffects(defId, buff);
            count++;
        }
        UpdateStats("buff", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseRecipes(XDocument doc)
    {
        Console.Write("  recipes.xml... ");
        int count = 0;
        foreach (var recipe in doc.Descendants("recipe"))
        {
            var name = recipe.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)recipe;
            var defId = AddDefinition("recipe", name, "recipes.xml", lineInfo.LineNumber, null);

            AddReference("xml", defId, "recipes.xml", lineInfo.LineNumber, "item", name, "recipe_output");

            foreach (var ingredient in recipe.Elements("ingredient"))
            {
                var ingredientName = ingredient.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(ingredientName))
                    AddReference("xml", defId, "recipes.xml", lineInfo.LineNumber, "item", ingredientName, "recipe_ingredient");
            }
            count++;
        }
        UpdateStats("recipe", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseSounds(XDocument doc)
    {
        Console.Write("  sounds.xml... ");
        int count = 0;
        foreach (var sound in doc.Descendants("SoundDataNode"))
        {
            var name = sound.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)sound;
            AddDefinition("sound", name, "sounds.xml", lineInfo.LineNumber, null);
            count++;
        }
        UpdateStats("sound", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseVehicles(XDocument doc)
    {
        Console.Write("  vehicles.xml... ");
        int count = 0;
        foreach (var vehicle in doc.Descendants("vehicle"))
        {
            var name = vehicle.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)vehicle;
            var defId = AddDefinition("vehicle", name, "vehicles.xml", lineInfo.LineNumber, null);
            ParseProperties(defId, vehicle, "vehicles.xml");
            count++;
        }
        UpdateStats("vehicle", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseQuests(XDocument doc)
    {
        Console.Write("  quests.xml... ");
        int count = 0;
        foreach (var quest in doc.Descendants("quest"))
        {
            var id = quest.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            var lineInfo = (IXmlLineInfo)quest;
            var defId = AddDefinition("quest", id, "quests.xml", lineInfo.LineNumber, null);
            ParseProperties(defId, quest, "quests.xml");
            count++;
        }
        UpdateStats("quest", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseGameEvents(XDocument doc)
    {
        Console.Write("  gameevents.xml... ");
        int count = 0;
        foreach (var seq in doc.Descendants("action_sequence"))
        {
            var name = seq.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)seq;
            AddDefinition("game_event", name, "gameevents.xml", lineInfo.LineNumber, null);
            count++;
        }
        UpdateStats("game_event", count);
        Console.WriteLine($"{count}");
    }

    private static void ParseProgression(XDocument doc)
    {
        Console.Write("  progression.xml... ");
        int skillCount = 0, perkCount = 0;

        foreach (var skill in doc.Descendants("skill"))
        {
            var name = skill.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)skill;
            AddDefinition("skill", name, "progression.xml", lineInfo.LineNumber, null);
            skillCount++;
        }

        foreach (var perk in doc.Descendants("perk"))
        {
            var name = perk.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)perk;
            AddDefinition("perk", name, "progression.xml", lineInfo.LineNumber, null);
            perkCount++;
        }

        UpdateStats("skill", skillCount);
        UpdateStats("perk", perkCount);
        Console.WriteLine($"{skillCount} skills, {perkCount} perks");
    }

    private static void ParseLoot(XDocument doc)
    {
        Console.Write("  loot.xml... ");
        int containerCount = 0, groupCount = 0;

        foreach (var container in doc.Descendants("lootcontainer"))
        {
            var id = container.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            var lineInfo = (IXmlLineInfo)container;
            AddDefinition("loot_container", id, "loot.xml", lineInfo.LineNumber, null);
            containerCount++;
        }

        foreach (var group in doc.Descendants("lootgroup"))
        {
            var name = group.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)group;
            var defId = AddDefinition("loot_group", name, "loot.xml", lineInfo.LineNumber, null);

            foreach (var item in group.Descendants("item"))
            {
                var itemName = item.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(itemName))
                    AddReference("xml", defId, "loot.xml", lineInfo.LineNumber, "item", itemName, "loot_entry");
            }
            groupCount++;
        }

        UpdateStats("loot_container", containerCount);
        UpdateStats("loot_group", groupCount);
        Console.WriteLine($"{containerCount} containers, {groupCount} groups");
    }

    private static void ParseProperties(long defId, XElement element, string fileName)
    {
        foreach (var prop in element.Elements("property"))
        {
            var name = prop.Attribute("name")?.Value;
            var value = prop.Attribute("value")?.Value;
            var propClass = prop.Attribute("class")?.Value;
            var lineInfo = (IXmlLineInfo)prop;

            if (!string.IsNullOrEmpty(name))
            {
                AddProperty(defId, name, value, propClass, lineInfo.LineNumber);
                TrackPropertyReferences(defId, name, value, lineInfo.LineNumber, fileName);
            }
        }

        // Also parse nested property classes
        foreach (var propClass in element.Elements("property").Where(p => p.Attribute("class") != null))
        {
            var className = propClass.Attribute("class")?.Value;
            foreach (var nested in propClass.Elements("property"))
            {
                var name = nested.Attribute("name")?.Value;
                var value = nested.Attribute("value")?.Value;
                var lineInfo = (IXmlLineInfo)nested;

                if (!string.IsNullOrEmpty(name))
                    AddProperty(defId, name, value, className, lineInfo.LineNumber);
            }
        }
    }

    private static void ParseTriggeredEffects(long defId, XElement buff)
    {
        foreach (var effect in buff.Descendants("triggered_effect"))
        {
            var action = effect.Attribute("action")?.Value;
            var lineInfo = (IXmlLineInfo)effect;

            var buffRef = effect.Attribute("buff")?.Value;
            if (!string.IsNullOrEmpty(buffRef))
            {
                foreach (var b in buffRef.Split(','))
                    AddReference("xml", defId, "buffs.xml", lineInfo.LineNumber, "buff", b.Trim(), $"triggered_effect:{action}");
            }

            var sound = effect.Attribute("sound")?.Value;
            if (!string.IsNullOrEmpty(sound))
                AddReference("xml", defId, "buffs.xml", lineInfo.LineNumber, "sound", sound, $"triggered_effect:{action}");

            var eventRef = effect.Attribute("event")?.Value;
            if (!string.IsNullOrEmpty(eventRef))
                AddReference("xml", defId, "buffs.xml", lineInfo.LineNumber, "game_event", eventRef, $"triggered_effect:{action}");
        }
    }

    private static void TrackPropertyReferences(long defId, string propName, string? value, int line, string fileName)
    {
        if (string.IsNullOrEmpty(value)) return;

        var referenceProps = new Dictionary<string, string>
        {
            { "Extends", "item" },
            { "HandItem", "item" },
            { "BuffOnEat", "buff" },
            { "BuffOnExecute", "buff" },
            { "SpawnEntityName", "entity_class" },
            { "SoundIdle", "sound" },
            { "SoundDeath", "sound" },
            { "SoundAttack", "sound" },
            { "SoundRandom", "sound" },
            { "LootListOnDeath", "loot_group" },
        };

        if (referenceProps.TryGetValue(propName, out var targetType))
            AddReference("xml", defId, fileName, line, targetType, value, $"property:{propName}");
    }

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
            var deps = ScanCSharpDependencies(modDir, modName);
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

        // Show C# mod dependencies
        var modsWithDeps = results.Where(r => r.Dependencies.Count > 0).ToList();
        if (modsWithDeps.Any())
        {
            Console.WriteLine("\n─── C# Mod XML Dependencies ───\n");
            foreach (var mod in modsWithDeps)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"◆ {mod.Name}");
                Console.ResetColor();
                
                var grouped = mod.Dependencies.GroupBy(d => d.Type);
                foreach (var group in grouped.OrderBy(g => g.Key))
                {
                    Console.Write($"    {group.Key}: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(string.Join(", ", group.Select(d => d.Name).Distinct().Take(10)));
                    Console.ResetColor();
                    if (group.Select(d => d.Name).Distinct().Count() > 10)
                        Console.WriteLine($"           ... and {group.Select(d => d.Name).Distinct().Count() - 10} more");
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

    // Cache for decompiled mod code to avoid repeated decompilation
    private static readonly Dictionary<string, string> _decompileCache = new();
    private static string? _decompiledModsDir;

    private static List<CSharpDependency> ScanCSharpDependencies(string modDir, string modName)
    {
        var deps = new List<CSharpDependency>();

        // Patterns that indicate XML dependencies in C# code
        var patterns = GetXmlDependencyPatterns();

        // Find DLLs in the mod folder (skip 0Harmony.dll and other framework DLLs)
        var modDlls = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
            .Where(dll => !Path.GetFileName(dll).StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Equals("Mono.Cecil.dll", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Contains("System."))
            .Where(dll => !Path.GetFileName(dll).Contains("Microsoft."))
            .ToList();

        if (modDlls.Count == 0)
            return deps;

        // Decompile each mod DLL and scan
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
                                var name = match.Groups[nameGroup].Value;
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

    private static (string Pattern, string Type, int NameGroup)[] GetXmlDependencyPatterns()
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
            (@"ItemClass\.GetItem\s*\(\s*""([^""]+)""\s*\)\.Block", "block", 1),
            
            // Entity lookups
            (@"EntityClass\.FromString\s*\(\s*""([^""]+)""\s*\)", "entity_class", 1),
            (@"EntityFactory\.CreateEntity\s*\([^,]*,\s*""([^""]+)""\s*\)", "entity_class", 1),
            
            // Buff lookups
            (@"BuffManager\.GetBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"BuffClass\.GetBuffClass\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"\.AddBuff\s*\(\s*""([^""]+)""\s*[\),]", "buff", 1),
            (@"\.RemoveBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"\.HasBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"BuffManager\.Server_AddBuff\s*\([^,]*,\s*""([^""]+)""\s*\)", "buff", 1),
            
            // Recipe lookups
            (@"CraftingManager\.GetRecipe\s*\(\s*""([^""]+)""\s*\)", "recipe", 1),
            (@"Recipe\.GetRecipe\s*\(\s*""([^""]+)""\s*\)", "recipe", 1),
            
            // Sound lookups - various patterns
            (@"Manager\.Play\s*\([^,]*,\s*""([^""]+)""\s*[\),]", "sound", 1),
            (@"Manager\.BroadcastPlay\s*\([^,]*,\s*""([^""]+)""\s*[\),]", "sound", 1),
            (@"Audio\.Manager\.Play\s*\(\s*""([^""]+)""\s*[\),]", "sound", 1),
            (@"Manager\.PlayInsidePlayerHead\s*\(\s*""([^""]+)""\s*[,\)]", "sound", 1),
            (@"PlayInsidePlayerHead\s*\(\s*""([^""]+)""\s*[,\)]", "sound", 1),
            // Constant strings that look like sound names (fallbacks)
            (@"=\s*""([\w\-]+destroy)""\s*;", "sound", 1),
            (@"=\s*""([\w\-]+shatter)""\s*;", "sound", 1),
            (@"=\s*""(sound_[\w\-]+)""\s*;", "sound", 1),
            
            // Quest lookups
            (@"QuestClass\.GetQuest\s*\(\s*""([^""]+)""\s*\)", "quest", 1),
            
            // Loot lookups
            (@"LootContainer\.GetLootContainer\s*\(\s*""([^""]+)""\s*\)", "lootcontainer", 1),
            
            // Progression lookups
            (@"Progression\.GetProgressionClass\s*\(\s*""([^""]+)""\s*\)", "progression", 1),
            
            // Trader lookups
            (@"TraderInfo\.GetTraderInfo\s*\(\s*""([^""]+)""\s*\)", "trader_info", 1),
            
            // Workstation/Action lookups
            (@"Workstation\s*=\s*""([^""]+)""", "workstation", 1),
            
            // Localization key lookups (possible item/block name refs)
            (@"Localization\.Get\s*\(\s*""([^""]+)""\s*\)", "localization", 1),
            
            // Harmony patches - class being patched
            (@"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)", "harmony_class", 1),
            (@"\[HarmonyPatch\s*\(\s*""([^""]+)""\s*\)", "harmony_method", 1),
            (@"\[HarmonyPatch\s*\(\s*typeof\s*\([^)]+\)\s*,\s*""([^""]+)""\s*\)", "harmony_method", 1),
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

    private static List<string> DecompileModDll(string dllPath, string modName)
    {
        var csFiles = new List<string>();
        
        // Check cache first
        if (_decompileCache.TryGetValue(dllPath, out var cachedDir))
        {
            if (Directory.Exists(cachedDir))
                return Directory.GetFiles(cachedDir, "*.cs", SearchOption.AllDirectories).ToList();
        }

        // Create temp directory for decompiled output
        if (_decompiledModsDir == null)
        {
            _decompiledModsDir = Path.Combine(Path.GetTempPath(), "XmlIndexer_ModDecompile_" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(_decompiledModsDir);
            
            // Register cleanup on exit
            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupDecompiledMods();
        }

        var dllName = Path.GetFileNameWithoutExtension(dllPath);
        var outputDir = Path.Combine(_decompiledModsDir, modName, dllName);
        
        try
        {
            Directory.CreateDirectory(outputDir);
            
            // Run ilspycmd to decompile
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
            if (process == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Warning: ilspycmd not found. Install with: dotnet tool install -g ilspycmd");
                Console.ResetColor();
                return csFiles;
            }

            process.WaitForExit(30000); // 30 second timeout
            
            if (process.ExitCode == 0 && Directory.Exists(outputDir))
            {
                csFiles = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories).ToList();
                _decompileCache[dllPath] = outputDir;
            }
        }
        catch (Exception ex)
        {
            // ilspycmd not installed or other error - silently skip
            if (ex.Message.Contains("not recognized") || ex.Message.Contains("not found"))
            {
                // Only warn once
                if (!_decompileCache.ContainsKey("__warned__"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    Note: Install ilspycmd for C# mod analysis: dotnet tool install -g ilspycmd");
                    Console.ResetColor();
                    _decompileCache["__warned__"] = "";
                }
            }
        }

        return csFiles;
    }

    private static void CleanupDecompiledMods()
    {
        if (_decompiledModsDir != null && Directory.Exists(_decompiledModsDir))
        {
            try
            {
                Directory.Delete(_decompiledModsDir, true);
            }
            catch { /* Best effort cleanup */ }
        }
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
                var files = DecompileModDll(dll, modName);
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
        AnalyzeAllModsToDatabase(modsFolder);
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

    private static void AnalyzeAllModsToDatabase(string modsFolder)
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        var modDirs = Directory.GetDirectories(modsFolder)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .OrderBy(d => Path.GetFileName(d))
            .ToList();

        using var transaction = db.BeginTransaction();

        foreach (var modDir in modDirs)
        {
            var modName = Path.GetFileName(modDir);
            var configPath = Path.Combine(modDir, "Config");
            var hasXml = Directory.Exists(configPath) && Directory.GetFiles(configPath, "*.xml").Length > 0;
            var hasDll = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
                         .Any(d => !Path.GetFileName(d).StartsWith("0Harmony") && 
                                   !Path.GetFileName(d).Contains("Mono.Cecil"));

            // Parse ModInfo.xml if present
            string? displayName = null, description = null, author = null, version = null, website = null;
            var modInfoPath = Path.Combine(modDir, "ModInfo.xml");
            if (File.Exists(modInfoPath))
            {
                try
                {
                    var modInfoDoc = XDocument.Load(modInfoPath);
                    displayName = modInfoDoc.Root?.Element("DisplayName")?.Attribute("value")?.Value 
                                  ?? modInfoDoc.Root?.Element("Name")?.Attribute("value")?.Value;
                    description = modInfoDoc.Root?.Element("Description")?.Attribute("value")?.Value;
                    author = modInfoDoc.Root?.Element("Author")?.Attribute("value")?.Value;
                    version = modInfoDoc.Root?.Element("Version")?.Attribute("value")?.Value;
                    website = modInfoDoc.Root?.Element("Website")?.Attribute("value")?.Value;
                }
                catch { /* Ignore ModInfo.xml parse errors */ }
            }

            // Insert mod record
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO mods (name, has_xml, has_dll, display_name, description, author, version, website) 
                    VALUES ($name, $hasXml, $hasDll, $displayName, $description, $author, $version, $website)";
                cmd.Parameters.AddWithValue("$name", modName);
                cmd.Parameters.AddWithValue("$hasXml", hasXml ? 1 : 0);
                cmd.Parameters.AddWithValue("$hasDll", hasDll ? 1 : 0);
                cmd.Parameters.AddWithValue("$displayName", displayName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$description", description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$author", author ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$version", version ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$website", website ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            long modId;
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT last_insert_rowid()";
                modId = (long)cmd.ExecuteScalar()!;
            }

            int xmlOps = 0, conflicts = 0, cautions = 0;

            // Analyze XML operations
            if (hasXml)
            {
                var xmlFiles = Directory.GetFiles(configPath, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    var results = AnalyzeModXmlToDatabase(xmlFile, db, modId, modName);
                    xmlOps += results.Operations;
                    conflicts += results.Conflicts;
                    cautions += results.Cautions;
                }
            }

            // Analyze C# dependencies
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

            // Update mod stats
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"UPDATE mods SET 
                    xml_operations = $ops, csharp_dependencies = $deps,
                    conflicts = $conflicts, cautions = $cautions
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

    // XmlAnalysisResult defined in Models/DataModels.cs

    private static XmlAnalysisResult AnalyzeModXmlToDatabase(string filePath, SqliteConnection db, long modId, string modName)
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

                    // Extract target if possible
                    var target = ExtractTargetFromXPath(xpath);
                    
                    // Extract property name and value from xpath (e.g., @value, @name)
                    var propertyName = ExtractPropertyFromXPath(xpath);
                    
                    // Get the new value - either from element text, or from xpath for attribute sets
                    var newValue = element.Value?.Trim();
                    if (string.IsNullOrEmpty(newValue))
                    {
                        // For setattribute ops, the value might be in the xpath itself
                        newValue = ExtractValueFromXPath(xpath);
                    }
                    
                    // Store element content for context (limited)
                    var elementContent = element.ToString();
                    if (elementContent.Length > 4000) elementContent = elementContent.Substring(0, 4000) + "...";

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

    // ═══════════════════════════════════════════════════════════════════════════
    // STATISTICS - Fun and useful insights
    // ═══════════════════════════════════════════════════════════════════════════

    private static int ShowStats()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  7D2D ECOSYSTEM STATISTICS                                       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        // BASE GAME STATS
        Console.WriteLine("📊 BASE GAME DATA");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT definition_type, COUNT(*) as cnt 
                FROM xml_definitions GROUP BY definition_type ORDER BY cnt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var count = reader.GetInt32(1);
                var bar = new string('▓', Math.Min(count / 50, 30));
                Console.WriteLine($"  {type,-20} {count,6}  {bar}");
            }
        }

        Console.WriteLine();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_definitions";
            Console.WriteLine($"  Total Definitions:     {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_properties";
            Console.WriteLine($"  Total Properties:      {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_references";
            Console.WriteLine($"  Total Cross-Refs:      {cmd.ExecuteScalar()}");
        }

        // MOD STATS
        Console.WriteLine("\n\n🔧 MOD STATISTICS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods";
            Console.WriteLine($"  Total Mods:            {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_xml = 1 AND has_dll = 0";
            Console.WriteLine($"  XML-Only Mods:         {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_dll = 1 AND has_xml = 0";
            Console.WriteLine($"  C#-Only Mods:          {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_xml = 1 AND has_dll = 1";
            Console.WriteLine($"  Hybrid Mods:           {cmd.ExecuteScalar()}");
        }

        Console.WriteLine("\n  Operations by Type:");
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT operation, COUNT(*) FROM mod_xml_operations 
                GROUP BY operation ORDER BY COUNT(*) DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"    {reader.GetString(0),-18} {reader.GetInt32(1),6}");
            }
        }

        // FUN FACTS
        Console.WriteLine("\n\n🎮 FUN FACTS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name FROM xml_definitions 
                WHERE definition_type = 'item' ORDER BY LENGTH(name) DESC LIMIT 1";
            Console.WriteLine($"  Longest Item Name:     {cmd.ExecuteScalar()}");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_name, COUNT(*) as refs FROM xml_references 
                WHERE target_type = 'item' GROUP BY target_name ORDER BY refs DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                Console.WriteLine($"  Most Referenced Item:  {reader.GetString(0)} ({reader.GetInt32(1)} refs)");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name FROM xml_definitions 
                WHERE definition_type = 'block' AND name LIKE '%zombie%' LIMIT 1";
            var zombieBlock = cmd.ExecuteScalar();
            if (zombieBlock != null)
                Console.WriteLine($"  A Zombie Block:        {zombieBlock}");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(DISTINCT extends) FROM xml_definitions WHERE extends IS NOT NULL";
            Console.WriteLine($"  Unique Parent Classes: {cmd.ExecuteScalar()}");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name, COUNT(*) as props FROM xml_properties 
                JOIN xml_definitions ON xml_properties.definition_id = xml_definitions.id
                GROUP BY xml_properties.definition_id ORDER BY props DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                Console.WriteLine($"  Most Complex Entity:   {reader.GetString(0)} ({reader.GetInt32(1)} props)");
        }

        // CROSS-MOD INSIGHTS
        Console.WriteLine("\n\n⚡ CROSS-MOD INSIGHTS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_name, COUNT(DISTINCT mod_id) as mod_count
                FROM mod_xml_operations 
                WHERE target_name IS NOT NULL
                GROUP BY target_type, target_name 
                HAVING mod_count > 1
                ORDER BY mod_count DESC LIMIT 5";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("  Most Contested Entities (modified by multiple mods):");
            while (reader.Read())
            {
                Console.WriteLine($"    • {reader.GetString(0)}: {reader.GetInt32(1)} mods");
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.name, COUNT(*) as deps
                FROM mod_csharp_deps mcd
                JOIN mods m ON mcd.mod_id = m.id
                GROUP BY m.id ORDER BY deps DESC LIMIT 3";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n  C# Mods with Most XML Dependencies:");
            while (reader.Read())
            {
                Console.WriteLine($"    • {reader.GetString(0)}: {reader.GetInt32(1)} deps");
            }
        }

        Console.WriteLine();
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ECOSYSTEM VIEW - Combined codebase + mods health check
    // ═══════════════════════════════════════════════════════════════════════════

    private static int ShowEcosystem()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  7D2D ECOSYSTEM HEALTH CHECK                                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        // Overall health
        int active = 0, modified = 0, removed = 0, depended = 0;
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE status = 'active'";
            active = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE modified_by IS NOT NULL";
            modified = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE status = 'removed'";
            removed = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE depended_on_by IS NOT NULL";
            depended = Convert.ToInt32(cmd.ExecuteScalar());
        }

        Console.WriteLine("📊 ECOSYSTEM OVERVIEW");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Active Entities:       {active,6}  (base game entities still in play)");
        Console.WriteLine($"  Modified by Mods:      {modified,6}  (entities tweaked by XML mods)");
        Console.WriteLine($"  Removed by Mods:       {removed,6}  (entities deleted by mods)");
        Console.WriteLine($"  C# Dependencies:       {depended,6}  (entities needed by code mods)");

        // Danger zone: removed entities that are depended upon
        Console.WriteLine("\n\n⚠️  DANGER ZONE - Potential Breaking Conflicts");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT entity_type, entity_name, removed_by, depended_on_by
                FROM ecosystem_entities
                WHERE status = 'removed' AND depended_on_by IS NOT NULL
                LIMIT 10";
            using var reader = cmd.ExecuteReader();
            int dangerCount = 0;
            while (reader.Read())
            {
                dangerCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  🔥 {reader.GetString(0)}: {reader.GetString(1)}");
                Console.ResetColor();
                Console.WriteLine($"     Removed by:    {reader.GetString(2)}");
                Console.WriteLine($"     Needed by:     {reader.GetString(3)}");
            }
            if (dangerCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✓ No critical conflicts detected!");
                Console.ResetColor();
            }
        }

        // Mod impact summary
        Console.WriteLine("\n\n📦 MOD IMPACT SUMMARY");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.name, m.xml_operations, m.csharp_dependencies, m.conflicts, m.cautions,
                (SELECT COUNT(*) FROM mod_xml_operations WHERE mod_id = m.id AND operation = 'remove') as removes
                FROM mods m
                ORDER BY m.conflicts DESC, m.cautions DESC, m.xml_operations DESC
                LIMIT 15";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine($"  {"Mod Name",-35} {"Ops",5} {"Deps",5} {"Rem",4} {"Status",8}");
            Console.WriteLine($"  {new string('-', 35)} {new string('-', 5)} {new string('-', 5)} {new string('-', 4)} {new string('-', 8)}");
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (name.Length > 35) name = name[..32] + "...";
                var ops = reader.GetInt32(1);
                var deps = reader.GetInt32(2);
                var conflicts = reader.GetInt32(3);
                var removes = reader.GetInt32(5);
                
                string status;
                ConsoleColor color;
                if (conflicts > 0) { status = "CONFLICT"; color = ConsoleColor.Red; }
                else if (removes > 0) { status = "REMOVES"; color = ConsoleColor.Yellow; }
                else if (deps > 0) { status = "C#"; color = ConsoleColor.Cyan; }
                else if (ops > 0) { status = "OK"; color = ConsoleColor.Green; }
                else { status = "PASSIVE"; color = ConsoleColor.DarkGray; }

                Console.Write($"  {name,-35} {ops,5} {deps,5} {removes,4} ");
                Console.ForegroundColor = color;
                Console.WriteLine($"{status,8}");
                Console.ResetColor();
            }
        }

        // Predict new mod compatibility
        Console.WriteLine("\n\n🔮 COMPATIBILITY TIPS FOR NEW MODS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine("  Based on current ecosystem analysis:");
        Console.WriteLine();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT entity_type, COUNT(*) as cnt
                FROM ecosystem_entities WHERE status = 'removed'
                GROUP BY entity_type ORDER BY cnt DESC LIMIT 3";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("  ⚠️  Types with most removals (avoid depending on these):");
            while (reader.Read())
            {
                Console.WriteLine($"      • {reader.GetString(0)}: {reader.GetInt32(1)} removed");
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_type, target_name, COUNT(*) as cnt
                FROM mod_xml_operations
                WHERE target_name IS NOT NULL
                GROUP BY target_type, target_name
                ORDER BY cnt DESC LIMIT 3";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n  🎯 Most modified entities (high conflict risk if you touch these):");
            while (reader.Read())
            {
                Console.WriteLine($"      • {reader.GetString(0)}/{reader.GetString(1)}: {reader.GetInt32(2)} mods");
            }
        }

        Console.WriteLine("\n  💡 Safe zones: Entities with no current mod activity are safest to extend");
        Console.WriteLine();

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // REPORT GENERATION - Export to HTML, Markdown, JSON
    // ═══════════════════════════════════════════════════════════════════════════

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
            GenerateMarkdownReport(mdPath, reportData);
            Console.WriteLine($"  ✓ Markdown Report: {mdPath}");
        }

        if (formats.Contains("json"))
        {
            var jsonPath = Path.Combine(outputDir, $"ecosystem_data_{timestamp}.json");
            GenerateJsonExport(jsonPath, reportData, db);
            Console.WriteLine($"  ✓ JSON Export: {jsonPath}");
        }

        Console.WriteLine("\n✓ Reports generated successfully!");
        return 0;
    }

    // GatherReportData and GenerateHtmlReport moved to Reports/ modules

    private static string GetHealthIcon(string health) => health switch
    {
        "Healthy" => "✅",
        "Review" => "⚠️",
        "Broken" => "❌",
        _ => "❓"
    };

    private static void GenerateMarkdownReport(string path, ReportData data)
    {
        var md = $@"# 🎮 7D2D Mod Ecosystem Report

Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

## 📊 Base Game Statistics

| Metric | Value |
|--------|-------|
| Total Definitions | {data.TotalDefinitions:N0} |
| Total Properties | {data.TotalProperties:N0} |
| Cross-References | {data.TotalReferences:N0} |

### Definitions by Type

| Type | Count |
|------|-------|
{string.Join("\n", data.DefinitionsByType.Select(kv => $"| {kv.Key} | {kv.Value:N0} |"))}

## 🔧 Mod Statistics

| Metric | Value |
|--------|-------|
| Total Mods | {data.TotalMods} |
| XML-Only Mods | {data.XmlMods} |
| C#-Only Mods | {data.CSharpMods} |
| Hybrid Mods | {data.HybridMods} |

### XML Operations by Type

| Operation | Count |
|-----------|-------|
{string.Join("\n", data.OperationsByType.Select(kv => $"| {kv.Key} | {kv.Value} |"))}

{(data.CSharpByType.Any() ? $@"### C# Dependencies by Type

| Type | Count |
|------|-------|
{string.Join("\n", data.CSharpByType.Select(kv => $"| {kv.Key} | {kv.Value} |"))}" : "")}

{(data.HarmonyPatches.Any() ? $@"### 🔌 Harmony Patches

| Mod | Target Class | Method | Patch Type |
|-----|--------------|--------|------------|
{string.Join("\n", data.HarmonyPatches.Select(p => $"| {p.ModName} | `{p.ClassName}` | `{p.MethodName}` | {p.PatchType} |"))}" : "")}

{(data.ClassExtensions.Any() ? $@"### 🧬 Class Extensions

| Mod | Extends/Implements | Class |
|-----|-------------------|-------|
{string.Join("\n", data.ClassExtensions.Select(e => $"| {e.ModName} | {e.BaseClass} | `{e.ChildClass}` |"))}" : "")}

## 🌍 Ecosystem Health

| Metric | Value |
|--------|-------|
| Active Entities | {data.ActiveEntities:N0} |
| Modified by Mods | {data.ModifiedEntities} |
| Removed by Mods | {data.RemovedEntities} |
| C# Dependencies | {data.DependedEntities} |

{(data.DangerZone.Any() ? $@"
### ⚠️ Critical Conflicts

| Entity | Removed By | Needed By |
|--------|------------|-----------|
{string.Join("\n", data.DangerZone.Select(d => $"| {d.Type}/{d.Name} | {d.RemovedBy} | {d.DependedBy} |"))}" : @"
### ✅ No Critical Conflicts Detected

All C# mod dependencies are satisfied.")}

## 📦 Mod Overview

**Type Legend:** XML = Config changes only | C# Code = Uses Harmony patches | Hybrid = Both | Assets = Textures/sounds only

**Health Legend:** ✅ Healthy = Safe to use | ⚠️ Review = Check notes | ❌ Broken = Has problems

| Mod Name | Type | Health | Notes |
|----------|------|--------|-------|
{string.Join("\n", data.ModSummary.Select(m => $"| {m.Name} | {m.ModType} | {GetHealthIcon(m.Health)} {m.Health} | {m.HealthNote} |"))}

{GenerateModBehaviorMd(data.ModBehaviors)}

## 🎮 Fun Facts

- **Longest Item Name:** `{data.LongestItemName}`
- **Most Referenced:** `{data.MostReferencedItem}` ({data.MostReferencedCount:N0} things reference it)
- **Most Complex:** `{data.MostComplexEntity}` ({data.MostComplexProps:N0} properties)
- **Most Connected:** `{data.MostConnectedEntity}` (references {data.MostConnectedRefs:N0} different things)
- **Most Depended Upon:** `{data.MostDependedEntity}` ({data.MostDependedCount:N0} entities need this)

{GenerateContestedEntitiesMd(data.ContestedEntities)}

---
*Generated by 7D2D Mod Ecosystem Analyzer*
";

        File.WriteAllText(path, md);
    }

    private static string GenerateContestedEntitiesMd(List<ContestedEntity> entities)
    {
        if (!entities.Any())
            return "✅ **No Shared Entities** - No game entities are modified by multiple mods!";
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## ⚡ Shared Entities\n");
        sb.AppendLine("Entities touched by multiple mods:\n");
        
        foreach (var c in entities)
        {
            sb.AppendLine($"### {c.EntityType}/{c.EntityName}");
            sb.AppendLine($"**Risk:** {c.RiskLevel} - {c.RiskReason}\n");
            sb.AppendLine("| Mod | Operation |");
            sb.AppendLine("|-----|-----------|");
            foreach (var a in c.ModActions)
            {
                sb.AppendLine($"| {a.ModName} | {a.Operation} |");
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    private static string GenerateModBehaviorMd(List<ModBehavior> behaviors)
    {
        var meaningfulBehaviors = behaviors.Where(b => 
            b.KeyFeatures.Count > 0 || b.Warnings.Count > 0 || !b.OneLiner.Contains("no detectable")).ToList();
        
        if (!meaningfulBehaviors.Any())
            return "## 📝 Behavioral Analysis\n\nNo complex mod behaviors detected.\n";
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 📝 What Each Mod Does\n");
        sb.AppendLine("Human-readable analysis of what each mod actually does to your game.\n");
        
        foreach (var b in meaningfulBehaviors.OrderByDescending(x => x.KeyFeatures.Count + x.Warnings.Count))
        {
            sb.AppendLine($"### {b.ModName}");
            sb.AppendLine($"*{b.OneLiner}*\n");
            
            if (b.KeyFeatures.Count > 0)
            {
                sb.AppendLine("**What it does:**");
                foreach (var feature in b.KeyFeatures.Take(5))
                    sb.AppendLine($"- {feature}");
                if (b.KeyFeatures.Count > 5)
                    sb.AppendLine($"- ...and {b.KeyFeatures.Count - 5} more");
                sb.AppendLine();
            }
            
            if (b.SystemsAffected.Count > 0)
                sb.AppendLine($"**Systems affected:** {string.Join(", ", b.SystemsAffected)}\n");
            
            if (b.Warnings.Count > 0)
            {
                sb.AppendLine("**Warnings:**");
                foreach (var warning in b.Warnings)
                    sb.AppendLine($"- {warning}");
                sb.AppendLine();
            }
        }
        
        return sb.ToString();
    }

    private static void GenerateJsonExport(string path, ReportData data, SqliteConnection db)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($@"  ""generated"": ""{DateTime.Now:O}"",");
        sb.AppendLine($@"  ""baseGame"": {{");
        sb.AppendLine($@"    ""totalDefinitions"": {data.TotalDefinitions},");
        sb.AppendLine($@"    ""totalProperties"": {data.TotalProperties},");
        sb.AppendLine($@"    ""totalReferences"": {data.TotalReferences},");
        sb.AppendLine($@"    ""definitionsByType"": {{");
        sb.AppendLine(string.Join(",\n", data.DefinitionsByType.Select(kv => $@"      ""{kv.Key}"": {kv.Value}")));
        sb.AppendLine("    }");
        sb.AppendLine("  },");
        sb.AppendLine($@"  ""mods"": {{");
        sb.AppendLine($@"    ""total"": {data.TotalMods},");
        sb.AppendLine($@"    ""xmlOnly"": {data.XmlMods},");
        sb.AppendLine($@"    ""csharpOnly"": {data.CSharpMods},");
        sb.AppendLine($@"    ""hybrid"": {data.HybridMods},");
        sb.AppendLine($@"    ""operationsByType"": {{");
        sb.AppendLine(string.Join(",\n", data.OperationsByType.Select(kv => $@"      ""{kv.Key}"": {kv.Value}")));
        sb.AppendLine("    },");
        sb.AppendLine($@"    ""list"": [");
        sb.AppendLine(string.Join(",\n", data.ModSummary.Select(m => 
            $@"      {{ ""name"": ""{EscapeJson(m.Name)}"", ""type"": ""{m.ModType}"", ""health"": ""{m.Health}"", ""notes"": ""{EscapeJson(m.HealthNote)}"" }}")));
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine($@"  ""ecosystem"": {{");
        sb.AppendLine($@"    ""activeEntities"": {data.ActiveEntities},");
        sb.AppendLine($@"    ""modifiedEntities"": {data.ModifiedEntities},");
        sb.AppendLine($@"    ""removedEntities"": {data.RemovedEntities},");
        sb.AppendLine($@"    ""dependedEntities"": {data.DependedEntities},");
        sb.AppendLine($@"    ""criticalConflicts"": [");
        sb.AppendLine(string.Join(",\n", data.DangerZone.Select(d => 
            $@"      {{ ""type"": ""{d.Type}"", ""name"": ""{EscapeJson(d.Name)}"", ""removedBy"": ""{EscapeJson(d.RemovedBy)}"", ""neededBy"": ""{EscapeJson(d.DependedBy)}"" }}")));
        sb.AppendLine("    ]");
        sb.AppendLine("  }");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString());
    }

    private static string EscapeJson(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    // =====================================================
    // SEMANTIC TRACE EXPORT/IMPORT FOR LLM ANALYSIS
    // =====================================================

    private static int ExportSemanticTraces(string outputPath)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  EXPORTING SEMANTIC TRACES FOR LLM ANALYSIS                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Load existing mappings to skip already-completed items (enables batch processing)
        var existingMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var checkCmd = db.CreateCommand();
            checkCmd.CommandText = @"SELECT entity_type, entity_name, parent_context 
                                     FROM semantic_mappings 
                                     WHERE layman_description IS NOT NULL";
            using var checkReader = checkCmd.ExecuteReader();
            while (checkReader.Read())
            {
                var type = checkReader.GetString(0);
                var name = checkReader.GetString(1);
                var parent = checkReader.IsDBNull(2) ? "" : checkReader.GetString(2);
                existingMappings.Add($"{type}|{name}|{parent}");
            }
            if (existingMappings.Count > 0)
                Console.WriteLine($"  ℹ️  Skipping {existingMappings.Count} already-mapped items (batch mode)");
        }
        catch { /* Table doesn't exist yet */ }

        var traces = new List<SemanticTrace>();

        // Filter function to skip already-mapped items
        bool ShouldInclude(SemanticTrace trace)
        {
            var key = $"{trace.EntityType}|{trace.EntityName}|{trace.ParentContext ?? ""}";
            return !existingMappings.Contains(key);
        }

        // 1. ALL unique property names (the building blocks)
        Console.WriteLine("Collecting ALL property names...");
        var propNames = CollectAllPropertyNames(db).Where(ShouldInclude).ToList();
        traces.AddRange(propNames);
        Console.WriteLine($"  Found {propNames.Count} unique property names");

        // 2. ALL definitions (items, blocks, buffs, entities, etc.)
        Console.WriteLine("Collecting ALL definitions (items, blocks, buffs, etc.)...");
        var definitions = CollectAllDefinitions(db).Where(ShouldInclude).ToList();
        traces.AddRange(definitions);
        Console.WriteLine($"  Found {definitions.Count} definitions");

        // 3. ALL cross-reference patterns
        Console.WriteLine("Collecting cross-reference patterns...");
        var crossRefs = CollectAllCrossReferences(db).Where(ShouldInclude).ToList();
        traces.AddRange(crossRefs);
        Console.WriteLine($"  Found {crossRefs.Count} reference patterns");

        // 4. Definition type summaries
        Console.WriteLine("Collecting definition type summaries...");
        var defTypes = CollectAllDefinitionTypes(db).Where(ShouldInclude).ToList();
        traces.AddRange(defTypes);
        Console.WriteLine($"  Found {defTypes.Count} definition types");

        // 5. C# Classes (from mod analysis)
        Console.WriteLine("Collecting C# class definitions...");
        var csharpClasses = CollectCSharpClassTraces(db).Where(ShouldInclude).ToList();
        traces.AddRange(csharpClasses);
        Console.WriteLine($"  Found {csharpClasses.Count} unique C# classes");

        // Write JSONL output
        Console.WriteLine($"\nWriting {traces.Count} traces to {outputPath}...");
        using var writer = new StreamWriter(outputPath);
        foreach (var trace in traces)
        {
            var json = SerializeTrace(trace);
            writer.WriteLine(json);
        }

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  EXPORTED {traces.Count,5} TRACES                                       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  1. Run: python semantic_mapper.py {outputPath} output_mappings.jsonl");
        Console.WriteLine($"  2. Run: XmlIndexer import-semantic-mappings {_dbPath} output_mappings.jsonl");

        return 0;
    }

    // SemanticTrace defined in Models/DataModels.cs

    // =========================================================================
    // COMPREHENSIVE TRACE COLLECTORS - Captures ALL entities from database
    // =========================================================================

    private static List<SemanticTrace> CollectAllPropertyNames(SqliteConnection db)
    {
        // Get ALL unique property names with usage counts
        var traces = new List<SemanticTrace>();
        
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                p.property_name,
                COUNT(*) as usage_count,
                GROUP_CONCAT(DISTINCT d.definition_type) as used_in_types,
                GROUP_CONCAT(DISTINCT SUBSTR(p.property_value, 1, 50)) as sample_values
            FROM xml_properties p
            JOIN xml_definitions d ON p.definition_id = d.id
            WHERE p.property_name IS NOT NULL AND p.property_name != ''
            GROUP BY p.property_name
            ORDER BY usage_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var propName = reader.GetString(0);
            var usageCount = reader.GetInt32(1);
            var usedInTypes = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var sampleValues = reader.IsDBNull(3) ? "" : reader.GetString(3);
            
            // Truncate sample values for readability
            if (sampleValues.Length > 200) sampleValues = sampleValues.Substring(0, 200) + "...";

            var codeTrace = $@"<!-- Property: {propName} -->
<!-- Used {usageCount} times across: {usedInTypes} -->
<property name=""{propName}"" value=""...""/>

Sample values seen:
{sampleValues}";

            traces.Add(new SemanticTrace(
                EntityType: "property_name",
                EntityName: propName,
                ParentContext: usedInTypes,
                CodeTrace: codeTrace,
                UsageExamples: $"Used {usageCount} times in {usedInTypes}",
                RelatedEntities: null,
                GameContext: InferPropertyGameContext(propName)
            ));
        }

        return traces;
    }

    private static List<SemanticTrace> CollectAllDefinitions(SqliteConnection db)
    {
        // Get ALL definitions (items, blocks, buffs, etc.) - the 15,534 entities
        var traces = new List<SemanticTrace>();
        
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                d.definition_type,
                d.name,
                d.extends,
                COUNT(p.id) as prop_count,
                GROUP_CONCAT(p.property_name || '=' || COALESCE(SUBSTR(p.property_value, 1, 30), ''), '; ') as props
            FROM xml_definitions d
            LEFT JOIN xml_properties p ON d.id = p.definition_id
            GROUP BY d.id
            ORDER BY d.definition_type, d.name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var defType = reader.GetString(0);
            var name = reader.GetString(1);
            var extends = reader.IsDBNull(2) ? null : reader.GetString(2);
            var propCount = reader.GetInt32(3);
            var props = reader.IsDBNull(4) ? "" : reader.GetString(4);
            
            // Build representative XML
            var extendsAttr = extends != null ? $" extends=\"{extends}\"" : "";
            var propsPreview = string.Join("\n", props.Split("; ").Take(8).Where(p => !string.IsNullOrEmpty(p))
                .Select(p => $"  <property {FormatPropForTrace(p)}/>"));
            
            var codeTrace = $@"<{defType} name=""{name}""{extendsAttr}>
{propsPreview}
  <!-- ... {propCount} total properties -->
</{defType}>";

            traces.Add(new SemanticTrace(
                EntityType: "definition",
                EntityName: name,
                ParentContext: defType,
                CodeTrace: codeTrace,
                UsageExamples: extends != null ? $"Extends {extends}" : null,
                RelatedEntities: extends,
                GameContext: InferDefinitionGameContext(defType, name)
            ));
        }

        return traces;
    }

    private static List<SemanticTrace> CollectAllCrossReferences(SqliteConnection db)
    {
        // Get unique reference PATTERNS (not all 47k refs, but the types of relationships)
        var traces = new List<SemanticTrace>();
        
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                source_type,
                target_type,
                reference_context,
                COUNT(*) as ref_count,
                GROUP_CONCAT(DISTINCT target_name) as sample_targets
            FROM xml_references
            GROUP BY source_type, target_type, reference_context
            ORDER BY ref_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var sourceType = reader.GetString(0);
            var targetType = reader.GetString(1);
            var refContext = reader.IsDBNull(2) ? "direct" : reader.GetString(2);
            var refCount = reader.GetInt32(3);
            var sampleTargets = reader.IsDBNull(4) ? "" : reader.GetString(4);
            
            // Truncate
            if (sampleTargets.Length > 150) sampleTargets = sampleTargets.Substring(0, 150) + "...";

            var relationshipName = $"{sourceType}→{targetType}";
            var codeTrace = $@"<!-- Cross-reference pattern: {relationshipName} -->
<!-- Context: {refContext} -->
<!-- Found {refCount} times in the game data -->

When a {sourceType} references a {targetType} via '{refContext}':
  - Source type: {sourceType} (e.g., items, blocks, buffs)
  - Target type: {targetType} (what is being referenced)
  - Example targets: {sampleTargets}";

            traces.Add(new SemanticTrace(
                EntityType: "cross_reference_pattern",
                EntityName: relationshipName,
                ParentContext: refContext,
                CodeTrace: codeTrace,
                UsageExamples: $"{refCount} occurrences",
                RelatedEntities: sampleTargets,
                GameContext: $"{sourceType} → {targetType} relationships"
            ));
        }

        return traces;
    }

    private static List<SemanticTrace> CollectAllDefinitionTypes(SqliteConnection db)
    {
        // Get summary of each definition TYPE (item, block, buff, etc.)
        var traces = new List<SemanticTrace>();
        
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                definition_type,
                COUNT(*) as count,
                GROUP_CONCAT(DISTINCT name) as sample_names
            FROM xml_definitions
            GROUP BY definition_type
            ORDER BY count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var defType = reader.GetString(0);
            var count = reader.GetInt32(1);
            var sampleNames = reader.IsDBNull(2) ? "" : reader.GetString(2);
            
            if (sampleNames.Length > 200) sampleNames = sampleNames.Substring(0, 200) + "...";

            var codeTrace = $@"<!-- Definition Type: {defType} -->
<!-- Total count: {count} definitions -->

The game has {count} '{defType}' definitions.

Sample {defType} names:
{sampleNames}

When a mod modifies a '{defType}', it typically affects:
- [TO BE FILLED BY LLM: What gameplay aspect does this affect?]";

            traces.Add(new SemanticTrace(
                EntityType: "definition_type",
                EntityName: defType,
                ParentContext: null,
                CodeTrace: codeTrace,
                UsageExamples: $"{count} definitions exist",
                RelatedEntities: null,
                GameContext: InferDefinitionTypeContext(defType)
            ));
        }

        return traces;
    }

    private static List<SemanticTrace> CollectCSharpClassTraces(SqliteConnection db)
    {
        var traces = new List<SemanticTrace>();
        var classMethods = new Dictionary<string, List<string>>();

        // Get unique class names and their methods from mod_csharp_deps
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT dependency_name, dependency_type
            FROM mod_csharp_deps
            WHERE dependency_type IN ('harmony_class', 'harmony_method')
            ORDER BY dependency_type, dependency_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var depType = reader.GetString(1);

            if (depType == "harmony_class")
            {
                if (!classMethods.ContainsKey(name))
                    classMethods[name] = new List<string>();
            }
            else if (depType == "harmony_method")
            {
                var lastClass = classMethods.Keys.LastOrDefault();
                if (lastClass != null && !classMethods[lastClass].Contains(name))
                    classMethods[lastClass].Add(name);
            }
        }

        foreach (var (className, methods) in classMethods)
        {
            var gameContext = InferGameContextFromClassName(className);
            var methodList = methods.Count > 0 
                ? string.Join("\n", methods.Take(10).Select(m => $"    public void {m}() {{ ... }}"))
                : "    // No methods detected";

            var codeTrace = $@"// Game class: {className}
// Game Context: {gameContext}
// Patched methods: {methods.Count}

public class {className}
{{
{methodList}
}}";

            traces.Add(new SemanticTrace(
                EntityType: "csharp_class",
                EntityName: className,
                ParentContext: null,
                CodeTrace: codeTrace,
                UsageExamples: "Patched by mods via Harmony",
                RelatedEntities: methods.Count > 0 ? string.Join(", ", methods.Take(5)) : null,
                GameContext: gameContext
            ));
        }

        return traces;
    }

    private static string FormatPropForTrace(string prop)
    {
        if (!prop.Contains('=')) return $"name=\"{prop}\"";
        var parts = prop.Split('=', 2);
        return $"name=\"{parts[0]}\" value=\"{(parts.Length > 1 ? parts[1] : "")}\"";
    }

    private static string InferPropertyGameContext(string propName)
    {
        var lower = propName.ToLower();
        if (lower.Contains("damage") || lower.Contains("attack") || lower.Contains("weapon")) return "Combat";
        if (lower.Contains("health") || lower.Contains("stamina") || lower.Contains("food") || lower.Contains("water")) return "Survival/Stats";
        if (lower.Contains("craft") || lower.Contains("recipe") || lower.Contains("ingredient")) return "Crafting";
        if (lower.Contains("loot") || lower.Contains("harvest") || lower.Contains("drop")) return "Loot/Harvesting";
        if (lower.Contains("speed") || lower.Contains("move") || lower.Contains("jump")) return "Movement";
        if (lower.Contains("sound") || lower.Contains("audio") || lower.Contains("noise")) return "Audio";
        if (lower.Contains("light") || lower.Contains("glow") || lower.Contains("emit")) return "Lighting/Visual";
        if (lower.Contains("unlock") || lower.Contains("require") || lower.Contains("perk") || lower.Contains("skill")) return "Progression";
        if (lower.Contains("price") || lower.Contains("value") || lower.Contains("economic")) return "Economy";
        if (lower.Contains("buff") || lower.Contains("effect") || lower.Contains("modifier")) return "Buffs/Effects";
        if (lower.Contains("spawn") || lower.Contains("probability") || lower.Contains("chance")) return "Spawning/RNG";
        if (lower.Contains("block") || lower.Contains("material") || lower.Contains("durability")) return "Blocks/Building";
        if (lower.Contains("vehicle") || lower.Contains("fuel")) return "Vehicles";
        if (lower.Contains("zombie") || lower.Contains("entity") || lower.Contains("ai")) return "Entities/AI";
        return "Game Property";
    }

    private static string InferDefinitionGameContext(string defType, string name)
    {
        var context = InferDefinitionTypeContext(defType);
        var nameLower = name.ToLower();
        
        // Add specifics based on name patterns
        if (nameLower.Contains("zombie") || nameLower.Contains("spider") || nameLower.Contains("wolf")) return "Enemies";
        if (nameLower.Contains("gun") || nameLower.Contains("pistol") || nameLower.Contains("rifle") || nameLower.Contains("shotgun")) return "Ranged Weapons";
        if (nameLower.Contains("axe") || nameLower.Contains("machete") || nameLower.Contains("club") || nameLower.Contains("knife")) return "Melee Weapons";
        if (nameLower.Contains("armor") || nameLower.Contains("helmet") || nameLower.Contains("chest") || nameLower.Contains("boots")) return "Armor";
        if (nameLower.Contains("food") || nameLower.Contains("water") || nameLower.Contains("drink") || nameLower.Contains("can")) return "Food/Drink";
        if (nameLower.Contains("medical") || nameLower.Contains("bandage") || nameLower.Contains("first") || nameLower.Contains("antibiotic")) return "Medical";
        if (nameLower.Contains("ammo") || nameLower.Contains("bullet") || nameLower.Contains("shell") || nameLower.Contains("arrow")) return "Ammunition";
        
        return context;
    }

    private static string InferDefinitionTypeContext(string defType)
    {
        return defType switch
        {
            "item" => "Items/Equipment",
            "block" => "Blocks/Building",
            "buff" => "Buffs/Status Effects",
            "recipe" => "Crafting Recipes",
            "entity_class" => "Entities (Zombies, Animals, NPCs)",
            "entity_group" => "Spawn Groups",
            "loot_group" => "Loot Tables",
            "loot_container" => "Loot Containers",
            "sound" => "Audio/Sound Effects",
            "vehicle" => "Vehicles",
            "quest" => "Quests/Missions",
            "perk" => "Perks/Skills",
            "skill" => "Player Skills",
            "game_event" => "Game Events/Triggers",
            "trader" => "Traders/Vending",
            _ => "Game Configuration"
        };
    }

    private static string InferGameContextFromClassName(string className)
    {
        var lower = className.ToLower();
        if (lower.Contains("inventory") || lower.Contains("bag") || lower.Contains("backpack")) return "Inventory System";
        if (lower.Contains("craft") || lower.Contains("recipe")) return "Crafting System";
        if (lower.Contains("trader") || lower.Contains("vending")) return "Trading System";
        if (lower.Contains("vehicle")) return "Vehicle System";
        if (lower.Contains("zombie") || lower.Contains("enemy") || lower.Contains("entity")) return "Entity/AI System";
        if (lower.Contains("item") && lower.Contains("action")) return "Item Actions";
        if (lower.Contains("xui") || lower.Contains("gui") || lower.Contains("hud")) return "User Interface";
        if (lower.Contains("buff") || lower.Contains("effect")) return "Buff/Effect System";
        if (lower.Contains("spawn") || lower.Contains("director")) return "Spawning System";
        if (lower.Contains("loot") || lower.Contains("container")) return "Loot System";
        if (lower.Contains("block")) return "Block System";
        if (lower.Contains("world") || lower.Contains("chunk")) return "World System";
        if (lower.Contains("player")) return "Player System";
        if (lower.Contains("audio") || lower.Contains("sound")) return "Audio System";
        if (lower.Contains("net") || lower.Contains("server") || lower.Contains("client")) return "Networking";
        return "Game Core";
    }

    private static string SerializeTrace(SemanticTrace trace)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{");
        sb.Append($"\"entity_type\":\"{EscapeJson(trace.EntityType)}\",");
        sb.Append($"\"entity_name\":\"{EscapeJson(trace.EntityName)}\",");
        sb.Append($"\"parent_context\":{(trace.ParentContext != null ? $"\"{EscapeJson(trace.ParentContext)}\"" : "null")},");
        sb.Append($"\"code_trace\":\"{EscapeJson(trace.CodeTrace)}\",");
        sb.Append($"\"usage_examples\":{(trace.UsageExamples != null ? $"\"{EscapeJson(trace.UsageExamples)}\"" : "null")},");
        sb.Append($"\"related_entities\":{(trace.RelatedEntities != null ? $"\"{EscapeJson(trace.RelatedEntities)}\"" : "null")},");
        sb.Append($"\"game_context\":{(trace.GameContext != null ? $"\"{EscapeJson(trace.GameContext)}\"" : "null")},");
        // Fields for LLM to fill in:
        sb.Append("\"layman_description\":null,");
        sb.Append("\"technical_description\":null,");
        sb.Append("\"player_impact\":null");
        sb.Append("}");
        return sb.ToString();
    }

    private static int ImportSemanticMappings(string inputPath)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  IMPORTING SEMANTIC MAPPINGS FROM LLM OUTPUT                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: File not found: {inputPath}");
            return 1;
        }

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Ensure table exists (might be old database)
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS semantic_mappings (
                id INTEGER PRIMARY KEY,
                entity_type TEXT NOT NULL,
                entity_name TEXT NOT NULL,
                parent_context TEXT,
                layman_description TEXT,
                technical_description TEXT,
                player_impact TEXT,
                related_systems TEXT,
                example_usage TEXT,
                generated_by TEXT DEFAULT 'llm',
                confidence REAL DEFAULT 0.8,
                llm_model TEXT,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(entity_type, entity_name, parent_context)
            )";
            cmd.ExecuteNonQuery();
        }

        var imported = 0;
        var skipped = 0;

        using var streamReader = new StreamReader(inputPath);
        string? line;
        while ((line = streamReader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                // Parse JSON line (simple parser for our known format)
                var mapping = ParseMappingJson(line);
                if (mapping == null || string.IsNullOrEmpty(mapping.Value.layman))
                {
                    skipped++;
                    continue;
                }

                using var cmd = db.CreateCommand();
                cmd.CommandText = @"INSERT OR REPLACE INTO semantic_mappings 
                    (entity_type, entity_name, parent_context, layman_description, technical_description, 
                     player_impact, generated_by, confidence, llm_model)
                    VALUES ($type, $name, $parent, $layman, $technical, $impact, 'llm', 0.8, $model)";
                cmd.Parameters.AddWithValue("$type", mapping.Value.type);
                cmd.Parameters.AddWithValue("$name", mapping.Value.name);
                cmd.Parameters.AddWithValue("$parent", mapping.Value.parent ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$layman", mapping.Value.layman);
                cmd.Parameters.AddWithValue("$technical", mapping.Value.technical ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$impact", mapping.Value.impact ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$model", mapping.Value.model ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
                imported++;
            }
            catch
            {
                skipped++;
            }
        }

        Console.WriteLine($"  Imported: {imported} mappings");
        Console.WriteLine($"  Skipped:  {skipped} (no description or parse error)");

        return 0;
    }

    private static (string type, string name, string? parent, string? layman, string? technical, string? impact, string? model)? 
        ParseMappingJson(string json)
    {
        // Simple JSON parsing for our known format
        string? GetValue(string key)
        {
            var pattern = $"\"{key}\":";
            var idx = json.IndexOf(pattern);
            if (idx < 0) return null;
            idx += pattern.Length;
            
            // Skip whitespace
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            
            if (idx >= json.Length) return null;
            if (json[idx] == 'n') return null; // null
            if (json[idx] != '"') return null;
            
            idx++; // skip opening quote
            var end = idx;
            while (end < json.Length && json[end] != '"')
            {
                if (json[end] == '\\') end++; // skip escaped char
                end++;
            }
            
            return json.Substring(idx, end - idx).Replace("\\n", "\n").Replace("\\\"", "\"");
        }

        var type = GetValue("entity_type");
        var name = GetValue("entity_name");
        if (type == null || name == null) return null;

        return (
            type, name,
            GetValue("parent_context"),
            GetValue("layman_description"),
            GetValue("technical_description"),
            GetValue("player_impact"),
            GetValue("llm_model")
        );
    }

    private static int ShowSemanticStatus()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SEMANTIC MAPPING STATUS                                         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Check if table exists
        using var checkCmd = db.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='semantic_mappings'";
        if (checkCmd.ExecuteScalar() == null)
        {
            Console.WriteLine("  No semantic_mappings table found.");
            Console.WriteLine("  Run 'export-semantic-traces' first to generate traces for LLM analysis.");
            return 0;
        }

        // Get counts
        using var countCmd = db.CreateCommand();
        countCmd.CommandText = @"
            SELECT entity_type, COUNT(*), 
                   SUM(CASE WHEN layman_description IS NOT NULL THEN 1 ELSE 0 END) as filled
            FROM semantic_mappings
            GROUP BY entity_type";

        Console.WriteLine("  Entity Type         Total    Filled   Coverage");
        Console.WriteLine("  ────────────────────────────────────────────────");

        using var reader = countCmd.ExecuteReader();
        var totalTotal = 0;
        var totalFilled = 0;
        while (reader.Read())
        {
            var type = reader.GetString(0);
            var total = reader.GetInt32(1);
            var filled = reader.GetInt32(2);
            var pct = total > 0 ? (filled * 100 / total) : 0;
            Console.WriteLine($"  {type,-20} {total,5}    {filled,5}   {pct,3}%");
            totalTotal += total;
            totalFilled += filled;
        }

        Console.WriteLine("  ────────────────────────────────────────────────");
        var totalPct = totalTotal > 0 ? (totalFilled * 100 / totalTotal) : 0;
        Console.WriteLine($"  {"TOTAL",-20} {totalTotal,5}    {totalFilled,5}   {totalPct,3}%");

        return 0;
    }

    private static int ShowModDetails(string modPattern)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  MOD DETAILS: {modPattern,-50} ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        // Find the mod
        using var modCmd = db.CreateCommand();
        modCmd.CommandText = @"SELECT id, name, has_xml, has_dll, xml_operations, csharp_dependencies, 
            display_name, description, author, version, website 
            FROM mods WHERE name LIKE $pattern";
        modCmd.Parameters.AddWithValue("$pattern", $"%{modPattern}%");

        using var modReader = modCmd.ExecuteReader();
        if (!modReader.Read())
        {
            Console.WriteLine($"  No mod found matching '{modPattern}'");
            return 1;
        }

        var modId = modReader.GetInt32(0);
        var modName = modReader.GetString(1);
        var hasXml = modReader.GetInt32(2) == 1;
        var hasDll = modReader.GetInt32(3) == 1;
        var xmlOps = modReader.GetInt32(4);
        var csharpDeps = modReader.GetInt32(5);
        var displayName = modReader.IsDBNull(6) ? null : modReader.GetString(6);
        var description = modReader.IsDBNull(7) ? null : modReader.GetString(7);
        var author = modReader.IsDBNull(8) ? null : modReader.GetString(8);
        var version = modReader.IsDBNull(9) ? null : modReader.GetString(9);
        modReader.Close();

        Console.WriteLine($"  Name:         {modName}");
        if (displayName != null) Console.WriteLine($"  Display Name: {displayName}");
        if (author != null) Console.WriteLine($"  Author:       {author}");
        if (version != null) Console.WriteLine($"  Version:      {version}");
        if (description != null) Console.WriteLine($"  Description:  {description}");
        Console.WriteLine($"  Type:         {(hasXml && hasDll ? "Hybrid" : hasXml ? "XML-Only" : hasDll ? "C#-Only" : "Assets")}");
        Console.WriteLine($"  XML Ops:      {xmlOps}");
        Console.WriteLine($"  C# Deps:      {csharpDeps}");

        // Show XML operations
        if (xmlOps > 0)
        {
            Console.WriteLine("\n  ═══ XML OPERATIONS ═══════════════════════════════════════════════");
            using var opCmd = db.CreateCommand();
            opCmd.CommandText = @"SELECT operation, xpath, target_type, target_name, property_name, new_value, element_content, file_path, line_number 
                FROM mod_xml_operations WHERE mod_id = $modId ORDER BY file_path, line_number";
            opCmd.Parameters.AddWithValue("$modId", modId);

            using var opReader = opCmd.ExecuteReader();
            while (opReader.Read())
            {
                var op = opReader.GetString(0);
                var xpath = opReader.GetString(1);
                var targetType = opReader.IsDBNull(2) ? "?" : opReader.GetString(2);
                var targetName = opReader.IsDBNull(3) ? "?" : opReader.GetString(3);
                var propName = opReader.IsDBNull(4) ? null : opReader.GetString(4);
                var newValue = opReader.IsDBNull(5) ? null : opReader.GetString(5);
                var content = opReader.IsDBNull(6) ? null : opReader.GetString(6);
                var file = opReader.IsDBNull(7) ? "" : opReader.GetString(7);
                var line = opReader.IsDBNull(8) ? 0 : opReader.GetInt32(8);

                Console.WriteLine($"\n  [{op.ToUpper()}] {targetType}/{targetName}");
                Console.WriteLine($"  XPath: {xpath}");
                if (propName != null) Console.WriteLine($"  Property: {propName} = {newValue}");
                if (content != null)
                {
                    Console.WriteLine($"  Content:");
                    foreach (var contentLine in content.Split('\n').Take(20))
                        Console.WriteLine($"    {contentLine.TrimEnd()}");
                    if (content.Split('\n').Length > 20)
                        Console.WriteLine($"    ... ({content.Split('\n').Length - 20} more lines)");
                }
                Console.WriteLine($"  Source: {file}:{line}");
            }
        }

        // Show C# dependencies
        if (csharpDeps > 0)
        {
            Console.WriteLine("\n  ═══ C# DEPENDENCIES ══════════════════════════════════════════════");
            using var depCmd = db.CreateCommand();
            depCmd.CommandText = @"SELECT dependency_type, dependency_name, source_file, line_number 
                FROM mod_csharp_deps WHERE mod_id = $modId ORDER BY dependency_type, dependency_name";
            depCmd.Parameters.AddWithValue("$modId", modId);

            using var depReader = depCmd.ExecuteReader();
            var lastType = "";
            while (depReader.Read())
            {
                var depType = depReader.GetString(0);
                var depName = depReader.GetString(1);
                var srcFile = depReader.IsDBNull(2) ? "" : depReader.GetString(2);
                var srcLine = depReader.IsDBNull(3) ? 0 : depReader.GetInt32(3);

                if (depType != lastType)
                {
                    Console.WriteLine($"\n  [{depType}]");
                    lastType = depType;
                }
                Console.WriteLine($"    {depName}");
                if (!string.IsNullOrEmpty(srcFile))
                    Console.WriteLine($"      @ {srcFile}:{srcLine}");
            }
        }

        return 0;
    }
}
