using System.Text.RegularExpressions;

namespace XmlIndexer.Reports;

/// <summary>
/// Extracts and classifies tokens from code snippets for documentation linking.
/// </summary>
public class CodeTokenizer
{
    public enum TokenType { Keyword, Type, XPathOperator, Identifier }

    public record Token(string Value, TokenType Type, int StartIndex, int Length);

    // C# keywords to detect
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Access modifiers
        "public", "private", "protected", "internal",
        // Class modifiers
        "abstract", "sealed", "static", "partial", "virtual", "override", "new",
        // Type definitions
        "class", "struct", "interface", "enum", "delegate", "record", "namespace",
        // Built-in types
        "int", "string", "bool", "void", "float", "double", "decimal",
        "byte", "sbyte", "short", "ushort", "uint", "long", "ulong", "char",
        "object", "dynamic", "var",
        // Variable modifiers
        "const", "readonly", "volatile",
        // Control flow
        "if", "else", "switch", "case", "for", "foreach", "while", "do",
        "break", "continue", "return", "goto", "yield", "throw",
        // Exception handling
        "try", "catch", "finally",
        // Other keywords
        "async", "await", "using", "lock", "event",
        "ref", "out", "in", "params",
        "true", "false", "null", "this", "base",
        "typeof", "sizeof", "is", "as", "where", "get", "set", "value"
    };

    // XPath operators/axes
    private static readonly HashSet<string> XPathTokens = new()
    {
        "//", "/", "..", ".", "@", "ancestor", "ancestor-or-self", "child",
        "descendant", "descendant-or-self", "following", "following-sibling",
        "parent", "preceding", "preceding-sibling", "self"
    };

    // Match identifiers (PascalCase/camelCase words)
    private static readonly Regex IdentifierPattern = new(
        @"\b([A-Z][a-zA-Z0-9]*|[a-z][a-zA-Z0-9]*)\b",
        RegexOptions.Compiled);

    // Match XPath operators
    private static readonly Regex XPathPattern = new(
        @"(//|\.\.|\.|@|/)",
        RegexOptions.Compiled);

    /// <summary>
    /// Tokenize a C# code snippet.
    /// </summary>
    public IEnumerable<Token> TokenizeCSharp(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            yield break;

        foreach (Match match in IdentifierPattern.Matches(code))
        {
            var value = match.Value;
            var type = CSharpKeywords.Contains(value) ? TokenType.Keyword
                : char.IsUpper(value[0]) ? TokenType.Type
                : TokenType.Identifier;

            yield return new Token(value, type, match.Index, match.Length);
        }
    }

    /// <summary>
    /// Tokenize an XPath expression.
    /// </summary>
    public IEnumerable<Token> TokenizeXPath(string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
            yield break;

        foreach (Match match in XPathPattern.Matches(xpath))
        {
            if (XPathTokens.Contains(match.Value))
            {
                yield return new Token(match.Value, TokenType.XPathOperator, match.Index, match.Length);
            }
        }

        // Also extract axis names like "ancestor::"
        var axisPattern = new Regex(@"\b(ancestor|ancestor-or-self|child|descendant|descendant-or-self|following|following-sibling|parent|preceding|preceding-sibling|self)(?=::)");
        foreach (Match match in axisPattern.Matches(xpath))
        {
            yield return new Token(match.Value, TokenType.XPathOperator, match.Index, match.Length);
        }
    }

    /// <summary>
    /// Extract linkable tokens from code, returning only first occurrence of each.
    /// </summary>
    public IEnumerable<Token> ExtractLinkable(string code, bool isCSharp = true)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tokens = isCSharp ? TokenizeCSharp(code) : TokenizeXPath(code);

        foreach (var token in tokens)
        {
            // Skip plain identifiers (only link keywords and types)
            if (token.Type == TokenType.Identifier)
                continue;

            // First occurrence only
            if (seen.Add(token.Value))
                yield return token;
        }
    }
}
