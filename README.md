# 7D2D Decompiler Script & Mod Ecosystem Analyzer

A comprehensive toolkit for 7 Days to Die mod developers. Extract game code, analyze mod ecosystems, detect conflicts, and generate detailed compatibility reports.

## What This Toolkit Does

| Tool | Purpose |
|------|---------|
| **Decompiler** | Extract game C# code and XML configs, track changes between updates |
| **XmlIndexer** | Analyze mods, detect conflicts, generate interactive HTML reports |
| **CallGraphExtractor** | Build call graph database for code analysis |
| **QueryDb** | Query game code relationships and find callers/callees |

## Quick Start

### 1. Extract Game Code (One-Time Setup)

```powershell
git clone https://github.com/rk-gamemods/7D2D-DecompilerScript.git
cd 7D2D-DecompilerScript
.\Decompile-7D2D.ps1
```

This extracts decompiled C# and XML configs to `7D2DCodebase/`.

### 2. Analyze Your Mods & Generate Report

```powershell
cd toolkit/XmlIndexer
dotnet run -- report "C:\Steam\steamapps\common\7 Days To Die" "C:\...\Mods" ./reports --open
```

This generates an interactive HTML report showing:
- All mods and what they modify
- Conflicts between mods (XML and Harmony patches)
- Game code references and dependencies
- Entity cross-references (items → buffs → recipes)

---

## Mod Ecosystem Analyzer (XmlIndexer)

The main tool for mod developers. Analyzes your entire mod collection and generates detailed reports.

### Features

- **Conflict Detection** — Find mods that modify the same XML paths or patch the same methods
- **Harmony Patch Analysis** — Detect C# mods and their Harmony patches
- **Dependency Graph** — See what depends on what
- **Interactive HTML Reports** — Browse your mod ecosystem in a web browser
- **Game Code Integration** — Links mod changes to actual game code

### Generate a Report

```powershell
cd toolkit/XmlIndexer

# Single command: analyze everything and open report
dotnet run -- report "C:\Steam\steamapps\common\7 Days To Die" "C:\Users\...\Mods" ./reports --open
```

### Report Pages

| Page | Contents |
|------|----------|
| **Index** | Overview, mod count, conflict summary |
| **Mods** | Each mod's changes, patches, and potential conflicts |
| **Conflicts** | All detected conflicts with severity ratings |
| **C# Mods** | Harmony patches, target methods, patch types |
| **Entities** | Items, blocks, buffs with cross-references |
| **Dependencies** | What depends on what |
| **Game Code** | Classes and methods referenced by mods |
| **Glossary** | XML property definitions |

### CLI Commands

```powershell
# Full workflow (build database + analyze + detect conflicts)
dotnet run -- full-analyze <game_path> <mods_path> <database.db>

# Generate report from scratch
dotnet run -- report <game_path> <mods_path> <output_dir> [--open]

# Individual steps
dotnet run -- build <game_path> <database.db>           # Index game XML
dotnet run -- analyze-mods <database.db> <mods_path>    # Analyze mods
dotnet run -- detect-conflicts <database.db>            # Find conflicts

# Queries
dotnet run -- refs <db> item gunPistol                  # Find references
dotnet run -- search <db> "*medical*"                   # Search by pattern
dotnet run -- query <db> "SELECT * FROM mods"           # Custom SQL
```

See [toolkit/XmlIndexer/USER_GUIDE.md](toolkit/XmlIndexer/USER_GUIDE.md) for complete documentation.

---

## Game Code Decompiler

Extract decompiled C# source code and XML configs from the game. Essential for understanding game internals.

### Usage

```powershell
# Auto-detect game location, extract everything
.\Decompile-7D2D.ps1

# Custom paths
.\Decompile-7D2D.ps1 -GamePath "D:\Games\7 Days To Die" -OutputPath "./GameCode"

# Code only (skip XML)
.\Decompile-7D2D.ps1 -CodeOnly

# Data only (skip decompilation)
.\Decompile-7D2D.ps1 -DataOnly
```

### Output Structure

```
7D2DCodebase/
├── Assembly-CSharp/           # Main game code (~4,700 classes)
├── Assembly-CSharp-firstpass/ # Additional game systems
├── Data/Config/               # All XML configs
│   ├── blocks.xml            # 6,000+ block definitions
│   ├── items.xml             # 1,300+ item definitions
│   ├── buffs.xml             # 460+ buff definitions
│   ├── recipes.xml           # 580+ recipes
│   ├── entityclasses.xml     # Entity definitions
│   └── ...
└── README.md
```

### Version Tracking with Git

