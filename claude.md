# XmlIndexer - Claude AI Context

## Project Overview

This is the **7 Days to Die Mod Ecosystem Analyzer** - a C# .NET 8 tool that:
- Indexes base game XML configuration into SQLite
- Analyzes installed mods for conflicts and dependencies
- Generates interactive multi-page HTML reports

## CRITICAL: How to Run Commands

**Project Location:** `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer`

**ALWAYS use this pattern:**
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- <command> <args>
```

## Quick Reference Commands

### Generate Report (most common)
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- report "C:\Steam\steamapps\common\7 Days To Die" "C:\Steam\steamapps\common\7 Days To Die\Mods" "./reports" --open
```

### Run Ad-Hoc SQL Query
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- query "./ecosystem.db" "SELECT name, target_type, target_name FROM mod_xml_operations LIMIT 10"
```

### Build Only (no report)
```powershell
cd "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer"; dotnet run -- full-analyze "C:\Steam\steamapps\common\7 Days To Die" "C:\Steam\steamapps\common\7 Days To Die\Mods" "./ecosystem.db"
```

## Path Reference

| What | Path |
|------|------|
| XmlIndexer project | `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer` |
| Decompiled game code | `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2DCodebase` |
| Test mods folder | `C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\temp_analysis` |
| Game folder (live) | `C:\Steam\steamapps\common\7 Days To Die` |
| Mods folder (live) | `C:\Steam\steamapps\common\7 Days To Die\Mods` |
| Report output | `./reports/ecosystem_YYYY-MM-DD_HHmm/` |
| Database | `./ecosystem.db` or in output folder |

## Database Schema (Key Tables)

### `mods`
```sql
id, name, path, has_xml, has_dll, load_order
```

### `mod_xml_operations`
```sql
id, mod_id, file_path, operation, xpath, target_type, target_name, property_name, old_value, new_value
```

### `xml_definitions`
```sql
id, definition_type, name, parent_name, file_path
```

### `xml_properties`
```sql
id, definition_id, property_name, property_value
```

### `harmony_patches`
```sql
id, mod_id, patch_class, target_class, target_method, patch_type, harmony_priority, returns_bool, modifies_result
```

### `harmony_conflicts`
```sql
id, target_class, target_method, conflict_type, severity, mod1_id, mod2_id, explanation
```

### `game_code_analysis`
```sql
id, analysis_type, class_name, method_name, severity, confidence, description, file_path
```

## Source Code Structure

```
XmlIndexer/
├── Program.cs                    # CLI entry, commands, DB operations
├── Models/
│   └── DataModels.cs            # Records: XPathTarget, ModInfo, HarmonyPatchInfo, etc.
├── Database/
│   └── DatabaseBuilder.cs       # Schema definition (single source of truth)
├── Analysis/
│   ├── ModAnalyzer.cs           # ExtractTargetFromXPath, mod parsing, Harmony scanning
│   ├── ReportDataCollector.cs   # SQL queries for reports
│   ├── HarmonyConflictDetector.cs # Harmony patch conflict detection
│   └── GameCodeAnalyzer.cs      # Game code bug hunting
├── Utils/
│   └── CSharpAnalyzer.cs        # C# analysis, Harmony patch regex scanning
├── Reports/
│   ├── ReportSiteGenerator.cs   # Orchestrates multi-page report
│   ├── SharedAssets.cs          # CSS, HTML helpers
│   ├── IndexPageGenerator.cs    # Dashboard (index.html)
│   ├── EntityPageGenerator.cs   # Entity explorer
│   ├── ModPageGenerator.cs      # Mod analysis
│   ├── ConflictPageGenerator.cs # Conflict center
│   ├── DependencyPageGenerator.cs
│   ├── CSharpPageGenerator.cs   # C# dependencies + Harmony conflicts
│   ├── GameCodePageGenerator.cs # Game code analysis findings
│   └── GlossaryPageGenerator.cs
```

## All CLI Commands

| Command | Arguments | Description |
|---------|-----------|-------------|
| `report` | `<game> <mods> <output> [--open]` | Full rebuild + HTML report |
| `full-analyze` | `<game> <mods> <db>` | Build database only |
| `query` | `<db> "<sql>"` | Run ad-hoc SQL query |
| `stats` | `<db>` | Show database statistics |
| `build` | `<game> <db>` | Index game XML only |
| `analyze-mod` | `<db> <mod_path>` | Analyze single mod |
| `analyze-mods` | `<db> <mods_folder>` | Analyze all mods |
| `refs` | `<db> <type> <name>` | Find references |
| `list` | `<db> <type>` | List entities by type |
| `search` | `<db> <pattern>` | Search entities |

## Recent Work Context

### ProxiCraft Release Process

When working on ProxiCraft releases, always reference [ProxiCraft/RELEASE_PROCESS.md](../../../../ProxiCraft/RELEASE_PROCESS.md) for the comprehensive release checklist. This standardized process ensures all version updates, changelog entries, README download links, and distribution steps are completed consistently.

### XPath Parser Enhancement
The `ExtractTargetFromXPath` function in `ModAnalyzer.cs` extracts entity targets from mod XPath expressions. Enhanced to handle:
- Standard: `/items/item[@name='gunPistol']` → type=item, name=gunPistol
- Fragile selectors: `/loot/lootcontainer[@size='12,10']` → type=lootcontainer, name=[size='12,10'], IsFragile=true

### Conflict Detection Logic
In `ReportDataCollector.cs`:
- `GetContestedEntities()` - Entities touched by multiple mods
- `GetPropertyConflicts()` - Same property, different values = conflict
- Redundant same-value edits are excluded (not conflicts)

### Report Output
Generated in timestamped folders like `./reports/ecosystem_2026-01-03_1955/`:
- index.html (dashboard)
- entities.html
- mods.html
- conflicts.html
- dependencies.html
- csharp.html
- glossary.html
