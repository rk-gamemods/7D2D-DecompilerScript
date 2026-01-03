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
    ";
}
