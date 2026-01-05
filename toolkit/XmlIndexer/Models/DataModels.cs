namespace XmlIndexer.Models;

// =========================================================================
// Core Data Models - XML Indexing
// =========================================================================

/// <summary>XML entity definition (item, block, buff, etc.)</summary>
public record XmlDefinition(long Id, string Type, string Name, string File, int Line, string? Extends);

/// <summary>Property within an XML definition</summary>
public record XmlProperty(long DefId, string Name, string? Value, string? Class, int Line);

/// <summary>Cross-reference between XML entities</summary>
public record XmlReference(string SrcType, long? SrcDefId, string SrcFile, int Line, string TgtType, string TgtName, string Context);

/// <summary>C# mod dependency on game code</summary>
public record CSharpDependency(string ModName, string Type, string Name, string SourceFile, int Line, string Pattern, string? CodeSnippet = null);

// =========================================================================
// Mod Analysis Models
// =========================================================================

/// <summary>Summary result from analyzing a single mod</summary>
public record ModResult(string Name, int Conflicts, int Cautions, bool IsCodeOnly, List<CSharpDependency> Dependencies);

/// <summary>Track XML removals for cross-mod conflict detection</summary>
public record XmlRemoval(string ModName, string Type, string Name, string XPath);

/// <summary>Result of analyzing XML operations in a mod</summary>
public record XmlAnalysisResult(int Operations, int Conflicts, int Cautions);

/// <summary>Impact status of an XPath operation</summary>
public enum ImpactStatus { Safe, Caution, Conflict }

// =========================================================================
// Report Data Models
// =========================================================================

/// <summary>Complete data container for report generation</summary>
public record ReportData(
    int TotalDefinitions, int TotalProperties, int TotalReferences,
    Dictionary<string, int> DefinitionsByType,
    int TotalMods, int XmlMods, int CSharpMods, int HybridMods,
    Dictionary<string, int> OperationsByType,
    Dictionary<string, int> CSharpByType,
    List<(string ModName, string ClassName, string MethodName, string PatchType)> HarmonyPatches,
    List<(string ModName, string BaseClass, string ChildClass)> ClassExtensions,
    int ActiveEntities, int ModifiedEntities, int RemovedEntities, int DependedEntities,
    List<(string Type, string Name, string RemovedBy, string DependedBy)> DangerZone,
    List<ModInfo> ModSummary,
    // Entity stats
    string LongestItemName, string MostReferencedItem, int MostReferencedCount,
    string MostComplexEntity, int MostComplexProps,
    string MostConnectedEntity, int MostConnectedRefs,
    string MostDependedEntity, int MostDependedCount,
    // Contested entities
    List<ContestedEntity> ContestedEntities,
    // Behavioral analysis
    List<ModBehavior> ModBehaviors,
    // Interconnection scores
    List<InterconnectedEntity> TopInterconnected,
    // Most invasive mods
    List<InvasiveMod> MostInvasiveMods,
    // Property conflicts
    List<PropertyConflict> PropertyConflicts,
    // Dependency chain data (new)
    int TotalTransitiveRefs,
    List<InheritanceHotspot> InheritanceHotspots,
    List<EntityDependencyInfo> SampleDependencyChains,
    // Game code analysis
    int GameCodeBugs = 0,
    int GameCodeWarnings = 0,
    int GameCodeInfo = 0,
    int GameCodeOpportunities = 0
);

/// <summary>Entity with interconnection score (references to/from others)</summary>
public record InterconnectedEntity(
    string EntityType,
    string EntityName,
    int OutgoingRefs,
    int IncomingRefs,
    int InheritanceDepth,
    int TotalScore
);

/// <summary>Mod ranked by invasiveness (total game modifications)</summary>
public record InvasiveMod(
    string ModName,
    int TotalChanges,
    int XmlChanges,
    int HarmonyPatches,
    int UniqueEntities
);

