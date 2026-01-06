using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Utils;

/// <summary>
/// Resolves short type names to fully qualified types for accurate signature matching.
/// Handles C# built-in aliases, using statements, and common Unity/System types.
/// </summary>
public class TypeResolver
{
    // C# built-in type aliases
    private static readonly Dictionary<string, string> BuiltInAliases = new(StringComparer.Ordinal)
    {
        ["bool"] = "System.Boolean",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["char"] = "System.Char",
        ["decimal"] = "System.Decimal",
        ["double"] = "System.Double",
        ["float"] = "System.Single",
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["object"] = "System.Object",
        ["string"] = "System.String",
        ["void"] = "System.Void",
        ["nint"] = "System.IntPtr",
        ["nuint"] = "System.UIntPtr"
    };

    // Common namespaces for type resolution
    private static readonly string[] CommonNamespaces = new[]
    {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Linq",
        "System.IO",
        "System.Text",
        "System.Text.RegularExpressions",
        "System.Reflection",
        "UnityEngine",
        "UnityEngine.UI",
        "UnityEngine.Events"
    };

    // Known Unity types for quick resolution
    private static readonly Dictionary<string, string> UnityTypes = new(StringComparer.Ordinal)
    {
        ["GameObject"] = "UnityEngine.GameObject",
        ["Transform"] = "UnityEngine.Transform",
        ["Vector2"] = "UnityEngine.Vector2",
        ["Vector3"] = "UnityEngine.Vector3",
        ["Vector4"] = "UnityEngine.Vector4",
        ["Quaternion"] = "UnityEngine.Quaternion",
        ["Color"] = "UnityEngine.Color",
        ["Rect"] = "UnityEngine.Rect",
        ["Bounds"] = "UnityEngine.Bounds",
        ["Ray"] = "UnityEngine.Ray",
        ["RaycastHit"] = "UnityEngine.RaycastHit",
        ["Material"] = "UnityEngine.Material",
        ["Texture"] = "UnityEngine.Texture",
        ["Texture2D"] = "UnityEngine.Texture2D",
        ["Sprite"] = "UnityEngine.Sprite",
        ["AudioClip"] = "UnityEngine.AudioClip",
        ["AudioSource"] = "UnityEngine.AudioSource",
        ["Camera"] = "UnityEngine.Camera",
        ["Rigidbody"] = "UnityEngine.Rigidbody",
        ["Collider"] = "UnityEngine.Collider",
        ["BoxCollider"] = "UnityEngine.BoxCollider",
        ["SphereCollider"] = "UnityEngine.SphereCollider",
        ["CapsuleCollider"] = "UnityEngine.CapsuleCollider",
        ["MeshCollider"] = "UnityEngine.MeshCollider",
        ["MonoBehaviour"] = "UnityEngine.MonoBehaviour",
        ["ScriptableObject"] = "UnityEngine.ScriptableObject",
        ["Coroutine"] = "UnityEngine.Coroutine",
        ["AnimationCurve"] = "UnityEngine.AnimationCurve",
        ["Animator"] = "UnityEngine.Animator",
        ["Animation"] = "UnityEngine.Animation",
        ["ParticleSystem"] = "UnityEngine.ParticleSystem"
    };

    // Known System collection types
    private static readonly Dictionary<string, string> CollectionTypes = new(StringComparer.Ordinal)
    {
        ["List"] = "System.Collections.Generic.List",
        ["Dictionary"] = "System.Collections.Generic.Dictionary",
        ["HashSet"] = "System.Collections.Generic.HashSet",
        ["Queue"] = "System.Collections.Generic.Queue",
        ["Stack"] = "System.Collections.Generic.Stack",
        ["LinkedList"] = "System.Collections.Generic.LinkedList",
        ["SortedList"] = "System.Collections.Generic.SortedList",
        ["SortedDictionary"] = "System.Collections.Generic.SortedDictionary",
        ["SortedSet"] = "System.Collections.Generic.SortedSet",
        ["IEnumerable"] = "System.Collections.Generic.IEnumerable",
        ["ICollection"] = "System.Collections.Generic.ICollection",
        ["IList"] = "System.Collections.Generic.IList",
        ["IDictionary"] = "System.Collections.Generic.IDictionary",
        ["ISet"] = "System.Collections.Generic.ISet",
        ["KeyValuePair"] = "System.Collections.Generic.KeyValuePair",
        ["ArrayList"] = "System.Collections.ArrayList",
        ["Hashtable"] = "System.Collections.Hashtable"
    };

    private readonly Dictionary<string, HashSet<string>> _fileUsings = new();
    private readonly Dictionary<string, string> _knownTypes = new();
    private readonly SqliteConnection? _db;

    public TypeResolver(SqliteConnection? db = null)
    {
        _db = db;
        LoadKnownTypesFromDatabase();
    }

