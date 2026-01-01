# Behavioral Validation System Design

## Executive Summary

Enhance the 7D2D Mod Toolkit to validate not just "will this mod break?" but "will this mod do what the author intended?" by introducing behavioral specifications that describe expected mod functionality in human-readable terms.

**Core Insight**: Most mod bugs aren't crashes - they're mismatches between what the code does and what the human expected. A trader price change that goes the wrong direction, a spawn rate that's 10x instead of 2x, a feature that only works in single-player when multiplayer was intended.

---

## Problem Statement

### Current State
```
Developer writes code → Code compiles → QA checks patches exist → ???
                                                                  ↓
                                          Hope it works as intended
```

### Desired State
```
Developer writes code → Code compiles → Behavioral spec generated/validated
                                                    ↓
                              Human reviews: "Wait, this makes zombies WEAKER?"
                                                    ↓
                                          Bug caught before release
```

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        BEHAVIORAL VALIDATION SYSTEM                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐              │
│  │  Mod Source  │    │  Game Code   │    │  Game XML    │              │
│  │  (.cs files) │    │  (callgraph) │    │  (configs)   │              │
│  └──────┬───────┘    └──────┬───────┘    └──────┬───────┘              │
│         │                   │                   │                       │
│         └───────────────────┼───────────────────┘                       │
│                             ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    ANALYSIS ENGINE                               │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │   │
│  │  │   Harmony   │  │    XML      │  │   Config    │              │   │
│  │  │   Analyzer  │  │   Differ    │  │   Parser    │              │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘              │   │
│  │                          │                                       │   │
│  │                          ▼                                       │   │
│  │  ┌─────────────────────────────────────────────────────────┐    │   │
│  │  │              EFFECT CALCULATOR                           │    │   │
│  │  │  - What values change and by how much?                   │    │   │
│  │  │  - What code paths are modified?                         │    │   │
│  │  │  - What conditions gate the changes?                     │    │   │
│  │  └─────────────────────────────────────────────────────────┘    │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                             │                                           │
│                             ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    SPEC GENERATOR                                │   │
│  │  Produces human-readable behavioral specification                │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                             │                                           │
│              ┌──────────────┼──────────────┐                           │
│              ▼              ▼              ▼                           │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                 │
│  │  .modspec    │  │   Terminal   │  │   Markdown   │                 │
│  │  (machine)   │  │   Output     │  │   Report     │                 │
│  └──────────────┘  └──────────────┘  └──────────────┘                 │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    VALIDATOR                                     │   │
│  │  Compares generated spec against author-provided expected spec   │   │
│  │  Flags mismatches as potential behavioral bugs                   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Specification Format (.modspec)

### Design Principles

1. **Human-first**: A non-coder should understand what the mod does
2. **Machine-verifiable**: System can generate and compare specs
3. **Hierarchical**: High-level summary → detailed breakdowns
4. **Diff-friendly**: Changes between versions are clear
5. **Composable**: Multiple mods' specs can be merged to show net effect

### File Format: YAML

Chosen for readability, comment support, and tooling availability.

### Specification Structure

