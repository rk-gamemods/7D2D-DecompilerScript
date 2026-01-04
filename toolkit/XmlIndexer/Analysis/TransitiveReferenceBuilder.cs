using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace XmlIndexer.Analysis;

/// <summary>
/// Builds the transitive_references table by computing all indirect relationships
/// between XML definitions using a BFS (breadth-first search) traversal.
/// 
/// PURPOSE: Enable questions like "if I change buffCoffeeBuzz, what else is affected?"
/// by pre-computing all dependency chains so queries don't need recursive CTEs.
/// 
/// The algorithm:
/// 1. Load all direct references (extends, buffs, loot entries, recipe ingredients, etc.)
/// 2. For each entity, BFS to find all reachable entities (up to MAX_DEPTH)
/// 3. Store each discovered path with its depth and chain of reference types
/// </summary>
public class TransitiveReferenceBuilder
{
    private readonly string _dbPath;
    private const int MAX_DEPTH = 10;  // Prevent infinite loops, limit chain length

    public TransitiveReferenceBuilder(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Entry point: Clears existing transitive references and rebuilds from scratch.
    /// This is the recommended approach since references form a web, not a stream.
    /// </summary>
    public TransitiveBuildResult BuildTransitiveReferences()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        var result = new TransitiveBuildResult { StartedAt = DateTime.UtcNow };

        // Step 0: Ensure table exists (migration)
        EnsureTableExists(db);

        // Step 1: Clear existing data
        ClearTransitiveReferences(db);

        // Step 2: Load all direct edges (entity A -> entity B via some reference type)
        var edges = LoadDirectEdges(db);
        result.DirectEdgesCount = edges.Count;

        // Step 3: Build lookup for efficient traversal
        var adjacencyList = BuildAdjacencyList(edges);
        result.UniqueSourceEntities = adjacencyList.Count;

        // Step 4: For each entity with outgoing edges, BFS to find all reachable entities
        var transitiveRows = ComputeTransitiveClosure(adjacencyList);
        result.TransitiveRowsGenerated = transitiveRows.Count;

        // Step 5: Bulk insert into database
        BulkInsertTransitiveReferences(db, transitiveRows);

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    private void ClearTransitiveReferences(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM transitive_references";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates the transitive_references table if it doesn't exist (migration support).
    /// </summary>
    private void EnsureTableExists(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS transitive_references (
                id INTEGER PRIMARY KEY,
                source_def_id INTEGER NOT NULL,
                target_def_id INTEGER NOT NULL,
                path_depth INTEGER NOT NULL,
                path_json TEXT NOT NULL,
                reference_types TEXT,
                UNIQUE(source_def_id, target_def_id),
                FOREIGN KEY (source_def_id) REFERENCES xml_definitions(id),
                FOREIGN KEY (target_def_id) REFERENCES xml_definitions(id)
            );
            CREATE INDEX IF NOT EXISTS idx_transitive_source ON transitive_references(source_def_id);
            CREATE INDEX IF NOT EXISTS idx_transitive_target ON transitive_references(target_def_id);
            CREATE INDEX IF NOT EXISTS idx_transitive_depth ON transitive_references(path_depth);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Load all direct reference relationships from xml_definitions and xml_references.
    /// Returns edges as (source_def_id, target_def_id, reference_type).
    /// </summary>
    private List<DirectEdge> LoadDirectEdges(SqliteConnection db)
    {
        var edges = new List<DirectEdge>();

        // Edge type 1: "extends" relationships from xml_definitions table
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT 
                    d1.id as source_id,
                    d2.id as target_id,
                    'extends' as ref_type
                FROM xml_definitions d1
                JOIN xml_definitions d2 
                    ON d1.extends = d2.name 
                    AND d1.definition_type = d2.definition_type
                WHERE d1.extends IS NOT NULL";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                edges.Add(new DirectEdge
                {
                    SourceDefId = reader.GetInt32(0),
                    TargetDefId = reader.GetInt32(1),
                    ReferenceType = reader.GetString(2)
                });
            }
        }

        // Edge type 2: All other references from xml_references table
        // We need to join back to xml_definitions to get the target's id
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT 
                    r.source_def_id,
                    d.id as target_id,
                    r.reference_context as ref_type
                FROM xml_references r
                JOIN xml_definitions d 
                    ON r.target_name = d.name 
                    AND r.target_type = d.definition_type
                WHERE r.source_def_id IS NOT NULL";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                edges.Add(new DirectEdge
                {
                    SourceDefId = reader.GetInt32(0),
                    TargetDefId = reader.GetInt32(1),
                    ReferenceType = reader.GetString(2)
                });
            }
        }

        return edges;
    }

    /// <summary>
    /// Build adjacency list: for each source entity, list of (target, refType) pairs.
    /// </summary>
    private Dictionary<int, List<(int targetId, string refType)>> BuildAdjacencyList(List<DirectEdge> edges)
    {
        var adjacency = new Dictionary<int, List<(int, string)>>();

        foreach (var edge in edges)
        {
            if (!adjacency.ContainsKey(edge.SourceDefId))
                adjacency[edge.SourceDefId] = new List<(int, string)>();

            adjacency[edge.SourceDefId].Add((edge.TargetDefId, edge.ReferenceType));
        }

        return adjacency;
    }

    /// <summary>
    /// For each entity with outgoing edges, BFS to find all reachable entities.
    /// Returns list of TransitiveRow (ready for database insertion).
    /// </summary>
    private List<TransitiveRow> ComputeTransitiveClosure(
        Dictionary<int, List<(int targetId, string refType)>> adjacency)
    {
        var results = new List<TransitiveRow>();

        foreach (var sourceId in adjacency.Keys)
        {
            var discovered = BfsFromSource(sourceId, adjacency);
            results.AddRange(discovered);
        }

        return results;
    }

    /// <summary>
    /// BFS from a single source entity to find all reachable entities with path info.
    /// </summary>
    private List<TransitiveRow> BfsFromSource(
        int sourceId,
        Dictionary<int, List<(int targetId, string refType)>> adjacency)
    {
        var results = new List<TransitiveRow>();
        
        // Track visited (target_id) to prevent cycles and duplicate paths
        // We keep the shortest path to each target
        var visited = new HashSet<int> { sourceId };
        
        // Queue: (currentId, depth, pathSoFar)
        var queue = new Queue<(int currentId, int depth, List<string> path)>();
        
        // Initialize with direct neighbors
        if (adjacency.TryGetValue(sourceId, out var neighbors))
        {
            foreach (var (targetId, refType) in neighbors)
            {
                if (!visited.Contains(targetId))
                {
                    visited.Add(targetId);
                    var path = new List<string> { $"{refType}:{targetId}" };
                    queue.Enqueue((targetId, 1, path));
                    
                    results.Add(new TransitiveRow
                    {
                        SourceDefId = sourceId,
                        TargetDefId = targetId,
                        PathDepth = 1,
                        PathJson = JsonSerializer.Serialize(path),
                        ReferenceTypes = refType
                    });
                }
            }
        }

        // BFS loop
        while (queue.Count > 0)
        {
            var (currentId, depth, currentPath) = queue.Dequeue();
            
            if (depth >= MAX_DEPTH)
                continue;

            if (adjacency.TryGetValue(currentId, out var nextNeighbors))
            {
                foreach (var (targetId, refType) in nextNeighbors)
                {
                    if (!visited.Contains(targetId))
                    {
                        visited.Add(targetId);
                        var newPath = new List<string>(currentPath) { $"{refType}:{targetId}" };
                        var newDepth = depth + 1;
                        
                        queue.Enqueue((targetId, newDepth, newPath));

                        // Collect all unique reference types in path
                        var refTypes = new HashSet<string>();
                        foreach (var step in newPath)
                        {
                            var colonIdx = step.IndexOf(':');
                            if (colonIdx > 0)
                                refTypes.Add(step.Substring(0, colonIdx));
                        }

                        results.Add(new TransitiveRow
                        {
                            SourceDefId = sourceId,
                            TargetDefId = targetId,
                            PathDepth = newDepth,
                            PathJson = JsonSerializer.Serialize(newPath),
                            ReferenceTypes = string.Join(",", refTypes.OrderBy(x => x))
                        });
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Bulk insert transitive reference rows into database.
    /// </summary>
    private void BulkInsertTransitiveReferences(SqliteConnection db, List<TransitiveRow> rows)
    {
        using var transaction = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        
        cmd.CommandText = @"
            INSERT INTO transitive_references 
                (source_def_id, target_def_id, path_depth, path_json, reference_types)
            VALUES ($src, $tgt, $depth, $path, $types)
            ON CONFLICT(source_def_id, target_def_id) DO UPDATE SET
                path_depth = excluded.path_depth,
                path_json = excluded.path_json,
                reference_types = excluded.reference_types";

        var pSrc = cmd.Parameters.Add("$src", SqliteType.Integer);
        var pTgt = cmd.Parameters.Add("$tgt", SqliteType.Integer);
        var pDepth = cmd.Parameters.Add("$depth", SqliteType.Integer);
        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
        var pTypes = cmd.Parameters.Add("$types", SqliteType.Text);

        foreach (var row in rows)
        {
            pSrc.Value = row.SourceDefId;
            pTgt.Value = row.TargetDefId;
            pDepth.Value = row.PathDepth;
            pPath.Value = row.PathJson;
            pTypes.Value = row.ReferenceTypes ?? (object)DBNull.Value;
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ========================================================================
    // Internal data structures
    // ========================================================================

    private class DirectEdge
    {
        public int SourceDefId { get; set; }
        public int TargetDefId { get; set; }
        public required string ReferenceType { get; set; }
    }

    private class TransitiveRow
    {
        public int SourceDefId { get; set; }
        public int TargetDefId { get; set; }
        public int PathDepth { get; set; }
        public required string PathJson { get; set; }
        public string? ReferenceTypes { get; set; }
    }
}

/// <summary>
/// Result of building transitive references - for logging/diagnostics.
/// </summary>
public class TransitiveBuildResult
{
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int DirectEdgesCount { get; set; }
    public int UniqueSourceEntities { get; set; }
    public int TransitiveRowsGenerated { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;

    public override string ToString() =>
        $"Transitive references built: {TransitiveRowsGenerated:N0} rows from {DirectEdgesCount:N0} direct edges ({UniqueSourceEntities:N0} source entities) in {Duration.TotalSeconds:F1}s";
}
