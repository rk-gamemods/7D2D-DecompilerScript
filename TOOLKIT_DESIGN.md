# 7D2D Mod Maintenance Toolkit — Design Document

## Elevator Pitch

**Stop spending hours tracing code paths manually.** This toolkit builds a complete map of the game's 4,400+ classes—every method call, every inheritance chain, every XML config—and lets you query it in seconds. 

Ask "what happens when a player crafts an item?" and get the exact call chain. Ask "will these 6 mods conflict?" and get a report pinpointing every collision. When the game updates, instantly see which of your patches might break.

**For AI assistants:** Instead of dumping 200K tokens of source files to understand one code path, query the graph and get a 500-token answer. 100x more efficient.

**For mod developers:** Turn days of reverse-engineering into minutes. Find the right hook point, verify mod compatibility, catch bugs before they ship.

**For the community:** Eventually, a tool simple enough that anyone can check "will these mods work together?" before installing.

---

## Technical Summary

A SQLite-based toolkit combining call graph analysis + keyword search to dramatically reduce AI token overhead. The call graph solves the core pain point (execution flow tracing), FTS5 handles lookup. ChromaDB deferred—not needed yet.

**Current Status (as of Dec 2024):** Core extraction pipeline complete and tested. Database contains 4,725 types, 39,342 methods, 165,271 resolved calls (85.6% resolution), 131,960 XML definitions, and 746 code→XML property access patterns. Query tools operational.

---

## Development Roadmap

### Phase 1: Call Graph Extractor ✅ COMPLETE
**Build call graph extractor** using Roslyn — Parse all ~4,400 game files with full semantic analysis, extract methods + their invocations with accurate type resolution, store as nodes/edges in SQLite. This is the high-value core: query "what calls `DecItem`?" or "trace path from `Craft` to `RemoveItems`".

**Implemented in C#** (`toolkit/CallGraphExtractor/`):
- `RoslynParser.cs` — Full Roslyn semantic analysis with auto-discovery of 373 DLL references
- `SqliteWriter.cs` — Database persistence with all tables
- `CallExtractor.cs` — Call edge extraction (internal + external calls)

### Phase 2: Full-Text Search ✅ COMPLETE
**Add FTS5 to same database** — Index method bodies for keyword search. Complements call graph: find entry points by name, then trace their flow. One DB file, two query modes.

**Implemented:** FTS5 virtual table `method_bodies` with porter stemming, 32,013 method bodies indexed.

### Phase 3: XML Game Data Integration ✅ COMPLETE (SCOPE EXPANSION)
**Originally unplanned — added during development.**

**New capability:** Extract and cross-reference XML game data with code:
- `XmlDefinitionExtractor.cs` — Parses all 42 game XML config files (items.xml, blocks.xml, etc.), extracts 131,960 property definitions with xpath locations
- `XmlPropertyAccessExtractor.cs` — Detects code that reads XML properties via `GetValue`, `GetInt`, `GetBool`, `Contains`, etc. Found 746 code→XML linkages

**Enables queries like:**
- "What code reads the 'Stacknumber' property?" 
- "What properties does 'gunPistol' have?"
- "Which XML properties are most frequently accessed by code?"

### Phase 4: Mod Analysis Framework ✅ COMPLETE (SCOPE EXPANSION)  
**Originally Phase 4 was just conflict detection — expanded to full mod parsing.**

**Implemented:**
- `ModParser.cs` — Parses mod C# files, detects Harmony patches via `[HarmonyPatch]` and method naming conventions (`Prefix`, `Postfix`, `Transpiler`)
- `ModXmlChangeParser.cs` — Parses mod XML files for xpath operations

**Schema expanded with:** `mods`, `mod_types`, `mod_methods`, `mod_method_bodies`, `harmony_patches`, `xml_changes`, `mod_conflicts` tables.

### Phase 5: CLI Query Tools ✅ COMPLETE
**Pivoted to C# instead of Python** for simpler deployment (single executable, no Python runtime needed).

**Implemented Commands:**
- `QueryDb <db>` — Summary statistics (types, methods, calls, mods)
- `QueryDb <db> sql "SELECT ..."` — Custom SQL queries
- `QueryDb <db> callers <method>` — Find all callers with ambiguous name detection
- `QueryDb <db> callees <method>` — Find internal + external calls made by a method
- `QueryDb <db> search <keyword>` — FTS5 full-text search with snippet highlighting
- `QueryDb <db> chain <from> <to>` — Recursive CTE path-finding (depth 10 limit)
- `QueryDb <db> impl <method>` — Find all implementations/overrides with inheritance info
- `QueryDb <db> compat` — Mod compatibility checker (Harmony + XML conflicts)
- `QueryDb <db> perf [category]` — Performance analysis (updates/getcomponent/find/strings/linq)
- `QueryDb <db> xml <name>` — XML definition lookup with code access references