/// <summary>Property-level conflict for load order awareness</summary>
public record PropertyConflict(
    string EntityType,
    string EntityName,
    string PropertyName,
    List<(string ModName, string Value)> Setters
);

/// <summary>User-friendly mod summary</summary>
public record ModInfo(
    string Name,
    int LoadOrder,
    int XmlOps,
    int CSharpDeps,
    int Removes,
    string ModType,
    string Health,
    string HealthNote,
    string? FolderName = null
);

/// <summary>Detailed contested entity with risk assessment</summary>
public record ContestedEntity(
    string EntityType,
    string EntityName,
    List<ConflictModAction> ModActions,
    string RiskLevel,
    string RiskReason
);

/// <summary>Detailed mod action in a conflict - includes XPath, property, and value context</summary>
public record ConflictModAction(
    string ModName,
    string Operation,
    string? XPath,
    string? PropertyName,
    string? NewValue,
    string? ElementContent,
    string? FilePath,
    int? LineNumber
);

/// <summary>Human-readable behavioral analysis for a mod</summary>
public record ModBehavior(
    string ModName,
    string OneLiner,
    List<string> KeyFeatures,
    List<string> SystemsAffected,
    List<string> Warnings,
    ModXmlInfo? XmlInfo
);

/// <summary>Data from ModInfo.xml</summary>
public record ModXmlInfo(
    string? DisplayName,
    string? Description,
    string? Author,
    string? Version,
    string? Website
);

// =========================================================================
// Dependency Chain Models (for impact analysis drill-down)
// =========================================================================

/// <summary>Entity that many others depend on (inheritance hotspot)</summary>
public record InheritanceHotspot(
    string EntityType,
    string EntityName,
    int DependentCount,
    string RiskLevel,
    List<string> TopDependents
);

/// <summary>Full dependency chain for an entity with drill-down details</summary>
public record DependencyChainEntry(
    string EntityType,
    string EntityName,
    int Depth,
    string ReferenceTypes,
    string FilePath,
    string PathJson  // Full path chain as JSON array
);

/// <summary>Entity with its complete dependency context for drill-down</summary>
public record EntityDependencyInfo(
    string EntityType,
    string EntityName,
    string FilePath,
    string? ExtendsFrom,
    List<DependencyChainEntry> DependsOn,
    List<DependencyChainEntry> DependedOnBy
);

// =========================================================================
// Semantic Trace Models (for LLM export/import)
// =========================================================================

/// <summary>Trace record for semantic analysis export</summary>
public record SemanticTrace(
    string EntityType,
    string EntityName,
    string? ParentContext,
    string CodeTrace,
    string? UsageExamples,
    string? RelatedEntities,
    string? GameContext
);

/// <summary>XPath target extraction result</summary>
/// <param name="Type">Entity type (item, block, loot_container, etc.)</param>
/// <param name="Name">Entity name if found via @name selector, or selector description</param>
/// <param name="SelectorAttribute">The attribute used to select (e.g., "name", "size", "id")</param>
/// <param name="SelectorValue">The value in the selector</param>
/// <param name="IsFragile">True if selection is NOT by @name - may be fragile/unreliable</param>
public record XPathTarget(string Type, string Name, string SelectorAttribute = "name", string? SelectorValue = null, bool IsFragile = false);

// =========================================================================
// Extended Report Data Models (for multi-page HTML reports)
// =========================================================================

/// <summary>Extended data for multi-page reports - gathered once and passed to all page generators</summary>
public record ExtendedReportData(
    List<EntityExport> AllEntities,
    List<ReferenceExport> AllReferences,
    List<TransitiveExport> AllTransitiveRefs,
    List<ModDetailExport> ModDetails,
    List<CallGraphNode> CallGraphNodes,
    List<EventFlowEdge> EventFlowData,
    Dictionary<string, int> ReferenceTypeCounts
);

/// <summary>Entity export for JSON embedding in entity page</summary>
public record EntityExport(
    string Type,
    string Name,
    string? FilePath,
    int Line,
    string? Extends,
    int PropertyCount,
    int ReferenceCount,
    List<PropertyExport>? Properties
);

