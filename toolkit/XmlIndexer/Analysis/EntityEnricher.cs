using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XmlIndexer.Utils;

namespace XmlIndexer.Analysis;

/// <summary>
/// Enriches game code findings with XML cross-references, caller analysis, 
/// reachability chains, and semantic context.
/// Modular design - each enrichment method is independent and testable.
/// </summary>
public class EntityEnricher
{
    private readonly SqliteConnection _db;
    private readonly string _codebasePath;
    
    // Cached lookups for performance
    private Dictionary<string, XmlEntityInfo>? _xmlEntities;
    private Dictionary<string, List<CallerInfo>>? _methodCallers;
    
    public EntityEnricher(SqliteConnection db, string codebasePath)
    {
        _db = db;
        _codebasePath = codebasePath;
    }

    #region Main Enrichment Entry Point

    /// <summary>
    /// Enriches a finding with all available context.
    /// Returns JSON strings for each enrichment field.
    /// </summary>
    public EnrichmentResult Enrich(
        string? analysisType,
        string? className,
        string? methodName,
        string? filePath,
        int? lineNumber,
        string? codeSnippet,
        string? relatedEntities)
    {
        var result = new EnrichmentResult();

        // Parse existing related_entities if present
        string? entityName = null;
        string? entityType = null;
        
        if (!string.IsNullOrEmpty(relatedEntities))
        {
            try
            {
                using var doc = JsonDocument.Parse(relatedEntities);
                if (doc.RootElement.TryGetProperty("entity_name", out var en))
                    entityName = en.GetString();
                if (doc.RootElement.TryGetProperty("entity_type", out var et))
                    entityType = et.GetString();
            }
            catch { }
        }

        // For hardcoded_entity findings, extract entity name from description/code
        if (analysisType == "hardcoded_entity" && string.IsNullOrEmpty(entityName))
        {
            entityName = ExtractEntityNameFromCode(codeSnippet);
            entityType = InferEntityType(codeSnippet);
        }

        // 1. XML Cross-reference
        if (!string.IsNullOrEmpty(entityName))
        {
            var xmlInfo = LookupXmlEntity(entityName, entityType);
            result.EntityName = entityName;
            result.XmlStatus = xmlInfo.Found ? "found" : "code_only";
            result.XmlFile = xmlInfo.XmlFile;
            
            // 2. Fuzzy matches for code-only entities
            if (!xmlInfo.Found)
            {
                result.FuzzyMatches = FindFuzzyMatches(entityName, entityType);
            }
        }

        // 3. Caller enrichment (who calls this method)
        if (!string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(methodName))
        {
            var callers = GetMethodCallers(className, methodName);
            result.DeadCodeAnalysis = BuildDeadCodeAnalysis(callers, className, methodName);
            
            // 4. Usage level classification
            result.UsageLevel = ClassifyUsageLevel(callers, className);
            
            // 5. Reachability / call chains
            result.Reachability = BuildReachability(callers, className, methodName);
        }

        // 6. Semantic context
        result.SemanticContext = BuildSemanticContext(analysisType, entityName, entityType, codeSnippet);

        // 7. Source context (full method body)
        if (!string.IsNullOrEmpty(filePath) && lineNumber.HasValue)
        {
            result.SourceContext = ExtractSourceContext(filePath, lineNumber.Value, methodName);
        }

        // 8. Call graph enrichment (from consolidated cg_* tables)
        if (!string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(methodName))
        {
            // Get methods that this method calls
            result.Callees = GetMethodCallees(className, methodName);
        }
        
        // 9. Type hierarchy (base classes and interfaces)
        if (!string.IsNullOrEmpty(className))
        {
            result.TypeHierarchy = GetTypeHierarchy(className);
        }

        return result;
    }

    #endregion

    #region Entity Detection (for GameCodeAnalyzer)