```yaml
# ProxiCraft.modspec
# Auto-generated: 2026-01-01 14:30:00 UTC
# Generator version: 1.0.0

meta:
  mod_name: "ProxiCraft"
  mod_version: "1.0.0"
  game_version: "V1.0 b336"
  spec_version: "1.0"
  generated_from: "source"  # or "binary" if decompiled
  
# =============================================================================
# SUMMARY - What does this mod do in plain English?
# =============================================================================
summary:
  one_liner: "Pull crafting materials from nearby containers automatically"
  
  key_features:
    - "Crafting uses items from nearby storage containers (15m default)"
    - "Reloading pulls ammo from nearby containers"
    - "Vehicle refueling uses fuel from nearby containers"
    - "Trader purchases use currency from nearby containers"
    - "Configurable storage source priority order"
    
  does_not_do:
    - "Does NOT allow selling items from containers to traders"
    - "Does NOT affect item spawns or loot tables"
    - "Does NOT modify any item stats or recipes"

# =============================================================================
# CONFIGURATION - What can users change?
# =============================================================================
configuration:
  file: "config.json"
  
  settings:
    - name: "range"
      type: "float"
      default: 15.0
      unit: "blocks/meters"
      description: "Maximum distance to search for containers"
      constraints:
        min: 0
        special_values:
          - value: -1
            meaning: "Unlimited range (all loaded chunks)"
          - value: 0
            meaning: "Disabled (only player inventory)"
            
    - name: "storagePriority"
      type: "dictionary<string, string>"
      description: "Order to search storage sources (lower = first)"
      default:
        Drone: "1"
        DewCollector: "2"
        Workstation: "3"
        Container: "4"
        Vehicle: "5"
      valid_keys:
        - "Drone"
        - "DewCollector"  
        - "Workstation"
        - "Container"
        - "Vehicle"
      behavior:
        missing_keys: "Appended in default order with warning"
        typos: "Fuzzy-matched to valid keys if unambiguous"
        duplicate_values: "Sorted alphabetically by key name"

    - name: "pullFromVehicles"
      type: "bool"
      default: true
      description: "Include vehicle storage in searches"
      
    # ... more settings ...

# =============================================================================
# BEHAVIORS - What does the mod actually DO?
# =============================================================================
behaviors:

  # ---------------------------------------------------------------------------
  # CRAFTING BEHAVIOR
  # ---------------------------------------------------------------------------
  - id: "crafting_from_storage"
    category: "crafting"
    enabled_by: "enableForCrafting"
    
    trigger:
      event: "Player attempts to craft item"
      method: "XUiM_PlayerInventory.HasItems"
      
    effect:
      description: "Counts items in nearby containers as available for crafting"
      
      vanilla_behavior:
        description: "Only counts items in player backpack and toolbelt"
        sources:
          - "Player.bag"
          - "Player.inventory (toolbelt)"
          
      modded_behavior:
        description: "Also counts items in nearby storage sources"
        sources:
          - "Player.bag"
          - "Player.inventory (toolbelt)"
          - "Nearby containers within range"
        order: "Determined by storagePriority config"
        
    conditions:
      - "config.enableForCrafting == true"
      - "config.modEnabled == true"
      - "Container within config.range blocks"
      - "Container not locked by another player"
      - "Container owned by player or unlocked"
      
    side_effects:
      - "Items removed from containers when craft completes"
      - "Container marked as modified (triggers save)"
      
  # ---------------------------------------------------------------------------
  # STORAGE PRIORITY BEHAVIOR
  # ---------------------------------------------------------------------------
  - id: "storage_priority_ordering"
    category: "internal"
    enabled_by: "always"
    
    trigger:
      event: "Items need to be removed from storage"
      method: "ContainerManager.RemoveItems"
      
    effect:
      description: "Storage sources are checked in configurable priority order"
      
      algorithm:
        1: "Parse storagePriority config at mod startup"
        2: "Sort storage types by config value (alphanumeric)"
        3: "Append any missing types in default order"
        4: "Cache sorted order for duration of session"
        5: "When removing items, iterate sources in cached order"
        6: "Stop iteration when required amount is fulfilled"
        
      example:
        config:
          Vehicle: "1"
          Drone: "2"
          Container: "3"
        result_order:
          - "Vehicle (explicitly set to 1)"
          - "Drone (explicitly set to 2)"
          - "Container (explicitly set to 3)"
          - "DewCollector (missing, appended)"
          - "Workstation (missing, appended)"
        warnings:
          - "storagePriority missing: DewCollector, Workstation"
          
    fuzzy_matching:
      description: "Typos in config keys are auto-corrected when unambiguous"
      examples:
        - input: "Dron"
          matches: "Drone"
          reason: "Sequential match D-r-o-n = 4/4 = 100%"
        - input: "Vehicel"
          matches: "Vehicle"
          reason: "Sequential match V-e-h-i-c-?-l = 6/7 = 86%"
        - input: "D"
          matches: null
          reason: "Ambiguous - matches both Drone and DewCollector"

  # ---------------------------------------------------------------------------
  # RELOAD BEHAVIOR
  # ---------------------------------------------------------------------------
  - id: "reload_from_storage"
    category: "combat"
    enabled_by: "enableForReload"
    
    trigger:
      event: "Player reloads weapon"
      method: "ItemActionRanged.ConsumeAmmo"
      
    effect:
      description: "Pulls ammo from nearby containers if not enough in inventory"
      
      vanilla_behavior:
        description: "Only uses ammo from player backpack and toolbelt"
        
      modded_behavior:  
        description: "Also pulls from nearby storage in priority order"
        
    conditions:
      - "config.enableForReload == true"
      - "Ammo type matches weapon requirement"
      - "Player inventory doesn't have enough ammo"

# =============================================================================
# PATCHES - Technical details of game modifications
# =============================================================================
patches:
  harmony:
    - target: "XUiM_PlayerInventory.HasItems"
      type: "Postfix"
      purpose: "Add container item counts to availability check"
      callers: 8
      
    - target: "XUiM_PlayerInventory.GetItemCount"
      type: "Postfix"  
      purpose: "Include container items in count"
      callers: 15
      
    - target: "ItemActionRanged.ConsumeAmmo"
      type: "Prefix"
      purpose: "Pre-stage ammo from containers before consumption"
      callers: 3
      
    # ... 39 more patches ...
    
  xml:
    # This mod doesn't modify XML
    changes: []

# =============================================================================
# COMPATIBILITY - How does this mod interact with others?
# =============================================================================
compatibility:
  conflicts:
    - mod: "BeyondStorage2"
      severity: "critical"
      reason: "Both mods patch the same inventory methods"
      recommendation: "Use only one storage extension mod"
      
  compatible:
    - mod: "ServerTools"
      notes: "No overlapping functionality"
      
  requires:
    - mod: "0_TFP_Harmony"
      version: ">=2.0"
      reason: "Harmony patching framework"

# =============================================================================
# MULTIPLAYER - Network behavior
# =============================================================================
multiplayer:
  sync_required: true
  
  behaviors:
    - name: "Container locking"
      description: "Containers being accessed by one player are locked for others"
      mechanism: "NetPackagePCLock broadcast on container open/close"
      
    - name: "Item removal sync"
      description: "When items removed from container, all clients see update"
      mechanism: "TileEntity.SetModified() triggers network sync"
      
  edge_cases:
    - scenario: "Two players craft simultaneously using same container"
      handling: "First player locks container, second falls back to inventory"
      
    - scenario: "Player A has mod, Player B doesn't"
      handling: "Mod detects via handshake, logs warning about potential desync"
```

