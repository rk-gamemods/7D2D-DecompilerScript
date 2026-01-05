using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XmlIndexer.Utils;

/// <summary>
/// Generic fuzzy matching for entity names using true semantic analysis.
/// No hardcoded special cases - works for any entity naming convention.
/// </summary>
public static class FuzzyEntityMatcher
{
    /// <summary>
    /// Represents a potential match with similarity score and reasoning.
    /// </summary>
    public record FuzzyMatch(
        string CandidateName,
        string CandidateType,
        string CandidateFile,
        double SimilarityScore,
        string MatchReason
    );

    /// <summary>
    /// Find similar entities in XML for a code-referenced name that doesn't exist.
    /// Uses multiple semantic similarity algorithms, no hardcoded patterns.
    /// </summary>
    public static List<FuzzyMatch> FindSimilarEntities(
        string missingName,
        string expectedType,  // "buff", "item", "block", etc.
        IEnumerable<(string Name, string Type, string File)> xmlEntities,
        int maxResults = 5,
        double minScore = 0.4)
    {
        var missingTokens = TokenizeName(missingName);
        var results = new List<FuzzyMatch>();

        foreach (var (name, type, file) in xmlEntities)
        {
            // Type match bonus - same type is more likely to be the intended match
            double typeBonus = GetTypeMatchBonus(expectedType, type);
            
            // Calculate multiple similarity metrics
            var candidateTokens = TokenizeName(name);
            
            double tokenOverlap = CalculateTokenOverlap(missingTokens, candidateTokens);
            double levenshtein = CalculateNormalizedLevenshtein(missingName, name);
            double prefixSuffix = CalculatePrefixSuffixMatch(missingName, name);
            double semanticSimilarity = CalculateSemanticTokenSimilarity(missingTokens, candidateTokens);
            
            // Weighted combination - no magic numbers for specific cases
            double rawScore = (tokenOverlap * 0.35) + 
                              (levenshtein * 0.25) + 
                              (prefixSuffix * 0.15) +
                              (semanticSimilarity * 0.25);
            
            double finalScore = rawScore + typeBonus;
            
            if (finalScore >= minScore)
            {
                string reason = BuildMatchReason(missingTokens, candidateTokens, tokenOverlap, levenshtein);
                results.Add(new FuzzyMatch(name, type, file, Math.Min(finalScore, 1.0), reason));
            }
        }

        return results
            .OrderByDescending(m => m.SimilarityScore)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Tokenize a name into semantic parts using multiple strategies.
    /// Handles camelCase, PascalCase, snake_case, and common prefixes/suffixes.
    /// </summary>
    public static List<string> TokenizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return new List<string>();

        var tokens = new List<string>();
        
        // Split on common delimiters first
        var parts = Regex.Split(name, @"[_\-\s]+");
        
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            
            // Split camelCase/PascalCase
            var camelTokens = Regex.Split(part, @"(?<!^)(?=[A-Z][a-z])|(?<=[a-z])(?=[A-Z])");
            
            foreach (var token in camelTokens)
            {
                if (!string.IsNullOrEmpty(token))
                {
                    tokens.Add(token.ToLowerInvariant());
                }
            }
        }

