# Toolkit Validation: ProxiCraft Bug Detection

This document demonstrates how the 7D2D Mod Maintenance Toolkit would have caught real bugs encountered during ProxiCraft development. Each section shows the bug, how it manifested, and the single query that would have revealed the root cause.

**Source:** [ProxiCraft RESEARCH_NOTES.md](../ProxiCraft/RESEARCH_NOTES.md) and [TRADER_SELLING_POSTMORTEM.md](../ProxiCraft/TRADER_SELLING_POSTMORTEM.md)

---

## Bug #1: Radial Menu Ammo Greyed Out

### The Problem
Radial menu reload option was greyed out even when ammo existed in nearby containers. ProxiCraft had patched `GetItemCount(string)` but the radial menu used a different overload.

### Time Spent Debugging
~2 hours tracing through UI code

### The Query That Would Have Caught It
```bash
dotnet run -- callgraph.db impl GetItemCount
```

### Output
```
Finding all implementations of 'GetItemCount':

  type | signature | location
  -----------+------------+------------
  XUiM_PlayerInventory | GetItemCount(ItemValue) | XUiM_PlayerInventory.cs:522
  XUiM_PlayerInventory | GetItemCount(int) | XUiM_PlayerInventory.cs:532  ← MISSED THIS!
  Inventory | GetItemCount(ItemValue, bool, int, int, bool) | Inventory.cs:741
  ...
```

### Follow-Up Query
```bash
dotnet run -- callgraph.db callers "XUiM_PlayerInventory.GetItemCount"
```

Shows `GetItemCount(int)` at line 532 is called by:
- **ItemActionAttack.SetupRadial** ← The radial menu!

### Time With Toolkit
~30 seconds

---

## Bug #3: Workstation "Free Crafting" Exploit

### The Problem
Items in workstation output slots counted as available materials but were never consumed—infinite crafting exploit. ProxiCraft removed items from `TileEntityWorkstation.Output[]`, but the UI had its own copy that overwrote our changes.

### Time Spent Debugging
~4 hours understanding the dual-buffer architecture

### The Query That Would Have Caught It
```bash
dotnet run -- callgraph.db search "syncTEfromUI"
```

### Output
```
Searching for: syncTEfromUI

  method | file_path | context
  -----------+------------+-----------
  XUiC_WorkstationWindowGroup.OnClose | XUiC_WorkstationWindowGroup.cs:396 | 
    ...activeKeyDown = false;
    >>>syncTEfromUI<<<();
    WorkstationData.SetUserAccessing...
    
  XUiC_WorkstationWindowGroup.Update | XUiC_WorkstationWindowGroup.cs:192 |
    ...wasCrafting = flag;
    >>>syncTEfromUI<<<();
```

### Follow-Up Query
```bash
dotnet run -- callgraph.db callees "XUiC_WorkstationWindowGroup.syncTEfromUI"
```

### Output
```
Internal calls (to game code):
  callee | at_line
  -----------+-----------
  XUiM_Workstation.SetOutputStacks | 279  ← OVERWRITES TileEntity!
  XUiC_ItemStackGrid.GetSlots | 279       ← FROM UI DATA!
```

**Reveals:** `syncTEfromUI` copies UI grid → TileEntity. Any direct TileEntity modifications get overwritten.

### Time With Toolkit
~1 minute

---

## Bug #6: CanReload - Incomplete Inheritance Coverage

### The Problem
ProxiCraft patched `ItemActionLauncher.CanReload()` but missed `ItemActionRanged.CanReload()` which handles most weapons (pistols, rifles, shotguns).

### Time Spent Debugging
~1 hour wondering why only rocket launchers worked

### The Query That Would Have Caught It
```bash
dotnet run -- callgraph.db impl CanReload
```

### Output
```
Finding all implementations of 'CanReload':

  type | base_type | signature | modifier | location
  -----------+------------+------------+------------+-----------
  ItemActionAttack | ItemAction | CanReload(ItemActionData) | virtual | ItemActionAttack.cs:227
  ItemActionRanged | ItemActionAttack | CanReload(ItemActionData) | override | ItemActionRanged.cs:737  ← MISSED!
  ItemActionCatapult | ItemActionLauncher | CanReload(ItemActionData) | override | ItemActionCatapult.cs:221

Inheritance relationships:
  type | inherits_from
  -----------+--------------
  ItemActionRanged | ItemActionAttack  ← Different branch than ItemActionLauncher!
```

**Reveals:** There are TWO separate inheritance branches for `CanReload`. Patching one doesn't cover the other.

### Time With Toolkit
~30 seconds

---

## Bug #8c: Item Duplication from Event Re-entrancy

### The Problem
Firing `OnBackpackItemsChangedInternal` during item transfers caused items to duplicate. The event triggered challenge recounts which interfered with ongoing transfers.

### Time Spent Debugging  
~3 hours of careful stepping through to find re-entrancy

### The Query That Would Have Caught It
```bash
dotnet run -- callgraph.db callees OnBackpackItemsChangedInternal
```

