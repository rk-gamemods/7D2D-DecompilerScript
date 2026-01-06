using System.Text.Json;
using Microsoft.Data.Sqlite;
using XmlIndexer.Models;

namespace XmlIndexer.Analysis;

/// <summary>
/// Detects conflicts between Harmony patches from different mods.
/// Analyzes patch collisions, transpiler duplicates, skip conflicts, and inheritance overlaps.
/// </summary>
public static class HarmonyConflictDetector
{
    /// <summary>
    /// Runs all conflict detection algorithms and stores results in the harmony_conflicts table.
    /// </summary>
    public static HarmonyConflictReport DetectAllConflicts(SqliteConnection db)
    {
        var report = new HarmonyConflictReport();

        // Run all detectors
        report.Collisions = DetectPatchCollisions(db);
        report.TranspilerConflicts = DetectTranspilerDuplicates(db);
        report.SkipConflicts = DetectSkipConflicts(db);
        report.InheritanceOverlaps = DetectInheritanceOverlaps(db);
        report.OrderConflicts = DetectOrderConflicts(db);

        // Persist conflicts to database
        PersistConflicts(db, report);

        return report;
    }

    /// <summary>
    /// Detects multiple mods patching the same game method.
    /// Distinguishes between same-signature (HIGH) and different-overload (LOW) collisions.
    /// </summary>
    public static List<HarmonyCollisionSummary> DetectPatchCollisions(SqliteConnection db)
    {
        var collisions = new List<HarmonyCollisionSummary>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT
                hp.target_class,
                hp.target_method,
                COUNT(DISTINCT hp.mod_id) as mod_count,
                GROUP_CONCAT(DISTINCT m.name) as mods,
                GROUP_CONCAT(DISTINCT hp.patch_type) as patch_types,
                SUM(CASE WHEN hp.patch_type = 'Transpiler' THEN 1 ELSE 0 END) as transpiler_count,
                SUM(CASE WHEN hp.returns_bool = 1 THEN 1 ELSE 0 END) as skip_capable_count,
                GROUP_CONCAT(DISTINCT hp.target_arg_types) as arg_types_variants
            FROM harmony_patches hp
            JOIN mods m ON hp.mod_id = m.id
            GROUP BY hp.target_class, hp.target_method
            HAVING COUNT(DISTINCT hp.mod_id) > 1
            ORDER BY transpiler_count DESC, skip_capable_count DESC, mod_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var targetClass = reader.GetString(0);
            var targetMethod = reader.GetString(1);
            var modCount = reader.GetInt32(2);
            var modsStr = reader.GetString(3);
            var patchTypesStr = reader.GetString(4);
            var transpilerCount = reader.GetInt32(5);
            var skipCapableCount = reader.GetInt32(6);
            var argTypesVariants = reader.IsDBNull(7) ? null : reader.GetString(7);

            // Determine severity based on conflict characteristics
            string severity;
            if (transpilerCount > 1)
                severity = "CRITICAL";
            else if (skipCapableCount > 0 && modCount > 1)
                severity = "HIGH";
            else if (modCount >= 3)
                severity = "MEDIUM";
            else
                severity = "LOW";

            // Check if patches target different overloads (reduces severity)
            var uniqueArgTypes = argTypesVariants?.Split(',').Where(s => !string.IsNullOrEmpty(s)).Distinct().Count() ?? 0;
            if (uniqueArgTypes > 1 && severity != "CRITICAL")
            {
                // Different overloads - reduce severity
                severity = severity == "HIGH" ? "MEDIUM" : "LOW";
            }

            collisions.Add(new HarmonyCollisionSummary(
                TargetClass: targetClass,
                TargetMethod: targetMethod,
                ModCount: modCount,
                Mods: modsStr.Split(',').ToList(),
                PatchTypes: patchTypesStr.Split(',').ToList(),
                TranspilerCount: transpilerCount,
                SkipCapableCount: skipCapableCount,
                Severity: severity
            ));
        }

