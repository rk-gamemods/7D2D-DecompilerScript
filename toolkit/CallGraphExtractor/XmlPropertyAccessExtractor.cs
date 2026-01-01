using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CallGraphExtractor;

/// <summary>
/// Detects XML property access patterns in code and extracts property names.
/// Finds patterns like:
///   - properties.GetString("PropertyName")
///   - properties.GetFloat("PropertyName")
///   - properties.ParseBool("PropertyName", ref value)
///   - properties.Values.TryGetValue("PropertyName", out value)
///   - properties.Contains("PropertyName")
/// </summary>
public class XmlPropertyAccessExtractor : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly SqliteWriter _db;
    private readonly Dictionary<IMethodSymbol, long> _symbolToId;
    private readonly bool _verbose;
    private readonly string _filePath;
    
    private IMethodSymbol? _currentMethod;
    private long _currentMethodId;
    private int _accessCount;
    
    // Method names that read XML properties via string parameter
    private static readonly HashSet<string> PropertyAccessMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        // Direct property access
        "GetBool",
        "GetFloat", 
        "GetInt",
        "GetString",
        "GetStringValue",
        "GetLocalizedString",
        
        // Parse methods
        "ParseBool",
        "ParseFloat",
        "ParseInt",
        "ParseString",
        "ParseLocalizedString",
        "ParseColor",
        "ParseColorHex",
        "ParseEnum",
        "ParseVec",
        
        // Existence check
        "Contains",
        
        // Dictionary access (when on Values property)
        "TryGetValue",
        "ContainsKey",
    };
    
    public XmlPropertyAccessExtractor(SemanticModel semanticModel, SqliteWriter db,
                                       Dictionary<IMethodSymbol, long> symbolToId,
                                       string filePath, bool verbose = false)
    {
        _semanticModel = semanticModel;
        _db = db;
        _symbolToId = symbolToId;
        _filePath = filePath;
        _verbose = verbose;
    }
    
    public int AccessCount => _accessCount;
    
    /// <summary>
    /// Extract property access from a method body.
    /// </summary>
    public void ExtractFromMethod(MethodDeclarationSyntax method, IMethodSymbol symbol, long methodId)
    {
        _currentMethod = symbol;
        _currentMethodId = methodId;
        Visit(method.Body);
        Visit(method.ExpressionBody);
    }
    
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        base.VisitInvocationExpression(node);
        
        if (node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            
            // Check if this is a property access method
            if (PropertyAccessMethods.Contains(methodName))
            {
                // Extract the first string literal argument (the property name)
                var propertyName = ExtractFirstStringArgument(node.ArgumentList);
                
                if (!string.IsNullOrEmpty(propertyName))
                {
                    // Verify this is being called on a DynamicProperties or similar type
                    if (IsPropertyAccessReceiver(memberAccess.Expression))
                    {
                        var lineSpan = node.GetLocation().GetLineSpan();
                        var lineNumber = lineSpan.StartLinePosition.Line + 1;
                        
                        _db.InsertXmlPropertyAccess(
                            _currentMethodId,
                            propertyName,
                            methodName,
                            _filePath,
                            lineNumber
                        );
                        _accessCount++;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Extract the first string literal argument from an argument list.
    /// </summary>
    private string? ExtractFirstStringArgument(ArgumentListSyntax argumentList)
    {
        if (argumentList.Arguments.Count == 0)
            return null;
            
        var firstArg = argumentList.Arguments[0].Expression;
        
        // Handle direct string literal: "PropertyName"
        if (firstArg is LiteralExpressionSyntax literal && 
            literal.Kind() == SyntaxKind.StringLiteralExpression)
        {
            return literal.Token.ValueText;
        }
        
        // Handle interpolated string with only a literal part: $"PropertyName"  
        if (firstArg is InterpolatedStringExpressionSyntax interpolated)
        {
            if (interpolated.Contents.Count == 1 && 
                interpolated.Contents[0] is InterpolatedStringTextSyntax text)
            {
                return text.TextToken.ValueText;
            }
        }
        
        // Handle nameof: nameof(SomeProperty) - extract the identifier
        if (firstArg is InvocationExpressionSyntax invocation &&
            invocation.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" })
        {
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var nameofArg = invocation.ArgumentList.Arguments[0].Expression;
                if (nameofArg is IdentifierNameSyntax identifierName)
                {
                    return identifierName.Identifier.Text;
                }
                if (nameofArg is MemberAccessExpressionSyntax ma)
                {
                    return ma.Name.Identifier.Text;
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Check if the receiver expression is a DynamicProperties or related type.
    /// </summary>
    private bool IsPropertyAccessReceiver(ExpressionSyntax expression)
    {
        // Get the type of the receiver
        var typeInfo = _semanticModel.GetTypeInfo(expression);
        var type = typeInfo.Type;
        
        if (type == null)
        {
            // If we can't determine the type, use heuristics based on the expression
            return IsProbablePropertyAccess(expression);
        }
        
        var typeName = type.Name;
        
        // Direct matches
        if (typeName == "DynamicProperties" || 
            typeName == "DictionarySave" ||
            typeName == "EntityClass" ||
            typeName == "ItemClass")
        {
            return true;
        }
        
        // Check if this is accessing .Values or .Properties
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            if (memberName == "Values" || memberName == "Properties" || 
                memberName == "Params1" || memberName == "Params2" ||
                memberName == "Data" || memberName == "Dict")
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Heuristic check when type information is unavailable.
    /// </summary>
    private bool IsProbablePropertyAccess(ExpressionSyntax expression)
    {
        var text = expression.ToString().ToLowerInvariant();
        
        // Common patterns for property access
        return text.Contains("properties") ||
               text.Contains("values") ||
               text.Contains("params") ||
               text.Contains("data") ||
               text.EndsWith(".dict");
    }
}

/// <summary>
/// Orchestrates property access extraction across all source files.
/// </summary>
public class XmlPropertyAccessOrchestrator
{
    private readonly CSharpCompilation _compilation;
    private readonly Dictionary<IMethodSymbol, long> _symbolToId;
    private readonly SqliteWriter _db;
    private readonly bool _verbose;
    private int _totalAccessCount;
    
    public XmlPropertyAccessOrchestrator(CSharpCompilation compilation,
                                          Dictionary<IMethodSymbol, long> symbolToId,
                                          SqliteWriter db, bool verbose = false)
    {
        _compilation = compilation;
        _symbolToId = symbolToId;
        _db = db;
        _verbose = verbose;
    }
    
    public int TotalAccessCount => _totalAccessCount;
    
    /// <summary>
    /// Extract property access from all syntax trees.
    /// </summary>
    public void ExtractAll(IEnumerable<string> sourcePaths)
    {
        Console.WriteLine("Extracting XML property access patterns...");
        
        using var transaction = _db.BeginTransaction();
        
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var semanticModel = _compilation.GetSemanticModel(tree);
            var filePath = GetRelativePath(tree.FilePath, sourcePaths);
            
            var extractor = new XmlPropertyAccessExtractor(
                semanticModel, _db, _symbolToId, filePath, _verbose);
            
            // Walk all method declarations
            var root = tree.GetRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(method);
                if (symbol != null && _symbolToId.TryGetValue(symbol, out var methodId))
                {
                    extractor.ExtractFromMethod(method, symbol, methodId);
                }
            }
            
            _totalAccessCount += extractor.AccessCount;
        }
        
        transaction.Commit();
        Console.WriteLine($"  Found {_totalAccessCount:N0} property access patterns");
    }
    
    private string GetRelativePath(string fullPath, IEnumerable<string> basePaths)
    {
        foreach (var basePath in basePaths)
        {
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar);
            }
        }
        return Path.GetFileName(fullPath);
    }
}