```powershell
# After game update, run again
.\Decompile-7D2D.ps1

# See what changed
cd 7D2DCodebase
git diff HEAD~1 --stat                    # Summary
git diff HEAD~1 -- "*.cs"                 # Code changes
git diff HEAD~1 -- "Data/Config/*.xml"    # XML changes
```

---

## Call Graph Analysis (Advanced)

For deep code analysis: find callers, trace execution paths, analyze performance.

### Build Call Graph Database

```powershell
cd toolkit/CallGraphExtractor
dotnet run -c Release -- `
  --source "../../7D2DCodebase" `
  --output "../callgraph_full.db" `
  --game-root "C:\Steam\steamapps\common\7 Days To Die" `
  --verbose
```

### Query the Database

```powershell
cd toolkit/QueryDb

# Who calls this method?
dotnet run -- ../callgraph_full.db callers "Bag.DecItem"

# What does this method call?
dotnet run -- ../callgraph_full.db callees "XUiM_PlayerInventory.GetItemCount"

# Find call path between methods
dotnet run -- ../callgraph_full.db chain "EntityPlayerLocal.Update" "Bag.DecItem"

# Search method bodies
dotnet run -- ../callgraph_full.db search "GetComponent"

# Find all implementations of a method
dotnet run -- ../callgraph_full.db impl "CanReload"

# Performance analysis (find expensive Update methods)
dotnet run -- ../callgraph_full.db perf
```

### Database Statistics

| Table | Count | Description |
|-------|-------|-------------|
| types | 4,725 | Classes, structs, interfaces |
| methods | 39,342 | All method signatures |
| calls | 165,271 | Resolved call edges (85.6% resolution) |
| method_bodies | 32,013 | FTS5 searchable method bodies |
| xml_definitions | 131,960 | Game XML property definitions |

---

## Requirements

- **Windows 10/11** with PowerShell 5.1+
- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Git** (recommended) — [Download](https://git-scm.com/download/win)
- **7 Days to Die** game installation

---

## Documentation

| Document | Contents |
|----------|----------|
| [toolkit/XmlIndexer/USER_GUIDE.md](toolkit/XmlIndexer/USER_GUIDE.md) | Complete XmlIndexer usage guide |
| [TOOLKIT_DESIGN.md](TOOLKIT_DESIGN.md) | Technical design, SQL examples, roadmap |
| [AI_CONTEXT.md](AI_CONTEXT.md) | Guide for AI assistants using this toolkit |
| [toolkit/QUICKSTART.md](toolkit/QUICKSTART.md) | Quick start for toolkit development |
| [toolkit/SCHEMA.md](toolkit/SCHEMA.md) | Database schema documentation |

---

## Key Classes for Modding

### Inventory & Containers
| Class | Purpose |
|-------|---------|
| `XUiM_PlayerInventory` | Player inventory manager |
| `Bag` | Player backpack |
| `TileEntityLootContainer` | Basic loot containers |
| `TileEntitySecureLootContainer` | Lockable storage |
| `TEFeatureStorage` | Storage feature for composite blocks |

### UI System
| Class | Purpose |
|-------|---------|
| `XUi` | Main UI controller |
| `XUiC_ItemStackGrid` | Item grid base class |
| `XUiC_LootContainer` | Container UI |
| `XUiC_RecipeList` | Crafting recipe list |

### Entities
| Class | Purpose |
|-------|---------|
| `EntityPlayerLocal` | Local player |
| `EntityAlive` | Base for living entities |
| `ItemAction` | Item use actions |
| `ItemClass` | Item definitions |

---

## Troubleshooting

### "Could not auto-detect 7 Days to Die installation"
```powershell
.\Decompile-7D2D.ps1 -GamePath "C:\Your\Path\To\7 Days To Die"
```

### ".NET SDK not found"
Install .NET 8.0 SDK from https://dotnet.microsoft.com/download

### "No mods detected"
- Check your mods folder path
- Each mod needs a `ModInfo.xml` file

### Report generation fails
```powershell
# Try full-analyze first to build database
cd toolkit/XmlIndexer
dotnet run -- full-analyze "C:\...\7 Days To Die" "C:\...\Mods" ecosystem.db
dotnet run -- report "C:\...\7 Days To Die" "C:\...\Mods" ./reports
```

---

## License

MIT License - Use freely for your modding projects.

---

## Related Projects

- [ProxiCraft](https://github.com/rk-gamemods/ProxiCraft) — Craft from nearby containers
- [AudibleBreakingGlassJars](https://github.com/rk-gamemods/AudibleBreakingGlassJars) — Sound when glass jars break
