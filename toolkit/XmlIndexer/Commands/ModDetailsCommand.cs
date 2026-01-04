using Microsoft.Data.Sqlite;

namespace XmlIndexer.Commands;

/// <summary>
/// Shows detailed information about a specific mod including XML operations and C# dependencies.
/// </summary>
public static class ModDetailsCommand
{
    public static int Execute(string dbPath, string modPattern)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  MOD DETAILS: {modPattern,-50} ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        using var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();

        // Find the mod
        using var modCmd = db.CreateCommand();
        modCmd.CommandText = @"SELECT id, name, has_xml, has_dll, xml_operations, csharp_dependencies, 
            display_name, description, author, version, website 
            FROM mods WHERE name LIKE $pattern";
        modCmd.Parameters.AddWithValue("$pattern", $"%{modPattern}%");

        using var modReader = modCmd.ExecuteReader();
        if (!modReader.Read())
        {
            Console.WriteLine($"  No mod found matching '{modPattern}'");
            return 1;
        }

        var modId = modReader.GetInt32(0);
        var modName = modReader.GetString(1);
        var hasXml = modReader.GetInt32(2) == 1;
        var hasDll = modReader.GetInt32(3) == 1;
        var xmlOps = modReader.GetInt32(4);
        var csharpDeps = modReader.GetInt32(5);
        var displayName = modReader.IsDBNull(6) ? null : modReader.GetString(6);
        var description = modReader.IsDBNull(7) ? null : modReader.GetString(7);
        var author = modReader.IsDBNull(8) ? null : modReader.GetString(8);
        var version = modReader.IsDBNull(9) ? null : modReader.GetString(9);
        modReader.Close();

        Console.WriteLine($"  Name:         {modName}");
        if (displayName != null) Console.WriteLine($"  Display Name: {displayName}");
        if (author != null) Console.WriteLine($"  Author:       {author}");
        if (version != null) Console.WriteLine($"  Version:      {version}");
        if (description != null) Console.WriteLine($"  Description:  {description}");
        Console.WriteLine($"  Type:         {(hasXml && hasDll ? "Hybrid" : hasXml ? "XML-Only" : hasDll ? "C#-Only" : "Assets")}");
        Console.WriteLine($"  XML Ops:      {xmlOps}");
        Console.WriteLine($"  C# Deps:      {csharpDeps}");

        // Show XML operations
        if (xmlOps > 0)
        {
            Console.WriteLine("\n  ═══ XML OPERATIONS ═══════════════════════════════════════════════");
            using var opCmd = db.CreateCommand();
            opCmd.CommandText = @"SELECT operation, xpath, target_type, target_name, property_name, new_value, element_content, file_path, line_number 
                FROM mod_xml_operations WHERE mod_id = $modId ORDER BY file_path, line_number";
            opCmd.Parameters.AddWithValue("$modId", modId);

            using var opReader = opCmd.ExecuteReader();
            while (opReader.Read())
            {
                var op = opReader.GetString(0);
                var xpath = opReader.GetString(1);
                var targetType = opReader.IsDBNull(2) ? "?" : opReader.GetString(2);
                var targetName = opReader.IsDBNull(3) ? "?" : opReader.GetString(3);
                var propName = opReader.IsDBNull(4) ? null : opReader.GetString(4);
                var newValue = opReader.IsDBNull(5) ? null : opReader.GetString(5);
                var content = opReader.IsDBNull(6) ? null : opReader.GetString(6);
                var file = opReader.IsDBNull(7) ? "" : opReader.GetString(7);
                var line = opReader.IsDBNull(8) ? 0 : opReader.GetInt32(8);

                Console.WriteLine($"\n  [{op.ToUpper()}] {targetType}/{targetName}");
                Console.WriteLine($"  XPath: {xpath}");
                if (propName != null) Console.WriteLine($"  Property: {propName} = {newValue}");
                if (content != null)
                {
                    Console.WriteLine($"  Content:");
                    foreach (var contentLine in content.Split('\n').Take(20))
                        Console.WriteLine($"    {contentLine.TrimEnd()}");
                    if (content.Split('\n').Length > 20)
                        Console.WriteLine($"    ... ({content.Split('\n').Length - 20} more lines)");
                }
                Console.WriteLine($"  Source: {file}:{line}");
            }
        }

        // Show C# dependencies
        if (csharpDeps > 0)
        {
            Console.WriteLine("\n  ═══ C# DEPENDENCIES ══════════════════════════════════════════════");
            using var depCmd = db.CreateCommand();
            depCmd.CommandText = @"SELECT dependency_type, dependency_name, source_file, line_number 
                FROM mod_csharp_deps WHERE mod_id = $modId ORDER BY dependency_type, dependency_name";
            depCmd.Parameters.AddWithValue("$modId", modId);

            using var depReader = depCmd.ExecuteReader();
            var lastType = "";
            while (depReader.Read())
            {
                var depType = depReader.GetString(0);
                var depName = depReader.GetString(1);
                var srcFile = depReader.IsDBNull(2) ? "" : depReader.GetString(2);
                var srcLine = depReader.IsDBNull(3) ? 0 : depReader.GetInt32(3);

                if (depType != lastType)
                {
                    Console.WriteLine($"\n  [{depType}]");
                    lastType = depType;
                }
                Console.WriteLine($"    {depName}");
                if (!string.IsNullOrEmpty(srcFile))
                    Console.WriteLine($"      @ {srcFile}:{srcLine}");
            }
        }

        return 0;
    }
}
