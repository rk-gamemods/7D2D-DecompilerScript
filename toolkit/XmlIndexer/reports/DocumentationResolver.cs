namespace XmlIndexer.Reports;

/// <summary>
/// Resolves code tokens to external documentation URLs.
/// Supports C# keywords, XPath operators, and game types.
/// </summary>
public class DocumentationResolver
{
    public enum TokenContext { CSharp, XPath, GameType }
    public enum Confidence { High, Medium, Low, Skip }

    public record DocumentationLink(
        string Url,
        string DisplayText,
        string? TooltipSummary,
        Confidence Confidence,
        bool IsExternal
    );

    // Cache resolved links
    private readonly Dictionary<(string, TokenContext), DocumentationLink?> _cache = new();

    // Tokens to skip (too common/meaningless)
    private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "j", "k", "x", "y", "z", "value", "result", "temp", "var",
        "true", "false", "null", "this", "base", "0", "1", "-1"
    };

    // C# keywords → MS Learn URLs
    private static readonly Dictionary<string, string> CSharpKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abstract"] = "abstract",
        ["async"] = "async",
        ["await"] = "await",
        ["class"] = "class",
        ["const"] = "const",
        ["delegate"] = "delegate",
        ["enum"] = "enum",
        ["event"] = "event",
        ["interface"] = "interface",
        ["internal"] = "internal",
        ["namespace"] = "namespace",
        ["new"] = "new-operator",
        ["override"] = "override",
        ["partial"] = "partial-type",
        ["private"] = "private",
        ["protected"] = "protected",
        ["public"] = "public",
        ["readonly"] = "readonly",
        ["sealed"] = "sealed",
        ["static"] = "static",
        ["struct"] = "struct",
        ["virtual"] = "virtual",
        ["volatile"] = "volatile",
        ["yield"] = "yield",
    };

    // XPath operators/axes → W3C URLs
    private static readonly Dictionary<string, string> XPathAxes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["//"] = "descendant-or-self",
        ["/"] = "child",
        [".."] = "parent",
        ["."] = "self",
        ["@"] = "attribute",
        ["ancestor"] = "ancestor",
        ["ancestor-or-self"] = "ancestor-or-self",
        ["child"] = "child",
        ["descendant"] = "descendant",
        ["descendant-or-self"] = "descendant-or-self",
        ["following"] = "following",
        ["following-sibling"] = "following-sibling",
        ["parent"] = "parent",
        ["preceding"] = "preceding",
        ["preceding-sibling"] = "preceding-sibling",
        ["self"] = "self",
    };

    // Tooltip summaries loaded from JSON (optional)
    private Dictionary<string, string>? _tooltips;

    public void LoadTooltips(Dictionary<string, string> tooltips)
    {
        _tooltips = tooltips;
    }

    public DocumentationLink? Resolve(string token, TokenContext context)
    {
        if (string.IsNullOrWhiteSpace(token) || Blacklist.Contains(token))
            return null;

        var key = (token.Trim(), context);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var link = context switch
        {
            TokenContext.CSharp => ResolveCSharp(token),
            TokenContext.XPath => ResolveXPath(token),
            TokenContext.GameType => ResolveGameType(token),
            _ => null
        };

        _cache[key] = link;
        return link;
    }

    private DocumentationLink? ResolveCSharp(string token)
    {
        if (CSharpKeywords.TryGetValue(token, out var slug))
        {
            var url = $"https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/{slug}";
            var tooltip = GetTooltip($"csharp:{token}") ?? $"C# keyword: {token}";
            return new DocumentationLink(url, token, tooltip, Confidence.High, IsExternal: true);
        }

        // Could be a .NET type (e.g., List, Dictionary)
        if (IsKnownDotNetType(token))
        {
            var url = $"https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.{token.ToLowerInvariant()}";
            var tooltip = GetTooltip($"dotnet:{token}") ?? $".NET type: {token}";
            return new DocumentationLink(url, token, tooltip, Confidence.Medium, IsExternal: true);
        }

        return null;
    }

    private DocumentationLink? ResolveXPath(string token)
    {
        if (XPathAxes.TryGetValue(token, out var axis))
        {
            var url = $"https://www.w3.org/TR/xpath/#axes";
            var tooltip = GetTooltip($"xpath:{token}") ?? $"XPath axis: {axis}";
            return new DocumentationLink(url, token, tooltip, Confidence.High, IsExternal: true);
        }

        return null;
    }

    private DocumentationLink? ResolveGameType(string token)
    {
        // Game types link to wiki for now (defer local pages to v2)
        if (IsLikelyGameType(token))
        {
            var tooltip = GetTooltip($"game:{token}") ?? $"7 Days to Die game type";
            return new DocumentationLink(
                $"https://7daystodie.fandom.com/wiki/{token}",
                token,
                tooltip,
                Confidence.Low,
                IsExternal: true
            );
        }

        return null;
    }

    private static bool IsKnownDotNetType(string token)
    {
        return token is "List" or "Dictionary" or "HashSet" or "Queue" or "Stack"
            or "Action" or "Func" or "Task" or "StringBuilder";
    }

    private static bool IsLikelyGameType(string token)
    {
        // Common game type prefixes
        return token.StartsWith("Entity") || token.StartsWith("Block")
            || token.StartsWith("Item") || token.StartsWith("XUi")
            || token.StartsWith("NetPackage") || token.StartsWith("Buff");
    }

    private string? GetTooltip(string key)
    {
        return _tooltips?.GetValueOrDefault(key);
    }

    /// <summary>
    /// Format a token as an HTML link if resolvable.
    /// Returns the original token if no link available.
    /// </summary>
    public string FormatAsLink(string token, TokenContext context)
    {
        var link = Resolve(token, context);
        if (link == null)
            return System.Web.HttpUtility.HtmlEncode(token);

        var tooltip = link.TooltipSummary != null
            ? $" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(link.TooltipSummary)}\""
            : "";

        var confidence = link.Confidence == Confidence.Low ? " data-confidence=\"low\"" : "";
        var external = link.IsExternal ? " target=\"_blank\" rel=\"noopener\"" : "";

        return $"<a href=\"{link.Url}\" class=\"doc-link\"{tooltip}{confidence}{external}>{System.Web.HttpUtility.HtmlEncode(link.DisplayText)}</a>";
    }
}
