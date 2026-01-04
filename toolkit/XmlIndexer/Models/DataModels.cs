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
    List<EntityDependencyInfo> SampleDependencyChains
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