**Features:**
- Method name disambiguation (shows all matches if name is ambiguous)
- JSON output support (`--json` flag)
- File path and line number references in all results

### Phase 6: Performance Analysis Queries 🆕 NEW CAPABILITY
**Discovered during development — the database enables vanilla game performance analysis.**

**Now queryable:**
- Update/LateUpdate/FixedUpdate methods by complexity (body size, call count)
- Methods calling expensive Unity APIs (`GetComponent`, `FindObjectsOfType`) in hot paths
- String allocations in frame-by-frame code
- Largest/most complex methods as optimization targets

**Example finding:** `PlayerMoveController.Update` is 48KB with 677 calls per frame including 7 `GetComponent` calls.

### Phase 7: AI Integration
**Include `AI_CONTEXT.md` instructions** — Document how an AI should use these tools. Example queries, output format, what to ask for different problem types.

---

## Current Database Statistics (Dec 2025)

Built from 7 Days to Die V1.1 (b14) decompiled source:

| Table | Count | Notes |
|-------|-------|-------|
| **types** | 4,725 | Classes, structs, interfaces, enums |
| **methods** | 39,342 | All method signatures extracted |
| **calls** | 117,757 | Internal call graph edges (resolved) |
| **external_calls** | 47,514 | Calls to Unity/BCL/third-party |
| **method_bodies** | 32,013 | FTS5 indexed method bodies |
| **xml_definitions** | 131,960 | Properties from 42 XML config files |
| **xml_property_access** | 746 | Code locations reading XML properties |

**Call Resolution:** 85.6% (165,271 of 193,110 total calls resolved)
- Uses 373 metadata references (187 .NET runtime + 186 game DLLs)
- Unresolved calls are typically to missing/native libraries

**Top External Call Targets:**
1. `Log.Out` — 1,448 calls
2. `Vector3..ctor` — 1,359 calls
3. `Component.get_transform` — 1,158 calls
4. `Component.get_gameObject` — 1,075 calls
5. `Log.Error` — 1,007 calls

**XML Files Parsed:**
- blocks.xml (55,387 definitions)
- shapes.xml (19,411)
- items.xml (18,696)
- buffs.xml (7,251)
- entityclasses.xml (4,667)
- And 37 more...

---

## Example Queries

### Performance Analysis (Vanilla Game)

**Find largest Update methods (complexity indicator):**
```sql
SELECT t.full_name || '.' || m.name as method, 
       length(mb.body) as body_size 
FROM methods m 
JOIN types t ON m.type_id = t.id 
JOIN method_bodies mb ON m.id = mb.method_id 
WHERE m.name IN ('Update', 'LateUpdate', 'FixedUpdate') 
ORDER BY body_size DESC LIMIT 10;
```

**Find Update methods calling expensive Unity APIs:**
```sql
SELECT t.full_name || '.' || m.name as method, 
       ec.target_method, COUNT(*) as calls
FROM methods m 
JOIN types t ON m.type_id = t.id 
JOIN external_calls ec ON m.id = ec.caller_id 
WHERE m.name = 'Update' 
  AND ec.target_method IN ('GetComponent', 'FindObjectsOfType', 'Find')
GROUP BY method, ec.target_method
ORDER BY calls DESC;
```

**Find string allocations in hot paths:**
```sql
SELECT t.full_name || '.' || m.name as method
FROM methods m 
JOIN types t ON m.type_id = t.id 
JOIN method_bodies mb ON m.id = mb.method_id 
WHERE m.name = 'Update' 
  AND mb.body LIKE '%string.Format%';
```

### XML Property Tracing

**What code reads a specific XML property?**
```sql
SELECT m.name as method, t.name as type, xpa.access_method, 
       xpa.file_path, xpa.line_number
FROM xml_property_access xpa
JOIN methods m ON xpa.method_id = m.id
JOIN types t ON m.type_id = t.id
WHERE xpa.property_name = 'Stacknumber';
```

**What properties does an item have?**
```sql
SELECT property_name, property_value
FROM xml_definitions
WHERE element_name = 'gunHandgunT1Pistol' 
  AND property_name IS NOT NULL;
```

### Call Graph Navigation

