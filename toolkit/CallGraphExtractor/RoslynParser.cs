using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CallGraphExtractor;

/// <summary>
/// Parses C# source files using Roslyn to extract type and method information.
/// </summary>
public class RoslynParser
{
    private readonly bool _verbose;
    private readonly List<MetadataReference> _references;
    
    // Maps to track IDs for later call edge resolution
    private readonly Dictionary<ISymbol, long> _symbolToId = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, long> _signatureToMethodId = new();
    
    // Keep compilation for call extraction
    private CSharpCompilation? _compilation;
    private string? _sourcePath;
    
    public RoslynParser(bool verbose = false, string? refsPath = null)
    {
        _verbose = verbose;
        _references = LoadReferences(refsPath);
    }
    
    /// <summary>
    /// Load metadata references for type resolution.
    /// </summary>
    private List<MetadataReference> LoadReferences(string? refsPath)
    {
        var refs = new List<MetadataReference>();
        
        // Always include core .NET references
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        
        foreach (var assembly in trustedAssemblies)
        {
            if (File.Exists(assembly))
            {
                try
                {
                    refs.Add(MetadataReference.CreateFromFile(assembly));
                }
                catch
                {
                    // Skip problematic assemblies
                }
            }
        }
        
        // Add game DLLs if provided
        if (!string.IsNullOrEmpty(refsPath) && Directory.Exists(refsPath))
        {
            foreach (var dll in Directory.GetFiles(refsPath, "*.dll"))
            {
                try
                {
                    refs.Add(MetadataReference.CreateFromFile(dll));
                }
                catch
                {
                    // Skip problematic DLLs
                }
            }
        }
        
        return refs;
    }
    