/// <summary>Property export for entity details</summary>
public record PropertyExport(string Name, string? Value, string? Class);

/// <summary>Reference export for JSON embedding</summary>
public record ReferenceExport(
    string SourceType,
    string SourceName,
    string TargetType,
    string TargetName,
    string Context
);

/// <summary>Transitive reference export</summary>
public record TransitiveExport(
    string SourceType,
    string SourceName,
    string TargetType,
    string TargetName,
    int Depth,
    string ReferenceTypes
);

/// <summary>Detailed mod export for mods page</summary>
public record ModDetailExport(
    string ModName,
    List<XmlOperationExport> XmlOperations,
    List<HarmonyPatchExport> HarmonyPatches,
    List<ClassExtensionExport> ClassExtensions,
    List<CSharpEntityDependency>? EntityDependencies = null,
    Dictionary<string, int>? EntityTypeBreakdown = null,
    List<string>? TopTargetedEntities = null
);

/// <summary>C# code dependency on a game entity (item, block, buff, etc.)</summary>
public record CSharpEntityDependency(
    string EntityType,
    string EntityName,
    string SourceFile,
    string Pattern
);

/// <summary>XML operation for mod details</summary>
public record XmlOperationExport(
    string Operation,
    string? TargetType,
    string? TargetName,
    string? PropertyName,
    string? XPath,
    string? ElementContent
);

/// <summary>Harmony patch for mod details</summary>
public record HarmonyPatchExport(
    string ClassName,
    string MethodName,
    string PatchType,
    string? CodeSnippet = null
);

/// <summary>Class extension for mod details</summary>
public record ClassExtensionExport(
    string BaseClass,
    string ChildClass
);

/// <summary>Call graph node for C# page</summary>
public record CallGraphNode(
    string MethodName,
    string ClassName,
    string ModName,
    int Depth,
    List<string> Calls
);

/// <summary>Event flow edge for C# page</summary>
public record EventFlowEdge(
    string EventName,
    int SubscriberCount,
    int TriggerCount
);

// =========================================================================
// Harmony Patch Analysis Models
// =========================================================================

/// <summary>Detailed Harmony patch information for conflict detection</summary>
public record HarmonyPatchInfo(
    long Id,
    long ModId,
    string PatchClass,
    string TargetClass,
    string TargetMethod,
    string PatchType,
    string? TargetMemberKind = null,
    string? TargetArgTypes = null,
    string? TargetDeclaringType = null,
    int HarmonyPriority = 400,
    string? HarmonyBefore = null,
    string? HarmonyAfter = null,
    bool ReturnsBool = false,
    bool ModifiesResult = false,
    bool ModifiesState = false,
    bool IsGuarded = false,
    string? GuardCondition = null,
    bool IsDynamic = false,
    string? ParameterSignature = null,
    string? CodeSnippet = null,
    string? SourceFile = null,
    int? LineNumber = null
);

/// <summary>Detected conflict between Harmony patches</summary>
public record HarmonyConflict(
    long Id,
    string TargetClass,
    string TargetMethod,
    string ConflictType,
    string Severity,
    string Confidence,
    long? Mod1Id,
    long? Mod2Id,
    long? Patch1Id,
    long? Patch2Id,
    bool SameSignature,
    string? Explanation,
    string? Reasoning
);

/// <summary>Harmony conflict types</summary>
public enum HarmonyConflictType
{
    Collision,              // Multiple mods patching same method
    TranspilerDuplicate,    // Multiple transpilers on same method (CRITICAL)
    SignatureMismatch,      // Patch signature doesn't match game method
    SkipConflict,           // Prefix can skip + other patches exist
    InheritanceOverlap,     // Patches on parent/child class methods
    OrderConflict           // Conflicting priority/before/after
}

