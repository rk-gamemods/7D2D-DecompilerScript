using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CallGraphExtractor;

/// <summary>
/// Automated QA Analyzer for 7D2D mods.
/// Discovers all functionality, analyzes interactions, traces flows, and identifies gaps.
/// </summary>
public class ModAnalyzer
{
    private readonly SqliteConnection _conn;
    private readonly bool _verbose;
    
    public ModAnalyzer(string databasePath, bool verbose = false)
    {
        _conn = new SqliteConnection($"Data Source={databasePath}");
        _conn.Open();
        _verbose = verbose;
    }
    
    /// <summary>
    /// Run complete QA analysis on a mod.
    /// </summary>
    public ModAnalysisResult Analyze(string modPath)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("              AUTOMATED MOD QA ANALYSIS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine($"\nMod Path: {modPath}\n");
        
        var result = new ModAnalysisResult
        {
            ModPath = modPath,
            AnalyzedAt = DateTime.UtcNow
        };
        
        // Phase 1: Discovery
        Console.WriteLine("Phase 1: DISCOVERY");
        Console.WriteLine("───────────────────────────────────────");
        DiscoverMod(modPath, result);
        
        // Phase 2: Interaction Analysis
        Console.WriteLine("\nPhase 2: INTERACTION ANALYSIS");
        Console.WriteLine("───────────────────────────────────────");
        AnalyzeInteractions(result);
        
        // Phase 3: Flow Tracing
        Console.WriteLine("\nPhase 3: FLOW TRACING");
        Console.WriteLine("───────────────────────────────────────");
        TraceFlows(result);
        
        // Phase 4: Gap Detection
        Console.WriteLine("\nPhase 4: GAP ANALYSIS");
        Console.WriteLine("───────────────────────────────────────");
        DetectGaps(result);
        
        return result;
    }
    
    /// <summary>
    /// Phase 1: Discover all mod functionality.
    /// </summary>
    private void DiscoverMod(string modPath, ModAnalysisResult result)
    {
        // Load mod metadata
        result.Metadata = LoadModMetadata(modPath);
        Console.WriteLine($"  Mod: {result.Metadata.Name} v{result.Metadata.Version}");
        
        // Find all C# source files
        var sourceFiles = FindSourceFiles(modPath);
        result.SourceFiles = sourceFiles;
        Console.WriteLine($"  Source files: {sourceFiles.Count}");
        
        // Discover Harmony patches
        Console.WriteLine("  Discovering Harmony patches...");
        var patchDiscovery = new HarmonyPatchDiscovery(_verbose);
        patchDiscovery.DiscoverPatches(sourceFiles);
        result.HarmonyPatches = patchDiscovery.Patches.ToList();
        Console.WriteLine($"    Found {result.HarmonyPatches.Count} patches ({patchDiscovery.AttributePatchCount} attribute, {patchDiscovery.RuntimePatchCount} runtime)");
        
        // Discover events (subscriptions and fires)
        Console.WriteLine("  Discovering event interactions...");
        DiscoverEventInteractions(sourceFiles, result);
        Console.WriteLine($"    Found {result.EventSubscriptions.Count} subscriptions, {result.EventFires.Count} fires");
        
        // Discover XML changes
        Console.WriteLine("  Discovering XML changes...");
        DiscoverXmlChanges(modPath, result);
        Console.WriteLine($"    Found {result.XmlChanges.Count} XML modifications");
    }
    
    /// <summary>
    /// Phase 2: Analyze how mod functionality interacts with game code.
    /// </summary>
    private void AnalyzeInteractions(ModAnalysisResult result)
    {
        // Resolve patch targets to game methods
        Console.WriteLine("  Resolving patch targets...");
        int resolved = 0, unresolved = 0;
        
        foreach (var patch in result.HarmonyPatches)
        {
            var gameMethodId = ResolvePatchTarget(patch);
            if (gameMethodId.HasValue)
            {
                patch.GameMethodId = gameMethodId.Value;
                resolved++;
                
                // Get callers of this method
                var callers = GetMethodCallers(gameMethodId.Value);
                result.PatchCallers[patch.GetSignature()] = callers;
                
                if (_verbose)
                    Console.WriteLine($"    ✓ {patch.GetSignature()} → {callers.Count} callers");
            }
            else
            {
                unresolved++;
                if (_verbose)
                    Console.WriteLine($"    ✗ {patch.GetSignature()} (not found in game code)");
            }
        }
        
        Console.WriteLine($"    Resolved: {resolved}, Unresolved: {unresolved}");
        
        // Analyze inheritance chains for patched methods
        Console.WriteLine("  Analyzing inheritance chains...");
        foreach (var patch in result.HarmonyPatches.Where(p => p.GameMethodId.HasValue))
        {
            var chain = GetInheritanceChain(patch.TargetType, patch.TargetMethod);
            if (chain.Any())
            {
                result.InheritanceChains[patch.GetSignature()] = chain;
                if (_verbose)
                    Console.WriteLine($"    {patch.TargetMethod}: {chain.Count} related methods");
            }
        }
        
        // Find method overloads
        Console.WriteLine("  Finding method overloads...");
        foreach (var patch in result.HarmonyPatches.Where(p => p.GameMethodId.HasValue))
        {
            var overloads = GetMethodOverloads(patch.TargetType, patch.TargetMethod);
            if (overloads.Count > 1)
            {
                result.MethodOverloads[patch.GetSignature()] = overloads;
                if (_verbose)
                    Console.WriteLine($"    {patch.TargetType}.{patch.TargetMethod}: {overloads.Count} overloads");
            }
        }
    }
    