        return tokens.Where(t => t.Length > 1).ToList(); // Filter out single chars
    }

    /// <summary>
    /// Calculate Jaccard-like token overlap between two token sets.
    /// </summary>
    private static double CalculateTokenOverlap(List<string> tokens1, List<string> tokens2)
    {
        if (tokens1.Count == 0 || tokens2.Count == 0) return 0;

        var set1 = new HashSet<string>(tokens1);
        var set2 = new HashSet<string>(tokens2);
        
        int intersection = set1.Intersect(set2).Count();
        int union = set1.Union(set2).Count();
        
        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Normalized Levenshtein distance (0 = completely different, 1 = identical).
    /// </summary>
    private static double CalculateNormalizedLevenshtein(string s1, string s2)
    {
        s1 = s1.ToLowerInvariant();
        s2 = s2.ToLowerInvariant();
        
        int maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0) return 1;
        
        int distance = LevenshteinDistance(s1, s2);
        return 1.0 - ((double)distance / maxLen);
    }

    /// <summary>
    /// Standard Levenshtein distance implementation.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        int[,] d = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s1.Length, s2.Length];
    }

    /// <summary>
    /// Check for common prefix/suffix patterns (e.g., "buff" prefix).
    /// </summary>
    private static double CalculatePrefixSuffixMatch(string s1, string s2)
    {
        s1 = s1.ToLowerInvariant();
        s2 = s2.ToLowerInvariant();
        
        // Find longest common prefix
        int prefixLen = 0;
        int maxPrefix = Math.Min(s1.Length, s2.Length);
        while (prefixLen < maxPrefix && s1[prefixLen] == s2[prefixLen])
            prefixLen++;
        
        // Find longest common suffix
        int suffixLen = 0;
        int maxSuffix = Math.Min(s1.Length, s2.Length) - prefixLen;
        while (suffixLen < maxSuffix && s1[s1.Length - 1 - suffixLen] == s2[s2.Length - 1 - suffixLen])
            suffixLen++;
        
        int maxLen = Math.Max(s1.Length, s2.Length);
        return maxLen > 0 ? (double)(prefixLen + suffixLen) / maxLen : 0;
    }

    /// <summary>
    /// Semantic token similarity - accounts for related words and common patterns.
    /// </summary>
    private static double CalculateSemanticTokenSimilarity(List<string> tokens1, List<string> tokens2)
    {
        if (tokens1.Count == 0 || tokens2.Count == 0) return 0;

        double totalScore = 0;
        int comparisons = 0;

        foreach (var t1 in tokens1)
        {
            double bestMatch = 0;
            foreach (var t2 in tokens2)
            {
                // Exact match
                if (t1 == t2)
                {
                    bestMatch = Math.Max(bestMatch, 1.0);
                    continue;
                }
                
                // Substring containment
                if (t1.Contains(t2) || t2.Contains(t1))
                {
                    double shorter = Math.Min(t1.Length, t2.Length);
                    double longer = Math.Max(t1.Length, t2.Length);
                    bestMatch = Math.Max(bestMatch, shorter / longer);
                    continue;
                }
                
                // Common prefix (at least 3 chars)
                int commonPrefix = 0;
                int maxLen = Math.Min(t1.Length, t2.Length);
                while (commonPrefix < maxLen && t1[commonPrefix] == t2[commonPrefix])
                    commonPrefix++;
                
                if (commonPrefix >= 3)
                {
                    bestMatch = Math.Max(bestMatch, (double)commonPrefix / Math.Max(t1.Length, t2.Length));
                }
            }
            totalScore += bestMatch;
            comparisons++;
        }

        return comparisons > 0 ? totalScore / comparisons : 0;
    }

    /// <summary>
    /// Type match bonus - same type entities are more likely matches.
    /// Generic logic, not hardcoded for specific types.
    /// </summary>
    private static double GetTypeMatchBonus(string expectedType, string candidateType)
    {
        if (string.IsNullOrEmpty(expectedType) || string.IsNullOrEmpty(candidateType))
            return 0;
        
        expectedType = expectedType.ToLowerInvariant();
        candidateType = candidateType.ToLowerInvariant();
        
        // Exact type match
        if (expectedType == candidateType)
            return 0.15;
        
        // Partial type match (e.g., "triggered_effect" contains "effect")
        if (expectedType.Contains(candidateType) || candidateType.Contains(expectedType))
            return 0.05;
        
        return 0;
    }

    /// <summary>
    /// Build a human-readable explanation of why this match was found.
    /// </summary>
    private static string BuildMatchReason(
        List<string> missingTokens, 
        List<string> candidateTokens,
        double tokenOverlap,
        double levenshtein)
    {
        var reasons = new List<string>();
        
        var shared = missingTokens.Intersect(candidateTokens).ToList();
        if (shared.Count > 0)
        {
            reasons.Add($"Shared tokens: {string.Join(", ", shared)}");
        }
        
        if (levenshtein > 0.7)
        {
            reasons.Add("Similar spelling");
        }
        
        if (tokenOverlap > 0.5)
        {
            reasons.Add("High token overlap");
        }

        return reasons.Count > 0 ? string.Join("; ", reasons) : "Partial similarity";
    }
}