---

## Spec Generation Process

### Phase 1: Static Analysis

```
┌─────────────────────────────────────────────────────────────────────────┐
│ INPUT: Mod source files (.cs)                                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. PARSE HARMONY ATTRIBUTES                                            │
│     [HarmonyPatch(typeof(X), "Method")]                                 │
│     [HarmonyPrefix/Postfix/Transpiler]                                  │
│                                                                          │
│  2. EXTRACT PATCH BODIES                                                │
│     - What parameters are accessed?                                      │
│     - What values are modified?                                          │
│     - What conditions gate the changes?                                  │
│                                                                          │
│  3. TRACE DATA FLOW                                                     │
│     - Where do modified values come from?                                │
│     - Where do they flow to?                                             │
│     - What's the net transformation?                                     │
│                                                                          │
│  4. IDENTIFY CONFIG DEPENDENCIES                                        │
│     - What config values affect behavior?                                │
│     - What are the boolean gates?                                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Phase 2: Semantic Interpretation

```
┌─────────────────────────────────────────────────────────────────────────┐
│ INPUT: Extracted patch information + Game callgraph                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. CLASSIFY PATCH PURPOSE                                              │
│     Pattern: "Postfix on GetItemCount that adds to __result"            │
│     Interpretation: "Increases reported item count"                      │
│                                                                          │
│  2. IDENTIFY AFFECTED SYSTEMS                                           │
│     Method: XUiM_PlayerInventory.GetItemCount                           │
│     Callers: CraftingManager, RecipeTracker, QuestObjective...          │
│     Systems: Crafting, Quest tracking, UI display                        │
│                                                                          │
│  3. DETERMINE NET EFFECT                                                │
│     Change: Item counts increased by container contents                  │
│     Result: "Player appears to have more items than in inventory"        │
│                                                                          │
│  4. GENERATE NATURAL LANGUAGE                                           │
│     "When checking if player has items, also count items in              │
│      nearby containers within the configured range"                      │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Phase 3: XML Differencing

