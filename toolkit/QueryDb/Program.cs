// 7D2D Toolkit - Database Query Tool
// Usage:
//   dotnet run -- <database.db>                        Show summary statistics
//   dotnet run -- <database.db> sql "SELECT ..."       Run custom SQL query
//   dotnet run -- <database.db> callers <method>       Find all callers of a method
//   dotnet run -- <database.db> callees <method>       Find all methods called by a method
//   dotnet run -- <database.db> search <keyword>       Full-text search in method bodies
//   dotnet run -- <database.db> chain <from> <to>      Find call path between methods
//   dotnet run -- <database.db> implementations <method>  Find all implementations/overrides
//   dotnet run -- <database.db> compat                 Check mod compatibility
//   dotnet run -- <database.db> perf                   Performance analysis of game code

using System.Text.Json;
using Microsoft.Data.Sqlite;

class Program
{
    static SqliteConnection? _conn;
    static bool _jsonOutput = false;
    
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var dbPath = args[0];
        if (!File.Exists(dbPath))
        {
            Console.WriteLine($"Database not found: {dbPath}");
            return 1;
        }

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        // Check for --json flag
        var argList = args.ToList();
        if (argList.Contains("--json"))
        {
            _jsonOutput = true;
            argList.Remove("--json");
            args = argList.ToArray();
        }

        if (args.Length == 1)
        {
            ShowSummary();
            return 0;
        }

        var command = args[1].ToLower();
        var cmdArgs = args.Skip(2).ToArray();

