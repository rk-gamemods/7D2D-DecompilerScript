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
                           string? baseType, string? assembly, string? filePath, int? lineNumber,
                           bool isAbstract = false, bool isSealed = false, bool isStatic = false)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO types (name, namespace, full_name, kind, base_type, assembly, file_path, line_number,
                              is_abstract, is_sealed, is_static)
            VALUES (@name, @namespace, @full_name, @kind, @base_type, @assembly, @file_path, @line_number,
                    @is_abstract, @is_sealed, @is_static)
            ON CONFLICT(full_name) DO UPDATE SET
                assembly = COALESCE(assembly, @assembly),
                file_path = COALESCE(file_path, @file_path),
                line_number = COALESCE(line_number, @line_number)
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@namespace", @namespace ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@full_name", fullName);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@base_type", baseType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@assembly", assembly ?? (object)DBNull.Value);
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
                             string? assembly, string? filePath, int? lineNumber, int? endLine,
                             bool isStatic = false, bool isVirtual = false, 
                             bool isOverride = false, bool isAbstract = false,
                             string? access = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO methods (type_id, name, signature, return_type, assembly, file_path, line_number,
                                end_line, is_static, is_virtual, is_override, is_abstract, access)
            VALUES (@type_id, @name, @signature, @return_type, @assembly, @file_path, @line_number,
                    @end_line, @is_static, @is_virtual, @is_override, @is_abstract, @access)
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@type_id", typeId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@signature", signature);
        cmd.Parameters.AddWithValue("@return_type", returnType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@assembly", assembly ?? (object)DBNull.Value);
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
    // External call writing (calls to Unity, BCL, etc.)
    // =========================================================================
    
    public void InsertExternalCall(long callerId, string? targetAssembly, string targetType, 
                                   string targetMethod, string? targetSignature,
                                   string? filePath, int? lineNumber)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO external_calls (caller_id, target_assembly, target_type, target_method, 
                                       target_signature, file_path, line_number)
            VALUES (@caller_id, @target_assembly, @target_type, @target_method, 
                    @target_signature, @file_path, @line_number)
        ";
        
        cmd.Parameters.AddWithValue("@caller_id", callerId);
        cmd.Parameters.AddWithValue("@target_assembly", targetAssembly ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@target_type", targetType);
        cmd.Parameters.AddWithValue("@target_method", targetMethod);
        cmd.Parameters.AddWithValue("@target_signature", targetSignature ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
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
    // XML Definition writing
    // =========================================================================
    
    public void InsertXmlDefinition(string fileName, string elementType, string? elementName,
                                    string elementXpath, string? propertyName, string? propertyValue,
                                    string? propertyClass, int? lineNumber)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO xml_definitions (file_name, element_type, element_name, element_xpath,
                                         property_name, property_value, property_class, line_number)
            VALUES (@file_name, @element_type, @element_name, @element_xpath,
                    @property_name, @property_value, @property_class, @line_number)
        ";
        
        cmd.Parameters.AddWithValue("@file_name", fileName);
        cmd.Parameters.AddWithValue("@element_type", elementType);
        cmd.Parameters.AddWithValue("@element_name", elementName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@element_xpath", elementXpath);
        cmd.Parameters.AddWithValue("@property_name", propertyName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@property_value", propertyValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@property_class", propertyClass ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    
    // =========================================================================
    // XML Property Access writing
    // =========================================================================
    
    public void InsertXmlPropertyAccess(long methodId, string propertyName, string accessMethod,
                                        string? filePath, int? lineNumber)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO xml_property_access (method_id, property_name, access_method, file_path, line_number)
            VALUES (@method_id, @property_name, @access_method, @file_path, @line_number)
        ";
        
        cmd.Parameters.AddWithValue("@method_id", methodId);
        cmd.Parameters.AddWithValue("@property_name", propertyName);
        cmd.Parameters.AddWithValue("@access_method", accessMethod);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    
    // =========================================================================
    // Mod writing
    // =========================================================================
    
    public long InsertMod(string modName, string? modPath, string? version, string? author)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO mods (name, mod_path, version, author, analyzed_at)
            VALUES (@name, @mod_path, @version, @author, @analyzed_at)
            ON CONFLICT(name) DO UPDATE SET
                mod_path = COALESCE(mod_path, @mod_path),
                version = COALESCE(version, @version),
                author = COALESCE(author, @author),
                analyzed_at = @analyzed_at
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@name", modName);
        cmd.Parameters.AddWithValue("@mod_path", modPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@version", version ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@author", author ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@analyzed_at", DateTime.UtcNow.ToString("o"));
        
        return (long)cmd.ExecuteScalar()!;
    }
    
    public long InsertModType(long modId, string name, string? @namespace, string fullName, 
                              string kind, string? baseType, string? filePath, int? lineNumber)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO mod_types (mod_id, name, namespace, full_name, kind, base_type, file_path, line_number)
            VALUES (@mod_id, @name, @namespace, @full_name, @kind, @base_type, @file_path, @line_number)
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@mod_id", modId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@namespace", @namespace ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@full_name", fullName);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@base_type", baseType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        
        return (long)cmd.ExecuteScalar()!;
    }
    
    public long InsertModMethod(long modTypeId, string name, string signature, string? returnType,
                                string? filePath, int? lineNumber, int? endLine,
                                bool isStatic = false, string? access = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO mod_methods (mod_type_id, name, signature, return_type, file_path, line_number, end_line)
            VALUES (@mod_type_id, @name, @signature, @return_type, @file_path, @line_number, @end_line)
            RETURNING id
        ";
        
        cmd.Parameters.AddWithValue("@mod_type_id", modTypeId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@signature", signature);
        cmd.Parameters.AddWithValue("@return_type", returnType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@end_line", endLine ?? (object)DBNull.Value);
        
        return (long)cmd.ExecuteScalar()!;
    }
    
    public void InsertModMethodBody(long modMethodId, string body)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO mod_method_bodies (mod_method_id, body) VALUES (@mod_method_id, @body)
        ";
        cmd.Parameters.AddWithValue("@mod_method_id", modMethodId.ToString());
        cmd.Parameters.AddWithValue("@body", body);
        cmd.ExecuteNonQuery();
    }
    
    public void InsertHarmonyPatch(long modMethodId, string targetType, string targetMethod,
                                   string patchType, string? filePath, int? lineNumber)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO harmony_patches (mod_method_id, mod_name, target_type, target_method, patch_type, 
                                        file_path, line_number)
            VALUES (@mod_method_id, '', @target_type, @target_method, @patch_type, @file_path, @line_number)
        ";
        
        cmd.Parameters.AddWithValue("@mod_method_id", modMethodId);
        cmd.Parameters.AddWithValue("@target_type", targetType);
        cmd.Parameters.AddWithValue("@target_method", targetMethod);
        cmd.Parameters.AddWithValue("@patch_type", patchType);
        cmd.Parameters.AddWithValue("@file_path", filePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@line_number", lineNumber ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    
    public void InsertXmlChange(long modId, string xmlFile, string xpath, string operation,
                                string? propertyName, string? oldValue, string? newValue)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO xml_changes (mod_id, mod_name, file_name, xpath, operation, value)
            VALUES (@mod_id, '', @file_name, @xpath, @operation, @value)
        ";
        
        cmd.Parameters.AddWithValue("@mod_id", modId);
        cmd.Parameters.AddWithValue("@file_name", xmlFile);
        cmd.Parameters.AddWithValue("@xpath", xpath);
        cmd.Parameters.AddWithValue("@operation", operation);
        cmd.Parameters.AddWithValue("@value", newValue ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
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