```
┌─────────────────────────────────────────────────────────────────────────┐
│ INPUT: Mod XML files + Vanilla XML files                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. PARSE XPATH OPERATIONS                                              │
│     <set xpath="/items/item[@name='gunPistol']/property[@name='Tags']"  │
│          >perkGunslinger,weapon</set>                                    │
│                                                                          │
│  2. COMPUTE DELTA                                                       │
│     Before: Tags = "perkGunslinger"                                      │
│     After:  Tags = "perkGunslinger,weapon"                               │
│     Delta:  Added "weapon" tag                                           │
│                                                                          │
│  3. INTERPRET GAME MEANING                                              │
│     "weapon" tag → Item affected by weapon-related perks                 │
│     Net effect: "Pistol now benefits from generic weapon perks"          │
│                                                                          │
│  4. DETECT CONFLICTS                                                    │
│     If Mod A sets Tags = "X" and Mod B sets Tags = "Y"                   │
│     → Last loaded wins, earlier changes lost                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Validation Process

### Command Interface

```bash
# Generate spec from mod (for human review)
QueryDb callgraph.db spec generate <mod-path> [--output spec.modspec]

# Validate mod against its spec (CI/CD check)
QueryDb callgraph.db spec validate <mod-path> --spec expected.modspec

# Compare two mods for conflicts
QueryDb callgraph.db spec conflict <mod-a-path> <mod-b-path>

# Show net effect of multiple mods combined
QueryDb callgraph.db spec merge <mod-a> <mod-b> <mod-c> --output combined.modspec

# Diff two versions of same mod
QueryDb callgraph.db spec diff <old-spec.modspec> <new-spec.modspec>
```

### Validation Output

```
================================================================================
                         BEHAVIORAL VALIDATION REPORT
================================================================================

Mod: ProxiCraft v1.0.0
Spec: ProxiCraft.modspec (author-provided)
Generated: 2026-01-01 15:00:00 UTC

SUMMARY
───────
✅ 47 behaviors validated
⚠️  2 warnings (non-breaking)
❌ 1 mismatch detected

VALIDATED BEHAVIORS
───────────────────
✅ crafting_from_storage
   Expected: "Counts items in nearby containers as available for crafting"
   Actual:   Confirmed - XUiM_PlayerInventory.HasItems postfix adds container counts
   
✅ storage_priority_ordering  
   Expected: "Storage sources checked in configurable priority order"
   Actual:   Confirmed - OrderStorages() sorts by _cachedOrder before iteration
   
✅ reload_from_storage
   Expected: "Pulls ammo from nearby containers if not enough in inventory"
   Actual:   Confirmed - ItemActionRanged.ConsumeAmmo prefix pre-stages ammo

