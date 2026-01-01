using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace CallGraphExtractor;

/// <summary>
/// Discovers ALL Harmony patches in mod code using AST parsing.
/// Handles both attribute-based patches and runtime Harmony.Patch() calls.
/// </summary>
public class HarmonyPatchDiscovery
{
    private readonly bool _verbose;
    private readonly List<HarmonyPatchInfo> _patches = new();
    
    public HarmonyPatchDiscovery(bool verbose = false)
    {
        _verbose = verbose;
    }
    
    public IReadOnlyList<HarmonyPatchInfo> Patches => _patches;
    public int PatchCount => _patches.Count;
    public int AttributePatchCount => _patches.Count(p => !p.IsRuntimePatch);
    public int RuntimePatchCount => _patches.Count(p => p.IsRuntimePatch);
    
    /// <summary>
    /// Discover all Harmony patches in the given source files.
    /// </summary>
    public void DiscoverPatches(IEnumerable<string> sourceFiles)
    {
        foreach (var file in sourceFiles)
        {
            if (!File.Exists(file)) continue;
            
            try
            {
                var code = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(code, path: file);
                var root = tree.GetRoot();
                
                // Find attribute-based patches
                DiscoverAttributePatches(root, file);
                
                // Find runtime Harmony.Patch() calls
                DiscoverRuntimePatches(root, file);
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.WriteLine($"  Warning: Failed to parse {file}: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Discover patches declared via [HarmonyPatch] attributes.
    /// </summary>
    private void DiscoverAttributePatches(SyntaxNode root, string filePath)
    {
        // Find all classes with HarmonyPatch attribute
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var classPatches = ExtractPatchesFromAttributes(classDecl.AttributeLists, classDecl, filePath);
            
            // Check methods within the class for patch type attributes
            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodName = method.Identifier.Text;
                var patchType = DeterminePatchTypeFromMethodOrAttributes(method);
                
                if (patchType != null)
                {
                    // Method-level patches (or inheriting class-level target)
                    var methodPatches = ExtractPatchesFromAttributes(method.AttributeLists, classDecl, filePath);
                    
                    // Also try to extract method name from method-level [HarmonyPatch("methodName")]
                    string? methodLevelTargetMethod = ExtractMethodNameFromAttributes(method.AttributeLists);
                    
                    if (methodPatches.Any())
                    {
                        foreach (var patch in methodPatches)
                        {
                            patch.PatchMethod = methodName;
                            patch.Type = patchType.Value;
                            _patches.Add(patch);
                            
                            if (_verbose)
                                Console.WriteLine($"    [Attribute] {patch.Type}: {patch.TargetType}.{patch.TargetMethod}");
                        }
                    }
                    else if (classPatches.Any())
                    {
                        // Method inherits target type from class, but may have method name from method-level attribute
                        foreach (var classPatch in classPatches)
                        {
                            var patch = classPatch.Clone();
                            patch.PatchMethod = methodName;
                            patch.Type = patchType.Value;
                            
                            // Merge method-level target method if present
                            if (!string.IsNullOrEmpty(methodLevelTargetMethod))
                            {
                                patch.TargetMethod = methodLevelTargetMethod;
                            }
                            
                            _patches.Add(patch);
                            
                            if (_verbose)
                                Console.WriteLine($"    [Attribute] {patch.Type}: {patch.TargetType}.{patch.TargetMethod}");
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Extract just the method name from method-level [HarmonyPatch("methodName")] attributes.
    /// </summary>
    private string? ExtractMethodNameFromAttributes(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();
                if (!attrName.Contains("HarmonyPatch")) continue;
                
                if (attr.ArgumentList == null) continue;
                
                var args = attr.ArgumentList.Arguments.ToList();
                foreach (var arg in args)
                {
                    var argExpr = arg.Expression;
                    
                    // String literal: "methodName"
                    if (argExpr is LiteralExpressionSyntax literal && 
                        literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        return literal.Token.ValueText;
                    }
                    // nameof(Class.Method)
                    else if (argExpr is InvocationExpressionSyntax invocation &&
                             invocation.Expression.ToString() == "nameof")
                    {
                        var nameofArg = invocation.ArgumentList.Arguments.FirstOrDefault();
                        if (nameofArg != null)
                        {
                            var nameText = nameofArg.Expression.ToString();
                            return nameText.Contains('.') 
                                ? nameText.Split('.').Last() 
                                : nameText;
                        }
                    }
                }
            }
        }
        return null;
    }
    
    /// <summary>
    /// Extract patch info from HarmonyPatch attributes.
    /// </summary>
    private List<HarmonyPatchInfo> ExtractPatchesFromAttributes(
        SyntaxList<AttributeListSyntax> attributeLists, 
        ClassDeclarationSyntax classDecl,
        string filePath)
    {
        var patches = new List<HarmonyPatchInfo>();
        
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();
                if (!attrName.Contains("HarmonyPatch")) continue;
                
                var patch = new HarmonyPatchInfo
                {
                    PatchClass = classDecl.Identifier.Text,
                    FilePath = filePath,
                    LineNumber = attr.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    IsRuntimePatch = false
                };
                
                if (attr.ArgumentList == null) continue;
                
                var args = attr.ArgumentList.Arguments.ToList();
                if (args.Count == 0) continue;
                
                // Parse arguments
                // Pattern 1: [HarmonyPatch(typeof(TargetClass), "MethodName")]
                // Pattern 2: [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.Method))]
                // Pattern 3: [HarmonyPatch(typeof(TargetClass), "Method", new Type[] { typeof(int) })]
                
                for (int i = 0; i < args.Count; i++)
                {
                    var arg = args[i];
                    var argExpr = arg.Expression;
                    
                    // typeof(TargetClass)
                    if (argExpr is TypeOfExpressionSyntax typeOf)
                    {
                        patch.TargetType = typeOf.Type.ToString();
                    }
                    // "MethodName" or nameof(...)
                    else if (argExpr is LiteralExpressionSyntax literal && 
                             literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        patch.TargetMethod = literal.Token.ValueText;
                    }
                    else if (argExpr is InvocationExpressionSyntax invocation &&
                             invocation.Expression.ToString() == "nameof")
                    {
                        var nameofArg = invocation.ArgumentList.Arguments.FirstOrDefault();
                        if (nameofArg != null)
                        {
                            var nameText = nameofArg.Expression.ToString();
                            // Extract just the method name from "ClassName.MethodName"
                            patch.TargetMethod = nameText.Contains('.') 
                                ? nameText.Split('.').Last() 
                                : nameText;
                        }
                    }
                    // new Type[] { typeof(int), typeof(string) }
                    else if (argExpr is ArrayCreationExpressionSyntax arrayCreation)
                    {
                        patch.TargetArgumentTypes = ExtractTypeArray(arrayCreation);
                    }
                    else if (argExpr is ImplicitArrayCreationExpressionSyntax implicitArray)
                    {
                        patch.TargetArgumentTypes = ExtractTypeArrayFromInitializer(implicitArray.Initializer);
                    }
                    // MethodType enum
                    else if (argExpr is MemberAccessExpressionSyntax memberAccess &&
                             memberAccess.Expression.ToString().Contains("MethodType"))
                    {
                        // Handle MethodType.Getter, MethodType.Setter, etc.
                        patch.MethodType = memberAccess.Name.ToString();
                    }
                }
                
                if (!string.IsNullOrEmpty(patch.TargetType))
                {
                    patches.Add(patch);
                }
            }
        }
        
        return patches;
    }
    
    /// <summary>
    /// Discover patches created via runtime Harmony.Patch() calls.
    /// </summary>
    private void DiscoverRuntimePatches(SyntaxNode root, string filePath)
    {
        // Find all invocations of .Patch() method
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // Check if this is a .Patch() call
            string? methodName = null;
            
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                methodName = memberAccess.Name.Identifier.Text;
            }
            else if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                methodName = identifier.Identifier.Text;
            }
            
            if (methodName != "Patch") continue;
            
            // Parse the Patch() arguments
            var args = invocation.ArgumentList.Arguments.ToList();
            if (args.Count == 0) continue;
            
            var patch = new HarmonyPatchInfo
            {
                FilePath = filePath,
                LineNumber = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                IsRuntimePatch = true,
                PatchClass = GetContainingTypeName(invocation) ?? "Unknown"
            };
            
            // First argument is the target method (usually AccessTools.Method(...))
            var targetArg = args[0].Expression;
            ParseAccessToolsMethod(targetArg, patch);
            
            // Remaining arguments are named: prefix:, postfix:, transpiler:, finalizer:
            for (int i = 1; i < args.Count; i++)
            {
                var arg = args[i];
                var paramName = arg.NameColon?.Name.Identifier.Text;
                
                if (paramName == null) continue;
                
                PatchType? patchType = paramName.ToLower() switch
                {
                    "prefix" => PatchType.Prefix,
                    "postfix" => PatchType.Postfix,
                    "transpiler" => PatchType.Transpiler,
                    "finalizer" => PatchType.Finalizer,
                    _ => null
                };
                
                if (patchType != null)
                {
                    // Extract the HarmonyMethod info
                    var harmonyMethodInfo = ParseHarmonyMethod(arg.Expression);
                    if (harmonyMethodInfo != null)
                    {
                        var specificPatch = patch.Clone();
                        specificPatch.Type = patchType.Value;
                        specificPatch.PatchMethod = harmonyMethodInfo.Value.MethodName;
                        
                        if (!string.IsNullOrEmpty(specificPatch.TargetType) && 
                            !string.IsNullOrEmpty(specificPatch.TargetMethod))
                        {
                            _patches.Add(specificPatch);
                            
                            if (_verbose)
                                Console.WriteLine($"    [Runtime] {specificPatch.Type}: {specificPatch.TargetType}.{specificPatch.TargetMethod}");
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Parse AccessTools.Method() to get target type and method.
    /// </summary>
    private void ParseAccessToolsMethod(ExpressionSyntax expression, HarmonyPatchInfo patch)
    {
        // Direct variable reference - try to find the declaration
        if (expression is IdentifierNameSyntax identifier)
        {
            // Can't resolve without semantic model, but we can note it
            patch.TargetMethod = $"[unresolved: {identifier.Identifier.Text}]";
            return;
        }
        
        // AccessTools.Method(typeof(X), "Y") or AccessTools.Method(typeof(X), "Y", new[] { ... })
        if (expression is InvocationExpressionSyntax invocation)
        {
            var methodAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (methodAccess == null) return;
            
            var accessToolsMethod = methodAccess.Name.Identifier.Text;
            if (accessToolsMethod != "Method" && accessToolsMethod != "PropertyGetter" && 
                accessToolsMethod != "PropertySetter" && accessToolsMethod != "Constructor")
                return;
            
            var args = invocation.ArgumentList.Arguments.ToList();
            
            foreach (var arg in args)
            {
                var argExpr = arg.Expression;
                
                // typeof(TargetClass)
                if (argExpr is TypeOfExpressionSyntax typeOf)
                {
                    patch.TargetType = typeOf.Type.ToString();
                }
                // "MethodName"
                else if (argExpr is LiteralExpressionSyntax literal &&
                         literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    if (string.IsNullOrEmpty(patch.TargetMethod))
                        patch.TargetMethod = literal.Token.ValueText;
                }
                // nameof(...)
                else if (argExpr is InvocationExpressionSyntax nameofInvoke &&
                         nameofInvoke.Expression.ToString() == "nameof")
                {
                    var nameofArg = nameofInvoke.ArgumentList.Arguments.FirstOrDefault();
                    if (nameofArg != null)
                    {
                        var nameText = nameofArg.Expression.ToString();
                        patch.TargetMethod = nameText.Contains('.') 
                            ? nameText.Split('.').Last() 
                            : nameText;
                    }
                }
                // new[] { typeof(int), typeof(string) } or new Type[] { ... }
                else if (argExpr is ArrayCreationExpressionSyntax arrayCreation)
                {
                    patch.TargetArgumentTypes = ExtractTypeArray(arrayCreation);
                }
                else if (argExpr is ImplicitArrayCreationExpressionSyntax implicitArray)
                {
                    patch.TargetArgumentTypes = ExtractTypeArrayFromInitializer(implicitArray.Initializer);
                }
            }
        }
    }
    
    /// <summary>
    /// Parse new HarmonyMethod(typeof(X), "Y") to get patch method info.
    /// </summary>
    private (string TypeName, string MethodName)? ParseHarmonyMethod(ExpressionSyntax expression)
    {
        // new HarmonyMethod(typeof(PatchClass), "PatchMethod")
        if (expression is ObjectCreationExpressionSyntax creation)
        {
            var args = creation.ArgumentList?.Arguments.ToList();
            if (args == null || args.Count < 2) return null;
            
            string? typeName = null;
            string? methodName = null;
            
            foreach (var arg in args)
            {
                if (arg.Expression is TypeOfExpressionSyntax typeOf)
                {
                    typeName = typeOf.Type.ToString();
                }
                else if (arg.Expression is LiteralExpressionSyntax literal &&
                         literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    methodName = literal.Token.ValueText;
                }
                else if (arg.Expression is InvocationExpressionSyntax nameofInvoke &&
                         nameofInvoke.Expression.ToString() == "nameof")
                {
                    var nameofArg = nameofInvoke.ArgumentList.Arguments.FirstOrDefault();
                    if (nameofArg != null)
                    {
                        var nameText = nameofArg.Expression.ToString();
                        methodName = nameText.Contains('.') 
                            ? nameText.Split('.').Last() 
                            : nameText;
                    }
                }
            }
            
            if (typeName != null && methodName != null)
                return (typeName, methodName);
        }
        
        // Could also be: new HarmonyMethod(AccessTools.Method(...))
        // For now, return null for these complex cases
        
        return null;
    }
    
    /// <summary>
    /// Determine patch type from method name or attributes.
    /// </summary>
    private PatchType? DeterminePatchTypeFromMethodOrAttributes(MethodDeclarationSyntax method)
    {
        var methodName = method.Identifier.Text;
        
        // Check attributes first
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();
                if (attrName.Contains("HarmonyPrefix")) return PatchType.Prefix;
                if (attrName.Contains("HarmonyPostfix")) return PatchType.Postfix;
                if (attrName.Contains("HarmonyTranspiler")) return PatchType.Transpiler;
                if (attrName.Contains("HarmonyFinalizer")) return PatchType.Finalizer;
            }
        }
        
        // Check method name convention
        if (methodName.Equals("Prefix", StringComparison.OrdinalIgnoreCase)) return PatchType.Prefix;
        if (methodName.Equals("Postfix", StringComparison.OrdinalIgnoreCase)) return PatchType.Postfix;
        if (methodName.Equals("Transpiler", StringComparison.OrdinalIgnoreCase)) return PatchType.Transpiler;
        if (methodName.Equals("Finalizer", StringComparison.OrdinalIgnoreCase)) return PatchType.Finalizer;
        
        return null;
    }
    
    private string[] ExtractTypeArray(ArrayCreationExpressionSyntax arrayCreation)
    {
        if (arrayCreation.Initializer == null) return Array.Empty<string>();
        return ExtractTypeArrayFromInitializer(arrayCreation.Initializer);
    }
    
    private string[] ExtractTypeArrayFromInitializer(InitializerExpressionSyntax initializer)
    {
        var types = new List<string>();
        foreach (var expr in initializer.Expressions)
        {
            if (expr is TypeOfExpressionSyntax typeOf)
            {
                types.Add(typeOf.Type.ToString());
            }
        }
        return types.ToArray();
    }
    
    private string? GetContainingTypeName(SyntaxNode node)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return typeDecl?.Identifier.Text;
    }
    
    /// <summary>
    /// Print discovery summary.
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine($"  Harmony patches discovered: {PatchCount}");
        Console.WriteLine($"    Attribute-based: {AttributePatchCount}");
        Console.WriteLine($"    Runtime patches: {RuntimePatchCount}");
        
        if (_verbose && _patches.Any())
        {
            var byType = _patches.GroupBy(p => p.Type);
            foreach (var group in byType)
            {
                Console.WriteLine($"    {group.Key}: {group.Count()}");
            }
        }
    }
}

/// <summary>
/// Information about a discovered Harmony patch.
/// </summary>
public class HarmonyPatchInfo
{
    public string PatchClass { get; set; } = "";
    public string PatchMethod { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetMethod { get; set; } = "";
    public string[]? TargetArgumentTypes { get; set; }
    public string? MethodType { get; set; }  // Getter, Setter, Constructor, etc.
    public PatchType Type { get; set; }
    public int Priority { get; set; } = 400;
    public bool IsRuntimePatch { get; set; }
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    
    // For analysis
    public int? GameMethodId { get; set; }
    
    public HarmonyPatchInfo Clone() => new HarmonyPatchInfo
    {
        PatchClass = PatchClass,
        PatchMethod = PatchMethod,
        TargetType = TargetType,
        TargetMethod = TargetMethod,
        TargetArgumentTypes = TargetArgumentTypes?.ToArray(),
        MethodType = MethodType,
        Type = Type,
        Priority = Priority,
        IsRuntimePatch = IsRuntimePatch,
        FilePath = FilePath,
        LineNumber = LineNumber,
        GameMethodId = GameMethodId
    };
    
    public string GetSignature()
    {
        var args = TargetArgumentTypes != null && TargetArgumentTypes.Length > 0
            ? $"({string.Join(", ", TargetArgumentTypes)})"
            : "";
        return $"{TargetType}.{TargetMethod}{args}";
    }
}

public enum PatchType
{
    Prefix,
    Postfix,
    Transpiler,
    Finalizer
}
