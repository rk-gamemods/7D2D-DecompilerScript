using Microsoft.Data.Sqlite;

namespace XmlIndexer.Commands;

/// <summary>
/// Displays ecosystem health check - overview of base game entities, mod impacts, and conflicts.
/// </summary>
public static class EcosystemCommand
{
    public static int Execute(string dbPath)
    {
        using var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  7D2D ECOSYSTEM HEALTH CHECK                                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");

        // Overall health
        int active = 0, modified = 0, removed = 0, depended = 0;
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE status = 'active'";
            active = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE modified_by IS NOT NULL";
            modified = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE status = 'removed'";
            removed = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ecosystem_entities WHERE depended_on_by IS NOT NULL";
            depended = Convert.ToInt32(cmd.ExecuteScalar());
        }

        Console.WriteLine("📊 ECOSYSTEM OVERVIEW");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Active Entities:       {active,6}  (base game entities still in play)");
        Console.WriteLine($"  Modified by Mods:      {modified,6}  (entities tweaked by XML mods)");
        Console.WriteLine($"  Removed by Mods:       {removed,6}  (entities deleted by mods)");
        Console.WriteLine($"  C# Dependencies:       {depended,6}  (entities needed by code mods)");

        // Danger zone: removed entities that are depended upon
        Console.WriteLine("\n\n⚠️  DANGER ZONE - Potential Breaking Conflicts");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT entity_type, entity_name, removed_by, depended_on_by
                FROM ecosystem_entities
                WHERE status = 'removed' AND depended_on_by IS NOT NULL
                LIMIT 10";
            using var reader = cmd.ExecuteReader();
            int dangerCount = 0;
            while (reader.Read())
            {
                dangerCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  🔥 {reader.GetString(0)}: {reader.GetString(1)}");
                Console.ResetColor();
                Console.WriteLine($"     Removed by:    {reader.GetString(2)}");
                Console.WriteLine($"     Needed by:     {reader.GetString(3)}");
            }
            if (dangerCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✓ No critical conflicts detected!");
                Console.ResetColor();
            }
        }

        // Mod impact summary
        Console.WriteLine("\n\n📦 MOD IMPACT SUMMARY");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.name, m.xml_operations, m.csharp_dependencies, m.conflicts, m.cautions,
                (SELECT COUNT(*) FROM mod_xml_operations WHERE mod_id = m.id AND operation = 'remove') as removes
                FROM mods m
                ORDER BY m.conflicts DESC, m.cautions DESC, m.xml_operations DESC
                LIMIT 15";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine($"  {"Mod Name",-35} {"Ops",5} {"Deps",5} {"Rem",4} {"Status",8}");
            Console.WriteLine($"  {new string('-', 35)} {new string('-', 5)} {new string('-', 5)} {new string('-', 4)} {new string('-', 8)}");
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (name.Length > 35) name = name[..32] + "...";
                var ops = reader.GetInt32(1);
                var deps = reader.GetInt32(2);
                var conflicts = reader.GetInt32(3);
                var removes = reader.GetInt32(5);
                
                string status;
                ConsoleColor color;
                if (conflicts > 0) { status = "CONFLICT"; color = ConsoleColor.Red; }
                else if (removes > 0) { status = "REMOVES"; color = ConsoleColor.Yellow; }
                else if (deps > 0) { status = "C#"; color = ConsoleColor.Cyan; }
                else if (ops > 0) { status = "OK"; color = ConsoleColor.Green; }
                else { status = "PASSIVE"; color = ConsoleColor.DarkGray; }

                Console.Write($"  {name,-35} {ops,5} {deps,5} {removes,4} ");
                Console.ForegroundColor = color;
                Console.WriteLine($"{status,8}");
                Console.ResetColor();
            }
        }

        // Predict new mod compatibility
        Console.WriteLine("\n\n🔮 COMPATIBILITY TIPS FOR NEW MODS");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine("  Based on current ecosystem analysis:");
        Console.WriteLine();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT entity_type, COUNT(*) as cnt
                FROM ecosystem_entities WHERE status = 'removed'
                GROUP BY entity_type ORDER BY cnt DESC LIMIT 3";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("  ⚠️  Types with most removals (avoid depending on these):");
            while (reader.Read())
            {
                Console.WriteLine($"      • {reader.GetString(0)}: {reader.GetInt32(1)} removed");
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT target_type, target_name, COUNT(*) as cnt
                FROM mod_xml_operations
                WHERE target_name IS NOT NULL
                GROUP BY target_type, target_name
                ORDER BY cnt DESC LIMIT 3";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n  🎯 Most modified entities (high conflict risk if you touch these):");
            while (reader.Read())
            {
                Console.WriteLine($"      • {reader.GetString(0)}/{reader.GetString(1)}: {reader.GetInt32(2)} mods");
            }
        }

        Console.WriteLine("\n  💡 Safe zones: Entities with no current mod activity are safest to extend");
        Console.WriteLine();

        return 0;
    }
}