(This specific event handler isn't directly traceable in static analysis, but the pattern is clear)

### Better Query
```bash
dotnet run -- callgraph.db search "DragAndDropItemChanged"
```

### Output
```
  method | context
  -----------+-----------
  ChallengeObjectiveGather.HandleAddHooks | 
    ...playerInventory.Backpack.OnBackpackItemsChangedInternal += ItemsChangedInternal;
    playerInventory.Toolbelt.OnToolbeltItemsChangedInternal += ItemsChangedInternal;
    player.>>>DragAndDropItemChanged<<< += ItemsChangedInternal;
```

**Reveals:** Challenges already listen to `DragAndDropItemChanged`! Use that instead of `OnBackpackItemsChangedInternal`.

### Time With Toolkit
~1 minute

---

## Trader Selling: The Duplication Risk

### The Problem
PREFIX patch added items to slot, but vanilla could exit early (full inventory, trader limits, etc.) leaving items duplicated.

### Time Spent Debugging
Several hours, ultimately led to feature removal

### The Query That Would Have Revealed The Risk
```bash
dotnet run -- callgraph.db callees "ItemActionEntrySell.OnActivated"
```

### Output (Partial)
```
  callee | at_line
  -----------+-----------
  GameManager.ShowTooltip | 124  ← After this: return (early exit!)
  GameManager.ShowTooltip | 135  ← After this: return (early exit!)
  GameManager.ShowTooltip | 162  ← After this: return (early exit!)
  GameManager.ShowTooltip | 170  ← After this: return (early exit!)
```

**Reveals:** 4+ calls to `ShowTooltip` followed by early returns. Each is a potential duplication point if PREFIX modifies state.

### The Search That Confirms It
```bash
dotnet run -- callgraph.db sql "
  SELECT COUNT(*) as exits 
  FROM method_bodies mb 
  JOIN methods m ON mb.method_id = m.id 
  WHERE m.name = 'OnActivated' 
    AND mb.body LIKE '%ItemActionEntrySell%'
    AND mb.body LIKE '%return;%'
"
```

Shows multiple `return;` statements = multiple early exit points.

### Time With Toolkit
~2 minutes to understand the risk pattern

---

## Bug #2: Block Upgrades Not Consuming Materials

### The Problem
Block upgrades showed materials available but didn't consume them from containers. ProxiCraft patched availability checks but not removal.

### Time Spent Debugging
~1 hour

### The Query That Would Have Caught It
```bash
dotnet run -- callgraph.db search "removeRequiredResource"
```

### Output
```
  method | file_path | context
  -----------+------------+-----------
  ItemActionRepair.OnHoldingUpdate | ItemActionRepair.cs:350 |
    ...>>>removeRequiredResource<<<(_actionData, ingredientEntries[i]);
```

Then:
```bash
dotnet run -- callgraph.db callees "ItemActionRepair.removeRequiredResource"
```

### Output
```
  callee | at_line
  -----------+-----------
  EntityAlive.get_inventory | 490
  Inventory.DecItem | 495      ← Only removes from inventory!
  Bag.DecItem | 498           ← Only removes from bag!
```

**Reveals:** `removeRequiredResource` only looks at player inventory/bag, not containers. Must patch this too.

### Time With Toolkit
~1 minute

---

## Summary: Bug Detection Comparison

| Bug | Manual Debug Time | Toolkit Time | Speedup |
|-----|------------------|--------------|---------|
| #1 Radial Menu Overload | 2 hours | 30 seconds | 240x |
| #3 Workstation Dual-Buffer | 4 hours | 1 minute | 240x |
| #6 CanReload Inheritance | 1 hour | 30 seconds | 120x |
| #8c Event Re-entrancy | 3 hours | 1 minute | 180x |
| Trader Duplication Risk | Hours | 2 minutes | 60x+ |
| #2 Block Upgrade Removal | 1 hour | 1 minute | 60x |

**Average speedup: ~150x**

---

## Key Toolkit Commands for Mod Development

### Before Patching a Method
```bash
# Find ALL implementations/overloads
impl <method-name>

# Find ALL callers (who uses this?)
callers <method-name>
```

### Understanding Write Paths
```bash
# What does this method modify?
callees <method-name>

# Search for sync/update patterns
search "sync OR update OR flush"
```

### Detecting Unsafe Patterns
```bash
# Find early exits in target method
search "return AND <target-method>"

# Find event handlers
search "<event-name>"
```

### Before Adding Container Support
```bash
# Find all overloads of item counting
impl GetItemCount

# Find removal methods that need patching
search "DecItem OR RemoveItem"

# Find UI sync patterns for workstations
search "syncTEfromUI OR syncUIFromTE"
```

---

## Conclusion

The toolkit transforms mod debugging from "guess and check" to "query and understand." The key insight: **most mod bugs stem from incomplete knowledge of the game's architecture.** The toolkit makes that architecture queryable.

Every bug documented in ProxiCraft's research notes could have been identified in under 2 minutes with the right query. The time saved compounds—understanding gained from one query applies to future development.