    /// <summary>
    /// Scans a file for hardcoded entity references.
    /// Returns findings for each detected entity.
    /// </summary>
    public static List<HardcodedEntityMatch> DetectHardcodedEntities(string content, string[] lines)
    {
        var matches = new List<HardcodedEntityMatch>();
        var patterns = CSharpAnalyzer.GetXmlDependencyPatterns();

        foreach (var (pattern, type, nameGroup) in patterns)
        {
            // Skip non-entity patterns (inheritance, localization, etc.)
            if (type.StartsWith("extends_") || type.StartsWith("implements_") || type == "localization")
                continue;

            var regex = new Regex(pattern, RegexOptions.Compiled);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                // Skip comments
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                foreach (Match match in regex.Matches(line))
                {
                    var name = match.Groups[nameGroup].Value;
                    
                    // Skip invalid matches
                    if (string.IsNullOrEmpty(name) || name.Contains("{") || name.Contains("+"))
                        continue;

                    // Skip obvious variables/parameters
                    if (name.Length < 3 || name == "null" || name == "true" || name == "false")
                        continue;

                    matches.Add(new HardcodedEntityMatch(
                        EntityName: name,
                        EntityType: type,
                        LineNumber: i + 1,
                        CodeSnippet: line.Trim(),
                        FullMatch: match.Value
                    ));
                }
            }
        }

