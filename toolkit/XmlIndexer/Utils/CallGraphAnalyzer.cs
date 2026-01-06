using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace XmlIndexer.Utils;

/// <summary>
/// Analyzes C# files to build a method call graph and persists it to the database.
/// Supports incremental updates by tracking file hashes.
/// </summary>
public class CallGraphAnalyzer
{
    private readonly SqliteConnection _db;
    private readonly Dictionary<string, string> _fileHashes = new();
    
    public int FilesAnalyzed { get; private set; }
    public int FilesSkipped { get; private set; }
    public int MethodCallsFound { get; private set; }

    public CallGraphAnalyzer(SqliteConnection db)
    {
        _db = db;
        EnsureSchema();
        LoadExistingHashes();
    }

    private void EnsureSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS method_calls (
                id INTEGER PRIMARY KEY,
                caller_file TEXT NOT NULL,
                caller_class TEXT,
                caller_method TEXT,
                target_class TEXT NOT NULL,
                target_method TEXT NOT NULL,
                call_type TEXT,
                line_number INTEGER,
                code_snippet TEXT,
                file_hash TEXT,
                UNIQUE(caller_file, line_number, target_class, target_method)
            );
            CREATE INDEX IF NOT EXISTS idx_method_calls_caller ON method_calls(caller_file);
            CREATE INDEX IF NOT EXISTS idx_method_calls_target ON method_calls(target_class, target_method);
            CREATE INDEX IF NOT EXISTS idx_method_calls_hash ON method_calls(file_hash);
        ";
        cmd.ExecuteNonQuery();
    }

    private void LoadExistingHashes()
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT caller_file, file_hash FROM method_calls WHERE file_hash IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _fileHashes[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch { /* Table may not exist yet */ }
    }

    /// <summary>
    /// Analyzes all C# files in the codebase and builds the call graph.
    /// Uses incremental updates - only re-analyzes changed files.
    /// </summary>
    public void AnalyzeCallGraph(string codebasePath, bool forceReanalyze = false)
    {
        if (!Directory.Exists(codebasePath))
        {
            return;
        }

        var csFiles = Directory.GetFiles(codebasePath, "*.cs", SearchOption.AllDirectories);

        using var transaction = _db.BeginTransaction();

        foreach (var file in csFiles)
        {
            try
            {
                AnalyzeFile(file, forceReanalyze);
            }
            catch (Exception ex)
            {
                // Silent - don't break on individual file failures
                System.Diagnostics.Debug.WriteLine($"CallGraph warning: {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        transaction.Commit();
    }

    private void AnalyzeFile(string filePath, bool forceReanalyze)
    {
        var content = File.ReadAllText(filePath);
        var hash = ComputeHash(content);

        // Check if file has changed
        if (!forceReanalyze && _fileHashes.TryGetValue(filePath, out var existingHash) && existingHash == hash)
        {
            FilesSkipped++;
            return;
        }

        // Clear existing calls for this file
        ClearFileCalls(filePath);

        var lines = content.Split('\n');
        var currentClass = ExtractClassName(content);
        var currentMethod = "";

        // Pattern for method declarations
        var methodDeclPattern = new Regex(
            @"(?:public|private|protected|internal)\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        // Pattern for method calls: ClassName.MethodName( or just MethodName(
        var methodCallPattern = new Regex(
            @"(?:(\w+)\.)?(\w+)\s*\(",
            RegexOptions.Compiled);

        // Pattern for static method calls: ClassName.MethodName(
        var staticCallPattern = new Regex(
            @"([A-Z]\w+)\.(\w+)\s*\(",
            RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.Trim();

            // Skip comments
            if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*") || trimmedLine.StartsWith("*"))
                continue;

            // Track current method
            var methodDecl = methodDeclPattern.Match(line);
            if (methodDecl.Success)
            {
                currentMethod = methodDecl.Groups[1].Value;
            }

            // Find static method calls (ClassName.Method pattern)
            foreach (Match match in staticCallPattern.Matches(line))
            {
                var targetClass = match.Groups[1].Value;
                var targetMethod = match.Groups[2].Value;

                // Skip common false positives
                if (IsCommonFalsePositive(targetClass, targetMethod))
                    continue;

                // Skip if it's a type cast or generic
                var beforeMatch = line.Substring(0, match.Index);
                if (beforeMatch.EndsWith("(") || beforeMatch.EndsWith("<") || beforeMatch.EndsWith("new "))
                    continue;

                PersistMethodCall(filePath, currentClass, currentMethod, targetClass, targetMethod, 
                    "static", i + 1, GetCodeSnippet(lines, i), hash);
                MethodCallsFound++;
            }
        }

        _fileHashes[filePath] = hash;
        FilesAnalyzed++;
    }

    private static bool IsCommonFalsePositive(string className, string methodName)
    {
        // Skip common system/framework calls that aren't interesting
        var skipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Console", "Debug", "Log", "String", "Math", "Mathf", "Convert",
            "int", "float", "double", "bool", "string", "object", "Array",
            "List", "Dictionary", "HashSet", "StringBuilder", "Regex",
            "Path", "File", "Directory", "Environment", "Type", "Activator"
        };

        var skipMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ToString", "GetType", "Equals", "GetHashCode", "CompareTo",
            "Parse", "TryParse", "Format", "Join", "Split", "Contains",
            "Add", "Remove", "Clear", "Count", "Length"
        };

        return skipClasses.Contains(className) || skipMethods.Contains(methodName);
    }

    private static string ExtractClassName(string content)
    {
        var match = Regex.Match(content, @"(?:class|struct)\s+(\w+)");
        return match.Success ? match.Groups[1].Value : "Unknown";
    }

    private static string GetCodeSnippet(string[] lines, int lineIndex)
    {
        return lines[lineIndex].Trim();
    }

    private static string ComputeHash(string content)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = md5.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }

    private void ClearFileCalls(string filePath)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM method_calls WHERE caller_file = $path";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.ExecuteNonQuery();
    }

    private void PersistMethodCall(string callerFile, string callerClass, string callerMethod,
        string targetClass, string targetMethod, string callType, int lineNumber, 
        string codeSnippet, string fileHash)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO method_calls
            (caller_file, caller_class, caller_method, target_class, target_method, 
             call_type, line_number, code_snippet, file_hash)
            VALUES ($file, $callerClass, $callerMethod, $targetClass, $targetMethod,
                    $callType, $line, $snippet, $hash)";

        cmd.Parameters.AddWithValue("$file", callerFile);
        cmd.Parameters.AddWithValue("$callerClass", callerClass);
        cmd.Parameters.AddWithValue("$callerMethod", callerMethod ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$targetClass", targetClass);
        cmd.Parameters.AddWithValue("$targetMethod", targetMethod);
        cmd.Parameters.AddWithValue("$callType", callType);
        cmd.Parameters.AddWithValue("$line", lineNumber);
        cmd.Parameters.AddWithValue("$snippet", codeSnippet);
        cmd.Parameters.AddWithValue("$hash", fileHash);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets statistics about the call graph.
    /// </summary>
    public static (int TotalCalls, int UniqueTargets, int UniqueCallers) GetStats(SqliteConnection db)
    {
        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    COUNT(*) as total,
                    COUNT(DISTINCT target_class || '.' || target_method) as unique_targets,
                    COUNT(DISTINCT caller_file) as unique_callers
                FROM method_calls";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            }
        }
        catch { }
        return (0, 0, 0);
    }
}
