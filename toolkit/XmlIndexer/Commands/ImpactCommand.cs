using Microsoft.Data.Sqlite;

namespace XmlIndexer.Commands;

/// <summary>
/// Shows what entities depend on a given entity (using transitive references).
/// Answers the question: "If I change X, what else is affected?"
/// </summary>
public static class ImpactCommand
{
    public static int Execute(string dbPath, string type, string name)
    {
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Error: Database not found: {dbPath}");
            return 1;
        }

        Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  IMPACT ANALYSIS: {type} '{name}'");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝\n");

        using var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();

        // Find the entity
        using var defCmd = db.CreateCommand();
        defCmd.CommandText = @"SELECT id, file_path, line_number, extends 
                               FROM xml_definitions 
                               WHERE definition_type = $type AND name = $name";
        defCmd.Parameters.AddWithValue("$type", type);
        defCmd.Parameters.AddWithValue("$name", name);

        using var defReader = defCmd.ExecuteReader();
        if (!defReader.Read())
        {
            Console.WriteLine($"  Entity not found: {type} '{name}'");
            Console.WriteLine("  Tip: Use 'XmlIndexer search <db> <pattern>' to find entities");
            return 1;
        }

        var entityId = defReader.GetInt32(0);
        var filePath = defReader.GetString(1);
        var lineNumber = defReader.GetInt32(2);
        var extends = defReader.IsDBNull(3) ? null : defReader.GetString(3);
        defReader.Close();

        Console.WriteLine($"  Definition: {filePath}:{lineNumber}");
        if (extends != null)
            Console.WriteLine($"  Extends: {extends}");
        Console.WriteLine();

        // Check if transitive_references table exists and has data
        using var checkCmd = db.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM transitive_references";
        long transCount = 0;
        try
        {
            transCount = (long)checkCmd.ExecuteScalar()!;
        }
        catch
        {
            Console.WriteLine("  ⚠ Transitive references not built yet!");
            Console.WriteLine($"  Run: XmlIndexer build-dependency-graph {Path.GetFileName(dbPath)}");
            return 1;
        }

        if (transCount == 0)
        {
            Console.WriteLine("  ⚠ Transitive references table is empty!");
            Console.WriteLine($"  Run: XmlIndexer build-dependency-graph {Path.GetFileName(dbPath)}");
            return 1;
        }

        // Find what depends on this entity (entities where this is the TARGET)
        Console.WriteLine("═══ ENTITIES THAT DEPEND ON THIS ════════════════════════════════════");
        Console.WriteLine();

        using var depCmd = db.CreateCommand();
        depCmd.CommandText = @"
            SELECT 
                d.definition_type, d.name, tr.path_depth, tr.reference_types
            FROM transitive_references tr
            JOIN xml_definitions d ON tr.source_def_id = d.id
            WHERE tr.target_def_id = $entityId
            ORDER BY tr.path_depth, d.definition_type, d.name
            LIMIT 100";
        depCmd.Parameters.AddWithValue("$entityId", entityId);

        using var depReader = depCmd.ExecuteReader();
        int dependentCount = 0;
        int lastDepth = -1;

        while (depReader.Read())
        {
            var depType = depReader.GetString(0);
            var depName = depReader.GetString(1);
            var depth = depReader.GetInt32(2);
            var refTypes = depReader.GetString(3);

            if (depth != lastDepth)
            {
                Console.WriteLine($"  ── Depth {depth} ({(depth == 1 ? "direct" : $"{depth} hops away")}) ──");
                lastDepth = depth;
            }

            Console.WriteLine($"    [{depType}] {depName}  ({refTypes})");
            dependentCount++;
        }
        depReader.Close();

        if (dependentCount == 0)
        {
            Console.WriteLine("  No entities depend on this one.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Total: {dependentCount} dependent entities");
        }

        // Find what this entity depends on (entities where this is the SOURCE)
        Console.WriteLine();
        Console.WriteLine("═══ ENTITIES THIS DEPENDS ON ═════════════════════════════════════════");
        Console.WriteLine();

        using var reqCmd = db.CreateCommand();
        reqCmd.CommandText = @"
            SELECT 
                d.definition_type, d.name, tr.path_depth, tr.reference_types
            FROM transitive_references tr
            JOIN xml_definitions d ON tr.target_def_id = d.id
            WHERE tr.source_def_id = $entityId
            ORDER BY tr.path_depth, d.definition_type, d.name
            LIMIT 100";
        reqCmd.Parameters.AddWithValue("$entityId", entityId);

        using var reqReader = reqCmd.ExecuteReader();
        int requirementCount = 0;
        lastDepth = -1;

        while (reqReader.Read())
        {
            var reqType = reqReader.GetString(0);
            var reqName = reqReader.GetString(1);
            var depth = reqReader.GetInt32(2);
            var refTypes = reqReader.GetString(3);

            if (depth != lastDepth)
            {
                Console.WriteLine($"  ── Depth {depth} ({(depth == 1 ? "direct" : $"{depth} hops away")}) ──");
                lastDepth = depth;
            }

            Console.WriteLine($"    [{reqType}] {reqName}  ({refTypes})");
            requirementCount++;
        }
        reqReader.Close();

        if (requirementCount == 0)
        {
            Console.WriteLine("  This entity has no dependencies.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Total: {requirementCount} required entities");
        }

        // Check for mod conflicts involving this entity
        Console.WriteLine();
        Console.WriteLine("═══ MOD CONFLICTS INVOLVING THIS ENTITY ══════════════════════════════");
        Console.WriteLine();

        using var conflictCmd = db.CreateCommand();
        conflictCmd.CommandText = @"
            SELECT severity, pattern_id, pattern_name, explanation
            FROM mod_indirect_conflicts
            WHERE shared_entity_id = $entityId
            ORDER BY 
                CASE severity WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                pattern_id
            LIMIT 20";
        conflictCmd.Parameters.AddWithValue("$entityId", entityId);

        using var conflictReader = conflictCmd.ExecuteReader();
        int conflictCount = 0;

        while (conflictReader.Read())
        {
            var severity = conflictReader.GetString(0).ToUpper();
            var patternId = conflictReader.GetString(1);
            var patternName = conflictReader.IsDBNull(2) ? "" : conflictReader.GetString(2);
            var explanation = conflictReader.IsDBNull(3) ? "" : conflictReader.GetString(3);

            ConsoleColor color = severity switch
            {
                "HIGH" => ConsoleColor.Red,
                "MEDIUM" => ConsoleColor.Yellow,
                _ => ConsoleColor.DarkGray
            };

            Console.ForegroundColor = color;
            Console.Write($"  [{severity}] ");
            Console.ResetColor();
            Console.WriteLine($"{patternId}: {patternName}");
            Console.WriteLine($"         {explanation}");
            Console.WriteLine();
            conflictCount++;
        }
        conflictReader.Close();

        if (conflictCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✓ No mod conflicts involving this entity.");
            Console.ResetColor();
        }

        return 0;
    }
}