**Who calls this method?**
```sql
SELECT t.name || '.' || m.name as caller, c.file_path, c.line_number
FROM calls c
JOIN methods m ON c.caller_id = m.id
JOIN types t ON m.type_id = t.id
WHERE c.callee_id = (SELECT id FROM methods WHERE name = 'DecItem' LIMIT 1);
```

**What does this method call?**
```sql
SELECT t.name || '.' || m.name as callee
FROM calls c
JOIN methods m ON c.callee_id = m.id
JOIN types t ON m.type_id = t.id
WHERE c.caller_id = (SELECT id FROM methods WHERE name = 'RemoveItems' LIMIT 1);
```

---

## Architecture Decisions

### 0. Philosophy — Use Everything, Optimize Nothing

**This extraction runs once per game update (1-2x/month at most).** Runtime is completely irrelevant—if it takes 30 seconds or 10 minutes, we don't care. We do the work once, then query the resulting database until the next game update.

**Therefore: Load everything. Be comprehensive. No selectivity.**

- **All source directories** — Parse `Assembly-CSharp`, `Assembly-CSharp-firstpass`, and any other decompiled assemblies we find
- **All DLLs from game directory** — Recursively scan the entire game installation for every `.dll` file. Unity modules, third-party libraries, mod loaders, everything. Load them all as metadata references.
- **No filtering** — Don't try to be smart about which DLLs "matter." Native DLLs will fail to load (they're C++), so catch those exceptions and skip silently. But try everything.
- **Log transparently** — Report what loaded, what failed, and why. But don't let failures stop the process.

The goal is **maximum call resolution**. Every unresolved call is a potential blind spot when debugging mod issues. The game ships with ~60-80 DLLs in the Managed folder alone—load all of them.

### 1. Parser Choice — Roslyn for Accuracy

**Decision: Use Roslyn.** Speed isn't a concern—this runs once per game update (1-2x/month), so a few minutes is fine. Robustness matters more.

**Roslyn** is Microsoft's official C# compiler-as-a-library. It doesn't just parse—it *compiles* the code mentally. It knows that `bag.DecItem()` refers to `Bag.DecItem(ItemValue, int)` specifically, resolves inheritance, understands overloads. Like having a C# expert read and annotate every line.

What Roslyn gives us:
- **Accurate type resolution** — Knows exactly which overload is called
- **Inheritance tracking** — Knows `EntityVehicle : EntityAlive`
- **Full signatures** — Can detect when a method signature changes between game versions
- **Cross-reference** — "Find all implementations of this interface"

**Implementation note:** Roslyn needs referenced DLLs to fully resolve types. We've implemented comprehensive auto-discovery:

**DLL Loading Strategy (Implemented):**
```
1. Scan --game-root recursively for ALL .dll files
2. Load each as MetadataReference (skip native DLLs that fail)
3. Also load .NET runtime assemblies from typeof(object).Assembly.Location
4. Result: 373 references (187 .NET + 186 game DLLs)
```