/// <summary>Conflict severity levels</summary>
public enum ConflictSeverity
{
    Critical,   // Almost guaranteed to cause issues (multiple transpilers)
    High,       // Very likely to cause issues (skip conflicts, result overwrites)
    Medium,     // May cause issues depending on usage (same target, different values)
    Low         // Informational (same target, potentially compatible)
}

/// <summary>Confidence level for detected issues</summary>
public enum ConfidenceLevel
{
    High,       // High certainty this is a real issue
    Medium,     // Likely an issue but may have false positives
    Low         // Possible issue, requires manual verification
}

// =========================================================================
// Game Code Analysis Models
// =========================================================================

/// <summary>Game code analysis finding (bug, dead code, etc.)</summary>
public record GameCodeFinding(
    long Id,
    string AnalysisType,
    string ClassName,
    string? MethodName,
    string Severity,
    string Confidence,
    string? Description,
    string? Reasoning,
    string? CodeSnippet,
    string? FilePath,
    int? LineNumber,
    string? PotentialFix,
    string? RelatedEntities,
    string? FileHash,
    bool IsUnityMagic = false,
    bool IsReflectionTarget = false
);

/// <summary>Game code analysis types</summary>
public enum GameCodeAnalysisType
{
    DeadCode,           // Methods never called
    Unreachable,        // Code after unconditional return/throw
    NullDeref,          // Potential null dereference
    Unimplemented,      // NotImplementedException thrown
    Suspicious,         // Suspicious patterns (FP equality, missing break, etc.)
    Todo,               // TODO/FIXME comments
    Secret,             // Hidden features, debug flags, console commands
    EmptyCatch,         // Empty catch blocks
    StubMethod          // Methods with only return null/default
}

/// <summary>Finding severity for game code analysis</summary>
public enum FindingSeverity
{
    Bug,            // Likely a bug
    Warning,        // Potential issue worth investigating
    Info,           // Informational finding
    Opportunity     // Modding opportunity or hidden feature
}

// =========================================================================
// Type Resolution Models
// =========================================================================

/// <summary>Cached method signature for overload matching</summary>
public record MethodSignatureInfo(
    long Id,
    string ClassName,
    string MethodName,
    string ParameterTypes,
    string? ParameterTypesFull,
    string? ReturnType,
    string? ReturnTypeFull,
    bool IsStatic,
    bool IsVirtual,
    bool IsOverride,
    string? AccessModifier,
    string? DeclaringClass,
    string? FilePath,
    string? FileHash
);

/// <summary>Class inheritance information</summary>
public record ClassInheritanceInfo(
    long Id,
    string ClassName,
    string? ParentClass,
    string? Interfaces,
    bool IsAbstract,
    bool IsSealed,
    string? FilePath,
    string? FileHash
);

/// <summary>Type alias from using statement</summary>
public record TypeAliasInfo(
    long Id,
    string FilePath,
    string ShortName,
    string FullName,
    string? Namespace
);

// =========================================================================
// Report Models for Harmony Conflicts
// =========================================================================

/// <summary>Harmony collision summary for reports</summary>
public record HarmonyCollisionSummary(
    string TargetClass,
    string TargetMethod,
    int ModCount,
    List<string> Mods,
    List<string> PatchTypes,
    int TranspilerCount,
    int SkipCapableCount,
    string Severity
);

/// <summary>Transpiler conflict for reports</summary>
public record TranspilerConflict(
    string TargetClass,
    string TargetMethod,
    int TranspilerCount,
    List<string> Mods,
    string Reason
);

/// <summary>Skip conflict for reports</summary>
public record SkipConflictInfo(
    string TargetClass,
    string TargetMethod,
    string SkipMod,
    string SkipClass,
    int SkipPriority,
    List<string> AffectedMods,
    string Severity,
    string Reason
);

/// <summary>Game code issue summary for reports</summary>
public record GameCodeIssueSummary(
    string AnalysisType,
    string Severity,
    string Confidence,
    int IssueCount,
    List<string> AffectedClasses
);
