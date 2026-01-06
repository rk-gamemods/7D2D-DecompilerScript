using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;
using XmlIndexer.Utils;

namespace XmlIndexer.Analysis;

/// <summary>
/// Analyzes game code to find potential bugs, dead code, stubs, and hidden features.
/// Focuses on high-confidence, low-false-positive detections.
/// </summary>
public class GameCodeAnalyzer
{
    private readonly SqliteConnection _db;
    private readonly Dictionary<string, string> _fileHashes = new();

    public int FilesAnalyzed { get; private set; }
    public int FilesSkipped { get; private set; }
    public int FindingsTotal { get; private set; }

    // Stats by type
    public int BugCount { get; private set; }
    public int WarningCount { get; private set; }
    public int InfoCount { get; private set; }
    public int OpportunityCount { get; private set; }

    public GameCodeAnalyzer(SqliteConnection db)
    {
        _db = db;
        LoadExistingHashes();
    }

    private void LoadExistingHashes()
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT file_path, file_hash FROM game_code_analysis WHERE file_hash IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _fileHashes[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch { /* Table may not exist yet */ }
    }

    /// <summary>
    /// Analyzes all C# files in the game codebase for potential issues.
    /// </summary>
    public void AnalyzeGameCode(string codebasePath, bool forceReanalyze = false)
    {
        if (!Directory.Exists(codebasePath))
        {
            Console.WriteLine($"  Warning: Game codebase path not found: {codebasePath}");
            return;
        }

        var csFiles = Directory.GetFiles(codebasePath, "*.cs", SearchOption.AllDirectories);
        Console.WriteLine($"  Analyzing {csFiles.Length} C# files for potential issues...");

        using var transaction = _db.BeginTransaction();

        foreach (var file in csFiles)
        {
            try
            {
                AnalyzeFile(file, forceReanalyze);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Failed to analyze {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        transaction.Commit();

        Console.WriteLine($"  Analyzed: {FilesAnalyzed} files, Skipped: {FilesSkipped} unchanged");
        
        // Also build call graph for method-level analysis
        Console.WriteLine($"  Building method call graph...");
        var callGraphAnalyzer = new CallGraphAnalyzer(_db);
        callGraphAnalyzer.AnalyzeCallGraph(codebasePath, forceReanalyze);
        Console.WriteLine($"  Call graph: {callGraphAnalyzer.FilesAnalyzed} files, {callGraphAnalyzer.MethodCallsFound} calls found");
        Console.WriteLine($"  Findings: {BugCount} bugs, {WarningCount} warnings, {InfoCount} info, {OpportunityCount} opportunities");
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

        // Clear existing findings for this file
        ClearFileFindings(filePath);

        var lines = content.Split('\n');
        var findings = new List<GameCodeFinding>();

        // Extract class context for findings
        var currentClass = ExtractClassName(content);

        // Run all detection patterns
        findings.AddRange(DetectNotImplemented(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectEmptyCatch(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectTodoComments(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectStubMethods(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectUnreachableCode(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectSuspiciousPatterns(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectSecretFeatures(content, lines, filePath, hash, currentClass));
        findings.AddRange(DetectHardcodedEntities(content, lines, filePath, hash, currentClass));

        // Persist findings
        foreach (var finding in findings)
        {
            PersistFinding(finding);
            FindingsTotal++;

            switch (finding.Severity)
            {
                case "BUG": BugCount++; break;
                case "WARNING": WarningCount++; break;
                case "INFO": InfoCount++; break;
                case "OPPORTUNITY": OpportunityCount++; break;
            }
        }

        _fileHashes[filePath] = hash;
        FilesAnalyzed++;
    }

    /// <summary>
    /// Detects NotImplementedException throws.
    /// </summary>
    private List<GameCodeFinding> DetectNotImplemented(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();
        var pattern = new Regex(@"throw\s+new\s+NotImplementedException\s*\(\s*\)", RegexOptions.IgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(lines[i]))
            {
                var methodName = FindEnclosingMethod(lines, i);
                findings.Add(CreateFinding(
                    type: "unimplemented",
                    className: className,
                    methodName: methodName,
                    severity: "WARNING",
                    confidence: "high",
                    description: "Method throws NotImplementedException",
                    reasoning: "This method is not implemented and will throw at runtime if called",
                    codeSnippet: GetCodeContext(lines, i, 2),
                    filePath: filePath,
                    lineNumber: i + 1,
                    potentialFix: "Implement the method or remove if unused",
                    fileHash: hash
                ));
            }
        }

        return findings;
    }

    /// <summary>
    /// Detects empty catch blocks.
    /// </summary>
    private List<GameCodeFinding> DetectEmptyCatch(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();

        // Pattern for catch blocks with empty or near-empty bodies
        var pattern = new Regex(@"catch\s*(?:\([^)]*\))?\s*\{\s*(?://[^\n]*\n\s*)?\}", RegexOptions.Multiline);

        foreach (Match match in pattern.Matches(content))
        {
            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
            var methodName = FindEnclosingMethod(lines, lineNumber - 1);

            findings.Add(CreateFinding(
                type: "empty_catch",
                className: className,
                methodName: methodName,
                severity: "WARNING",
                confidence: "high",
                description: "Empty catch block silently swallows exceptions",
                reasoning: "Empty catch blocks hide errors and make debugging difficult",
                codeSnippet: GetCodeContext(lines, lineNumber - 1, 2),
                filePath: filePath,
                lineNumber: lineNumber,
                potentialFix: "Log the exception or handle it appropriately",
                fileHash: hash
            ));
        }

        return findings;
    }

    /// <summary>
    /// Detects TODO and FIXME comments.
    /// </summary>
    private List<GameCodeFinding> DetectTodoComments(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();
        var pattern = new Regex(@"//\s*(TODO|FIXME|HACK|XXX|BUG)\s*:?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (Match match in pattern.Matches(content))
        {
            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
            var tag = match.Groups[1].Value.ToUpper();
            var message = match.Groups[2].Value.Trim();
            var methodName = FindEnclosingMethod(lines, lineNumber - 1);

            var severity = tag switch
            {
                "BUG" or "FIXME" => "WARNING",
                "HACK" or "XXX" => "INFO",
                _ => "INFO"
            };

            findings.Add(CreateFinding(
                type: "todo",
                className: className,
                methodName: methodName,
                severity: severity,
                confidence: "high",
                description: $"{tag}: {(string.IsNullOrEmpty(message) ? "(no description)" : message)}",
                reasoning: "Developer marked this code as needing attention",
                codeSnippet: GetCodeContext(lines, lineNumber - 1, 1),
                filePath: filePath,
                lineNumber: lineNumber,
                potentialFix: "Address the TODO/FIXME or create a mod to fix it",
                fileHash: hash
            ));
        }

        return findings;
    }

    /// <summary>
    /// Detects stub methods that only return null or default.
    /// </summary>
    private List<GameCodeFinding> DetectStubMethods(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();

        // Pattern for methods with only "return null;" or "return default;"
        var methodPattern = new Regex(
            @"(?:public|private|protected|internal)\s+(?:static\s+)?(\w+(?:<[^>]+>)?)\s+(\w+)\s*\([^)]*\)\s*\{\s*return\s+(?:null|default(?:\([^)]*\))?)\s*;\s*\}",
            RegexOptions.Multiline);

        foreach (Match match in methodPattern.Matches(content))
        {
            var returnType = match.Groups[1].Value;
            var methodName = match.Groups[2].Value;
            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            // Skip Unity magic methods
            if (CSharpAnalyzer.UnityMagicMethods.Contains(methodName))
                continue;

            // Skip void returns (they can't return null)
            if (returnType == "void")
                continue;

            findings.Add(CreateFinding(
                type: "stub_method",
                className: className,
                methodName: methodName,
                severity: "INFO",
                confidence: "medium",
                description: $"Method '{methodName}' only returns {(returnType.Contains("?") ? "null" : "default")}",
                reasoning: "This method appears to be a stub that does nothing useful",
                codeSnippet: GetCodeContext(lines, lineNumber - 1, 2),
                filePath: filePath,
                lineNumber: lineNumber,
                potentialFix: "Check if this is intentionally empty or needs implementation",
                fileHash: hash,
                isUnityMagic: false
            ));
        }

        return findings;
    }

    /// <summary>
    /// Detects unreachable code after unconditional return/throw.
    /// </summary>
    private List<GameCodeFinding> DetectUnreachableCode(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();

        // Look for code after return/throw that isn't a closing brace or another control statement
        for (int i = 0; i < lines.Length - 1; i++)
        {
            var line = lines[i].Trim();
            var nextLine = lines[i + 1].Trim();

            // Check for return/throw followed by non-control code
            if ((line.StartsWith("return ") || line.StartsWith("throw ")) &&
                line.EndsWith(";") &&
                !string.IsNullOrWhiteSpace(nextLine) &&
                !nextLine.StartsWith("}") &&
                !nextLine.StartsWith("//") &&
                !nextLine.StartsWith("case ") &&
                !nextLine.StartsWith("default:") &&
                !nextLine.StartsWith("#") &&
                nextLine != "{")
            {
                var methodName = FindEnclosingMethod(lines, i);

                findings.Add(CreateFinding(
                    type: "unreachable",
                    className: className,
                    methodName: methodName,
                    severity: "WARNING",
                    confidence: "medium",
                    description: "Unreachable code after return/throw statement",
                    reasoning: "Code after an unconditional return or throw will never execute",
                    codeSnippet: GetCodeContext(lines, i, 2),
                    filePath: filePath,
                    lineNumber: i + 2,
                    potentialFix: "Remove the unreachable code or fix the control flow",
                    fileHash: hash
                ));
            }
        }

        return findings;
    }

    /// <summary>
    /// Detects suspicious code patterns.
    /// </summary>
    private List<GameCodeFinding> DetectSuspiciousPatterns(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();

        // Floating point equality checks
        var fpEqualityPattern = new Regex(@"(\w+)\s*==\s*([\d\.]+f?)\s*(?:&&|\|\||;|\))", RegexOptions.IgnoreCase);
        foreach (Match match in fpEqualityPattern.Matches(content))
        {
            var value = match.Groups[2].Value;
            if (value.Contains(".") || value.EndsWith("f"))
            {
                var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                var methodName = FindEnclosingMethod(lines, lineNumber - 1);

                findings.Add(CreateFinding(
                    type: "suspicious",
                    className: className,
                    methodName: methodName,
                    severity: "INFO",
                    confidence: "low",
                    description: "Floating point equality comparison",
                    reasoning: "Comparing floats with == can fail due to precision issues",
                    codeSnippet: GetCodeContext(lines, lineNumber - 1, 1),
                    filePath: filePath,
                    lineNumber: lineNumber,
                    potentialFix: "Use Mathf.Approximately() or tolerance-based comparison",
                    fileHash: hash
                ));
            }
        }

        // String comparison without StringComparison
        var stringComparePattern = new Regex(@"\.Equals\s*\(\s*""[^""]*""\s*\)|==\s*""[^""]*""", RegexOptions.Multiline);
        foreach (Match match in stringComparePattern.Matches(content))
        {
            // Skip if it's already using StringComparison
            var context = content.Substring(Math.Max(0, match.Index - 20), Math.Min(match.Length + 40, content.Length - Math.Max(0, match.Index - 20)));
            if (context.Contains("StringComparison"))
                continue;

            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
            var methodName = FindEnclosingMethod(lines, lineNumber - 1);

            // This is very common and usually fine - mark as very low confidence
            // Only add if it looks like it might be a case-sensitivity issue
            if (context.ToLower().Contains("name") || context.ToLower().Contains("id"))
            {
                findings.Add(CreateFinding(
                    type: "suspicious",
                    className: className,
                    methodName: methodName,
                    severity: "INFO",
                    confidence: "low",
                    description: "String comparison may be case-sensitive",
                    reasoning: "String comparisons without StringComparison may behave unexpectedly",
                    codeSnippet: GetCodeContext(lines, lineNumber - 1, 1),
                    filePath: filePath,
                    lineNumber: lineNumber,
                    potentialFix: "Consider using StringComparison.OrdinalIgnoreCase if case shouldn't matter",
                    fileHash: hash
                ));
            }
        }

        return findings;
    }

    /// <summary>
    /// Detects potential secret or hidden features.
    /// </summary>
    private List<GameCodeFinding> DetectSecretFeatures(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();

        // Debug flags
        var debugFlagPattern = new Regex(@"(?:static\s+)?bool\s+(\w*[Dd]ebug\w*|is[Dd]ebug|enable[Dd]ebug)\s*[=;]", RegexOptions.Multiline);
        foreach (Match match in debugFlagPattern.Matches(content))
        {
            var flagName = match.Groups[1].Value;
            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            findings.Add(CreateFinding(
                type: "secret",
                className: className,
                methodName: null,
                severity: "OPPORTUNITY",
                confidence: "medium",
                description: $"Debug flag: {flagName}",
                reasoning: "This flag might enable debug features when set to true",
                codeSnippet: GetCodeContext(lines, lineNumber - 1, 1),
                filePath: filePath,
                lineNumber: lineNumber,
                potentialFix: "Could be enabled via Harmony patch for debugging",
                fileHash: hash
            ));
        }

        // Console command patterns
        var consoleCommandPattern = new Regex(@"new\s+ConsoleCmdAbstract\s*\(\s*""([^""]+)""", RegexOptions.Multiline);
        foreach (Match match in consoleCommandPattern.Matches(content))
        {
            var commandName = match.Groups[1].Value;
            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            findings.Add(CreateFinding(
                type: "secret",
                className: className,
                methodName: null,
                severity: "OPPORTUNITY",
                confidence: "high",
                description: $"Console command: {commandName}",
                reasoning: "This console command may provide useful functionality",
                codeSnippet: GetCodeContext(lines, lineNumber - 1, 2),
                filePath: filePath,
                lineNumber: lineNumber,
                potentialFix: "Can be used in-game via the console",
                fileHash: hash
            ));
        }

        // Conditional compilation blocks
        var conditionalPattern = new Regex(@"#if\s+(DEBUG|DEVELOPMENT|EDITOR|UNITY_EDITOR)", RegexOptions.Multiline);
        foreach (Match match in conditionalPattern.Matches(content))
        {
            var condition = match.Groups[1].Value;
            var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            findings.Add(CreateFinding(
                type: "secret",
                className: className,
                methodName: null,
                severity: "OPPORTUNITY",
                confidence: "high",
                description: $"Conditional code block: #{match.Value.Trim()}",
                reasoning: "Code in this block is only compiled in special builds",
                codeSnippet: GetCodeContext(lines, lineNumber - 1, 3),
                filePath: filePath,
                lineNumber: lineNumber,
                potentialFix: "May contain developer tools or debug features",
                fileHash: hash
            ));
        }

        return findings;
    }

    /// <summary>
    /// Detects hardcoded entity references (buffs, items, blocks, sounds, etc.).
    /// These are important for modders to know what entities are referenced in code.
    /// </summary>
    private List<GameCodeFinding> DetectHardcodedEntities(string content, string[] lines, string filePath, string hash, string className)
    {
        var findings = new List<GameCodeFinding>();
        var seenEntities = new HashSet<string>(); // Dedupe within same file
        
        var matches = EntityEnricher.DetectHardcodedEntities(content, lines);
        
        foreach (var match in matches)
        {
            // Dedupe by entity name within same file
            var key = $"{match.EntityType}:{match.EntityName}";
            if (seenEntities.Contains(key))
                continue;
            seenEntities.Add(key);

            var methodName = FindEnclosingMethod(lines, match.LineNumber - 1);

            // Build related_entities JSON for enrichment later
            var relatedEntities = JsonSerializer.Serialize(new
            {
                entity_name = match.EntityName,
                entity_type = match.EntityType
            });

            findings.Add(CreateFinding(
                type: "hardcoded_entity",
                className: className,
                methodName: methodName,
                severity: "INFO",
                confidence: "high",
                description: $"Hardcoded {match.EntityType}: {match.EntityName}",
                reasoning: $"Code references '{match.EntityName}' directly - cannot be changed via XML alone",
                codeSnippet: GetCodeContext(lines, match.LineNumber - 1, 2),
                filePath: filePath,
                lineNumber: match.LineNumber,
                potentialFix: "Use Harmony to patch the method if you need to change this reference",
                fileHash: hash,
                relatedEntities: relatedEntities
            ));
        }

        return findings;
    }

    // Helper methods

    private static string ExtractClassName(string content)
    {
        var match = Regex.Match(content, @"(?:class|struct)\s+(\w+)");
        return match.Success ? match.Groups[1].Value : "Unknown";
    }

    private static string? FindEnclosingMethod(string[] lines, int lineIndex)
    {
        // Look backwards for a method or property declaration
        // Handles: public, private, protected, internal
        // Modifiers: static, virtual, override, abstract, sealed, async, new, extern, unsafe, partial
        for (int i = lineIndex; i >= 0; i--)
        {
            // Try to match a method declaration (has parentheses)
            var methodMatch = Regex.Match(lines[i], 
                @"(?:public|private|protected|internal)\s+" +
                @"(?:(?:static|virtual|override|abstract|sealed|async|new|extern|unsafe|partial)\s+)*" +
                @"(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\(");
            if (methodMatch.Success)
                return methodMatch.Groups[1].Value;

            // Try to match a property declaration (has => or { after name, no parentheses)
            // Pattern: [modifiers] ReturnType PropertyName { OR [modifiers] ReturnType PropertyName =>
            var propMatch = Regex.Match(lines[i],
                @"(?:public|private|protected|internal)\s+" +
                @"(?:(?:static|virtual|override|abstract|sealed|new|extern|unsafe)\s+)*" +
                @"(?:\w+(?:<[^>]+>)?(?:\?)?(?:\[\])?)\s+(\w+)\s*(?:=>|\{|$)");
            if (propMatch.Success && !lines[i].Contains('('))  // Ensure it's not a method
                return propMatch.Groups[1].Value + " (property)";

            // Also check for getter/setter keywords to identify property context
            if (Regex.IsMatch(lines[i].Trim(), @"^(?:get|set)\s*(?:\{|=>)"))
            {
                // We're inside a property accessor, continue looking backwards for property name
                continue;
            }

            // Stop if we hit a class declaration
            if (Regex.IsMatch(lines[i], @"(?:class|struct|interface)\s+\w+"))
                break;
        }
        return null;
    }

    private static string GetCodeContext(string[] lines, int lineIndex, int contextLines)
    {
        var start = Math.Max(0, lineIndex - contextLines);
        var end = Math.Min(lines.Length - 1, lineIndex + contextLines);

        var result = new StringBuilder();
        for (int i = start; i <= end; i++)
        {
            var marker = i == lineIndex ? ">>> " : "    ";
            result.AppendLine($"{marker}{lines[i].TrimEnd()}");
        }

        return result.ToString().TrimEnd();
    }

    private static string ComputeHash(string content)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = md5.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private GameCodeFinding CreateFinding(
        string type, string className, string? methodName, string severity, string confidence,
        string description, string reasoning, string codeSnippet, string filePath,
        int lineNumber, string potentialFix, string fileHash,
        bool isUnityMagic = false, bool isReflectionTarget = false, string? relatedEntities = null)
    {
        return new GameCodeFinding(
            Id: 0,
            AnalysisType: type,
            ClassName: className,
            MethodName: methodName,
            Severity: severity,
            Confidence: confidence,
            Description: description,
            Reasoning: reasoning,
            CodeSnippet: codeSnippet,
            FilePath: filePath,
            LineNumber: lineNumber,
            PotentialFix: potentialFix,
            RelatedEntities: relatedEntities,
            FileHash: fileHash,
            IsUnityMagic: isUnityMagic,
            IsReflectionTarget: isReflectionTarget
        );
    }

    private void ClearFileFindings(string filePath)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM game_code_analysis WHERE file_path = $path";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.ExecuteNonQuery();
    }

    private void PersistFinding(GameCodeFinding finding)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO game_code_analysis
            (analysis_type, class_name, method_name, severity, confidence,
             description, reasoning, code_snippet, file_path, line_number,
             potential_fix, related_entities, file_hash, is_unity_magic, is_reflection_target)
            VALUES ($type, $class, $method, $severity, $confidence,
                    $desc, $reason, $snippet, $path, $line,
                    $fix, $related, $hash, $magic, $reflect)";

        cmd.Parameters.AddWithValue("$type", finding.AnalysisType);
        cmd.Parameters.AddWithValue("$class", finding.ClassName);
        cmd.Parameters.AddWithValue("$method", finding.MethodName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$severity", finding.Severity);
        cmd.Parameters.AddWithValue("$confidence", finding.Confidence);
        cmd.Parameters.AddWithValue("$desc", finding.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", finding.Reasoning ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$snippet", finding.CodeSnippet ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$path", finding.FilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$line", finding.LineNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$fix", finding.PotentialFix ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$related", finding.RelatedEntities ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", finding.FileHash ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$magic", finding.IsUnityMagic ? 1 : 0);
        cmd.Parameters.AddWithValue("$reflect", finding.IsReflectionTarget ? 1 : 0);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets a summary of all findings by type and severity.
    /// </summary>
    public static GameCodeAnalysisSummary GetSummary(SqliteConnection db)
    {
        var summary = new GameCodeAnalysisSummary();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT analysis_type, severity, confidence, COUNT(*) as count
            FROM game_code_analysis
            GROUP BY analysis_type, severity, confidence
            ORDER BY
                CASE severity WHEN 'BUG' THEN 1 WHEN 'WARNING' THEN 2 WHEN 'INFO' THEN 3 ELSE 4 END,
                CASE confidence WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            summary.TypeCounts[(reader.GetString(0), reader.GetString(1))] = reader.GetInt32(3);

            switch (reader.GetString(1))
            {
                case "BUG": summary.BugCount += reader.GetInt32(3); break;
                case "WARNING": summary.WarningCount += reader.GetInt32(3); break;
                case "INFO": summary.InfoCount += reader.GetInt32(3); break;
                case "OPPORTUNITY": summary.OpportunityCount += reader.GetInt32(3); break;
            }
        }

        summary.TotalCount = summary.BugCount + summary.WarningCount + summary.InfoCount + summary.OpportunityCount;
        return summary;
    }
}

/// <summary>
/// Summary of game code analysis findings.
/// </summary>
public class GameCodeAnalysisSummary
{
    public int TotalCount { get; set; }
    public int BugCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int OpportunityCount { get; set; }
    public Dictionary<(string Type, string Severity), int> TypeCounts { get; } = new();
}
