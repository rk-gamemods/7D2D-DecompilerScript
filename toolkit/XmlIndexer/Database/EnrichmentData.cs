namespace XmlIndexer.Database;

/// <summary>
/// Data transfer object for cached enrichment data.
/// </summary>
public class EnrichmentData
{
    public string? EntityName { get; set; }
    public string? XmlStatus { get; set; }
    public string? XmlFile { get; set; }
    public string? FuzzyMatches { get; set; }
    public string? DeadCodeAnalysis { get; set; }
    public string? SemanticContext { get; set; }
    public string? Reachability { get; set; }
    public string? SourceContext { get; set; }
    public string? UsageLevel { get; set; }
    public string? Callees { get; set; }
    public string? TypeHierarchy { get; set; }
}
