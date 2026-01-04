using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace XmlIndexer.Analysis;

/// <summary>
/// Detects indirect conflicts between mods by analyzing transitive relationships.
/// 
/// FRAMING (important for UI/tooltips):
/// "Most overlaps are informational, not problems. 
///  Severity indicates how much attention something deserves, not that something is wrong."
/// 
/// This class identifies when two mods touch different entities that are related
/// through inheritance/buff chains, which the simpler XPath-based conflict detection misses.
/// 
/// Example: ModA modifies buffCoffeeBuzz, ModB modifies drinkJarCoffee.
/// They don't touch the same XPath, but drinkJarCoffee triggers buffCoffeeBuzz.
/// If ModB removes drinkJarCoffee, ModA's buff changes become unreachable.
/// </summary>
public class IndirectConflictDetector
{
    private readonly string _dbPath;

    public IndirectConflictDetector(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Entry point: Clears existing indirect conflicts and rebuilds using transitive reference data.
    /// Must be called AFTER TransitiveReferenceBuilder has populated transitive_references table.
    /// </summary>
    public IndirectConflictResult DetectIndirectConflicts()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        var result = new IndirectConflictResult { StartedAt = DateTime.UtcNow };

        // Ensure table exists (migration)
        EnsureTableExists(db);

        // Clear existing data
        ClearIndirectConflicts(db);

        // Run detection patterns
        var conflicts = new List<IndirectConflict>();

        // HIGH severity patterns (H1-H10)
        conflicts.AddRange(DetectDeletedEntityReferenced(db));      // H1
        conflicts.AddRange(DetectBrokenInheritanceChain(db));       // H2
        conflicts.AddRange(DetectConflictingBaseValues(db));        // H3
        conflicts.AddRange(DetectOrphanedBuffApplication(db));      // H4
        conflicts.AddRange(DetectRecipeOutputConflict(db));         // H5
        conflicts.AddRange(DetectLootTableBreakage(db));            // H6
        conflicts.AddRange(DetectEntityClassConflict(db));          // H7

        // MEDIUM severity patterns (M1-M8)
        conflicts.AddRange(DetectTransitivePropertyOverride(db));   // M1
        conflicts.AddRange(DetectBuffChainInterference(db));        // M2
        conflicts.AddRange(DetectSharedInheritanceModification(db)); // M3
        conflicts.AddRange(DetectRecipeIngredientChange(db));       // M4

        // LOW severity patterns (L1-L6) - informational
        conflicts.AddRange(DetectAdditiveStacking(db));             // L1
        conflicts.AddRange(DetectModsEnhancingSameEntity(db));      // L2
        conflicts.AddRange(DetectRelatedGroupModifications(db));    // L3

        result.ConflictsDetected = conflicts.Count;
        result.HighCount = conflicts.Count(c => c.Severity == "high");
        result.MediumCount = conflicts.Count(c => c.Severity == "medium");
        result.LowCount = conflicts.Count(c => c.Severity == "low");

        // Bulk insert
        BulkInsertConflicts(db, conflicts);

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    private void ClearIndirectConflicts(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM mod_indirect_conflicts";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates the mod_indirect_conflicts table if it doesn't exist (migration support).
    /// </summary>
    private void EnsureTableExists(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS mod_indirect_conflicts (
                id INTEGER PRIMARY KEY,
                mod1_id INTEGER NOT NULL,
                mod2_id INTEGER NOT NULL,
                shared_entity_id INTEGER NOT NULL,
                mod1_entity_id INTEGER,
                mod2_entity_id INTEGER,
                mod1_operation TEXT,
                mod2_operation TEXT,
                mod1_path_json TEXT,
                mod2_path_json TEXT,
                severity TEXT NOT NULL,
                pattern_id TEXT NOT NULL,
                pattern_name TEXT,
                explanation TEXT,
                FOREIGN KEY (mod1_id) REFERENCES mods(id),
                FOREIGN KEY (mod2_id) REFERENCES mods(id),
                FOREIGN KEY (shared_entity_id) REFERENCES xml_definitions(id)
            );
            CREATE INDEX IF NOT EXISTS idx_indirect_mod1 ON mod_indirect_conflicts(mod1_id);
            CREATE INDEX IF NOT EXISTS idx_indirect_mod2 ON mod_indirect_conflicts(mod2_id);
            CREATE INDEX IF NOT EXISTS idx_indirect_shared ON mod_indirect_conflicts(shared_entity_id);
            CREATE INDEX IF NOT EXISTS idx_indirect_severity ON mod_indirect_conflicts(severity);
            CREATE INDEX IF NOT EXISTS idx_indirect_pattern ON mod_indirect_conflicts(pattern_id);
        ";
        cmd.ExecuteNonQuery();
    }

    // ========================================================================
    // HIGH SEVERITY PATTERNS (H1-H10)
    // "Will likely cause errors or broken functionality"
    // ========================================================================

    /// <summary>
    /// H1: Deleted entity referenced - ModA deletes entity that is still referenced elsewhere
    /// </summary>
    private List<IndirectConflict> DetectDeletedEntityReferenced(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        // Simpler query: find remove operations where the target has references
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id as mod1_id,
                d.id as shared_entity_id,
                o.target_name as deleted_entity,
                m1.name as deleter_mod,
                COUNT(DISTINCT r.id) as ref_count
            FROM mod_xml_operations o
            JOIN mods m1 ON o.mod_id = m1.id
            JOIN xml_definitions d ON o.target_name = d.name AND o.target_type = d.definition_type
            JOIN xml_references r ON r.target_name = d.name AND r.target_type = d.definition_type
            WHERE o.operation = 'remove'
            GROUP BY m1.id, d.id, o.target_name, m1.name
            HAVING COUNT(DISTINCT r.id) > 0";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = 0, // Vanilla references
                SharedEntityId = reader.GetInt32(1),
                Severity = "high",
                PatternId = "H1",
                PatternName = "Deleted entity referenced",
                Explanation = $"'{reader.GetString(3)}' removes '{reader.GetString(2)}' which has {reader.GetInt32(4)} references"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// H2: Broken inheritance chain - ModA modifies parent in chain that ModB depends on through child
    /// </summary>
    private List<IndirectConflict> DetectBrokenInheritanceChain(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id as mod1_id,
                m2.id as mod2_id,
                tr.target_def_id as shared_entity_id,
                d_target.name as parent_entity,
                d_source.name as child_entity,
                m1.name as parent_modifier_mod,
                m2.name as child_modifier_mod,
                tr.path_depth
            FROM transitive_references tr
            JOIN xml_definitions d_source ON tr.source_def_id = d_source.id
            JOIN xml_definitions d_target ON tr.target_def_id = d_target.id
            JOIN mod_xml_operations o1 ON o1.target_name = d_target.name AND o1.target_type = d_target.definition_type
            JOIN mod_xml_operations o2 ON o2.target_name = d_source.name AND o2.target_type = d_source.definition_type
            JOIN mods m1 ON o1.mod_id = m1.id
            JOIN mods m2 ON o2.mod_id = m2.id
            WHERE tr.reference_types LIKE '%extends%'
              AND o1.operation IN ('remove', 'set')
              AND o1.property_name IS NOT NULL
              AND m1.id != m2.id
              AND tr.path_depth > 1";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Severity = "high",
                PatternId = "H2",
                PatternName = "Broken inheritance chain",
                Explanation = $"'{reader.GetString(5)}' modifies parent '{reader.GetString(3)}' which '{reader.GetString(6)}' depends on through child '{reader.GetString(4)}' ({reader.GetInt32(7)} hops)"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// H3: Conflicting base values - two mods set incompatible values on same property through inheritance
    /// </summary>
    private List<IndirectConflict> DetectConflictingBaseValues(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id, m2.id, d.id,
                o1.property_name,
                o1.new_value as val1,
                o2.new_value as val2,
                m1.name, m2.name, d.name
            FROM mod_xml_operations o1
            JOIN mod_xml_operations o2 ON o1.target_name = o2.target_name 
                AND o1.target_type = o2.target_type
                AND o1.property_name = o2.property_name
            JOIN mods m1 ON o1.mod_id = m1.id
            JOIN mods m2 ON o2.mod_id = m2.id
            JOIN xml_definitions d ON o1.target_name = d.name AND o1.target_type = d.definition_type
            WHERE o1.operation = 'set' AND o2.operation = 'set'
              AND o1.new_value != o2.new_value
              AND o1.mod_id < o2.mod_id";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Mod1Operation = "set",
                Mod2Operation = "set",
                Severity = "high",
                PatternId = "H3",
                PatternName = "Conflicting base values",
                Explanation = $"Both '{reader.GetString(6)}' and '{reader.GetString(7)}' set '{reader.GetString(3)}' on '{reader.GetString(8)}' to different values ({reader.GetString(4)} vs {reader.GetString(5)})"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// H4: Orphaned buff application - mod removes buff that is still referenced
    /// </summary>
    private List<IndirectConflict> DetectOrphanedBuffApplication(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        // Simpler query: find buff removals where the buff has AddBuff references
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id,
                d_buff.id,
                d_buff.name as buff_name,
                m1.name as remover_mod,
                COUNT(DISTINCT r.id) as ref_count
            FROM mod_xml_operations o
            JOIN mods m1 ON o.mod_id = m1.id
            JOIN xml_definitions d_buff ON o.target_name = d_buff.name AND d_buff.definition_type = 'buff'
            JOIN xml_references r ON r.target_name = d_buff.name AND r.target_type = 'buff'
            WHERE o.operation = 'remove'
              AND o.target_type = 'buff'
              AND r.reference_context LIKE '%AddBuff%'
            GROUP BY m1.id, d_buff.id, d_buff.name, m1.name
            HAVING COUNT(DISTINCT r.id) > 0";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = 0,
                SharedEntityId = reader.GetInt32(1),
                Severity = "high",
                PatternId = "H4",
                PatternName = "Orphaned buff application",
                Explanation = $"'{reader.GetString(3)}' removes buff '{reader.GetString(2)}' which is applied by {reader.GetInt32(4)} items/effects"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// H5: Recipe output conflict - two mods modify recipes for same output
    /// </summary>
    private List<IndirectConflict> DetectRecipeOutputConflict(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        // Find mods that both modify the same recipe
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id, m2.id, d.id,
                d.name as recipe_name,
                m1.name, m2.name
            FROM mod_xml_operations o1
            JOIN mod_xml_operations o2 ON o1.target_name = o2.target_name
            JOIN mods m1 ON o1.mod_id = m1.id
            JOIN mods m2 ON o2.mod_id = m2.id
            JOIN xml_definitions d ON o1.target_name = d.name AND d.definition_type = 'recipe'
            WHERE o1.target_type = 'recipe'
              AND o2.target_type = 'recipe'
              AND o1.mod_id < o2.mod_id";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Severity = "high",
                PatternId = "H5",
                PatternName = "Recipe conflict",
                Explanation = $"Both '{reader.GetString(4)}' and '{reader.GetString(5)}' modify recipe '{reader.GetString(3)}'"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// H6: Loot table breakage - mod removes items still in loot tables
    /// </summary>
    private List<IndirectConflict> DetectLootTableBreakage(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        // Find item removals where the item is in loot tables
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id,
                d_item.id,
                o.target_name as removed_item,
                m1.name as item_remover,
                COUNT(DISTINCT r.id) as loot_ref_count
            FROM mod_xml_operations o
            JOIN mods m1 ON o.mod_id = m1.id
            JOIN xml_definitions d_item ON o.target_name = d_item.name AND d_item.definition_type = 'item'
            JOIN xml_references r ON r.target_name = o.target_name AND r.reference_context = 'loot_entry'
            WHERE o.operation = 'remove'
              AND o.target_type = 'item'
            GROUP BY m1.id, d_item.id, o.target_name, m1.name
            HAVING COUNT(DISTINCT r.id) > 0";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = 0,
                SharedEntityId = reader.GetInt32(1),
                Severity = "high",
                PatternId = "H6",
                PatternName = "Loot table breakage",
                Explanation = $"'{reader.GetString(3)}' removes item '{reader.GetString(2)}' which appears in {reader.GetInt32(4)} loot tables"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// H7: Entity class conflict - incompatible modifications to core entity_classes
    /// </summary>
    private List<IndirectConflict> DetectEntityClassConflict(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id, m2.id, d.id,
                d.name as entity_class,
                o1.property_name,
                o1.new_value as val1,
                o2.new_value as val2,
                m1.name, m2.name
            FROM mod_xml_operations o1
            JOIN mod_xml_operations o2 ON o1.target_name = o2.target_name
                AND o1.property_name = o2.property_name
            JOIN mods m1 ON o1.mod_id = m1.id
            JOIN mods m2 ON o2.mod_id = m2.id
            JOIN xml_definitions d ON o1.target_name = d.name
            WHERE o1.target_type = 'entity_class'
              AND o2.target_type = 'entity_class'
              AND o1.mod_id < o2.mod_id
              AND (o1.new_value != o2.new_value OR o1.operation != o2.operation)";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Severity = "high",
                PatternId = "H7",
                PatternName = "Entity class conflict",
                Explanation = $"Both '{reader.GetString(7)}' and '{reader.GetString(8)}' modify entity_class '{reader.GetString(3)}' property '{reader.GetString(4)}'"
            });
        }

        return conflicts;
    }

    // ========================================================================
    // MEDIUM SEVERITY PATTERNS (M1-M8)
    // "Impactful gameplay change, worth verifying this is what you want"
    // ========================================================================

    /// <summary>
    /// M1: Transitive property override - property changes propagate unexpectedly through inheritance
    /// </summary>
    private List<IndirectConflict> DetectTransitivePropertyOverride(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        // Detect property changes that propagate through inheritance chains
        // Simplified: find mods modifying parents of entities with deep inheritance
        cmd.CommandText = @"
            SELECT DISTINCT
                m.id, m.id, tr.target_def_id,
                d_parent.name as parent,
                d_child.name as child,
                o.property_name,
                m.name, m.name,
                tr.path_depth
            FROM transitive_references tr
            JOIN xml_definitions d_parent ON tr.target_def_id = d_parent.id
            JOIN xml_definitions d_child ON tr.source_def_id = d_child.id
            JOIN mod_xml_operations o ON o.target_name = d_parent.name
            JOIN mods m ON o.mod_id = m.id
            WHERE tr.reference_types LIKE '%extends%'
              AND tr.path_depth >= 2
              AND o.property_name IS NOT NULL
            LIMIT 100";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Severity = "medium",
                PatternId = "M1",
                PatternName = "Transitive property override",
                Explanation = $"'{reader.GetString(6)}' modifies '{reader.GetString(5)}' on '{reader.GetString(3)}', affecting '{reader.GetString(7)}'s entity '{reader.GetString(4)}' ({reader.GetInt32(8)} inheritance hops)"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// M2: Buff chain interference - mods touch different parts of same buff chain
    /// </summary>
    private List<IndirectConflict> DetectBuffChainInterference(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id, m2.id, tr.target_def_id,
                d_start.name as buff1,
                d_end.name as buff2,
                m1.name, m2.name,
                tr.path_depth
            FROM transitive_references tr
            JOIN xml_definitions d_start ON tr.source_def_id = d_start.id
            JOIN xml_definitions d_end ON tr.target_def_id = d_end.id
            JOIN mod_xml_operations o1 ON o1.target_name = d_start.name AND o1.target_type = 'buff'
            JOIN mod_xml_operations o2 ON o2.target_name = d_end.name AND o2.target_type = 'buff'
            JOIN mods m1 ON o1.mod_id = m1.id
            JOIN mods m2 ON o2.mod_id = m2.id
            WHERE tr.reference_types LIKE '%AddBuff%'
              AND m1.id != m2.id
              AND m1.id < m2.id
            LIMIT 50";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Severity = "medium",
                PatternId = "M2",
                PatternName = "Buff chain interference",
                Explanation = $"'{reader.GetString(5)}' modifies buff '{reader.GetString(3)}' which chains to buff '{reader.GetString(4)}' modified by '{reader.GetString(6)}'"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// M3: Shared inheritance modification - multiple mods modify same parent entity
    /// </summary>
    private List<IndirectConflict> DetectSharedInheritanceModification(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                d.id as shared_entity_id,
                d.name as parent_name,
                COUNT(DISTINCT o.mod_id) as mod_count,
                GROUP_CONCAT(DISTINCT m.name) as mods,
                (SELECT COUNT(*) FROM transitive_references tr WHERE tr.target_def_id = d.id) as dependent_count
            FROM xml_definitions d
            JOIN mod_xml_operations o ON o.target_name = d.name AND o.target_type = d.definition_type
            JOIN mods m ON o.mod_id = m.id
            WHERE d.extends IS NULL
              AND d.definition_type IN ('item', 'block', 'entity_class')
            GROUP BY d.id
            HAVING COUNT(DISTINCT o.mod_id) >= 2
            ORDER BY dependent_count DESC
            LIMIT 50";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var mods = reader.GetString(3).Split(',');
            if (mods.Length >= 2)
            {
                conflicts.Add(new IndirectConflict
                {
                    Mod1Id = 0, // Would need additional query to get proper IDs
                    Mod2Id = 0,
                    SharedEntityId = reader.GetInt32(0),
                    Severity = "medium",
                    PatternId = "M3",
                    PatternName = "Shared inheritance modification",
                    Explanation = $"{reader.GetInt32(2)} mods ({reader.GetString(3)}) modify parent '{reader.GetString(1)}' with {reader.GetInt32(4)} dependents"
                });
            }
        }

        return conflicts;
    }

    /// <summary>
    /// M4: Recipe ingredient change - recipe modified while ingredient sources change
    /// </summary>
    private List<IndirectConflict> DetectRecipeIngredientChange(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                m1.id, m2.id, d_recipe.id,
                d_recipe.name as recipe,
                r.target_name as ingredient,
                m1.name, m2.name
            FROM xml_references r
            JOIN xml_definitions d_recipe ON r.source_def_id = d_recipe.id
            JOIN mod_xml_operations o1 ON o1.target_name = d_recipe.name AND o1.target_type = 'recipe'
            JOIN mod_xml_operations o2 ON o2.target_name = r.target_name
            JOIN mods m1 ON o1.mod_id = m1.id
            JOIN mods m2 ON o2.mod_id = m2.id
            WHERE r.reference_context = 'recipe_ingredient'
              AND m1.id != m2.id
            LIMIT 50";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = reader.GetInt32(0),
                Mod2Id = reader.GetInt32(1),
                SharedEntityId = reader.GetInt32(2),
                Severity = "medium",
                PatternId = "M4",
                PatternName = "Recipe ingredient change",
                Explanation = $"'{reader.GetString(5)}' modifies recipe '{reader.GetString(3)}' while '{reader.GetString(6)}' modifies ingredient '{reader.GetString(4)}'"
            });
        }

        return conflicts;
    }

    // ========================================================================
    // LOW SEVERITY PATTERNS (L1-L6)
    // "Here's an interaction you might want to know about"
    // ========================================================================

    /// <summary>
    /// L1: Additive stacking - multiple mods add to same entity (usually fine, just informational)
    /// </summary>
    private List<IndirectConflict> DetectAdditiveStacking(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                d.id as entity_id,
                d.name as entity_name,
                d.definition_type,
                COUNT(DISTINCT o.mod_id) as mod_count,
                GROUP_CONCAT(DISTINCT m.name) as mods
            FROM mod_xml_operations o
            JOIN xml_definitions d ON o.target_name = d.name AND o.target_type = d.definition_type
            JOIN mods m ON o.mod_id = m.id
            WHERE o.operation = 'append'
            GROUP BY d.id
            HAVING COUNT(DISTINCT o.mod_id) >= 2
            ORDER BY mod_count DESC
            LIMIT 30";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = 0,
                Mod2Id = 0,
                SharedEntityId = reader.GetInt32(0),
                Severity = "low",
                PatternId = "L1",
                PatternName = "Additive stacking",
                Explanation = $"{reader.GetInt32(3)} mods append to {reader.GetString(2)} '{reader.GetString(1)}': {reader.GetString(4)}"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// L2: Mods enhancing same entity - separate modifications to same target (coordination opportunity)
    /// </summary>
    private List<IndirectConflict> DetectModsEnhancingSameEntity(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                d.id,
                d.name,
                d.definition_type,
                COUNT(DISTINCT o.mod_id) as mod_count,
                GROUP_CONCAT(DISTINCT m.name || ':' || o.operation) as mod_actions
            FROM mod_xml_operations o
            JOIN xml_definitions d ON o.target_name = d.name AND o.target_type = d.definition_type
            JOIN mods m ON o.mod_id = m.id
            WHERE o.operation NOT IN ('remove', 'removeattribute')
            GROUP BY d.id
            HAVING COUNT(DISTINCT o.mod_id) >= 3
            ORDER BY mod_count DESC
            LIMIT 20";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = 0,
                Mod2Id = 0,
                SharedEntityId = reader.GetInt32(0),
                Severity = "low",
                PatternId = "L2",
                PatternName = "Mods enhancing same entity",
                Explanation = $"{reader.GetInt32(3)} mods touch {reader.GetString(2)} '{reader.GetString(1)}': {reader.GetString(4)}"
            });
        }

        return conflicts;
    }

    /// <summary>
    /// L3: Related group modifications - mods touch entities in same logical group
    /// </summary>
    private List<IndirectConflict> DetectRelatedGroupModifications(SqliteConnection db)
    {
        var conflicts = new List<IndirectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                r.target_name as group_name,
                COUNT(DISTINCT o.mod_id) as mod_count,
                GROUP_CONCAT(DISTINCT m.name) as mods
            FROM xml_references r
            JOIN xml_definitions d ON r.source_def_id = d.id
            JOIN mod_xml_operations o ON o.target_name = d.name
            JOIN mods m ON o.mod_id = m.id
            WHERE r.reference_context = 'group_member'
            GROUP BY r.target_name
            HAVING COUNT(DISTINCT o.mod_id) >= 2
            ORDER BY mod_count DESC
            LIMIT 20";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new IndirectConflict
            {
                Mod1Id = 0,
                Mod2Id = 0,
                SharedEntityId = 0,
                Severity = "low",
                PatternId = "L3",
                PatternName = "Related group modifications",
                Explanation = $"{reader.GetInt32(1)} mods modify entities in group '{reader.GetString(0)}': {reader.GetString(2)}"
            });
        }

