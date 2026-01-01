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

        // Game data config option (for XML extraction)
        var gameDataOption = new Option<DirectoryInfo?>(
            aliases: ["--game-data", "-d"],
            description: "Path to game Data/Config folder (for XML definition extraction)");

        // Mods directory option (for parsing mod code and XML changes)
        var modsOption = new Option<DirectoryInfo[]?>(
            aliases: ["--mods", "-m"],
            description: "Paths to mod directories or Mods folders to parse (can specify multiple)")
        {
            AllowMultipleArgumentsPerToken = true
        };

        // Verbose output
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output (show skipped DLLs, compilation errors, etc.)");

        rootCommand.AddOption(sourceOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(gameRootOption);
        rootCommand.AddOption(gameDataOption);
        rootCommand.AddOption(modsOption);
        rootCommand.AddOption(verboseOption);

        rootCommand.SetHandler(async (source, output, gameRoot, gameData, mods, verbose) =>
        {
            await RunExtraction(source, output, gameRoot, gameData, mods, verbose);
        }, sourceOption, outputOption, gameRootOption, gameDataOption, modsOption, verboseOption);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunExtraction(DirectoryInfo source, FileInfo output, DirectoryInfo? gameRoot, 
                                     DirectoryInfo? gameData, DirectoryInfo[]? mods, bool verbose)
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
        if (gameData != null)
            Console.WriteLine($"Game Data: {gameData.FullName}");
        Console.WriteLine();

        // Validate source directory
        if (!source.Exists)
        {
            Console.Error.WriteLine($"Error: Source directory does not exist: {source.FullName}");
            return;
        }

        // Auto-detect game data if game root provided
        if (gameData == null && gameRoot != null)
        {
            var autoDataPath = Path.Combine(gameRoot.FullName, "Data", "Config");
            if (Directory.Exists(autoDataPath))
            {
                gameData = new DirectoryInfo(autoDataPath);
                Console.WriteLine($"Auto-detected game data: {gameData.FullName}");
                Console.WriteLine();
            }
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
        db.SetMetadata("game_data", gameData?.FullName ?? "");
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
        
        // Step 7: Extract XML definitions (if game data provided)
        int xmlDefinitionCount = 0;
        if (gameData != null && gameData.Exists)
        {
            Console.WriteLine("Step 6: Extracting XML definitions");
            Console.WriteLine("───────────────────────────────────────");
            var xmlStart = stopwatch.Elapsed;
            var xmlExtractor = new XmlDefinitionExtractor(verbose);
            xmlExtractor.ExtractFromGameData(gameData.FullName, db);
            xmlDefinitionCount = xmlExtractor.DefinitionCount;
            var xmlTime = stopwatch.Elapsed - xmlStart;
            Console.WriteLine($"  Extracted in {xmlTime.TotalSeconds:F1}s");
            Console.WriteLine();
        }
        
        // Step 8: Extract XML property access patterns from code
        Console.WriteLine("Step 7: Extracting XML property access patterns");
        Console.WriteLine("───────────────────────────────────────");
        var propAccessStart = stopwatch.Elapsed;
        var propAccessExtractor = new XmlPropertyAccessOrchestrator(
            parser.Compilation!,
            parser.MethodSymbolToId,
            db,
            verbose
        );
        propAccessExtractor.ExtractAll(sourcePaths);
        var propAccessTime = stopwatch.Elapsed - propAccessStart;
        Console.WriteLine($"  Extracted in {propAccessTime.TotalSeconds:F1}s");
        Console.WriteLine();
        
        // Step 9: Extract event subscriptions and fires (for behavioral flow analysis)
        Console.WriteLine("Step 8: Extracting event flows");
        Console.WriteLine("───────────────────────────────────────");
        var eventStart = stopwatch.Elapsed;
        var eventExtractor = new EventFlowExtractor(db, verbose);
        
        foreach (var sourceDir in parser.SourceDirectories)
        {
            Console.WriteLine($"  Scanning: {sourceDir.AssemblyName}");
            foreach (var syntaxTree in parser.Compilation!.SyntaxTrees)
            {
                var filePath = syntaxTree.FilePath;
                if (!filePath.StartsWith(sourceDir.Path)) continue;
                
                var relativePath = Path.GetRelativePath(sourceDir.Path, filePath);
                var semanticModel = parser.Compilation.GetSemanticModel(syntaxTree);
                eventExtractor.ExtractFromTree(syntaxTree, semanticModel, relativePath, isMod: false);
            }
        }
        eventExtractor.PrintSummary();
        var eventTime = stopwatch.Elapsed - eventStart;
        Console.WriteLine($"  Extracted in {eventTime.TotalSeconds:F1}s");
        Console.WriteLine();
        
        // Step 10: Parse mods (if provided)
        int modCount = 0, modPatchCount = 0, modXmlChangeCount = 0;
        if (mods != null && mods.Length > 0)
        {
            Console.WriteLine("Step 9: Parsing mods");
            Console.WriteLine("───────────────────────────────────────");
            var modStart = stopwatch.Elapsed;
            var modParser = new ModParser(db, verbose);
            var xmlChangeParser = new ModXmlChangeParser(db, verbose);
            
            foreach (var modPath in mods)
            {
                if (!modPath.Exists)
                {
                    Console.WriteLine($"  Warning: Mod path not found: {modPath.FullName}");
                    continue;
                }
                
                // Check if this is a single mod or a Mods directory
                if (IsModDirectory(modPath.FullName))
                {
                    modParser.ParseMod(modPath.FullName);
                    xmlChangeParser.ParseModXmlChanges(modPath.FullName, 0); // Will need mod ID
                }
                else
                {
                    modParser.ParseModsDirectory(modPath.FullName);
                    // Parse XML changes for each mod
                    foreach (var modDir in Directory.GetDirectories(modPath.FullName))
                    {
                        if (IsModDirectory(modDir))
                        {
                            // We'd need mod ID here, but for now just parse
                            // xmlChangeParser.ParseModXmlChanges(modDir, modId);
                        }
                    }
                }
            }
            
            modCount = modParser.ModCount;
            modPatchCount = modParser.PatchCount;
            modXmlChangeCount = xmlChangeParser.ChangeCount;
            
            var modTime = stopwatch.Elapsed - modStart;
            Console.WriteLine($"  Parsed in {modTime.TotalSeconds:F1}s");
            Console.WriteLine();
        }
        
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
        if (xmlDefinitionCount > 0)
        {
            Console.WriteLine($"    XML definitions: {xmlDefinitionCount:N0}");
        }
        Console.WriteLine($"    XML property accesses: {propAccessExtractor.TotalAccessCount:N0}");
        Console.WriteLine($"    Event declarations: {eventExtractor.DeclarationCount:N0}");
        Console.WriteLine($"    Event subscriptions: {eventExtractor.SubscriptionCount:N0}");
        Console.WriteLine($"    Event fires: {eventExtractor.FireCount:N0}");
        if (modCount > 0)
        {
            Console.WriteLine($"    Mods parsed: {modCount}");
            Console.WriteLine($"    Harmony patches: {modPatchCount}");
            Console.WriteLine($"    XML changes: {modXmlChangeCount}");
        }
        Console.WriteLine();
    }
    
    /// <summary>
    /// Check if a directory is a mod (has ModInfo.xml or C# files).
    /// </summary>
    private static bool IsModDirectory(string path)
    {
        if (File.Exists(Path.Combine(path, "ModInfo.xml")))
            return true;
        if (Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Any())
            return true;
        var configDir = Path.Combine(path, "Config");
        if (Directory.Exists(configDir) && Directory.GetFiles(configDir, "*.xml").Any())
            return true;
        return false;
    }
}