    /// <summary>
    /// Loads type alias mappings from the database if available.
    /// </summary>
    private void LoadKnownTypesFromDatabase()
    {
        if (_db == null) return;

        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT short_name, full_name FROM type_aliases GROUP BY short_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var shortName = reader.GetString(0);
                var fullName = reader.GetString(1);
                if (!_knownTypes.ContainsKey(shortName))
                    _knownTypes[shortName] = fullName;
            }
        }
        catch { /* Table may not exist yet */ }
    }

    /// <summary>
    /// Parses using statements from C# source code and registers them for a file.
    /// </summary>
    public void ParseUsingStatements(string filePath, string content)
    {
        var usings = new HashSet<string>();

        // Match using statements
        var usingPattern = new Regex(@"^using\s+(?:static\s+)?([A-Za-z_][\w\.]*)\s*;", RegexOptions.Multiline);
        foreach (Match match in usingPattern.Matches(content))
        {
            usings.Add(match.Groups[1].Value);
        }

        // Match using aliases: using Foo = Some.Namespace.Bar;
        var aliasPattern = new Regex(@"^using\s+(\w+)\s*=\s*([A-Za-z_][\w\.]+)\s*;", RegexOptions.Multiline);
        foreach (Match match in aliasPattern.Matches(content))
        {
            var alias = match.Groups[1].Value;
            var fullType = match.Groups[2].Value;
            _knownTypes[alias] = fullType;
        }

        _fileUsings[filePath] = usings;
    }

    /// <summary>
    /// Resolves a type name to its fully qualified form.
    /// </summary>
    /// <param name="typeName">The type name (may be short or fully qualified)</param>
    /// <param name="filePath">The source file path (for using statement context)</param>
    /// <returns>The fully qualified type name, or the original if resolution fails</returns>
    public string ResolveType(string typeName, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return typeName;

        // Handle nullable types
        if (typeName.EndsWith("?"))
        {
            var innerType = ResolveType(typeName.TrimEnd('?'), filePath);
            return $"System.Nullable<{innerType}>";
        }

        // Handle array types
        if (typeName.EndsWith("[]"))
        {
            var elementType = ResolveType(typeName.Substring(0, typeName.Length - 2), filePath);
            return $"{elementType}[]";
        }

        // Handle generic types: List<string> -> System.Collections.Generic.List<System.String>
        if (typeName.Contains("<"))
        {
            return ResolveGenericType(typeName, filePath);
        }

        // Already fully qualified?
        if (typeName.Contains("."))
            return typeName;

        // Check C# built-in aliases first
        if (BuiltInAliases.TryGetValue(typeName, out var builtIn))
            return builtIn;

        // Check known types from database/parsing
        if (_knownTypes.TryGetValue(typeName, out var known))
            return known;

        // Check Unity types
        if (UnityTypes.TryGetValue(typeName, out var unity))
            return unity;

        // Check collection types
        if (CollectionTypes.TryGetValue(typeName, out var collection))
            return collection;

        // Try to resolve using file's using statements
        if (filePath != null && _fileUsings.TryGetValue(filePath, out var usings))
        {
            foreach (var ns in usings)
            {
                var candidate = $"{ns}.{typeName}";
                if (IsKnownType(candidate))
                    return candidate;
            }
        }

        // Try common namespaces as last resort
        foreach (var ns in CommonNamespaces)
        {
            var candidate = $"{ns}.{typeName}";
            if (IsKnownType(candidate))
                return candidate;
        }

        // Unable to resolve - return as-is
        return typeName;
    }

    /// <summary>
    /// Resolves a generic type like List&lt;string&gt; to fully qualified form.
    /// </summary>
    private string ResolveGenericType(string typeName, string? filePath)
    {
        var openBracket = typeName.IndexOf('<');
        var closeBracket = typeName.LastIndexOf('>');

        if (openBracket < 0 || closeBracket < 0 || closeBracket <= openBracket)
            return typeName;

        var baseType = typeName.Substring(0, openBracket);
        var typeArgs = typeName.Substring(openBracket + 1, closeBracket - openBracket - 1);

        // Resolve the base generic type
        var resolvedBase = ResolveType(baseType, filePath);

        // Parse and resolve type arguments (handling nested generics)
        var resolvedArgs = ParseAndResolveTypeArguments(typeArgs, filePath);

        return $"{resolvedBase}<{string.Join(", ", resolvedArgs)}>";
    }

    /// <summary>
    /// Parses comma-separated type arguments, handling nested generics.
    /// </summary>
    private List<string> ParseAndResolveTypeArguments(string typeArgs, string? filePath)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;

        foreach (char c in typeArgs)
        {
            if (c == '<')
            {
                depth++;
                current.Append(c);
            }
            else if (c == '>')
            {
                depth--;
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(ResolveType(current.ToString().Trim(), filePath));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            args.Add(ResolveType(current.ToString().Trim(), filePath));

        return args;
    }

    /// <summary>
    /// Checks if a type is known (exists in our type database).
    /// </summary>
    private bool IsKnownType(string fullTypeName)
    {
        // Check built-in types
        if (BuiltInAliases.ContainsValue(fullTypeName))
            return true;

        // Check Unity types
        if (UnityTypes.ContainsValue(fullTypeName))
            return true;

        // Check collection types
        if (CollectionTypes.ContainsValue(fullTypeName))
            return true;

        // Check database
        if (_db != null)
        {
            try
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM class_inheritance WHERE class_name = $name LIMIT 1";
                cmd.Parameters.AddWithValue("$name", fullTypeName);
                return cmd.ExecuteScalar() != null;
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Normalizes a method signature for comparison.
    /// Converts all types to fully qualified forms.
    /// </summary>
    public string NormalizeSignature(string signature, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return signature;

        // Pattern to match type names in a signature
        // This handles: int, string, List<int>, Dictionary<string, int>, etc.
        var typePattern = new Regex(@"(?<type>\w+(?:<[^>]+>)?(?:\[\])?(?:\?)?)(?=\s+\w+|,|\)|$)");

        return typePattern.Replace(signature, match =>
        {
            var type = match.Groups["type"].Value;
            return ResolveType(type, filePath);
        });
    }

    /// <summary>
    /// Compares two method signatures for equality after normalization.
    /// </summary>
    public bool SignaturesMatch(string sig1, string sig2, string? file1 = null, string? file2 = null)
    {
        var norm1 = NormalizeSignature(sig1, file1);
        var norm2 = NormalizeSignature(sig2, file2);
        return string.Equals(norm1, norm2, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a parameter list and returns normalized type names.
    /// Input: "string name, int count, List<string> items"
    /// Output: ["System.String", "System.Int32", "System.Collections.Generic.List<System.String>"]
    /// </summary>
    public List<string> ParseParameterTypes(string parameterList, string? filePath = null)
    {
        var types = new List<string>();

        if (string.IsNullOrWhiteSpace(parameterList))
            return types;

        // Split by comma, handling generics
        var parts = SplitParameters(parameterList);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Remove parameter modifiers (ref, out, in, params)
            trimmed = Regex.Replace(trimmed, @"^(ref|out|in|params)\s+", "");

            // Extract the type (first word or generic expression)
            var typeMatch = Regex.Match(trimmed, @"^(\w+(?:<[^>]+>)?(?:\[\])?(?:\?)?)");
            if (typeMatch.Success)
            {
                var type = typeMatch.Groups[1].Value;
                types.Add(ResolveType(type, filePath));
            }
        }

        return types;
    }

    /// <summary>
    /// Splits a parameter list by commas, respecting generic type brackets.
    /// </summary>
    private static List<string> SplitParameters(string parameterList)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;

        foreach (char c in parameterList)
        {
            if (c == '<')
            {
                depth++;
                current.Append(c);
            }
            else if (c == '>')
            {
                depth--;
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    /// <summary>
    /// Persists type aliases to the database for a specific file.
    /// </summary>
    public void PersistTypeAliases(SqliteConnection db, string filePath)
    {
        if (!_fileUsings.TryGetValue(filePath, out var usings))
            return;

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO type_aliases (file_path, short_name, full_name, namespace)
            VALUES ($path, $short, $full, $ns)";

        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
        var pShort = cmd.Parameters.Add("$short", SqliteType.Text);
        var pFull = cmd.Parameters.Add("$full", SqliteType.Text);
        var pNs = cmd.Parameters.Add("$ns", SqliteType.Text);

        pPath.Value = filePath;

        // Store namespace usings as potential type sources
        foreach (var ns in usings)
        {
            // For namespace usings, we store the namespace itself
            var shortName = ns.Contains('.') ? ns.Substring(ns.LastIndexOf('.') + 1) : ns;
            pShort.Value = shortName;
            pFull.Value = ns;
            pNs.Value = ns;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Gets the inheritance chain for a class from the database.
    /// </summary>
    public List<string> GetInheritanceChain(string className)
    {
        var chain = new List<string> { className };

        if (_db == null)
            return chain;

        var current = className;
        var visited = new HashSet<string> { className };

        while (true)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT parent_class FROM class_inheritance WHERE class_name = $name";
            cmd.Parameters.AddWithValue("$name", current);

            var parent = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(parent) || visited.Contains(parent))
                break;

            chain.Add(parent);
            visited.Add(parent);
            current = parent;
        }

        return chain;
    }

    /// <summary>
    /// Checks if a class inherits from a specific base class.
    /// </summary>
    public bool InheritsFrom(string className, string baseClassName)
    {
        var chain = GetInheritanceChain(className);
        return chain.Contains(baseClassName);
    }
}
