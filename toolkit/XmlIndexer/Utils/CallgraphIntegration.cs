using Microsoft.Data.Sqlite;

namespace XmlIndexer.Utils;

/// <summary>
/// Integrates with the QueryDb callgraph database to detect C# code that
/// references entities modified or removed by XML mods.
/// </summary>
public class CallgraphIntegration : IDisposable
{
    private readonly SqliteConnection? _callgraphDb;
    private readonly SqliteConnection _modDb;
    private bool _disposed;

    /// <summary>
    /// Creates a new callgraph integration instance.
    /// </summary>
    /// <param name="modDbPath">Path to the XmlIndexer mod analysis database</param>
    /// <param name="callgraphDbPath">Path to QueryDb's callgraph_full.db (optional)</param>
    public CallgraphIntegration(string modDbPath, string? callgraphDbPath = null)
    {
        _modDb = new SqliteConnection($"Data Source={modDbPath}");
        _modDb.Open();

        // Try to find callgraph database
        callgraphDbPath ??= Environment.GetEnvironmentVariable("CALLGRAPH_DB_PATH");
        callgraphDbPath ??= FindDefaultCallgraphDb(modDbPath);

        if (!string.IsNullOrEmpty(callgraphDbPath) && File.Exists(callgraphDbPath))
        {
            _callgraphDb = new SqliteConnection($"Data Source={callgraphDbPath}");
            _callgraphDb.Open();
        }
    }

    /// <summary>
    /// Whether the callgraph database is available for enhanced analysis.
    /// </summary>
    public bool HasCallgraph => _callgraphDb != null;

    /// <summary>
    /// Finds Harmony patches that target methods reading entities affected by XML mods.
    /// </summary>
    public List<CallgraphConflict> FindHarmonyXmlConflicts()
    {
        var conflicts = new List<CallgraphConflict>();

        if (_callgraphDb == null)
            return conflicts;

        // Get all Harmony patches from mod_csharp_deps
        var harmonyPatches = GetHarmonyPatches();

        // Get all XML removals and modifications
        var xmlOperations = GetSignificantXmlOperations();

        foreach (var patch in harmonyPatches)
        {
            // Find methods called by the patched class
            var calledMethods = GetCalledMethods(patch.TargetClass);

            // Check if any called methods reference entities affected by XML mods
            foreach (var method in calledMethods)
            {
                var referencedEntities = GetEntityReferencesInMethod(method);

                foreach (var entity in referencedEntities)
                {
                    var affectingOps = xmlOperations
                        .Where(op => op.TargetType == entity.Type && op.TargetName == entity.Name)
                        .ToList();

                    if (affectingOps.Any())
                    {
                        foreach (var op in affectingOps)
                        {
                            conflicts.Add(new CallgraphConflict
                            {
                                CSharpMod = patch.ModName,
                                HarmonyTargetClass = patch.TargetClass,
                                HarmonyTargetMethod = patch.TargetMethod,
                                HarmonyPatchType = patch.PatchType,
                                CalledMethod = method,
                                ReferencedEntityType = entity.Type,
                                ReferencedEntityName = entity.Name,
                                XmlMod = op.ModName,
                                XmlOperation = op.Operation,
                                XPath = op.XPath,
                                Severity = op.Operation is "remove" or "removeattribute" ? "HIGH" : "MEDIUM",
                                Reason = $"Harmony {patch.PatchType} on {patch.TargetClass}.{patch.TargetMethod} " +
                                        $"calls {method} which uses {entity.Type} '{entity.Name}', " +
                                        $"but mod '{op.ModName}' {op.Operation}s it"
                            });
                        }
                    }
                }
            }
        }

        return conflicts;
    }

    private List<HarmonyPatch> GetHarmonyPatches()
    {
        var patches = new List<HarmonyPatch>();

        using var cmd = _modDb.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name, cd.dependency_name, cd.pattern
            FROM mod_csharp_deps cd
            JOIN mods m ON cd.mod_id = m.id
            WHERE cd.dependency_type = 'harmony_class'";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var modName = reader.GetString(0);
            var targetClass = reader.GetString(1);
            var pattern = reader.IsDBNull(2) ? "" : reader.GetString(2);

            patches.Add(new HarmonyPatch
            {
                ModName = modName,
                TargetClass = targetClass,
                TargetMethod = ExtractMethodFromPattern(pattern),
                PatchType = ExtractPatchTypeFromPattern(pattern)
            });
        }

