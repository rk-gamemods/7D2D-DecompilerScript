using Microsoft.Data.Sqlite;

namespace CallGraphExtractor;

/// <summary>
/// Handles SQLite database creation and writing extracted data.
/// </summary>
public class SqliteWriter : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;
    private bool _disposed;
    
    public SqliteWriter(string dbPath)
    {
        _dbPath = dbPath;
        _connection = new SqliteConnection($"Data Source={dbPath}");
    }
    
    /// <summary>
    /// Open connection and initialize schema.
    /// </summary>
    public void Initialize()
    {
        _connection.Open();
        CreateSchema();
    }
    
    /// <summary>
    /// Create all database tables from embedded schema.
    /// </summary>
    private void CreateSchema()
    {
        var schema = GetEmbeddedSchema();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = schema;
        cmd.ExecuteNonQuery();
        
        // Set schema version
        SetMetadata("schema_version", "1");
    }
    
    /// <summary>
    /// Load the schema.sql from the toolkit directory.
    /// </summary>
    private static string GetEmbeddedSchema()
    {
        // Look for schema.sql relative to the executable
        var exeDir = AppContext.BaseDirectory;
        var schemaPath = Path.Combine(exeDir, "..", "..", "..", "..", "schema.sql");
        
        // Normalize the path
        schemaPath = Path.GetFullPath(schemaPath);
        
        if (!File.Exists(schemaPath))
        {
            // Try looking in current directory as fallback
            schemaPath = Path.Combine(Directory.GetCurrentDirectory(), "schema.sql");
        }
        
        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException($"schema.sql not found. Looked in: {schemaPath}");
        }
        
        return File.ReadAllText(schemaPath);
    }
    
    // =========================================================================
    // Metadata
    // =========================================================================
    
    public void SetMetadata(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO metadata (key, value) VALUES (@key, @value)
        ";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }
    
    // =========================================================================
    // Type writing
    // =========================================================================
    
    public long InsertType(string name, string? @namespace, string fullName, string kind,
                           string? baseType, string? filePath, int? lineNumber,
                           bool isAbstract = false, bool isSealed = false, bool isStatic = false)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO types (name, namespace, full_name, kind, base_type, file_path, line_number,
                              is_abstract, is_sealed, is_static)
            VALUES (@name, @namespace, @full_name, @kind, @base_type, @file_path, @line_number,
                    @is_abstract, @is_sealed, @is_static)
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@namespace", @namespace ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@full_name", fullName);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@base_type", baseType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@is_abstract", isAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_sealed", isSealed ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_static", isStatic ? 1 : 0);
        
        return (long)cmd.ExecuteScalar()!;
    }
    
    // =========================================================================
    // Method writing
    // =========================================================================
    
    public long InsertMethod(long typeId, string name, string signature, string? returnType,
                             string? filePath, int? lineNumber, int? endLine,
                             bool isStatic = false, bool isVirtual = false, 
                             bool isOverride = false, bool isAbstract = false,
                             string? access = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO methods (type_id, name, signature, return_type, file_path, line_number,
                                end_line, is_static, is_virtual, is_override, is_abstract, access)
            VALUES (@type_id, @name, @signature, @return_type, @file_path, @line_number,
                    @end_line, @is_static, @is_virtual, @is_override, @is_abstract, @access)
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@type_id", typeId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@signature", signature);
        cmd.Parameters.AddWithValue("@return_type", returnType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@end_line", endLine ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@is_static", isStatic ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_virtual", isVirtual ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_override", isOverride ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_abstract", isAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("@access", access ?? (object)DBNull.Value);
        
        return (long)cmd.ExecuteScalar()!;
    }
    
    /// <summary>
    /// Insert method body for FTS5 indexing.
    /// </summary>
    public void InsertMethodBody(long methodId, string body)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO method_bodies (method_id, body) VALUES (@method_id, @body)
        ";
        cmd.Parameters.AddWithValue("@method_id", methodId.ToString());
        cmd.Parameters.AddWithValue("@body", body);
        cmd.ExecuteNonQuery();
    }
    
    // =========================================================================
    // Call edge writing
    // =========================================================================
    
    public void InsertCall(long callerId, long calleeId, string? filePath, int? lineNumber,
                           string callType = "direct")
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO calls (caller_id, callee_id, file_path, line_number, call_type)
            VALUES (@caller_id, @callee_id, @file_path, @line_number, @call_type)
        ";
        
        cmd.Parameters.AddWithValue("@caller_id", callerId);
        cmd.Parameters.AddWithValue("@callee_id", calleeId);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@call_type", callType);
        cmd.ExecuteNonQuery();
    }
    
    // =========================================================================
    // Interface implementation writing
    // =========================================================================
    
    public void InsertImplements(long typeId, string interfaceName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO implements (type_id, interface_name)
            VALUES (@type_id, @interface_name)
        ";
        cmd.Parameters.AddWithValue("@type_id", typeId);
        cmd.Parameters.AddWithValue("@interface_name", interfaceName);
        cmd.ExecuteNonQuery();
    }
    
    // =========================================================================
    // Batch operations for performance
    // =========================================================================
    
    public SqliteTransaction BeginTransaction()
    {
        return _connection.BeginTransaction();
    }
    
    // =========================================================================
    // Dispose
    // =========================================================================
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Close();
            _connection.Dispose();
            _disposed = true;
        }
    }
}
