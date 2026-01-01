using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CallGraphExtractor;

/// <summary>
/// Parses C# source files using Roslyn to extract type and method information.
/// Supports multiple source directories (e.g., Assembly-CSharp + Assembly-CSharp-firstpass).
/// </summary>
public class RoslynParser
{
    private readonly bool _verbose;
    private readonly List<MetadataReference> _references = new();
    
    // Maps to track IDs for later call edge resolution
    private readonly Dictionary<ISymbol, long> _symbolToId = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, long> _signatureToMethodId = new();
    
    // Keep compilation for call extraction
    private CSharpCompilation? _compilation;
    
    // Track loaded/failed DLLs for reporting
    private int _loadedDllCount = 0;
    private int _failedDllCount = 0;
    private readonly List<string> _failedDlls = new();
    
    // Track source directories and their assembly names
    private readonly List<SourceDirectory> _sourceDirectories = new();
    
    public record SourceDirectory(string Path, string AssemblyName);
    
    public RoslynParser(bool verbose = false)
    {
        _verbose = verbose;
    }
    
    /// <summary>
    /// Load ALL metadata references from:
    /// 1. .NET runtime (TRUSTED_PLATFORM_ASSEMBLIES)
    /// 2. Game installation directory (recursive search)
    /// </summary>
    public void LoadReferences(string? gameRoot)
    {
        Console.WriteLine("Loading metadata references...");
        
        // Always include core .NET references
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        
        foreach (var assembly in trustedAssemblies)
        {
            TryLoadReference(assembly);
        }
        Console.WriteLine($"  Loaded {_loadedDllCount} .NET runtime assemblies");
        
        // Load ALL DLLs from game directory (recursive)
        if (!string.IsNullOrEmpty(gameRoot) && Directory.Exists(gameRoot))
        {
            var beforeCount = _loadedDllCount;
            Console.WriteLine($"  Scanning {gameRoot} for DLLs...");
            
            foreach (var dll in Directory.GetFiles(gameRoot, "*.dll", SearchOption.AllDirectories))
            {
                TryLoadReference(dll);
            }
            
            Console.WriteLine($"  Loaded {_loadedDllCount - beforeCount} game DLLs");
        }
        
        // Report failures (likely native DLLs)
        if (_failedDllCount > 0)
        {
            Console.WriteLine($"  Skipped {_failedDllCount} DLLs (likely native/unmanaged)");
            if (_verbose && _failedDlls.Count > 0)
            {
                foreach (var dll in _failedDlls.Take(10))
                {
                    Console.WriteLine($"    - {Path.GetFileName(dll)}");
                }
                if (_failedDlls.Count > 10)
                    Console.WriteLine($"    ... and {_failedDlls.Count - 10} more");
            }
        }
        
        Console.WriteLine($"  Total: {_references.Count} references loaded");
    }
    
    /// <summary>
    /// Try to load a DLL as a metadata reference. Fails silently for native DLLs.
    /// </summary>
    private void TryLoadReference(string dllPath)
    {
        if (!File.Exists(dllPath)) return;
        
        try
        {
            _references.Add(MetadataReference.CreateFromFile(dllPath));
            _loadedDllCount++;
        }
        catch
        {
            // Likely a native DLL - skip silently but track for reporting
            _failedDllCount++;
            _failedDlls.Add(dllPath);
        }
    }
    
    /// <summary>
    /// Add a source directory to parse. Call before ExtractToDatabase.
    /// </summary>
    public void AddSourceDirectory(string path, string? assemblyName = null)
    {
        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Warning: Source directory not found: {path}");
            return;
        }
        
        // Default assembly name to directory name
        assemblyName ??= Path.GetFileName(path);
        