        return command switch
        {
            "sql" => RunCustomSql(cmdArgs),
            "callers" => FindCallers(cmdArgs),
            "callees" => FindCallees(cmdArgs),
            "search" => SearchBodies(cmdArgs),
            "chain" => FindChain(cmdArgs),
            "implementations" or "impl" => FindImplementations(cmdArgs),
            "compat" => CheckCompatibility(cmdArgs),
            "perf" or "performance" => PerformanceAnalysis(cmdArgs),
            "xml" => XmlQuery(cmdArgs),
            _ => UnknownCommand(command)
        };
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"7D2D Toolkit - Database Query Tool

Usage:
  QueryDb <database.db>                           Show summary statistics
  QueryDb <database.db> sql ""SELECT ...""          Run custom SQL query
  QueryDb <database.db> callers <method>          Find all callers of a method
  QueryDb <database.db> callees <method>          Find all methods called by a method  
  QueryDb <database.db> search <keyword>          Full-text search in method bodies
  QueryDb <database.db> chain <from> <to>         Find call path between methods
  QueryDb <database.db> impl <method>             Find all implementations/overrides
  QueryDb <database.db> compat                    Check mod compatibility (conflicts)
  QueryDb <database.db> perf [category]           Performance analysis of game code
  QueryDb <database.db> xml <item-or-property>    Query XML definitions

Options:
  --json                                          Output results as JSON

Examples:
  QueryDb callgraph.db callers GetItemCount
  QueryDb callgraph.db callees ""PlayerMoveController.Update""
  QueryDb callgraph.db search ""GetComponent""
  QueryDb callgraph.db chain Craft DecItem
  QueryDb callgraph.db impl CanReload
  QueryDb callgraph.db perf updates
  QueryDb callgraph.db xml gunPistol
");
    }

    static int UnknownCommand(string cmd)
    {
        Console.WriteLine($"Unknown command: {cmd}");
        PrintUsage();
        return 1;
    }

    // ========================================================================
    // SUMMARY - Default view showing database statistics
    // ========================================================================
    static void ShowSummary()
    {
        Console.WriteLine($"Database: {_conn!.DataSource}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("Table Counts:");
        RunQuery(@"
            SELECT 'Types' as tbl, COUNT(*) as cnt FROM types 
            UNION ALL SELECT 'Methods', COUNT(*) FROM methods 
            UNION ALL SELECT 'Internal Calls', COUNT(*) FROM calls 
            UNION ALL SELECT 'External Calls', COUNT(*) FROM external_calls 
            UNION ALL SELECT 'Method Bodies (FTS)', COUNT(*) FROM method_bodies
            UNION ALL SELECT 'XML Definitions', COUNT(*) FROM xml_definitions
            UNION ALL SELECT 'XML Property Access', COUNT(*) FROM xml_property_access
            UNION ALL SELECT 'Mods', COUNT(*) FROM mods
            UNION ALL SELECT 'Harmony Patches', COUNT(*) FROM harmony_patches
        ");

        Console.WriteLine("\nTypes by Assembly:");
        RunQuery("SELECT assembly, COUNT(*) as count FROM types GROUP BY assembly ORDER BY count DESC");

        Console.WriteLine("\nTop 10 External Call Targets:");
        RunQuery(@"SELECT target_assembly || '.' || target_type || '.' || target_method as target, 
                         COUNT(*) as calls 
                  FROM external_calls 
                  GROUP BY target 
                  ORDER BY calls DESC LIMIT 10");

        Console.WriteLine("\nMods in Database:");
        RunQuery(@"
            SELECT m.name, 
                   (SELECT COUNT(*) FROM harmony_patches hp WHERE hp.mod_name = m.name) as patches
            FROM mods m
        ");

        Console.WriteLine("\nUse 'QueryDb <db> help' for available commands.");
    }

    // ========================================================================
    // SQL - Run custom SQL query
    // ========================================================================
    static int RunCustomSql(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> sql \"SELECT ...\"");
            return 1;
        }
        var sql = string.Join(" ", args);
        Console.WriteLine($"Running: {sql}\n");
        RunQuery(sql);
        return 0;
    }

    // ========================================================================
    // CALLERS - Find all methods that call a given method
    // ========================================================================
    static int FindCallers(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> callers <method-name>");
            Console.WriteLine("       QueryDb <db> callers \"TypeName.MethodName\"");
            return 1;
        }

        var methodName = string.Join(" ", args);
        
        // First, find matching methods
        var methods = FindMatchingMethods(methodName);
        if (methods.Count == 0)
        {
            Console.WriteLine($"No methods found matching: {methodName}");
            return 1;
        }

        if (methods.Count > 1 && !methodName.Contains("."))
        {
            Console.WriteLine($"Found {methods.Count} methods matching '{methodName}':");
            foreach (var m in methods.Take(20))
            {
                Console.WriteLine($"  {m.FullName} ({m.Signature}) - {m.FilePath}:{m.Line}");
            }
            if (methods.Count > 20)
                Console.WriteLine($"  ... and {methods.Count - 20} more. Be more specific.");
            Console.WriteLine("\nTip: Use \"TypeName.MethodName\" to narrow down.");
            return 0;
        }

        // Find callers for each matching method
        foreach (var method in methods)
        {
            Console.WriteLine($"\n═══ Callers of {method.FullName} ═══");
            Console.WriteLine($"    Defined at: {method.FilePath}:{method.Line}");
            Console.WriteLine();
            
            // Internal callers (from game code)
            RunQuery($@"
                SELECT t.name || '.' || m.name as caller, 
                       c.file_path, c.line_number
                FROM calls c
                JOIN methods m ON c.caller_id = m.id
                JOIN types t ON m.type_id = t.id
                WHERE c.callee_id = {method.Id}
                ORDER BY t.name, m.name
                LIMIT 50
            ");
        }
        return 0;
    }

    // ========================================================================
    // CALLEES - Find all methods called by a given method
    // ========================================================================
    static int FindCallees(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> callees <method-name>");
            Console.WriteLine("       QueryDb <db> callees \"TypeName.MethodName\"");
            return 1;
        }

        var methodName = string.Join(" ", args);
        var methods = FindMatchingMethods(methodName);
        
        if (methods.Count == 0)
        {
            Console.WriteLine($"No methods found matching: {methodName}");
            return 1;
        }

        if (methods.Count > 1 && !methodName.Contains("."))
        {
            Console.WriteLine($"Found {methods.Count} methods matching '{methodName}':");
            foreach (var m in methods.Take(20))
            {
                Console.WriteLine($"  {m.FullName} ({m.Signature})");
            }
            if (methods.Count > 20)
                Console.WriteLine($"  ... and {methods.Count - 20} more.");
            return 0;
        }

        foreach (var method in methods)
        {
            Console.WriteLine($"\n═══ Calls made by {method.FullName} ═══");
            Console.WriteLine($"    Defined at: {method.FilePath}:{method.Line}");
            
            // Internal calls
            Console.WriteLine("\n  Internal calls (to game code):");
            RunQuery($@"
                SELECT t.name || '.' || m.name as callee,
                       c.line_number as at_line
                FROM calls c
                JOIN methods m ON c.callee_id = m.id
                JOIN types t ON m.type_id = t.id
                WHERE c.caller_id = {method.Id}
                ORDER BY c.line_number
                LIMIT 50
            ");

            // External calls
            Console.WriteLine("\n  External calls (to Unity/BCL):");
            RunQuery($@"
                SELECT target_type || '.' || target_method as callee,
                       line_number as at_line
                FROM external_calls
                WHERE caller_id = {method.Id}
                ORDER BY line_number
                LIMIT 30
            ");
        }
        return 0;
    }

    // ========================================================================
    // SEARCH - Full-text search in method bodies
    // ========================================================================
    static int SearchBodies(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> search <keyword>");
            Console.WriteLine("       QueryDb <db> search \"GetComponent AND Update\"");
            return 1;
        }

        var searchTerm = string.Join(" ", args);
        Console.WriteLine($"Searching for: {searchTerm}\n");

        RunQuery($@"
            SELECT t.name || '.' || m.name as method,
                   m.file_path, m.line_number,
                   snippet(method_bodies, 1, '>>>', '<<<', '...', 20) as context
            FROM method_bodies mb
            JOIN methods m ON mb.method_id = m.id
            JOIN types t ON m.type_id = t.id
            WHERE method_bodies MATCH '{searchTerm.Replace("'", "''")}'
            ORDER BY rank
            LIMIT 30
        ");
        return 0;
    }

    // ========================================================================
    // CHAIN - Find call path between two methods using recursive CTE
    // ========================================================================
    static int FindChain(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: QueryDb <db> chain <from-method> <to-method>");
            Console.WriteLine("       QueryDb <db> chain Craft DecItem");
            return 1;
        }

        var fromName = args[0];
        var toName = args[1];
        
        var fromMethods = FindMatchingMethods(fromName);
        var toMethods = FindMatchingMethods(toName);

        if (fromMethods.Count == 0)
        {
            Console.WriteLine($"No methods found matching: {fromName}");
            return 1;
        }
        if (toMethods.Count == 0)
        {
            Console.WriteLine($"No methods found matching: {toName}");
            return 1;
        }

        // Take first match for simplicity (could prompt user to choose)
        var fromMethod = fromMethods.First();
        var toMethod = toMethods.First();

        Console.WriteLine($"Finding path from {fromMethod.FullName} to {toMethod.FullName}...\n");

        // Use recursive CTE for BFS path finding
        // Limited to depth 10 to prevent runaway queries
        var sql = $@"
            WITH RECURSIVE call_path(method_id, path, depth) AS (
                -- Start from the source method
                SELECT {fromMethod.Id}, '{fromMethod.FullName}', 0
                
                UNION ALL
                
                -- Recursively find callees
                SELECT c.callee_id,
                       cp.path || ' -> ' || t.name || '.' || m.name,
                       cp.depth + 1
                FROM call_path cp
                JOIN calls c ON c.caller_id = cp.method_id
                JOIN methods m ON c.callee_id = m.id
                JOIN types t ON m.type_id = t.id
                WHERE cp.depth < 10
                  AND cp.path NOT LIKE '%' || t.name || '.' || m.name || '%'  -- Prevent cycles
            )
            SELECT path, depth
            FROM call_path
            WHERE method_id = {toMethod.Id}
            ORDER BY depth
            LIMIT 5
        ";

        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        var found = false;
        while (reader.Read())
        {
            found = true;
            var path = reader.GetString(0);
            var depth = reader.GetInt32(1);
            Console.WriteLine($"Path (depth {depth}):");
            var steps = path.Split(" -> ");
            for (int i = 0; i < steps.Length; i++)
            {
                Console.WriteLine($"  {new string(' ', i * 2)}{(i > 0 ? "└─ " : "")}{steps[i]}");
            }
            Console.WriteLine();
        }

        if (!found)
        {
            Console.WriteLine("No path found within depth 10.");
            Console.WriteLine("The methods may not be connected, or the path is too long.");
        }

        return 0;
    }

    // ========================================================================
    // IMPLEMENTATIONS - Find all implementations/overrides of a method
    // ========================================================================
    static int FindImplementations(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> impl <method-name>");
            Console.WriteLine("       Finds all classes that implement/override a method");
            return 1;
        }

        var methodName = string.Join(" ", args);
        Console.WriteLine($"Finding all implementations of '{methodName}':\n");

        // Find all methods with this name, grouped by declaring type
        RunQuery($@"
            SELECT t.name as type, 
                   t.base_type,
                   m.signature,
                   CASE WHEN m.is_override THEN 'override' 
                        WHEN m.is_virtual THEN 'virtual'
                        WHEN m.is_abstract THEN 'abstract'
                        ELSE '' END as modifier,
                   m.file_path || ':' || m.line_number as location
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.name = '{methodName.Replace("'", "''")}'
            ORDER BY t.base_type, t.name
        ");

        // Also show inheritance hierarchy if we find multiple
        Console.WriteLine("\nInheritance relationships:");
        RunQuery($@"
            SELECT DISTINCT t.name as type, t.base_type as inherits_from
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.name = '{methodName.Replace("'", "''")}'
              AND t.base_type IS NOT NULL
            ORDER BY t.base_type, t.name
        ");

        return 0;
    }

    // ========================================================================
    // COMPAT - Check mod compatibility, detect conflicts
    // ========================================================================
    static int CheckCompatibility(string[] args)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("                     MOD COMPATIBILITY CHECK                        ");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");

        // List all mods
        Console.WriteLine("Mods in database:");
        RunQuery("SELECT name, version, author FROM mods");

        // Check for Harmony patch conflicts (same method patched by multiple mods)
        Console.WriteLine("\n═══ HARMONY PATCH CONFLICTS ═══");
        Console.WriteLine("Methods patched by multiple mods (potential conflicts):\n");
        
        RunQuery(@"
            SELECT target_type || '.' || target_method as target,
                   GROUP_CONCAT(DISTINCT mod_name) as mods,
                   GROUP_CONCAT(DISTINCT patch_type) as patch_types,
                   COUNT(DISTINCT mod_name) as mod_count
            FROM harmony_patches
            GROUP BY target_type, target_method
            HAVING COUNT(DISTINCT mod_name) > 1
            ORDER BY mod_count DESC
        ");

        // Check for XML conflicts (same xpath modified by multiple mods)
        Console.WriteLine("\n═══ XML CONFLICTS ═══");
        Console.WriteLine("XML paths modified by multiple mods:\n");
        
        RunQuery(@"
            SELECT file_name || ':' || xpath as target,
                   GROUP_CONCAT(DISTINCT mod_name) as mods,
                   GROUP_CONCAT(DISTINCT operation) as operations,
                   COUNT(DISTINCT mod_name) as mod_count
            FROM xml_changes
            GROUP BY file_name, xpath
            HAVING COUNT(DISTINCT mod_name) > 1
            ORDER BY mod_count DESC
        ");

        // Load order suggestions based on patch types
        Console.WriteLine("\n═══ LOAD ORDER SUGGESTIONS ═══");
        Console.WriteLine("Transpilers should generally load before Prefix/Postfix on same method:\n");
        
        RunQuery(@"
            SELECT target_type || '.' || target_method as method,
                   GROUP_CONCAT(mod_name || ' [' || patch_type || ']', ', ') as patches
            FROM harmony_patches
            WHERE target_type || '.' || target_method IN (
                SELECT target_type || '.' || target_method 
                FROM harmony_patches 
                WHERE patch_type = 'Transpiler'
            )
            GROUP BY target_type, target_method
            HAVING COUNT(DISTINCT patch_type) > 1
            ORDER BY target_type, target_method
        ");

        // Summary
        Console.WriteLine("\n═══ SUMMARY ═══");
        RunQuery(@"
            SELECT 
                (SELECT COUNT(DISTINCT target_type || target_method) 
                 FROM harmony_patches 
                 GROUP BY target_type, target_method 
                 HAVING COUNT(DISTINCT mod_name) > 1) as harmony_conflicts,
                (SELECT COUNT(DISTINCT file_name || xpath) 
                 FROM xml_changes 
                 GROUP BY file_name, xpath 
                 HAVING COUNT(DISTINCT mod_name) > 1) as xml_conflicts
        ");

        return 0;
    }

    // ========================================================================
    // PERF - Performance analysis of game code
    // ========================================================================
    static int PerformanceAnalysis(string[] args)
    {
        var category = args.Length > 0 ? args[0].ToLower() : "all";

        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("                    PERFORMANCE ANALYSIS                            ");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");

        if (category is "all" or "updates")
        {
            Console.WriteLine("═══ LARGEST UPDATE METHODS (by code size) ═══\n");
            RunQuery(@"
                SELECT t.full_name || '.' || m.name as method, 
                       length(mb.body) as body_size,
                       (SELECT COUNT(*) FROM calls c WHERE c.caller_id = m.id) as internal_calls,
                       (SELECT COUNT(*) FROM external_calls ec WHERE ec.caller_id = m.id) as external_calls
                FROM methods m 
                JOIN types t ON m.type_id = t.id 
                JOIN method_bodies mb ON m.id = mb.method_id 
                WHERE m.name IN ('Update', 'LateUpdate', 'FixedUpdate') 
                ORDER BY body_size DESC 
                LIMIT 15
            ");
        }

        if (category is "all" or "getcomponent")
        {
            Console.WriteLine("\n═══ UPDATE METHODS WITH GetComponent CALLS ═══");
            Console.WriteLine("(GetComponent in Update is expensive - should be cached)\n");
            RunQuery(@"
                SELECT t.full_name || '.' || m.name as method, 
                       COUNT(*) as getcomponent_calls
                FROM methods m 
                JOIN types t ON m.type_id = t.id 
                JOIN external_calls ec ON m.id = ec.caller_id 
                WHERE m.name IN ('Update', 'LateUpdate', 'FixedUpdate')
                  AND ec.target_method = 'GetComponent'
                GROUP BY m.id
                ORDER BY getcomponent_calls DESC
                LIMIT 15
            ");
        }

        if (category is "all" or "find")
        {
            Console.WriteLine("\n═══ METHODS USING FindObjectsOfType ═══");
            Console.WriteLine("(Very expensive - O(n) scan of all objects)\n");
            RunQuery(@"
                SELECT t.full_name || '.' || m.name as method,
                       ec.target_method,
                       m.file_path || ':' || m.line_number as location
                FROM methods m 
                JOIN types t ON m.type_id = t.id 
                JOIN external_calls ec ON m.id = ec.caller_id 
                WHERE ec.target_method LIKE 'FindObject%'
                ORDER BY t.name, m.name
                LIMIT 20
            ");
        }

        if (category is "all" or "strings")
        {
            Console.WriteLine("\n═══ UPDATE METHODS WITH STRING ALLOCATIONS ═══");
            Console.WriteLine("(string.Format in Update causes GC pressure)\n");
            RunQuery(@"
                SELECT t.full_name || '.' || m.name as method
                FROM methods m 
                JOIN types t ON m.type_id = t.id 
                JOIN method_bodies mb ON m.id = mb.method_id 
                WHERE m.name IN ('Update', 'LateUpdate', 'FixedUpdate')
                  AND (mb.body LIKE '%string.Format%' 
                    OR mb.body LIKE '%String.Concat%'
                    OR mb.body LIKE '%+ ""%')
                LIMIT 20
            ");
        }

        if (category is "all" or "linq")
        {
            Console.WriteLine("\n═══ HOT METHODS WITH LINQ ═══");
            Console.WriteLine("(LINQ in hot paths causes allocations)\n");
            RunQuery(@"
                SELECT t.full_name || '.' || m.name as method
                FROM methods m 
                JOIN types t ON m.type_id = t.id 
                JOIN method_bodies mb ON m.id = mb.method_id 
                WHERE m.name IN ('Update', 'LateUpdate', 'FixedUpdate', 'Tick')
                  AND (mb.body LIKE '%.Where(%' 
                    OR mb.body LIKE '%.Select(%'
                    OR mb.body LIKE '%.FirstOrDefault(%'
                    OR mb.body LIKE '%.ToList(%'
                    OR mb.body LIKE '%.ToArray(%')
                LIMIT 20
            ");
        }

        Console.WriteLine("\n═══ CATEGORIES ═══");
        Console.WriteLine("Run with: perf <category>");
        Console.WriteLine("  all         - All analyses (default)");
        Console.WriteLine("  updates     - Largest Update methods");
        Console.WriteLine("  getcomponent - GetComponent in hot paths");
        Console.WriteLine("  find        - FindObjectsOfType usage");
        Console.WriteLine("  strings     - String allocations in Update");
        Console.WriteLine("  linq        - LINQ in hot methods");

        return 0;
    }

    // ========================================================================
    // XML - Query XML definitions
    // ========================================================================
    static int XmlQuery(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> xml <item-name>");
            Console.WriteLine("       QueryDb <db> xml <property-name>");
            return 1;
        }

        var query = string.Join(" ", args);
        
        // First try as element name
        Console.WriteLine($"═══ XML Definitions for '{query}' ═══\n");
        
        RunQuery($@"
            SELECT file_name, element_type, property_name, property_value
            FROM xml_definitions
            WHERE element_name = '{query.Replace("'", "''")}'
              AND property_name IS NOT NULL
            ORDER BY property_name
            LIMIT 50
        ");

        // Also show code that accesses this property
        Console.WriteLine($"\n═══ Code accessing property '{query}' ═══\n");
        RunQuery($@"
            SELECT t.name || '.' || m.name as method,
                   xpa.access_method,
                   xpa.file_path || ':' || xpa.line_number as location
            FROM xml_property_access xpa
            JOIN methods m ON xpa.method_id = m.id
            JOIN types t ON m.type_id = t.id
            WHERE xpa.property_name = '{query.Replace("'", "''")}'
            LIMIT 20
        ");

        return 0;
    }

    // ========================================================================
    // HELPERS
    // ========================================================================
    
    record MethodInfo(int Id, string FullName, string Signature, string? FilePath, int Line);
    
    static List<MethodInfo> FindMatchingMethods(string name)
    {
        var results = new List<MethodInfo>();
        using var cmd = _conn!.CreateCommand();
        
        // Check if name includes type (e.g., "PlayerMoveController.Update")
        if (name.Contains("."))
        {
            var parts = name.Split('.');
            var typeName = string.Join(".", parts.Take(parts.Length - 1));
            var methodName = parts.Last();
            
            cmd.CommandText = @"
                SELECT m.id, t.name || '.' || m.name, m.signature, m.file_path, m.line_number
                FROM methods m
                JOIN types t ON m.type_id = t.id
                WHERE m.name = @methodName 
                  AND (t.name = @typeName OR t.full_name = @typeName OR t.name LIKE '%' || @typeName)
            ";
            cmd.Parameters.AddWithValue("@methodName", methodName);
            cmd.Parameters.AddWithValue("@typeName", typeName);
        }
        else
        {
            cmd.CommandText = @"
                SELECT m.id, t.name || '.' || m.name, m.signature, m.file_path, m.line_number
                FROM methods m
                JOIN types t ON m.type_id = t.id
                WHERE m.name = @name
            ";
            cmd.Parameters.AddWithValue("@name", name);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new MethodInfo(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            ));
        }
        return results;
    }

    static void RunQuery(string sql)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        
        try
        {
            using var reader = cmd.ExecuteReader();
            
            if (_jsonOutput)
            {
                var results = new List<Dictionary<string, object?>>();
                var cols = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < cols.Count; i++)
                    {
                        row[cols[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    results.Add(row);
                }
                Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                // Print column headers
                var cols = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                Console.WriteLine("  " + string.Join(" | ", cols));
                Console.WriteLine("  " + string.Join("-+-", cols.Select(c => new string('-', Math.Max(c.Length, 10)))));
                
                var rowCount = 0;
                while (reader.Read())
                {
                    var values = cols.Select((c, i) => 
                        reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "").ToList();
                    Console.WriteLine("  " + string.Join(" | ", values));
                    rowCount++;
                }
                
                if (rowCount == 0)
                    Console.WriteLine("  (no results)");
            }
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"SQL Error: {ex.Message}");
        }
    }
}
