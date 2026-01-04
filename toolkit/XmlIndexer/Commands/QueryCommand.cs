using Microsoft.Data.Sqlite;

namespace XmlIndexer.Commands;

/// <summary>
/// Executes arbitrary SQL queries against the ecosystem database.
/// </summary>
public static class QueryCommand
{
    public static int Execute(string dbPath, string sql)
    {
        using var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();

        Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  SQL QUERY RESULTS                                               ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝\n");
        Console.WriteLine($"Query: {sql}\n");

        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();

            // Get column names
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            // Read all rows into memory to calculate column widths
            var rows = new List<string[]>();
            while (reader.Read())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = reader.IsDBNull(i) ? "(null)" : reader.GetValue(i)?.ToString() ?? "";
                }
                rows.Add(values);
                if (rows.Count >= 100) break;
            }

            // Calculate column widths (min 10, max 60)
            var widths = new int[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                widths[i] = Math.Max(columns[i].Length, 10);
                foreach (var row in rows)
                {
                    widths[i] = Math.Max(widths[i], Math.Min(row[i].Length, 60));
                }
                widths[i] = Math.Min(widths[i], 60);
            }

            // Print header
            Console.WriteLine(string.Join(" | ", columns.Select((c, i) => c.PadRight(widths[i]))));
            Console.WriteLine(string.Join("─┼─", widths.Select(w => new string('─', w))));

            // Print rows
            foreach (var row in rows)
            {
                var line = string.Join(" | ", row.Select((v, i) => 
                    v.Length > widths[i] ? v.Substring(0, widths[i] - 3) + "..." : v.PadRight(widths[i])));
                Console.WriteLine(line);
            }

            Console.WriteLine($"\n{rows.Count} row(s) returned" + (rows.Count >= 100 ? " (limit 100)" : ""));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Query error: {ex.Message}");
            return 1;
        }

        return 0;
    }
}
