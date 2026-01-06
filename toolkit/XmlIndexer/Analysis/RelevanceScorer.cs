using Microsoft.Data.Sqlite;

namespace XmlIndexer.Analysis;

/// <summary>
/// Computes relevance scores for game code analysis findings.
/// Scores help surface the most interesting/actionable findings in reports.
/// </summary>
public class RelevanceScorer
{
    private readonly SqliteConnection _db;
    private readonly Dictionary<string, double> _weights = new();
    private readonly List<(string Keyword, string Category, double Multiplier)> _keywords = new();
    
    // Entity type scores (ordered by modding interest)
    private static readonly Dictionary<string, int> EntityScores = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Item", 40 }, { "ItemClass", 40 },
        { "Block", 35 }, { "BlockClass", 35 },
        { "Entity", 30 }, { "EntityClass", 30 },
        { "Recipe", 25 },
        { "Buff", 20 }, { "Effect", 20 },
        { "Vehicle", 20 },
        { "Quest", 15 },
        { "Trader", 15 },
        { "UI", 10 }, { "Window", 10 }, { "XUi", 10 },
        { "Sound", 10 }, { "Audio", 10 }
    };
    
    public int FindingsScored { get; private set; }
    public int HighRelevanceCount { get; private set; }
    public int MediumRelevanceCount { get; private set; }
    public int LowRelevanceCount { get; private set; }

    public RelevanceScorer(SqliteConnection db)
    {
        _db = db;
        LoadWeights();
        LoadKeywords();
    }

    private void LoadWeights()
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT factor_name, weight FROM relevance_weights";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _weights[reader.GetString(0)] = reader.GetDouble(1);
            }
        }
        catch
        {
            // Use defaults if table doesn't exist
            _weights["connectivity"] = 1.0;
            _weights["entity"] = 1.2;
            _weights["mod"] = 1.5;
            _weights["keyword"] = 0.8;
        }
    }

    private void LoadKeywords()
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT keyword, category, multiplier FROM important_keywords";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _keywords.Add((reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
            }
        }
        catch
        {
            // Keywords table may not exist yet - scorer will work without them
        }
    }

    /// <summary>
    /// Computes relevance scores for all game code analysis findings.
    /// </summary>
    public void ComputeScores(bool force = false)
    {
        Console.WriteLine("  Computing relevance scores...");
        
        // Get all analysis findings that need scoring
        var findings = GetFindingsToScore(force);
        
        if (findings.Count == 0)
        {
            Console.WriteLine("  ✓ All findings already scored                       [Skip - Cached]");
            return;
        }

        // Clear existing scores if forcing
        if (force)
        {
            ClearAllScores();
        }

        using var transaction = _db.BeginTransaction();
        
        foreach (var finding in findings)
        {
            var scores = ComputeFindingScores(finding);
            PersistScores(finding.Id, scores);
            FindingsScored++;
            
            // Track distribution
            var total = scores.TotalScore;
            if (total >= 60) HighRelevanceCount++;
            else if (total >= 30) MediumRelevanceCount++;
            else LowRelevanceCount++;
        }
        
        transaction.Commit();
        
        Console.WriteLine($"  ✓ Scored {FindingsScored} findings: {HighRelevanceCount} high, {MediumRelevanceCount} medium, {LowRelevanceCount} low relevance");
    }

    private List<AnalysisFinding> GetFindingsToScore(bool force)
    {
        var findings = new List<AnalysisFinding>();
        
        using var cmd = _db.CreateCommand();
        
        if (force)
        {
            // Get all findings
            cmd.CommandText = @"
                SELECT id, analysis_type, class_name, method_name, severity, confidence,
                       description, file_path, line_number, related_entities
                FROM game_code_analysis";
        }
        else
        {
            // Get only findings without scores
            cmd.CommandText = @"
                SELECT g.id, g.analysis_type, g.class_name, g.method_name, g.severity, g.confidence,
                       g.description, g.file_path, g.line_number, g.related_entities
                FROM game_code_analysis g
                LEFT JOIN code_relevance r ON g.id = r.analysis_id
                WHERE r.id IS NULL";
        }
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            findings.Add(new AnalysisFinding
            {
                Id = reader.GetInt64(0),
                AnalysisType = reader.GetString(1),
                ClassName = reader.GetString(2),
                MethodName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Severity = reader.GetString(4),
                Confidence = reader.GetString(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                FilePath = reader.IsDBNull(7) ? null : reader.GetString(7),
                LineNumber = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                RelatedEntities = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        
        return findings;
    }

    private void ClearAllScores()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM code_relevance";
        cmd.ExecuteNonQuery();
    }

    private RelevanceScores ComputeFindingScores(AnalysisFinding finding)
    {
        var connectivity = ComputeConnectivityScore(finding);
        var entity = ComputeEntityScore(finding);
        var mod = ComputeModScore(finding);
        var keyword = ComputeKeywordScore(finding);
        var penalty = ComputeArtifactPenalty(finding);
        
        // Apply weights
        var weightedConnectivity = connectivity * _weights.GetValueOrDefault("connectivity", 1.0);
        var weightedEntity = entity * _weights.GetValueOrDefault("entity", 1.2);
        var weightedMod = mod * _weights.GetValueOrDefault("mod", 1.5);
        var weightedKeyword = keyword * _weights.GetValueOrDefault("keyword", 0.8);
        
        var total = (int)(weightedConnectivity + weightedEntity + weightedMod + weightedKeyword + penalty);
        
        // Clamp total to reasonable range
        total = Math.Max(-50, Math.Min(150, total));
        
        return new RelevanceScores
        {
            ConnectivityScore = connectivity,
            EntityScore = entity,
            ModScore = mod,
            KeywordScore = keyword,
            ArtifactPenalty = penalty,
            TotalScore = total
        };
    }

    /// <summary>
    /// Computes connectivity score based on usage level and caller count.
    /// Range: 0-100
    /// </summary>
    internal int ComputeConnectivityScore(AnalysisFinding finding)
    {
        var score = 0;
        
        // Check if we have enriched metadata with usage info
        var usageLevel = GetUsageLevel(finding.Id);
        
        switch (usageLevel?.ToUpperInvariant())
        {
            case "HIGH": score += 30; break;
            case "MEDIUM": score += 15; break;
            case "LOW": score += 5; break;
        }
        
        // Check caller count from call graph
        var callerCount = GetCallerCount(finding.ClassName, finding.MethodName);
        if (callerCount >= 5) score += 20;
        else if (callerCount >= 3) score += 10;
        else if (callerCount >= 1) score += 5;
        
        // Check if method is reachable from entry points
        if (IsReachableFromEntryPoints(finding.ClassName, finding.MethodName))
        {
            score += 10;
        }
        
        return Math.Min(100, score);
    }

    /// <summary>
    /// Computes entity score based on what game entity the code relates to.
    /// Range: 0-50
    /// </summary>
    internal int ComputeEntityScore(AnalysisFinding finding)
    {
        var score = 0;
        
        // Check class name for entity type patterns
        var className = finding.ClassName ?? "";
        
        foreach (var kvp in EntityScores)
        {
            if (className.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, kvp.Value);
            }
        }
        
        // Check related entities
        if (!string.IsNullOrEmpty(finding.RelatedEntities))
        {
            foreach (var kvp in EntityScores)
            {
                if (finding.RelatedEntities.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    score = Math.Max(score, kvp.Value);
                }
            }
        }
        
        return Math.Min(50, score);
    }

    /// <summary>
    /// Computes mod cross-reference score based on whether mods interact with this code.
    /// Range: 0-50
    /// </summary>
    internal int ComputeModScore(AnalysisFinding finding)
    {
        var score = 0;
        
        // Check for Harmony patches targeting this class/method
        if (IsHarmonyPatched(finding.ClassName, finding.MethodName))
        {
            score += 50;
        }
        
        // Check for mod XML references
        if (HasModXmlReferences(finding.ClassName))
        {
            score += 30;
        }
        
        // Check for mod C# dependencies
        if (HasModCSharpDependencies(finding.ClassName))
        {
            score += 40;
        }
        
        return Math.Min(50, score);
    }

    /// <summary>
    /// Computes keyword score based on important terms in class/method names.
    /// Range: -20 to +40
    /// </summary>
    internal int ComputeKeywordScore(AnalysisFinding finding)
    {
        var score = 0.0;
        var searchText = $"{finding.ClassName} {finding.MethodName}";
        
        foreach (var (keyword, category, multiplier) in _keywords)
        {
            if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                if (category == "artifact")
                {
                    // Artifact keywords are penalties (multiplier is negative)
                    score += multiplier * 20; // e.g., -0.5 * 20 = -10
                }
                else
                {
                    // Positive keywords boost score
                    score += multiplier * 15;
                }
            }
        }
        
        return Math.Max(-20, Math.Min(40, (int)score));
    }

    /// <summary>
    /// Computes artifact penalty for debug/test/unreachable code.
    /// Range: -40 to 0
    /// </summary>
    internal int ComputeArtifactPenalty(AnalysisFinding finding)
    {
        var penalty = 0;
        
        // Unreachable code gets significant penalty
        if (finding.AnalysisType == "Unreachable" || 
            (finding.Description?.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            penalty -= 40;
        }
        
        // Debug/test classes get penalty
        var className = finding.ClassName ?? "";
        if (className.Contains("Debug", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            penalty -= 30;
        }
        
        // TODO comments get small penalty (already noted, not actionable)
        if (finding.AnalysisType == "Todo")
        {
            penalty -= 10;
        }
        
        return Math.Max(-40, penalty);
    }

    // ============================================================================
    // Database Query Helpers
    // ============================================================================

    private string? GetUsageLevel(long analysisId)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT json_extract(usage_level_json, '$.level')
                FROM game_code_analysis_enriched
                WHERE analysis_id = $id";
            cmd.Parameters.AddWithValue("$id", analysisId);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null; // Enriched table may not exist
        }
    }

    private int GetCallerCount(string className, string? methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return 0;
        
        try
        {
            using var cmd = _db.CreateCommand();
            // Check both cg_calls and method_calls tables
            cmd.CommandText = @"
                SELECT COUNT(DISTINCT caller_id) 
                FROM cg_calls c
                JOIN cg_methods m ON c.callee_id = m.id
                JOIN cg_types t ON m.type_id = t.id
                WHERE t.name = $class AND m.name = $method
                UNION ALL
                SELECT COUNT(DISTINCT caller_file)
                FROM method_calls
                WHERE target_class = $class AND target_method = $method";
            cmd.Parameters.AddWithValue("$class", className);
            cmd.Parameters.AddWithValue("$method", methodName);
            
            using var reader = cmd.ExecuteReader();
            var count = 0;
            while (reader.Read())
            {
                count += reader.GetInt32(0);
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private bool IsReachableFromEntryPoints(string className, string? methodName)
    {
        // Check transitive_references table for reachability
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM transitive_references
                WHERE target_class = $class 
                AND ($method IS NULL OR target_method = $method)
                AND depth <= 5";
            cmd.Parameters.AddWithValue("$class", className);
            cmd.Parameters.AddWithValue("$method", methodName ?? (object)DBNull.Value);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool IsHarmonyPatched(string className, string? methodName)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            if (string.IsNullOrEmpty(methodName))
            {
                cmd.CommandText = "SELECT COUNT(*) FROM harmony_patches WHERE target_class = $class";
                cmd.Parameters.AddWithValue("$class", className);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM harmony_patches WHERE target_class = $class AND target_method = $method";
                cmd.Parameters.AddWithValue("$class", className);
                cmd.Parameters.AddWithValue("$method", methodName);
            }
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool HasModXmlReferences(string className)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM mod_xml_operations 
                WHERE xpath LIKE '%' || $class || '%' 
                   OR target_name = $class";
            cmd.Parameters.AddWithValue("$class", className);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool HasModCSharpDependencies(string className)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM mod_csharp_deps 
                WHERE dependency_name = $class";
            cmd.Parameters.AddWithValue("$class", className);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch
        {
            return false;
        }
    }

    private void PersistScores(long analysisId, RelevanceScores scores)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO code_relevance 
            (analysis_id, connectivity_score, entity_score, mod_score, keyword_score, artifact_penalty, total_score, computed_at)
            VALUES ($id, $conn, $entity, $mod, $kw, $penalty, $total, datetime('now'))";
        cmd.Parameters.AddWithValue("$id", analysisId);
        cmd.Parameters.AddWithValue("$conn", scores.ConnectivityScore);
        cmd.Parameters.AddWithValue("$entity", scores.EntityScore);
        cmd.Parameters.AddWithValue("$mod", scores.ModScore);
        cmd.Parameters.AddWithValue("$kw", scores.KeywordScore);
        cmd.Parameters.AddWithValue("$penalty", scores.ArtifactPenalty);
        cmd.Parameters.AddWithValue("$total", scores.TotalScore);
        cmd.ExecuteNonQuery();
    }

    // ============================================================================
    // Helper Types (internal for testing)
    // ============================================================================

    internal class AnalysisFinding
    {
        public long Id { get; set; }
        public string AnalysisType { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string? MethodName { get; set; }
        public string Severity { get; set; } = "";
        public string Confidence { get; set; } = "";
        public string? Description { get; set; }
        public string? FilePath { get; set; }
        public int? LineNumber { get; set; }
        public string? RelatedEntities { get; set; }
    }

    private class RelevanceScores
    {
        public int ConnectivityScore { get; set; }
        public int EntityScore { get; set; }
        public int ModScore { get; set; }
        public int KeywordScore { get; set; }
        public int ArtifactPenalty { get; set; }
        public int TotalScore { get; set; }
    }
}
