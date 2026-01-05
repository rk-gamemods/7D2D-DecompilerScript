using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Database;

/// <summary>
/// Handles SQLite database schema creation and bulk data writing.
/// </summary>
public static class DatabaseBuilder
{
    /// <summary>
    /// Creates the complete database schema for XML indexing and mod analysis.
    /// </summary>
    public static void CreateSchema(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Bulk write all in-memory data to the database.
    /// </summary>
    public static void BulkWriteAllData(
        SqliteConnection db,
        List<XmlDefinition> definitions,
        List<XmlProperty> properties,
        List<XmlReference> references,
        Dictionary<string, int> stats)
    {
        using var transaction = db.BeginTransaction();

        BulkWriteDefinitions(db, definitions);
        BulkWriteProperties(db, properties);
        BulkWriteReferences(db, references);
        BulkWriteStats(db, stats);

        transaction.Commit();
    }

    private static void BulkWriteDefinitions(SqliteConnection db, List<XmlDefinition> definitions)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO xml_definitions (id, definition_type, name, file_path, line_number, extends) VALUES ($id, $type, $name, $file, $line, $extends)";
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pType = cmd.Parameters.Add("$type", SqliteType.Text);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pFile = cmd.Parameters.Add("$file", SqliteType.Text);
        var pLine = cmd.Parameters.Add("$line", SqliteType.Integer);
        var pExtends = cmd.Parameters.Add("$extends", SqliteType.Text);

        foreach (var def in definitions)
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

    private static void BulkWriteProperties(SqliteConnection db, List<XmlProperty> properties)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO xml_properties (definition_id, property_name, property_value, property_class, line_number) VALUES ($defId, $name, $value, $class, $line)";
        var pDefId = cmd.Parameters.Add("$defId", SqliteType.Integer);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pValue = cmd.Parameters.Add("$value", SqliteType.Text);
        var pClass = cmd.Parameters.Add("$class", SqliteType.Text);
        var pLine = cmd.Parameters.Add("$line", SqliteType.Integer);

        foreach (var prop in properties)
        {
            pDefId.Value = prop.DefId;
            pName.Value = prop.Name;
            pValue.Value = prop.Value ?? (object)DBNull.Value;
            pClass.Value = prop.Class ?? (object)DBNull.Value;
            pLine.Value = prop.Line;
            cmd.ExecuteNonQuery();
        }
    }

    private static void BulkWriteReferences(SqliteConnection db, List<XmlReference> references)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO xml_references (source_type, source_def_id, source_file, source_line, target_type, target_name, reference_context) VALUES ($srcType, $srcId, $srcFile, $line, $tgtType, $tgtName, $ctx)";
        var pSrcType = cmd.Parameters.Add("$srcType", SqliteType.Text);
        var pSrcId = cmd.Parameters.Add("$srcId", SqliteType.Integer);
        var pSrcFile = cmd.Parameters.Add("$srcFile", SqliteType.Text);
        var pLine = cmd.Parameters.Add("$line", SqliteType.Integer);
        var pTgtType = cmd.Parameters.Add("$tgtType", SqliteType.Text);
        var pTgtName = cmd.Parameters.Add("$tgtName", SqliteType.Text);
        var pCtx = cmd.Parameters.Add("$ctx", SqliteType.Text);

        foreach (var r in references)
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

    private static void BulkWriteStats(SqliteConnection db, Dictionary<string, int> stats)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO xml_stats (definition_type, count) VALUES ($type, $count)";
        var pType = cmd.Parameters.Add("$type", SqliteType.Text);
        var pCount = cmd.Parameters.Add("$count", SqliteType.Integer);

