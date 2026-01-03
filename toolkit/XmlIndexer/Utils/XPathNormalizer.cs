using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.XPath;

namespace XmlIndexer.Utils;

/// <summary>
/// Normalizes XPath expressions for consistent comparison and hashing.
/// Handles whitespace, quote styles, predicate ordering, and common patterns.
/// </summary>
public static class XPathNormalizer
{
    /// <summary>
    /// Result of XPath normalization including validation status.
    /// </summary>
    public record NormalizationResult(
        string Original,
        string Normalized,
        string Hash,
        bool IsValid,
        string? ValidationError
    );

    /// <summary>
    /// Validates that an XPath expression is valid XPath 1.0.
    /// </summary>
    public static bool ValidateXPath10(string xpath, out string? error)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            error = "XPath expression is empty or null";
            return false;
        }

        try
        {
            // .NET's XPathExpression.Compile only supports XPath 1.0
            XPathExpression.Compile(xpath);
            error = null;
            return true;
        }
        catch (XPathException ex)
        {
            error = $"Invalid XPath 1.0: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Normalizes an XPath expression for consistent comparison.
    /// Returns the normalized form along with a hash for grouping.
    /// </summary>
    public static NormalizationResult Normalize(string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return new NormalizationResult(xpath, "", "", false, "Empty XPath");
        }

        // Validate first
        if (!ValidateXPath10(xpath, out var validationError))
        {
            // Still attempt normalization for invalid XPaths (they may work in the game)
            var bestEffort = NormalizeInternal(xpath);
            return new NormalizationResult(xpath, bestEffort, ComputeHash(bestEffort), false, validationError);
        }

        var normalized = NormalizeInternal(xpath);
        var hash = ComputeHash(normalized);

        return new NormalizationResult(xpath, normalized, hash, true, null);
    }

    /// <summary>
    /// Computes a normalized hash for an XPath + operation combination.
    /// This is the primary grouping key for conflict detection.
    /// </summary>
    public static string ComputeConflictKey(string normalizedXPath, string operation)
    {
        var combined = $"{normalizedXPath.ToLowerInvariant()}|{operation.ToLowerInvariant()}";
        return ComputeHash(combined);
    }

    private static string NormalizeInternal(string xpath)
    {
        var result = xpath;

        // 1. Normalize whitespace: collapse multiple spaces, trim
        result = Regex.Replace(result, @"\s+", " ").Trim();

        // 2. Remove spaces around operators and brackets
        result = Regex.Replace(result, @"\s*\[\s*", "[");
        result = Regex.Replace(result, @"\s*\]\s*", "]");
        result = Regex.Replace(result, @"\s*/\s*", "/");
        result = Regex.Replace(result, @"\s*=\s*", "=");
        result = Regex.Replace(result, @"\s*@\s*", "@");

        // 3. Standardize quotes: single quotes -> double quotes
        result = StandardizeQuotes(result);

        // 4. Normalize predicate ordering within each bracket
        result = NormalizePredicates(result);

        // 5. Normalize numeric predicates
        result = NormalizeNumericPredicates(result);

        // 6. Strip redundant path components
        result = StripRedundantPaths(result);

        // 7. Lowercase element/attribute names (XPath is case-sensitive, but normalize for comparison)
        // Note: We don't lowercase string values inside predicates
        result = NormalizeCase(result);

        return result;
    }

    private static string StandardizeQuotes(string xpath)
    {
        // Replace single quotes with double quotes, but handle escaped quotes
        var result = new StringBuilder();
        bool inDoubleQuote = false;
        bool inSingleQuote = false;

        for (int i = 0; i < xpath.Length; i++)
        {
            char c = xpath[i];

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                result.Append('"');
            }
            else if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                result.Append('"'); // Convert to double quote
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static string NormalizePredicates(string xpath)
    {
        // Find predicates and sort attributes within them alphabetically
        // e.g., [@class='x'][@name='y'] -> [@class='x'][@name='y'] (already sorted)
        // e.g., [@name='y'][@class='x'] -> [@class='x'][@name='y']

        var predicatePattern = new Regex(@"\[([^\[\]]+)\]");

        return predicatePattern.Replace(xpath, match =>
        {
            var content = match.Groups[1].Value;

            // Check if this predicate contains multiple conditions with 'and'
            if (content.Contains(" and "))
            {
                var conditions = content.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .OrderBy(c => c)
                    .ToArray();
                return "[" + string.Join(" and ", conditions) + "]";
            }

            return match.Value;
        });
    }

    private static string NormalizeNumericPredicates(string xpath)
    {
        // Normalize position predicates:
        // [position()=1] -> [1]
        // [position()=last()] -> [last()]
        // [1] stays as [1]

        var result = xpath;

        // [position()=N] -> [N]
        result = Regex.Replace(result, @"\[position\(\)\s*=\s*(\d+)\]", "[$1]");

        // [position()=last()] -> [last()]
        result = Regex.Replace(result, @"\[position\(\)\s*=\s*last\(\)\]", "[last()]");

        return result;
    }

    private static string StripRedundantPaths(string xpath)
    {
        var result = xpath;

        // Remove redundant ./
        result = Regex.Replace(result, @"(?<![\.])\.\/", "");

        // Simplify //./  to //
        result = Regex.Replace(result, @"\/\/\.\/", "//");

        // Remove trailing slashes
        result = result.TrimEnd('/');

        return result;
    }

    private static string NormalizeCase(string xpath)
    {
        // Lowercase everything except string literals (content between quotes)
        var result = new StringBuilder();
        bool inQuote = false;
        char quoteChar = '\0';

        foreach (char c in xpath)
        {
            if (!inQuote && (c == '"' || c == '\''))
            {
                inQuote = true;
                quoteChar = c;
                result.Append(c);
            }
            else if (inQuote && c == quoteChar)
            {
                inQuote = false;
                quoteChar = '\0';
                result.Append(c);
            }
            else if (inQuote)
            {
                result.Append(c); // Preserve case in string literals
            }
            else
            {
                result.Append(char.ToLowerInvariant(c));
            }
        }

        return result.ToString();
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha256.ComputeHash(bytes);

        // Return first 16 characters of hex string (64 bits)
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Extracts the target entity type and name from an XPath expression.
    /// Enhanced version that handles more patterns.
    /// </summary>
    public static (string? Type, string? Name) ExtractTarget(string xpath)
    {
        var patterns = new Dictionary<string, string>
        {
            { @"/items/item\[@name=[""']([^""']+)[""']\]", "item" },
            { @"/blocks/block\[@name=[""']([^""']+)[""']\]", "block" },
            { @"/entity_classes/entity_class\[@name=[""']([^""']+)[""']\]", "entity_class" },
            { @"/buffs/buff\[@name=[""']([^""']+)[""']\]", "buff" },
            { @"/recipes/recipe\[@name=[""']([^""']+)[""']\]", "recipe" },
            { @"/progression/perks/perk\[@name=[""']([^""']+)[""']\]", "perk" },
            { @"/progression/skills/skill\[@name=[""']([^""']+)[""']\]", "skill" },
            { @"/lootcontainers/lootcontainer\[@id=[""']([^""']+)[""']\]", "lootcontainer" },
            { @"/lootgroups/lootgroup\[@name=[""']([^""']+)[""']\]", "lootgroup" },
            { @"/vehicles/vehicle\[@name=[""']([^""']+)[""']\]", "vehicle" },
            { @"/quests/quest\[@id=[""']([^""']+)[""']\]", "quest" },
            { @"/gamestages\[@name=[""']([^""']+)[""']\]", "gamestages" },
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(xpath, pattern.Key, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return (pattern.Value, match.Groups[1].Value);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Determines if two XPath expressions target the same node after normalization.
    /// </summary>
    public static bool AreEquivalent(string xpath1, string xpath2)
    {
        var norm1 = Normalize(xpath1);
        var norm2 = Normalize(xpath2);
        return norm1.Hash == norm2.Hash;
    }
}
