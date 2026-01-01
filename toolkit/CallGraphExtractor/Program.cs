using System.CommandLine;

namespace CallGraphExtractor;

/// <summary>
/// Call Graph Extractor - Parses C# source files using Roslyn and extracts
/// method definitions and call relationships into a SQLite database.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Extract call graph from C# source files into SQLite database");

        // Source directory option
        var sourceOption = new Option<DirectoryInfo>(
            aliases: ["--source", "-s"],
            description: "Path to the source code directory to analyze")
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

        // Reference assemblies option (for Roslyn type resolution)
        var refsOption = new Option<DirectoryInfo?>(
            aliases: ["--refs", "-r"],
            description: "Path to reference DLLs for type resolution (optional)");

        // Verbose output
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        rootCommand.AddOption(sourceOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(refsOption);
        rootCommand.AddOption(verboseOption);

        rootCommand.SetHandler(async (source, output, refs, verbose) =>
        {
            await RunExtraction(source, output, refs, verbose);
        }, sourceOption, outputOption, refsOption, verboseOption);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunExtraction(DirectoryInfo source, FileInfo output, DirectoryInfo? refs, bool verbose)
    {
        Console.WriteLine($"Call Graph Extractor");
        Console.WriteLine($"====================");
        Console.WriteLine($"Source: {source.FullName}");
        Console.WriteLine($"Output: {output.FullName}");
        if (refs != null)
            Console.WriteLine($"Refs:   {refs.FullName}");
        Console.WriteLine();

        // Validate source directory
        if (!source.Exists)
        {
            Console.Error.WriteLine($"Error: Source directory does not exist: {source.FullName}");
            return;
        }

        // Count source files
        var csFiles = source.GetFiles("*.cs", SearchOption.AllDirectories);
        Console.WriteLine($"Found {csFiles.Length} C# files to analyze");

        // Delete existing database if present
        if (output.Exists)
        {
            Console.WriteLine($"Removing existing database: {output.FullName}");
            output.Delete();
        }

        // Initialize database
        Console.WriteLine("Initializing database...");
        using var db = new SqliteWriter(output.FullName);
        db.Initialize();
        
        // Store metadata
        db.SetMetadata("source_path", source.FullName);
        db.SetMetadata("build_timestamp", DateTime.UtcNow.ToString("o"));
        db.SetMetadata("file_count", csFiles.Length.ToString());
        
        Console.WriteLine("Database initialized with schema.");
        Console.WriteLine();

        // Parse with Roslyn and extract types/methods
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var parser = new RoslynParser(verbose, refs?.FullName);
        parser.ExtractToDatabase(source.FullName, db);
        var parseTime = stopwatch.Elapsed;
        
        // Extract call relationships
        Console.WriteLine();
        stopwatch.Restart();
        var callExtractor = new CallExtractor(
            parser.Compilation!,
            parser.SymbolToId,
            parser.SignatureToMethodId,
            verbose
        );
        callExtractor.ExtractCalls(db, source.FullName);
        var callTime = stopwatch.Elapsed;
        
        Console.WriteLine();
        Console.WriteLine($"Parse time: {parseTime.TotalSeconds:F1}s, Call extraction: {callTime.TotalSeconds:F1}s");
        Console.WriteLine("Extraction complete!");
    }
}