        foreach (var stat in stats)
        {
            pType.Value = stat.Key;
            pCount.Value = stat.Value;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Complete database schema for XML indexing and mod analysis.
    /// </summary>
    private const string Schema = @"
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
            folder_name TEXT,
            load_order INTEGER,
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
        CREATE INDEX idx_mod_load_order ON mods(load_order);

        CREATE TABLE mod_xml_operations (
            id INTEGER PRIMARY KEY,
            mod_id INTEGER,
            operation TEXT NOT NULL,
            xpath TEXT NOT NULL,
            xpath_normalized TEXT,
            xpath_hash TEXT,
            target_type TEXT,
            target_name TEXT,
            property_name TEXT,
            new_value TEXT,
            element_content TEXT,
            file_path TEXT,
            line_number INTEGER,
            impact_status TEXT,
            -- Passive effect context
            effect_name TEXT,           -- Extracted from xpath (e.g., 'CarryCapacity')
            effect_operation TEXT,      -- The passive_effect operation (perc_add, base_set, etc.)
            effect_value_type TEXT,     -- 'base' or 'perc'
            is_set_operation INTEGER,   -- 1 if base_set/perc_set, 0 otherwise
            parent_entity TEXT,         -- The entity/buff/item containing this effect
            parent_entity_name TEXT,    -- The specific name (e.g., 'playerMale', 'buffGodMode')
            -- Triggered effect context
            is_triggered_effect INTEGER,-- 1 if this modifies a triggered_effect
            trigger_action TEXT,        -- ModifyCVar, AddBuff, etc.
            trigger_cvar TEXT,          -- Target CVar name (e.g., '$damage')
            trigger_operation TEXT,     -- set, add, multiply, subtract
            -- Modification tracking
            modifies_operation INTEGER, -- 1 if xpath targets @operation
            modifies_value INTEGER      -- 1 if xpath targets @value
        );
        CREATE INDEX idx_modxml_mod ON mod_xml_operations(mod_id);
        CREATE INDEX idx_modxml_target ON mod_xml_operations(target_type, target_name);
        CREATE INDEX idx_modxml_xpath_hash ON mod_xml_operations(xpath_hash);
        CREATE INDEX idx_modxml_xpath_hash_op ON mod_xml_operations(xpath_hash, operation);
        CREATE INDEX idx_modxml_effect ON mod_xml_operations(effect_name, parent_entity_name);
        CREATE INDEX idx_modxml_trigger ON mod_xml_operations(trigger_cvar);

        CREATE TABLE mod_csharp_deps (
            id INTEGER PRIMARY KEY,
            mod_id INTEGER,
            dependency_type TEXT NOT NULL,
            dependency_name TEXT NOT NULL,
            source_file TEXT,
            line_number INTEGER,
            pattern TEXT,
            code_snippet TEXT
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
            entity_type TEXT NOT NULL,
            entity_name TEXT NOT NULL,
            parent_context TEXT,
            layman_description TEXT,
            technical_description TEXT,
            player_impact TEXT,
            related_systems TEXT,
            example_usage TEXT,
            generated_by TEXT DEFAULT 'pending',
            confidence REAL DEFAULT 0.0,
            llm_model TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(entity_type, entity_name, parent_context)
        );
        CREATE INDEX idx_semantic_type ON semantic_mappings(entity_type);
        CREATE INDEX idx_semantic_name ON semantic_mappings(entity_name);

        -- CONFLICT DETECTION VIEWS --

        -- v_xpath_conflicts: Groups operations by normalized xpath+operation to find real conflicts
        CREATE VIEW v_xpath_conflicts AS
        SELECT
            o.xpath_hash,
            o.xpath_normalized,
            o.operation,
            COUNT(DISTINCT o.mod_id) as mod_count,
            GROUP_CONCAT(DISTINCT m.name) as mods_involved,
            COUNT(DISTINCT o.new_value) as distinct_values,
            CASE
                WHEN COUNT(DISTINCT o.new_value) > 1 THEN 'REAL_CONFLICT'
                WHEN COUNT(DISTINCT o.new_value) = 1 THEN 'SAME_VALUE'
                ELSE 'COMPLEMENTARY'
            END as conflict_type,
            CASE
                WHEN o.operation IN ('remove', 'removeattribute') THEN 'HIGH'
                WHEN o.target_type IN ('entity_class', 'progression', 'gamestages', 'loot') THEN 'MEDIUM'
                WHEN o.target_name LIKE 'player%' OR o.target_name LIKE 'zombie%' THEN 'MEDIUM'
                ELSE 'LOW'
            END as base_severity,
            o.target_type,
            o.target_name,
            o.property_name
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.xpath_hash IS NOT NULL
        GROUP BY o.xpath_hash, o.operation
        HAVING COUNT(DISTINCT o.mod_id) > 1;

        -- v_destructive_conflicts: Detects edit-vs-remove conflicts (HIGH severity)
        CREATE VIEW v_destructive_conflicts AS
        SELECT
            a.xpath_hash,
            a.xpath_normalized as xpath,
            ma.name as editor_mod,
            a.operation as editor_op,
            a.new_value as editor_value,
            mb.name as remover_mod,
            b.operation as remover_op,
            a.target_type,
            a.target_name,
            ma.load_order as editor_load_order,
            mb.load_order as remover_load_order,
            CASE
                WHEN mb.load_order > ma.load_order THEN 'REMOVER_WINS'
                ELSE 'EDITOR_WINS'
            END as winner
        FROM mod_xml_operations a
        JOIN mod_xml_operations b ON a.xpath_hash = b.xpath_hash
        JOIN mods ma ON a.mod_id = ma.id
        JOIN mods mb ON b.mod_id = mb.id
        WHERE a.operation IN ('set', 'append', 'setattribute', 'insertAfter', 'insertBefore')
          AND b.operation IN ('remove', 'removeattribute')
          AND a.mod_id != b.mod_id;

        -- v_contested_entities: Entity-level conflict summary with risk assessment
        CREATE VIEW v_contested_entities AS
        SELECT
            o.target_type,
            o.target_name,
            COUNT(DISTINCT o.mod_id) as mod_count,
            GROUP_CONCAT(DISTINCT m.name || ':' || o.operation) as mod_actions,
            SUM(CASE WHEN o.operation IN ('remove', 'removeattribute') THEN 1 ELSE 0 END) as removal_count,
            SUM(CASE WHEN o.operation = 'set' THEN 1 ELSE 0 END) as set_count,
            SUM(CASE WHEN o.operation = 'append' THEN 1 ELSE 0 END) as append_count,
            CASE
                WHEN SUM(CASE WHEN o.operation IN ('remove', 'removeattribute') THEN 1 ELSE 0 END) > 0
                     AND SUM(CASE WHEN o.operation NOT IN ('remove', 'removeattribute') THEN 1 ELSE 0 END) > 0
                THEN 'HIGH'
                WHEN COUNT(DISTINCT o.mod_id) >= 3 THEN 'MEDIUM'
                WHEN o.target_name LIKE 'player%' OR o.target_name LIKE 'zombie%' THEN 'MEDIUM'
                ELSE 'LOW'
            END as risk_level
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.target_type IS NOT NULL AND o.target_name IS NOT NULL
        GROUP BY o.target_type, o.target_name
        HAVING COUNT(DISTINCT o.mod_id) > 1;

        -- v_load_order_winners: Shows which mod wins for each conflicting xpath
        CREATE VIEW v_load_order_winners AS
        SELECT
            o.xpath_hash,
            o.xpath_normalized as xpath,
            o.operation,
            m.name as winning_mod,
            m.load_order,
            o.new_value as winning_value,
            o.target_type,
            o.target_name,
            (SELECT COUNT(DISTINCT o2.mod_id)
             FROM mod_xml_operations o2
             WHERE o2.xpath_hash = o.xpath_hash AND o2.operation = o.operation) as total_mods
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.xpath_hash IS NOT NULL
          AND m.load_order = (
              SELECT MAX(m2.load_order)
              FROM mod_xml_operations o2
              JOIN mods m2 ON o2.mod_id = m2.id
              WHERE o2.xpath_hash = o.xpath_hash AND o2.operation = o.operation
          )
          AND (SELECT COUNT(DISTINCT o3.mod_id)
               FROM mod_xml_operations o3
               WHERE o3.xpath_hash = o.xpath_hash AND o3.operation = o.operation) > 1;

        -- ============================================================================
        -- EFFECT-LEVEL CONFLICT DETECTION VIEWS
        -- ============================================================================

        -- v_effect_operation_conflicts: Different operation types on same effect (HIGH)
        CREATE VIEW v_effect_operation_conflicts AS
        SELECT
            o1.effect_name,
            o1.parent_entity_name,
            m1.name as mod1,
            o1.new_value as mod1_operation,
            m2.name as mod2,
            o2.new_value as mod2_operation,
            'HIGH' as severity,
            'Different math operations on same effect' as reason
        FROM mod_xml_operations o1
        JOIN mod_xml_operations o2
            ON o1.effect_name = o2.effect_name
            AND o1.parent_entity_name = o2.parent_entity_name
        JOIN mods m1 ON o1.mod_id = m1.id
        JOIN mods m2 ON o2.mod_id = m2.id
        WHERE o1.modifies_operation = 1
          AND o2.modifies_operation = 1
          AND o1.new_value != o2.new_value
          AND o1.mod_id < o2.mod_id
          AND o1.new_value IN ('perc_add','perc_set','base_add','base_set','perc_subtract','base_subtract')
          AND o2.new_value IN ('perc_add','perc_set','base_add','base_set','perc_subtract','base_subtract');

        -- v_set_overrides_add: SET operations nullifying ADD operations (HIGH)
        CREATE VIEW v_set_overrides_add AS
        SELECT
            o1.effect_name,
            o1.parent_entity_name,
            m1.name as add_mod,
            o1.effect_operation as add_operation,
            o1.new_value as add_value,
            m2.name as set_mod,
            o2.effect_operation as set_operation,
            o2.new_value as set_value,
            m1.load_order as add_load_order,
            m2.load_order as set_load_order,
            CASE WHEN m2.load_order > m1.load_order THEN 'SET_WINS' ELSE 'ADD_APPLIED_AFTER' END as outcome,
            'HIGH' as severity
        FROM mod_xml_operations o1
        JOIN mod_xml_operations o2
            ON o1.effect_name = o2.effect_name
            AND o1.parent_entity_name = o2.parent_entity_name
        JOIN mods m1 ON o1.mod_id = m1.id
        JOIN mods m2 ON o2.mod_id = m2.id
        WHERE o1.effect_operation LIKE '%_add%'
          AND o2.effect_operation LIKE '%_set%'
          AND o1.mod_id != o2.mod_id;

        -- v_synergistic_stacking: ANY 2+ mods modifying same effect (MEDIUM)
        CREATE VIEW v_synergistic_stacking AS
        SELECT
            o.effect_name,
            o.parent_entity_name,
            o.effect_operation,
            COUNT(DISTINCT o.mod_id) as mod_count,
            GROUP_CONCAT(DISTINCT m.name) as mods,
            GROUP_CONCAT(DISTINCT m.name || '=' || COALESCE(o.new_value, '?')) as mod_values,
            'MEDIUM' as severity,
            'Multiple mods modifying same effect - values may stack' as reason
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.effect_name IS NOT NULL
          AND o.effect_operation IS NOT NULL
        GROUP BY o.effect_name, o.parent_entity_name, o.effect_operation
        HAVING COUNT(DISTINCT o.mod_id) > 1;

        -- v_mixed_modifier_interaction: Different mods using base vs perc on same effect (MEDIUM)
        CREATE VIEW v_mixed_modifier_interaction AS
        SELECT
            o.effect_name,
            o.parent_entity_name,
            COUNT(DISTINCT o.mod_id) as mod_count,
            GROUP_CONCAT(DISTINCT m.name || ':' || o.effect_operation) as mod_operations,
            SUM(CASE WHEN o.effect_value_type = 'base' THEN 1 ELSE 0 END) as base_mod_count,
            SUM(CASE WHEN o.effect_value_type = 'perc' THEN 1 ELSE 0 END) as perc_mod_count,
            'MEDIUM' as severity,
            'Mixed base and percentage modifiers may amplify unexpectedly' as reason
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.effect_name IS NOT NULL
          AND o.effect_value_type IS NOT NULL
        GROUP BY o.effect_name, o.parent_entity_name
        HAVING COUNT(DISTINCT o.effect_value_type) > 1
           AND COUNT(DISTINCT o.mod_id) > 1;

        -- v_triggered_effect_conflicts: CVar modifications from multiple mods
        CREATE VIEW v_triggered_effect_conflicts AS
        SELECT
            o.trigger_cvar,
            o.trigger_operation,
            COUNT(DISTINCT o.mod_id) as mod_count,
            GROUP_CONCAT(DISTINCT m.name) as mods,
            GROUP_CONCAT(DISTINCT m.name || ':' || o.trigger_operation || '=' || COALESCE(o.new_value, '?')) as details,
            CASE
                WHEN o.trigger_operation = 'multiply' AND COUNT(DISTINCT o.mod_id) > 1 THEN 'HIGH'
                WHEN COUNT(DISTINCT CASE WHEN o.trigger_operation = 'set' THEN o.mod_id END) > 0
                     AND COUNT(DISTINCT CASE WHEN o.trigger_operation != 'set' THEN o.mod_id END) > 0 THEN 'MEDIUM'
                ELSE 'LOW'
            END as severity,
            'Multiple mods modifying same CVar' as reason
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.is_triggered_effect = 1
          AND o.trigger_cvar IS NOT NULL
        GROUP BY o.trigger_cvar, o.trigger_operation
        HAVING COUNT(DISTINCT o.mod_id) > 1;

        -- v_partial_modifications: Mods changing @operation or @value but not both (MEDIUM)
        CREATE VIEW v_partial_modifications AS
        SELECT
            o.effect_name,
            o.parent_entity_name,
            m.name as mod_name,
            o.modifies_operation,
            o.modifies_value,
            o.file_path,
            o.line_number,
            'MEDIUM' as severity,
            CASE
                WHEN o.modifies_operation = 1 AND COALESCE(o.modifies_value, 0) = 0 THEN 'Changes @operation without @value'
                WHEN COALESCE(o.modifies_operation, 0) = 0 AND o.modifies_value = 1 THEN 'Changes @value without @operation'
            END as reason
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.effect_name IS NOT NULL
          AND ((o.modifies_operation = 1 AND COALESCE(o.modifies_value, 0) = 0)
               OR (COALESCE(o.modifies_operation, 0) = 0 AND o.modifies_value = 1));

        -- v_effect_summary: Complete view of all modifications to each effect
        CREATE VIEW v_effect_summary AS
        SELECT
            o.effect_name,
            o.parent_entity,
            o.parent_entity_name,
            COUNT(DISTINCT o.mod_id) as total_mods,
            GROUP_CONCAT(DISTINCT m.name || ':' || COALESCE(o.effect_operation, 'unknown')) as mod_operations,
            SUM(CASE WHEN o.effect_operation LIKE '%_set%' THEN 1 ELSE 0 END) as set_count,
            SUM(CASE WHEN o.effect_operation LIKE '%_add%' THEN 1 ELSE 0 END) as add_count,
            SUM(CASE WHEN o.effect_operation LIKE 'perc_%' THEN 1 ELSE 0 END) as perc_count,
            SUM(CASE WHEN o.effect_operation LIKE 'base_%' THEN 1 ELSE 0 END) as base_count,
            CASE
                WHEN SUM(CASE WHEN o.effect_operation LIKE '%_set%' THEN 1 ELSE 0 END) > 0
                     AND SUM(CASE WHEN o.effect_operation LIKE '%_add%' THEN 1 ELSE 0 END) > 0 THEN 'HIGH'
                WHEN COUNT(DISTINCT o.mod_id) >= 3 THEN 'MEDIUM'
                ELSE 'LOW'
            END as risk_level
        FROM mod_xml_operations o
        JOIN mods m ON o.mod_id = m.id
        WHERE o.effect_name IS NOT NULL
        GROUP BY o.effect_name, o.parent_entity, o.parent_entity_name
        HAVING COUNT(DISTINCT o.mod_id) > 1;

        -- ============================================================================
        -- HARMONY PATCH ANALYSIS TABLES
        -- ============================================================================

        -- Detailed Harmony patch metadata for conflict detection
        CREATE TABLE harmony_patches (
            id INTEGER PRIMARY KEY,
            mod_id INTEGER NOT NULL,
            patch_class TEXT NOT NULL,
            target_class TEXT NOT NULL,
            target_method TEXT NOT NULL,
            patch_type TEXT NOT NULL,
            target_member_kind TEXT,
            target_arg_types TEXT,
            target_declaring_type TEXT,
            harmony_priority INTEGER DEFAULT 400,
            harmony_before TEXT,
            harmony_after TEXT,
            returns_bool INTEGER DEFAULT 0,
            modifies_result INTEGER DEFAULT 0,
            modifies_state INTEGER DEFAULT 0,
            is_guarded INTEGER DEFAULT 0,
            guard_condition TEXT,
            is_dynamic INTEGER DEFAULT 0,
            parameter_signature TEXT,
            code_snippet TEXT,
            source_file TEXT,
            line_number INTEGER,
            FOREIGN KEY (mod_id) REFERENCES mods(id),
            UNIQUE(mod_id, target_class, target_method, patch_type, patch_class)
        );
        CREATE INDEX idx_harmony_mod ON harmony_patches(mod_id);
        CREATE INDEX idx_harmony_target ON harmony_patches(target_class, target_method);
        CREATE INDEX idx_harmony_target_type ON harmony_patches(target_class, target_method, patch_type);
        CREATE INDEX idx_harmony_priority ON harmony_patches(harmony_priority);

        -- Detected Harmony patch conflicts between mods
        CREATE TABLE harmony_conflicts (
            id INTEGER PRIMARY KEY,
            target_class TEXT NOT NULL,
            target_method TEXT NOT NULL,
            conflict_type TEXT NOT NULL,
            severity TEXT NOT NULL,
            confidence TEXT NOT NULL,
            mod1_id INTEGER,
            mod2_id INTEGER,
            patch1_id INTEGER,
            patch2_id INTEGER,
            same_signature INTEGER DEFAULT 1,
            explanation TEXT,
            reasoning TEXT,
            FOREIGN KEY (mod1_id) REFERENCES mods(id),
            FOREIGN KEY (mod2_id) REFERENCES mods(id),
            FOREIGN KEY (patch1_id) REFERENCES harmony_patches(id),
            FOREIGN KEY (patch2_id) REFERENCES harmony_patches(id)
        );
        CREATE INDEX idx_conflict_severity ON harmony_conflicts(severity);
        CREATE INDEX idx_conflict_type ON harmony_conflicts(conflict_type);
        CREATE INDEX idx_conflict_target ON harmony_conflicts(target_class, target_method);

        -- ============================================================================
        -- GAME CODE ANALYSIS TABLES
        -- ============================================================================

        -- Game code bug hunting findings
        CREATE TABLE game_code_analysis (
            id INTEGER PRIMARY KEY,
            analysis_type TEXT NOT NULL,
            class_name TEXT NOT NULL,
            method_name TEXT,
            severity TEXT NOT NULL,
            confidence TEXT NOT NULL,
            description TEXT,
            reasoning TEXT,
            code_snippet TEXT,
            file_path TEXT,
            line_number INTEGER,
            potential_fix TEXT,
            related_entities TEXT,
            file_hash TEXT,
            is_unity_magic INTEGER DEFAULT 0,
            is_reflection_target INTEGER DEFAULT 0,
            UNIQUE(file_path, line_number, analysis_type)
        );
        CREATE INDEX idx_analysis_type ON game_code_analysis(analysis_type);
        CREATE INDEX idx_analysis_severity ON game_code_analysis(severity, confidence);
        CREATE INDEX idx_analysis_file ON game_code_analysis(file_path);
        CREATE INDEX idx_analysis_class ON game_code_analysis(class_name);

        -- Cached game method signatures for overload matching
        CREATE TABLE method_signatures (
            id INTEGER PRIMARY KEY,
            class_name TEXT NOT NULL,
            method_name TEXT NOT NULL,
            parameter_types TEXT NOT NULL,
            parameter_types_full TEXT,
            return_type TEXT,
            return_type_full TEXT,
            is_static INTEGER DEFAULT 0,
            is_virtual INTEGER DEFAULT 0,
            is_override INTEGER DEFAULT 0,
            access_modifier TEXT,
            declaring_class TEXT,
            file_path TEXT,
            file_hash TEXT,
            UNIQUE(class_name, method_name, parameter_types)
        );
        CREATE INDEX idx_sig_class ON method_signatures(class_name);
        CREATE INDEX idx_sig_method ON method_signatures(class_name, method_name);
        CREATE INDEX idx_sig_declaring ON method_signatures(declaring_class);

        -- Class inheritance hierarchy for base class patch detection
        CREATE TABLE class_inheritance (
            id INTEGER PRIMARY KEY,
            class_name TEXT NOT NULL UNIQUE,
            parent_class TEXT,
            interfaces TEXT,
            is_abstract INTEGER DEFAULT 0,
            is_sealed INTEGER DEFAULT 0,
            file_path TEXT,
            file_hash TEXT
        );
        CREATE INDEX idx_inheritance_parent ON class_inheritance(parent_class);
        CREATE INDEX idx_inheritance_class ON class_inheritance(class_name);

        -- Type alias mapping from using statements
        CREATE TABLE type_aliases (
            id INTEGER PRIMARY KEY,
            file_path TEXT NOT NULL,
            short_name TEXT NOT NULL,
            full_name TEXT NOT NULL,
            namespace TEXT,
            UNIQUE(file_path, short_name)
        );
        CREATE INDEX idx_alias_short ON type_aliases(short_name);
        CREATE INDEX idx_alias_file ON type_aliases(file_path);

        -- ============================================================================
        -- METHOD CALL GRAPH TABLE (for incremental call graph analysis)
        -- ============================================================================

        CREATE TABLE IF NOT EXISTS method_calls (
            id INTEGER PRIMARY KEY,
            caller_file TEXT NOT NULL,
            caller_class TEXT,
            caller_method TEXT,
            target_class TEXT NOT NULL,
            target_method TEXT NOT NULL,
            call_type TEXT,
            line_number INTEGER,
            code_snippet TEXT,
            file_hash TEXT,
            UNIQUE(caller_file, line_number, target_class, target_method)
        );
        CREATE INDEX IF NOT EXISTS idx_method_calls_caller ON method_calls(caller_file);
        CREATE INDEX IF NOT EXISTS idx_method_calls_target ON method_calls(target_class, target_method);
        CREATE INDEX IF NOT EXISTS idx_method_calls_hash ON method_calls(file_hash);

        -- ============================================================================
        -- HARMONY CONFLICT DETECTION VIEWS
        -- ============================================================================

        -- v_harmony_collisions: Multiple mods patching same game method
        CREATE VIEW v_harmony_collisions AS
        SELECT
            hp.target_class,
            hp.target_method,
            COUNT(DISTINCT hp.mod_id) as mod_count,
            GROUP_CONCAT(DISTINCT m.name) as mods,
            GROUP_CONCAT(DISTINCT hp.patch_type) as patch_types,
            SUM(CASE WHEN hp.patch_type = 'Transpiler' THEN 1 ELSE 0 END) as transpiler_count,
            SUM(CASE WHEN hp.returns_bool = 1 THEN 1 ELSE 0 END) as skip_capable_count,
            CASE
                WHEN SUM(CASE WHEN hp.patch_type = 'Transpiler' THEN 1 ELSE 0 END) > 1 THEN 'CRITICAL'
                WHEN SUM(CASE WHEN hp.returns_bool = 1 THEN 1 ELSE 0 END) > 0
                     AND COUNT(DISTINCT hp.mod_id) > 1 THEN 'HIGH'
                WHEN COUNT(DISTINCT hp.mod_id) >= 3 THEN 'MEDIUM'
                ELSE 'LOW'
            END as severity
        FROM harmony_patches hp
        JOIN mods m ON hp.mod_id = m.id
        GROUP BY hp.target_class, hp.target_method
        HAVING COUNT(DISTINCT hp.mod_id) > 1;

        -- v_transpiler_conflicts: Multiple transpilers on same method (almost always problematic)
        CREATE VIEW v_transpiler_conflicts AS
        SELECT
            hp.target_class,
            hp.target_method,
            COUNT(*) as transpiler_count,
            GROUP_CONCAT(m.name) as mods,
            'CRITICAL' as severity,
            'Multiple transpilers modifying same method IL - high chance of conflict' as reason
        FROM harmony_patches hp
        JOIN mods m ON hp.mod_id = m.id
        WHERE hp.patch_type = 'Transpiler'
        GROUP BY hp.target_class, hp.target_method
        HAVING COUNT(*) > 1;

        -- v_skip_conflicts: Prefix patches that can skip original + other patches exist
        CREATE VIEW v_skip_conflicts AS
        SELECT
            hp1.target_class,
            hp1.target_method,
            m1.name as skip_mod,
            hp1.patch_class as skip_class,
            hp1.harmony_priority as skip_priority,
            GROUP_CONCAT(DISTINCT m2.name) as affected_mods,
            COUNT(DISTINCT hp2.mod_id) as affected_count,
            CASE
                WHEN hp1.harmony_priority > 400 THEN 'HIGH'
                ELSE 'MEDIUM'
            END as severity,
            'Prefix can return false to skip original method and lower-priority patches' as reason
        FROM harmony_patches hp1
        JOIN harmony_patches hp2 ON hp1.target_class = hp2.target_class
            AND hp1.target_method = hp2.target_method
            AND hp1.id != hp2.id
        JOIN mods m1 ON hp1.mod_id = m1.id
        JOIN mods m2 ON hp2.mod_id = m2.id
        WHERE hp1.returns_bool = 1
        GROUP BY hp1.id;

        -- v_harmony_priority_order: Shows execution order based on priority
        CREATE VIEW v_harmony_priority_order AS
        SELECT
            hp.target_class,
            hp.target_method,
            hp.patch_type,
            m.name as mod_name,
            hp.patch_class,
            hp.harmony_priority,
            CASE hp.patch_type
                WHEN 'Prefix' THEN ROW_NUMBER() OVER (
                    PARTITION BY hp.target_class, hp.target_method, hp.patch_type
                    ORDER BY hp.harmony_priority DESC)
                WHEN 'Postfix' THEN ROW_NUMBER() OVER (
                    PARTITION BY hp.target_class, hp.target_method, hp.patch_type
                    ORDER BY hp.harmony_priority ASC)
                ELSE ROW_NUMBER() OVER (
                    PARTITION BY hp.target_class, hp.target_method, hp.patch_type
                    ORDER BY hp.harmony_priority DESC)
            END as execution_order,
            hp.returns_bool,
            hp.modifies_result
        FROM harmony_patches hp
        JOIN mods m ON hp.mod_id = m.id
        ORDER BY hp.target_class, hp.target_method, hp.patch_type, execution_order;

        -- v_guarded_patches: Patches with safety guards (lower false positive rate)
        CREATE VIEW v_guarded_patches AS
        SELECT
            hp.target_class,
            hp.target_method,
            m.name as mod_name,
            hp.patch_class,
            hp.patch_type,
            hp.guard_condition,
            'Guarded patch - may not apply if guard condition fails' as note
        FROM harmony_patches hp
        JOIN mods m ON hp.mod_id = m.id
        WHERE hp.is_guarded = 1;

        -- v_game_code_issues: Summary of game code analysis findings
        CREATE VIEW v_game_code_issues AS
        SELECT
            analysis_type,
            severity,
            confidence,
            COUNT(*) as issue_count,
            GROUP_CONCAT(DISTINCT class_name) as affected_classes
        FROM game_code_analysis
        GROUP BY analysis_type, severity, confidence
        ORDER BY
            CASE severity
                WHEN 'BUG' THEN 1
                WHEN 'WARNING' THEN 2
                WHEN 'INFO' THEN 3
                WHEN 'OPPORTUNITY' THEN 4
            END,
            CASE confidence
                WHEN 'high' THEN 1
                WHEN 'medium' THEN 2
                WHEN 'low' THEN 3
            END;
    ";
}
