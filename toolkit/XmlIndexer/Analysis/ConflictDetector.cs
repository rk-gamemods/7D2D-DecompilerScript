using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace XmlIndexer.Analysis;

/// <summary>
/// Detects and reports conflicts between mods using normalized XPath analysis.
/// Outputs structured JSON for AI consumption and web report integration.
/// </summary>
public class ConflictDetector
{
    private readonly string _dbPath;
    private readonly string? _callgraphDbPath;

    public ConflictDetector(string dbPath, string? callgraphDbPath = null)
    {
        _dbPath = dbPath;
        _callgraphDbPath = callgraphDbPath ?? Environment.GetEnvironmentVariable("CALLGRAPH_DB_PATH");
    }

    /// <summary>
    /// Runs full conflict detection and returns structured results.
    /// </summary>
    public ConflictReport DetectAllConflicts()
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();

        var report = new ConflictReport
        {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            DatabasePath = _dbPath,

            // XPath-level conflicts
            RealConflicts = GetRealConflicts(db),
            DestructiveConflicts = GetDestructiveConflicts(db),
            ContestedEntities = GetContestedEntities(db),
            LoadOrderWinners = GetLoadOrderWinners(db),
            CSharpXmlConflicts = GetCSharpXmlConflicts(db),

            // Effect-level conflicts
            EffectOperationConflicts = GetEffectOperationConflicts(db),
            SetOverridesAddConflicts = GetSetOverridesAddConflicts(db),
            SynergisticStackingWarnings = GetSynergisticStacking(db),
            MixedModifierInteractions = GetMixedModifierInteractions(db),
            TriggeredEffectConflicts = GetTriggeredEffectConflicts(db),
            PartialModifications = GetPartialModifications(db),
            EffectSummaries = GetEffectSummaries(db)
        };

        // Calculate summary including new effect-level conflicts
        report.Summary = new ConflictSummary
        {
            High = report.RealConflicts.Count(c => c.Severity == "HIGH") +
                   report.DestructiveConflicts.Count +
                   report.CSharpXmlConflicts.Count +
                   report.EffectOperationConflicts.Count +
                   report.SetOverridesAddConflicts.Count +
                   report.TriggeredEffectConflicts.Count(c => c.Severity == "HIGH"),
            Medium = report.RealConflicts.Count(c => c.Severity == "MEDIUM") +
                     report.ContestedEntities.Count(e => e.RiskLevel == "MEDIUM") +
                     report.SynergisticStackingWarnings.Count +
                     report.MixedModifierInteractions.Count +
                     report.PartialModifications.Count +
                     report.TriggeredEffectConflicts.Count(c => c.Severity == "MEDIUM"),
            Low = report.RealConflicts.Count(c => c.Severity == "LOW") +
                  report.ContestedEntities.Count(e => e.RiskLevel == "LOW") +
                  report.TriggeredEffectConflicts.Count(c => c.Severity == "LOW"),
            TotalScore = CalculateTotalScore(report)
        };

