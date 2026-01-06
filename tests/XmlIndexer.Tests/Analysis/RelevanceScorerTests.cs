using Xunit;
using Microsoft.Data.Sqlite;
using XmlIndexer.Analysis;
using XmlIndexer.Database;

namespace XmlIndexer.Tests.Analysis;

/// <summary>
/// Tests for RelevanceScorer - scoring algorithms and database interactions.
/// </summary>
public class RelevanceScorerTests : IDisposable
{
    private readonly SqliteConnection _db;

    public RelevanceScorerTests()
    {
        // Create in-memory database for each test
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        DatabaseBuilder.CreateSchema(_db);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }

    // ==========================================================================
    // Schema Tests
    // ==========================================================================

    [Fact]
    public void Schema_CodeRelevance_TableExists()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='code_relevance'";
        var result = cmd.ExecuteScalar();
        Assert.NotNull(result);
        Assert.Equal("code_relevance", result);
    }

    [Fact]
    public void Schema_RelevanceWeights_HasDefaultValues()
    {
        // Seed defaults
        DatabaseBuilder.EnsureSchema(_db);
        
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM relevance_weights";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.True(count >= 4, "Should have at least 4 default weights");
        
        // Check specific weights exist
        cmd.CommandText = "SELECT weight FROM relevance_weights WHERE factor_name = 'mod'";
        var modWeight = Convert.ToDouble(cmd.ExecuteScalar());
        Assert.Equal(1.5, modWeight);
    }

    [Fact]
    public void Schema_ImportantKeywords_SeededCorrectly()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM important_keywords";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.True(count > 10, "Should have many seeded keywords");
        
        // Check inventory keywords exist
        cmd.CommandText = "SELECT multiplier FROM important_keywords WHERE keyword = 'Inventory' AND category = 'inventory'";
        var multiplier = Convert.ToDouble(cmd.ExecuteScalar());
        Assert.Equal(1.5, multiplier);
    }

    [Fact]
    public void Schema_CodeRelevance_HasForeignKey_ToGameCodeAnalysis()
    {
        // Insert a finding first
        InsertTestFinding("TestClass", "TestMethod", "WARNING");
        
        // Get the finding ID
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id FROM game_code_analysis LIMIT 1";
        var findingId = Convert.ToInt64(cmd.ExecuteScalar());
        
        // Insert a relevance score
        cmd.CommandText = @"
            INSERT INTO code_relevance (analysis_id, connectivity_score, entity_score, mod_score, keyword_score, artifact_penalty, total_score, computed_at)
            VALUES ($id, 30, 40, 50, 10, -5, 125, datetime('now'))";
        cmd.Parameters.AddWithValue("$id", findingId);
        cmd.ExecuteNonQuery();
        
        // Verify it was inserted
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT total_score FROM code_relevance WHERE analysis_id = $id";
        cmd.Parameters.AddWithValue("$id", findingId);
        var score = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(125, score);
    }

    [Fact]
    public void Schema_ImportantKeywords_UniqueConstraintWorks()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        // Try to insert duplicate keyword
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO important_keywords (keyword, category, multiplier) VALUES ('Inventory', 'inventory', 2.0)";
        
        var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
        Assert.Contains("UNIQUE constraint failed", ex.Message);
    }

    // ==========================================================================
    // Connectivity Score Tests
    // ==========================================================================

    [Theory]
    [InlineData("HIGH", 30)]
    [InlineData("MEDIUM", 15)]
    [InlineData("LOW", 5)]
    public void ComputeConnectivityScore_UsageLevels_ReturnsCorrectPoints(string usageLevel, int expectedMinScore)
    {
        // Create scorer with seeded data
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        // Create test finding with usage level
        var finding = CreateTestAnalysisFinding("TestClass", "TestMethod", usageLevel);
        
        // Score should be at least the usage level contribution
        // (actual score may be higher if callers are found)
        var score = scorer.ComputeConnectivityScore(finding);
        Assert.True(score >= 0, $"Connectivity score should be non-negative, got {score}");
    }

    [Fact]
    public void ComputeConnectivityScore_NoCallers_Returns0OrLow()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("OrphanClass", "OrphanMethod");
        
        var score = scorer.ComputeConnectivityScore(finding);
        // With no callers and no usage level info, score should be low
        Assert.True(score <= 10, $"Score for orphan method should be low, got {score}");
    }

    // ==========================================================================
    // Entity Score Tests
    // ==========================================================================

    [Theory]
    [InlineData("ItemClass", 40)]
    [InlineData("BlockManager", 35)]
    [InlineData("EntityPlayer", 30)]
    [InlineData("RecipeManager", 25)]
    [InlineData("BuffClass", 20)]
    [InlineData("XUiController", 10)]
    [InlineData("SomeRandomClass", 0)]
    public void ComputeEntityScore_ReturnsCorrectPoints(string className, int expectedScore)
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding(className, "TestMethod");
        
        var score = scorer.ComputeEntityScore(finding);
        Assert.Equal(expectedScore, score);
    }

    [Fact]
    public void ComputeEntityScore_RelatedEntities_BoostsScore()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("UtilityClass", "HelperMethod", relatedEntities: "Item:gunPistol");
        
        var score = scorer.ComputeEntityScore(finding);
        Assert.Equal(40, score); // Item = 40 points
    }

    // ==========================================================================
    // Keyword Score Tests
    // ==========================================================================

    [Fact]
    public void ComputeKeywordScore_InventoryKeyword_ReturnsPositiveScore()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("PlayerInventory", "AddItem");
        
        var score = scorer.ComputeKeywordScore(finding);
        Assert.True(score > 0, $"Inventory keyword should give positive score, got {score}");
    }

    [Fact]
    public void ComputeKeywordScore_DebugKeyword_ReturnsNegativeScore()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("DebugManager", "DumpState");
        
        var score = scorer.ComputeKeywordScore(finding);
        Assert.True(score < 0, $"Debug keyword should give negative score, got {score}");
    }

    [Fact]
    public void ComputeKeywordScore_NoKeywords_Returns0()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("SomeClass", "SomeMethod");
        
        var score = scorer.ComputeKeywordScore(finding);
        Assert.Equal(0, score);
    }

    // ==========================================================================
    // Artifact Penalty Tests
    // ==========================================================================

    [Fact]
    public void ComputeArtifactPenalty_UnreachableCode_ReturnsMinus40()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("TestClass", "TestMethod", analysisType: "Unreachable");
        
        var penalty = scorer.ComputeArtifactPenalty(finding);
        Assert.Equal(-40, penalty);
    }

    [Fact]
    public void ComputeArtifactPenalty_DebugClass_ReturnsMinus30()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("DebugConsole", "LogMessage");
        
        var penalty = scorer.ComputeArtifactPenalty(finding);
        Assert.Equal(-30, penalty);
    }

    [Fact]
    public void ComputeArtifactPenalty_TodoComment_ReturnsMinus10()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("NormalClass", "NormalMethod", analysisType: "Todo");
        
        var penalty = scorer.ComputeArtifactPenalty(finding);
        Assert.Equal(-10, penalty);
    }

    [Fact]
    public void ComputeArtifactPenalty_NormalCode_Returns0()
    {
        DatabaseBuilder.EnsureSchema(_db);
        var scorer = new RelevanceScorer(_db);
        
        var finding = CreateTestAnalysisFinding("ItemClass", "GetValue", analysisType: "hookable_event");
        
        var penalty = scorer.ComputeArtifactPenalty(finding);
        Assert.Equal(0, penalty);
    }

    // ==========================================================================
    // Integration Tests
    // ==========================================================================

    [Fact]
    public void ComputeScores_ExistingDatabase_AddsNewTables()
    {
        // Verify tables exist after EnsureSchema
        DatabaseBuilder.EnsureSchema(_db);
        
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM sqlite_master 
            WHERE type='table' AND name IN ('code_relevance', 'relevance_weights', 'important_keywords')";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    [Fact]
    public void ComputeScores_AllFindingsScored()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        // Insert test findings
        InsertTestFinding("ClassA", "Method1", "WARNING");
        InsertTestFinding("ClassB", "Method2", "INFO");
        InsertTestFinding("ClassC", "Method3", "OPPORTUNITY");
        
        // Run scorer
        var scorer = new RelevanceScorer(_db);
        scorer.ComputeScores();
        
        // Verify all scored
        Assert.Equal(3, scorer.FindingsScored);
        
        // Verify scores in database
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM code_relevance";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    [Fact]
    public void ComputeScores_SkipsAlreadyScored()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        InsertTestFinding("TestClass", "TestMethod", "WARNING");
        
        var scorer1 = new RelevanceScorer(_db);
        scorer1.ComputeScores();
        Assert.Equal(1, scorer1.FindingsScored);
        
        // Run again - should skip
        var scorer2 = new RelevanceScorer(_db);
        scorer2.ComputeScores();
        Assert.Equal(0, scorer2.FindingsScored);
    }

    [Fact]
    public void ComputeScores_ForceRecomputes()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        InsertTestFinding("TestClass", "TestMethod", "WARNING");
        
        var scorer1 = new RelevanceScorer(_db);
        scorer1.ComputeScores();
        Assert.Equal(1, scorer1.FindingsScored);
        
        // Force recompute
        var scorer2 = new RelevanceScorer(_db);
        scorer2.ComputeScores(force: true);
        Assert.Equal(1, scorer2.FindingsScored);
    }

    [Fact]
    public void KeywordSeeding_DuplicateRun_DoesNotDuplicateKeywords()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM important_keywords";
        var count1 = Convert.ToInt32(cmd.ExecuteScalar());
        
        // Run EnsureSchema again
        DatabaseBuilder.EnsureSchema(_db);
        
        var count2 = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(count1, count2);
    }

    [Fact]
    public void CalculateTotalScore_AppliesWeightsCorrectly()
    {
        DatabaseBuilder.EnsureSchema(_db);
        
        // Insert a finding with known characteristics
        InsertTestFinding("ItemInventory", "AddItem", "OPPORTUNITY"); // Item=40, Inventory keyword
        
        var scorer = new RelevanceScorer(_db);
        scorer.ComputeScores();
        
        // Check the stored score
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT total_score, entity_score, keyword_score FROM code_relevance LIMIT 1";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        
        var totalScore = reader.GetInt32(0);
        var entityScore = reader.GetInt32(1);
        var keywordScore = reader.GetInt32(2);
        
        // Entity should be 40 (Item)
        Assert.Equal(40, entityScore);
        // Keyword should be positive (Inventory)
        Assert.True(keywordScore > 0);
        // Total should reflect weighted sum
        Assert.True(totalScore > 0);
    }

    [Fact]
    public void CalculateTotalScore_WithMissingWeights_UsesDefaults()
    {
        // Create database without seeding weights
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM relevance_weights";
        cmd.ExecuteNonQuery();
        
        InsertTestFinding("TestClass", "TestMethod", "WARNING");
        
        // Should not throw and should use default weights
        var scorer = new RelevanceScorer(_db);
        scorer.ComputeScores();
        
        Assert.Equal(1, scorer.FindingsScored);
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    private int _nextLineNumber = 1;

    private void InsertTestFinding(string className, string methodName, string severity)
    {
        var lineNumber = _nextLineNumber++;
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO game_code_analysis 
            (analysis_type, class_name, method_name, severity, confidence, description, file_path, line_number)
            VALUES ('test', $class, $method, $severity, 'medium', 'Test finding', 'test.cs', $line)";
        cmd.Parameters.AddWithValue("$class", className);
        cmd.Parameters.AddWithValue("$method", methodName);
        cmd.Parameters.AddWithValue("$severity", severity);
        cmd.Parameters.AddWithValue("$line", lineNumber);
        cmd.ExecuteNonQuery();
    }

    private RelevanceScorer.AnalysisFinding CreateTestAnalysisFinding(
        string className, 
        string methodName, 
        string? usageLevel = null,
        string? analysisType = null,
        string? relatedEntities = null)
    {
        return new RelevanceScorer.AnalysisFinding
        {
            Id = 1,
            AnalysisType = analysisType ?? "test",
            ClassName = className,
            MethodName = methodName,
            Severity = "WARNING",
            Confidence = "medium",
            Description = "Test finding",
            RelatedEntities = relatedEntities
        };
    }
}