    /// <summary>
    /// Phase 3: Trace behavioral flows through events and patches.
    /// </summary>
    private void TraceFlows(ModAnalysisResult result)
    {
        // For each event fired by mod, trace subscribers
        Console.WriteLine("  Tracing event flows...");
        foreach (var fire in result.EventFires)
        {
            var flow = new BehavioralFlow
            {
                TriggerType = "event_fire",
                TriggerDescription = $"Mod fires {fire.EventName}",
                TriggerEvent = fire.EventName
            };
            
            // Find all subscribers
            var subscribers = GetEventSubscribers(fire.EventName);
            flow.AffectedMethods = subscribers;
            
            // Check if subscribers are covered by patches
            foreach (var sub in subscribers)
            {
                var matchingPatch = result.HarmonyPatches.FirstOrDefault(p => 
                    p.TargetType.EndsWith(sub.TypeName) && p.TargetMethod == sub.MethodName);
                
                if (matchingPatch != null)
                {
                    flow.PatchesCovering.Add(matchingPatch.GetSignature());
                }
            }
            
            flow.IsCovered = flow.AffectedMethods.Count == 0 || flow.PatchesCovering.Count > 0;
            result.BehavioralFlows.Add(flow);
            
            if (_verbose)
                Console.WriteLine($"    Event '{fire.EventName}': {subscribers.Count} subscribers, " +
                                  $"{(flow.IsCovered ? "covered" : "UNCOVERED")}");
        }
        
        // For each patch, trace callers and their context
        Console.WriteLine("  Tracing patch impact...");
        foreach (var patch in result.HarmonyPatches.Where(p => p.GameMethodId.HasValue))
        {
            var callers = result.PatchCallers.GetValueOrDefault(patch.GetSignature(), new());
            var categories = CategorizeCallers(callers);
            result.PatchImpact[patch.GetSignature()] = categories;
            
            if (_verbose && categories.Any())
                Console.WriteLine($"    {patch.GetSignature()}: affects {string.Join(", ", categories.Keys)}");
        }
    }
    