        return collisions;
    }

    /// <summary>
    /// Detects multiple transpilers on the same method - almost always problematic.
    /// Transpilers modify IL directly and multiple independent transpilers will likely break each other.
    /// </summary>
    public static List<TranspilerConflict> DetectTranspilerDuplicates(SqliteConnection db)
    {
        var conflicts = new List<TranspilerConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT
                hp.target_class,
                hp.target_method,
                COUNT(*) as transpiler_count,
                GROUP_CONCAT(m.name) as mods
            FROM harmony_patches hp
            JOIN mods m ON hp.mod_id = m.id
            WHERE hp.patch_type = 'Transpiler'
            GROUP BY hp.target_class, hp.target_method
            HAVING COUNT(*) > 1
            ORDER BY transpiler_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new TranspilerConflict(
                TargetClass: reader.GetString(0),
                TargetMethod: reader.GetString(1),
                TranspilerCount: reader.GetInt32(2),
                Mods: reader.GetString(3).Split(',').ToList(),
                Reason: "Multiple transpilers modifying same method IL - high chance of conflict"
            ));
        }

        return conflicts;
    }

    /// <summary>
    /// Detects Prefix patches that can skip the original method while other patches exist.
    /// A Prefix returning false skips the original AND all lower-priority patches.
    /// </summary>
    public static List<SkipConflictInfo> DetectSkipConflicts(SqliteConnection db)
    {
        var conflicts = new List<SkipConflictInfo>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT
                hp1.target_class,
                hp1.target_method,
                m1.name as skip_mod,
                hp1.patch_class as skip_class,
                hp1.harmony_priority as skip_priority,
                GROUP_CONCAT(DISTINCT m2.name) as affected_mods,
                COUNT(DISTINCT hp2.mod_id) as affected_count
            FROM harmony_patches hp1
            JOIN harmony_patches hp2 ON hp1.target_class = hp2.target_class
                AND hp1.target_method = hp2.target_method
                AND hp1.id != hp2.id
            JOIN mods m1 ON hp1.mod_id = m1.id
            JOIN mods m2 ON hp2.mod_id = m2.id
            WHERE hp1.returns_bool = 1
            GROUP BY hp1.id
            HAVING affected_count > 0
            ORDER BY skip_priority DESC, affected_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var skipPriority = reader.GetInt32(4);
            var severity = skipPriority > 400 ? "HIGH" : "MEDIUM";

            conflicts.Add(new SkipConflictInfo(
                TargetClass: reader.GetString(0),
                TargetMethod: reader.GetString(1),
                SkipMod: reader.GetString(2),
                SkipClass: reader.GetString(3),
                SkipPriority: skipPriority,
                AffectedMods: reader.GetString(5).Split(',').ToList(),
                Severity: severity,
                Reason: "Prefix can return false to skip original method and lower-priority patches"
            ));
        }

        return conflicts;
    }

    /// <summary>
    /// Detects patches on parent/child class methods that may interact unexpectedly.
    /// If both parent and child class methods are patched, calls may hit both patches.
    /// </summary>
    public static List<HarmonyConflict> DetectInheritanceOverlaps(SqliteConnection db)
    {
        var conflicts = new List<HarmonyConflict>();

        // First, check if we have inheritance data
        using var checkCmd = db.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM class_inheritance";
        var inheritanceCount = (long)checkCmd.ExecuteScalar()!;

        if (inheritanceCount == 0)
        {
            // No inheritance data - cannot detect overlaps
            return conflicts;
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT
                hp1.id as patch1_id,
                hp2.id as patch2_id,
                hp1.target_class as parent_class,
                hp2.target_class as child_class,
                hp1.target_method,
                m1.id as mod1_id,
                m2.id as mod2_id,
                m1.name as mod1_name,
                m2.name as mod2_name,
                ci.is_abstract
            FROM harmony_patches hp1
            JOIN class_inheritance ci ON hp1.target_class = ci.parent_class
            JOIN harmony_patches hp2 ON ci.class_name = hp2.target_class
                AND hp1.target_method = hp2.target_method
            JOIN mods m1 ON hp1.mod_id = m1.id
            JOIN mods m2 ON hp2.mod_id = m2.id
            WHERE hp1.mod_id != hp2.mod_id";

        using var reader = cmd.ExecuteReader();
        long conflictId = 0;
        while (reader.Read())
        {
            var isAbstract = reader.GetInt32(9) == 1;

            // Confidence depends on whether base method is virtual/abstract
            var confidence = isAbstract ? "medium" : "low";

            conflicts.Add(new HarmonyConflict(
                Id: conflictId++,
                TargetClass: reader.GetString(2),
                TargetMethod: reader.GetString(4),
                ConflictType: "inheritance_overlap",
                Severity: "MEDIUM",
                Confidence: confidence,
                Mod1Id: reader.GetInt64(5),
                Mod2Id: reader.GetInt64(6),
                Patch1Id: reader.GetInt64(0),
                Patch2Id: reader.GetInt64(1),
                SameSignature: true,
                Explanation: $"Patches on parent ({reader.GetString(2)}) and child ({reader.GetString(3)}) class methods may both fire",
                Reasoning: $"Mod '{reader.GetString(7)}' patches parent class, mod '{reader.GetString(8)}' patches child class"
            ));
        }

        return conflicts;
    }

    /// <summary>
    /// Detects patches with conflicting priority/before/after declarations.
    /// </summary>
    public static List<HarmonyConflict> DetectOrderConflicts(SqliteConnection db)
    {
        var conflicts = new List<HarmonyConflict>();
        long conflictId = 0;

        // Detect before/after circular dependencies
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT
                hp1.id as patch1_id,
                hp2.id as patch2_id,
                hp1.target_class,
                hp1.target_method,
                m1.id as mod1_id,
                m2.id as mod2_id,
                m1.name as mod1_name,
                m2.name as mod2_name,
                hp1.harmony_before as p1_before,
                hp1.harmony_after as p1_after,
                hp2.harmony_before as p2_before,
                hp2.harmony_after as p2_after
            FROM harmony_patches hp1
            JOIN harmony_patches hp2 ON hp1.target_class = hp2.target_class
                AND hp1.target_method = hp2.target_method
                AND hp1.id < hp2.id
            JOIN mods m1 ON hp1.mod_id = m1.id
            JOIN mods m2 ON hp2.mod_id = m2.id
            WHERE (hp1.harmony_before IS NOT NULL OR hp1.harmony_after IS NOT NULL
                   OR hp2.harmony_before IS NOT NULL OR hp2.harmony_after IS NOT NULL)";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var mod1Name = reader.GetString(6);
            var mod2Name = reader.GetString(7);
            var p1Before = reader.IsDBNull(8) ? null : reader.GetString(8);
            var p1After = reader.IsDBNull(9) ? null : reader.GetString(9);
            var p2Before = reader.IsDBNull(10) ? null : reader.GetString(10);
            var p2After = reader.IsDBNull(11) ? null : reader.GetString(11);

            // Check for circular dependency
            bool hasCircular = false;
            string? reason = null;

            if (p1Before != null && p2After != null)
            {
                var p1BeforeList = JsonSerializer.Deserialize<List<string>>(p1Before) ?? new();
                var p2AfterList = JsonSerializer.Deserialize<List<string>>(p2After) ?? new();

                // Mod1 wants to run before X, Mod2 wants to run after Y
                // If X contains mod2 and Y contains mod1, we have a circular dep
                if (p1BeforeList.Any(b => mod2Name.Contains(b, StringComparison.OrdinalIgnoreCase)) &&
                    p2AfterList.Any(a => mod1Name.Contains(a, StringComparison.OrdinalIgnoreCase)))
                {
                    hasCircular = true;
                    reason = $"Circular order dependency: {mod1Name} wants to run before {mod2Name}, but {mod2Name} wants to run after {mod1Name}";
                }
            }

            if (hasCircular)
            {
                conflicts.Add(new HarmonyConflict(
                    Id: conflictId++,
                    TargetClass: reader.GetString(2),
                    TargetMethod: reader.GetString(3),
                    ConflictType: "order_conflict",
                    Severity: "HIGH",
                    Confidence: "high",
                    Mod1Id: reader.GetInt64(4),
                    Mod2Id: reader.GetInt64(5),
                    Patch1Id: reader.GetInt64(0),
                    Patch2Id: reader.GetInt64(1),
                    SameSignature: true,
                    Explanation: "Circular ordering dependency between patches",
                    Reasoning: reason
                ));
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Identifies patches that have safety guards (try/catch or AccessTools null checks).
    /// These are less likely to cause crashes but may silently fail.
    /// </summary>
    public static List<(string ModName, string PatchClass, string TargetClass, string TargetMethod, string? GuardCondition)> GetGuardedPatches(SqliteConnection db)
    {
        var guarded = new List<(string, string, string, string, string?)>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name, hp.patch_class, hp.target_class, hp.target_method, hp.guard_condition
            FROM harmony_patches hp
            JOIN mods m ON hp.mod_id = m.id
            WHERE hp.is_guarded = 1";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            guarded.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }

        return guarded;
    }

    /// <summary>
    /// Gets the execution order for patches on a specific method based on Harmony priority rules.
    /// Prefixes: Higher priority runs first
    /// Postfixes: Lower priority runs first (reverse order)
    /// </summary>
    public static List<(string ModName, string PatchClass, string PatchType, int Priority, int ExecutionOrder)> GetPatchExecutionOrder(
        SqliteConnection db, string targetClass, string targetMethod)
    {
        var order = new List<(string, string, string, int, int)>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name, hp.patch_class, hp.patch_type, hp.harmony_priority
            FROM harmony_patches hp
            JOIN mods m ON hp.mod_id = m.id
            WHERE hp.target_class = $class AND hp.target_method = $method
            ORDER BY hp.patch_type,
                     CASE hp.patch_type
                         WHEN 'Prefix' THEN -hp.harmony_priority
                         ELSE hp.harmony_priority
                     END";

        cmd.Parameters.AddWithValue("$class", targetClass);
        cmd.Parameters.AddWithValue("$method", targetMethod);

        using var reader = cmd.ExecuteReader();
        int prefixOrder = 0, postfixOrder = 0, transpilerOrder = 0;

        while (reader.Read())
        {
            var patchType = reader.GetString(2);
            int execOrder = patchType switch
            {
                "Prefix" => ++prefixOrder,
                "Postfix" => ++postfixOrder,
                "Transpiler" => ++transpilerOrder,
                _ => 0
            };

            order.Add((
                reader.GetString(0),
                reader.GetString(1),
                patchType,
                reader.GetInt32(3),
                execOrder
            ));
        }

        return order;
    }

    /// <summary>
    /// Persists detected conflicts to the harmony_conflicts table.
    /// </summary>
    private static void PersistConflicts(SqliteConnection db, HarmonyConflictReport report)
    {
        // Clear existing conflicts
        using var clearCmd = db.CreateCommand();
        clearCmd.CommandText = "DELETE FROM harmony_conflicts";
        clearCmd.ExecuteNonQuery();

        using var insertCmd = db.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO harmony_conflicts
            (target_class, target_method, conflict_type, severity, confidence,
             mod1_id, mod2_id, patch1_id, patch2_id, same_signature, explanation, reasoning)
            VALUES ($class, $method, $type, $severity, $confidence,
                    $mod1, $mod2, $patch1, $patch2, $same, $explanation, $reasoning)";

        var pClass = insertCmd.Parameters.Add("$class", SqliteType.Text);
        var pMethod = insertCmd.Parameters.Add("$method", SqliteType.Text);
        var pType = insertCmd.Parameters.Add("$type", SqliteType.Text);
        var pSeverity = insertCmd.Parameters.Add("$severity", SqliteType.Text);
        var pConfidence = insertCmd.Parameters.Add("$confidence", SqliteType.Text);
        var pMod1 = insertCmd.Parameters.Add("$mod1", SqliteType.Integer);
        var pMod2 = insertCmd.Parameters.Add("$mod2", SqliteType.Integer);
        var pPatch1 = insertCmd.Parameters.Add("$patch1", SqliteType.Integer);
        var pPatch2 = insertCmd.Parameters.Add("$patch2", SqliteType.Integer);
        var pSame = insertCmd.Parameters.Add("$same", SqliteType.Integer);
        var pExplanation = insertCmd.Parameters.Add("$explanation", SqliteType.Text);
        var pReasoning = insertCmd.Parameters.Add("$reasoning", SqliteType.Text);

        using var transaction = db.BeginTransaction();

        // Insert collision conflicts
        foreach (var collision in report.Collisions.Where(c => c.Severity is "CRITICAL" or "HIGH"))
        {
            pClass.Value = collision.TargetClass;
            pMethod.Value = collision.TargetMethod;
            pType.Value = "collision";
            pSeverity.Value = collision.Severity;
            pConfidence.Value = collision.TranspilerCount > 1 ? "high" : "medium";
            pMod1.Value = DBNull.Value;
            pMod2.Value = DBNull.Value;
            pPatch1.Value = DBNull.Value;
            pPatch2.Value = DBNull.Value;
            pSame.Value = 1;
            pExplanation.Value = $"{collision.ModCount} mods patch {collision.TargetClass}.{collision.TargetMethod}";
            pReasoning.Value = $"Mods: {string.Join(", ", collision.Mods)}; Types: {string.Join(", ", collision.PatchTypes)}";
            insertCmd.ExecuteNonQuery();
        }

        // Insert transpiler conflicts
        foreach (var tc in report.TranspilerConflicts)
        {
            pClass.Value = tc.TargetClass;
            pMethod.Value = tc.TargetMethod;
            pType.Value = "transpiler_duplicate";
            pSeverity.Value = "CRITICAL";
            pConfidence.Value = "high";
            pMod1.Value = DBNull.Value;
            pMod2.Value = DBNull.Value;
            pPatch1.Value = DBNull.Value;
            pPatch2.Value = DBNull.Value;
            pSame.Value = 1;
            pExplanation.Value = $"{tc.TranspilerCount} transpilers modify {tc.TargetClass}.{tc.TargetMethod}";
            pReasoning.Value = tc.Reason;
            insertCmd.ExecuteNonQuery();
        }

        // Insert skip conflicts
        foreach (var sc in report.SkipConflicts)
        {
            pClass.Value = sc.TargetClass;
            pMethod.Value = sc.TargetMethod;
            pType.Value = "skip_conflict";
            pSeverity.Value = sc.Severity;
            pConfidence.Value = "medium";
            pMod1.Value = DBNull.Value;
            pMod2.Value = DBNull.Value;
            pPatch1.Value = DBNull.Value;
            pPatch2.Value = DBNull.Value;
            pSame.Value = 1;
            pExplanation.Value = $"{sc.SkipMod}'s prefix can skip original, affecting {sc.AffectedMods.Count} other patches";
            pReasoning.Value = sc.Reason;
            insertCmd.ExecuteNonQuery();
        }

        // Insert inheritance overlaps
        foreach (var io in report.InheritanceOverlaps)
        {
            pClass.Value = io.TargetClass;
            pMethod.Value = io.TargetMethod;
            pType.Value = io.ConflictType;
            pSeverity.Value = io.Severity;
            pConfidence.Value = io.Confidence;
            pMod1.Value = io.Mod1Id ?? (object)DBNull.Value;
            pMod2.Value = io.Mod2Id ?? (object)DBNull.Value;
            pPatch1.Value = io.Patch1Id ?? (object)DBNull.Value;
            pPatch2.Value = io.Patch2Id ?? (object)DBNull.Value;
            pSame.Value = io.SameSignature ? 1 : 0;
            pExplanation.Value = io.Explanation;
            pReasoning.Value = io.Reasoning;
            insertCmd.ExecuteNonQuery();
        }

        // Insert order conflicts
        foreach (var oc in report.OrderConflicts)
        {
            pClass.Value = oc.TargetClass;
            pMethod.Value = oc.TargetMethod;
            pType.Value = oc.ConflictType;
            pSeverity.Value = oc.Severity;
            pConfidence.Value = oc.Confidence;
            pMod1.Value = oc.Mod1Id ?? (object)DBNull.Value;
            pMod2.Value = oc.Mod2Id ?? (object)DBNull.Value;
            pPatch1.Value = oc.Patch1Id ?? (object)DBNull.Value;
            pPatch2.Value = oc.Patch2Id ?? (object)DBNull.Value;
            pSame.Value = oc.SameSignature ? 1 : 0;
            pExplanation.Value = oc.Explanation;
            pReasoning.Value = oc.Reasoning;
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Generates a summary of harmony conflicts for reporting.
    /// </summary>
    public static HarmonyConflictSummaryReport GetConflictSummary(SqliteConnection db)
    {
        var summary = new HarmonyConflictSummaryReport();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT conflict_type, severity, COUNT(*) as count
            FROM harmony_conflicts
            GROUP BY conflict_type, severity
            ORDER BY
                CASE severity
                    WHEN 'CRITICAL' THEN 1
                    WHEN 'HIGH' THEN 2
                    WHEN 'MEDIUM' THEN 3
                    ELSE 4
                END";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var conflictType = reader.GetString(0);
            var severity = reader.GetString(1);
            var count = reader.GetInt32(2);

            summary.ConflictCounts[(conflictType, severity)] = count;

            switch (severity)
            {
                case "CRITICAL": summary.CriticalCount += count; break;
                case "HIGH": summary.HighCount += count; break;
                case "MEDIUM": summary.MediumCount += count; break;
                default: summary.LowCount += count; break;
            }
        }

        summary.TotalCount = summary.CriticalCount + summary.HighCount + summary.MediumCount + summary.LowCount;

        return summary;
    }
}

/// <summary>
/// Complete report of all detected Harmony conflicts.
/// </summary>
public class HarmonyConflictReport
{
    public List<HarmonyCollisionSummary> Collisions { get; set; } = new();
    public List<TranspilerConflict> TranspilerConflicts { get; set; } = new();
    public List<SkipConflictInfo> SkipConflicts { get; set; } = new();
    public List<HarmonyConflict> InheritanceOverlaps { get; set; } = new();
    public List<HarmonyConflict> OrderConflicts { get; set; } = new();

    public int TotalConflicts => Collisions.Count + TranspilerConflicts.Count +
                                  SkipConflicts.Count + InheritanceOverlaps.Count +
                                  OrderConflicts.Count;

    public int CriticalCount => TranspilerConflicts.Count +
                                 Collisions.Count(c => c.Severity == "CRITICAL");

    public int HighCount => Collisions.Count(c => c.Severity == "HIGH") +
                            SkipConflicts.Count(s => s.Severity == "HIGH") +
                            OrderConflicts.Count(o => o.Severity == "HIGH");
}

/// <summary>
/// Summary statistics for conflict reporting.
/// </summary>
public class HarmonyConflictSummaryReport
{
    public int TotalCount { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public Dictionary<(string Type, string Severity), int> ConflictCounts { get; } = new();
}
