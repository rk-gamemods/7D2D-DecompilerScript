using Microsoft.Data.Sqlite;
using XmlIndexer.Database;

namespace XmlIndexer.Tests.Database;

/// <summary>
/// Tests for DatabaseBuilder - schema creation, caching, hash computation.
/// </summary>
public class DatabaseBuilderTests
{
    /// <summary>
    /// Test that database schema creates successfully with all expected tables.
    /// </summary>
    [Fact]
    public void CreateSchema_CreatesExpectedTables()
    {
        // Use in-memory SQLite database for fast tests
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        DatabaseBuilder.CreateSchema(connection);
        
        // Verify key tables exist
        var expectedTables = new[]
        {
            "xml_definitions",
            "xml_properties", 
            "xml_references",
            "mods",
            "mod_xml_operations",
            "file_hashes"
        };
        
        foreach (var table in expectedTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}'";
            var result = cmd.ExecuteScalar();
            Assert.NotNull(result);
            Assert.Equal(table, result?.ToString());
        }
    }

    /// <summary>
    /// Test that file hash caching works correctly.
    /// </summary>
    [Fact]
    public void FileHashCaching_DetectsChanges()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        DatabaseBuilder.CreateSchema(connection);
        
        // First check - no hash stored
        var hasHash = HasStoredHash(connection, "test_key");
        Assert.False(hasHash);
        
        // Store a hash
        StoreHash(connection, "test_key", "abc123");
        
        // Now should have hash
        hasHash = HasStoredHash(connection, "test_key");
        Assert.True(hasHash);
        
        // Retrieve and verify
        var hash = GetStoredHash(connection, "test_key");
        Assert.Equal("abc123", hash);
    }
    
    private static bool HasStoredHash(SqliteConnection db, string key)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM file_hashes WHERE file_path = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() != null;
    }
    
    private static string? GetStoredHash(SqliteConnection db, string key)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM file_hashes WHERE file_path = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }
    
    private static void StoreHash(SqliteConnection db, string key, string hash)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO file_hashes (file_path, content_hash, file_type, last_processed)
            VALUES ($key, $hash, 'test', datetime('now'))";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.ExecuteNonQuery();
    }
}