        _sourceDirectories.Add(new SourceDirectory(path, assemblyName));
        Console.WriteLine($"  Added source directory: {assemblyName} ({path})");
    }
    
    /// <summary>
    /// Auto-discover source directories by looking for Assembly-* siblings.
    /// </summary>
    public void AutoDiscoverSourceDirectories(string basePath)
    {
        Console.WriteLine($"Auto-discovering source directories in {basePath}...");
        
        if (!Directory.Exists(basePath))
        {
            Console.WriteLine($"Warning: Base path not found: {basePath}");
            return;
        }
        
        // Look for Assembly-* directories
        var assemblyDirs = Directory.GetDirectories(basePath, "Assembly-*");
        foreach (var dir in assemblyDirs.OrderBy(d => d))
        {
            var assemblyName = Path.GetFileName(dir);
            AddSourceDirectory(dir, assemblyName);
        }
        
        // If no Assembly-* dirs found, check if basePath itself is a source directory
        if (!_sourceDirectories.Any())
        {
            var csFiles = Directory.GetFiles(basePath, "*.cs", SearchOption.TopDirectoryOnly);
            if (csFiles.Any())
            {
                AddSourceDirectory(basePath);
            }
        }
        
        Console.WriteLine($"  Found {_sourceDirectories.Count} source directories");
    }
    
    /// <summary>
    /// Parse all source directories and extract types/methods into database.
    /// </summary>
    public void ExtractToDatabase(SqliteWriter db)
    {
        if (!_sourceDirectories.Any())
        {
            throw new InvalidOperationException("No source directories configured. Call AddSourceDirectory or AutoDiscoverSourceDirectories first.");
        }
        
        // Collect all source files with their assembly names
        var allFiles = new List<(string Path, string Assembly)>();
        foreach (var srcDir in _sourceDirectories)
        {
            var files = Directory.GetFiles(srcDir.Path, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                allFiles.Add((file, srcDir.AssemblyName));
            }
        }
        
        Console.WriteLine($"Parsing {allFiles.Count} files from {_sourceDirectories.Count} assemblies...");
        
        // Create syntax trees for all files
        var syntaxTrees = new List<SyntaxTree>();
        var treeToAssembly = new Dictionary<SyntaxTree, string>();
        
        foreach (var (filePath, assembly) in allFiles)
        {
            var code = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(code, path: filePath);
            syntaxTrees.Add(tree);
            treeToAssembly[tree] = assembly;
        }
        Console.WriteLine($"Created {syntaxTrees.Count} syntax trees");
        
        // Create compilation for semantic analysis
        _compilation = CSharpCompilation.Create(
            "GameCode",
            syntaxTrees,
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        
        // Report diagnostic summary
        var errorCount = _compilation.GetDiagnostics()
            .Count(d => d.Severity == DiagnosticSeverity.Error);
        
        if (errorCount > 0)
        {
            Console.WriteLine($"Note: {errorCount} compilation errors (expected for decompiled code)");
            if (_verbose)
            {
                var samples = _compilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(5);
                foreach (var d in samples)
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
            var assembly = treeToAssembly.GetValueOrDefault(tree, "Unknown");
            
            // Find the source directory this file belongs to for relative path
            var srcDir = _sourceDirectories.FirstOrDefault(s => filePath.StartsWith(s.Path));
            var relativePath = srcDir != null 
                ? Path.GetRelativePath(srcDir.Path, filePath)
                : Path.GetFileName(filePath);
            
            var semanticModel = _compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            
            // Find all type declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();
            
            foreach (var typeDecl in typeDeclarations)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (typeSymbol == null) continue;
                
                var typeId = ExtractType(db, typeSymbol, assembly, relativePath, typeDecl);
                typeCount++;
                
                // Extract methods from this type
                foreach (var member in typeDecl.Members)
                {
                    if (member is MethodDeclarationSyntax methodDecl)
                    {
                        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                        if (methodSymbol != null)
                        {
                            ExtractMethod(db, methodSymbol, typeId, assembly, relativePath, methodDecl);
                            methodCount++;
                        }
                    }
                    else if (member is ConstructorDeclarationSyntax ctorDecl)
                    {
                        var ctorSymbol = semanticModel.GetDeclaredSymbol(ctorDecl);
                        if (ctorSymbol != null)
                        {
                            ExtractConstructor(db, ctorSymbol, typeId, assembly, relativePath, ctorDecl);
                            methodCount++;
                        }
                    }
                    else if (member is PropertyDeclarationSyntax propDecl)
                    {
                        var propSymbol = semanticModel.GetDeclaredSymbol(propDecl);
                        if (propSymbol != null)
                        {
                            ExtractProperty(db, propSymbol, typeId, assembly, relativePath, propDecl);
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
        
        // Print per-assembly breakdown
        Console.WriteLine($"Extracted {typeCount} types, {methodCount} methods");
        foreach (var srcDir in _sourceDirectories)
        {
            var asmTypes = _compilation.SyntaxTrees
                .Where(t => treeToAssembly.GetValueOrDefault(t) == srcDir.AssemblyName)
                .Count();
            Console.WriteLine($"  {srcDir.AssemblyName}: {asmTypes} files");
        }
    }
    
    /// <summary>
    /// Extract and store a type declaration.
    /// </summary>
    private long ExtractType(SqliteWriter db, INamedTypeSymbol symbol, string assembly, string filePath, TypeDeclarationSyntax decl)
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
            assembly: assembly,
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
    private void ExtractMethod(SqliteWriter db, IMethodSymbol symbol, long typeId, string assembly, string filePath, MethodDeclarationSyntax decl)
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
            assembly: assembly,
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
    private void ExtractConstructor(SqliteWriter db, IMethodSymbol symbol, long typeId, string assembly, string filePath, ConstructorDeclarationSyntax decl)
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
            assembly: assembly,
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
    private void ExtractProperty(SqliteWriter db, IPropertySymbol symbol, long typeId, string assembly, string filePath, PropertyDeclarationSyntax decl)
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
                assembly: assembly,
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
                assembly: assembly,
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
    /// Get the source directories being parsed.
    /// </summary>
    public IReadOnlyList<SourceDirectory> SourceDirectories => _sourceDirectories;
}
