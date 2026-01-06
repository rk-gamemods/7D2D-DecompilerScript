using Microsoft.Data.Sqlite;

namespace XmlIndexer.Commands;

/// <summary>
/// Displays ecosystem statistics including base game data, mod stats, and cross-mod insights.
/// </summary>
public static class StatsCommand
{
    public static int Execute(string dbPath)
    {
        using var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  7D2D ECOSYSTEM STATISTICS                                       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        // BASE GAME STATS
        Console.WriteLine("📊 BASE GAME DATA");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT definition_type, COUNT(*) as cnt 
                FROM xml_definitions GROUP BY definition_type ORDER BY cnt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var count = reader.GetInt32(1);
                var bar = new string('▓', Math.Min(count / 50, 30));
                Console.WriteLine($"  {type,-20} {count,6}  {bar}");
            }
        }

        Console.WriteLine();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_definitions";
            Console.WriteLine($"  Total Definitions:     {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_properties";
            Console.WriteLine($"  Total Properties:      {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM xml_references";
            Console.WriteLine($"  Total Cross-Refs:      {cmd.ExecuteScalar()}");
        }

        // MOD STATS
        Console.WriteLine("\n\n🔧 MOD STATISTICS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods";
            Console.WriteLine($"  Total Mods:            {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_xml = 1 AND has_dll = 0";
            Console.WriteLine($"  XML-Only Mods:         {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_dll = 1 AND has_xml = 0";
            Console.WriteLine($"  C#-Only Mods:          {cmd.ExecuteScalar()}");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE has_xml = 1 AND has_dll = 1";
            Console.WriteLine($"  Hybrid Mods:           {cmd.ExecuteScalar()}");
        }

        Console.WriteLine("\n  Operations by Type:");
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT operation, COUNT(*) FROM mod_xml_operations 
                GROUP BY operation ORDER BY COUNT(*) DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"    {reader.GetString(0),-18} {reader.GetInt32(1),6}");
            }
        }

        // FUN FACTS
        Console.WriteLine("\n\n🎮 FUN FACTS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name FROM xml_definitions 
                WHERE definition_type = 'item' ORDER BY LENGTH(name) DESC LIMIT 1";
            Console.WriteLine($"  Longest Item Name:     {cmd.ExecuteScalar()}");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_name, COUNT(*) as refs FROM xml_references 
                WHERE target_type = 'item' GROUP BY target_name ORDER BY refs DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                Console.WriteLine($"  Most Referenced Item:  {reader.GetString(0)} ({reader.GetInt32(1)} refs)");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name FROM xml_definitions 
                WHERE definition_type = 'block' AND name LIKE '%zombie%' LIMIT 1";
            var zombieBlock = cmd.ExecuteScalar();
            if (zombieBlock != null)
                Console.WriteLine($"  A Zombie Block:        {zombieBlock}");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(DISTINCT extends) FROM xml_definitions WHERE extends IS NOT NULL";
            Console.WriteLine($"  Unique Parent Classes: {cmd.ExecuteScalar()}");
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT name, COUNT(*) as props FROM xml_properties 
                JOIN xml_definitions ON xml_properties.definition_id = xml_definitions.id
                GROUP BY xml_properties.definition_id ORDER BY props DESC LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                Console.WriteLine($"  Most Complex Entity:   {reader.GetString(0)} ({reader.GetInt32(1)} props)");
        }

        // CROSS-MOD INSIGHTS
        Console.WriteLine("\n\n⚡ CROSS-MOD INSIGHTS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_name, COUNT(DISTINCT mod_id) as mod_count
                FROM mod_xml_operations 
                WHERE target_name IS NOT NULL
                GROUP BY target_type, target_name 
                HAVING mod_count > 1
                ORDER BY mod_count DESC LIMIT 5";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("  Most Contested Entities (modified by multiple mods):");
            while (reader.Read())
            {
                Console.WriteLine($"    • {reader.GetString(0)}: {reader.GetInt32(1)} mods");
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.name, COUNT(*) as deps
                FROM mod_csharp_deps mcd
                JOIN mods m ON mcd.mod_id = m.id
                GROUP BY m.id ORDER BY deps DESC LIMIT 3";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n  C# Mods with Most XML Dependencies:");
            while (reader.Read())
            {
                Console.WriteLine($"    • {reader.GetString(0)}: {reader.GetInt32(1)} deps");
            }
        }

        Console.WriteLine();
        return 0;
    }
}
