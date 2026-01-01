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

        // TODO: Implement in subsequent commits
        // 1. Initialize SQLite database with schema
        // 2. Create Roslyn compilation
        // 3. Parse all files, extract methods
        // 4. Extract call relationships
        // 5. Write to database

        Console.WriteLine();
        Console.WriteLine("Extraction complete!");
    }
}
