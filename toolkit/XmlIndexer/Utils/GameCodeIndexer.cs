using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Utils;

/// <summary>
/// Indexes game code to extract method signatures, class inheritance, and type information.
/// Used for accurate Harmony patch signature matching and inheritance overlap detection.
/// Supports incremental updates via file hashing.
/// </summary>
public class GameCodeIndexer
{
    private readonly SqliteConnection _db;
    private readonly TypeResolver _typeResolver;
    private readonly Dictionary<string, string> _fileHashes = new();

    public int FilesProcessed { get; private set; }
    public int FilesSkipped { get; private set; }
    public int MethodsIndexed { get; private set; }
    public int ClassesIndexed { get; private set; }

    public GameCodeIndexer(SqliteConnection db)
    {
        _db = db;
        _typeResolver = new TypeResolver(db);
        LoadExistingHashes();
    }

    /// <summary>
    /// Loads existing file hashes from the database for incremental updates.
    /// </summary>
    private void LoadExistingHashes()
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT file_path, file_hash FROM class_inheritance WHERE file_hash IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                var hash = reader.GetString(1);
                _fileHashes[path] = hash;
            }
        }
        catch { /* Table may not exist yet */ }
    }

    /// <summary>
    /// Indexes all C# files in a game codebase directory.
    /// </summary>
    /// <param name="codebasePath">Path to the decompiled game code (e.g., 7D2DCodebase/)</param>
    /// <param name="forceReindex">If true, reindex all files regardless of hash</param>
    public void IndexGameCode(string codebasePath, bool forceReindex = false)
    {
        if (!Directory.Exists(codebasePath))
        {
            Console.WriteLine($"  Warning: Game codebase path not found: {codebasePath}");
            return;
        }

        var csFiles = Directory.GetFiles(codebasePath, "*.cs", SearchOption.AllDirectories);
        Console.WriteLine($"  Found {csFiles.Length} C# files to index");

        using var transaction = _db.BeginTransaction();

        foreach (var file in csFiles)
        {
            try
            {
                IndexFile(file, forceReindex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Failed to index {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        transaction.Commit();

        Console.WriteLine($"  Indexed: {FilesProcessed} files, {ClassesIndexed} classes, {MethodsIndexed} methods");
        Console.WriteLine($"  Skipped: {FilesSkipped} unchanged files");
    }

    /// <summary>
    /// Indexes a single C# file.
    /// </summary>
    private void IndexFile(string filePath, bool forceReindex)
    {
        var content = File.ReadAllText(filePath);
        var hash = ComputeHash(content);

        // Check if file has changed
        if (!forceReindex && _fileHashes.TryGetValue(filePath, out var existingHash) && existingHash == hash)
        {
            FilesSkipped++;
            return;
        }

        // Parse using statements for type resolution
        _typeResolver.ParseUsingStatements(filePath, content);

        // Extract and store class information
        var classes = ExtractClasses(content, filePath, hash);
        foreach (var cls in classes)
        {
            PersistClassInheritance(cls);
            ClassesIndexed++;

            // Extract and store method signatures
            var methods = ExtractMethods(cls.ClassBody, cls.ClassName, filePath, hash);
            foreach (var method in methods)
            {
                PersistMethodSignature(method);
                MethodsIndexed++;
            }
        }

        _fileHashes[filePath] = hash;
        FilesProcessed++;
    }

    /// <summary>
    /// Extracts class declarations from source code.
    /// </summary>
    private List<ClassInfo> ExtractClasses(string content, string filePath, string fileHash)
    {
        var classes = new List<ClassInfo>();

        // Pattern to match class/struct/interface declarations
        var classPattern = new Regex(
            @"(?:public|private|protected|internal)?\s*(?:abstract|sealed|static|partial)?\s*" +
            @"(class|struct|interface)\s+(\w+)(?:<[^>]+>)?\s*" +
            @"(?::\s*([\w\s,<>\.]+))?\s*(?:where[^{]+)?\s*\{",
            RegexOptions.Multiline);

        foreach (Match match in classPattern.Matches(content))
        {
            var kind = match.Groups[1].Value;
            var className = match.Groups[2].Value;
            var inheritance = match.Groups[3].Success ? match.Groups[3].Value : null;

            // Extract the class body
            var braceStart = match.Index + match.Length - 1;
            var classBody = CSharpAnalyzer.ExtractClassBody(content, braceStart);

            // Parse inheritance
            string? parentClass = null;
            var interfaces = new List<string>();

            if (!string.IsNullOrEmpty(inheritance))
            {
                var parts = inheritance.Split(',').Select(p => p.Trim()).ToList();
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    // Interfaces typically start with 'I' followed by uppercase
                    if (part.StartsWith("I") && part.Length > 1 && char.IsUpper(part[1]))
                    {
                        interfaces.Add(_typeResolver.ResolveType(part, filePath));
                    }
                    else if (parentClass == null)
                    {
                        parentClass = _typeResolver.ResolveType(part, filePath);
                    }
                    else
                    {
                        interfaces.Add(_typeResolver.ResolveType(part, filePath));
                    }
                }
            }

            // Detect modifiers
            var isAbstract = match.Value.Contains("abstract");
            var isSealed = match.Value.Contains("sealed");

            classes.Add(new ClassInfo(
                ClassName: className,
                ParentClass: parentClass,
                Interfaces: interfaces.Count > 0 ? JsonSerializer.Serialize(interfaces) : null,
                IsAbstract: isAbstract,
                IsSealed: isSealed,
                FilePath: filePath,
                FileHash: fileHash,
                ClassBody: classBody
            ));
        }

        return classes;
    }

    /// <summary>
    /// Extracts method signatures from a class body.
    /// </summary>
    private List<MethodInfo> ExtractMethods(string classBody, string className, string filePath, string fileHash)
    {
        var methods = new List<MethodInfo>();

        if (string.IsNullOrEmpty(classBody))
            return methods;

        // Pattern to match method declarations
        var methodPattern = new Regex(
            @"(?:(?:public|private|protected|internal)\s+)?" +
            @"(?:(static|virtual|override|abstract|sealed|extern|async)\s+)*" +
            @"([\w<>,\[\]\?]+)\s+" +  // Return type
            @"(\w+)\s*" +              // Method name
            @"\(([^)]*)\)",            // Parameters
            RegexOptions.Multiline);

        foreach (Match match in methodPattern.Matches(classBody))
        {
            var modifiers = match.Groups[1].Captures.Cast<Capture>().Select(c => c.Value).ToList();
            var returnType = match.Groups[2].Value;
            var methodName = match.Groups[3].Value;
            var parameters = match.Groups[4].Value;

            // Skip constructors (same name as class), property accessors, etc.
            if (methodName == className || methodName == "get" || methodName == "set" ||
                methodName == "add" || methodName == "remove")
                continue;

            // Skip common false positives
            if (returnType == "if" || returnType == "else" || returnType == "while" ||
                returnType == "for" || returnType == "foreach" || returnType == "switch" ||
                returnType == "return" || returnType == "throw" || returnType == "new")
                continue;

            // Parse parameter types
            var paramTypes = _typeResolver.ParseParameterTypes(parameters, filePath);
            var paramTypesJson = JsonSerializer.Serialize(paramTypes);

            // Resolve return type
            var resolvedReturn = _typeResolver.ResolveType(returnType, filePath);

            // Determine access modifier from surrounding text
            var surroundingText = classBody.Substring(Math.Max(0, match.Index - 50), Math.Min(50 + match.Length, classBody.Length - Math.Max(0, match.Index - 50)));
            var accessModifier = "private"; // default
            if (surroundingText.Contains("public")) accessModifier = "public";
            else if (surroundingText.Contains("protected")) accessModifier = "protected";
            else if (surroundingText.Contains("internal")) accessModifier = "internal";

            methods.Add(new MethodInfo(
                ClassName: className,
                MethodName: methodName,
                ParameterTypes: paramTypesJson,
                ParameterTypesFull: paramTypesJson, // Already resolved
                ReturnType: returnType,
                ReturnTypeFull: resolvedReturn,
                IsStatic: modifiers.Contains("static"),
                IsVirtual: modifiers.Contains("virtual"),
                IsOverride: modifiers.Contains("override"),
                AccessModifier: accessModifier,
                DeclaringClass: null, // Set later via inheritance analysis
                FilePath: filePath,
                FileHash: fileHash
            ));
        }

        return methods;
    }

    /// <summary>
    /// Persists class inheritance information to the database.
    /// </summary>
    private void PersistClassInheritance(ClassInfo cls)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO class_inheritance
            (class_name, parent_class, interfaces, is_abstract, is_sealed, file_path, file_hash)
            VALUES ($name, $parent, $interfaces, $abstract, $sealed, $path, $hash)";

        cmd.Parameters.AddWithValue("$name", cls.ClassName);
        cmd.Parameters.AddWithValue("$parent", cls.ParentClass ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$interfaces", cls.Interfaces ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$abstract", cls.IsAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("$sealed", cls.IsSealed ? 1 : 0);
        cmd.Parameters.AddWithValue("$path", cls.FilePath);
        cmd.Parameters.AddWithValue("$hash", cls.FileHash);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Persists method signature information to the database.
    /// </summary>
    private void PersistMethodSignature(MethodInfo method)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO method_signatures
            (class_name, method_name, parameter_types, parameter_types_full,
             return_type, return_type_full, is_static, is_virtual, is_override,
             access_modifier, declaring_class, file_path, file_hash)
            VALUES ($class, $method, $params, $paramsFull, $ret, $retFull,
                    $static, $virtual, $override, $access, $declaring, $path, $hash)";

        cmd.Parameters.AddWithValue("$class", method.ClassName);
        cmd.Parameters.AddWithValue("$method", method.MethodName);
        cmd.Parameters.AddWithValue("$params", method.ParameterTypes);
        cmd.Parameters.AddWithValue("$paramsFull", method.ParameterTypesFull ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ret", method.ReturnType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$retFull", method.ReturnTypeFull ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$static", method.IsStatic ? 1 : 0);
        cmd.Parameters.AddWithValue("$virtual", method.IsVirtual ? 1 : 0);
        cmd.Parameters.AddWithValue("$override", method.IsOverride ? 1 : 0);
        cmd.Parameters.AddWithValue("$access", method.AccessModifier ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$declaring", method.DeclaringClass ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$path", method.FilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", method.FileHash ?? (object)DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Computes MD5 hash of file content for change detection.
    /// </summary>
    private static string ComputeHash(string content)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = md5.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Finds all overloads of a method in a class.
    /// </summary>
    public List<MethodSignatureInfo> GetMethodOverloads(string className, string methodName)
    {
        var overloads = new List<MethodSignatureInfo>();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT id, class_name, method_name, parameter_types, parameter_types_full,
                   return_type, return_type_full, is_static, is_virtual, is_override,
                   access_modifier, declaring_class, file_path, file_hash
            FROM method_signatures
            WHERE class_name = $class AND method_name = $method";

        cmd.Parameters.AddWithValue("$class", className);
        cmd.Parameters.AddWithValue("$method", methodName);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            overloads.Add(new MethodSignatureInfo(
                Id: reader.GetInt64(0),
                ClassName: reader.GetString(1),
                MethodName: reader.GetString(2),
                ParameterTypes: reader.GetString(3),
                ParameterTypesFull: reader.IsDBNull(4) ? null : reader.GetString(4),
                ReturnType: reader.IsDBNull(5) ? null : reader.GetString(5),
                ReturnTypeFull: reader.IsDBNull(6) ? null : reader.GetString(6),
                IsStatic: reader.GetInt32(7) == 1,
                IsVirtual: reader.GetInt32(8) == 1,
                IsOverride: reader.GetInt32(9) == 1,
                AccessModifier: reader.IsDBNull(10) ? null : reader.GetString(10),
                DeclaringClass: reader.IsDBNull(11) ? null : reader.GetString(11),
                FilePath: reader.IsDBNull(12) ? null : reader.GetString(12),
                FileHash: reader.IsDBNull(13) ? null : reader.GetString(13)
            ));
        }

        return overloads;
    }

    /// <summary>
    /// Checks if a patch signature matches any method overload.
    /// </summary>
    public (bool Matches, string? MatchedOverload) MatchesPatchSignature(
        string className, string methodName, string? patchParamTypes)
    {
        var overloads = GetMethodOverloads(className, methodName);

        if (overloads.Count == 0)
            return (false, null);

        if (string.IsNullOrEmpty(patchParamTypes))
        {
            // No specific overload - matches if any exist
            return (true, overloads.First().ParameterTypes);
        }

        // Parse patch parameter types
        List<string>? patchTypes;
        try
        {
            patchTypes = JsonSerializer.Deserialize<List<string>>(patchParamTypes);
        }
        catch
        {
            return (false, null);
        }

        // Try to match against each overload
        foreach (var overload in overloads)
        {
            try
            {
                var overloadTypes = JsonSerializer.Deserialize<List<string>>(overload.ParameterTypes);
                if (overloadTypes != null && TypesMatch(patchTypes, overloadTypes))
                {
                    return (true, overload.ParameterTypes);
                }
            }
            catch { }
        }

        return (false, null);
    }

    /// <summary>
    /// Checks if two type lists match (handling Harmony injection parameters).
    /// </summary>
    private bool TypesMatch(List<string>? patchTypes, List<string> gameTypes)
    {
        if (patchTypes == null || patchTypes.Count == 0)
            return true; // Empty patch params match anything

        // Remove Harmony injection parameters from patch types
        var filteredPatch = patchTypes
            .Where(t => !t.StartsWith("__") && t != "MethodBase" && t != "IEnumerable<CodeInstruction>")
            .ToList();

        if (filteredPatch.Count != gameTypes.Count)
            return false;

        for (int i = 0; i < filteredPatch.Count; i++)
        {
            if (!_typeResolver.SignaturesMatch(filteredPatch[i], gameTypes[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Removes stale data for files that no longer exist or have changed.
    /// </summary>
    public void CleanupStaleData(string codebasePath)
    {
        var existingFiles = new HashSet<string>(
            Directory.GetFiles(codebasePath, "*.cs", SearchOption.AllDirectories),
            StringComparer.OrdinalIgnoreCase);

        // Find files in database that no longer exist
        var staleFiles = _fileHashes.Keys.Where(f => !existingFiles.Contains(f)).ToList();

        if (staleFiles.Count == 0)
            return;

        Console.WriteLine($"  Cleaning up {staleFiles.Count} stale file entries...");

        using var transaction = _db.BeginTransaction();

        foreach (var file in staleFiles)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM class_inheritance WHERE file_path = $path";
            cmd.Parameters.AddWithValue("$path", file);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "DELETE FROM method_signatures WHERE file_path = $path";
            cmd.ExecuteNonQuery();

            _fileHashes.Remove(file);
        }

        transaction.Commit();
    }

    // Internal types for parsing
    private record ClassInfo(
        string ClassName,
        string? ParentClass,
        string? Interfaces,
        bool IsAbstract,
        bool IsSealed,
        string FilePath,
        string FileHash,
        string ClassBody
    );

    private record MethodInfo(
        string ClassName,
        string MethodName,
        string ParameterTypes,
        string? ParameterTypesFull,
        string? ReturnType,
        string? ReturnTypeFull,
        bool IsStatic,
        bool IsVirtual,
        bool IsOverride,
        string? AccessModifier,
        string? DeclaringClass,
        string? FilePath,
        string? FileHash
    );
}