        return conflicts;
    }

    // ========================================================================
    // Database Operations
    // ========================================================================

    private void BulkInsertConflicts(SqliteConnection db, List<IndirectConflict> conflicts)
    {
        // Filter out invalid conflicts (bad foreign keys)
        var validModIds = new HashSet<int>();
        var validEntityIds = new HashSet<int>();
        
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM mods";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) validModIds.Add(reader.GetInt32(0));
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM xml_definitions";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) validEntityIds.Add(reader.GetInt32(0));
        }

        var validConflicts = conflicts.Where(c => 
            validModIds.Contains(c.Mod1Id) && 
            validModIds.Contains(c.Mod2Id) && 
            validEntityIds.Contains(c.SharedEntityId)).ToList();

        using var transaction = db.BeginTransaction();
        using var cmd2 = db.CreateCommand();

        cmd2.CommandText = @"
            INSERT INTO mod_indirect_conflicts 
                (mod1_id, mod2_id, shared_entity_id, mod1_operation, mod2_operation,
                 severity, pattern_id, pattern_name, explanation)
            VALUES ($m1, $m2, $shared, $op1, $op2, $sev, $pid, $pname, $exp)";

        var pM1 = cmd2.Parameters.Add("$m1", SqliteType.Integer);
        var pM2 = cmd2.Parameters.Add("$m2", SqliteType.Integer);
        var pShared = cmd2.Parameters.Add("$shared", SqliteType.Integer);
        var pOp1 = cmd2.Parameters.Add("$op1", SqliteType.Text);
        var pOp2 = cmd2.Parameters.Add("$op2", SqliteType.Text);
        var pSev = cmd2.Parameters.Add("$sev", SqliteType.Text);
        var pPid = cmd2.Parameters.Add("$pid", SqliteType.Text);
        var pPname = cmd2.Parameters.Add("$pname", SqliteType.Text);
        var pExp = cmd2.Parameters.Add("$exp", SqliteType.Text);

        foreach (var c in validConflicts)
        {
            pM1.Value = c.Mod1Id;
            pM2.Value = c.Mod2Id;
            pShared.Value = c.SharedEntityId;
            pOp1.Value = c.Mod1Operation ?? (object)DBNull.Value;
            pOp2.Value = c.Mod2Operation ?? (object)DBNull.Value;
            pSev.Value = c.Severity;
            pPid.Value = c.PatternId;
            pPname.Value = c.PatternName ?? (object)DBNull.Value;
            pExp.Value = c.Explanation ?? (object)DBNull.Value;
            cmd2.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ========================================================================
    // Internal Models
    // ========================================================================

    private class IndirectConflict
    {
        public int Mod1Id { get; set; }
        public int Mod2Id { get; set; }
        public int SharedEntityId { get; set; }
        public string? Mod1Operation { get; set; }
        public string? Mod2Operation { get; set; }
        public required string Severity { get; set; }
        public required string PatternId { get; set; }
        public string? PatternName { get; set; }
        public string? Explanation { get; set; }
    }
}

/// <summary>
/// Result of indirect conflict detection - for logging/diagnostics.
/// </summary>
public class IndirectConflictResult
{
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int ConflictsDetected { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;

    public override string ToString() =>
        $"Indirect conflicts detected: {ConflictsDetected} (HIGH: {HighCount}, MEDIUM: {MediumCount}, LOW: {LowCount}) in {Duration.TotalSeconds:F1}s";
}