        return report;
    }

    /// <summary>
    /// Outputs the conflict report as JSON to stdout.
    /// </summary>
    public void OutputJson(ConflictReport report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Console.WriteLine(JsonSerializer.Serialize(report, options));
    }

    private List<XPathConflict> GetRealConflicts(SqliteConnection db)
    {
        var conflicts = new List<XPathConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT xpath_hash, xpath_normalized, operation, mod_count, mods_involved,
                   distinct_values, conflict_type, base_severity, target_type, target_name, property_name
            FROM v_xpath_conflicts
            WHERE conflict_type = 'REAL_CONFLICT'
            ORDER BY
                CASE base_severity WHEN 'HIGH' THEN 1 WHEN 'MEDIUM' THEN 2 ELSE 3 END,
                mod_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new XPathConflict
            {
                XPathHash = reader.GetString(0),
                XPath = reader.IsDBNull(1) ? null : reader.GetString(1),
                Operation = reader.GetString(2),
                ModCount = reader.GetInt32(3),
                ModsInvolved = reader.GetString(4).Split(',').ToList(),
                DistinctValues = reader.GetInt32(5),
                ConflictType = reader.GetString(6),
                Severity = reader.GetString(7),
                TargetType = reader.IsDBNull(8) ? null : reader.GetString(8),
                TargetName = reader.IsDBNull(9) ? null : reader.GetString(9),
                PropertyName = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }

        // Get the actual values for each conflict
        foreach (var conflict in conflicts)
        {
            if (conflict.XPathHash != null && conflict.Operation != null)
                conflict.Values = GetConflictValues(db, conflict.XPathHash, conflict.Operation);
        }

        return conflicts;
    }

    private List<ConflictValue> GetConflictValues(SqliteConnection db, string xpathHash, string operation)
    {
        var values = new List<ConflictValue>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name, m.load_order, o.new_value, o.file_path, o.line_number
            FROM mod_xml_operations o
            JOIN mods m ON o.mod_id = m.id
            WHERE o.xpath_hash = $hash AND o.operation = $op
            ORDER BY m.load_order";
        cmd.Parameters.AddWithValue("$hash", xpathHash);
        cmd.Parameters.AddWithValue("$op", operation);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            values.Add(new ConflictValue
            {
                ModName = reader.GetString(0),
                LoadOrder = reader.GetInt32(1),
                Value = reader.IsDBNull(2) ? null : reader.GetString(2),
                FilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                LineNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            });
        }

        return values;
    }

    private List<DestructiveConflict> GetDestructiveConflicts(SqliteConnection db)
    {
        var conflicts = new List<DestructiveConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT xpath_hash, xpath, editor_mod, editor_op, editor_value,
                   remover_mod, remover_op, target_type, target_name,
                   editor_load_order, remover_load_order, winner
            FROM v_destructive_conflicts
            ORDER BY target_type, target_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new DestructiveConflict
            {
                XPathHash = reader.GetString(0),
                XPath = reader.IsDBNull(1) ? null : reader.GetString(1),
                EditorMod = reader.GetString(2),
                EditorOperation = reader.GetString(3),
                EditorValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                RemoverMod = reader.GetString(5),
                RemoverOperation = reader.GetString(6),
                TargetType = reader.IsDBNull(7) ? null : reader.GetString(7),
                TargetName = reader.IsDBNull(8) ? null : reader.GetString(8),
                EditorLoadOrder = reader.GetInt32(9),
                RemoverLoadOrder = reader.GetInt32(10),
                Winner = reader.GetString(11),
                Severity = "HIGH"
            });
        }

        return conflicts;
    }

    private List<ConflictContestedEntity> GetContestedEntities(SqliteConnection db)
    {
        var entities = new List<ConflictContestedEntity>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT target_type, target_name, mod_count, mod_actions,
                   removal_count, set_count, append_count, risk_level
            FROM v_contested_entities
            ORDER BY
                CASE risk_level WHEN 'HIGH' THEN 1 WHEN 'MEDIUM' THEN 2 ELSE 3 END,
                mod_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entities.Add(new ConflictContestedEntity
            {
                TargetType = reader.GetString(0),
                TargetName = reader.GetString(1),
                ModCount = reader.GetInt32(2),
                ModActions = ParseModActions(reader.GetString(3)),
                RemovalCount = reader.GetInt32(4),
                SetCount = reader.GetInt32(5),
                AppendCount = reader.GetInt32(6),
                RiskLevel = reader.GetString(7)
            });
        }

        return entities;
    }

    private List<LoadOrderWinner> GetLoadOrderWinners(SqliteConnection db)
    {
        var winners = new List<LoadOrderWinner>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT xpath_hash, xpath, operation, winning_mod, load_order,
                   winning_value, target_type, target_name, total_mods
            FROM v_load_order_winners
            ORDER BY total_mods DESC, target_type, target_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            winners.Add(new LoadOrderWinner
            {
                XPathHash = reader.GetString(0),
                XPath = reader.IsDBNull(1) ? null : reader.GetString(1),
                Operation = reader.GetString(2),
                WinningMod = reader.GetString(3),
                LoadOrder = reader.GetInt32(4),
                WinningValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                TargetType = reader.IsDBNull(6) ? null : reader.GetString(6),
                TargetName = reader.IsDBNull(7) ? null : reader.GetString(7),
                TotalMods = reader.GetInt32(8)
            });
        }

        return winners;
    }

    private List<CSharpXmlConflict> GetCSharpXmlConflicts(SqliteConnection db)
    {
        var conflicts = new List<CSharpXmlConflict>();

        // Find C# mods that depend on entities that are removed by XML mods
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT
                cd.mod_id as csharp_mod_id,
                mc.name as csharp_mod,
                cd.dependency_type,
                cd.dependency_name,
                cd.source_file,
                cd.line_number,
                cd.pattern,
                mx.name as xml_mod,
                o.operation,
                o.xpath
            FROM mod_csharp_deps cd
            JOIN mods mc ON cd.mod_id = mc.id
            JOIN mod_xml_operations o ON
                (o.target_type = cd.dependency_type OR
                 (cd.dependency_type = 'harmony_class' AND o.target_type IS NOT NULL))
                AND o.target_name = cd.dependency_name
            JOIN mods mx ON o.mod_id = mx.id
            WHERE o.operation IN ('remove', 'removeattribute')
              AND mc.id != mx.id
            ORDER BY mc.name, cd.dependency_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new CSharpXmlConflict
            {
                CSharpMod = reader.GetString(1),
                DependencyType = reader.GetString(2),
                DependencyName = reader.GetString(3),
                SourceFile = reader.IsDBNull(4) ? null : reader.GetString(4),
                LineNumber = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Pattern = reader.IsDBNull(6) ? null : reader.GetString(6),
                XmlMod = reader.GetString(7),
                XmlOperation = reader.GetString(8),
                XPath = reader.IsDBNull(9) ? null : reader.GetString(9),
                Severity = "HIGH",
                Reason = $"C# mod '{reader.GetString(1)}' depends on {reader.GetString(2)} '{reader.GetString(3)}' which is removed by '{reader.GetString(7)}'"
            });
        }

        return conflicts;
    }

    private List<ModAction> ParseModActions(string modActionsStr)
    {
        var actions = new List<ModAction>();
        foreach (var action in modActionsStr.Split(','))
        {
            var parts = action.Split(':');
            if (parts.Length == 2)
            {
                actions.Add(new ModAction { ModName = parts[0], Operation = parts[1] });
            }
        }
        return actions;
    }

    // ========================================================================
    // Effect-Level Conflict Detection Methods
    // ========================================================================

    private List<EffectOperationConflict> GetEffectOperationConflicts(SqliteConnection db)
    {
        var conflicts = new List<EffectOperationConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT effect_name, parent_entity_name, mod1, mod1_operation, mod2, mod2_operation, severity, reason
            FROM v_effect_operation_conflicts
            ORDER BY effect_name, parent_entity_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new EffectOperationConflict
            {
                EffectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                ParentEntityName = reader.IsDBNull(1) ? null : reader.GetString(1),
                Mod1 = reader.IsDBNull(2) ? null : reader.GetString(2),
                Mod1Operation = reader.IsDBNull(3) ? null : reader.GetString(3),
                Mod2 = reader.IsDBNull(4) ? null : reader.GetString(4),
                Mod2Operation = reader.IsDBNull(5) ? null : reader.GetString(5),
                Severity = reader.GetString(6),
                Reason = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return conflicts;
    }

    private List<SetOverridesAddConflict> GetSetOverridesAddConflicts(SqliteConnection db)
    {
        var conflicts = new List<SetOverridesAddConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT effect_name, parent_entity_name, add_mod, add_operation, add_value,
                   set_mod, set_operation, set_value, add_load_order, set_load_order, outcome, severity
            FROM v_set_overrides_add
            ORDER BY effect_name, parent_entity_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new SetOverridesAddConflict
            {
                EffectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                ParentEntityName = reader.IsDBNull(1) ? null : reader.GetString(1),
                AddMod = reader.IsDBNull(2) ? null : reader.GetString(2),
                AddOperation = reader.IsDBNull(3) ? null : reader.GetString(3),
                AddValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                SetMod = reader.IsDBNull(5) ? null : reader.GetString(5),
                SetOperation = reader.IsDBNull(6) ? null : reader.GetString(6),
                SetValue = reader.IsDBNull(7) ? null : reader.GetString(7),
                AddLoadOrder = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                SetLoadOrder = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                Outcome = reader.IsDBNull(10) ? null : reader.GetString(10),
                Severity = reader.GetString(11)
            });
        }

        return conflicts;
    }

    private List<SynergisticStacking> GetSynergisticStacking(SqliteConnection db)
    {
        var warnings = new List<SynergisticStacking>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT effect_name, parent_entity_name, effect_operation, mod_count, mods, mod_values, severity, reason
            FROM v_synergistic_stacking
            ORDER BY mod_count DESC, effect_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            warnings.Add(new SynergisticStacking
            {
                EffectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                ParentEntityName = reader.IsDBNull(1) ? null : reader.GetString(1),
                EffectOperation = reader.IsDBNull(2) ? null : reader.GetString(2),
                ModCount = reader.GetInt32(3),
                Mods = reader.IsDBNull(4) ? new List<string>() : reader.GetString(4).Split(',').ToList(),
                ModValues = reader.IsDBNull(5) ? null : reader.GetString(5),
                Severity = reader.GetString(6),
                Reason = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return warnings;
    }

    private List<MixedModifierInteraction> GetMixedModifierInteractions(SqliteConnection db)
    {
        var interactions = new List<MixedModifierInteraction>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT effect_name, parent_entity_name, mod_count, mod_operations, base_mod_count, perc_mod_count, severity, reason
            FROM v_mixed_modifier_interaction
            ORDER BY mod_count DESC, effect_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            interactions.Add(new MixedModifierInteraction
            {
                EffectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                ParentEntityName = reader.IsDBNull(1) ? null : reader.GetString(1),
                ModCount = reader.GetInt32(2),
                ModOperations = reader.IsDBNull(3) ? null : reader.GetString(3),
                BaseModCount = reader.GetInt32(4),
                PercModCount = reader.GetInt32(5),
                Severity = reader.GetString(6),
                Reason = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return interactions;
    }

    private List<TriggeredEffectConflict> GetTriggeredEffectConflicts(SqliteConnection db)
    {
        var conflicts = new List<TriggeredEffectConflict>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT trigger_cvar, trigger_operation, mod_count, mods, details, severity, reason
            FROM v_triggered_effect_conflicts
            ORDER BY
                CASE severity WHEN 'HIGH' THEN 1 WHEN 'MEDIUM' THEN 2 ELSE 3 END,
                mod_count DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new TriggeredEffectConflict
            {
                TriggerCVar = reader.IsDBNull(0) ? null : reader.GetString(0),
                TriggerOperation = reader.IsDBNull(1) ? null : reader.GetString(1),
                ModCount = reader.GetInt32(2),
                Mods = reader.IsDBNull(3) ? new List<string>() : reader.GetString(3).Split(',').ToList(),
                Details = reader.IsDBNull(4) ? null : reader.GetString(4),
                Severity = reader.IsDBNull(5) ? null : reader.GetString(5),
                Reason = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return conflicts;
    }

    private List<PartialModification> GetPartialModifications(SqliteConnection db)
    {
        var modifications = new List<PartialModification>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT effect_name, parent_entity_name, mod_name, modifies_operation, modifies_value,
                   file_path, line_number, severity, reason
            FROM v_partial_modifications
            ORDER BY effect_name, mod_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            modifications.Add(new PartialModification
            {
                EffectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                ParentEntityName = reader.IsDBNull(1) ? null : reader.GetString(1),
                ModName = reader.IsDBNull(2) ? null : reader.GetString(2),
                ModifiesOperation = reader.IsDBNull(3) ? false : reader.GetInt32(3) == 1,
                ModifiesValue = reader.IsDBNull(4) ? false : reader.GetInt32(4) == 1,
                FilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
                LineNumber = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                Severity = reader.GetString(7),
                Reason = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return modifications;
    }

    private List<EffectSummary> GetEffectSummaries(SqliteConnection db)
    {
        var summaries = new List<EffectSummary>();

        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT effect_name, parent_entity, parent_entity_name, total_mods, mod_operations,
                   set_count, add_count, perc_count, base_count, risk_level
            FROM v_effect_summary
            ORDER BY
                CASE risk_level WHEN 'HIGH' THEN 1 WHEN 'MEDIUM' THEN 2 ELSE 3 END,
                total_mods DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            summaries.Add(new EffectSummary
            {
                EffectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                ParentEntity = reader.IsDBNull(1) ? null : reader.GetString(1),
                ParentEntityName = reader.IsDBNull(2) ? null : reader.GetString(2),
                TotalMods = reader.GetInt32(3),
                ModOperations = reader.IsDBNull(4) ? null : reader.GetString(4),
                SetCount = reader.GetInt32(5),
                AddCount = reader.GetInt32(6),
                PercCount = reader.GetInt32(7),
                BaseCount = reader.GetInt32(8),
                RiskLevel = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return summaries;
    }

    private int CalculateTotalScore(ConflictReport report)
    {
        int score = 0;

        // HIGH severity = 40 points each
        score += (report.RealConflicts.Count(c => c.Severity == "HIGH") +
                  report.DestructiveConflicts.Count +
                  report.CSharpXmlConflicts.Count +
                  report.EffectOperationConflicts.Count +
                  report.SetOverridesAddConflicts.Count +
                  report.TriggeredEffectConflicts.Count(c => c.Severity == "HIGH")) * 40;

        // MEDIUM severity = 25 points each
        score += (report.RealConflicts.Count(c => c.Severity == "MEDIUM") +
                  report.ContestedEntities.Count(e => e.RiskLevel == "MEDIUM") +
                  report.SynergisticStackingWarnings.Count +
                  report.MixedModifierInteractions.Count +
                  report.PartialModifications.Count +
                  report.TriggeredEffectConflicts.Count(c => c.Severity == "MEDIUM")) * 25;

        // LOW severity = 10 points each
        score += (report.RealConflicts.Count(c => c.Severity == "LOW") +
                  report.ContestedEntities.Count(e => e.RiskLevel == "LOW") +
                  report.TriggeredEffectConflicts.Count(c => c.Severity == "LOW")) * 10;

        return score;
    }
}

// ============================================================================
// Conflict Report Data Models
// ============================================================================

public class ConflictReport
{
    public string? GeneratedAt { get; set; }
    public string? DatabasePath { get; set; }

    // XPath-level conflicts
    public List<XPathConflict> RealConflicts { get; set; } = new();
    public List<DestructiveConflict> DestructiveConflicts { get; set; } = new();
    public List<ConflictContestedEntity> ContestedEntities { get; set; } = new();
    public List<LoadOrderWinner> LoadOrderWinners { get; set; } = new();
    public List<CSharpXmlConflict> CSharpXmlConflicts { get; set; } = new();

    // Effect-level conflicts (new)
    public List<EffectOperationConflict> EffectOperationConflicts { get; set; } = new();
    public List<SetOverridesAddConflict> SetOverridesAddConflicts { get; set; } = new();
    public List<SynergisticStacking> SynergisticStackingWarnings { get; set; } = new();
    public List<MixedModifierInteraction> MixedModifierInteractions { get; set; } = new();
    public List<TriggeredEffectConflict> TriggeredEffectConflicts { get; set; } = new();
    public List<PartialModification> PartialModifications { get; set; } = new();
    public List<EffectSummary> EffectSummaries { get; set; } = new();

    public ConflictSummary Summary { get; set; } = new();
}

public class ConflictSummary
{
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public int TotalScore { get; set; }
}

public class XPathConflict
{
    public string? XPathHash { get; set; }
    public string? XPath { get; set; }
    public string? Operation { get; set; }
    public int ModCount { get; set; }
    public List<string> ModsInvolved { get; set; } = new();
    public int DistinctValues { get; set; }
    public string? ConflictType { get; set; }
    public string? Severity { get; set; }
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public string? PropertyName { get; set; }
    public List<ConflictValue> Values { get; set; } = new();
}

public class ConflictValue
{
    public string? ModName { get; set; }
    public int LoadOrder { get; set; }
    public string? Value { get; set; }
    public string? FilePath { get; set; }
    public int LineNumber { get; set; }
}

public class DestructiveConflict
{
    public string? XPathHash { get; set; }
    public string? XPath { get; set; }
    public string? EditorMod { get; set; }
    public string? EditorOperation { get; set; }
    public string? EditorValue { get; set; }
    public string? RemoverMod { get; set; }
    public string? RemoverOperation { get; set; }
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public int EditorLoadOrder { get; set; }
    public int RemoverLoadOrder { get; set; }
    public string? Winner { get; set; }
    public string? Severity { get; set; }
}

public class ConflictContestedEntity
{
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public int ModCount { get; set; }
    public List<ModAction> ModActions { get; set; } = new();
    public int RemovalCount { get; set; }
    public int SetCount { get; set; }
    public int AppendCount { get; set; }
    public string? RiskLevel { get; set; }
}

public class ModAction
{
    public string? ModName { get; set; }
    public string? Operation { get; set; }
}

public class LoadOrderWinner
{
    public string? XPathHash { get; set; }
    public string? XPath { get; set; }
    public string? Operation { get; set; }
    public string? WinningMod { get; set; }
    public int LoadOrder { get; set; }
    public string? WinningValue { get; set; }
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public int TotalMods { get; set; }
}

public class CSharpXmlConflict
{
    public string? CSharpMod { get; set; }
    public string? DependencyType { get; set; }
    public string? DependencyName { get; set; }
    public string? SourceFile { get; set; }
    public int LineNumber { get; set; }
    public string? Pattern { get; set; }
    public string? XmlMod { get; set; }
    public string? XmlOperation { get; set; }
    public string? XPath { get; set; }
    public string? Severity { get; set; }
    public string? Reason { get; set; }
}

// ============================================================================
// Effect-Level Conflict Models
// ============================================================================

/// <summary>
/// Different operation types on same effect (e.g., perc_add vs perc_set) - HIGH severity
/// </summary>
public class EffectOperationConflict
{
    public string? EffectName { get; set; }
    public string? ParentEntityName { get; set; }
    public string? Mod1 { get; set; }
    public string? Mod1Operation { get; set; }
    public string? Mod2 { get; set; }
    public string? Mod2Operation { get; set; }
    public string Severity { get; set; } = "HIGH";
    public string? Reason { get; set; }
}

/// <summary>
/// SET operations overriding ADD operations - HIGH severity
/// </summary>
public class SetOverridesAddConflict
{
    public string? EffectName { get; set; }
    public string? ParentEntityName { get; set; }
    public string? AddMod { get; set; }
    public string? AddOperation { get; set; }
    public string? AddValue { get; set; }
    public string? SetMod { get; set; }
    public string? SetOperation { get; set; }
    public string? SetValue { get; set; }
    public int AddLoadOrder { get; set; }
    public int SetLoadOrder { get; set; }
    public string? Outcome { get; set; }
    public string Severity { get; set; } = "HIGH";
}

/// <summary>
/// Multiple mods modifying same effect with additive operations - MEDIUM severity
/// </summary>
public class SynergisticStacking
{
    public string? EffectName { get; set; }
    public string? ParentEntityName { get; set; }
    public string? EffectOperation { get; set; }
    public int ModCount { get; set; }
    public List<string> Mods { get; set; } = new();
    public string? ModValues { get; set; }
    public string Severity { get; set; } = "MEDIUM";
    public string? Reason { get; set; }
}

/// <summary>
/// Mixed base and percentage modifiers from different mods - MEDIUM severity
/// </summary>
public class MixedModifierInteraction
{
    public string? EffectName { get; set; }
    public string? ParentEntityName { get; set; }
    public int ModCount { get; set; }
    public string? ModOperations { get; set; }
    public int BaseModCount { get; set; }
    public int PercModCount { get; set; }
    public string Severity { get; set; } = "MEDIUM";
    public string? Reason { get; set; }
}

/// <summary>
/// CVar modifications from multiple mods - severity varies
/// </summary>
public class TriggeredEffectConflict
{
    public string? TriggerCVar { get; set; }
    public string? TriggerOperation { get; set; }
    public int ModCount { get; set; }
    public List<string> Mods { get; set; } = new();
    public string? Details { get; set; }
    public string? Severity { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Mods changing @operation or @value but not both - MEDIUM severity
/// </summary>
public class PartialModification
{
    public string? EffectName { get; set; }
    public string? ParentEntityName { get; set; }
    public string? ModName { get; set; }
    public bool ModifiesOperation { get; set; }
    public bool ModifiesValue { get; set; }
    public string? FilePath { get; set; }
    public int LineNumber { get; set; }
    public string Severity { get; set; } = "MEDIUM";
    public string? Reason { get; set; }
}

/// <summary>
/// Summary of all modifications to a specific effect
/// </summary>
public class EffectSummary
{
    public string? EffectName { get; set; }
    public string? ParentEntity { get; set; }
    public string? ParentEntityName { get; set; }
    public int TotalMods { get; set; }
    public string? ModOperations { get; set; }
    public int SetCount { get; set; }
    public int AddCount { get; set; }
    public int PercCount { get; set; }
    public int BaseCount { get; set; }
    public string? RiskLevel { get; set; }
}
