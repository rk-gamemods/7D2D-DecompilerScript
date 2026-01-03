using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Collects and aggregates data from the database for report generation.
/// </summary>
public static class ReportDataCollector
{
    public static ReportData GatherReportData(SqliteConnection db)
    {
        // Base game stats
        var (totalDefs, totalProps, totalRefs, defsByType) = GetBaseGameStats(db);
        
        // Mod stats
        var (totalMods, xmlMods, csharpMods, hybridMods, opsByType) = GetModStats(db);
        
        // Ecosystem stats
        var (active, modified, removed, depended, dangerZone) = GetEcosystemStats(db);
        
        // Mod summary
        var modSummary = GetModSummary(db);
        
        // Fun facts
        var (longestItem, mostRefItem, mostRefCount, mostComplex, mostComplexProps,
             mostConnected, mostConnectedRefs, mostDepended, mostDependedCount) = GetFunFacts(db);
        
        // Contested entities
        var contested = GetContestedEntities(db);
        
        // C# analysis
        var (csharpByType, harmonyPatches, classExtensions) = GetCSharpAnalysis(db);
        
        // Behavioral analysis
        var modBehaviors = GenerateModBehaviors(db);
        
        // New metrics
        var topInterconnected = GetTopInterconnected(db);
        var mostInvasiveMods = GetMostInvasiveMods(db);
        var propertyConflicts = GetPropertyConflicts(db);

        return new ReportData(
            totalDefs, totalProps, totalRefs, defsByType,
            totalMods, xmlMods, csharpMods, hybridMods, opsByType,
            csharpByType, harmonyPatches, classExtensions,
            active, modified, removed, depended, dangerZone, modSummary,
            longestItem, mostRefItem, mostRefCount, mostComplex, mostComplexProps,
            mostConnected, mostConnectedRefs, mostDepended, mostDependedCount,
            contested, modBehaviors,
            topInterconnected, mostInvasiveMods, propertyConflicts
        );
    }

    private static (int totalDefs, int totalProps, int totalRefs, Dictionary<string, int> defsByType) 
        GetBaseGameStats(SqliteConnection db)
    {
        int totalDefs = 0, totalProps = 0, totalRefs = 0;
        var defsByType = new Dictionary<string, int>();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_definitions";
            totalDefs = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_properties";
            totalProps = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_references";
            totalRefs = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT definition_type, COUNT(*) FROM xml_definitions GROUP BY definition_type ORDER BY COUNT(*) DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                defsByType[reader.GetString(0)] = reader.GetInt32(1);
        }