        return patches;
    }

    private List<XmlOperation> GetSignificantXmlOperations()
    {
        var operations = new List<XmlOperation>();

        using var cmd = _modDb.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name, o.operation, o.xpath, o.target_type, o.target_name
            FROM mod_xml_operations o
            JOIN mods m ON o.mod_id = m.id
            WHERE o.target_type IS NOT NULL
              AND o.target_name IS NOT NULL
              AND o.operation IN ('remove', 'removeattribute', 'set', 'setattribute')";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            operations.Add(new XmlOperation
            {
                ModName = reader.GetString(0),
                Operation = reader.GetString(1),
                XPath = reader.IsDBNull(2) ? null : reader.GetString(2),
                TargetType = reader.GetString(3),
                TargetName = reader.GetString(4)
            });
        }

        return operations;
    }

    private List<string> GetCalledMethods(string className)
    {
        var methods = new List<string>();

        if (_callgraphDb == null)
            return methods;

        // Query the callgraph for methods called by this class
        using var cmd = _callgraphDb.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT callee_method
            FROM calls
            WHERE caller_class LIKE $className
            LIMIT 100";
        cmd.Parameters.AddWithValue("$className", $"%{className}%");

        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    methods.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Callgraph schema may differ - ignore errors
        }

        return methods;
    }

    private List<EntityReference> GetEntityReferencesInMethod(string methodName)
    {
        var refs = new List<EntityReference>();

        if (_callgraphDb == null)
            return refs;

        // Query for string literals in the method that match entity names
        using var cmd = _callgraphDb.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT literal_value
            FROM string_literals
            WHERE method_name = $method
            LIMIT 50";
        cmd.Parameters.AddWithValue("$method", methodName);

        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0)) continue;

                var literal = reader.GetString(0);

                // Try to identify entity type from the string
                var entityType = InferEntityType(literal);
                if (entityType != null)
                {
                    refs.Add(new EntityReference { Type = entityType, Name = literal });
                }
            }
        }
        catch
        {
            // Callgraph schema may differ - ignore errors
        }

        return refs;
    }

    private string? InferEntityType(string value)
    {
        // Check if this string exists in any XML definitions
        using var cmd = _modDb.CreateCommand();
        cmd.CommandText = @"
            SELECT definition_type
            FROM xml_definitions
            WHERE name = $name
            LIMIT 1";
        cmd.Parameters.AddWithValue("$name", value);

        var result = cmd.ExecuteScalar();
        return result as string;
    }

    private static string ExtractMethodFromPattern(string pattern)
    {
        // Try to extract method name from Harmony pattern like [HarmonyPatch("MethodName")]
        var match = System.Text.RegularExpressions.Regex.Match(
            pattern, @"\[HarmonyPatch\s*\(\s*""([^""]+)""\s*\)");
        return match.Success ? match.Groups[1].Value : "Unknown";
    }

    private static string ExtractPatchTypeFromPattern(string pattern)
    {
        if (pattern.Contains("Prefix")) return "Prefix";
        if (pattern.Contains("Postfix")) return "Postfix";
        if (pattern.Contains("Transpiler")) return "Transpiler";
        return "Unknown";
    }

    private static string? FindDefaultCallgraphDb(string modDbPath)
    {
        // Look for callgraph_full.db in common locations relative to the mod database
        var searchPaths = new[]
        {
            Path.Combine(Path.GetDirectoryName(modDbPath) ?? "", "..", "QueryDb", "callgraph_full.db"),
            Path.Combine(Path.GetDirectoryName(modDbPath) ?? "", "callgraph_full.db"),
            "callgraph_full.db"
        };

        foreach (var path in searchPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _callgraphDb?.Dispose();
            _modDb.Dispose();
            _disposed = true;
        }
    }
}

// Supporting data models

public class HarmonyPatch
{
    public string ModName { get; set; } = "";
    public string TargetClass { get; set; } = "";
    public string TargetMethod { get; set; } = "";
    public string PatchType { get; set; } = "";
}

public class XmlOperation
{
    public string ModName { get; set; } = "";
    public string Operation { get; set; } = "";
    public string? XPath { get; set; }
    public string TargetType { get; set; } = "";
    public string TargetName { get; set; } = "";
}

public class EntityReference
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
}

public class CallgraphConflict
{
    public string CSharpMod { get; set; } = "";
    public string HarmonyTargetClass { get; set; } = "";
    public string HarmonyTargetMethod { get; set; } = "";
    public string HarmonyPatchType { get; set; } = "";
    public string CalledMethod { get; set; } = "";
    public string ReferencedEntityType { get; set; } = "";
    public string ReferencedEntityName { get; set; } = "";
    public string XmlMod { get; set; } = "";
    public string XmlOperation { get; set; } = "";
    public string? XPath { get; set; }
    public string Severity { get; set; } = "HIGH";
    public string Reason { get; set; } = "";
}
