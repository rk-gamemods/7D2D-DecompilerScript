using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CallGraphExtractor;

/// <summary>
/// Parses mod source files, detecting Harmony patches and extracting mod code structure.
/// </summary>
public class ModParser
{
    private readonly SqliteWriter _db;
    private readonly bool _verbose;
    
    // Track parsed mods to avoid duplicates
    private readonly Dictionary<string, long> _modNameToId = new();
    
    // Statistics
    private int _modCount;
    private int _typeCount;
    private int _methodCount;
    private int _patchCount;
    
    public ModParser(SqliteWriter db, bool verbose = false)
    {
        _db = db;
        _verbose = verbose;
    }
    
    public int ModCount => _modCount;
    public int TypeCount => _typeCount;
    public int MethodCount => _methodCount;
    public int PatchCount => _patchCount;
    
    /// <summary>
    /// Parse a mod directory. Expects structure like:
    ///   ModName/
    ///     ModInfo.xml (or ModInfo.txt)
    ///     *.cs files or Source/*.cs
    ///     Config/*.xml (optional XML changes)
    /// </summary>
    public void ParseMod(string modPath)
    {
        var modDir = new DirectoryInfo(modPath);
        if (!modDir.Exists) return;
        
        // Read mod info
        var (modName, version, author) = ReadModInfo(modPath);
        if (string.IsNullOrEmpty(modName))
        {
            modName = modDir.Name;
        }
        
        Console.WriteLine($"  Parsing mod: {modName}");
        
        // Register the mod
        var modId = _db.InsertMod(modName, modPath, version, author);
        _modNameToId[modName] = modId;
        _modCount++;
        
        // Find C# source files
        var csFiles = FindCSharpFiles(modPath);
        if (csFiles.Count == 0)
        {
            if (_verbose)
                Console.WriteLine($"    No C# files found");
            return;
        }
        
        if (_verbose)
            Console.WriteLine($"    Found {csFiles.Count} C# files");
        
        // Parse each file
        foreach (var csFile in csFiles)
        {
            try
            {
                ParseSourceFile(csFile, modId);
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.WriteLine($"    Warning: Failed to parse {Path.GetFileName(csFile)}: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Parse all mods in a Mods directory.
    /// </summary>
    public void ParseModsDirectory(string modsPath)
    {
        if (!Directory.Exists(modsPath))
        {
            Console.WriteLine($"  Warning: Mods directory not found: {modsPath}");
            return;
        }
        
        Console.WriteLine($"Parsing mods from {modsPath}...");
        
        using var transaction = _db.BeginTransaction();
        
        foreach (var modDir in Directory.GetDirectories(modsPath))
        {
            // Check if this looks like a mod (has ModInfo.xml or C# files)
            if (IsModDirectory(modDir))
            {
                try
                {
                    ParseMod(modDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Failed to parse mod {Path.GetFileName(modDir)}: {ex.Message}");
                }
            }
        }
        
        transaction.Commit();
        
        Console.WriteLine($"  Parsed {_modCount} mods, {_typeCount} types, {_methodCount} methods, {_patchCount} Harmony patches");
    }
    
    /// <summary>
    /// Parse a single mod source file.
    /// </summary>
    private void ParseSourceFile(string filePath, long modId)
    {
        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code, path: filePath);
        var root = tree.GetRoot();
        
        var fileName = Path.GetFileName(filePath);
        
        // Find all type declarations (classes, structs)
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            ParseTypeDeclaration(typeDecl, modId, fileName);
        }
    }
    
    /// <summary>
    /// Parse a type declaration and its Harmony patches.
    /// </summary>
    private void ParseTypeDeclaration(TypeDeclarationSyntax typeDecl, long modId, string fileName)
    {
        var typeName = typeDecl.Identifier.Text;
        var namespaceName = GetNamespace(typeDecl);
        var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
        
        var kind = typeDecl switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            _ => "type"
        };
        
        // Get base type
        string? baseType = null;
        if (typeDecl.BaseList != null && typeDecl.BaseList.Types.Count > 0)
        {
            baseType = typeDecl.BaseList.Types[0].Type.ToString();
        }
        
        var lineSpan = typeDecl.GetLocation().GetLineSpan();
        var lineNumber = lineSpan.StartLinePosition.Line + 1;
        
        // Insert mod type
        var modTypeId = _db.InsertModType(modId, typeName, namespaceName, fullName, kind, baseType, fileName, lineNumber);
        _typeCount++;
        
        // Check for HarmonyPatch attribute on the class
        var classTargetType = ExtractHarmonyPatchTarget(typeDecl.AttributeLists);
        
        // Parse methods
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            ParseMethodDeclaration(method, modTypeId, classTargetType, fileName);
        }
    }
    
    /// <summary>
    /// Parse a method declaration and detect Harmony patches.
    /// </summary>
    private void ParseMethodDeclaration(MethodDeclarationSyntax method, long modTypeId, 
                                         HarmonyPatchTarget? classTarget, string fileName)
    {
        var methodName = method.Identifier.Text;
        var signature = BuildSignature(method);
        var returnType = method.ReturnType.ToString();
        var isStatic = method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        var access = GetAccessModifier(method.Modifiers);
        
        var lineSpan = method.GetLocation().GetLineSpan();
        var lineNumber = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;
        
        // Insert mod method
        var modMethodId = _db.InsertModMethod(modTypeId, methodName, signature, returnType, 
                                               fileName, lineNumber, endLine, isStatic, access);
        _methodCount++;
        
        // Store method body
        var body = method.Body?.ToString() ?? method.ExpressionBody?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(body))
        {
            _db.InsertModMethodBody(modMethodId, body);
        }
        
        // Check for Harmony attributes
        var methodTarget = ExtractHarmonyPatchTarget(method.AttributeLists);
        var patchType = DetectPatchType(method.AttributeLists);
        
        if (patchType != null)
        {
            // Determine target from method attributes or class attributes
            var targetType = methodTarget?.TargetType ?? classTarget?.TargetType;
            var targetMethod = methodTarget?.TargetMethod ?? classTarget?.TargetMethod ?? methodName;
            
            if (targetType != null)
            {
                _db.InsertHarmonyPatch(modMethodId, targetType, targetMethod, patchType, fileName, lineNumber);
                _patchCount++;
                
                if (_verbose)
                    Console.WriteLine($"      Found {patchType}: {targetType}.{targetMethod}");
            }
        }
    }
    
