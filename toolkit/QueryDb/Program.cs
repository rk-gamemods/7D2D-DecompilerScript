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
            "flow" => TraceFlow(cmdArgs),
            "events" => ShowEvents(cmdArgs),
            "effective" => ShowEffectiveBehavior(cmdArgs),
            "qa" or "analyze" => RunModQa(cmdArgs, dbPath),
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
  QueryDb <database.db> flow <event-or-method>    Trace behavioral flow (events + patches)
  QueryDb <database.db> events <event-name>       Show event subscriptions and fires
  QueryDb <database.db> effective <method>        Show effective behavior with patches
  QueryDb <database.db> qa <mod-path>             Run automated QA analysis on a mod

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
  QueryDb callgraph.db flow DragAndDropItemChanged
  QueryDb callgraph.db events DragAndDropItemChanged
  QueryDb callgraph.db effective ""XUiM_PlayerInventory.GetItemCount""
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
            UNION ALL SELECT 'Event Declarations', COUNT(*) FROM event_declarations
            UNION ALL SELECT 'Event Subscriptions', COUNT(*) FROM event_subscriptions
            UNION ALL SELECT 'Event Fires', COUNT(*) FROM event_fires
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
    // FLOW - Trace behavioral flow through events and patches
    // ========================================================================
    static int TraceFlow(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> flow <event-or-method-name>");
            Console.WriteLine("       Traces the complete behavioral flow including events and patches");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  flow DragAndDropItemChanged       - Trace what happens when this event fires");
            Console.WriteLine("  flow HandleLootSlotChangedEvent   - Trace from this method trigger");
            Console.WriteLine("  flow GetItemCount                 - Trace effective behavior of patched method");
            return 1;
        }

        var query = string.Join(" ", args);
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine($"          BEHAVIORAL FLOW ANALYSIS: {query}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");

        // Check if tables exist
        if (!TableExists("event_subscriptions"))
        {
            Console.WriteLine("⚠ Event tables not found. Run extraction with event tracking enabled.");
            Console.WriteLine("  The flow command requires: event_subscriptions, event_fires tables.");
            Console.WriteLine();
        }

        // Step 1: Check if this is an event
        Console.WriteLine("═══ EVENT ANALYSIS ═══\n");
        
        Console.WriteLine($"Event declarations matching '{query}':");
        RunQuery($@"
            SELECT owning_type, event_name, delegate_type, file_path || ':' || line_number as location
            FROM event_declarations
            WHERE event_name LIKE '%{query.Replace("'", "''")}%'
            ORDER BY owning_type
        ");

        Console.WriteLine($"\nSubscriptions to '{query}':");
        RunQuery($@"
            SELECT subscriber_type, 
                   event_owner_type || '.' || event_name as event,
                   handler_method,
                   CASE WHEN is_mod THEN '(MOD)' ELSE '' END as source,
                   file_path || ':' || line_number as location
            FROM event_subscriptions
            WHERE event_name LIKE '%{query.Replace("'", "''")}%'
            ORDER BY event_owner_type, subscriber_type
        ");

        Console.WriteLine($"\nWhere '{query}' is fired:");
        RunQuery($@"
            SELECT firing_type, 
                   event_owner_type || '.' || event_name as event,
                   fire_method,
                   CASE WHEN is_conditional THEN 'conditional' ELSE 'always' END as invoke_type,
                   CASE WHEN is_mod THEN '(MOD)' ELSE '' END as source,
                   file_path || ':' || line_number as location
            FROM event_fires
            WHERE event_name LIKE '%{query.Replace("'", "''")}%'
            ORDER BY firing_type
        ");

        // Step 2: Check for Harmony patches related to this
        Console.WriteLine("\n═══ HARMONY PATCHES ═══\n");
        
        Console.WriteLine($"Patches targeting methods related to '{query}':");
        RunQuery($@"
            SELECT hp.mod_name, 
                   hp.patch_type,
                   hp.target_type || '.' || hp.target_method as target,
                   hp.priority,
                   hp.patch_class || '.' || hp.patch_method as patch
            FROM harmony_patches hp
            WHERE hp.target_method LIKE '%{query.Replace("'", "''")}%'
               OR hp.target_type LIKE '%{query.Replace("'", "''")}%'
            ORDER BY hp.target_type, hp.target_method, hp.priority
        ");

        // Step 3: Show methods that match (might be the trigger)
        Console.WriteLine("\n═══ METHOD MATCHES ═══\n");
        
        var methods = FindMatchingMethods(query);
        if (methods.Count > 0)
        {
            Console.WriteLine($"Found {methods.Count} method(s) matching '{query}':\n");
            foreach (var m in methods.Take(10))
            {
                Console.WriteLine($"  {m.FullName}");
                Console.WriteLine($"    Location: {m.FilePath}:{m.Line}");
                Console.WriteLine($"    Signature: {m.Signature}");
                
                // Check if this method has patches
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = $@"
                    SELECT mod_name, patch_type, priority 
                    FROM harmony_patches 
                    WHERE target_method = '{m.FullName.Split('.').Last()}' 
                      AND (target_type LIKE '%{m.FullName.Split('.').First()}%' 
                           OR target_type = '{m.FullName.Split('.').First()}')
                ";
                using var reader = cmd.ExecuteReader();
                var patches = new List<string>();
                while (reader.Read())
                {
                    patches.Add($"{reader.GetString(0)} ({reader.GetString(1)}, pri={reader.GetInt32(2)})");
                }
                if (patches.Count > 0)
                {
                    Console.WriteLine($"    Patches: {string.Join(", ", patches)}");
                }
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine($"No methods found matching '{query}'");
        }

        // Step 4: Build and display the flow
        Console.WriteLine("\n═══ BEHAVIORAL FLOW ═══\n");
        BuildFlowVisualization(query);

        return 0;
    }

    static void BuildFlowVisualization(string query)
    {
        // Find event subscriptions
        var subscriptions = new List<(string Subscriber, string Event, string Handler)>();
        using (var cmd = _conn!.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT subscriber_type, event_owner_type || '.' || event_name, handler_method
                FROM event_subscriptions
                WHERE event_name LIKE '%{query.Replace("'", "''")}%'
            ";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                subscriptions.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        // Find where event is fired
        var fires = new List<(string FiringType, string Event, bool IsMod)>();
        using (var cmd = _conn!.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT firing_type, event_owner_type || '.' || event_name, is_mod
                FROM event_fires
                WHERE event_name LIKE '%{query.Replace("'", "''")}%'
            ";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                fires.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
            }
        }

        // Build visualization
        if (fires.Count > 0 || subscriptions.Count > 0)
        {
            Console.WriteLine($"FLOW: {query}");
            Console.WriteLine();
            
            foreach (var fire in fires)
            {
                var modTag = fire.IsMod ? " [MOD]" : "";
                Console.WriteLine($"TRIGGER: {fire.FiringType}{modTag}");
                Console.WriteLine($"  │  fires event: {fire.Event}");
                Console.WriteLine("  │");
                Console.WriteLine("  └─► [SUBSCRIBERS]");
                
                var matchingSubs = subscriptions.Where(s => s.Event == fire.Event).ToList();
                for (int i = 0; i < matchingSubs.Count; i++)
                {
                    var sub = matchingSubs[i];
                    var prefix = i == matchingSubs.Count - 1 ? "      └─►" : "      ├─►";
                    Console.WriteLine($"{prefix} {sub.Subscriber}.{sub.Handler}");
                    
                    // Check if handler has patches
                    using var cmd = _conn!.CreateCommand();
                    cmd.CommandText = $@"
                        SELECT mod_name, patch_type 
                        FROM harmony_patches 
                        WHERE target_method = '{sub.Handler}'
                    ";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var patchPrefix = i == matchingSubs.Count - 1 ? "          " : "      │   ";
                        Console.WriteLine($"{patchPrefix}    └─► [{reader.GetString(1).ToUpper()}: {reader.GetString(0)}]");
                    }
                }
                Console.WriteLine();
            }

            // Check for patches on methods matching query
            using (var cmd = _conn!.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT target_type, target_method, mod_name, patch_type
                    FROM harmony_patches
                    WHERE target_method LIKE '%{query.Replace("'", "''")}%'
                ";
                using var reader = cmd.ExecuteReader();
                var hasPatches = false;
                while (reader.Read())
                {
                    if (!hasPatches)
                    {
                        Console.WriteLine("PATCHED METHODS:");
                        hasPatches = true;
                    }
                    Console.WriteLine($"  {reader.GetString(0)}.{reader.GetString(1)}");
                    Console.WriteLine($"    └─► [{reader.GetString(3).ToUpper()}: {reader.GetString(2)}]");
                }
            }
        }
        else
        {
            Console.WriteLine("No event flow data found. This might mean:");
            Console.WriteLine("  1. The query doesn't match any events in the database");
            Console.WriteLine("  2. Event extraction hasn't been run yet");
            Console.WriteLine("  3. Try a different search term");
        }
    }

    // ========================================================================
    // EVENTS - Show event subscriptions and fires
    // ========================================================================
    static int ShowEvents(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> events <event-name>");
            Console.WriteLine("       Shows all subscriptions and fires for an event");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  events DragAndDropItemChanged");
            Console.WriteLine("  events ItemsChanged");
            Console.WriteLine("  events OnOpen");
            return 1;
        }

        var eventName = string.Join(" ", args);
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine($"                    EVENT: {eventName}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");

        if (!TableExists("event_subscriptions"))
        {
            Console.WriteLine("⚠ Event tables not found. Run extraction with event tracking enabled.");
            return 1;
        }

        // Event declarations
        Console.WriteLine("═══ DECLARATIONS ═══\n");
        RunQuery($@"
            SELECT owning_type as type, 
                   event_name as event, 
                   delegate_type as delegate,
                   CASE WHEN is_public THEN 'public' ELSE 'private' END as visibility,
                   file_path || ':' || line_number as location
            FROM event_declarations
            WHERE event_name LIKE '%{eventName.Replace("'", "''")}%'
            ORDER BY owning_type
        ");

        // Who subscribes
        Console.WriteLine("\n═══ SUBSCRIBERS ═══");
        Console.WriteLine("(Who listens to this event)\n");
        RunQuery($@"
            SELECT subscriber_type as subscriber,
                   handler_method as handler,
                   event_owner_type || '.' || event_name as event,
                   subscription_type as op,
                   CASE WHEN is_mod THEN 'MOD' ELSE 'GAME' END as source,
                   file_path || ':' || line_number as location
            FROM event_subscriptions
            WHERE event_name LIKE '%{eventName.Replace("'", "''")}%'
            ORDER BY is_mod DESC, subscriber_type
        ");

        // Who fires
        Console.WriteLine("\n═══ FIRES ═══");
        Console.WriteLine("(Who triggers this event)\n");
        RunQuery($@"
            SELECT firing_type as type,
                   event_owner_type || '.' || event_name as event,
                   fire_method as method,
                   CASE WHEN is_conditional THEN 'conditional' ELSE 'always' END as mode,
                   CASE WHEN is_mod THEN 'MOD' ELSE 'GAME' END as source,
                   file_path || ':' || line_number as location
            FROM event_fires
            WHERE event_name LIKE '%{eventName.Replace("'", "''")}%'
            ORDER BY is_mod DESC, firing_type
        ");

        // Summary
        Console.WriteLine("\n═══ SUMMARY ═══\n");
        
        int subCount = 0, fireCount = 0, modFires = 0;
        using (var cmd = _conn!.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT 
                    (SELECT COUNT(*) FROM event_subscriptions WHERE event_name LIKE '%{eventName.Replace("'", "''")}%'),
                    (SELECT COUNT(*) FROM event_fires WHERE event_name LIKE '%{eventName.Replace("'", "''")}%'),
                    (SELECT COUNT(*) FROM event_fires WHERE event_name LIKE '%{eventName.Replace("'", "''")}%' AND is_mod = 1)
            ";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                subCount = reader.GetInt32(0);
                fireCount = reader.GetInt32(1);
                modFires = reader.GetInt32(2);
            }
        }
        
        Console.WriteLine($"  Subscribers: {subCount}");
        Console.WriteLine($"  Fire points: {fireCount} ({modFires} from mods)");
        
        if (modFires > 0)
        {
            Console.WriteLine($"\n  ✓ Event is fired by mod code - this enables reactive patterns");
        }

        return 0;
    }

    // ========================================================================
    // EFFECTIVE - Show effective behavior of a method with patches
    // ========================================================================
    static int ShowEffectiveBehavior(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> effective <method-name>");
            Console.WriteLine("       Shows the effective behavior after all patches applied");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  effective GetItemCount");
            Console.WriteLine("  effective \"XUiM_PlayerInventory.GetItemCount\"");
            Console.WriteLine("  effective HandleUpdatingCurrent");
            return 1;
        }

        var methodName = string.Join(" ", args);
        var methods = FindMatchingMethods(methodName);

        if (methods.Count == 0)
        {
            Console.WriteLine($"No methods found matching: {methodName}");
            return 1;
        }

        foreach (var method in methods.Take(5))
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════════════");
            Console.WriteLine($"    EFFECTIVE BEHAVIOR: {method.FullName}");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");

            Console.WriteLine($"Location: {method.FilePath}:{method.Line}");
            Console.WriteLine($"Signature: {method.Signature}");
            Console.WriteLine();

            // Check for patches
            Console.WriteLine("═══ PATCHES APPLIED ═══\n");
            
            var methodShortName = method.FullName.Contains('.') 
                ? method.FullName.Split('.').Last() 
                : method.FullName;
            var typeName = method.FullName.Contains('.') 
                ? method.FullName.Split('.').First() 
                : "";

            var patches = new List<(string Mod, string Type, int Priority, string PatchClass)>();
            using (var cmd = _conn!.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT mod_name, patch_type, priority, patch_class
                    FROM harmony_patches
                    WHERE target_method = @method
                      AND (target_type LIKE '%' || @type || '%' OR @type = '')
                    ORDER BY priority DESC, patch_type
                ";
                cmd.Parameters.AddWithValue("@method", methodShortName);
                cmd.Parameters.AddWithValue("@type", typeName);
                
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    patches.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetInt32(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3)
                    ));
                }
            }

            if (patches.Count == 0)
            {
                Console.WriteLine("  (no patches found - vanilla behavior unchanged)");
            }
            else
            {
                // Group by type and show execution order
                var prefixes = patches.Where(p => p.Type == "Prefix").OrderByDescending(p => p.Priority).ToList();
                var postfixes = patches.Where(p => p.Type == "Postfix").OrderBy(p => p.Priority).ToList();
                var transpilers = patches.Where(p => p.Type == "Transpiler").ToList();

                Console.WriteLine("  Execution order:");
                Console.WriteLine();

                if (prefixes.Any())
                {
                    Console.WriteLine("  ┌─ PREFIXES (run before original)");
                    foreach (var p in prefixes)
                    {
                        Console.WriteLine($"  │  Priority {p.Priority}: {p.Mod}");
                        if (!string.IsNullOrEmpty(p.PatchClass))
                            Console.WriteLine($"  │      Class: {p.PatchClass}");
                    }
                    Console.WriteLine("  │");
                }

                if (transpilers.Any())
                {
                    Console.WriteLine("  ├─ TRANSPILERS (modify IL code)");
                    foreach (var t in transpilers)
                    {
                        Console.WriteLine($"  │  {t.Mod}");
                    }
                    Console.WriteLine("  │");
                }

                Console.WriteLine("  ├─► [ORIGINAL METHOD RUNS]");
                Console.WriteLine("  │");

                if (postfixes.Any())
                {
                    Console.WriteLine("  └─ POSTFIXES (run after original)");
                    foreach (var p in postfixes)
                    {
                        Console.WriteLine($"     Priority {p.Priority}: {p.Mod}");
                        if (!string.IsNullOrEmpty(p.PatchClass))
                            Console.WriteLine($"         Class: {p.PatchClass}");
                    }
                }
            }

            // Show callers that are affected
            Console.WriteLine("\n═══ AFFECTED CALLERS ═══\n");
            Console.WriteLine($"Methods that call {method.FullName} (behavior changed by patches):\n");
            RunQuery($@"
                SELECT t.name || '.' || m.name as caller,
                       c.file_path || ':' || c.line_number as location
                FROM calls c
                JOIN methods m ON c.caller_id = m.id
                JOIN types t ON m.type_id = t.id
                WHERE c.callee_id = {method.Id}
                ORDER BY t.name, m.name
                LIMIT 20
            ");

            // Check cached effective behavior if exists
            if (TableExists("effective_methods"))
            {
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = "SELECT vanilla_behavior, effective_behavior FROM effective_methods WHERE method_id = @id";
                cmd.Parameters.AddWithValue("@id", method.Id);
                using var reader = cmd.ExecuteReader();
                if (reader.Read() && !reader.IsDBNull(0))
                {
                    Console.WriteLine("\n═══ BEHAVIOR SUMMARY ═══\n");
                    Console.WriteLine($"  VANILLA: {reader.GetString(0)}");
                    if (!reader.IsDBNull(1))
                        Console.WriteLine($"  EFFECTIVE: {reader.GetString(1)}");
                }
            }

            Console.WriteLine();
        }

        return 0;
    }

    // ========================================================================
    // QA - Automated mod quality analysis
    // ========================================================================
    static int RunModQa(string[] args, string dbPath)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: QueryDb <db> qa <mod-path> [--output report.md] [--verbose]");
            Console.WriteLine("       Runs automated QA analysis on a mod");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  qa \"C:\\path\\to\\ProxiCraft\"");
            Console.WriteLine("  qa \"C:\\path\\to\\MyMod\" --output MyMod_QA.md");
            Console.WriteLine("  qa \"C:\\path\\to\\MyMod\" --verbose");
            return 1;
        }

        var modPath = args[0];
        string? outputPath = null;
        bool verbose = false;

        // Parse additional arguments
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
            else if (args[i] == "--verbose" || args[i] == "-v")
            {
                verbose = true;
            }
        }

        if (!Directory.Exists(modPath))
        {
            Console.WriteLine($"Mod directory not found: {modPath}");
            return 1;
        }

        try
        {
            // Run the analysis
            var analyzer = new CallGraphExtractor.ModAnalyzer(dbPath, verbose);
            var result = analyzer.Analyze(modPath);
            
            // Generate report
            var report = analyzer.GenerateReport(result);
            
            // Output report
            if (outputPath != null)
            {
                File.WriteAllText(outputPath, report);
                Console.WriteLine($"\n✅ Report written to: {outputPath}");
            }
            else
            {
                Console.WriteLine("\n" + new string('═', 67));
                Console.WriteLine("                      QA ANALYSIS REPORT");
                Console.WriteLine(new string('═', 67) + "\n");
                Console.WriteLine(report);
            }
            
            // Return code based on findings
            var highGaps = result.Gaps.Count(g => g.Severity == CallGraphExtractor.GapSeverity.High);
            return highGaps > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during analysis: {ex.Message}");
            if (verbose)
                Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static bool TableExists(string tableName)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result) > 0;
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