WARNINGS
────────
⚠️  Config setting 'enableTraderSelling' exists but behavior is disabled
    Spec says: "Does NOT allow selling items from containers"
    Code has:  Config property exists but feature code commented/removed
    Impact:    Cosmetic - setting has no effect
    Suggestion: Remove deprecated setting or document as legacy
    
⚠️  Multiplayer handshake uses ModEvents.PlayerSpawnedInWorld
    This event may fire before all chunks loaded
    Impact:    Container scan may miss some storages on initial spawn
    Suggestion: Add delayed re-scan or document limitation

MISMATCHES
──────────
❌ BEHAVIORAL MISMATCH: vehicle_repair_from_storage
   
   Spec says:
     "Vehicle repair uses repair kits from nearby containers"
     
   Code analysis found:
     XUiM_Vehicle.RepairVehicle prefix moves repair kit to player bag
     BUT: Only moves kit if player bag has space
     
   Discrepancy:
     Spec implies: "Always works if container has repair kit"
     Actual:       "Fails silently if player bag is full"
     
   Impact: HIGH
     User with full inventory cannot repair vehicle even with
     repair kits in nearby container. No error message shown.
     
   Recommendation:
     Option A: Update spec to document limitation
     Option B: Modify code to handle full inventory case
     Option C: Add user-visible message when bag full

================================================================================
                              VALIDATION RESULT: FAIL
================================================================================
1 behavioral mismatch requires attention before release.

Run with --details for full patch analysis.
Run with --fix-suggestions for automated fix recommendations.
```

---

## Multi-Mod Conflict Detection

### Conflict Report Example

```
================================================================================
                         MOD CONFLICT ANALYSIS
================================================================================

Mods Analyzed:
  1. ProxiCraft v1.0.0
  2. BeyondStorage2 v2.5.0
  3. BetterTraders v3.0.0

CRITICAL CONFLICTS
──────────────────
❌ PATCH COLLISION: XUiM_PlayerInventory.HasItems

   ProxiCraft:     Postfix adds container item counts
   BeyondStorage2: Postfix adds container item counts
   
   Load Order Impact:
     If ProxiCraft loads first:  BS2 postfix runs last, may double-count
     If BS2 loads first:         ProxiCraft postfix runs last, may double-count
     
   Net Effect: ITEM COUNTS DOUBLED
     Player sees 2x actual items in crafting UI
     Crafting may fail when "sufficient" items shown
     
   Recommendation: Use ONLY ONE storage extension mod

❌ PATCH COLLISION: ItemActionRanged.ConsumeAmmo

   ProxiCraft:     Prefix pre-stages ammo from containers
   BeyondStorage2: Prefix pre-stages ammo from containers
   
   Both prefixes will run, potentially moving ammo twice
   
   Net Effect: AMMO CONSUMED TWICE OR INVENTORY OVERFLOW
   
   Recommendation: Disable reload feature in one mod's config

POTENTIAL CONFLICTS
───────────────────
⚠️  OVERLAPPING FUNCTIONALITY: Trader purchases

   ProxiCraft:    enableForTrader pulls currency from containers
   BetterTraders: Modifies trader UI and pricing
   
   Analysis: Different code paths, likely compatible
   Risk: LOW - but test trader interactions
   
⚠️  SHARED RESOURCE: TileEntity.SetModified()

   Both mods call SetModified() on containers
   Multiple calls are idempotent (safe)
   Risk: NONE - but slight performance overhead

COMPATIBLE
──────────
✅ ProxiCraft + BetterTraders
   No overlapping patches
   Different game systems modified
   
================================================================================
                         NET COMBINED EFFECT
================================================================================

If user runs ProxiCraft + BetterTraders (excluding BS2):

