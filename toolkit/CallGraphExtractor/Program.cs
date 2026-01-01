using System.CommandLine;

namespace CallGraphExtractor;

/// <summary>
/// Call Graph Extractor - Parses C# source files using Roslyn and extracts
/// method definitions and call relationships into a SQLite database.
/// 
/// Philosophy: Use Everything, Optimize Nothing
/// This extraction runs once per game update (1-2x/month at most).
/// Runtime is irrelevant - completeness is the priority.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Extract call graph from C# source files into SQLite database");

        // Source directory option (can be decompiled codebase root for auto-discovery)
        var sourceOption = new Option<DirectoryInfo>(
            aliases: ["--source", "-s"],
            description: "Path to decompiled source code directory (auto-discovers Assembly-* subdirectories)")
        {
            IsRequired = true
        };

        // Output database option
        var outputOption = new Option<FileInfo>(
            aliases: ["--output", "-o"],
            description: "Path to the output SQLite database file")
        {
            IsRequired = true
        };

        // Game root option (for loading ALL DLLs as metadata references)
        var gameRootOption = new Option<DirectoryInfo?>(
            aliases: ["--game-root", "-g"],
            description: "Path to game installation (recursively loads ALL DLLs for type resolution)");

        // Verbose output
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output (show skipped DLLs, compilation errors, etc.)");

        rootCommand.AddOption(sourceOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(gameRootOption);
        rootCommand.AddOption(verboseOption);

        rootCommand.SetHandler(async (source, output, gameRoot, verbose) =>
        {
            await RunExtraction(source, output, gameRoot, verbose);
        }, sourceOption, outputOption, gameRootOption, verboseOption);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunExtraction(DirectoryInfo source, FileInfo output, DirectoryInfo? gameRoot, bool verbose)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("  7D2D Call Graph Extractor");
        Console.WriteLine("  Philosophy: Use Everything, Optimize Nothing");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine($"Source:    {source.FullName}");
        Console.WriteLine($"Output:    {output.FullName}");
        if (gameRoot != null)
            Console.WriteLine($"Game Root: {gameRoot.FullName}");
        Console.WriteLine();

        // Validate source directory
        if (!source.Exists)
        {
            Console.Error.WriteLine($"Error: Source directory does not exist: {source.FullName}");
            return;
        }

        // Initialize parser
        var parser = new RoslynParser(verbose);
        
        // Step 1: Auto-discover source directories
        Console.WriteLine("Step 1: Discovering source directories");
        Console.WriteLine("───────────────────────────────────────");
        parser.AutoDiscoverSourceDirectories(source.FullName);
        
        if (!parser.SourceDirectories.Any())
        {
            Console.Error.WriteLine("Error: No source directories found!");
            return;
        }
        Console.WriteLine();

        // Step 2: Load ALL metadata references
        Console.WriteLine("Step 2: Loading metadata references");
        Console.WriteLine("───────────────────────────────────────");
        parser.LoadReferences(gameRoot?.FullName);
        Console.WriteLine();

        // Step 3: Delete existing database if present
        if (output.Exists)
        {
            Console.WriteLine($"Removing existing database: {output.FullName}");
            output.Delete();
        }

        // Step 4: Initialize database
        Console.WriteLine("Step 3: Initializing database");
        Console.WriteLine("───────────────────────────────────────");
        using var db = new SqliteWriter(output.FullName);
        db.Initialize();
        
        // Store metadata
        db.SetMetadata("source_path", source.FullName);
        db.SetMetadata("game_root", gameRoot?.FullName ?? "");
        db.SetMetadata("build_timestamp", DateTime.UtcNow.ToString("o"));
        db.SetMetadata("source_directories", string.Join(";", parser.SourceDirectories.Select(s => s.AssemblyName)));
        
        Console.WriteLine("Database initialized with schema.");
        Console.WriteLine();

        // Step 5: Parse and extract types/methods
        Console.WriteLine("Step 4: Parsing source files and extracting types/methods");
        Console.WriteLine("───────────────────────────────────────");
        var parseStart = stopwatch.Elapsed;
        parser.ExtractToDatabase(db);
        var parseTime = stopwatch.Elapsed - parseStart;
        Console.WriteLine();
        
        // Step 6: Extract call relationships
        Console.WriteLine("Step 5: Extracting call relationships");
        Console.WriteLine("───────────────────────────────────────");
        var callStart = stopwatch.Elapsed;
        var callExtractor = new CallExtractor(
            parser.Compilation!,
            parser.SymbolToId,
            parser.SignatureToMethodId,
            verbose
        );
        
        // Pass all source directories for relative path calculation
        var sourcePaths = parser.SourceDirectories.Select(s => s.Path).ToList();
        callExtractor.ExtractCalls(db, sourcePaths);
        var callTime = stopwatch.Elapsed - callStart;
        
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("  Extraction Complete!");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Parse time:      {parseTime.TotalSeconds:F1}s");
        Console.WriteLine($"  Call extraction: {callTime.TotalSeconds:F1}s");
        Console.WriteLine($"  Total time:      {stopwatch.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine();
        Console.WriteLine($"  Database: {output.FullName}");
        Console.WriteLine();
        
        // Print resolution statistics
        var totalCalls = callExtractor.CallCount + callExtractor.ExternalCallCount + callExtractor.UnresolvedCount;
        var resolvedPct = 100.0 * (callExtractor.CallCount + callExtractor.ExternalCallCount) / Math.Max(1, totalCalls);
        Console.WriteLine($"  Call Resolution: {resolvedPct:F1}% ({callExtractor.CallCount + callExtractor.ExternalCallCount:N0} resolved, {callExtractor.UnresolvedCount:N0} unresolved)");
        Console.WriteLine($"    Internal calls: {callExtractor.CallCount:N0}");
        Console.WriteLine($"    External calls: {callExtractor.ExternalCallCount:N0} (Unity, BCL, third-party)");
        Console.WriteLine();
    }
}