        return (totalDefs, totalProps, totalRefs, defsByType);
    }

    private static (int totalMods, int xmlMods, int csharpMods, int hybridMods, Dictionary<string, int> opsByType)
        GetModStats(SqliteConnection db)
    {
        int totalMods = 0, xmlMods = 0, csharpMods = 0, hybridMods = 0;
        var opsByType = new Dictionary<string, int>();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods";
            totalMods = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_xml = 1 AND has_dll = 0";
            xmlMods = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_dll = 1 AND has_xml = 0";
            csharpMods = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_xml = 1 AND has_dll = 1";
            hybridMods = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT operation, COUNT(*) FROM mod_xml_operations GROUP BY operation ORDER BY COUNT(*) DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                opsByType[reader.GetString(0)] = reader.GetInt32(1);
        }

        return (totalMods, xmlMods, csharpMods, hybridMods, opsByType);
    }

    private static (int active, int modified, int removed, int depended, List<(string, string, string, string)> dangerZone)
        GetEcosystemStats(SqliteConnection db)
    {
        int active = 0, modified = 0, removed = 0, depended = 0;
        var dangerZone = new List<(string, string, string, string)>();

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
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT entity_type, entity_name, removed_by, depended_on_by
                FROM ecosystem_entities WHERE status = 'removed' AND depended_on_by IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                dangerZone.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return (active, modified, removed, depended, dangerZone);
    }

    private static List<ModInfo> GetModSummary(SqliteConnection db)
    {
        var modSummary = new List<ModInfo>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT m.name, m.xml_operations, m.csharp_dependencies, m.conflicts, m.has_xml, m.has_dll,
            (SELECT COUNT(*) FROM mod_xml_operations WHERE mod_id = m.id AND operation = 'remove') as removes
            FROM mods m ORDER BY m.conflicts DESC, m.xml_operations DESC, m.csharp_dependencies DESC";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var ops = reader.GetInt32(1);
            var deps = reader.GetInt32(2);
            var conflicts = reader.GetInt32(3);
            var hasXml = reader.GetInt32(4) == 1;
            var hasDll = reader.GetInt32(5) == 1;
            var removes = reader.GetInt32(6);
            
            string modType = (hasXml, hasDll) switch
            {
                (true, true) => "Hybrid",
                (true, false) => "XML",
                (false, true) => "C# Code",
                _ => "Assets"
            };
            
            string health, healthNote;
            if (conflicts > 0)
            {
                health = "Broken";
                healthNote = "Removes game content needed by other mods";
            }
            else if (removes > 0)
            {
                health = "Healthy";
                healthNote = $"Intentionally removes {removes} game element(s)";
            }
            else if (ops > 0 || deps > 0)
            {
                health = "Healthy";
                healthNote = ops > 0 && deps > 0 ? "Modifies game via XML and code" 
                           : ops > 0 ? "Modifies game via XML" 
                           : "Modifies game via C# code patches";
            }
            else
            {
                health = "Healthy";
                healthNote = hasDll ? "C# code mod (no game dependencies detected)" 
                           : hasXml ? "XML mod (no changes detected)" 
                           : "Asset-only mod (textures, sounds, etc.)";
            }
            
            modSummary.Add(new ModInfo(name, ops, deps, removes, modType, health, healthNote));
        }

        return modSummary;
    }

    private static (string longestItem, string mostRefItem, int mostRefCount, string mostComplex, int mostComplexProps,
                    string mostConnected, int mostConnectedRefs, string mostDepended, int mostDependedCount)
        GetFunFacts(SqliteConnection db)
    {
        string longestItem = "", mostRefItem = "", mostComplex = "", mostConnected = "", mostDepended = "";
        int mostRefCount = 0, mostComplexProps = 0, mostConnectedRefs = 0, mostDependedCount = 0;

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM xml_definitions WHERE definition_type = 'item' ORDER BY LENGTH(name) DESC LIMIT 1";
            longestItem = cmd.ExecuteScalar()?.ToString() ?? "";
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_name, COUNT(*) as refs FROM xml_references 
                WHERE target_type = 'item' GROUP BY target_name ORDER BY refs DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) { mostRefItem = reader.GetString(0); mostRefCount = reader.GetInt32(1); }
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name, COUNT(*) as props FROM xml_properties 
                JOIN xml_definitions ON xml_properties.definition_id = xml_definitions.id
                GROUP BY xml_properties.definition_id ORDER BY props DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) { mostComplex = reader.GetString(0); mostComplexProps = reader.GetInt32(1); }
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT d.name, COUNT(DISTINCT r.target_name) as outgoing_refs
                FROM xml_definitions d JOIN xml_references r ON r.source_def_id = d.id
                GROUP BY d.id ORDER BY outgoing_refs DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) { mostConnected = reader.GetString(0); mostConnectedRefs = reader.GetInt32(1); }
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_name, COUNT(DISTINCT source_def_id) as incoming_refs
                FROM xml_references WHERE target_name IS NOT NULL
                GROUP BY target_type, target_name ORDER BY incoming_refs DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) { mostDepended = reader.GetString(0); mostDependedCount = reader.GetInt32(1); }
        }

        return (longestItem, mostRefItem, mostRefCount, mostComplex, mostComplexProps,
                mostConnected, mostConnectedRefs, mostDepended, mostDependedCount);
    }

    private static List<ContestedEntity> GetContestedEntities(SqliteConnection db)
    {
        var contested = new List<ContestedEntity>();
        var candidates = new List<(string type, string name, int count)>();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_type, target_name, COUNT(DISTINCT mod_id) as mod_count
                FROM mod_xml_operations WHERE target_name IS NOT NULL
                GROUP BY target_type, target_name HAVING mod_count > 1 ORDER BY mod_count DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                candidates.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        foreach (var (entityType, entityName, _) in candidates)
        {
            var modActions = new List<(string, string)>();
            using var detailCmd = db.CreateCommand();
            detailCmd.CommandText = @"SELECT m.name, mxo.operation FROM mod_xml_operations mxo 
                JOIN mods m ON mxo.mod_id = m.id
                WHERE mxo.target_type = $type AND mxo.target_name = $name ORDER BY mxo.operation";
            detailCmd.Parameters.AddWithValue("$type", entityType);
            detailCmd.Parameters.AddWithValue("$name", entityName);
            
            using var detailReader = detailCmd.ExecuteReader();
            while (detailReader.Read())
                modActions.Add((detailReader.GetString(0), detailReader.GetString(1)));

            var operations = modActions.Select(a => a.Item2.ToLower()).ToList();
            var hasRemove = operations.Any(o => o == "remove");
            var multipleWriters = operations.Count(o => o == "set" || o == "setattribute") > 1;

            string riskLevel, riskReason;
            if (hasRemove && modActions.Count > 1)
            {
                riskLevel = "High";
                riskReason = "One mod removes this while others modify it";
            }
            else if (multipleWriters)
            {
                riskLevel = "Medium";
                riskReason = "Multiple mods overwrite the same values (last one wins)";
            }
            else if (operations.All(o => o == "append" || o == "insertafter" || o == "insertbefore"))
            {
                riskLevel = "None";
                riskReason = "All mods just add content - fully compatible";
            }
            else
            {
                riskLevel = "Low";
                riskReason = "Operations appear compatible";
            }
            
            contested.Add(new ContestedEntity(entityType, entityName, modActions, riskLevel, riskReason));
        }

        return contested;
    }

    private static (Dictionary<string, int> csharpByType, 
                    List<(string, string, string, string)> harmonyPatches,
                    List<(string, string, string)> classExtensions)
        GetCSharpAnalysis(SqliteConnection db)
    {
        var csharpByType = new Dictionary<string, int>();
        var harmonyPatches = new List<(string, string, string, string)>();
        var classExtensions = new List<(string, string, string)>();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT dependency_type, COUNT(*) FROM mod_csharp_deps GROUP BY dependency_type ORDER BY COUNT(*) DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                csharpByType[reader.GetString(0)] = reader.GetInt32(1);
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT m.name, hc.dependency_name as class_name,
                    COALESCE(hm.dependency_name, '') as method_name,
                    CASE 
                        WHEN EXISTS(SELECT 1 FROM mod_csharp_deps hp WHERE hp.mod_id = m.id 
                            AND hp.dependency_type = 'harmony_prefix' AND hp.source_file = hc.source_file) THEN 'Prefix'
                        WHEN EXISTS(SELECT 1 FROM mod_csharp_deps hp WHERE hp.mod_id = m.id 
                            AND hp.dependency_type = 'harmony_postfix' AND hp.source_file = hc.source_file) THEN 'Postfix'
                        WHEN EXISTS(SELECT 1 FROM mod_csharp_deps hp WHERE hp.mod_id = m.id 
                            AND hp.dependency_type = 'harmony_transpiler' AND hp.source_file = hc.source_file) THEN 'Transpiler'
                        ELSE 'Patch'
                    END as patch_type
                FROM mods m
                JOIN mod_csharp_deps hc ON hc.mod_id = m.id AND hc.dependency_type = 'harmony_class'
                LEFT JOIN mod_csharp_deps hm ON hm.mod_id = m.id AND hm.dependency_type = 'harmony_method' 
                    AND hm.source_file = hc.source_file
                ORDER BY m.name, hc.dependency_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                harmonyPatches.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.name, mcd.dependency_type, mcd.dependency_name 
                FROM mod_csharp_deps mcd JOIN mods m ON mcd.mod_id = m.id
                WHERE mcd.dependency_type LIKE 'extends_%' OR mcd.dependency_type LIKE 'implements_%'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var depType = reader.GetString(1).Replace("extends_", "").Replace("implements_", "");
                classExtensions.Add((reader.GetString(0), depType, reader.GetString(2)));
            }
        }

        return (csharpByType, harmonyPatches, classExtensions);
    }

    private static List<InterconnectedEntity> GetTopInterconnected(SqliteConnection db)
    {
        var result = new List<InterconnectedEntity>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT d.definition_type, d.name,
                COALESCE(out_refs.cnt, 0) as outgoing_refs,
                COALESCE(in_refs.cnt, 0) as incoming_refs,
                0 as inheritance_depth
            FROM xml_definitions d
            LEFT JOIN (SELECT source_def_id, COUNT(*) as cnt FROM xml_references GROUP BY source_def_id) out_refs ON d.id = out_refs.source_def_id
            LEFT JOIN (SELECT target_type, target_name, COUNT(*) as cnt FROM xml_references GROUP BY target_type, target_name) in_refs 
                ON d.definition_type = in_refs.target_type AND d.name = in_refs.target_name
            ORDER BY (COALESCE(out_refs.cnt, 0) + COALESCE(in_refs.cnt, 0)) DESC";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var outRefs = reader.GetInt32(2);
            var inRefs = reader.GetInt32(3);
            result.Add(new InterconnectedEntity(reader.GetString(0), reader.GetString(1), outRefs, inRefs, 0, outRefs + inRefs));
        }
        return result;
    }

    private static List<InvasiveMod> GetMostInvasiveMods(SqliteConnection db)
    {
        var result = new List<InvasiveMod>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name,
                COALESCE(xml_stats.xml_count, 0) + COALESCE(csharp_stats.patch_count, 0) as total_changes,
                COALESCE(xml_stats.xml_count, 0) as xml_changes,
                COALESCE(csharp_stats.patch_count, 0) as harmony_patches,
                COALESCE(xml_stats.entity_count, 0) + COALESCE(csharp_stats.class_count, 0) as unique_targets
            FROM mods m
            LEFT JOIN (SELECT mod_id, COUNT(*) as xml_count, COUNT(DISTINCT target_type || '/' || target_name) as entity_count FROM mod_xml_operations GROUP BY mod_id) xml_stats ON m.id = xml_stats.mod_id
            LEFT JOIN (SELECT mod_id, COUNT(*) as patch_count, COUNT(DISTINCT dependency_name) as class_count FROM mod_csharp_deps WHERE dependency_type IN ('harmony_class', 'harmony_prefix', 'harmony_postfix', 'harmony_transpiler') GROUP BY mod_id) csharp_stats ON m.id = csharp_stats.mod_id
            ORDER BY total_changes DESC";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(new InvasiveMod(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        return result;
    }

    private static List<PropertyConflict> GetPropertyConflicts(SqliteConnection db)
    {
        var result = new List<PropertyConflict>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT mxo.target_type, mxo.target_name, mxo.property_name,
                GROUP_CONCAT(m.name || ':' || COALESCE(mxo.new_value, ''), '|') as setters
            FROM mod_xml_operations mxo JOIN mods m ON mxo.mod_id = m.id
            WHERE mxo.property_name IS NOT NULL AND mxo.property_name != ''
              AND mxo.operation IN ('set', 'setattribute')
            GROUP BY mxo.target_type, mxo.target_name, mxo.property_name
            HAVING COUNT(DISTINCT mxo.mod_id) > 1
            ORDER BY COUNT(DISTINCT mxo.mod_id) DESC";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var settersStr = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var setters = settersStr.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => { var parts = s.Split(':', 2); return (parts[0], parts.Length > 1 ? parts[1] : ""); })
                .ToList();
            
            result.Add(new PropertyConflict(
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                setters));
        }
        return result;
    }

    private static List<ModBehavior> GenerateModBehaviors(SqliteConnection db)
    {
        var behaviors = new List<ModBehavior>();
        
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT m.id, m.name, m.has_xml, m.has_dll,
            m.display_name, m.description, m.author, m.version, m.website
            FROM mods m ORDER BY m.name";
        
        using var reader = cmd.ExecuteReader();
        var mods = new List<(int id, string name, bool hasXml, bool hasDll, ModXmlInfo? xmlInfo)>();
        while (reader.Read())
        {
            ModXmlInfo? xmlInfo = null;
            if (!reader.IsDBNull(4) || !reader.IsDBNull(5) || !reader.IsDBNull(6) || !reader.IsDBNull(7) || !reader.IsDBNull(8))
            {
                xmlInfo = new ModXmlInfo(
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8));
            }
            mods.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) == 1, reader.GetInt32(3) == 1, xmlInfo));
        }
        reader.Close();

        foreach (var (modId, modName, hasXml, hasDll, xmlInfo) in mods)
        {
            var features = new List<string>();
            var systems = new HashSet<string>();
            var warnings = new List<string>();
            
            // Get XML operations for this mod
            using (var opCmd = db.CreateCommand())
            {
                opCmd.CommandText = @"SELECT operation, target_type, target_name, property_name 
                    FROM mod_xml_operations WHERE mod_id = @id";
                opCmd.Parameters.AddWithValue("@id", modId);
                using var opReader = opCmd.ExecuteReader();
                while (opReader.Read())
                {
                    var targetType = opReader.IsDBNull(1) ? "" : opReader.GetString(1);
                    if (!string.IsNullOrEmpty(targetType))
                        systems.Add(MapEntityTypeToSystem(targetType));
                }
            }

            // Get C# dependencies
            using (var depCmd = db.CreateCommand())
            {
                depCmd.CommandText = "SELECT dependency_type, dependency_name FROM mod_csharp_deps WHERE mod_id = @id";
                depCmd.Parameters.AddWithValue("@id", modId);
                using var depReader = depCmd.ExecuteReader();
                while (depReader.Read())
                {
                    var depType = depReader.GetString(0);
                    if (depType.StartsWith("harmony_"))
                        features.Add($"Patches {depReader.GetString(1)}");
                }
            }

            var oneLiner = features.Count > 0 ? features.First() : 
                           systems.Count > 0 ? $"Modifies {string.Join(", ", systems.Take(3))}" :
                           "Game modification";

            behaviors.Add(new ModBehavior(modName, oneLiner, features.Take(10).ToList(), systems.ToList(), warnings, xmlInfo));
        }

        return behaviors;
    }

    private static string MapEntityTypeToSystem(string entityType) => entityType switch
    {
        "item" => "Items",
        "block" => "Blocks",
        "buff" => "Buffs/Effects",
        "recipe" => "Crafting",
        "entity_class" => "Entities",
        "vehicle" => "Vehicles",
        "quest" => "Quests",
        "sound" => "Audio",
        "loot_group" or "loot_container" => "Loot",
        "skill" or "perk" => "Progression",
        _ => entityType
    };
}