    /// <summary>
    /// Extract HarmonyPatch target info from attributes.
    /// </summary>
    private HarmonyPatchTarget? ExtractHarmonyPatchTarget(SyntaxList<AttributeListSyntax> attributeLists)
    {
        string? targetType = null;
        string? targetMethod = null;
        
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();
                if (attrName != "HarmonyPatch" && attrName != "HarmonyPatchAttribute")
                    continue;
                
                if (attr.ArgumentList == null)
                    continue;
                
                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    var argExpr = arg.Expression;
                    
                    // typeof(TargetType)
                    if (argExpr is TypeOfExpressionSyntax typeOf)
                    {
                        targetType = typeOf.Type.ToString();
                    }
                    // "MethodName" string literal
                    else if (argExpr is LiteralExpressionSyntax literal && 
                             literal.Kind() == SyntaxKind.StringLiteralExpression)
                    {
                        targetMethod = literal.Token.ValueText;
                    }
                    // nameof(Method)
                    else if (argExpr is InvocationExpressionSyntax inv &&
                             inv.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" })
                    {
                        if (inv.ArgumentList.Arguments.Count > 0)
                        {
                            var nameofArg = inv.ArgumentList.Arguments[0].Expression;
                            targetMethod = nameofArg is IdentifierNameSyntax id 
                                ? id.Identifier.Text 
                                : nameofArg.ToString();
                        }
                    }
                }
            }
        }
        
        if (targetType == null && targetMethod == null)
            return null;
        
        return new HarmonyPatchTarget(targetType, targetMethod);
    }
    
    /// <summary>
    /// Detect the Harmony patch type from method attributes.
    /// </summary>
    private string? DetectPatchType(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString().Replace("Attribute", "");
                
                if (attrName is "HarmonyPrefix" or "Prefix")
                    return "Prefix";
                if (attrName is "HarmonyPostfix" or "Postfix")
                    return "Postfix";
                if (attrName is "HarmonyTranspiler" or "Transpiler")
                    return "Transpiler";
                if (attrName is "HarmonyFinalizer" or "Finalizer")
                    return "Finalizer";
                if (attrName is "HarmonyReversePatch" or "ReversePatch")
                    return "ReversePatch";
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Read mod info from ModInfo.xml.
    /// </summary>
    private (string? name, string? version, string? author) ReadModInfo(string modPath)
    {
        var modInfoPath = Path.Combine(modPath, "ModInfo.xml");
        if (!File.Exists(modInfoPath))
            return (null, null, null);
        
        try
        {
            var doc = XDocument.Load(modInfoPath);
            var root = doc.Root;
            if (root == null) return (null, null, null);
            
            var name = root.Element("Name")?.Attribute("value")?.Value 
                    ?? root.Element("name")?.Value;
            var version = root.Element("Version")?.Attribute("value")?.Value
                       ?? root.Element("version")?.Value;
            var author = root.Element("Author")?.Attribute("value")?.Value
                      ?? root.Element("author")?.Value;
            
            return (name, version, author);
        }
        catch
        {
            return (null, null, null);
        }
    }
    
    /// <summary>
    /// Find C# source files in a mod directory.
    /// </summary>
    private List<string> FindCSharpFiles(string modPath)
    {
        var files = new List<string>();
        
        // Direct .cs files in mod root
        files.AddRange(Directory.GetFiles(modPath, "*.cs", SearchOption.TopDirectoryOnly));
        
        // Source subdirectory
        var sourceDir = Path.Combine(modPath, "Source");
        if (Directory.Exists(sourceDir))
        {
            files.AddRange(Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories));
        }
        
        // Scripts subdirectory
        var scriptsDir = Path.Combine(modPath, "Scripts");
        if (Directory.Exists(scriptsDir))
        {
            files.AddRange(Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories));
        }
        
        // ModName subdirectory (common pattern)
        var modName = Path.GetFileName(modPath);
        var modSubDir = Path.Combine(modPath, modName);
        if (Directory.Exists(modSubDir))
        {
            files.AddRange(Directory.GetFiles(modSubDir, "*.cs", SearchOption.AllDirectories));
        }
        
        return files.Distinct().ToList();
    }
    
    /// <summary>
    /// Check if a directory looks like a mod.
    /// </summary>
    private bool IsModDirectory(string path)
    {
        // Has ModInfo.xml
        if (File.Exists(Path.Combine(path, "ModInfo.xml")))
            return true;
        
        // Has C# files
        if (Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Any())
            return true;
        
        // Has Config directory with XML files
        var configDir = Path.Combine(path, "Config");
        if (Directory.Exists(configDir) && Directory.GetFiles(configDir, "*.xml").Any())
            return true;
        
        return false;
    }
    
    private string? GetNamespace(TypeDeclarationSyntax typeDecl)
    {
        var parent = typeDecl.Parent;
        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            if (parent is FileScopedNamespaceDeclarationSyntax fsns)
                return fsns.Name.ToString();
            parent = parent.Parent;
        }
        return null;
    }
    
    private string BuildSignature(MethodDeclarationSyntax method)
    {
        var parameters = string.Join(", ", method.ParameterList.Parameters
            .Select(p => $"{p.Type} {p.Identifier}"));
        return $"{method.Identifier}({parameters})";
    }
    
    private string GetAccessModifier(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return "public";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword))) return "private";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword))) return "protected";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "internal";
        return "private";
    }
    
    private record HarmonyPatchTarget(string? TargetType, string? TargetMethod);
}