    /// <summary>
    /// Parse all C# files in the source directory and extract types/methods.
    /// </summary>
    public void ExtractToDatabase(string sourcePath, SqliteWriter db)
    {
        _sourcePath = sourcePath;
        var files = Directory.GetFiles(sourcePath, "*.cs", SearchOption.AllDirectories);
        Console.WriteLine($"Parsing {files.Length} files...");
        
        // Create syntax trees for all files
        var syntaxTrees = new List<SyntaxTree>();
        foreach (var file in files)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code, path: file);
            syntaxTrees.Add(tree);
        }
        Console.WriteLine($"Created {syntaxTrees.Count} syntax trees");
        
        // Create compilation for semantic analysis
        _compilation = CSharpCompilation.Create(
            "GameCode",
            syntaxTrees,
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        
        // Check for critical errors (informational only)
        var diagnostics = _compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Take(10)
            .ToList();
        
        if (diagnostics.Any())
        {
            Console.WriteLine($"Note: {diagnostics.Count}+ compilation errors (expected for partial codebase)");
            if (_verbose)
            {
                foreach (var d in diagnostics.Take(3))
                {
                    Console.WriteLine($"  {d.GetMessage()}");
                }
            }
        }
        
        // Process each file
        var typeCount = 0;
        var methodCount = 0;
        
        using var transaction = db.BeginTransaction();
        
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var filePath = tree.FilePath;
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var semanticModel = _compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            
            // Find all type declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();
            
            foreach (var typeDecl in typeDeclarations)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (typeSymbol == null) continue;
                
                var typeId = ExtractType(db, typeSymbol, relativePath, typeDecl);
                typeCount++;
                
                // Extract methods from this type
                foreach (var member in typeDecl.Members)
                {
                    if (member is MethodDeclarationSyntax methodDecl)
                    {
                        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                        if (methodSymbol != null)
                        {
                            ExtractMethod(db, methodSymbol, typeId, relativePath, methodDecl);
                            methodCount++;
                        }
                    }
                    else if (member is ConstructorDeclarationSyntax ctorDecl)
                    {
                        var ctorSymbol = semanticModel.GetDeclaredSymbol(ctorDecl);
                        if (ctorSymbol != null)
                        {
                            ExtractConstructor(db, ctorSymbol, typeId, relativePath, ctorDecl);
                            methodCount++;
                        }
                    }
                    else if (member is PropertyDeclarationSyntax propDecl)
                    {
                        var propSymbol = semanticModel.GetDeclaredSymbol(propDecl);
                        if (propSymbol != null)
                        {
                            ExtractProperty(db, propSymbol, typeId, relativePath, propDecl);
                            // Properties can have get/set methods - count them
                            if (propDecl.AccessorList != null)
                                methodCount += propDecl.AccessorList.Accessors.Count;
                        }
                    }
                }
            }
            
            if (_verbose && typeCount % 500 == 0)
            {
                Console.WriteLine($"  Processed {typeCount} types, {methodCount} methods...");
            }
        }
        
        transaction.Commit();
        
        Console.WriteLine($"Extracted {typeCount} types, {methodCount} methods");
    }
    
    /// <summary>
    /// Extract and store a type declaration.
    /// </summary>
    private long ExtractType(SqliteWriter db, INamedTypeSymbol symbol, string filePath, TypeDeclarationSyntax decl)
    {
        var kind = symbol.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            _ => "class"
        };
        
        var baseType = symbol.BaseType?.ToDisplayString();
        if (baseType == "object" || baseType == "System.Object")
            baseType = null;
        
        var lineSpan = decl.GetLocation().GetLineSpan();
        var lineNumber = lineSpan.StartLinePosition.Line + 1;
        
        var typeId = db.InsertType(
            name: symbol.Name,
            @namespace: symbol.ContainingNamespace?.ToDisplayString(),
            fullName: symbol.ToDisplayString(),
            kind: kind,
            baseType: baseType,
            filePath: filePath,
            lineNumber: lineNumber,
            isAbstract: symbol.IsAbstract,
            isSealed: symbol.IsSealed,
            isStatic: symbol.IsStatic
        );
        
        _symbolToId[symbol] = typeId;
        
        // Record implemented interfaces
        foreach (var iface in symbol.Interfaces)
        {
            db.InsertImplements(typeId, iface.ToDisplayString());
        }
        
        return typeId;
    }
    
    /// <summary>
    /// Extract and store a method declaration.
    /// </summary>
    private void ExtractMethod(SqliteWriter db, IMethodSymbol symbol, long typeId, string filePath, MethodDeclarationSyntax decl)
    {
        var signature = BuildSignature(symbol);
        var lineSpan = decl.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;
        
        var methodId = db.InsertMethod(
            typeId: typeId,
            name: symbol.Name,
            signature: signature,
            returnType: symbol.ReturnType.ToDisplayString(),
            filePath: filePath,
            lineNumber: startLine,
            endLine: endLine,
            isStatic: symbol.IsStatic,
            isVirtual: symbol.IsVirtual,
            isOverride: symbol.IsOverride,
            isAbstract: symbol.IsAbstract,
            access: AccessibilityToString(symbol.DeclaredAccessibility)
        );
        
        _symbolToId[symbol] = methodId;
        
        // Store signature -> ID mapping for call resolution
        var fullSig = $"{symbol.ContainingType.ToDisplayString()}.{signature}";
        _signatureToMethodId[fullSig] = methodId;
        
        // Store method body for FTS
        if (decl.Body != null)
        {
            db.InsertMethodBody(methodId, decl.Body.ToFullString());
        }
        else if (decl.ExpressionBody != null)
        {
            db.InsertMethodBody(methodId, decl.ExpressionBody.ToFullString());
        }
    }
    
    /// <summary>
    /// Extract and store a constructor.
    /// </summary>
    private void ExtractConstructor(SqliteWriter db, IMethodSymbol symbol, long typeId, string filePath, ConstructorDeclarationSyntax decl)
    {
        var signature = BuildSignature(symbol);
        var lineSpan = decl.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;
        
        var methodId = db.InsertMethod(
            typeId: typeId,
            name: symbol.IsStatic ? ".cctor" : ".ctor",
            signature: signature,
            returnType: null,
            filePath: filePath,
            lineNumber: startLine,
            endLine: endLine,
            isStatic: symbol.IsStatic,
            isVirtual: false,
            isOverride: false,
            isAbstract: false,
            access: AccessibilityToString(symbol.DeclaredAccessibility)
        );
        
        _symbolToId[symbol] = methodId;
        
        var fullSig = $"{symbol.ContainingType.ToDisplayString()}.{signature}";
        _signatureToMethodId[fullSig] = methodId;
        
        if (decl.Body != null)
        {
            db.InsertMethodBody(methodId, decl.Body.ToFullString());
        }
    }
    
    /// <summary>
    /// Extract property accessors as methods.
    /// </summary>
    private void ExtractProperty(SqliteWriter db, IPropertySymbol symbol, long typeId, string filePath, PropertyDeclarationSyntax decl)
    {
        var lineSpan = decl.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;
        
        if (symbol.GetMethod != null)
        {
            var getSignature = $"get_{symbol.Name}()";
            var methodId = db.InsertMethod(
                typeId: typeId,
                name: $"get_{symbol.Name}",
                signature: getSignature,
                returnType: symbol.Type.ToDisplayString(),
                filePath: filePath,
                lineNumber: startLine,
                endLine: endLine,
                isStatic: symbol.IsStatic,
                isVirtual: symbol.GetMethod.IsVirtual,
                isOverride: symbol.GetMethod.IsOverride,
                isAbstract: symbol.GetMethod.IsAbstract,
                access: AccessibilityToString(symbol.GetMethod.DeclaredAccessibility)
            );
            _symbolToId[symbol.GetMethod] = methodId;
            
            var fullSig = $"{symbol.ContainingType.ToDisplayString()}.{getSignature}";
            _signatureToMethodId[fullSig] = methodId;
        }
        
        if (symbol.SetMethod != null)
        {
            var setSignature = $"set_{symbol.Name}({symbol.Type.ToDisplayString()})";
            var methodId = db.InsertMethod(
                typeId: typeId,
                name: $"set_{symbol.Name}",
                signature: setSignature,
                returnType: "void",
                filePath: filePath,
                lineNumber: startLine,
                endLine: endLine,
                isStatic: symbol.IsStatic,
                isVirtual: symbol.SetMethod.IsVirtual,
                isOverride: symbol.SetMethod.IsOverride,
                isAbstract: symbol.SetMethod.IsAbstract,
                access: AccessibilityToString(symbol.SetMethod.DeclaredAccessibility)
            );
            _symbolToId[symbol.SetMethod] = methodId;
            
            var fullSig = $"{symbol.ContainingType.ToDisplayString()}.{setSignature}";
            _signatureToMethodId[fullSig] = methodId;
        }
    }
    
    /// <summary>
    /// Build a method signature string like "GetItemCount(string, int)".
    /// </summary>
    private string BuildSignature(IMethodSymbol method)
    {
        var paramTypes = string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString()));
        var name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;
        return $"{name}({paramTypes})";
    }
    
    private string AccessibilityToString(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private"
        };
    }
    
    /// <summary>
    /// Get the method ID for a symbol (used in call extraction).
    /// </summary>
    public long? GetMethodId(IMethodSymbol symbol)
    {
        if (_symbolToId.TryGetValue(symbol, out var id))
            return id;
        
        // Try signature lookup as fallback
        var fullSig = $"{symbol.ContainingType?.ToDisplayString()}.{BuildSignature(symbol)}";
        if (_signatureToMethodId.TryGetValue(fullSig, out id))
            return id;
        
        return null;
    }
    
    /// <summary>
    /// Get the symbol-to-ID mappings for call extraction.
    /// </summary>
    public IReadOnlyDictionary<ISymbol, long> SymbolToId => _symbolToId;
    public IReadOnlyDictionary<string, long> SignatureToMethodId => _signatureToMethodId;
    
    /// <summary>
    /// Get the compilation for call extraction (available after ExtractToDatabase).
    /// </summary>
    public CSharpCompilation? Compilation => _compilation;
    
    /// <summary>
    /// Get the source path (available after ExtractToDatabase).
    /// </summary>
    public string? SourcePath => _sourcePath;
}
