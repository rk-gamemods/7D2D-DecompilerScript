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

---

## Development Roadmap

### Phase 1: Call Graph Extractor
**Build call graph extractor** using Roslyn — Parse all ~4,400 game files with full semantic analysis, extract methods + their invocations with accurate type resolution, store as nodes/edges in SQLite. This is the high-value core: query "what calls `DecItem`?" or "trace path from `Craft` to `RemoveItems`".

### Phase 2: Full-Text Search
**Add FTS5 to same database** — Index method bodies for keyword search. Complements call graph: find entry points by name, then trace their flow. One DB file, two query modes.

### Phase 3: CLI Tool
**Create Python CLI (`7d2d-tools`)** with commands:
- `build` — Parse codebase → DB
- `callers <method>` — Who calls this method?
- `callees <method>` — What does this method call?
- `chain <from> <to>` — Trace execution path between methods
- `search <keyword>` — FTS5 keyword search
- `compat <mod1> <mod2> ...` — Check mod compatibility, detect conflicts

### Phase 4: Mod Compatibility Detection
**Add mod compatibility detection** — Parse Harmony patches and XML changes from mods, detect direct conflicts (same method patched) and XML collisions (same node modified), report exact file:line locations.

### Phase 5: AI Integration
**Include `AI_CONTEXT.md` instructions** — Document how an AI should use these tools. Example queries, output format, what to ask for different problem types.

---

## Architecture Decisions

### 1. Parser Choice — Roslyn for Accuracy

**Decision: Use Roslyn.** Speed isn't a concern—this runs once per game update (1-2x/month), so a few minutes is fine. Robustness matters more.

**Roslyn** is Microsoft's official C# compiler-as-a-library. It doesn't just parse—it *compiles* the code mentally. It knows that `bag.DecItem()` refers to `Bag.DecItem(ItemValue, int)` specifically, resolves inheritance, understands overloads. Like having a C# expert read and annotate every line.

What Roslyn gives us:
- **Accurate type resolution** — Knows exactly which overload is called
- **Inheritance tracking** — Knows `EntityVehicle : EntityAlive`
- **Full signatures** — Can detect when a method signature changes between game versions
- **Cross-reference** — "Find all implementations of this interface"

**Implementation note:** Roslyn needs referenced DLLs to fully resolve types. We'll point it at the game's managed DLLs (same ones we decompile from). The decompiled .cs files + original DLLs together give Roslyn everything it needs.

### 2. Graph Library Choice — igraph over NetworkX

**Decision: Use igraph instead of NetworkX.** Both are Python graph libraries, but igraph is 10-50x faster for our graph size (50-100K methods, 200-500K edges). NetworkX is fine for learning/prototyping but becomes sluggish at scale.

| Aspect | NetworkX | igraph |
|--------|----------|--------|
| Speed (path finding) | ~seconds | ~milliseconds |
| Memory | Higher | Lower |
| Windows install | Easy | Easy (`pip install igraph`) |
| API style | Pythonic dicts | C-library wrapper |
| Learning curve | Gentle | Steeper |

**Hybrid approach:** Use SQLite for simple queries ("who calls X?") without loading the graph at all. Only load igraph for complex analysis (multi-path finding, centrality, reachability).

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

### 4. Output Format — JSON Only (for now)

**Decision: JSON output only.** Structured data lets AI reason programmatically about depth, branches, multiple paths. Easy to:
- Filter results
- Chain queries
- Parse into follow-up questions

Human-readable text reports are a future enhancement (separate reporting layer).

```bash
7d2d-tools chain CraftingManager.Craft Bag.DecItem
```

Output:
```json
{
  "query": "chain",
  "from": "CraftingManager.Craft", 
  "to": "Bag.DecItem",
  "paths": [
    {
      "depth": 3,
      "chain": ["CraftingManager.Craft", "XUiM_PlayerInventory.RemoveItems", "Bag.DecItem"],
      "files": ["CraftingManager.cs:234", "XUiM_PlayerInventory.cs:567", "Bag.cs:123"]
    }
  ],
  "total_paths": 1
}
```

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