Crafting:
  - Uses items from containers (ProxiCraft)
  - Recipe costs unchanged (BetterTraders doesn't modify)
  
Trading:
  - Currency pulled from containers (ProxiCraft)
  - Trader prices modified (BetterTraders)
  - Combined: Buy things with container money at modified prices ✅
  
Storage Priority:
  - Controlled by ProxiCraft config
  - BetterTraders has no storage features
  - No interaction ✅
```

---

## Author Workflow

### Creating a New Mod

```
1. Write mod code
2. Run: QueryDb spec generate MyMod/ --output MyMod.modspec
3. Review generated spec - does it match your intent?
4. If mismatches found:
   a. Bug in code? Fix code, regenerate
   b. Spec wrong? Edit spec to match actual intent
5. Commit MyMod.modspec alongside code
6. CI runs: QueryDb spec validate MyMod/ --spec MyMod.modspec
7. Validation passes → Release
```

### Updating an Existing Mod

```
1. Make code changes
2. Run: QueryDb spec generate MyMod/ --output new.modspec
3. Run: QueryDb spec diff MyMod.modspec new.modspec
4. Review changes:
   
   SPEC DIFF: MyMod v1.0 → v1.1
   ─────────────────────────────
   + Added behavior: storage_priority_ordering
     "Storage sources checked in configurable priority order"
     
   ~ Modified behavior: crafting_from_storage
     - Was: "Checks containers in discovery order"
     + Now: "Checks containers in configured priority order"
     
   + Added config: storagePriority
     Type: dictionary<string, string>
     Default: {Drone: 1, DewCollector: 2, ...}
     
5. Verify changes are intentional
6. Update MyMod.modspec
7. Update changelog from spec diff
```

### User Reporting Issues

```
User runs: QueryDb spec generate ProblematicMod/ --output report.modspec

Sends report.modspec to mod author with note:
"The spec says this mod makes zombies 50% stronger but they seem weaker"

Author compares:
- Their intended spec (committed)  
- Generated spec from user's installed version
- Finds: User has outdated DLL with old behavior

Or finds: Bug in code - strength multiplier is 0.5 instead of 1.5
```

---

## Implementation Phases

### Phase 1: Foundation (2 weeks)
- [ ] Define .modspec YAML schema
- [ ] Implement spec parser/serializer
- [ ] Basic Harmony patch extraction to spec format
- [ ] Command: `spec generate` (patches only)

### Phase 2: XML Support (1 week)
- [ ] XML diff extraction
- [ ] XPath operation interpretation
- [ ] Vanilla XML value lookup
- [ ] Add XML changes to spec output

### Phase 3: Semantic Analysis (2 weeks)
- [ ] Pattern library for common mod behaviors
- [ ] Natural language generation for effects
- [ ] Caller/callee impact analysis
- [ ] Config dependency tracking

### Phase 4: Validation (1 week)
- [ ] Command: `spec validate`
- [ ] Spec comparison logic
- [ ] Mismatch detection and reporting
- [ ] Warning vs error classification

### Phase 5: Multi-Mod Analysis (2 weeks)
- [ ] Command: `spec conflict`
- [ ] Patch collision detection
- [ ] Load order simulation
- [ ] Net effect calculation
- [ ] Command: `spec merge`

### Phase 6: Polish (1 week)
- [ ] Rich terminal output formatting
- [ ] Markdown report generation
- [ ] CI/CD integration examples
- [ ] Documentation and examples

---

## Appendix A: Semantic Pattern Library

The system recognizes common mod patterns and generates appropriate descriptions:

| Code Pattern | Semantic Interpretation |
|--------------|------------------------|
| `__result += X` in Postfix | "Increases {method return} by {X description}" |
| `__result = false` in Prefix with `return false` | "Prevents {method} from executing when {condition}" |
| `if (config.X) return` | "Feature gated by config setting '{X}'" |
| `TileEntity.SetModified()` | "Marks container for save/sync" |
| `Traverse.Field().SetValue()` | "Modifies private field '{field}' to {value}" |
| XML `<set xpath=".../@value">` | "Changes {property} from {old} to {new}" |
| XML `<append xpath="...">` | "Adds {element} to {parent}" |
| XML `<remove xpath="...">` | "Removes {element} from game" |

---

## Appendix B: Example Generated Specs

### Simple XML Mod

```yaml
# CheaperOil.modspec
meta:
  mod_name: "Cheaper Oil"
  mod_version: "1.0"
  
summary:
  one_liner: "Reduces trader oil prices by 50%"
  
  key_features:
    - "Oil costs 50% less at traders"
    
  does_not_do:
    - "Does NOT affect oil found in loot"
    - "Does NOT change oil crafting recipes"

behaviors:
  - id: "oil_price_reduction"
    category: "economy"
    
    trigger:
      event: "Game loads item definitions"
      
    effect:
      description: "Oil trader price reduced by 50%"
      
      changes:
        - item: "resourceOil"
          property: "EconomicValue"
          vanilla: 100
          modded: 50
          change: "-50%"

patches:
  xml:
    - file: "items.xml"
      xpath: "/items/item[@name='resourceOil']/property[@name='EconomicValue']/@value"
      operation: "set"
      old_value: "100"
      new_value: "50"
```

### Complex Harmony Mod

```yaml
# ZombieRebalance.modspec  
meta:
  mod_name: "Zombie Rebalance"
  mod_version: "2.0"
  
summary:
  one_liner: "Makes zombies scale with game stage more aggressively"
  
  key_features:
    - "Zombie health scales 2x faster with game stage"
    - "Zombie damage scales 1.5x faster with game stage"
    - "Feral zombies get bonus night damage"
    
behaviors:
  - id: "health_scaling"
    category: "difficulty"
    
    effect:
      description: "Zombie health increases faster as game stage rises"
      
      formula:
        vanilla: "baseHealth * (1 + gameStage * 0.01)"
        modded:  "baseHealth * (1 + gameStage * 0.02)"
        
      example:
        game_stage: 100
        base_health: 150
        vanilla_result: 300 HP
        modded_result: 450 HP
        change: "+50% more HP at GS100"
```

---

## Appendix C: Non-Coder Friendly Output

When running for end users, generate simplified output:

```
================================================================================
                    WHAT DOES THIS MOD DO?
================================================================================

Mod Name: ProxiCraft
Version: 1.0.0

SIMPLE SUMMARY
──────────────
This mod lets you craft, reload, and buy things using items stored in
nearby containers - not just what's in your backpack.

WHAT IT CHANGES
───────────────
✓ CRAFTING
  Before: You could only craft with items in your backpack
  After:  You can craft with items in nearby storage boxes too
  
✓ RELOADING  
  Before: Reload only used ammo from your inventory
  After:  Reload pulls ammo from nearby containers if you run out
  
✓ TRADING
  Before: Could only buy with money in your backpack
  After:  Can use money stored in nearby containers
  
✓ VEHICLE REPAIR
  Before: Needed repair kit in your inventory
  After:  Can use repair kits from nearby containers

WHAT IT DOES NOT CHANGE
───────────────────────
✗ Does NOT let you sell items from containers to traders
✗ Does NOT change any item stats or recipes
✗ Does NOT affect zombie spawns or difficulty

SETTINGS YOU CAN CHANGE
───────────────────────
• Range: How far away containers can be (default: 15 blocks)
• Priority: Which containers to check first (drones → workstations → boxes)
• Features: Turn individual features on/off

COMPATIBILITY
─────────────
⚠️  DO NOT use with: BeyondStorage2 (does the same thing, will conflict)
✓  Works with: Most other mods

================================================================================
```

---

## Success Criteria

The system is successful when:

1. **Developer Confidence**: "I can change code and know immediately if I broke expected behavior"

2. **User Understanding**: "I can read what a mod does without understanding code"

3. **Conflict Prevention**: "I know before installing if two mods will fight"

4. **Bug Discovery**: "The tool found a behavior I didn't intend - before users did"

5. **Idiot-Proof Reporting**: "A non-coder sent me a spec file and I immediately saw the problem"
