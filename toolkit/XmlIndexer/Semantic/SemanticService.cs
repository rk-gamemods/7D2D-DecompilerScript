using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Semantic;

/// <summary>
/// Handles semantic trace export/import for LLM analysis.
/// Collects ALL entities from the database and formats them for LLM processing.
/// </summary>
public class SemanticService
{
    private readonly string _dbPath;

    public SemanticService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public int ExportSemanticTraces(string outputPath)
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

    public int ImportSemanticMappings(string inputPath)
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

    public int ShowSemanticStatus()
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

    // =========================================================================
    // COMPREHENSIVE TRACE COLLECTORS - Captures ALL entities from database
    // =========================================================================

    private List<SemanticTrace> CollectAllPropertyNames(SqliteConnection db)
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

    private List<SemanticTrace> CollectAllDefinitions(SqliteConnection db)
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

    private List<SemanticTrace> CollectAllCrossReferences(SqliteConnection db)
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

    private List<SemanticTrace> CollectAllDefinitionTypes(SqliteConnection db)
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

    private List<SemanticTrace> CollectCSharpClassTraces(SqliteConnection db)
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

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

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

    private string SerializeTrace(SemanticTrace trace)
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

    private static string EscapeJson(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");

    private (string type, string name, string? parent, string? layman, string? technical, string? impact, string? model)? 
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
}
