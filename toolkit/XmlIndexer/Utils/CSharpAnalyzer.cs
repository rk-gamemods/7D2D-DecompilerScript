using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using XmlIndexer.Models;

namespace XmlIndexer.Utils;

/// <summary>
/// Consolidated C# analysis utilities for scanning mod DLLs.
/// Provides Harmony patch detection, XML dependency patterns, and code extraction.
/// </summary>
public static class CSharpAnalyzer
{
    // Cache for decompiled mod code to avoid repeated decompilation
    private static readonly Dictionary<string, string> _decompileCache = new();
    private static string? _decompiledModsDir;

    // Unity magic methods that should not be flagged as dead code
    public static readonly HashSet<string> UnityMagicMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Awake", "Start", "Update", "FixedUpdate", "LateUpdate",
        "OnEnable", "OnDisable", "OnDestroy",
        "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay",
        "OnTriggerEnter2D", "OnTriggerExit2D", "OnTriggerStay2D",
        "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay",
        "OnCollisionEnter2D", "OnCollisionExit2D", "OnCollisionStay2D",
        "OnGUI", "OnDrawGizmos", "OnDrawGizmosSelected",
        "OnApplicationFocus", "OnApplicationPause", "OnApplicationQuit",
        "OnBecameVisible", "OnBecameInvisible",
        "OnMouseDown", "OnMouseUp", "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseDrag",
        "OnBeforeSerialize", "OnAfterDeserialize",
        "OnValidate", "Reset", "OnAnimatorMove", "OnAnimatorIK",
        "OnRenderObject", "OnWillRenderObject", "OnPreRender", "OnPostRender",
        "OnRenderImage", "OnParticleCollision", "OnParticleTrigger"
    };

    /// <summary>
    /// Returns the comprehensive set of regex patterns for detecting XML entity dependencies in C# code.
    /// This is the authoritative source (28+ patterns) - use this instead of duplicating patterns.
    /// </summary>
    public static (string Pattern, string Type, int NameGroup)[] GetXmlDependencyPatterns()
    {
        return new (string Pattern, string Type, int NameGroup)[]
        {
            // Item lookups
            (@"ItemClass\.GetItem\s*\(\s*""([^""]+)""\s*[,\)]", "item", 1),
            (@"ItemClass\.GetItemClass\s*\(\s*""([^""]+)""\s*\)", "item", 1),
            (@"new\s+ItemValue\s*\(\s*ItemClass\.GetItem\s*\(\s*""([^""]+)""\s*\)", "item", 1),

            // Block lookups
            (@"Block\.GetBlockByName\s*\(\s*""([^""]+)""\s*\)", "block", 1),
            (@"Block\.GetBlockValue\s*\(\s*""([^""]+)""\s*\)", "block", 1),
            (@"ItemClass\.GetItem\s*\(\s*""([^""]+)""\s*\)\.Block", "block", 1),

            // Entity lookups
            (@"EntityClass\.FromString\s*\(\s*""([^""]+)""\s*\)", "entity_class", 1),
            (@"EntityFactory\.CreateEntity\s*\([^,]*,\s*""([^""]+)""\s*\)", "entity_class", 1),

            // Buff lookups
            (@"BuffManager\.GetBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"BuffClass\.GetBuffClass\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"\.AddBuff\s*\(\s*""([^""]+)""\s*[\),]", "buff", 1),
            (@"\.RemoveBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"\.HasBuff\s*\(\s*""([^""]+)""\s*\)", "buff", 1),
            (@"BuffManager\.Server_AddBuff\s*\([^,]*,\s*""([^""]+)""\s*\)", "buff", 1),

            // Recipe lookups
            (@"CraftingManager\.GetRecipe\s*\(\s*""([^""]+)""\s*\)", "recipe", 1),
            (@"Recipe\.GetRecipe\s*\(\s*""([^""]+)""\s*\)", "recipe", 1),

            // Sound lookups - various patterns
            (@"Manager\.Play\s*\([^,]*,\s*""([^""]+)""\s*[\),]", "sound", 1),
            (@"Manager\.BroadcastPlay\s*\([^,]*,\s*""([^""]+)""\s*[\),]", "sound", 1),
            (@"Audio\.Manager\.Play\s*\(\s*""([^""]+)""\s*[\),]", "sound", 1),
            (@"Manager\.PlayInsidePlayerHead\s*\(\s*""([^""]+)""\s*[,\)]", "sound", 1),
            (@"PlayInsidePlayerHead\s*\(\s*""([^""]+)""\s*[,\)]", "sound", 1),
            // Constant strings that look like sound names (fallbacks)
            (@"=\s*""([\w\-]+destroy)""\s*;", "sound", 1),
            (@"=\s*""([\w\-]+shatter)""\s*;", "sound", 1),
            (@"=\s*""(sound_[\w\-]+)""\s*;", "sound", 1),

            // Quest lookups
            (@"QuestClass\.GetQuest\s*\(\s*""([^""]+)""\s*\)", "quest", 1),

            // Loot lookups
            (@"LootContainer\.GetLootContainer\s*\(\s*""([^""]+)""\s*\)", "lootcontainer", 1),

            // Progression lookups
            (@"Progression\.GetProgressionClass\s*\(\s*""([^""]+)""\s*\)", "progression", 1),

            // Trader lookups
            (@"TraderInfo\.GetTraderInfo\s*\(\s*""([^""]+)""\s*\)", "trader_info", 1),

            // Workstation/Action lookups
            (@"Workstation\s*=\s*""([^""]+)""", "workstation", 1),

            // Localization key lookups (possible item/block name refs)
            (@"Localization\.Get\s*\(\s*""([^""]+)""\s*\)", "localization", 1),

            // Inheritance patterns
            (@":\s*(ItemAction\w*)\b", "extends_itemaction", 1),
            (@":\s*(Block\w*)\b", "extends_block", 1),
            (@":\s*(EntityAlive|Entity\w*)\b", "extends_entity", 1),
            (@":\s*(MinEventAction\w*)\b", "extends_mineventaction", 1),
            (@":\s*(IModApi)\b", "implements_imodapi", 1),
        };
    }

    /// <summary>
    /// Scans a mod directory for C# dependencies by decompiling DLLs and analyzing the source.
    /// </summary>
    public static List<CSharpDependency> ScanCSharpDependencies(string modDir, string modName)
    {
        var deps = new List<CSharpDependency>();
        var patterns = GetXmlDependencyPatterns();

        // Find DLLs in the mod folder (skip framework DLLs)
        var modDlls = Directory.GetFiles(modDir, "*.dll", SearchOption.AllDirectories)
            .Where(dll => !Path.GetFileName(dll).StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Equals("Mono.Cecil.dll", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !Path.GetFileName(dll).Contains("System."))
            .Where(dll => !Path.GetFileName(dll).Contains("Microsoft."))
            .ToList();

        if (modDlls.Count == 0)
            return deps;

        foreach (var dllPath in modDlls)
        {
            var csFiles = DecompileModDll(dllPath, modName);
            if (csFiles.Count == 0) continue;

            foreach (var csFile in csFiles)
            {
                try
                {
                    var content = File.ReadAllText(csFile);
                    var lines = content.Split('\n');
                    var fileName = Path.GetFileName(csFile);

                    // Scan for non-Harmony patterns (line-by-line)
                    foreach (var (pattern, type, nameGroup) in patterns)
                    {
                        var regex = new Regex(pattern, RegexOptions.Compiled);

                        for (int i = 0; i < lines.Length; i++)
                        {
                            var matches = regex.Matches(lines[i]);
                            foreach (Match match in matches)
                            {
                                var name = match.Groups[nameGroup].Value;
                                if (!string.IsNullOrEmpty(name) && !name.Contains("{") && !name.Contains("+"))
                                {
                                    deps.Add(new CSharpDependency(modName, type, name, fileName, i + 1, match.Value.Trim()));
                                }
                            }
                        }
                    }

                    // Scan for Harmony patches with proper class structure parsing
                    var harmonyPatches = ScanHarmonyPatches(content, modName, fileName);
                    deps.AddRange(harmonyPatches);
                }
                catch { /* Skip unreadable files */ }
            }
        }

        return deps;
    }

    /// <summary>
    /// Properly parses C# source to extract Harmony patch information.
    /// Finds [HarmonyPatch] class declarations and identifies which methods (Prefix/Postfix/Transpiler)
    /// are actually inside each class by tracking brace depth.
    /// </summary>
    public static List<CSharpDependency> ScanHarmonyPatches(string content, string modName, string fileName)
    {
        var deps = new List<CSharpDependency>();

        // Pattern to find [HarmonyPatch(typeof(ClassName), "MethodName")] followed by a class declaration
        var patchClassPattern = new Regex(
            @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)(?:\s*,\s*""([^""]+)"")?\s*\)\]" +
            @"[^\{]*?(?:class|struct)\s+(\w+)[^\{]*\{",
            RegexOptions.Singleline);

        // Patterns to identify patch method types
        var prefixPatterns = new[] {
            new Regex(@"\[HarmonyPrefix\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+(?:bool|void)\s+Prefix\s*\(")
        };
        var postfixPatterns = new[] {
            new Regex(@"\[HarmonyPostfix\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+void\s+Postfix\s*\(")
        };
        var transpilerPatterns = new[] {
            new Regex(@"\[HarmonyTranspiler\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+IEnumerable<CodeInstruction>\s+Transpiler\s*\(")
        };

        var matches = patchClassPattern.Matches(content);
        foreach (Match match in matches)
        {
            var targetClass = match.Groups[1].Value;
            var targetMethod = match.Groups[2].Success ? match.Groups[2].Value : "";
            var patchClassName = match.Groups[3].Value;
            var classStartIndex = match.Index + match.Length - 1; // Position of opening brace

            // Find the closing brace of this class by tracking brace depth
            var classBody = ExtractClassBody(content, classStartIndex);
            if (string.IsNullOrEmpty(classBody)) continue;

            // Calculate line number for reporting
            int lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            // Check which patch types are present in this class body
            var patchTypes = new List<string>();

            if (prefixPatterns.Any(p => p.IsMatch(classBody)))
                patchTypes.Add("Prefix");
            if (postfixPatterns.Any(p => p.IsMatch(classBody)))
                patchTypes.Add("Postfix");
            if (transpilerPatterns.Any(p => p.IsMatch(classBody)))
                patchTypes.Add("Transpiler");

            // If no specific patch type found, default to "Patch"
            if (patchTypes.Count == 0)
                patchTypes.Add("Patch");

            // Create a dependency record for each patch type found
            foreach (var patchType in patchTypes)
            {
                var depType = patchType.ToLower() switch
                {
                    "prefix" => "harmony_prefix",
                    "postfix" => "harmony_postfix",
                    "transpiler" => "harmony_transpiler",
                    _ => "harmony_patch"
                };

                // Store as: harmony_patch with combined info: TargetClass.TargetMethod
                var patchTarget = string.IsNullOrEmpty(targetMethod)
                    ? targetClass
                    : $"{targetClass}.{targetMethod}";

                // Extract the code snippet for this patch method
                var codeSnippet = ExtractMethodCode(classBody, patchType);

                deps.Add(new CSharpDependency(modName, depType, patchTarget, fileName, lineNumber,
                    $"[HarmonyPatch] {patchType} on {patchTarget}", codeSnippet));
            }

            // NOTE: We no longer store redundant harmony_class and harmony_method entries.
            // The harmony_prefix/postfix/transpiler/patch entries contain all necessary info.
        }

        return deps;
    }

    /// <summary>
    /// Enhanced Harmony patch scanning that extracts detailed patch metadata for conflict detection.
    /// Returns HarmonyPatchInfo objects with priority, behavioral flags, and guard conditions.
    /// </summary>
    public static List<HarmonyPatchInfo> ScanHarmonyPatchesDetailed(string content, long modId, string fileName)
    {
        var patches = new List<HarmonyPatchInfo>();
        long patchId = 0;

        // Pattern to find [HarmonyPatch] class declarations with various attribute formats
        // CRITICAL: Use [\s\S]*? instead of [^\{]*? to handle preprocessor directives between attributes and class
        var patchClassPatterns = new[]
        {
            // Pattern 1: typeof(Class), "Method" format - method specified in class attribute
            new Regex(
                @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)\s*,\s*""([^""]+)""\s*\)\]" +
                @"[\s\S]*?(?:class|struct)\s+(\w+)[^\{]*\{",
                RegexOptions.Singleline),
            // Pattern 2: typeof(Class), nameof(Method) format at class level
            new Regex(
                @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)\s*,\s*nameof\s*\(\s*[\w\.]*\.?(\w+)\s*\)\s*\)\]" +
                @"[\s\S]*?(?:class|struct)\s+(\w+)[^\{]*\{",
                RegexOptions.Singleline),
            // Pattern 3: Multiple adjacent [HarmonyPatch] attributes format (class then method string)
            new Regex(
                @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)\s*\)\]\s*" +
                @"\[HarmonyPatch\s*\(\s*""([^""]+)""\s*\)\]" +
                @"[\s\S]*?(?:class|struct)\s+(\w+)[^\{]*\{",
                RegexOptions.Singleline),
            // Pattern 4: Class-only attribute - method name in method attributes (common pattern!)
            // This captures classes with just [HarmonyPatch(typeof(Class))] - method will be extracted from body
            new Regex(
                @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)\s*\)\]\s*" +
                @"(?!\s*\[HarmonyPatch)" + // NOT followed by another HarmonyPatch (to avoid double-matching)
                @"[\s\S]*?(?:public|private|internal)?\s*(?:static\s+)?(?:class|struct)\s+(\w+)[^\{]*\{",
                RegexOptions.Singleline),
            // Pattern 5: [HarmonyPatchAll] or [HarmonyTargetMethods] pattern - method determined dynamically
            new Regex(
                @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)\s*\)\]\s*" +
                @"(?:\[HarmonyTargetMethods?\]|\[HarmonyPatchAll\])" +
                @"[\s\S]*?(?:class|struct)\s+(\w+)[^\{]*\{",
                RegexOptions.Singleline)
        };

        // Patterns to extract method names from [HarmonyPatch("methodName")] or [HarmonyPatch(nameof(Method))] inside class body
        var methodPatchPatterns = new[]
        {
            // "methodName" string literal format
            new Regex(@"\[HarmonyPatch\s*\(\s*""([^""]+)""\s*\)\]", RegexOptions.Multiline),
            // nameof(Method) or nameof(Class.Method) format - method-level attribute
            new Regex(@"\[HarmonyPatch\s*\(\s*nameof\s*\(\s*(?:[\w\.]+\.)?(\w+)\s*\)\s*\)\]", RegexOptions.Multiline)
        };
        // Legacy single pattern reference (for compatibility - uses first pattern)
        var methodPatchPattern = methodPatchPatterns[0];

        // Pattern for Harmony attributes in class body
        var priorityPattern = new Regex(@"\[HarmonyPriority\s*\(\s*(\d+)\s*\)\]");
        var beforePattern = new Regex(@"\[HarmonyBefore\s*\((.*?)\)\]", RegexOptions.Singleline);
        var afterPattern = new Regex(@"\[HarmonyAfter\s*\((.*?)\)\]", RegexOptions.Singleline);
        var methodTypePattern = new Regex(@"\[HarmonyPatch\s*\(\s*MethodType\.(\w+)\s*\)\]");
        // Support both new Type[] {...} syntax and collection expression [typeof(...)] syntax
        var argTypesPatterns = new[]
        {
            // Traditional: new Type[] { typeof(A), typeof(B) }
            new Regex(@"\[HarmonyPatch\s*\(\s*new\s*Type\s*\[\]\s*\{([^\}]+)\}\s*\)\]"),
            // Collection expression: [typeof(A), typeof(B)]
            new Regex(@"\[HarmonyPatch\s*\(\s*\[\s*(typeof\s*\([^\]]+)\]\s*\)\]")
        };
        var argTypesPattern = argTypesPatterns[0]; // Legacy reference

        // Behavioral detection patterns
        var returnsBoolPattern = new Regex(@"static\s+bool\s+Prefix\s*\(");
        var modifiesResultPattern = new Regex(@"(__result\s*=|ref\s+\w+\s+__result)");
        var modifiesStatePattern = new Regex(@"(__state\s*=|ref\s+\w+\s+__state|ref\s+\w+\s+\w+[^,\)]*,)");
        var guardedTryCatchPattern = new Regex(@"try\s*\{[^}]*(?:Prefix|Postfix|Transpiler)[^}]*\}\s*catch");
        var guardedAccessToolsPattern = new Regex(@"AccessTools\.(Method|DeclaredMethod|Property|Field)\s*\([^)]*\)\s*!=\s*null");

        foreach (var pattern in patchClassPatterns)
        {
            var matches = pattern.Matches(content);
            foreach (Match match in matches)
            {
                var targetClass = match.Groups[1].Value;
                // Group 2 may be method name or class name depending on pattern
                var targetMethod = match.Groups.Count > 3 && match.Groups[2].Success ? match.Groups[2].Value : "";
                var patchClassName = match.Groups.Count > 3 ? match.Groups[3].Value : match.Groups[2].Value;
                var classStartIndex = match.Index + match.Length - 1;

                // Extract the full class body
                var classBody = ExtractClassBody(content, classStartIndex);
                if (string.IsNullOrEmpty(classBody)) continue;

                // Calculate line number
                int lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

                // Extract class-level attributes from the matched region
                var classRegion = content.Substring(Math.Max(0, match.Index - 500), Math.Min(500 + match.Length, content.Length - match.Index + 500));

                // Extract priority
                int priority = 400; // Default Harmony priority
                var priorityMatch = priorityPattern.Match(classRegion);
                if (priorityMatch.Success)
                    int.TryParse(priorityMatch.Groups[1].Value, out priority);

                // Extract before/after
                string? beforeIds = null;
                string? afterIds = null;
                var beforeMatch = beforePattern.Match(classRegion);
                if (beforeMatch.Success)
                    beforeIds = ExtractStringArrayFromAttribute(beforeMatch.Groups[1].Value);
                var afterMatch = afterPattern.Match(classRegion);
                if (afterMatch.Success)
                    afterIds = ExtractStringArrayFromAttribute(afterMatch.Groups[1].Value);

                // Extract method type (MethodType.Getter, MethodType.Setter, MethodType.Constructor)
                string? memberKind = null;
                var methodTypeMatch = methodTypePattern.Match(classRegion);
                if (methodTypeMatch.Success)
                    memberKind = methodTypeMatch.Groups[1].Value.ToLower();

                // Extract argument types for overload matching (try both syntax patterns)
                string? argTypes = null;
                foreach (var argPattern in argTypesPatterns)
                {
                    var argTypesMatch = argPattern.Match(classRegion);
                    if (argTypesMatch.Success)
                    {
                        argTypes = ExtractTypeArrayFromAttribute(argTypesMatch.Groups[1].Value);
                        break;
                    }
                }

                // Detect if class body contains guard patterns
                bool isGuarded = guardedTryCatchPattern.IsMatch(classBody) || guardedAccessToolsPattern.IsMatch(classBody);
                string? guardCondition = null;
                if (isGuarded)
                {
                    var accessGuard = guardedAccessToolsPattern.Match(classBody);
                    if (accessGuard.Success)
                        guardCondition = accessGuard.Value;
                }

                // If no method name from class attribute, try to extract from class body
                var methodNames = new List<string>();
                if (string.IsNullOrEmpty(targetMethod))
                {
                    // Look for [HarmonyPatch("methodName")] or [HarmonyPatch(nameof(Method))] inside the class body
                    foreach (var methodPattern in methodPatchPatterns)
                    {
                        var methodMatches = methodPattern.Matches(classBody);
                        foreach (Match methodMatch in methodMatches)
                        {
                            var methodName = methodMatch.Groups[1].Value;
                            if (!methodNames.Contains(methodName))
                                methodNames.Add(methodName);
                        }
                    }
                    
                    // Also check for [HarmonyTargetMethods] which returns methods dynamically via IEnumerable<MethodBase>
                    if (classBody.Contains("HarmonyTargetMethod") || classBody.Contains("TargetMethods"))
                    {
                        // Try to extract AccessTools calls within TargetMethods method
                        var accessToolsPattern = new Regex(@"AccessTools\.(Method|DeclaredMethod)\s*\(\s*typeof\s*\(\s*[\w\.]+\s*\)\s*,\s*(?:""([^""]+)""|nameof\s*\([\w\.]*\.?(\w+)\))");
                        var accessMatches = accessToolsPattern.Matches(classBody);
                        foreach (Match am in accessMatches)
                        {
                            var methodName = !string.IsNullOrEmpty(am.Groups[2].Value) ? am.Groups[2].Value : am.Groups[3].Value;
                            if (!string.IsNullOrEmpty(methodName) && !methodNames.Contains(methodName))
                                methodNames.Add(methodName);
                        }
                        
                        // If still no methods found but has TargetMethods, mark as dynamic
                        if (methodNames.Count == 0)
                            methodNames.Add("[Dynamic]");
                    }
                }
                else
                {
                    methodNames.Add(targetMethod);
                }

                // If still no methods found, use "Unknown" as placeholder
                if (methodNames.Count == 0)
                    methodNames.Add("Unknown");

                // Check each patch type present in the class
                var patchTypes = DetectPatchTypes(classBody);

                foreach (var methodName in methodNames)
                {
                    foreach (var patchType in patchTypes)
                    {
                        // Extract the specific method code
                        var methodCode = ExtractMethodCode(classBody, patchType);

                        // Detect behavioral flags from the method code
                        bool returnsBool = false;
                        bool modifiesResult = false;
                        bool modifiesState = false;

                        if (methodCode != null)
                        {
                            if (patchType == "Prefix")
                                returnsBool = returnsBoolPattern.IsMatch(methodCode);
                            modifiesResult = modifiesResultPattern.IsMatch(methodCode);
                            modifiesState = modifiesStatePattern.IsMatch(methodCode);
                        }

                        // Extract parameter signature from patch method
                        string? paramSig = ExtractPatchMethodSignature(classBody, patchType);

                        patches.Add(new HarmonyPatchInfo(
                            Id: patchId++,
                            ModId: modId,
                            PatchClass: patchClassName,
                            TargetClass: targetClass,
                            TargetMethod: methodName,
                            PatchType: patchType,
                            TargetMemberKind: memberKind,
                            TargetArgTypes: argTypes,
                            TargetDeclaringType: null, // Determined later via inheritance analysis
                            HarmonyPriority: priority,
                            HarmonyBefore: beforeIds,
                            HarmonyAfter: afterIds,
                            ReturnsBool: returnsBool,
                            ModifiesResult: modifiesResult,
                            ModifiesState: modifiesState,
                            IsGuarded: isGuarded,
                            GuardCondition: guardCondition,
                            IsDynamic: false,
                            ParameterSignature: paramSig,
                            CodeSnippet: TruncateCodeSnippet(methodCode, 10),
                            SourceFile: fileName,
                            LineNumber: lineNumber
                        ));
                    }
                }
            }
        }

        // Scan for dynamic patches (non-attribute based)
        var dynamicPatches = ScanDynamicPatches(content, modId, fileName, ref patchId);
        patches.AddRange(dynamicPatches);

        return patches;
    }

    /// <summary>
    /// Scans for dynamic Harmony patches applied via code rather than attributes.
    /// These are lower confidence but important to detect.
    /// </summary>
    private static List<HarmonyPatchInfo> ScanDynamicPatches(string content, long modId, string fileName, ref long patchId)
    {
        var patches = new List<HarmonyPatchInfo>();

        // Pattern: harmony.Patch(AccessTools.Method(typeof(Class), "Method"), ...)
        var dynamicPatchPattern = new Regex(
            @"\.Patch\s*\(\s*AccessTools\.(Method|DeclaredMethod)\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)\s*,\s*""([^""]+)""\s*\)",
            RegexOptions.Multiline);

        // Pattern: PatchAll() - bulk patching
        var patchAllPattern = new Regex(@"\.PatchAll\s*\(\s*(?:typeof\s*\(\s*([\w\.]+)\s*\)|Assembly\.GetExecutingAssembly\s*\(\s*\))\s*\)");

        // Dynamic individual patches
        var dynamicMatches = dynamicPatchPattern.Matches(content);
        foreach (Match match in dynamicMatches)
        {
            var targetClass = match.Groups[2].Value;
            var targetMethod = match.Groups[3].Value;
            int lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;

            // Try to determine patch type from surrounding context
            var contextStart = Math.Max(0, match.Index - 200);
            var contextEnd = Math.Min(content.Length, match.Index + match.Length + 200);
            var context = content.Substring(contextStart, contextEnd - contextStart);

            string patchType = "Patch"; // Default
            if (context.Contains("prefix:") || context.Contains("Prefix"))
                patchType = "Prefix";
            else if (context.Contains("postfix:") || context.Contains("Postfix"))
                patchType = "Postfix";
            else if (context.Contains("transpiler:") || context.Contains("Transpiler"))
                patchType = "Transpiler";

            patches.Add(new HarmonyPatchInfo(
                Id: patchId++,
                ModId: modId,
                PatchClass: "DynamicPatch",
                TargetClass: targetClass,
                TargetMethod: targetMethod,
                PatchType: patchType,
                IsDynamic: true,
                CodeSnippet: TruncateCodeSnippet(match.Value, 5),
                SourceFile: fileName,
                LineNumber: lineNumber
            ));
        }

        return patches;
    }

    /// <summary>
    /// Detects which patch types (Prefix, Postfix, Transpiler, Finalizer) are present in a class body.
    /// </summary>
    private static List<string> DetectPatchTypes(string classBody)
    {
        var patchTypes = new List<string>();

        var prefixPatterns = new[]
        {
            new Regex(@"\[HarmonyPrefix\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+(?:bool|void)\s+Prefix\s*\(")
        };
        var postfixPatterns = new[]
        {
            new Regex(@"\[HarmonyPostfix\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+void\s+Postfix\s*\(")
        };
        var transpilerPatterns = new[]
        {
            new Regex(@"\[HarmonyTranspiler\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+IEnumerable<CodeInstruction>\s+Transpiler\s*\(")
        };
        var finalizerPatterns = new[]
        {
            new Regex(@"\[HarmonyFinalizer\]"),
            new Regex(@"(?:public\s+|private\s+)?static\s+(?:void|Exception)\s+Finalizer\s*\(")
        };

        if (prefixPatterns.Any(p => p.IsMatch(classBody)))
            patchTypes.Add("Prefix");
        if (postfixPatterns.Any(p => p.IsMatch(classBody)))
            patchTypes.Add("Postfix");
        if (transpilerPatterns.Any(p => p.IsMatch(classBody)))
            patchTypes.Add("Transpiler");
        if (finalizerPatterns.Any(p => p.IsMatch(classBody)))
            patchTypes.Add("Finalizer");

        if (patchTypes.Count == 0)
            patchTypes.Add("Patch");

        return patchTypes;
    }

    /// <summary>
    /// Extracts string values from Harmony attribute arguments like [HarmonyBefore("id1", "id2")].
    /// Returns a JSON array string.
    /// </summary>
    private static string? ExtractStringArrayFromAttribute(string attrContent)
    {
        var stringPattern = new Regex(@"""([^""]+)""");
        var matches = stringPattern.Matches(attrContent);
        if (matches.Count == 0) return null;

        var values = matches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        return JsonSerializer.Serialize(values);
    }

    /// <summary>
    /// Extracts type names from Harmony argument type arrays.
    /// Returns a JSON array string.
    /// </summary>
    private static string? ExtractTypeArrayFromAttribute(string attrContent)
    {
        var typeofPattern = new Regex(@"typeof\s*\(\s*([\w\.]+)\s*\)");
        var matches = typeofPattern.Matches(attrContent);
        if (matches.Count == 0) return null;

        var types = matches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        return JsonSerializer.Serialize(types);
    }

    /// <summary>
    /// Extracts the parameter signature from a Harmony patch method.
    /// </summary>
    private static string? ExtractPatchMethodSignature(string classBody, string patchType)
    {
        var methodPattern = patchType switch
        {
            "Prefix" => new Regex(@"static\s+(?:bool|void)\s+Prefix\s*\(([^\)]*)\)"),
            "Postfix" => new Regex(@"static\s+void\s+Postfix\s*\(([^\)]*)\)"),
            "Transpiler" => new Regex(@"static\s+IEnumerable<CodeInstruction>\s+Transpiler\s*\(([^\)]*)\)"),
            "Finalizer" => new Regex(@"static\s+(?:void|Exception)\s+Finalizer\s*\(([^\)]*)\)"),
            _ => null
        };

        if (methodPattern == null) return null;

        var match = methodPattern.Match(classBody);
        if (!match.Success) return null;

        var rawParams = match.Groups[1].Value;
        // Clean up and normalize the parameter signature
        var cleaned = Regex.Replace(rawParams, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Truncates a code snippet to a maximum number of lines for storage.
    /// </summary>
    private static string? TruncateCodeSnippet(string? code, int maxLines)
    {
        if (string.IsNullOrEmpty(code)) return null;

        var lines = code.Split('\n');
        if (lines.Length <= maxLines)
            return code.Trim();

        return string.Join("\n", lines.Take(maxLines)) + "\n// ... (truncated)";
    }

    /// <summary>
    /// Extracts the body of a class/struct starting from the opening brace.
    /// Properly handles nested braces to find the matching closing brace.
    /// </summary>
    public static string ExtractClassBody(string content, int openBraceIndex)
    {
        if (openBraceIndex >= content.Length || content[openBraceIndex] != '{')
            return "";

        int depth = 1;
        int i = openBraceIndex + 1;

        while (i < content.Length && depth > 0)
        {
            char c = content[i];

            // Skip string literals
            if (c == '"')
            {
                i++;
                while (i < content.Length && content[i] != '"')
                {
                    if (content[i] == '\\') i++; // Skip escaped chars
                    i++;
                }
            }
            // Skip char literals
            else if (c == '\'')
            {
                i++;
                while (i < content.Length && content[i] != '\'')
                {
                    if (content[i] == '\\') i++;
                    i++;
                }
            }
            // Skip single-line comments
            else if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                while (i < content.Length && content[i] != '\n') i++;
            }
            // Skip multi-line comments
            else if (c == '/' && i + 1 < content.Length && content[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < content.Length && !(content[i] == '*' && content[i + 1] == '/')) i++;
                i++; // Skip the closing /
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }

            i++;
        }

        if (depth != 0) return ""; // Unbalanced braces

        return content.Substring(openBraceIndex + 1, i - openBraceIndex - 2);
    }

    /// <summary>
    /// Extracts the full method source code for a Harmony patch method (Prefix, Postfix, or Transpiler).
    /// </summary>
    /// <param name="classBody">The body of the class containing the method</param>
    /// <param name="methodType">The type of method to extract: "Prefix", "Postfix", or "Transpiler"</param>
    /// <returns>The method source code, or null if not found</returns>
    public static string? ExtractMethodCode(string classBody, string methodType)
    {
        // Pattern to find the method signature
        var methodPattern = methodType switch
        {
            "Prefix" => new Regex(@"(?:\[HarmonyPrefix\][^\{]*)?(?:public\s+|private\s+)?static\s+(?:bool|void)\s+Prefix\s*\([^\)]*\)\s*\{"),
            "Postfix" => new Regex(@"(?:\[HarmonyPostfix\][^\{]*)?(?:public\s+|private\s+)?static\s+void\s+Postfix\s*\([^\)]*\)\s*\{"),
            "Transpiler" => new Regex(@"(?:\[HarmonyTranspiler\][^\{]*)?(?:public\s+|private\s+)?static\s+IEnumerable<CodeInstruction>\s+Transpiler\s*\([^\)]*\)\s*\{"),
            _ => null
        };

        if (methodPattern == null) return null;

        var match = methodPattern.Match(classBody);
        if (!match.Success) return null;

        // Find where the method signature starts (including any attributes)
        var signatureStart = match.Index;

        // Look back for [HarmonyPrefix/Postfix/Transpiler] attribute if present
        var attrPattern = new Regex($@"\[Harmony{methodType}\]\s*$");
        var precedingText = classBody.Substring(0, signatureStart);
        var lines = precedingText.Split('\n');
        var lastLine = lines.Length > 0 ? lines[^1] : "";

        // Check if the attribute is on the previous line
        if (lines.Length > 1)
        {
            var prevLine = lines[^2].Trim();
            if (prevLine.Contains($"[Harmony{methodType}]"))
            {
                // Include the attribute line
                var attrIndex = precedingText.LastIndexOf($"[Harmony{methodType}]");
                if (attrIndex >= 0)
                    signatureStart = attrIndex;
            }
        }

        // Find the opening brace of the method
        var braceIndex = match.Index + match.Length - 1;

        // Extract the method body using brace tracking
        var methodBody = ExtractMethodBody(classBody, braceIndex);
        if (string.IsNullOrEmpty(methodBody)) return null;

        // Return the full method including signature and body
        var fullMethod = classBody.Substring(signatureStart, braceIndex - signatureStart + 1) + methodBody + "}";

        // Clean up the code (remove excessive whitespace at start of lines)
        return CleanupCodeSnippet(fullMethod);
    }

    /// <summary>
    /// Extracts a method body starting from the opening brace.
    /// </summary>
    private static string ExtractMethodBody(string content, int openBraceIndex)
    {
        if (openBraceIndex >= content.Length || content[openBraceIndex] != '{')
            return "";

        int depth = 1;
        int i = openBraceIndex + 1;

        while (i < content.Length && depth > 0)
        {
            char c = content[i];

            // Skip string literals
            if (c == '"')
            {
                i++;
                while (i < content.Length && content[i] != '"')
                {
                    if (content[i] == '\\') i++;
                    i++;
                }
            }
            // Skip char literals
            else if (c == '\'')
            {
                i++;
                while (i < content.Length && content[i] != '\'')
                {
                    if (content[i] == '\\') i++;
                    i++;
                }
            }
            // Skip comments
            else if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                while (i < content.Length && content[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < content.Length && content[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < content.Length && !(content[i] == '*' && content[i + 1] == '/')) i++;
                i++;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }

            i++;
        }

        if (depth != 0) return "";

        return content.Substring(openBraceIndex + 1, i - openBraceIndex - 2);
    }

    /// <summary>
    /// Cleans up a code snippet by normalizing indentation.
    /// </summary>
    private static string CleanupCodeSnippet(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;

        var lines = code.Split('\n');

        // Find minimum indentation (excluding empty lines)
        int minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int indent = line.TakeWhile(char.IsWhiteSpace).Count();
            if (indent < minIndent) minIndent = indent;
        }

        if (minIndent == int.MaxValue || minIndent == 0)
            return code.Trim();

        // Remove the common indentation
        var result = lines.Select(line =>
            string.IsNullOrWhiteSpace(line) ? "" :
            (line.Length > minIndent ? line.Substring(minIndent) : line.TrimStart()));

        return string.Join("\n", result).Trim();
    }

    /// <summary>
    /// Decompiles a mod DLL using ILSpycmd and returns the list of generated .cs files.
    /// Results are cached to avoid repeated decompilation.
    /// </summary>
    public static List<string> DecompileModDll(string dllPath, string modName)
    {
        var csFiles = new List<string>();

        // Check cache first
        if (_decompileCache.TryGetValue(dllPath, out var cachedDir))
        {
            if (Directory.Exists(cachedDir))
                return Directory.GetFiles(cachedDir, "*.cs", SearchOption.AllDirectories).ToList();
        }

        // Create temp directory for decompiled output
        if (_decompiledModsDir == null)
        {
            _decompiledModsDir = Path.Combine(Path.GetTempPath(), "XmlIndexer_ModDecompile_" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(_decompiledModsDir);

            // Register cleanup on exit
            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupDecompiledMods();
        }

        var dllName = Path.GetFileNameWithoutExtension(dllPath);
        var outputDir = Path.Combine(_decompiledModsDir, modName, dllName);

        try
        {
            Directory.CreateDirectory(outputDir);

            // Run ilspycmd to decompile
            var psi = new ProcessStartInfo
            {
                FileName = "ilspycmd",
                Arguments = $"\"{dllPath}\" -p -o \"{outputDir}\" -lv Latest",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                WarnIlSpyNotInstalled();
                return csFiles;
            }

            process.WaitForExit(30000); // 30 second timeout

            if (process.ExitCode == 0 && Directory.Exists(outputDir))
            {
                csFiles = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories).ToList();
                _decompileCache[dllPath] = outputDir;
            }
        }
        catch (Exception ex)
        {
            // ilspycmd not installed or other error
            if (ex.Message.Contains("not recognized") || ex.Message.Contains("not found"))
            {
                WarnIlSpyNotInstalled();
            }
        }

        return csFiles;
    }

    private static bool _warnedIlSpy = false;

    private static void WarnIlSpyNotInstalled()
    {
        if (!_warnedIlSpy)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("    Note: Install ilspycmd for C# mod analysis: dotnet tool install -g ilspycmd");
            Console.ResetColor();
            _warnedIlSpy = true;
        }
    }

    /// <summary>
    /// Cleans up temporary decompiled mod files.
    /// </summary>
    public static void CleanupDecompiledMods()
    {
        if (_decompiledModsDir != null && Directory.Exists(_decompiledModsDir))
        {
            try
            {
                Directory.Delete(_decompiledModsDir, true);
            }
            catch { /* Best effort cleanup */ }
        }
    }
}
