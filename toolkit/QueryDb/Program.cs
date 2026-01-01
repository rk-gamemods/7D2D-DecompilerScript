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
RunQuery(conn, "SELECT 'Types' as tbl, COUNT(*) as cnt FROM types UNION ALL SELECT 'Methods', COUNT(*) FROM methods UNION ALL SELECT 'Internal Calls', COUNT(*) FROM calls UNION ALL SELECT 'External Calls', COUNT(*) FROM external_calls UNION ALL SELECT 'Method Bodies (FTS)', COUNT(*) FROM method_bodies");

Console.WriteLine();

// Per-assembly breakdown
Console.WriteLine("Types by Assembly:");
RunQuery(conn, "SELECT assembly, COUNT(*) as count FROM types GROUP BY assembly ORDER BY count DESC");

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