This "load everything" approach improved call resolution from 58.8% to **85.6%**. The remaining unresolved calls are typically:
- Calls to native code (C++ plugins)
- Generic type instantiations Roslyn can't resolve without full compilation
- Decompiler artifacts (invalid C# that Roslyn rejects)

### 2. Graph Library Choice — TBD (Deferred)

**Original Decision: igraph over NetworkX.**

**Update:** Graph path-finding not yet implemented. For simple queries (callers/callees), raw SQL is sufficient. For `chain` command, options being evaluated:
- **QuikGraph** (C#) — Keeps everything in one language
- **igraph** (Python) — More algorithms, but requires Python runtime
- **SQL CTEs** — Recursive queries for simple paths, no external deps

Most real-world queries are 1-2 hops and don't need a graph library.

### 3. Call Graph Scope — Unified Game + Mods + Mod Interactions

**Goal:** Understand game flow, find proper hook points, ensure mods don't conflict.

This *is* fundamentally a graph problem. The data model:

```
Nodes: Methods (game + mod)
Edges: 
  - "calls" (A invokes B)
  - "patches" (mod method hooks game method)
  - "modifies" (XML edit changes behavior)
```

**How Roslyn, SQLite, and igraph work together:**

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Data Pipeline                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  .cs files ──► ROSLYN ──► SQLite ──► igraph ──► Query Results       │
│               (parse)    (store)    (analyze)                        │
│                                                                      │
│  Roslyn's job:                                                       │
│    • Read C# source code                                             │
│    • Extract methods, calls, types with full accuracy                │
│    • Output: "MethodA calls MethodB" relationships                   │
│                                                                      │
│  SQLite's job:                                                       │
│    • Persist the extracted data                                      │
│    • Enable FTS5 keyword search                                      │
│    • Handle simple queries directly (no graph load needed)           │
│    • Survive between runs (don't re-parse unless game updates)       │
│                                                                      │
│  igraph's job:                                                       │
│    • Load graph from SQLite (only when needed)                       │
│    • Run graph algorithms (path finding, centrality, subgraphs)      │
│    • Answer "how do I get from A to B?" queries                      │
│    • Detect indirect mod conflicts via reachability analysis         │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Scope:**
1. **Game code** — Full call graph from decompiled Assembly-CSharp (Roslyn parses)
2. **Mod code** — Parse mod .cs files, add to same graph with "patches" edges
3. **XML changes** — Parse mod XML files, track which nodes they modify

### 4. Output Format — JSON + Interactive SQL

**Original Decision: JSON output only.**

**Update:** Current implementation supports:
- **Custom SQL** — Run any query via `QueryDb` tool for maximum flexibility
- **Default diagnostics** — Built-in queries for common checks
- **JSON** — Planned for structured output from convenience commands

Interactive SQL has proven valuable during development. JSON wrappers will be added for automation/AI integration.

---

## Mod Compatibility Detection

### Goal
Automatically detect conflicts between multiple mods and pinpoint EXACT locations.

Answer: "Are mods A, B, C, D, E, F compatible together?"

### Conflict Types

| Type | Description | Detection Method | Severity |
|------|-------------|------------------|----------|
| **Direct patch** | Two mods patch the same method | SQL: `GROUP BY (target_class, target_method) HAVING COUNT(*) > 1` | 🔴 High |
| **Indirect behavioral** | Mod A changes behavior that Mod B depends on | igraph: reachability from A's patches to B's call sites | 🟡 Medium |
| **Load order** | Mod A must load before Mod B | Infer from patch types (Transpiler before Prefix) | 🟡 Medium |
| **XML collision** | Two mods modify same XML node | SQL: `GROUP BY (file, xpath) HAVING COUNT(*) > 1` | 🟠 Medium-High |

### Database Schema

**From C# mods (Harmony patches):**
```sql
CREATE TABLE harmony_patches (
    id INTEGER PRIMARY KEY,
    mod_name TEXT NOT NULL,
    patch_class TEXT,           -- The mod's patch class
    patch_method TEXT,          -- Prefix/Postfix/Transpiler method
    target_type TEXT NOT NULL,  -- Game class being patched
    target_method TEXT NOT NULL,-- Game method being patched
    patch_type TEXT,            -- 'Prefix', 'Postfix', 'Transpiler'
    priority INTEGER,           -- Harmony priority if specified
    file_path TEXT,
    line_number INTEGER
);
```

**From XML mods:**
```sql
CREATE TABLE xml_changes (
    id INTEGER PRIMARY KEY,
    mod_name TEXT NOT NULL,
    file_name TEXT NOT NULL,    -- e.g., 'items.xml', 'blocks.xml'
    xpath TEXT NOT NULL,        -- Full XPath to modified node
    operation TEXT,             -- 'set', 'append', 'remove', 'insertAfter'
    attribute TEXT,             -- Which attribute if applicable
    file_path TEXT,
    line_number INTEGER
);
```

### Detection Algorithm

```
┌─────────────────────────────────────────────────────────────────────┐
│              7d2d-tools compat ModA ModB ModC ModD                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  1. PARSE each mod:                                                  │
│     • Scan .cs files for [HarmonyPatch] attributes                  │
│     • Scan .xml files for xpath operations                          │
│     • Store in harmony_patches / xml_changes tables                 │
│                                                                      │
│  2. DETECT direct conflicts:                                         │
│     • SQL query: same (target_type, target_method) by multiple mods │
│     • SQL query: same (file, xpath) by multiple mods                │
│                                                                      │
│  3. DETECT indirect conflicts (requires igraph):                     │
│     • For each mod's patches, compute "affected zone"               │
│       (all methods downstream in call graph)                        │
│     • Check if other mods call into that affected zone              │
│     • Flag as potential behavioral conflict                         │
│                                                                      │
│  4. INFER load order requirements:                                   │
│     • Transpilers should load before Prefix/Postfix on same method  │
│     • Higher priority patches should load in specific order         │
│                                                                      │
│  5. OUTPUT conflict report with exact locations                      │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Example Output

```bash
7d2d-tools compat ProxiCraft BeyondStorage2 AGF-Backpack
```

```json
{
  "query": "compat",
  "mods": ["ProxiCraft", "BeyondStorage2", "AGF-Backpack"],
  "conflicts": [
    {
      "type": "direct_patch",
      "severity": "high",
      "target": "XUiM_PlayerInventory.GetItemCount",
      "mods_involved": ["ProxiCraft", "BeyondStorage2"],
      "details": [
        {
          "mod": "ProxiCraft",
          "patch_type": "Postfix",
          "location": "ProxiCraft/Patches/InventoryPatches.cs:45"
        },
        {
          "mod": "BeyondStorage2", 
          "patch_type": "Postfix",
          "location": "BeyondStorage2/Harmony/ItemCount.cs:23"
        }
      ],
      "resolution": "Both mods modify item counting. May work if patch logic is additive. Test thoroughly."
    },
    {
      "type": "xml_collision",
      "severity": "medium",
      "target": "items.xml://item[@name='drinkJarBoiledWater']",
      "mods_involved": ["ProxiCraft", "AGF-Backpack"],
      "details": [
        {
          "mod": "ProxiCraft",
          "operation": "set",
          "attribute": "Stacknumber.value",
          "location": "ProxiCraft/Config/items.xml:34"
        },
        {
          "mod": "AGF-Backpack",
          "operation": "set", 
          "attribute": "Stacknumber.value",
          "location": "AGF-Backpack/Config/items.xml:156"
        }
      ],
      "resolution": "Both mods change stack size. Last-loaded mod wins. Verify intended value."
    }
  ],
  "load_order_suggestions": [
    {
      "recommendation": "Load ProxiCraft before BeyondStorage2",
      "reason": "ProxiCraft uses Transpiler on GetItemCount; BeyondStorage2 uses Postfix"
    }
  ],
  "summary": {
    "total_conflicts": 2,
    "high_severity": 1,
    "medium_severity": 1,
    "compatible": false,
    "compatible_with_caveats": true
  }
}
```

### Implementation Phases

**Phase 1 (covers 80% of real conflicts):**
- Direct patch conflicts (SQL only, no graph needed)
- XML collisions (SQL only)
- Exact file:line locations

**Phase 2 (requires call graph):**
- Indirect behavioral conflicts
- "Affected zone" analysis
- Load order inference

---

## Test Cases (From Real ProxiCraft Bugs)

Use these historical bugs as unit tests to validate the toolkit would catch them.

### Test Case 1: Method Overload Coverage Gap
**Source:** RESEARCH_NOTES.md - Bug #1 (Radial Menu Reload)

**Scenario:** ProxiCraft patched `XUiM_PlayerInventory.GetItemCount(string)` but the radial menu uses `GetItemCount(int)` overload.

**What tool should detect:**
- Call graph query: "Who calls `XUiM_PlayerInventory.GetItemCount`?"
- Should return BOTH overloads with different callers
- Alert: "Radial menu uses int overload, your mod only patches string overload"

**Test assertion:**
```
INPUT: callees("XUiC_RadialMenu.SomeMethod")
EXPECT: Contains "XUiM_PlayerInventory.GetItemCount(int)" 
NOT: "XUiM_PlayerInventory.GetItemCount(string)"
```

### Test Case 2: UI vs TileEntity Dual-Buffer Write Path
**Source:** RESEARCH_NOTES.md - Bug #3 (Workstation Free Crafting)

**Scenario:** ProxiCraft removed items from `TileEntity.items[]` but workstation UI overwrote changes on close.

**What tool should detect:**
- Call graph: Trace write path from UI → TileEntity
- Identify `syncTEfromUI()` call that overwrites TileEntity
- Alert: "Writing to TileEntity.items[] is unsafe when UI is open - UI sync overwrites"

**Test assertion:**
```
INPUT: chain("XUiC_WorkstationWindowGroup.OnClose", "TileEntityWorkstation.SetSlot")
EXPECT: Path exists through syncTEfromUI
FLAG: "Dual-buffer pattern detected - writes to TileEntity may be overwritten"
```

### Test Case 3: Event Firing Causes Side Effects
**Source:** RESEARCH_NOTES.md - Fix #8c (Item Duplication)

**Scenario:** Firing `OnBackpackItemsChangedInternal` during item transfers caused duplication.

**What tool should detect:**
- Call graph: What does `OnBackpackItemsChangedInternal` trigger?
- Identify it re-enters item transfer logic
- Alert: "Firing this event during transfers creates re-entrancy risk"

**Test assertion:**
```
INPUT: callees("OnBackpackItemsChangedInternal") 
EXPECT: Shows handlers that modify inventory
FLAG: "Re-entrancy hazard if fired during transfer operations"
```

### Test Case 4: Prefix Patch + Early Exit = State Corruption
**Source:** TRADER_SELLING_POSTMORTEM.md (Duplication Bug)

**Scenario:** Prefix patch modified slot count, but vanilla exited early → items duplicated.

**What tool should detect:**
- Static analysis: Method has multiple return statements
- Prefix patches that modify state are dangerous here
- Alert: "Method has 6 early exit points - prefix state changes may persist on failure"

**Test assertion:**
```
INPUT: analyze("ItemActionEntrySell.OnActivated")
EXPECT: 
  - early_exits: 6
  - warning: "Multiple exit points - prefix patches that modify state are hazardous"
  - recommendation: "Use postfix with __state or transpiler injection after all checks"
```

### Test Case 5: Context-Dependent Method Behavior
**Source:** RESEARCH_NOTES.md - Bug #4 (Take Like Button)

**Scenario:** `ItemStack.HasItem()` patched to include containers, but "Take Like" button used it to filter items, causing wrong behavior.

**What tool should detect:**
- Call graph: Who calls `ItemStack.HasItem()`?
- Multiple contexts with different expected behaviors
- Alert: "Method called from 5 contexts - patch may have unintended effects"

**Test assertion:**
```
INPUT: callers("ItemStack.HasItem")
EXPECT: Multiple distinct call sites including:
  - TakeItemLike_OnPress (filtering context)
  - CraftingManager (availability context)
FLAG: "Same method, different semantic expectations per caller"
```

### Test Case 6: Inheritance Hierarchy Not Covered
**Source:** RESEARCH_NOTES.md - Bug #1 (Only ItemActionLauncher patched)

**Scenario:** Only `ItemActionLauncher.CanReload()` patched, but `ItemActionRanged.CanReload()` also exists (different class, same purpose).

**What tool should detect:**
- Type hierarchy query: What classes have `CanReload()` method?
- Alert: "ItemActionRanged also has CanReload() - may need patching"

**Test assertion:**
```
INPUT: implementations("CanReload")
EXPECT: Returns both ItemActionRanged.CanReload and ItemActionLauncher.CanReload
FLAG: "Multiple implementations - verify all are patched if behavior should be consistent"
```

### Test Case 7: Competing Mods Same Target (Hypothetical BeyondStorage2)
**Source:** ProxiCraft + BeyondStorage2 both modify container item counting

**Scenario:** Two storage mods both patch `XUiM_PlayerInventory.GetItemCount()`.

**What tool should detect:**
- Direct conflict: Same method patched by multiple mods
- Exact locations in both mods

**Test assertion:**
```
INPUT: compat ProxiCraft BeyondStorage2
EXPECT: 
  conflict_type: "direct_patch"
  target: "XUiM_PlayerInventory.GetItemCount"
  mods: ["ProxiCraft", "BeyondStorage2"]
  locations: [exact file:line for each]
```

---

## Implementation Plan

### Project Structure

```
7D2D-DecompilerScript/
├── Decompile-7D2D.ps1              # Existing decompiler
├── TOOLKIT_DESIGN.md               # This document
├── toolkit/
│   ├── schema.sql                  # SQLite schema (comprehensive)
│   ├── CallGraphExtractor/         # C# Roslyn-based extractor ✅
│   │   ├── CallGraphExtractor.csproj
│   │   ├── Program.cs              # CLI entry point
│   │   ├── RoslynParser.cs         # Roslyn workspace + semantic analysis
│   │   ├── CallExtractor.cs        # Internal + external call extraction
│   │   ├── SqliteWriter.cs         # Database persistence
│   │   ├── XmlDefinitionExtractor.cs    # Game XML parsing (NEW)
│   │   ├── XmlPropertyAccessExtractor.cs # Code→XML linkage (NEW)
│   │   ├── ModParser.cs            # Mod C# analysis (NEW)
│   │   └── ModXmlChangeParser.cs   # Mod XML changes (NEW)
│   ├── QueryDb/                    # C# query tool ✅
│   │   ├── QueryDb.csproj
│   │   └── Program.cs              # Custom SQL + diagnostic queries
│   ├── 7d2d-tools/                 # Python CLI (future)
│   │   ├── pyproject.toml
│   │   └── 7d2d_tools/
│   └── callgraph_full.db           # Generated database
└── README.md
```

### CLI Usage (Current)

**Build database:**
```powershell
cd toolkit/CallGraphExtractor
dotnet run -c Release -- \
  --source "path/to/7D2DCodebase" \
  --output "../callgraph.db" \
  --game-root "C:\Steam\steamapps\common\7 Days To Die" \
  --game-data "C:\Steam\...\Data\Config" \
  --mods "path/to/Mods" \
  --verbose
```

**Query database:**
```powershell
cd toolkit/QueryDb
# Default diagnostic queries
dotnet run -- "../callgraph.db"

# Custom SQL
dotnet run -- "../callgraph.db" "SELECT * FROM methods WHERE name = 'Update' LIMIT 10"
```

### Phase 1: Foundation (Commits 1-4) ✅ COMPLETE

#### Commit 1: Project scaffolding ✅
- [x] Create `toolkit/` directory structure
- [x] Create `schema.sql` with tables: `methods`, `calls`, `types`
- [x] Create empty C# project `CallGraphExtractor.csproj`
- [x] Create empty Python project `pyproject.toml`
- [x] **COMMIT: "Add toolkit project scaffolding"**

#### Commit 2: SQLite schema + basic C# structure ✅
- [x] Define full schema in `schema.sql`
- [x] Add Roslyn NuGet packages to C# project
- [x] Create `SqliteWriter.cs` - connects to DB, creates tables
- [x] Test: Can create empty database with schema
- [x] **COMMIT: "Add SQLite schema and database writer"**

#### Commit 3: Roslyn parser - method extraction ✅
- [x] Create `RoslynParser.cs` - load workspace, enumerate files
- [x] Extract: class name, method name, signature, file:line
- [x] Write method nodes to SQLite
- [x] Test: Parse single file, verify methods in DB
- [x] **COMMIT: "Add Roslyn method extraction"**

#### Commit 4: Roslyn parser - call extraction ✅
- [x] Extend parser to find method invocations
- [x] Resolve invocation targets (which exact method is called)
- [x] Write call edges to SQLite
- [x] Test: Parse file with calls, verify edges in DB
- [x] **COMMIT: "Add call graph edge extraction"**

#### Commit 4b: Comprehensive DLL auto-discovery ✅ (ENHANCEMENT)
- [x] Recursive DLL scanning from game install directory
- [x] Load 373 metadata references (187 .NET + 186 game DLLs)
- [x] Improved call resolution: 58.8% → **85.6%** (165,271 resolved calls)
- [x] External call tracking for Unity/BCL boundary crossings
- [x] **COMMIT: "feat: Comprehensive auto-discovery with 85% call resolution"**

### Phase 2: Basic Queries (Commits 5-7) ✅ COMPLETE

#### Commit 5: C# Query Tool (Pivoted from Python) ✅
- [x] Create `QueryDb/` C# project
- [x] SQLite connection and diagnostic queries
- [x] Custom SQL support via command line
- [x] **COMMIT: "Add C# query tool with custom SQL support"**

#### Commit 6: Direct callers/callees queries ✅
- [x] Implement `callers <method>` - convenience wrapper
- [x] Implement `callees <method>` - convenience wrapper
- [x] Handle ambiguous method names (list matches)
- [x] **COMMIT: "Add comprehensive CLI commands to QueryDb"**

#### Commit 7: Path finding ✅
- [x] Implement `chain <from> <to>` - recursive CTE-based path finding
- [x] Chose SQL CTE approach (no external library needed)
- [x] Output path as indented tree view

### Phase 3: Full-Text Search (Commits 8-9) ✅ COMPLETE

#### Commit 8: FTS5 indexing ✅
- [x] Add `method_bodies` FTS5 virtual table to schema
- [x] Extend C# extractor to store method body text
- [x] Rebuild test database with body text (32,013 methods indexed)
- [x] **COMMIT: "Add FTS5 method body indexing"**

#### Commit 9: Search via SQL ✅
- [x] FTS5 queryable via custom SQL in QueryDb
- [x] Porter stemming enabled for flexible matching
- [x] Convenience `search <keyword>` command wrapper with snippet highlighting

### Phase 4: XML Game Data (NEW PHASE) ✅ COMPLETE

#### Commit 10: XML Definition Extraction ✅
- [x] Create `XmlDefinitionExtractor.cs`
- [x] Parse all 42 game XML config files
- [x] Extract 131,960 definitions with xpath, properties, values
- [x] **COMMIT: "feat: Comprehensive XML and Mod extraction"**

#### Commit 11: XML Property Access Detection ✅
- [x] Create `XmlPropertyAccessExtractor.cs`
- [x] Detect `GetValue`, `GetInt`, `GetBool`, `Contains`, etc. calls
- [x] Extract string literal property names
- [x] Link code to XML properties (746 patterns found)
- [x] **COMMIT: included in above**

### Phase 5: Mod Compatibility (Commits 12-15) ✅ COMPLETE

#### Commit 12: Harmony patch parser ✅
- [x] Create `ModParser.cs` - find [HarmonyPatch] attributes
- [x] Extract: target class, target method, patch type
- [x] Detect via naming conventions (Prefix, Postfix, Transpiler)
- [x] Store in `harmony_patches` table
- [x] Test: Parsed ProxiCraft + AudibleBreakingGlassJars (9 patches found)
- [x] **COMMIT: included in XML commit**

#### Commit 13: XML change parser ✅
- [x] Create `ModXmlChangeParser.cs` - parse 7D2D XML mod format
- [x] Extract: file, xpath, operation, attribute
- [x] Store in `xml_changes` table
- [x] **COMMIT: included in XML commit**

#### Commit 14: Direct conflict detection ✅
- [x] Implement `compat` command
- [x] SQL: Find same-method patches across mods  
- [x] SQL: Find same-xpath changes across mods
- [x] Output conflict report with exact locations
- [x] **COMMIT: "Add comprehensive CLI commands to QueryDb"**

#### Commit 15: Load order inference ✅
- [x] Analyze patch types (Transpiler vs Prefix/Postfix)
- [x] Generate load order recommendations
- [x] Add to compat output

### Phase 6: Polish & Documentation (Commits 16-18) 🔄 PARTIAL

#### Commit 16: Type hierarchy queries ✅
- [x] Add `impl <method>` command
- [x] Query to find all overrides with inheritance info
- [x] Shows modifier (virtual/override/abstract) and base type
- [x] **COMMIT: "Add comprehensive CLI commands to QueryDb"**

#### Commit 17: AI context document ✅
- [x] Create `AI_CONTEXT.md` with usage examples
- [x] Document query patterns for common problems
- [x] Include expected output formats

#### Commit 18: README and integration ✅
- [x] Update main README with toolkit usage
- [x] Add PowerShell wrapper to call toolkit after decompile
- [x] End-to-end test: decompile → build DB → query

### Phase 7: Performance Analysis (NEW PHASE) ✅ COMPLETE

Database supports comprehensive performance queries via `perf` command:
- [x] `perf updates` — Find largest Update/LateUpdate/FixedUpdate methods
- [x] `perf getcomponent` — Find Update methods with expensive GetComponent calls
- [x] `perf find` — Find methods using FindObjectsOfType (O(n) scan)
- [x] `perf strings` — Find Update methods with string allocations
- [x] `perf linq` — Find LINQ usage in hot paths
- [x] `perf` (no arg) — Run all categories
- [x] **COMMIT: "Add comprehensive CLI commands to QueryDb"**

---

## Test Data Setup

To run these tests, we need:

1. **Historical mod versions from git** — Extract buggy versions without affecting working directory
2. **Game code snapshot** from 7D2DCodebase (already have)
3. **Expected results JSON** for each test case

### Extracting Historical Test Data from Git

Git can extract old file versions without checkout (working directory stays untouched):

```powershell
# List commits with bug fixes to find "before" versions
git log --oneline --grep="fix" -- ProxiCraft/
git log --oneline --grep="Bug" -- ProxiCraft/

# Extract specific file at a commit (before the fix)
git show <commit>:ProxiCraft/ProxiCraft.cs > test_data/proxicraft_before_fix8c.cs

# Extract entire mod folder at a historical point
git archive <commit> ProxiCraft/ | tar -x -C ./test_data/historical/

# Compare what changed in a fix (to understand what the tool should detect)
git diff <before_commit> <after_commit> -- ProxiCraft/
```

**Key commits to extract (find via `git log`):**
- Before Fix #8c (item duplication) — Test Case 3
- Before Bug #1 fix (radial menu) — Test Case 1  
- Before Bug #3 fix (workstation) — Test Case 2
- Before Bug #4 fix (Take Like) — Test Case 5
- Trader selling code before removal — Test Case 4

Each historical version becomes a test fixture: "Given this code, tool should flag X problem."

Tests validate: "Given this mod code and this game code, does the tool detect the known issue?"
