# XmlIndexer - Copilot Instructions

## CRITICAL: Project Location

**This project lives at:** `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer`

**To run commands, ALWAYS use this exact pattern:**
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- <command>
```

## Important Paths

| Path | Purpose |
|------|---------|
| `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2DCodebase` | Decompiled game code (Assembly-CSharp, Data) |
| `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\temp_analysis` | Test mods folder for analysis |
| `C:\Steam\steamapps\common\7 Days To Die` | Game installation |
| `C:\Steam\steamapps\common\7 Days To Die\Mods` | Installed mods (live game) |
| `./reports/` | Report output (relative to XmlIndexer folder) |
| `./ecosystem.db` | Database file (relative to XmlIndexer folder) |

## Most Common Commands

### Generate Full Report (rebuilds everything)
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- report "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2DCodebase" "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\temp_analysis" "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\temp_analysis"
```

### Query Database (ad-hoc SQL)
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- query "./ecosystem.db" "SELECT * FROM mods LIMIT 5"
```

### Show Statistics
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- stats "./ecosystem.db"
```

## All Available Commands

| Command | Usage |
|---------|-------|
| `report` | `report <game> <mods> <output> [--open]` - Full rebuild + HTML report |
| `full-analyze` | `full-analyze <game> <mods> <db>` - Build database only |
| `query` | `query <db> "<sql>"` - Run ad-hoc SQL query |
| `stats` | `stats <db>` - Show database statistics |
| `refs` | `refs <db> <type> <name>` - Find references to entity |
| `list` | `list <db> <type>` - List all entities of type |
| `search` | `search <db> <pattern>` - Search entities by name |
| `detect-harmony-conflicts` | `detect-harmony-conflicts <db>` - Detect Harmony patch conflicts |
| `analyze-game` | `analyze-game <db> <codebase>` - Analyze game code for bugs |
| `build-dependency-graph` | `build-dependency-graph <db>` - Build transitive references |

## Key Database Tables

| Table | Purpose |
|-------|---------|
| `mods` | All discovered mods |
| `mod_xml_operations` | XML operations (set, append, remove) by mods |
| `xml_definitions` | Base game entity definitions |
| `xml_properties` | Properties on entities |
| `xml_references` | Cross-references between entities |
| `mod_csharp_deps` | C# class dependencies from mod DLLs |
| `harmony_patches` | Detailed Harmony patch metadata |
| `harmony_conflicts` | Detected Harmony patch conflicts |
| `game_code_analysis` | Game code bug hunting findings |

## Key Source Files

| File | Purpose |
|------|---------|
| `Program.cs` | CLI entry point, all commands, core logic |
| `Models/DataModels.cs` | All record types and DTOs |
| `Database/DatabaseBuilder.cs` | Schema definition (single source of truth) |
| `Analysis/ReportDataCollector.cs` | SQL queries for report data |
| `Analysis/ModAnalyzer.cs` | Mod XML/DLL analysis, xpath parsing |
| `Analysis/HarmonyConflictDetector.cs` | Harmony patch conflict detection |
| `Analysis/GameCodeAnalyzer.cs` | Game code bug hunting |
| `Utils/CSharpAnalyzer.cs` | C# code analysis, Harmony patch scanning |
| `Reports/SharedAssets.cs` | CSS, HTML helpers, common components |
| `Reports/*PageGenerator.cs` | Individual HTML page generators |

## Code Style

- C# records for immutable data
- Static classes in `XmlIndexer.Reports` namespace
- CSS: Obsidian dark theme, emerald (#3fb950) / amber (#d29922) accents
- JavaScript inlined in HTML via `<script>` tags

## Architecture Flow

1. **Database Build** → Index game XML into SQLite (uses DatabaseBuilder.CreateSchema)
2. **Mod Analysis** → Parse mod XML operations, scan Harmony patches
3. **Report Generation** → Run HarmonyConflictDetector, GameCodeAnalyzer, generate HTML
4. **Output** → Multi-page HTML report with index, entities, mods, conflicts, csharp, gamecode pages
