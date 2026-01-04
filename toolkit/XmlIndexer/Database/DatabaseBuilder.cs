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
    ";
}
