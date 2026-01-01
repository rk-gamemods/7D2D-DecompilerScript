using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CallGraphExtractor;

/// <summary>
/// Extracts method call relationships by walking syntax trees with semantic analysis.
/// </summary>
public class CallExtractor
{
    private readonly CSharpCompilation _compilation;
    private readonly IReadOnlyDictionary<ISymbol, long> _symbolToId;
    private readonly IReadOnlyDictionary<string, long> _signatureToMethodId;
    private readonly bool _verbose;
    
    private int _callCount = 0;
    private int _unresolvedCount = 0;
    
    public CallExtractor(
        CSharpCompilation compilation,
        IReadOnlyDictionary<ISymbol, long> symbolToId,
        IReadOnlyDictionary<string, long> signatureToMethodId,
        bool verbose = false)
    {
        _compilation = compilation;
        _symbolToId = symbolToId;
        _signatureToMethodId = signatureToMethodId;
        _verbose = verbose;
    }
    
    /// <summary>
    /// Extract all method call edges and write to database.
    /// </summary>
    public void ExtractCalls(SqliteWriter db, string sourcePath)
    {
        Console.WriteLine("Extracting call relationships...");
        
        using var transaction = db.BeginTransaction();
        
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var filePath = tree.FilePath;
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var semanticModel = _compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            
            // Process each method/constructor/property
            ProcessMethods(db, root, semanticModel, relativePath);
        }
        
        transaction.Commit();
        