    /// <summary>
    /// Phase 4: Detect potential gaps in coverage.
    /// Focus on ACTIONABLE gaps that relate to what the mod actually does.
    /// </summary>
    private void DetectGaps(ModAnalysisResult result)
    {
        // Build a set of classes/namespaces the mod interacts with
        var modRelevantTypes = BuildModRelevantTypes(result);
        Console.WriteLine($"  Mod interacts with {modRelevantTypes.Count} relevant type patterns");
        
        // Check for missing overloads - but only for overloads that are CALLED somewhere
        Console.WriteLine("  Checking for missing overloads (called only)...");
        foreach (var kvp in result.MethodOverloads)
        {
            var patchedSignature = kvp.Key;
            var allOverloads = kvp.Value;
            
            var patchedOverload = result.HarmonyPatches.FirstOrDefault(p => p.GetSignature() == patchedSignature);
            if (patchedOverload == null) continue;
            
            // If the patch doesn't specify argument types, it patches by name only 
            // (Harmony auto-resolves), so all overloads with same name are covered
            if (patchedOverload.TargetArgumentTypes == null || patchedOverload.TargetArgumentTypes.Length == 0)
            {
                if (_verbose)
                    Console.WriteLine($"    ℹ Skipping overload check for {patchedOverload.TargetMethod} (name-only patch)");
                continue;
            }
            
            foreach (var overload in allOverloads)
            {
                // Skip if already patched
                var isPatched = result.HarmonyPatches.Any(p => 
                    p.TargetType == overload.TypeName && 
                    p.TargetMethod == overload.MethodName &&
                    SignaturesMatch(p.TargetArgumentTypes, overload.ArgumentTypes));
                if (isPatched) continue;
                
                // Only flag if this overload is actually called from relevant code
                var overloadCallers = GetOverloadCallers(overload);
                var relevantCallers = overloadCallers.Where(c => IsRelevantType(c.TypeName, modRelevantTypes)).ToList();
                
                if (relevantCallers.Count == 0)
                {
                    // Also skip if overload has no callers at all
                    if (overloadCallers.Count == 0) continue;
                    
                    // Skip if delegates to a patched overload
                    var delegates = CheckIfDelegatesToPatched(overload, patchedOverload);
                    if (delegates) continue;
                    
                    // Low severity - exists but not in critical paths
                    if (_verbose)
                    {
                        var gap = new GapFinding
                        {
                            Type = GapType.MissingOverload,
                            Severity = GapSeverity.Low,
                            Description = $"Overload {overload.TypeName}.{overload.MethodName}({string.Join(", ", overload.ArgumentTypes)}) not patched",
                            Mitigation = $"Has {overloadCallers.Count} callers but none in mod-relevant paths",
                            RelatedPatch = patchedSignature
                        };
                        result.Gaps.Add(gap);
                    }
                }
                else
                {
                    // Medium severity - called from relevant code
                    var gap = new GapFinding
                    {
                        Type = GapType.MissingOverload,
                        Severity = GapSeverity.Medium,
                        Description = $"Overload {overload.TypeName}.{overload.MethodName}({string.Join(", ", overload.ArgumentTypes)}) not patched",
                        Mitigation = $"Called by: {string.Join(", ", relevantCallers.Take(3).Select(c => c.TypeName + "." + c.MethodName))}",
                        RelatedPatch = patchedSignature
                    };
                    result.Gaps.Add(gap);
                    Console.WriteLine($"    ⚠ Missing overload: {gap.Description}");
                }
            }
        }
        
        // Check for inheritance chain gaps - only for methods where it matters
        // Skip lifecycle methods (OnOpen/OnClose/etc) as those rarely call base
        Console.WriteLine("  Checking inheritance chains (high-impact only)...");
        foreach (var kvp in result.InheritanceChains)
        {
            var patchedSignature = kvp.Key;
            var chain = kvp.Value;
            
            // Find the patched method to understand what we're checking
            var patchedInfo = result.HarmonyPatches.FirstOrDefault(p => p.GetSignature() == patchedSignature);
            if (patchedInfo == null) continue;
            
            // Skip lifecycle/common override methods - they rarely call base
            if (ShouldSkipInheritanceCheck(patchedInfo.TargetMethod))
            {
                if (_verbose)
                    Console.WriteLine($"    ℹ Skipping {patchedInfo.TargetMethod} inheritance check (lifecycle method)");
                continue;
            }
            
            foreach (var derivedMethod in chain.Where(m => m.IsOverride))
            {
                // Already patched? Skip
                var isAlsoPatched = result.HarmonyPatches.Any(p =>
                    p.TargetType == derivedMethod.TypeName && p.TargetMethod == derivedMethod.MethodName);
                if (isAlsoPatched) continue;
                
                // Calls base? Skip - patch will hit it via base call
                if (derivedMethod.CallsBase) continue;
                
                // Is this override type relevant to what the mod does?
                if (!IsRelevantType(derivedMethod.TypeName, modRelevantTypes))
                {
                    // Skip - it's some random class the mod doesn't care about
                    continue;
                }
                
                // Is this override directly called somewhere?
                var directCallers = GetDirectOverrideCallers(derivedMethod);
                if (directCallers.Count == 0)
                {
                    // Not directly called - likely only reached via virtual dispatch
                    // This is a low-severity finding since the base patch handles it
                    if (_verbose)
                    {
                        var gap = new GapFinding
                        {
                            Type = GapType.InheritanceBypass,
                            Severity = GapSeverity.Low,
                            Description = $"Override {derivedMethod.TypeName}.{derivedMethod.MethodName} doesn't call base",
                            Mitigation = "Not directly called - virtual dispatch goes through base",
                            RelatedPatch = patchedSignature
                        };
                        result.Gaps.Add(gap);
                    }
                    continue;
                }
                
                // This is a real gap - override doesn't call base AND is directly called
                var gap2 = new GapFinding
                {
                    Type = GapType.InheritanceBypass,
                    Severity = GapSeverity.High,
                    Description = $"Override {derivedMethod.TypeName}.{derivedMethod.MethodName} doesn't call base",
                    Mitigation = $"Directly called by: {string.Join(", ", directCallers.Take(3).Select(c => c.TypeName))}",
                    RelatedPatch = patchedSignature
                };
                result.Gaps.Add(gap2);
                Console.WriteLine($"    ❌ Inheritance gap: {gap2.Description}");
            }
        }
        
        // Check for uncovered event subscribers - only if mod fires those events
        Console.WriteLine("  Checking event coverage...");
        foreach (var flow in result.BehavioralFlows.Where(f => !f.IsCovered && f.AffectedMethods.Count > 0))
        {
            // Only flag if the mod fires this event OR patches something that fires it
            var modFiresEvent = result.EventFires.Any(e => e.EventName == flow.TriggerEvent);
            if (!modFiresEvent) continue;
            
            var gap = new GapFinding
            {
                Type = GapType.UncoveredEventSubscriber,
                Severity = GapSeverity.Medium,
                Description = $"Event {flow.TriggerEvent} has {flow.AffectedMethods.Count} subscribers not covered by patches",
                Mitigation = "Review if these subscribers need patching"
            };
            
            result.Gaps.Add(gap);
            Console.WriteLine($"    ⚠ Event gap: {gap.Description}");
        }
        
        // Summary
        var highGaps = result.Gaps.Count(g => g.Severity == GapSeverity.High);
        var mediumGaps = result.Gaps.Count(g => g.Severity == GapSeverity.Medium);
        var lowGaps = result.Gaps.Count(g => g.Severity == GapSeverity.Low);
        
        Console.WriteLine($"\n  Gap summary: {highGaps} high, {mediumGaps} medium, {lowGaps} low");
    }
    
