// Quick query tool to verify database contents
using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : "callgraph_full.db";
if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database not found: {dbPath}");
    return;
}

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

Console.WriteLine($"Database: {dbPath}");
Console.WriteLine("═══════════════════════════════════════════════════════════════════");
Console.WriteLine();

// Count summary
Console.WriteLine("Table Counts:");
RunQuery(conn, @"
    SELECT 'Types' as tbl, COUNT(*) as cnt FROM types 
    UNION ALL SELECT 'Methods', COUNT(*) FROM methods 
    UNION ALL SELECT 'Internal Calls', COUNT(*) FROM calls 
    UNION ALL SELECT 'External Calls', COUNT(*) FROM external_calls 
    UNION ALL SELECT 'Method Bodies (FTS)', COUNT(*) FROM method_bodies
    UNION ALL SELECT 'XML Definitions', COUNT(*) FROM xml_definitions
");

Console.WriteLine();

// Per-assembly breakdown
Console.WriteLine("Types by Assembly:");
RunQuery(conn, "SELECT assembly, COUNT(*) as count FROM types GROUP BY assembly ORDER BY count DESC");

Console.WriteLine();

// XML definitions by file
Console.WriteLine("XML Definitions by File (top 10):");
RunQuery(conn, "SELECT file_name, COUNT(*) as definitions FROM xml_definitions GROUP BY file_name ORDER BY definitions DESC LIMIT 10");

Console.WriteLine();

// XML element types
Console.WriteLine("XML Element Types (top 10):");
RunQuery(conn, "SELECT element_type, COUNT(*) as count FROM xml_definitions GROUP BY element_type ORDER BY count DESC LIMIT 10");

Console.WriteLine();

// Top external call targets
Console.WriteLine("Top 10 External Call Targets (Unity/BCL boundary crossings):");
RunQuery(conn, "SELECT target_assembly || '.' || target_type || '.' || target_method as target, COUNT(*) as calls FROM external_calls GROUP BY target ORDER BY calls DESC LIMIT 10");

Console.WriteLine();

// Sample query: who calls Transform.SetParent?
Console.WriteLine("Sample Query - Who calls UnityEngine.Transform.SetParent?");
RunQuery(conn, @"
    SELECT m.name as caller_method, t.name as caller_type, ec.file_path, ec.line_number
    FROM external_calls ec
    JOIN methods m ON ec.caller_id = m.id
    JOIN types t ON m.type_id = t.id
    WHERE ec.target_type LIKE '%Transform%' AND ec.target_method = 'SetParent'
    LIMIT 10
");

Console.WriteLine();

// Sample XML query: properties on an item
Console.WriteLine("Sample Query - Properties of 'gunHandgunT1Pistol':");
RunQuery(conn, @"
    SELECT property_name, property_value, property_class
    FROM xml_definitions
    WHERE element_name = 'gunHandgunT1Pistol' AND property_name IS NOT NULL
    LIMIT 15
");

Console.WriteLine();

// XML Property Access examples
Console.WriteLine("Sample Query - Code that reads 'Stacknumber' property:");
RunQuery(conn, @"
    SELECT m.name as method, t.name as type, xpa.access_method, xpa.file_path, xpa.line_number
    FROM xml_property_access xpa
    JOIN methods m ON xpa.method_id = m.id
    JOIN types t ON m.type_id = t.id
    WHERE xpa.property_name = 'Stacknumber'
    LIMIT 10
");

Console.WriteLine();

// Most accessed XML properties
Console.WriteLine("Top 15 Most Accessed XML Properties:");
RunQuery(conn, @"
    SELECT property_name, COUNT(*) as access_count, GROUP_CONCAT(DISTINCT access_method) as methods
    FROM xml_property_access
    GROUP BY property_name
    ORDER BY access_count DESC
    LIMIT 15
");

Console.WriteLine();

// Mod summary
Console.WriteLine("Mods in Database:");
RunQuery(conn, @"
    SELECT m.name, 
           COUNT(DISTINCT mt.id) as types, 
           COUNT(DISTINCT mm.id) as methods,
           (SELECT COUNT(*) FROM harmony_patches hp WHERE hp.mod_method_id IN 
            (SELECT mm2.id FROM mod_methods mm2 JOIN mod_types mt2 ON mm2.mod_type_id = mt2.id WHERE mt2.mod_id = m.id)) as patches
    FROM mods m
    LEFT JOIN mod_types mt ON mt.mod_id = m.id
    LEFT JOIN mod_methods mm ON mm.mod_type_id = mt.id
    GROUP BY m.id
");

Console.WriteLine();

// Harmony patches
Console.WriteLine("Harmony Patches Found:");
RunQuery(conn, @"
    SELECT hp.target_type, hp.target_method, hp.patch_type,
           mm.name as patch_method, mt.name as patch_class
    FROM harmony_patches hp
    LEFT JOIN mod_methods mm ON hp.mod_method_id = mm.id
    LEFT JOIN mod_types mt ON mm.mod_type_id = mt.id
    LIMIT 15
");

void RunQuery(SqliteConnection conn, string sql)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    using var reader = cmd.ExecuteReader();
    
    // Print column headers
    var cols = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
    Console.WriteLine("  " + string.Join(" | ", cols));
    Console.WriteLine("  " + string.Join("-+-", cols.Select(c => new string('-', Math.Max(c.Length, 10)))));
    
    // Print rows
    while (reader.Read())
    {
        var values = cols.Select((c, i) => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "").ToList();
        Console.WriteLine("  " + string.Join(" | ", values));
    }
}