        Console.WriteLine($"Extracted {_callCount} call edges ({_unresolvedCount} unresolved targets)");
    }
    
    private void ProcessMethods(SqliteWriter db, SyntaxNode root, SemanticModel model, string filePath)
    {
        // Find all methods
        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = model.GetDeclaredSymbol(methodDecl);
            if (methodSymbol == null) continue;
            
            var callerId = GetMethodId(methodSymbol);
            if (callerId == null) continue;
            
            // Find all invocations within this method
            ExtractInvocationsFromBody(db, methodDecl.Body ?? (SyntaxNode?)methodDecl.ExpressionBody, 
                model, filePath, callerId.Value);
        }
        
        // Find all constructors
        foreach (var ctorDecl in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var ctorSymbol = model.GetDeclaredSymbol(ctorDecl);
            if (ctorSymbol == null) continue;
            
            var callerId = GetMethodId(ctorSymbol);
            if (callerId == null) continue;
            
            ExtractInvocationsFromBody(db, ctorDecl.Body, model, filePath, callerId.Value);
            
            // Also check initializer (base/this calls)
            if (ctorDecl.Initializer != null)
            {
                var initSymbol = model.GetSymbolInfo(ctorDecl.Initializer).Symbol as IMethodSymbol;
                if (initSymbol != null)
                {
                    var calleeId = GetMethodId(initSymbol);
                    if (calleeId != null)
                    {
                        var lineSpan = ctorDecl.Initializer.GetLocation().GetLineSpan();
                        db.InsertCall(callerId.Value, calleeId.Value, filePath, 
                            lineSpan.StartLinePosition.Line + 1, "direct");
                        _callCount++;
                    }
                }
            }
        }
        
        // Find property accessors
        foreach (var propDecl in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var propSymbol = model.GetDeclaredSymbol(propDecl);
            if (propSymbol == null) continue;
            
            if (propDecl.AccessorList != null)
            {
                foreach (var accessor in propDecl.AccessorList.Accessors)
                {
                    var accessorSymbol = accessor.Keyword.IsKind(SyntaxKind.GetKeyword) 
                        ? propSymbol.GetMethod 
                        : propSymbol.SetMethod;
                    
                    if (accessorSymbol == null) continue;
                    
                    var callerId = GetMethodId(accessorSymbol);
                    if (callerId == null) continue;
                    
                    ExtractInvocationsFromBody(db, accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody, 
                        model, filePath, callerId.Value);
                }
            }
            else if (propDecl.ExpressionBody != null)
            {
                // Expression-bodied property (getter only)
                if (propSymbol.GetMethod != null)
                {
                    var callerId = GetMethodId(propSymbol.GetMethod);
                    if (callerId != null)
                    {
                        ExtractInvocationsFromBody(db, propDecl.ExpressionBody, model, filePath, callerId.Value);
                    }
                }
            }
        }
    }
    
    private void ExtractInvocationsFromBody(SqliteWriter db, SyntaxNode? body, 
        SemanticModel model, string filePath, long callerId)
    {
        if (body == null) return;
        
        // Find direct method invocations
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            ExtractInvocation(db, invocation, model, filePath, callerId);
        }
        
        // Find object creation (constructor calls)
        foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            ExtractObjectCreation(db, creation, model, filePath, callerId);
        }
        
        // Find member access that might be property access
        foreach (var memberAccess in body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            ExtractMemberAccess(db, memberAccess, model, filePath, callerId);
        }
    }
    
    private void ExtractInvocation(SqliteWriter db, InvocationExpressionSyntax invocation, 
        SemanticModel model, string filePath, long callerId)
    {
        var symbolInfo = model.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        
        // Try candidates if direct resolution failed
        if (methodSymbol == null && symbolInfo.CandidateSymbols.Length > 0)
        {
            methodSymbol = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }
        
        if (methodSymbol == null)
        {
            _unresolvedCount++;
            return;
        }
        
        var calleeId = GetMethodId(methodSymbol);
        if (calleeId == null)
        {
            // Method exists but not in our database (e.g., BCL method)
            return;
        }
        
        var lineSpan = invocation.GetLocation().GetLineSpan();
        var callType = methodSymbol.IsVirtual || methodSymbol.IsOverride || methodSymbol.IsAbstract 
            ? "virtual" : "direct";
        
        db.InsertCall(callerId, calleeId.Value, filePath, lineSpan.StartLinePosition.Line + 1, callType);
        _callCount++;
    }
    
    private void ExtractObjectCreation(SqliteWriter db, ObjectCreationExpressionSyntax creation, 
        SemanticModel model, string filePath, long callerId)
    {
        var symbolInfo = model.GetSymbolInfo(creation);
        var ctorSymbol = symbolInfo.Symbol as IMethodSymbol;
        
        if (ctorSymbol == null && symbolInfo.CandidateSymbols.Length > 0)
        {
            ctorSymbol = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }
        
        if (ctorSymbol == null)
        {
            _unresolvedCount++;
            return;
        }
        
        var calleeId = GetMethodId(ctorSymbol);
        if (calleeId == null) return;
        
        var lineSpan = creation.GetLocation().GetLineSpan();
        db.InsertCall(callerId, calleeId.Value, filePath, lineSpan.StartLinePosition.Line + 1, "direct");
        _callCount++;
    }
    
    private void ExtractMemberAccess(SqliteWriter db, MemberAccessExpressionSyntax memberAccess, 
        SemanticModel model, string filePath, long callerId)
    {
        // Skip if this is part of an invocation (handled separately)
        if (memberAccess.Parent is InvocationExpressionSyntax)
            return;
        
        var symbolInfo = model.GetSymbolInfo(memberAccess);
        
        // Check if this is a property access
        if (symbolInfo.Symbol is IPropertySymbol propSymbol)
        {
            // Determine if it's a get or set based on context
            var isWrite = memberAccess.Parent is AssignmentExpressionSyntax assignment 
                && assignment.Left == memberAccess;
            
            var accessorSymbol = isWrite ? propSymbol.SetMethod : propSymbol.GetMethod;
            if (accessorSymbol == null) return;
            
            var calleeId = GetMethodId(accessorSymbol);
            if (calleeId == null) return;
            
            var lineSpan = memberAccess.GetLocation().GetLineSpan();
            var callType = accessorSymbol.IsVirtual || accessorSymbol.IsOverride 
                ? "virtual" : "direct";
            
            db.InsertCall(callerId, calleeId.Value, filePath, lineSpan.StartLinePosition.Line + 1, callType);
            _callCount++;
        }
    }
    
    /// <summary>
    /// Look up method ID from symbol.
    /// </summary>
    private long? GetMethodId(IMethodSymbol symbol)
    {
        // Try direct symbol lookup first
        if (_symbolToId.TryGetValue(symbol, out var id))
            return id;
        
        // Handle reduced extension methods
        if (symbol.ReducedFrom != null && _symbolToId.TryGetValue(symbol.ReducedFrom, out id))
            return id;
        
        // Handle overridden methods - map to the actual override we have
        if (symbol.IsOverride && symbol.OverriddenMethod != null)
        {
            if (_symbolToId.TryGetValue(symbol.OverriddenMethod, out id))
                return id;
        }
        
        // Try signature-based lookup as fallback
        var signature = BuildSignature(symbol);
        var containingType = symbol.ContainingType?.ToDisplayString();
        if (containingType != null)
        {
            var fullSig = $"{containingType}.{signature}";
            if (_signatureToMethodId.TryGetValue(fullSig, out id))
                return id;
        }
        
        return null;
    }
    
    private string BuildSignature(IMethodSymbol method)
    {
        var paramTypes = string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString()));
        var name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;
        return $"{name}({paramTypes})";
    }
    
    public int CallCount => _callCount;
    public int UnresolvedCount => _unresolvedCount;
}