        return matches;
    }

    #endregion

    #region XML Cross-Reference

    private XmlEntityInfo LookupXmlEntity(string entityName, string? entityType)
    {
        EnsureXmlEntitiesLoaded();

        // Try exact match first
        var key = $"{entityType}:{entityName}".ToLowerInvariant();
        if (_xmlEntities!.TryGetValue(key, out var info))
            return info;

        // Try without type prefix
        foreach (var kvp in _xmlEntities)
        {
            if (kvp.Key.EndsWith($":{entityName.ToLowerInvariant()}"))
                return kvp.Value;
        }

        // Try case-insensitive name match
        var lowerName = entityName.ToLowerInvariant();
        foreach (var kvp in _xmlEntities)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 2 && parts[1].Equals(lowerName, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return new XmlEntityInfo(false, null, null);
    }

    private void EnsureXmlEntitiesLoaded()
    {
        if (_xmlEntities != null) return;

        _xmlEntities = new Dictionary<string, XmlEntityInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT definition_type, name, file_path 
                FROM xml_definitions 
                WHERE name IS NOT NULL";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                var file = reader.IsDBNull(2) ? null : reader.GetString(2);

                var key = $"{type}:{name}".ToLowerInvariant();
                _xmlEntities[key] = new XmlEntityInfo(true, file, type);
            }
        }
        catch { /* Table may not exist */ }
    }

    #endregion

    #region Fuzzy Matching

    private string? FindFuzzyMatches(string entityName, string? entityType)
    {
        EnsureXmlEntitiesLoaded();

        var matches = new List<object>();
        var lowerName = entityName.ToLowerInvariant();
        
        // Tokenize entity name for better matching (e.g., "concreteBlock" -> ["concrete", "block"])
        var sourceTokens = TokenizeCamelCase(entityName);

        foreach (var kvp in _xmlEntities!)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length != 2) continue;

            var xmlType = parts[0];
            var xmlName = parts[1];

            // Don't filter by type for fuzzy matches - we want cross-type suggestions
            // e.g., "concreteBlock" (missing item) -> "bridgeConcreteBlock" (existing block)

            var lowerXmlName = xmlName.ToLowerInvariant();
            var score = CalculateFuzzyScore(lowerName, lowerXmlName);
            
            // Also check token-based similarity
            var targetTokens = TokenizeCamelCase(xmlName);
            var tokenScore = CalculateTokenScore(sourceTokens, targetTokens);
            var tokenReason = GetTokenReason(sourceTokens, targetTokens);
            
            // Use the better score
            var bestScore = Math.Max(score, tokenScore);
            
            if (bestScore >= 0.4) // Lower threshold to catch more potential matches
            {
                var reason = tokenScore > score && !string.IsNullOrEmpty(tokenReason) 
                    ? tokenReason 
                    : GetFuzzyReason(lowerName, lowerXmlName);
                    
                matches.Add(new
                {
                    name = xmlName,
                    type = xmlType,
                    file = kvp.Value.XmlFile ?? $"{xmlType}s.xml",
                    score = Math.Round(bestScore, 2),
                    reason = reason
                });
            }
        }

        if (matches.Count == 0) return null;

        // Sort by score descending, take top 5
        var topMatches = matches
            .OrderByDescending(m => ((dynamic)m).score)
            .Take(5)
            .ToList();

        return JsonSerializer.Serialize(topMatches);
    }
    
    private static List<string> TokenizeCamelCase(string name)
    {
        // Split camelCase/PascalCase into tokens
        var tokens = new List<string>();
        var current = new StringBuilder();
        
        foreach (var c in name)
        {
            if (char.IsUpper(c) && current.Length > 0)
            {
                tokens.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
            if (char.IsLetterOrDigit(c))
                current.Append(c);
        }
        
        if (current.Length > 0)
            tokens.Add(current.ToString().ToLowerInvariant());
        
        return tokens;
    }
    
    private static double CalculateTokenScore(List<string> source, List<string> target)
    {
        if (source.Count == 0 || target.Count == 0) return 0;
        
        var matches = 0;
        foreach (var s in source)
        {
            if (target.Any(t => t.Contains(s) || s.Contains(t)))
                matches++;
        }
        
        return (double)matches / Math.Max(source.Count, target.Count);
    }
    
    private static string GetTokenReason(List<string> source, List<string> target)
    {
        var shared = source.Where(s => target.Any(t => t.Equals(s, StringComparison.OrdinalIgnoreCase))).ToList();
        if (shared.Count > 0)
        {
            var overlap = (double)shared.Count / Math.Max(source.Count, target.Count);
            var overlapDesc = overlap >= 0.5 ? "High" : "Moderate";
            return $"Shared tokens: {string.Join(", ", shared)}; {overlapDesc} token overlap";
        }
        return "";
    }

    private static double CalculateFuzzyScore(string source, string target)
    {
        // Levenshtein distance-based similarity
        var distance = LevenshteinDistance(source, target);
        var maxLen = Math.Max(source.Length, target.Length);
        if (maxLen == 0) return 1.0;

        var similarity = 1.0 - ((double)distance / maxLen);

        // Bonus for substring matches
        if (target.Contains(source) || source.Contains(target))
            similarity = Math.Max(similarity, 0.8);

        // Bonus for same prefix
        var commonPrefix = 0;
        for (int i = 0; i < Math.Min(source.Length, target.Length); i++)
        {
            if (source[i] == target[i]) commonPrefix++;
            else break;
        }
        if (commonPrefix >= 4)
            similarity = Math.Max(similarity, 0.7 + (commonPrefix * 0.02));

        return similarity;
    }

    private static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var m = source.Length;
        var n = target.Length;
        var d = new int[m + 1, n + 1];

        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }

    private static string GetFuzzyReason(string source, string target)
    {
        if (target.Contains(source)) return "Contains search term";
        if (source.Contains(target)) return "Search term contains this";
        
        var distance = LevenshteinDistance(source, target);
        if (distance <= 2) return "Similar spelling (possible typo)";
        if (distance <= 4) return "Close match";
        
        // Check for common prefix
        var commonPrefix = 0;
        for (int i = 0; i < Math.Min(source.Length, target.Length); i++)
        {
            if (source[i] == target[i]) commonPrefix++;
            else break;
        }
        if (commonPrefix >= 4) return $"Same prefix ({source.Substring(0, commonPrefix)}...)";

        return "Partial match";
    }

    #endregion

    #region Caller Enrichment

    private List<CallerInfo> GetMethodCallers(string className, string methodName)
    {
        EnsureMethodCallersLoaded();

        var key = $"{className}.{methodName}".ToLowerInvariant();
        if (_methodCallers!.TryGetValue(key, out var callers))
            return callers;

        return new List<CallerInfo>();
    }

    private void EnsureMethodCallersLoaded()
    {
        if (_methodCallers != null) return;

        _methodCallers = new Dictionary<string, List<CallerInfo>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT target_class, target_method, caller_file, caller_class, caller_method, line_number, code_snippet
                FROM method_calls
                WHERE target_class IS NOT NULL AND target_method IS NOT NULL";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var targetClass = reader.GetString(0);
                var targetMethod = reader.GetString(1);
                var key = $"{targetClass}.{targetMethod}".ToLowerInvariant();

                if (!_methodCallers.ContainsKey(key))
                    _methodCallers[key] = new List<CallerInfo>();

                var filePath = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var fileName = Path.GetFileName(filePath);

                _methodCallers[key].Add(new CallerInfo(
                    CallerClass: reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                    CallerMethod: reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
                    FilePath: filePath,
                    FileName: fileName,
                    LineNumber: reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    CodeSnippet: reader.IsDBNull(6) ? "" : reader.GetString(6)
                ));
            }
        }
        catch { /* Table may not exist */ }
    }

    private string? BuildDeadCodeAnalysis(List<CallerInfo> callers, string className, string? methodName)
    {
        var externalCallers = callers.Where(c => 
            !c.CallerClass.Equals(className, StringComparison.OrdinalIgnoreCase)).ToList();

        var isLikelyDead = callers.Count == 0;
        var confidence = callers.Count == 0 ? 0.8 : 
                        externalCallers.Count == 0 ? 0.5 : 0.1;

        var reasoning = callers.Count == 0 
            ? "No callers found in decompiled code"
            : externalCallers.Count == 0 
                ? "Only called internally within same class"
                : $"Called by {externalCallers.Count} external location(s)";

        // Check for Unity magic methods
        if (!string.IsNullOrEmpty(methodName) && CSharpAnalyzer.UnityMagicMethods.Contains(methodName))
        {
            isLikelyDead = false;
            confidence = 0.1;
            reasoning = "Unity lifecycle method - called by engine";
        }

        var methodCallers = callers.Take(10).Select(c => new
        {
            caller_class = c.CallerClass,
            caller_method = c.CallerMethod,
            file_path = c.FilePath,
            line_number = c.LineNumber,
            code_snippet = c.CodeSnippet
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            is_likely_dead = isLikelyDead,
            confidence = confidence,
            reasoning = reasoning,
            caller_count = callers.Count,
            external_caller_count = externalCallers.Count,
            method_callers = methodCallers
        });
    }

    #endregion

    #region Usage Level Classification

    private string ClassifyUsageLevel(List<CallerInfo> callers, string className)
    {
        var externalCallers = callers.Where(c => 
            !c.CallerClass.Equals(className, StringComparison.OrdinalIgnoreCase)).ToList();

        return externalCallers.Count switch
        {
            >= 5 => "active",
            >= 2 => "moderate",
            1 => "low",
            0 when callers.Count > 0 => "internal",
            0 => "unused",
            _ => "unknown"
        };
    }

    #endregion

    #region Reachability / Call Chains

    private string? BuildReachability(List<CallerInfo> callers, string className, string methodName)
    {
        var externalCallers = callers.Where(c => 
            !c.CallerClass.Equals(className, StringComparison.OrdinalIgnoreCase)).ToList();

        var usageLevel = ClassifyUsageLevel(callers, className);
        var confidence = usageLevel switch
        {
            "active" => 0.95,
            "moderate" => 0.85,
            "low" => 0.7,
            "internal" => 0.5,
            "unused" => 0.8,
            _ => 0.3
        };

        var reasoning = usageLevel switch
        {
            "active" => $"Actively used - called from {externalCallers.Count} external locations",
            "moderate" => $"Moderately used - called from {externalCallers.Count} external locations",
            "low" => "Low usage - only 1 external caller",
            "internal" => "Internal only - called within same class",
            "unused" => "No callers found in static analysis",
            _ => "Unable to determine usage"
        };

        // Build simplified call chains (up to 3 levels)
        var callChains = BuildCallChains(callers, className, methodName, maxDepth: 3, maxChains: 5);

        return JsonSerializer.Serialize(new
        {
            usage_level = usageLevel,
            confidence = confidence,
            reasoning = reasoning,
            caller_count = callers.Count,
            call_chains = callChains
        });
    }

    private List<List<object>> BuildCallChains(List<CallerInfo> callers, string className, string methodName, int maxDepth, int maxChains)
    {
        var chains = new List<List<object>>();
        
        foreach (var caller in callers.Take(maxChains))
        {
            var chain = new List<object>();

            // Check if caller is an entry point
            var entryPointType = DetectEntryPointType(caller.CallerMethod);
            if (entryPointType != null)
            {
                chain.Add(new
                {
                    @class = caller.CallerClass,
                    method = caller.CallerMethod,
                    entry_point_type = entryPointType
                });
            }
            else
            {
                // Try to find who calls the caller (one level up)
                var parentCallers = GetMethodCallers(caller.CallerClass, caller.CallerMethod);
                if (parentCallers.Count > 0)
                {
                    var parent = parentCallers.First();
                    var parentEntryType = DetectEntryPointType(parent.CallerMethod);
                    
                    chain.Add(new
                    {
                        @class = parent.CallerClass,
                        method = parent.CallerMethod,
                        entry_point_type = parentEntryType
                    });
                }

                chain.Add(new
                {
                    @class = caller.CallerClass,
                    method = caller.CallerMethod,
                    entry_point_type = (string?)null
                });
            }

            // Add the target method
            chain.Add(new
            {
                @class = className,
                method = methodName,
                entry_point_type = (string?)null
            });

            if (chain.Count > 0)
                chains.Add(chain);
        }

        return chains;
    }

    private static string? DetectEntryPointType(string? methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return null;

        // Unity lifecycle methods
        if (CSharpAnalyzer.UnityMagicMethods.Contains(methodName))
            return "unity";

        // Console commands
        if (methodName.StartsWith("Cmd", StringComparison.OrdinalIgnoreCase) ||
            methodName.Equals("Execute", StringComparison.OrdinalIgnoreCase))
            return "console";

        // Event handlers
        if (methodName.StartsWith("On", StringComparison.OrdinalIgnoreCase))
            return "event";

        // Main entry
        if (methodName.Equals("Main", StringComparison.OrdinalIgnoreCase))
            return "main";

        return null;
    }

    #endregion

    #region Semantic Context

    private string? BuildSemanticContext(string? analysisType, string? entityName, string? entityType, string? codeSnippet)
    {
        var category = InferCategory(analysisType, entityType, entityName);
        var moddability = AssessModdability(analysisType, category, codeSnippet);
        var reasoning = GenerateReasoning(category, entityType, entityName, codeSnippet);
        var advice = GenerateAdvice(category, moddability, analysisType);

        var relatedConcerns = new List<string>();
        if (analysisType == "hardcoded_entity")
        {
            relatedConcerns.Add("Hardcoded references cannot be changed via XML");
            if (entityType == "buff")
                relatedConcerns.Add("Consider Harmony patch to intercept buff application");
            if (entityType == "item")
                relatedConcerns.Add("Item replacements may require ItemClass patch");
        }

        return JsonSerializer.Serialize(new
        {
            category = category,
            reasoning = reasoning,
            moddability_level = moddability,
            actionable_advice = advice,
            related_concerns = relatedConcerns
        });
    }

    private static string InferCategory(string? analysisType, string? entityType, string? entityName)
    {
        if (analysisType == "hardcoded_entity")
        {
            return entityType switch
            {
                "buff" => "Status Effect",
                "item" => "Item Reference",
                "block" => "Block Reference",
                "entity_class" => "Entity Spawn",
                "sound" => "Audio Reference",
                "recipe" => "Crafting",
                "quest" => "Quest System",
                _ => "Game Entity"
            };
        }

        return analysisType switch
        {
            "hookable_event" => "Extension Point",
            "console_command" => "Debug Tool",
            "singleton_access" => "System Access",
            "stub_method" => "Incomplete Implementation",
            "unimplemented" => "Missing Feature",
            "empty_catch" => "Error Handling",
            "todo" => "Developer Note",
            _ => "Code Pattern"
        };
    }

    private static string AssessModdability(string? analysisType, string category, string? codeSnippet)
    {
        if (analysisType == "hardcoded_entity")
            return "requires_harmony";

        if (analysisType == "hookable_event")
            return "safe_to_extend";

        if (analysisType == "stub_method")
            return "good_hook_point";

        return "review_required";
    }

    private static string GenerateReasoning(string category, string? entityType, string? entityName, string? codeSnippet)
    {
        if (!string.IsNullOrEmpty(entityName))
        {
            return $"Code references '{entityName}' ({entityType ?? "unknown type"}) directly in source";
        }

        // Use specific reasoning for each category to avoid duplication like "code pattern pattern"
        return category switch
        {
            "Code Pattern" => "Pattern detected in game code that may affect mod compatibility",
            "Extension Point" => "This is an extension point where mods can safely hook in",
            "Debug Tool" => "Console command or debug utility - only accessible via F1 console",
            "System Access" => "Accesses a game system singleton - common but may have side effects",
            "Incomplete Implementation" => "Method is incomplete or stubbed - potential hook point for mods",
            "Missing Feature" => "Method throws NotImplementedException - safe to ignore unless called",
            "Error Handling" => "Error handling pattern detected - review for edge cases",
            "Developer Note" => "Contains TODO or developer comment indicating incomplete work",
            "Status Effect" => "References a buff/debuff status effect",
            "Item Reference" => "References an item by name in code",
            "Block Reference" => "References a block type in code",
            "Entity Spawn" => "Spawns or references an entity class",
            "Audio Reference" => "References a sound effect or audio",
            "Crafting" => "References a crafting recipe",
            "Quest System" => "References quest or progression system",
            "Game Entity" => "References a game entity directly in code",
            _ => $"Found {category.ToLower()} in game code"
        };
    }

    private static string GenerateAdvice(string category, string moddability, string? analysisType)
    {
        if (analysisType == "hardcoded_entity")
        {
            return "Use Harmony to patch the method and redirect to your custom entity, or ensure the referenced entity exists in your mod's XML";
        }

        if (analysisType == "hookable_event")
        {
            return "Safe to extend via Harmony Postfix - add custom behavior after vanilla logic";
        }

        if (analysisType == "stub_method")
        {
            return "Good hook point - replace with Harmony Prefix returning false to inject custom logic";
        }

        return "Review the code context to determine best modding approach";
    }

    #endregion

    #region Source Context

    private string? ExtractSourceContext(string filePath, int lineNumber, string? methodName)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var lines = File.ReadAllLines(filePath);
            if (lineNumber < 1 || lineNumber > lines.Length) return null;

            // Find method boundaries
            var (methodBody, startLine, endLine) = ExtractMethodBody(lines, lineNumber - 1, methodName);

            if (string.IsNullOrEmpty(methodBody)) return null;

            return JsonSerializer.Serialize(new
            {
                method_body = methodBody,
                method_start_line = startLine,
                method_end_line = endLine
            });
        }
        catch
        {
            return null;
        }
    }

    private static (string Body, int StartLine, int EndLine) ExtractMethodBody(string[] lines, int targetLine, string? methodName)
    {
        // Scan backwards to find method or property signature
        int startLine = targetLine;
        
        // Pattern for methods (has parentheses)
        var methodPattern = new Regex(@"(?:public|private|protected|internal)\s+(?:(?:static|virtual|override|abstract|sealed|async|new|extern|unsafe|partial)\s+)*(?:\w+(?:<[^>]+>)?)\s+\w+\s*\(");
        
        // Pattern for properties (no parentheses, has { or => after name)
        var propertyPattern = new Regex(@"(?:public|private|protected|internal)\s+(?:(?:static|virtual|override|abstract|sealed|new|extern|unsafe)\s+)*(?:\w+(?:<[^>]+>)?(?:\?)?(?:\[\])?)\s+\w+\s*(?:=>|\{|$)");

        for (int i = targetLine; i >= 0; i--)
        {
            // Check for method signature first
            if (methodPattern.IsMatch(lines[i]))
            {
                startLine = i;
                break;
            }
            
            // Check for property signature (no parentheses)
            if (propertyPattern.IsMatch(lines[i]) && !lines[i].Contains('('))
            {
                startLine = i;
                break;
            }
            
            // Stop if we hit a class declaration
            if (Regex.IsMatch(lines[i], @"(?:class|struct|interface)\s+\w+"))
                break;
        }

        // Find the opening brace
        int braceStart = startLine;
        for (int i = startLine; i < Math.Min(startLine + 5, lines.Length); i++)
        {
            if (lines[i].Contains('{'))
            {
                braceStart = i;
                break;
            }
        }

        // Track braces to find method end
        int depth = 0;
        int endLine = braceStart;
        bool foundOpen = false;

        for (int i = braceStart; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (char c in line)
            {
                if (c == '{') { depth++; foundOpen = true; }
                if (c == '}') depth--;
            }
            if (foundOpen && depth == 0)
            {
                endLine = i;
                break;
            }
        }

        // Build method body
        var sb = new StringBuilder();
        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            sb.AppendLine(lines[i]);
        }

        return (sb.ToString().TrimEnd(), startLine + 1, endLine + 1);
    }

    #endregion

    #region Call Graph Enrichment (from cg_* tables)

    /// <summary>
    /// Gets methods that this method calls (callees) from the consolidated call graph.
    /// </summary>
    private string? GetMethodCallees(string className, string methodName)
    {
        try
        {
            var callees = new List<object>();
            
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    callee_t.name as callee_class,
                    callee_m.name as callee_method,
                    callee_m.signature,
                    c.call_type,
                    c.line_number
                FROM cg_calls c
                JOIN cg_methods caller_m ON c.caller_id = caller_m.id
                JOIN cg_types caller_t ON caller_m.type_id = caller_t.id
                JOIN cg_methods callee_m ON c.callee_id = callee_m.id
                JOIN cg_types callee_t ON callee_m.type_id = callee_t.id
                WHERE caller_t.name = $className AND caller_m.name = $methodName
                LIMIT 20";
            
            cmd.Parameters.AddWithValue("$className", className);
            cmd.Parameters.AddWithValue("$methodName", methodName);
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                callees.Add(new
                {
                    callee_class = reader.GetString(0),
                    callee_method = reader.GetString(1),
                    signature = reader.IsDBNull(2) ? null : reader.GetString(2),
                    call_type = reader.IsDBNull(3) ? "direct" : reader.GetString(3),
                    line_number = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
                });
            }
            
            if (callees.Count == 0) return null;
            
            return JsonSerializer.Serialize(new
            {
                callee_count = callees.Count,
                callees = callees
            });
        }
        catch
        {
            return null; // cg_* tables may not exist
        }
    }

    /// <summary>
    /// Gets the type hierarchy (base classes and interfaces) for a class.
    /// </summary>
    private string? GetTypeHierarchy(string className)
    {
        try
        {
            // Get base class chain
            var baseClasses = new List<string>();
            var currentType = className;
            var maxDepth = 10; // Prevent infinite loops
            
            while (maxDepth-- > 0)
            {
                using var baseCmd = _db.CreateCommand();
                baseCmd.CommandText = @"
                    SELECT base_type FROM cg_types 
                    WHERE name = $name AND base_type IS NOT NULL AND base_type != ''";
                baseCmd.Parameters.AddWithValue("$name", currentType);
                
                var baseType = baseCmd.ExecuteScalar() as string;
                if (string.IsNullOrEmpty(baseType) || baseType == "object" || baseType == "Object")
                    break;
                    
                baseClasses.Add(baseType);
                currentType = baseType;
            }
            
            // Get implemented interfaces
            var interfaces = new List<string>();
            using var ifaceCmd = _db.CreateCommand();
            ifaceCmd.CommandText = @"
                SELECT i.interface_name 
                FROM cg_implements i
                JOIN cg_types t ON i.type_id = t.id
                WHERE t.name = $name";
            ifaceCmd.Parameters.AddWithValue("$name", className);
            
            using var reader = ifaceCmd.ExecuteReader();
            while (reader.Read())
            {
                interfaces.Add(reader.GetString(0));
            }
            
            if (baseClasses.Count == 0 && interfaces.Count == 0)
                return null;
            
            return JsonSerializer.Serialize(new
            {
                base_classes = baseClasses,
                interfaces = interfaces,
                depth = baseClasses.Count
            });
        }
        catch
        {
            return null; // cg_* tables may not exist
        }
    }

    #endregion

    #region Helpers

    private static string? ExtractEntityNameFromCode(string? codeSnippet)
    {
        if (string.IsNullOrEmpty(codeSnippet)) return null;

        // Look for quoted strings that look like entity names
        var stringPattern = new Regex(@"""([^""]{3,50})""");
        var match = stringPattern.Match(codeSnippet);
        if (match.Success)
        {
            var name = match.Groups[1].Value;
            // Skip obvious non-entity strings
            if (!name.Contains(" ") && !name.Contains("\\") && !name.StartsWith("http"))
                return name;
        }

        return null;
    }

    private static string? InferEntityType(string? codeSnippet)
    {
        if (string.IsNullOrEmpty(codeSnippet)) return null;

        if (codeSnippet.Contains("AddBuff") || codeSnippet.Contains("RemoveBuff") || codeSnippet.Contains("HasBuff") || codeSnippet.Contains("BuffManager") || codeSnippet.Contains("BuffClass"))
            return "buff";
        if (codeSnippet.Contains("ItemClass") || codeSnippet.Contains("ItemValue") || codeSnippet.Contains("GetItem"))
            return "item";
        if (codeSnippet.Contains("Block.") || codeSnippet.Contains("GetBlock"))
            return "block";
        if (codeSnippet.Contains("EntityClass") || codeSnippet.Contains("EntityFactory"))
            return "entity_class";
        if (codeSnippet.Contains("Play(") || codeSnippet.Contains("Audio") || codeSnippet.Contains("sound"))
            return "sound";

        return null;
    }

    #endregion
}

#region Records

public record EnrichmentResult
{
    public string? EntityName { get; set; }
    public string? XmlStatus { get; set; }
    public string? XmlFile { get; set; }
    public string? FuzzyMatches { get; set; }
    public string? DeadCodeAnalysis { get; set; }
    public string? SemanticContext { get; set; }
    public string? Reachability { get; set; }
    public string? SourceContext { get; set; }
    public string? UsageLevel { get; set; }
    // New fields from consolidated call graph
    public string? Callees { get; set; }       // Methods this method calls (from cg_calls)
    public string? TypeHierarchy { get; set; } // Base classes and interfaces (from cg_types/cg_implements)
}

public record HardcodedEntityMatch(
    string EntityName,
    string EntityType,
    int LineNumber,
    string CodeSnippet,
    string FullMatch
);

public record XmlEntityInfo(
    bool Found,
    string? XmlFile,
    string? EntityType
);

public record CallerInfo(
    string CallerClass,
    string CallerMethod,
    string FilePath,
    string FileName,
    int LineNumber,
    string CodeSnippet
);

#endregion
