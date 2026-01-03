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
public record CSharpDependency(string ModName, string Type, string Name, string SourceFile, int Line, string Pattern);

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
    List<PropertyConflict> PropertyConflicts
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
    int XmlOps,
    int CSharpDeps,
    int Removes,
    string ModType,
    string Health,
    string HealthNote
);

/// <summary>Detailed contested entity with risk assessment</summary>
public record ContestedEntity(
    string EntityType,
    string EntityName,
    List<(string ModName, string Operation)> ModActions,
    string RiskLevel,
    string RiskReason
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
public record XPathTarget(string Type, string Name);
