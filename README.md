# 7D2D Mod Ecosystem Analyzer

Analyze your 7 Days to Die mod collection, detect conflicts, and generate detailed compatibility reports. One command does everything.

## Quick Start (2 Steps)

### 1. Clone & Configure

```powershell
git clone https://github.com/rk-gamemods/7D2D-DecompilerScript.git
cd 7D2D-DecompilerScript

# Copy the example config and edit with your paths
copy config.example.txt config.txt
notepad config.txt
```

Edit `config.txt` with your game path:
```ini
GAME_PATH=C:\Steam\steamapps\common\7 Days To Die
MODS_PATH=C:\Steam\steamapps\common\7 Days To Die\Mods
```

### 2. Run Analysis

```cmd
RunAnalysis.bat
```

That's it! The report opens automatically in your browser.

---

## What You Get

An interactive HTML report showing:
- **All mods** and what they modify
- **Conflicts** between mods (XML and Harmony patches)  
- **C# mod analysis** with Harmony patch detection
- **Entity browser** (items, blocks, buffs with cross-references)
- **Dependency graph** showing what depends on what
- **Game code references** (if decompiled code is available)

---

## What `RunAnalysis.bat` Does

The batch file automates the entire workflow:

```
1. Load config.txt          → Get game/mods paths
2. Validate paths           → Ensure game installation exists
3. Build toolkit (once)     → Compile XmlIndexer if needed
4. Run analysis             → Index game XML, analyze mods, detect conflicts
5. Generate HTML report     → Multi-page interactive report
6. Open in browser          → View results immediately
```

**Output location:** `./output/ecosystem_YYYY-MM-DD_HHMM/`

---

## Requirements

- **Windows 10/11**
- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **7 Days to Die** game installation

Verify .NET is installed:
```cmd
dotnet --version
```

---

## Optional: Game Code Analysis

For deeper analysis including the **Game Code** page in reports, extract the decompiled game code first:

```powershell
.\Decompile-7D2D.ps1
```

Then add to `config.txt`:
```ini
CODEBASE_PATH=C:\path\to\7D2D-DecompilerScript\7D2DCodebase
```

This enables:
- Classes and methods referenced by mods
- Inheritance hierarchies
- Method signatures for Harmony patches

---

## Advanced Usage

### Manual CLI Commands

If you prefer running commands individually:

```powershell
cd toolkit/XmlIndexer

# Full workflow (build database + analyze + detect conflicts)
dotnet run -- full-analyze <game_path> <mods_path> <database.db>

# Generate report only
dotnet run -- report <game_path> <mods_path> <output_dir> [--open]

# Query the database
dotnet run -- refs <db> item gunPistol          # Find references
dotnet run -- search <db> "*medical*"           # Search by pattern
dotnet run -- query <db> "SELECT * FROM mods"   # Custom SQL
```

### Call Graph Analysis

For deep code analysis (find callers, trace execution paths):

```powershell
# Build call graph database
cd toolkit/CallGraphExtractor
dotnet run -c Release -- --source "../../7D2DCodebase" --output "../callgraph_full.db" --game-root "C:\...\7 Days To Die"

# Query it
cd ../QueryDb
dotnet run -- ../callgraph_full.db callers "Bag.DecItem"
dotnet run -- ../callgraph_full.db search "GetComponent"
```

---

## Documentation

| Document | Contents |
|----------|----------|
| [toolkit/XmlIndexer/USER_GUIDE.md](toolkit/XmlIndexer/USER_GUIDE.md) | Complete CLI reference |
| [TOOLKIT_DESIGN.md](TOOLKIT_DESIGN.md) | Technical design and SQL examples |
| [AI_CONTEXT.md](AI_CONTEXT.md) | Guide for AI assistants |

---

## Troubleshooting

### "config.txt not found"
Copy the example and edit it:
```cmd
copy config.example.txt config.txt
notepad config.txt
```

### "Game path invalid"
Verify your game path has a `Data\Config` folder:
```cmd
dir "C:\Steam\steamapps\common\7 Days To Die\Data\Config"
```

### ".NET SDK not found"
Install .NET 8.0 SDK from https://dotnet.microsoft.com/download/dotnet/8.0

### "Build failed"
Run manually to see detailed errors:
```cmd
cd toolkit\XmlIndexer
dotnet build -c Release
```

---

## License

MIT License - Use freely for your modding projects.

---

## Related Projects

- [ProxiCraft](https://github.com/rk-gamemods/ProxiCraft) — Craft from nearby containers
- [AudibleBreakingGlassJars](https://github.com/rk-gamemods/AudibleBreakingGlassJars) — Sound when glass jars break
