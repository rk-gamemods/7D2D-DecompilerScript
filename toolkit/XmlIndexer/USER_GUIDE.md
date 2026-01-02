# 7D2D Mod Ecosystem Analyzer - User Guide

A complete toolkit for analyzing 7 Days to Die mods, detecting conflicts, generating reports, and creating human-readable descriptions of what mods actually do.

## Table of Contents

1. [Quick Start](#quick-start)
2. [Full Workflow](#full-workflow)
3. [Individual Commands](#individual-commands)
4. [Semantic Analysis (LLM-Powered)](#semantic-analysis-llm-powered)
5. [Report Generation](#report-generation)
6. [Database Statistics](#database-statistics)
7. [Searching and Querying](#searching-and-querying)
8. [Examples](#examples)
9. [Troubleshooting](#troubleshooting)

---

## Quick Start

### Prerequisites
- .NET 8.0 SDK
- 7 Days to Die game installation
- Your mods folder

### Build the Tool
```powershell
cd toolkit/XmlIndexer
dotnet build
```

### Run Complete Analysis
```powershell
dotnet run -- full-analyze "C:\Steam\steamapps\common\7 Days To Die" "C:\...\Mods" ecosystem.db
```

This single command:
1. Indexes ALL game XML data (items, blocks, buffs, recipes, etc.)
2. Analyzes EVERY mod in your Mods folder
3. Detects conflicts between mods
4. Builds a searchable database

---

## Full Workflow

The recommended workflow for complete mod ecosystem analysis:

### Step 1: Build the Database
```powershell
# Full analysis (recommended - does everything)
dotnet run -- full-analyze "C:\Games\7 Days To Die" "D:\Mods\7D2D" ecosystem.db
```

Output:
```
╔═══════════════════════════════════════════════════════╗
║  7D2D MOD ECOSYSTEM ANALYZER - FULL WORKFLOW          ║
╚═══════════════════════════════════════════════════════╝

[1/4] Building base game index...    ✓  15,534 definitions  [12.3s]
[2/4] Analyzing mods...              ✓  14 mods analyzed    [3.2s]
[3/4] Detecting conflicts...         ✓  2 potential issues  [0.4s]
[4/4] Generating behavioral summary...  ✓ Done              [0.8s]
```

### Step 2: Generate Reports
```powershell
# Generate HTML report (interactive, best for viewing)
dotnet run -- report ecosystem.db ./reports --html

# Generate all formats
dotnet run -- report ecosystem.db ./reports --html --md --json
```

### Step 3: (Optional) Add Human-Readable Descriptions
See [Semantic Analysis](#semantic-analysis-llm-powered) section below.

---

## Individual Commands

### `build` - Index Game Data Only
```powershell
dotnet run -- build "C:\Games\7 Days To Die" ecosystem.db
```

Creates a database with ALL base game definitions:
- 6,289 blocks
- 2,453 sounds
- 1,877 entity groups
- 1,375 items
- 1,002 loot groups
- 889 game events
- 588 recipes
- 461 buffs
- 292 entity classes
- ...and more

### `analyze-mod` - Analyze Single Mod
```powershell
dotnet run -- analyze-mod ecosystem.db "D:\Mods\MyMod"
```

Detects:
- XML modifications (what game data it changes)
- C# code dependencies (what game code it hooks into)
- Harmony patches (runtime code modifications)
- Potential conflicts with base game

### `analyze-mods` - Analyze All Mods
```powershell
dotnet run -- analyze-mods ecosystem.db "D:\Mods"
```

Recursively analyzes every mod in the folder.

---

## Semantic Analysis (LLM-Powered)

This system generates **human-readable descriptions** of technical mod changes using a local LLM.

### Database Contents

After running `full-analyze`, your database contains:

| Data Type | Count | Example |
|-----------|-------|---------|
| Unique property names | ~804 | `EntityDamage`, `BagSize`, `CraftingTime` |
| Definitions | ~15,534 | Items, blocks, buffs, recipes, entities |
| Cross-reference patterns | ~22 | item→buff, recipe→item relationships |
| Definition types | ~13 | item, block, buff, recipe, etc. |
| C# classes (from mods) | ~26+ | `XUiM_PlayerInventory`, `ItemActionEat` |

**Total: ~16,400 items that can have descriptions**

### Workflow: Batch Processing with Local LLM

#### Step 1: Export Traces
```powershell
dotnet run -- export-semantic-traces ecosystem.db traces.jsonl
```

Output:
```
╔══════════════════════════════════════════════════════════════════╗
║  EXPORTING SEMANTIC TRACES FOR LLM ANALYSIS                      ║
╚══════════════════════════════════════════════════════════════════╝

  ℹ️  Skipping 0 already-mapped items (batch mode)
Collecting ALL property names...
  Found 804 unique property names
Collecting ALL definitions (items, blocks, buffs, etc.)...
  Found 15534 definitions
Collecting cross-reference patterns...
  Found 22 reference patterns
Collecting definition type summaries...
  Found 13 definition types
Collecting C# class definitions...
  Found 26 unique C# classes

Writing 16399 traces to traces.jsonl...

╔══════════════════════════════════════════════════════════════════╗
║  EXPORTED 16399 TRACES                                       ║
╚══════════════════════════════════════════════════════════════════╝
```

#### Step 2: Process with Local LLM

**Prerequisites:**
1. Install [LM Studio](https://lmstudio.ai/)
2. Download a model (recommended: Mistral 7B Instruct, Llama 2 13B Chat)
3. Start the local server: Settings → Local Server → Start

**Run the mapper:**
```powershell
python semantic_mapper.py traces.jsonl mappings.jsonl
```

**Batch Processing Tip:** Process in batches by stopping and restarting:
```powershell
# Process first 1000
python semantic_mapper.py traces.jsonl mappings_batch1.jsonl --limit 1000

# Import partial results
dotnet run -- import-semantic-mappings ecosystem.db mappings_batch1.jsonl

# Export remaining (skips already-done items!)
dotnet run -- export-semantic-traces ecosystem.db traces_remaining.jsonl
```

#### Step 3: Import Descriptions
```powershell
dotnet run -- import-semantic-mappings ecosystem.db mappings.jsonl
```

#### Step 4: Check Progress
```powershell
dotnet run -- semantic-status ecosystem.db
```

Output:
```
╔══════════════════════════════════════════════════════════════════╗
║  SEMANTIC MAPPING STATUS                                         ║
╚══════════════════════════════════════════════════════════════════╝

  Entity Type         Total    Filled   Coverage
  ────────────────────────────────────────────────
  property_name          804      500    62%
  definition           15534     8000    51%
  cross_reference_pattern  22       22   100%
  definition_type          13       13   100%
  csharp_class             26       26   100%
  ────────────────────────────────────────────────
  TOTAL                16399     8561    52%
```

### Trace Format Example

Each trace looks like:
```json
{
  "entity_type": "property_name",
  "entity_name": "EntityDamage",
  "parent_context": "item,buff",
  "code_trace": "<!-- Property: EntityDamage -->\n<property name=\"EntityDamage\" value=\"...\"/>\nSample values: 10, 25, 50, 100",
  "usage_examples": "Used 1523 times in item,buff",
  "game_context": "Combat",
  "layman_description": null  // LLM fills this in
}
```

After LLM processing:
```json
{
  "entity_type": "property_name",
  "entity_name": "EntityDamage",
  "layman_description": "How much damage this item deals to enemies and players"
}
```

---

## Report Generation

### HTML Report (Recommended)
```powershell
dotnet run -- report ecosystem.db ./reports --html
```

Features:
- Interactive navigation
- Conflict highlighting
- Mod behavioral summaries
- Expandable details
- Search within page

### Markdown Report
```powershell
dotnet run -- report ecosystem.db ./reports --md
```

Good for:
- GitHub/GitLab wikis
- Documentation
- Version control

### JSON Export
```powershell
dotnet run -- report ecosystem.db ./reports --json
```

Good for:
- Custom tooling
- Data processing
- Integration with other tools

---

## Database Statistics

### Quick Stats
```powershell
dotnet run -- stats ecosystem.db
```

Shows:
- Definition counts by type
- Mod statistics
- Fun facts (longest item name, most referenced, etc.)
- Cross-mod insights

### Ecosystem View
```powershell
dotnet run -- ecosystem ecosystem.db
```

Shows combined view of game + mod ecosystem.

---

## Searching and Querying

### Find All References
```powershell
# Find everything that references "gunPistol"
dotnet run -- refs ecosystem.db item gunPistol
```

### List All of a Type
```powershell
# List all buffs
dotnet run -- list ecosystem.db buff
```

### Search by Pattern
```powershell
# Find all items containing "medical"
dotnet run -- search ecosystem.db "*medical*"
```

---

## Examples

### Example 1: Check if Mods Conflict
```powershell
# Analyze mods
dotnet run -- full-analyze "C:\Steam\...\7 Days To Die" "C:\Mods" mods.db

# Generate report to see conflicts
dotnet run -- report mods.db ./output --html

# Open output/ecosystem_report_*.html in browser
```

### Example 2: Understand What a Mod Does
```powershell
# Analyze just that mod
dotnet run -- analyze-mod mods.db "C:\Mods\SomeMod"

# Generate report
dotnet run -- report mods.db ./output --html
```

Look at the "Behavioral Summary" section for plain-English descriptions.

### Example 3: Find All Mods That Touch Inventory
```powershell
# After building database
dotnet run -- search mods.db "*inventory*"
dotnet run -- search mods.db "*bag*"
dotnet run -- search mods.db "*backpack*"
```

### Example 4: Complete Semantic Analysis Pipeline
```powershell
# 1. Full analysis
dotnet run -- full-analyze "C:\Games\7D2D" "C:\Mods" eco.db

# 2. Export traces
dotnet run -- export-semantic-traces eco.db all_traces.jsonl

# 3. Process with LLM (in batches if needed)
python semantic_mapper.py all_traces.jsonl batch1.jsonl

# 4. Import results
dotnet run -- import-semantic-mappings eco.db batch1.jsonl

# 5. Check progress
dotnet run -- semantic-status eco.db

# 6. Generate final report (now with better descriptions!)
dotnet run -- report eco.db ./final_reports --html
```

---

## Troubleshooting

### "Database not found"
Make sure to run `build` or `full-analyze` first.

### "No mods detected"
- Check your mods folder path
- Each mod needs a `ModInfo.xml` file

### Semantic mapper won't connect
1. Open LM Studio
2. Go to Settings → Local Server
3. Click "Start Server"
4. Wait for "Server started" message
5. Try again

### Export shows 0 items after partial import
This is correct! The system skips already-mapped items. Use `semantic-status` to see what's been done.

### Large traces.jsonl file
The file can be 50-100MB with all 16,000+ traces. This is normal.

### Processing is slow
- Use a smaller/faster model (7B vs 13B)
- Reduce `--delay` parameter
- Process overnight

---

## Command Reference

| Command | Description |
|---------|-------------|
| `full-analyze <game> <mods> <db>` | Complete analysis workflow |
| `build <game> <db>` | Index game XML only |
| `analyze-mod <db> <mod>` | Analyze single mod |
| `analyze-mods <db> <mods>` | Analyze all mods |
| `report <db> <dir> [--html\|--md\|--json]` | Generate reports |
| `stats <db>` | Show statistics |
| `ecosystem <db>` | Combined ecosystem view |
| `refs <db> <type> <name>` | Find references |
| `list <db> <type>` | List definitions |
| `search <db> <pattern>` | Search definitions |
| `export-semantic-traces <db> <out>` | Export for LLM |
| `import-semantic-mappings <db> <in>` | Import LLM descriptions |
| `semantic-status <db>` | Show mapping coverage |

---

## File Structure

```
toolkit/XmlIndexer/
├── Program.cs              # Main tool code
├── semantic_mapper.py      # LLM processing script
├── USER_GUIDE.md          # This file
├── ecosystem.db           # Your database (after running)
└── traces.jsonl           # Export file (after export)
```

---

## Tips for Best Results

1. **Start fresh**: Delete old `.db` files when updating game version
2. **Use HTML reports**: Most feature-rich viewing experience
3. **Batch semantic analysis**: Process 500-1000 items at a time
4. **Check status often**: `semantic-status` shows progress
5. **Back up your database**: Once mappings are done, save the `.db` file

---

*Last updated: January 2026*