    /// <summary>
    /// Build a set of type patterns relevant to what this mod does.
    /// Only include types directly involved in the mod's patches.
    /// </summary>
    private HashSet<string> BuildModRelevantTypes(ModAnalysisResult result)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add types from patches - ONLY the exact types, not conceptual matches
        foreach (var patch in result.HarmonyPatches)
        {
            types.Add(patch.TargetType);
        }
        
        // Add types mentioned in XML changes
        foreach (var xml in result.XmlChanges)
        {
            // Extract type-like references from XPath
            var typeMatches = System.Text.RegularExpressions.Regex.Matches(xml.XPath, @"@class='([^']+)'");
            foreach (System.Text.RegularExpressions.Match match in typeMatches)
            {
                types.Add(match.Groups[1].Value);
            }
        }
        
        return types;
    }
    
    private bool IsRelevantType(string typeName, HashSet<string> relevantTypes)
    {
        // Direct match only - no conceptual fuzzy matching
        return relevantTypes.Contains(typeName);
    }
    
    /// <summary>
    /// Check if a method should be skipped for inheritance bypass analysis.
    /// These are commonly overridden lifecycle methods where inheritance bypass is expected.
    /// </summary>
    private bool ShouldSkipInheritanceCheck(string methodName)
    {
        // These methods are commonly overridden without calling base - that's normal
        var skipMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OnOpen", "OnClose", "Update", "Awake", "Start", "OnEnable", "OnDisable",
            "OnDestroy", "FixedUpdate", "LateUpdate", "OnGUI", "Init", "Cleanup",
            "UpdateBackend", "Refresh", "Reset"
        };
        return skipMethods.Contains(methodName);
    }
    
    private List<MethodReference> GetOverloadCallers(MethodReference overload)
    {
        var callers = new List<MethodReference>();
        
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT t2.name, m2.name
            FROM methods m
            JOIN types t ON m.type_id = t.id
            JOIN calls c ON c.callee_id = m.id
            JOIN methods m2 ON c.caller_id = m2.id
            JOIN types t2 ON m2.type_id = t2.id
            WHERE m.name = @methodName
              AND t.name = @typeName
            LIMIT 50
        ";
        cmd.Parameters.AddWithValue("@methodName", overload.MethodName);
        cmd.Parameters.AddWithValue("@typeName", overload.TypeName);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            callers.Add(new MethodReference
            {
                TypeName = reader.GetString(0),
                MethodName = reader.GetString(1)
            });
        }
        
        return callers;
    }
    
    private bool CheckIfDelegatesToPatched(MethodReference overload, HarmonyPatchInfo patchedMethod)
    {
        // Check if overload's body calls the patched method
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM methods m
            JOIN types t ON m.type_id = t.id
            JOIN calls c ON c.caller_id = m.id
            JOIN methods m2 ON c.callee_id = m2.id
            JOIN types t2 ON m2.type_id = t2.id
            WHERE m.name = @overloadMethod AND t.name = @overloadType
              AND m2.name = @patchedMethod AND t2.name LIKE '%' || @patchedType || '%'
        ";
        cmd.Parameters.AddWithValue("@overloadMethod", overload.MethodName);
        cmd.Parameters.AddWithValue("@overloadType", overload.TypeName);
        cmd.Parameters.AddWithValue("@patchedMethod", patchedMethod.TargetMethod);
        cmd.Parameters.AddWithValue("@patchedType", patchedMethod.TargetType);
        
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }
    
    private List<MethodReference> GetDirectOverrideCallers(MethodReference derivedMethod)
    {
        var callers = new List<MethodReference>();
        
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT t2.name, m2.name
            FROM methods m
            JOIN types t ON m.type_id = t.id
            JOIN calls c ON c.callee_id = m.id
            JOIN methods m2 ON c.caller_id = m2.id
            JOIN types t2 ON m2.type_id = t2.id
            WHERE m.name = @methodName
              AND t.name = @typeName
            LIMIT 20
        ";
        cmd.Parameters.AddWithValue("@methodName", derivedMethod.MethodName);
        cmd.Parameters.AddWithValue("@typeName", derivedMethod.TypeName);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            callers.Add(new MethodReference
            {
                TypeName = reader.GetString(0),
                MethodName = reader.GetString(1)
            });
        }
        
        return callers;
    }
    
    // ========================================================================
    // Helper Methods
    // ========================================================================
    
    private ModMetadata LoadModMetadata(string modPath)
    {
        var metadata = new ModMetadata { Name = Path.GetFileName(modPath) };
        var modInfoPath = Path.Combine(modPath, "ModInfo.xml");
        
        if (File.Exists(modInfoPath))
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(modInfoPath);
                var root = doc.Root;
                if (root != null)
                {
                    metadata.Name = root.Element("Name")?.Attribute("value")?.Value ?? metadata.Name;
                    metadata.Version = root.Element("Version")?.Attribute("value")?.Value ?? "unknown";
                    metadata.Author = root.Element("Author")?.Attribute("value")?.Value ?? "";
                    metadata.Description = root.Element("Description")?.Attribute("value")?.Value ?? "";
                }
            }
            catch { /* Use defaults */ }
        }
        
        return metadata;
    }
    
    private List<string> FindSourceFiles(string modPath)
    {
        var files = new List<string>();
        
        // Look for .cs files in common locations
        var searchDirs = new[] { modPath, Path.Combine(modPath, "Scripts"), Path.Combine(modPath, modPath.Split(Path.DirectorySeparatorChar).Last()) };
        
        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(dir))
            {
                files.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories));
            }
        }
        
        return files.Distinct().ToList();
    }
    
    private void DiscoverEventInteractions(List<string> sourceFiles, ModAnalysisResult result)
    {
        foreach (var file in sourceFiles)
        {
            try
            {
                var code = File.ReadAllText(file);
                var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();
                
                // This reuses EventFlowExtractor patterns
                // For now, simple regex-based detection for speed
                
                // Event subscriptions: something.EventName += handler
                var subMatches = System.Text.RegularExpressions.Regex.Matches(code, 
                    @"(\w+)\.(\w+)\s*\+=\s*(\w+)");
                foreach (System.Text.RegularExpressions.Match match in subMatches)
                {
                    result.EventSubscriptions.Add(new EventInteraction
                    {
                        EventName = match.Groups[2].Value,
                        HandlerMethod = match.Groups[3].Value,
                        FilePath = file,
                        IsSubscription = true
                    });
                }
                
                // Event fires: EventName?.Invoke() or EventName()
                var fireMatches = System.Text.RegularExpressions.Regex.Matches(code,
                    @"(\w+)\?\s*\.?\s*Invoke\s*\(|(\w+Changed)\s*\?\s*\.?\s*Invoke");
                foreach (System.Text.RegularExpressions.Match match in fireMatches)
                {
                    var eventName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (!string.IsNullOrEmpty(eventName))
                    {
                        result.EventFires.Add(new EventInteraction
                        {
                            EventName = eventName,
                            FilePath = file,
                            IsSubscription = false
                        });
                    }
                }
            }
            catch { /* Skip files with parse errors */ }
        }
    }
    
    private void DiscoverXmlChanges(string modPath, ModAnalysisResult result)
    {
        var configPath = Path.Combine(modPath, "Config");
        if (!Directory.Exists(configPath)) return;
        
        foreach (var xmlFile in Directory.GetFiles(configPath, "*.xml"))
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlFile);
                var fileName = Path.GetFileName(xmlFile);
                
                foreach (var elem in doc.Descendants())
                {
                    var xpath = elem.Attribute("xpath")?.Value;
                    if (xpath != null)
                    {
                        result.XmlChanges.Add(new XmlChangeInfo
                        {
                            FileName = fileName,
                            Operation = elem.Name.LocalName,
                            XPath = xpath
                        });
                    }
                }
            }
            catch { /* Skip invalid XML */ }
        }
    }
    
    private int? ResolvePatchTarget(HarmonyPatchInfo patch)
    {
        using var cmd = _conn.CreateCommand();
        
        // Try to find the method in the database
        cmd.CommandText = @"
            SELECT m.id 
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.name = @methodName
              AND (t.name = @typeName OR t.full_name LIKE '%' || @typeName || '%' OR t.name LIKE '%' || @typeName)
            LIMIT 1
        ";
        cmd.Parameters.AddWithValue("@methodName", patch.TargetMethod);
        cmd.Parameters.AddWithValue("@typeName", patch.TargetType);
        
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : null;
    }
    
    private List<MethodReference> GetMethodCallers(int methodId)
    {
        var callers = new List<MethodReference>();
        
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, m.name, m.signature
            FROM calls c
            JOIN methods m ON c.caller_id = m.id
            JOIN types t ON m.type_id = t.id
            WHERE c.callee_id = @methodId
        ";
        cmd.Parameters.AddWithValue("@methodId", methodId);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            callers.Add(new MethodReference
            {
                TypeName = reader.GetString(0),
                MethodName = reader.GetString(1),
                Signature = reader.GetString(2)
            });
        }
        
        return callers;
    }
    
    private List<MethodReference> GetInheritanceChain(string typeName, string methodName)
    {
        var chain = new List<MethodReference>();
        var visitedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // First, find the patched type and its base types (upward)
        var basesToCheck = new Queue<string>();
        basesToCheck.Enqueue(typeName);
        
        while (basesToCheck.Count > 0)
        {
            var currentType = basesToCheck.Dequeue();
            if (visitedTypes.Contains(currentType)) continue;
            visitedTypes.Add(currentType);
            
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.name, t.base_type, m.is_override, m.is_virtual
                FROM methods m
                JOIN types t ON m.type_id = t.id
                WHERE m.name = @methodName
                  AND (t.name = @typeName OR t.full_name LIKE '%' || @typeName)
            ";
            cmd.Parameters.AddWithValue("@methodName", methodName);
            cmd.Parameters.AddWithValue("@typeName", currentType);
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var baseType = reader.IsDBNull(1) ? null : reader.GetString(1);
                chain.Add(new MethodReference
                {
                    TypeName = reader.GetString(0),
                    MethodName = methodName,
                    IsOverride = reader.GetInt32(2) == 1,
                    IsVirtual = reader.GetInt32(3) == 1,
                    BaseType = baseType
                });
                
                // Queue the base type for checking
                if (!string.IsNullOrEmpty(baseType) && !visitedTypes.Contains(baseType))
                {
                    basesToCheck.Enqueue(baseType);
                }
            }
        }
        
        // Now find derived types (downward) - types that inherit from our patched type
        using var derivedCmd = _conn.CreateCommand();
        derivedCmd.CommandText = @"
            SELECT t.name, t.base_type, m.is_override, m.is_virtual
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.name = @methodName
              AND t.base_type LIKE '%' || @typeName || '%'
            ORDER BY t.name
        ";
        derivedCmd.Parameters.AddWithValue("@methodName", methodName);
        derivedCmd.Parameters.AddWithValue("@typeName", typeName);
        
        using var derivedReader = derivedCmd.ExecuteReader();
        while (derivedReader.Read())
        {
            var derivedTypeName = derivedReader.GetString(0);
            if (visitedTypes.Contains(derivedTypeName)) continue;
            visitedTypes.Add(derivedTypeName);
            
            chain.Add(new MethodReference
            {
                TypeName = derivedTypeName,
                MethodName = methodName,
                IsOverride = derivedReader.GetInt32(2) == 1,
                IsVirtual = derivedReader.GetInt32(3) == 1,
                BaseType = derivedReader.IsDBNull(1) ? null : derivedReader.GetString(1)
            });
        }
        
        return chain;
    }
    
    private List<MethodReference> GetMethodOverloads(string typeName, string methodName)
    {
        var overloads = new List<MethodReference>();
        
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, m.name, m.signature
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.name = @methodName
              AND (t.name = @typeName OR t.full_name LIKE '%' || @typeName || '%')
        ";
        cmd.Parameters.AddWithValue("@methodName", methodName);
        cmd.Parameters.AddWithValue("@typeName", typeName);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var signature = reader.GetString(2);
            overloads.Add(new MethodReference
            {
                TypeName = reader.GetString(0),
                MethodName = reader.GetString(1),
                Signature = signature,
                ArgumentTypes = ParseArgumentTypes(signature)
            });
        }
        
        return overloads;
    }
    
    private List<MethodReference> GetEventSubscribers(string eventName)
    {
        var subscribers = new List<MethodReference>();
        
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT subscriber_type, handler_method
            FROM event_subscriptions
            WHERE event_name LIKE '%' || @eventName || '%'
        ";
        cmd.Parameters.AddWithValue("@eventName", eventName);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var fullType = reader.GetString(0);
            subscribers.Add(new MethodReference
            {
                TypeName = fullType.Contains('.') ? fullType.Split('.').Last() : fullType,
                MethodName = reader.GetString(1)
            });
        }
        
        return subscribers;
    }
    
    private Dictionary<string, int> CategorizeCallers(List<MethodReference> callers)
    {
        var categories = new Dictionary<string, int>();
        
        foreach (var caller in callers)
        {
            var category = caller.TypeName switch
            {
                var t when t.Contains("Recipe") || t.Contains("Craft") => "Crafting",
                var t when t.Contains("Trader") || t.Contains("Trade") => "Trading",
                var t when t.Contains("Challenge") => "Challenges",
                var t when t.Contains("XUi") => "UI",
                var t when t.Contains("Player") => "Player",
                var t when t.Contains("Inventory") || t.Contains("Bag") => "Inventory",
                _ => "Other"
            };
            
            categories[category] = categories.GetValueOrDefault(category, 0) + 1;
        }
        
        return categories;
    }
    
    private bool SignaturesMatch(string[]? sig1, string[]? sig2)
    {
        if (sig1 == null && sig2 == null) return true;
        if (sig1 == null || sig2 == null) return false;
        if (sig1.Length != sig2.Length) return false;
        
        for (int i = 0; i < sig1.Length; i++)
        {
            if (!sig1[i].Equals(sig2[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        
        return true;
    }
    
    private string[] ParseArgumentTypes(string signature)
    {
        // Parse "MethodName(int, string)" → ["int", "string"]
        var match = System.Text.RegularExpressions.Regex.Match(signature, @"\(([^)]*)\)");
        if (!match.Success) return Array.Empty<string>();
        
        var args = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(args)) return Array.Empty<string>();
        
        return args.Split(',').Select(a => a.Trim()).ToArray();
    }
    
    /// <summary>
    /// Generate markdown report from analysis results.
    /// </summary>
    public string GenerateReport(ModAnalysisResult result)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"# QA Analysis Report: {result.Metadata.Name} v{result.Metadata.Version}");
        sb.AppendLine();
        sb.AppendLine($"**Analyzed:** {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Mod Path:** {result.ModPath}");
        sb.AppendLine();
        
        // Executive Summary
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Count |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Harmony Patches | {result.HarmonyPatches.Count} |");
        sb.AppendLine($"| Event Fires | {result.EventFires.Count} |");
        sb.AppendLine($"| Event Subscriptions | {result.EventSubscriptions.Count} |");
        sb.AppendLine($"| XML Changes | {result.XmlChanges.Count} |");
        sb.AppendLine();
        
        var highGaps = result.Gaps.Count(g => g.Severity == GapSeverity.High);
        var medGaps = result.Gaps.Count(g => g.Severity == GapSeverity.Medium);
        var lowGaps = result.Gaps.Count(g => g.Severity == GapSeverity.Low);
        
        if (result.Gaps.Count == 0)
        {
            sb.AppendLine("**✅ No gaps detected**");
        }
        else
        {
            sb.AppendLine($"**Potential Issues:** {highGaps} high, {medGaps} medium, {lowGaps} low");
        }
        sb.AppendLine();
        
        // Harmony Patches
        sb.AppendLine("## Harmony Patches");
        sb.AppendLine();
        sb.AppendLine("| Target | Type | Discovery | Callers |");
        sb.AppendLine("|--------|------|-----------|---------|");
        foreach (var patch in result.HarmonyPatches.OrderBy(p => p.TargetType).ThenBy(p => p.TargetMethod))
        {
            var callerCount = result.PatchCallers.GetValueOrDefault(patch.GetSignature(), new()).Count;
            var discovery = patch.IsRuntimePatch ? "Runtime" : "Attribute";
            sb.AppendLine($"| {patch.TargetType}.{patch.TargetMethod} | {patch.Type} | {discovery} | {callerCount} |");
        }
        sb.AppendLine();
        
        // Event Flows
        if (result.EventFires.Any())
        {
            sb.AppendLine("## Event Flows");
            sb.AppendLine();
            foreach (var fire in result.EventFires.DistinctBy(f => f.EventName))
            {
                var flow = result.BehavioralFlows.FirstOrDefault(f => f.TriggerEvent == fire.EventName);
                var status = flow?.IsCovered == true ? "✅" : "⚠️";
                var subCount = flow?.AffectedMethods.Count ?? 0;
                
                sb.AppendLine($"### {status} {fire.EventName}");
                sb.AppendLine();
                sb.AppendLine($"- **Subscribers:** {subCount}");
                
                if (flow != null && flow.AffectedMethods.Any())
                {
                    sb.AppendLine("- **Affected methods:**");
                    foreach (var method in flow.AffectedMethods.Take(10))
                    {
                        sb.AppendLine($"  - {method.TypeName}.{method.MethodName}");
                    }
                }
                sb.AppendLine();
            }
        }
        
        // Gaps
        if (result.Gaps.Any())
        {
            sb.AppendLine("## Potential Gaps");
            sb.AppendLine();
            
            foreach (var gapGroup in result.Gaps.GroupBy(g => g.Severity).OrderByDescending(g => g.Key))
            {
                var icon = gapGroup.Key switch
                {
                    GapSeverity.High => "❌",
                    GapSeverity.Medium => "⚠️",
                    GapSeverity.Low => "ℹ️",
                    _ => "•"
                };
                
                sb.AppendLine($"### {icon} {gapGroup.Key} Severity");
                sb.AppendLine();
                foreach (var gap in gapGroup)
                {
                    sb.AppendLine($"- **{gap.Type}**: {gap.Description}");
                    sb.AppendLine($"  - *Mitigation:* {gap.Mitigation}");
                }
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("## Gap Analysis: All Clear ✅");
            sb.AppendLine();
            sb.AppendLine("No significant gaps detected in patch coverage.");
        }
        
        return sb.ToString();
    }
}

// ============================================================================
// Data Models
// ============================================================================

public class ModAnalysisResult
{
    public string ModPath { get; set; } = "";
    public DateTime AnalyzedAt { get; set; }
    public ModMetadata Metadata { get; set; } = new();
    public List<string> SourceFiles { get; set; } = new();
    public List<HarmonyPatchInfo> HarmonyPatches { get; set; } = new();
    public List<EventInteraction> EventSubscriptions { get; set; } = new();
    public List<EventInteraction> EventFires { get; set; } = new();
    public List<XmlChangeInfo> XmlChanges { get; set; } = new();
    
    // Analysis results
    public Dictionary<string, List<MethodReference>> PatchCallers { get; set; } = new();
    public Dictionary<string, List<MethodReference>> InheritanceChains { get; set; } = new();
    public Dictionary<string, List<MethodReference>> MethodOverloads { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> PatchImpact { get; set; } = new();
    public List<BehavioralFlow> BehavioralFlows { get; set; } = new();
    public List<GapFinding> Gaps { get; set; } = new();
}

public class ModMetadata
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
}

public class EventInteraction
{
    public string EventName { get; set; } = "";
    public string? HandlerMethod { get; set; }
    public string FilePath { get; set; } = "";
    public bool IsSubscription { get; set; }
}

public class XmlChangeInfo
{
    public string FileName { get; set; } = "";
    public string Operation { get; set; } = "";
    public string XPath { get; set; } = "";
}

public class MethodReference
{
    public string TypeName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string Signature { get; set; } = "";
    public string[]? ArgumentTypes { get; set; }
    public bool IsOverride { get; set; }
    public bool IsVirtual { get; set; }
    public bool CallsBase { get; set; }
    public string? BaseType { get; set; }
}

public class BehavioralFlow
{
    public string TriggerType { get; set; } = "";
    public string TriggerDescription { get; set; } = "";
    public string? TriggerEvent { get; set; }
    public List<MethodReference> AffectedMethods { get; set; } = new();
    public List<string> PatchesCovering { get; set; } = new();
    public bool IsCovered { get; set; }
}

public class GapFinding
{
    public GapType Type { get; set; }
    public GapSeverity Severity { get; set; }
    public string Description { get; set; } = "";
    public string Mitigation { get; set; } = "";
    public string? RelatedPatch { get; set; }
}

public enum GapType
{
    MissingOverload,
    InheritanceBypass,
    UncoveredEventSubscriber,
    ParallelImplementation,
    DeadPatch
}

public enum GapSeverity
{
    Low,
    Medium,
    High
}
