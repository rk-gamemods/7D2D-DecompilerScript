using Microsoft.Data.Sqlite;
using XmlIndexer.Analysis;
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
        
        // Dependency chain data (new)
        var (totalTransitiveRefs, inheritanceHotspots, sampleChains) = GetDependencyChainData(db);
        
        // Game code analysis
        var gameCodeSummary = GameCodeAnalyzer.GetSummary(db);

        return new ReportData(
            totalDefs, totalProps, totalRefs, defsByType,
            totalMods, xmlMods, csharpMods, hybridMods, opsByType,
            csharpByType, harmonyPatches, classExtensions,
            active, modified, removed, depended, dangerZone, modSummary,
            longestItem, mostRefItem, mostRefCount, mostComplex, mostComplexProps,
            mostConnected, mostConnectedRefs, mostDepended, mostDependedCount,
            contested, modBehaviors,
            topInterconnected, mostInvasiveMods, propertyConflicts,
            totalTransitiveRefs, inheritanceHotspots, sampleChains,
            gameCodeSummary.BugCount, gameCodeSummary.WarningCount,
            gameCodeSummary.InfoCount, gameCodeSummary.OpportunityCount
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
        
        // Get operation details per mod for better health notes
        var modOpDetails = new Dictionary<string, (int sets, int appends, int removes, int entities)>();
        using (var detailCmd = db.CreateCommand())
        {
            detailCmd.CommandText = @"
                SELECT m.name,
                    SUM(CASE WHEN o.operation IN ('set', 'setattribute') THEN 1 ELSE 0 END) as sets,
                    SUM(CASE WHEN o.operation IN ('append', 'insertBefore', 'insertAfter') THEN 1 ELSE 0 END) as appends,
                    SUM(CASE WHEN o.operation = 'remove' THEN 1 ELSE 0 END) as removes,
                    COUNT(DISTINCT o.target_name) as entities
                FROM mods m
                LEFT JOIN mod_xml_operations o ON o.mod_id = m.id
                GROUP BY m.id, m.name";
            using var r = detailCmd.ExecuteReader();
            while (r.Read())
            {
                var modName = r.GetString(0);
                modOpDetails[modName] = (
                    r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    r.IsDBNull(4) ? 0 : r.GetInt32(4)
                );
            }
        }
        
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT m.name, m.folder_name, m.load_order, m.xml_operations, m.csharp_dependencies, 
            m.conflicts, m.has_xml, m.has_dll,
            (SELECT COUNT(*) FROM mod_xml_operations WHERE mod_id = m.id AND operation = 'remove') as removes
            FROM mods m ORDER BY m.load_order ASC";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var folderName = reader.IsDBNull(1) ? name : reader.GetString(1);
            var loadOrder = reader.GetInt32(2);
            var ops = reader.GetInt32(3);
            var deps = reader.GetInt32(4);
            var conflicts = reader.GetInt32(5);
            var hasXml = reader.GetInt32(6) == 1;
            var hasDll = reader.GetInt32(7) == 1;
            var removes = reader.GetInt32(8);
            
            var details = modOpDetails.TryGetValue(name, out var d) ? d : (sets: 0, appends: 0, removes: 0, entities: 0);
            
            string modType = (hasXml, hasDll) switch
            {
                (true, true) => "Hybrid",
                (true, false) => "XML",
                (false, true) => "C#",
                _ => "Config" // No XML operations detected, likely just ModInfo.xml or asset folder
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
                healthNote = $"Removes {removes} game element(s)";
            }
            else if (ops > 0 && deps > 0)
            {
                health = "Healthy";
                healthNote = $"Hybrid: {ops} XML ops on {d.entities} entities, {deps} C# patches";
            }
            else if (ops > 0)
            {
                health = "Healthy";
                var parts = new List<string>();
                if (d.sets > 0) parts.Add($"{d.sets} sets");
                if (d.appends > 0) parts.Add($"{d.appends} appends");
                healthNote = parts.Count > 0 
                    ? $"XML: {string.Join(", ", parts)} on {d.entities} entities"
                    : $"XML: {ops} operations on {d.entities} entities";
            }
            else if (deps > 0)
            {
                health = "Healthy";
                healthNote = $"C#: {deps} game code patches";
            }
            else
            {
                health = "Healthy";
                healthNote = hasDll ? "C# mod (no detected game patches)" 
                           : hasXml ? "Has Config/ but no operations parsed" 
                           : "Config only (no XML/DLL detected)";
            }
            
            modSummary.Add(new ModInfo(name, loadOrder, ops, deps, removes, modType, health, healthNote, folderName));
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

        // Step 1: Find entities where one mod REMOVES and others still modify (always a conflict)
        var removeConflicts = new List<(string type, string name)>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT mxo1.target_type, mxo1.target_name
                FROM mod_xml_operations mxo1
                WHERE mxo1.operation = 'remove'
                  AND mxo1.target_name IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM mod_xml_operations mxo2
                    WHERE mxo2.target_type = mxo1.target_type
                      AND mxo2.target_name = mxo1.target_name
                      AND mxo2.mod_id != mxo1.mod_id
                  )";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                removeConflicts.Add((reader.GetString(0), reader.GetString(1)));
        }

        // Step 2: Find REAL property conflicts - multiple mods setting the SAME property/xpath
        // But exclude cases where all mods set the SAME VALUE (just redundant, not a conflict)
        var propertyConflicts = new Dictionary<(string type, string name), List<(string propKey, int modCount, bool sameValue)>>();
        using (var cmd = db.CreateCommand())
        {
            // Group by entity + property/xpath - check if values differ
            cmd.CommandText = @"
                SELECT target_type, target_name, 
                       COALESCE(property_name, xpath, operation) as prop_key,
                       COUNT(DISTINCT mod_id) as mod_count,
                       COUNT(DISTINCT COALESCE(new_value, '')) as distinct_values
                FROM mod_xml_operations 
                WHERE target_name IS NOT NULL
                  AND operation IN ('set', 'setattribute')
                GROUP BY target_type, target_name, prop_key
                HAVING mod_count > 1
                ORDER BY mod_count DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!propertyConflicts.ContainsKey(key))
                    propertyConflicts[key] = new List<(string, int, bool)>();
                
                var distinctValues = reader.GetInt32(4);
                var sameValue = distinctValues <= 1; // All mods set same value (or null)
                propertyConflicts[key].Add((reader.GetString(2), reader.GetInt32(3), sameValue));
            }
        }

        // Combine the conflict sources
        var allConflictEntities = new HashSet<(string type, string name)>(removeConflicts);
        foreach (var key in propertyConflicts.Keys)
            allConflictEntities.Add(key);

        foreach (var (entityType, entityName) in allConflictEntities)
        {
            var modActions = new List<ConflictModAction>();
            using var detailCmd = db.CreateCommand();
            detailCmd.CommandText = @"SELECT m.name, mxo.operation, mxo.xpath, mxo.property_name,
                mxo.new_value, mxo.element_content, mxo.file_path, mxo.line_number
                FROM mod_xml_operations mxo
                JOIN mods m ON mxo.mod_id = m.id
                WHERE mxo.target_type = $type AND mxo.target_name = $name ORDER BY mxo.operation";
            detailCmd.Parameters.AddWithValue("$type", entityType);
            detailCmd.Parameters.AddWithValue("$name", entityName);

            using var detailReader = detailCmd.ExecuteReader();
            while (detailReader.Read())
            {
                modActions.Add(new ConflictModAction(
                    detailReader.GetString(0),  // ModName
                    detailReader.GetString(1),  // Operation
                    detailReader.IsDBNull(2) ? null : detailReader.GetString(2),   // XPath
                    detailReader.IsDBNull(3) ? null : detailReader.GetString(3),   // PropertyName
                    detailReader.IsDBNull(4) ? null : detailReader.GetString(4),   // NewValue
                    detailReader.IsDBNull(5) ? null : detailReader.GetString(5),   // ElementContent
                    detailReader.IsDBNull(6) ? null : detailReader.GetString(6),   // FilePath
                    detailReader.IsDBNull(7) ? null : detailReader.GetInt32(7)     // LineNumber
                ));
            }

            // Determine risk level based on actual conflict type
            var hasRemove = modActions.Any(a => a.Operation.ToLower() == "remove");
            var conflictingProps = propertyConflicts.GetValueOrDefault((entityType, entityName));

            string riskLevel, riskReason;
            if (hasRemove && modActions.Count > 1)
            {
                riskLevel = "High";
                riskReason = "One mod removes this entity while others modify it";
            }
            else if (conflictingProps != null && conflictingProps.Count > 0)
            {
                // Check if ALL conflicting properties have the same value across mods
                var realConflicts = conflictingProps.Where(p => !p.sameValue).ToList();
                var redundantOnly = conflictingProps.Where(p => p.sameValue).ToList();

                if (realConflicts.Count > 0)
                {
                    var propList = string.Join(", ", realConflicts.Take(3).Select(p => p.propKey));
                    riskLevel = "Medium";
                    riskReason = $"Multiple mods set different values for: {propList}";
                }
                else if (redundantOnly.Count > 0)
                {
                    // All mods set the same value - just redundant, skip entirely
                    // Don't add to contested list - it's not a real conflict
                    continue;
                }
                else
                {
                    riskLevel = "Low";
                    riskReason = "Operations may conflict";
                }
            }
            else
            {
                riskLevel = "Low";
                riskReason = "Operations may conflict";
            }

            // Only include actions that are part of actual conflicts
            if (hasRemove)
            {
                // For remove conflicts, show all actions
                contested.Add(new ContestedEntity(entityType, entityName, modActions, riskLevel, riskReason));
            }
            else if (conflictingProps != null)
            {
                // For property conflicts, filter to only show conflicting properties (not redundant ones)
                var realConflictKeys = conflictingProps.Where(p => !p.sameValue).Select(p => p.propKey).ToHashSet();
                var filteredActions = modActions.Where(a =>
                {
                    var key = a.PropertyName ?? a.XPath ?? a.Operation;
                    return realConflictKeys.Contains(key);
                }).ToList();

                if (filteredActions.Count > 0)
                    contested.Add(new ContestedEntity(entityType, entityName, filteredActions, riskLevel, riskReason));
            }
        }

        return contested.OrderByDescending(c => c.RiskLevel == "High" ? 2 : c.RiskLevel == "Medium" ? 1 : 0).ToList();
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

        // Harmony patches are stored as 'ClassName.MethodName' in dependency_name 
        // with dependency_type of harmony_prefix, harmony_postfix, or harmony_transpiler
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT m.name, 
                    CASE WHEN INSTR(mcd.dependency_name, '.') > 0 
                        THEN SUBSTR(mcd.dependency_name, 1, INSTR(mcd.dependency_name, '.') - 1)
                        ELSE mcd.dependency_name 
                    END as class_name,
                    CASE WHEN INSTR(mcd.dependency_name, '.') > 0 
                        THEN SUBSTR(mcd.dependency_name, INSTR(mcd.dependency_name, '.') + 1)
                        ELSE '' 
                    END as method_name,
                    CASE mcd.dependency_type
                        WHEN 'harmony_prefix' THEN 'Prefix'
                        WHEN 'harmony_postfix' THEN 'Postfix'
                        WHEN 'harmony_transpiler' THEN 'Transpiler'
                        ELSE 'Patch'
                    END as patch_type
                FROM mods m
                JOIN mod_csharp_deps mcd ON mcd.mod_id = m.id 
                WHERE mcd.dependency_type IN ('harmony_prefix', 'harmony_postfix', 'harmony_transpiler')
                ORDER BY m.name, class_name, method_name";
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
        // Only return properties where mods set DIFFERENT values (real conflicts)
        // Exclude same-value redundancies
        // Include entries even if target_name looks like a selector (fragile xpath)
        cmd.CommandText = @"
            SELECT mxo.target_type, mxo.target_name, mxo.property_name,
                GROUP_CONCAT(m.name || ':' || COALESCE(mxo.new_value, ''), '|') as setters
            FROM mod_xml_operations mxo JOIN mods m ON mxo.mod_id = m.id
            WHERE mxo.property_name IS NOT NULL AND mxo.property_name != ''
              AND mxo.target_name IS NOT NULL AND mxo.target_name != ''
              AND mxo.operation IN ('set', 'setattribute')
            GROUP BY mxo.target_type, mxo.target_name, mxo.property_name
            HAVING COUNT(DISTINCT mxo.mod_id) > 1 AND COUNT(DISTINCT COALESCE(mxo.new_value, '')) > 1
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

    /// <summary>
    /// Get dependency chain data from transitive_references table (if populated).
    /// </summary>
    private static (int totalTransitiveRefs, List<InheritanceHotspot> hotspots, List<EntityDependencyInfo> sampleChains) 
        GetDependencyChainData(SqliteConnection db)
    {
        int totalTransitiveRefs = 0;
        var hotspots = new List<InheritanceHotspot>();
        var sampleChains = new List<EntityDependencyInfo>();

        // Check if transitive_references table exists
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='transitive_references'";
            var exists = cmd.ExecuteScalar();
            if (exists == null)
                return (0, hotspots, sampleChains);
        }

        // Get total transitive reference count
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM transitive_references";
            totalTransitiveRefs = Convert.ToInt32(cmd.ExecuteScalar());
        }

        if (totalTransitiveRefs == 0)
            return (0, hotspots, sampleChains);

        // Get inheritance hotspots (most depended-upon entities)
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT 
                    d.definition_type, 
                    d.name,
                    COUNT(DISTINCT tr.source_def_id) as dependent_count,
                    GROUP_CONCAT(DISTINCT d_dep.name) as dependents
                FROM transitive_references tr
                JOIN xml_definitions d ON tr.target_def_id = d.id
                JOIN xml_definitions d_dep ON tr.source_def_id = d_dep.id
                GROUP BY tr.target_def_id
                ORDER BY dependent_count DESC
                LIMIT 25";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dependentCount = reader.GetInt32(2);
                var riskLevel = dependentCount switch
                {
                    > 100 => "CRITICAL",
                    > 50 => "HIGH",
                    > 20 => "MEDIUM",
                    _ => "LOW"
                };
                var dependents = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var topDependents = dependents.Split(',').Take(5).ToList();
                
                hotspots.Add(new InheritanceHotspot(
                    reader.GetString(0),
                    reader.GetString(1),
                    dependentCount,
                    riskLevel,
                    topDependents
                ));
            }
        }

        // Get sample dependency chains - include ALL entities with any dependencies/dependents
        // First priority: entities with most dependents (hotspots already covers this, but we add chains)
        // Second priority: entities with deep dependency chains
        // Third priority: entities that just extend something
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT
                    d.definition_type,
                    d.name,
                    d.file_path,
                    d.extends as extends_from,
                    COALESCE(
                        (SELECT COUNT(*) FROM transitive_references tr WHERE tr.source_def_id = d.id),
                        0
                    ) as deps_count,
                    COALESCE(
                        (SELECT COUNT(*) FROM transitive_references tr WHERE tr.target_def_id = d.id),
                        0
                    ) as dependent_count,
                    COALESCE(
                        (SELECT MAX(tr.path_depth) FROM transitive_references tr WHERE tr.source_def_id = d.id),
                        0
                    ) as max_depth
                FROM xml_definitions d
                WHERE EXISTS (
                    SELECT 1 FROM transitive_references tr 
                    WHERE tr.source_def_id = d.id OR tr.target_def_id = d.id
                )
                ORDER BY 
                    dependent_count DESC,
                    max_depth DESC,
                    deps_count DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var entityType = reader.GetString(0);
                var entityName = reader.GetString(1);
                var filePath = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var extendsFrom = reader.IsDBNull(3) ? null : reader.GetString(3);

                // Get what this entity depends on (with full path chain)
                var dependsOn = new List<DependencyChainEntry>();
                using (var depCmd = db.CreateCommand())
                {
                    depCmd.CommandText = @"
                        SELECT d.definition_type, d.name, tr.path_depth, tr.reference_types, d.file_path, tr.path_json
                        FROM transitive_references tr
                        JOIN xml_definitions d ON tr.target_def_id = d.id
                        WHERE tr.source_def_id = (SELECT id FROM xml_definitions WHERE name = @name AND definition_type = @type)
                        ORDER BY tr.path_depth
                        LIMIT 20";
                    depCmd.Parameters.AddWithValue("@name", entityName);
                    depCmd.Parameters.AddWithValue("@type", entityType);
                    using var depReader = depCmd.ExecuteReader();
                    while (depReader.Read())
                    {
                        dependsOn.Add(new DependencyChainEntry(
                            depReader.GetString(0),
                            depReader.GetString(1),
                            depReader.GetInt32(2),
                            depReader.IsDBNull(3) ? "" : depReader.GetString(3),
                            depReader.IsDBNull(4) ? "" : depReader.GetString(4),
                            depReader.IsDBNull(5) ? "[]" : depReader.GetString(5)
                        ));
                    }
                }

                // Get what depends on this entity (with full path chain)
                var dependedOnBy = new List<DependencyChainEntry>();
                using (var depByCmd = db.CreateCommand())
                {
                    depByCmd.CommandText = @"
                        SELECT d.definition_type, d.name, tr.path_depth, tr.reference_types, d.file_path, tr.path_json
                        FROM transitive_references tr
                        JOIN xml_definitions d ON tr.source_def_id = d.id
                        WHERE tr.target_def_id = (SELECT id FROM xml_definitions WHERE name = @name AND definition_type = @type)
                        ORDER BY tr.path_depth
                        LIMIT 20";
                    depByCmd.Parameters.AddWithValue("@name", entityName);
                    depByCmd.Parameters.AddWithValue("@type", entityType);
                    using var depByReader = depByCmd.ExecuteReader();
                    while (depByReader.Read())
                    {
                        dependedOnBy.Add(new DependencyChainEntry(
                            depByReader.GetString(0),
                            depByReader.GetString(1),
                            depByReader.GetInt32(2),
                            depByReader.IsDBNull(3) ? "" : depByReader.GetString(3),
                            depByReader.IsDBNull(4) ? "" : depByReader.GetString(4),
                            depByReader.IsDBNull(5) ? "[]" : depByReader.GetString(5)
                        ));
                    }
                }

                if (dependsOn.Count > 0 || dependedOnBy.Count > 0)
                {
                    sampleChains.Add(new EntityDependencyInfo(
                        entityType,
                        entityName,
                        filePath,
                        extendsFrom,
                        dependsOn,
                        dependedOnBy
                    ));
                }
            }
        }

        return (totalTransitiveRefs, hotspots, sampleChains);
    }

    // =========================================================================
    // Extended Data Collection (for multi-page HTML reports)
    // =========================================================================

    /// <summary>
    /// Gather extended data for multi-page reports.
    /// This includes full entity exports, references, and mod details.
    /// </summary>
    public static ExtendedReportData GatherExtendedData(SqliteConnection db)
    {
        var allEntities = ExportAllEntities(db);
        var allReferences = ExportAllReferences(db);
        var allTransitiveRefs = ExportAllTransitiveRefs(db);
        var modDetails = GetAllModDetails(db);
        var callGraphNodes = GetCallGraphNodes(db);
        var eventFlowData = GetEventFlowData(db);
        var referenceTypeCounts = GetReferenceTypeBreakdown(db);

        return new ExtendedReportData(
            allEntities,
            allReferences,
            allTransitiveRefs,
            modDetails,
            callGraphNodes,
            eventFlowData,
            referenceTypeCounts
        );
    }

    /// <summary>Export all entities with their properties for the entity page.</summary>
    private static List<EntityExport> ExportAllEntities(SqliteConnection db)
    {
        var entities = new List<EntityExport>();
        var entityProps = new Dictionary<long, List<PropertyExport>>();

        // First get all properties grouped by definition
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT definition_id, property_name, property_value, property_class
                FROM xml_properties ORDER BY definition_id, line_number";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var defId = reader.GetInt64(0);
                if (!entityProps.ContainsKey(defId))
                    entityProps[defId] = new List<PropertyExport>();
                entityProps[defId].Add(new PropertyExport(
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)
                ));
            }
        }

        // Get reference counts per entity
        var refCounts = new Dictionary<long, int>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT source_def_id, COUNT(*) FROM xml_references
                WHERE source_def_id IS NOT NULL GROUP BY source_def_id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                refCounts[reader.GetInt64(0)] = reader.GetInt32(1);
        }

        // Get all definitions
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, definition_type, name, file_path, line_number, extends
                FROM xml_definitions ORDER BY definition_type, name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                entityProps.TryGetValue(id, out var props);
                refCounts.TryGetValue(id, out var refCount);

                entities.Add(new EntityExport(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    props?.Count ?? 0,
                    refCount,
                    props
                ));
            }
        }

        return entities;
    }

    /// <summary>Export all references for the entity page.</summary>
    private static List<ReferenceExport> ExportAllReferences(SqliteConnection db)
    {
        var refs = new List<ReferenceExport>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT
                d.definition_type, d.name,
                r.target_type, r.target_name, r.reference_context
            FROM xml_references r
            JOIN xml_definitions d ON r.source_def_id = d.id
            WHERE r.target_name IS NOT NULL";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            refs.Add(new ReferenceExport(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4)
            ));
        }
        return refs;
    }

    /// <summary>Export all transitive references for the dependency page.</summary>
    private static List<TransitiveExport> ExportAllTransitiveRefs(SqliteConnection db)
    {
        var refs = new List<TransitiveExport>();

        // Check if table exists
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='transitive_references'";
            if (cmd.ExecuteScalar() == null)
                return refs;
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT
                    ds.definition_type, ds.name,
                    dt.definition_type, dt.name,
                    tr.path_depth, tr.reference_types
                FROM transitive_references tr
                JOIN xml_definitions ds ON tr.source_def_id = ds.id
                JOIN xml_definitions dt ON tr.target_def_id = dt.id
                LIMIT 10000";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                refs.Add(new TransitiveExport(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? "" : reader.GetString(5)
                ));
            }
        }
        return refs;
    }

    /// <summary>Get detailed mod information for the mods page.</summary>
    private static List<ModDetailExport> GetAllModDetails(SqliteConnection db)
    {
        var details = new List<ModDetailExport>();
        var modIds = new Dictionary<int, string>();

        // Get all mods
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM mods";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                modIds[reader.GetInt32(0)] = reader.GetString(1);
        }

        foreach (var (modId, modName) in modIds)
        {
            var xmlOps = new List<XmlOperationExport>();
            var patches = new List<HarmonyPatchExport>();
            var extensions = new List<ClassExtensionExport>();

            // Get XML operations
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT operation, target_type, target_name, property_name, xpath, element_content
                    FROM mod_xml_operations WHERE mod_id = @id ORDER BY id LIMIT 100";
                cmd.Parameters.AddWithValue("@id", modId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    xmlOps.Add(new XmlOperationExport(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5)
                    ));
                }
            }

            // Get Harmony patches
            // The new scanner stores harmony patches with the full target in dependency_name
            // e.g., harmony_prefix with dependency_name = "XUiC_LootContainer.OnOpen"
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT
                        dependency_name as target,
                        CASE dependency_type
                            WHEN 'harmony_prefix' THEN 'Prefix'
                            WHEN 'harmony_postfix' THEN 'Postfix'
                            WHEN 'harmony_transpiler' THEN 'Transpiler'
                            ELSE 'Patch'
                        END as patch_type,
                        code_snippet
                    FROM mod_csharp_deps
                    WHERE mod_id = @id
                    AND dependency_type IN ('harmony_prefix', 'harmony_postfix', 'harmony_transpiler', 'harmony_patch')
                    ORDER BY dependency_name";
                cmd.Parameters.AddWithValue("@id", modId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var target = reader.GetString(0);
                    var patchType = reader.GetString(1);
                    var codeSnippet = reader.IsDBNull(2) ? null : reader.GetString(2);

                    // Split target into class and method (format: "ClassName.MethodName" or just "ClassName")
                    var lastDot = target.LastIndexOf('.');
                    string className, methodName;
                    if (lastDot > 0)
                    {
                        className = target.Substring(0, lastDot);
                        methodName = target.Substring(lastDot + 1);
                    }
                    else
                    {
                        className = target;
                        methodName = "";
                    }

                    patches.Add(new HarmonyPatchExport(className, methodName, patchType, codeSnippet));
                }
            }

            // Get class extensions
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT dependency_type, dependency_name
                    FROM mod_csharp_deps
                    WHERE mod_id = @id AND (dependency_type LIKE 'extends_%' OR dependency_type LIKE 'implements_%')";
                cmd.Parameters.AddWithValue("@id", modId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var depType = reader.GetString(0).Replace("extends_", "").Replace("implements_", "");
                    extensions.Add(new ClassExtensionExport(depType, reader.GetString(1)));
                }
            }

            // Get C# entity dependencies (items, blocks, buffs, sounds, etc. referenced in code)
            var entityDeps = new List<CSharpEntityDependency>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT dependency_type, dependency_name, source_file, pattern
                    FROM mod_csharp_deps
                    WHERE mod_id = @id 
                    AND dependency_type IN ('item', 'block', 'entity_class', 'buff', 'recipe', 'sound', 'quest', 'localization')
                    ORDER BY dependency_type, dependency_name";
                cmd.Parameters.AddWithValue("@id", modId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    entityDeps.Add(new CSharpEntityDependency(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3)
                    ));
                }
            }

            // Get entity type breakdown
            var entityTypes = new Dictionary<string, int>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT target_type, COUNT(*) 
                    FROM mod_xml_operations 
                    WHERE mod_id = @id AND target_type IS NOT NULL
                    GROUP BY target_type ORDER BY COUNT(*) DESC";
                cmd.Parameters.AddWithValue("@id", modId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    entityTypes[reader.GetString(0)] = reader.GetInt32(1);
            }
            
            // Get top targeted entities
            var topEntities = new List<string>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT target_type || ':' || target_name, COUNT(*) 
                    FROM mod_xml_operations 
                    WHERE mod_id = @id AND target_name IS NOT NULL
                    GROUP BY target_type, target_name 
                    ORDER BY COUNT(*) DESC LIMIT 10";
                cmd.Parameters.AddWithValue("@id", modId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    topEntities.Add(reader.GetString(0));
            }

            details.Add(new ModDetailExport(modName, xmlOps, patches, extensions,
                entityDeps.Count > 0 ? entityDeps : null,
                entityTypes.Count > 0 ? entityTypes : null,
                topEntities.Count > 0 ? topEntities : null));
        }

        return details;
    }

    /// <summary>Get call graph nodes if available.</summary>
    private static List<CallGraphNode> GetCallGraphNodes(SqliteConnection db)
    {
        var nodes = new List<CallGraphNode>();
        // Call graph data would come from the CallGraphExtractor tool
        // This is a placeholder - actual implementation would query that data
        return nodes;
    }

    /// <summary>Get event flow data if available.</summary>
    private static List<EventFlowEdge> GetEventFlowData(SqliteConnection db)
    {
        var events = new List<EventFlowEdge>();
        // Event flow data would come from the CallGraphExtractor tool
        // This is a placeholder - actual implementation would query that data
        return events;
    }

    /// <summary>Get reference type breakdown.</summary>
    private static Dictionary<string, int> GetReferenceTypeBreakdown(SqliteConnection db)
    {
        var counts = new Dictionary<string, int>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT reference_context, COUNT(*)
            FROM xml_references
            WHERE reference_context IS NOT NULL AND reference_context != ''
            GROUP BY reference_context
            ORDER BY COUNT(*) DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }
}
