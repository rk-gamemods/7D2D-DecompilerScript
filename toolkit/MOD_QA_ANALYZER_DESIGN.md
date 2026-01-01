# Automated Mod QA Analyzer - Design Document

## Vision

**User Input:** "Here is my mod, give me a QA analysis"

**System Output:** Comprehensive report covering:
- All discovered functionality (patches, events, XML changes)
- How each item interacts with game code
- Complete behavioral flow traces
- Intelligent gap analysis highlighting potential problems

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    MOD QA ANALYZER                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐        │
│  │  DISCOVERY   │──▶│  INTERACTION │──▶│   TRACING    │        │
│  │    PHASE     │   │    PHASE     │   │    PHASE     │        │
│  └──────────────┘   └──────────────┘   └──────────────┘        │
│         │                  │                  │                 │
│         ▼                  ▼                  ▼                 │
│  ┌──────────────────────────────────────────────────────┐      │
│  │                    GAP ANALYSIS                       │      │
│  │  - Missing overloads    - Inheritance gaps            │      │
│  │  - Event bypass routes  - Conflicting patches         │      │
│  └──────────────────────────────────────────────────────┘      │
│                            │                                    │
│                            ▼                                    │
│  ┌──────────────────────────────────────────────────────┐      │
│  │                   REPORT GENERATOR                    │      │
│  │  - Summary     - Detailed findings   - Recommendations│      │
│  └──────────────────────────────────────────────────────┘      │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: Discovery

### What We Discover

#### 1.1 Harmony Patches (AST Parsing)

**Attribute-Based Patches:**
```csharp
// Pattern 1: Class-level HarmonyPatch attribute
[HarmonyPatch(typeof(TargetClass), "MethodName")]
class MyPatch { }

// Pattern 2: Method-level attributes
[HarmonyPatch(typeof(TargetClass), nameof(TargetClass.Method))]
[HarmonyPostfix]
static void Postfix() { }

// Pattern 3: With argument types
[HarmonyPatch(typeof(TargetClass), "Method", new Type[] { typeof(int), typeof(string) })]
```

**Runtime Patches (Harmony.Patch() calls):**
```csharp
// Pattern: Direct Harmony.Patch() invocation
harmony.Patch(
    AccessTools.Method(typeof(TargetClass), "MethodName"),
    postfix: new HarmonyMethod(typeof(MyPatch), "Postfix")
);

// Pattern: With argument types
var method = AccessTools.Method(typeof(XUiM_PlayerInventory), "GetItemCount", 
                                 new[] { typeof(ItemValue) });
harmony.Patch(method, postfix: new HarmonyMethod(typeof(Patches), "GetItemCount_Postfix"));
```

#### 1.2 Event Interactions

```csharp
// Event subscriptions in mod code
player.DragAndDropItemChanged += OnItemChanged;

// Event firing from mod code (key for understanding mod's reach)
DragAndDropItemChanged?.Invoke();
```

#### 1.3 XML Changes

```xml
<!-- Config/*.xml files -->
<configs>
  <set xpath="/items/item[@name='gunPistol']/property[@name='Magazine_size']/@value">30</set>
  <append xpath="/recipes">
    <recipe name="newRecipe" count="1">...</recipe>
  </append>
</configs>
```

#### 1.4 Mod Metadata

```xml
<!-- ModInfo.xml -->
<ModInfo>
  <Name value="ProxiCraft"/>
  <Version value="1.2.1"/>
  <Author value="YourName"/>
</ModInfo>
```

### Discovery Data Structure

```csharp
public class ModDiscoveryResult
{
    public ModMetadata Metadata { get; set; }
    public List<HarmonyPatchInfo> HarmonyPatches { get; set; }
    public List<EventSubscription> EventSubscriptions { get; set; }
    public List<EventFire> EventFires { get; set; }
    public List<XmlChange> XmlChanges { get; set; }
    public List<string> SourceFiles { get; set; }
}

public class HarmonyPatchInfo
{
    public string PatchClass { get; set; }
    public string PatchMethod { get; set; }
    public string TargetType { get; set; }
    public string TargetMethod { get; set; }
    public string[] TargetArgumentTypes { get; set; }
    public PatchType Type { get; set; }  // Prefix, Postfix, Transpiler, Finalizer
    public int Priority { get; set; }
    public bool IsRuntimePatch { get; set; }  // true if Harmony.Patch(), false if attribute
    public string FilePath { get; set; }
    public int LineNumber { get; set; }
    
    // Analysis fields (populated in Phase 2)
    public int? GameMethodId { get; set; }  // Resolved target in callgraph.db
    public string VanillaBehavior { get; set; }
    public string PatchedBehavior { get; set; }
}
```

---

## Phase 2: Interaction Analysis

For each discovered item, analyze how it interacts with the game.

### 2.1 Harmony Patch Resolution

```
For each HarmonyPatchInfo:
  1. Resolve target method in callgraph.db
  2. Get vanilla method body and behavior
  3. Analyze patch method to understand modification:
     - Prefix: Can it skip original? Does it modify parameters?
     - Postfix: Does it modify __result? Does it read __result?
     - Transpiler: What IL modifications?
  4. Determine "effective behavior" after patch
```

### 2.2 Inheritance Chain Analysis

```
For each patched method:
  1. Find all override/virtual relationships
  2. Check if base/derived methods are also patched
  3. Flag if derived classes bypass the patch
  
Example:
  ItemActionRanged.CanReload ← PATCHED
    └── ItemActionCatapult.CanReload (override)
        Does it call base.CanReload()? 
        YES → Covered
        NO → POTENTIAL GAP
```

### 2.3 Overload Analysis

```
For each patched method:
  1. Find all overloads of same method name in same class
  2. Check if all overloads are patched
  3. Determine if unpatched overloads delegate to patched ones
  
Example:
  GetItemCount(ItemValue) ← PATCHED
  GetItemCount(int) ← Check: calls GetItemCount(ItemValue)? Or direct implementation?
```

### 2.4 Caller Impact Analysis

```
For each patched method:
  1. Find all callers in game code
  2. Categorize callers by subsystem (Crafting, Trading, Challenges, etc.)
  3. Determine which gameplay features are affected
```

---

## Phase 3: Flow Tracing

Build complete cause → effect chains.

### 3.1 Event-Driven Flows

```
For each event fired by mod:
  1. Find all subscribers to that event
  2. Trace what each subscriber does
  3. Check if mod patches affect those subscriber paths
  
Example Flow:
  MOD fires: DragAndDropItemChanged
    └── Subscribers:
        ├── ChallengeObjectiveGather.ItemsChangedInternal
        │   └── HandleUpdatingCurrent() ← MOD HAS POSTFIX ✓
        ├── ChallengeObjectiveGatherByTag.ItemsChangedInternal  
        │   └── HandleUpdatingCurrent() ← INHERITS FROM BASE ✓
        └── EntityPlayerLocal.callInventoryChanged
            └── ... (trace continues)
```

### 3.2 Patch Chain Flows

```
For each patched method:
  1. Build call chain: What calls this? What does it call?
  2. Identify entry points (player actions that trigger this)
  3. Identify endpoints (what changes in game state)
  
Example:
  Player clicks "Craft" button
    └── XUiC_RecipeStack.OnPress()
        └── hasItems() 
            └── GetItemCount(ItemValue) ← MOD POSTFIX
                └── RETURNS: inventory + container items
```

---

## Phase 4: Gap Analysis

Intelligently identify potential problems.

### 4.1 Gap Detection Rules

| Gap Type | Detection Logic | Severity |
|----------|-----------------|----------|
| **Missing Overload** | Method X(A) patched, X(B) exists but not patched, X(B) doesn't delegate | HIGH |
| **Inheritance Bypass** | Base.Method patched, Derived.Method overrides without calling base | HIGH |
| **Event Subscriber Miss** | Mod fires event, subscriber path not covered by patches | MEDIUM |
| **Parallel Implementation** | Two methods do same thing, only one patched | MEDIUM |
| **Conflicting Patches** | Multiple mods patch same method | WARNING |
| **Dead Patch** | Patched method never called by game code | LOW |

### 4.2 Smart False Positive Filtering

```
Before flagging a gap, check:
  1. Event coverage: Does mod fire an event that triggers the "missed" code?
  2. Delegation: Does the unpatched method delegate to a patched one?
  3. Base call: Does override call base which is patched?
  4. Contextual irrelevance: Is the unpatched code path actually used?
```

### 4.3 Net Result Analysis

```
For each potential gap:
  1. Trace the COMPLETE flow from user action to game state change
  2. Determine if the gap actually affects the net result
  3. If net result is correct despite gap → mark as "Safe" with explanation
  4. If net result is wrong → mark as "Bug" with reproduction steps
```

---

## Phase 5: Report Generation

### Report Structure

```markdown
# QA Analysis Report: [ModName] v[Version]

## Executive Summary
- Total patches found: X
- Total events managed: Y
- Total XML changes: Z
- **Potential Issues: N** (H high, M medium, L low)

## Discovered Functionality

### Harmony Patches (X total)
| Target | Type | Coverage | Status |
|--------|------|----------|--------|
| XUiM_PlayerInventory.GetItemCount(ItemValue) | Postfix | Crafting, Trading | ✅ OK |
| XUiM_PlayerInventory.GetItemCount(int) | Postfix | Challenges | ✅ OK |
| ... | ... | ... | ... |

### Event Interactions (Y total)
| Event | Action | Subscribers Affected |
|-------|--------|---------------------|
| DragAndDropItemChanged | FIRES | 5 (all covered) |
| ... | ... | ... |

### XML Changes (Z total)
| File | Operation | Target |
|------|-----------|--------|
| items.xml | set | gunPistol/Magazine_size |
| ... | ... | ... |

## Behavioral Flows

### Flow 1: Crafting with Container Items
[Diagram showing complete flow]

### Flow 2: Challenge Progress Tracking
[Diagram showing complete flow]

## Gap Analysis

### ✅ Verified Working
- GetItemCount overloads: Both covered
- CanReload inheritance: Base patch catches all derivatives
- Challenge objectives: All subscribe to same event, postfix covers base class

### ⚠️ Warnings
- [Any warnings with explanation]

### ❌ Issues Found
- [Any actual bugs with reproduction steps]

## Recommendations
1. [Any suggestions for improvement]
```

---

## Implementation Plan

### New Files

1. **`ModAnalyzer.cs`** - Main orchestrator
2. **`HarmonyPatchDiscovery.cs`** - AST-based patch finder (attributes + runtime)
3. **`InteractionAnalyzer.cs`** - Phase 2 logic
4. **`FlowTracer.cs`** - Phase 3 logic
5. **`GapDetector.cs`** - Phase 4 logic
6. **`QaReportGenerator.cs`** - Phase 5 logic

### New QueryDb Command

```bash
QueryDb callgraph.db qa --mod "path/to/mod" [--output report.md] [--verbose]
```

### Data Flow

```
ModAnalyzer.Analyze(modPath, callgraphDb)
    │
    ├── HarmonyPatchDiscovery.Discover(modPath)
    │   └── Returns: List<HarmonyPatchInfo>
    │
    ├── EventFlowExtractor.ExtractFromMod(modPath)
    │   └── Returns: List<EventSubscription>, List<EventFire>
    │
    ├── ModXmlChangeParser.Parse(modPath)
    │   └── Returns: List<XmlChange>
    │
    ├── InteractionAnalyzer.Analyze(patches, callgraphDb)
    │   └── Returns: List<InteractionResult>
    │
    ├── FlowTracer.TraceAll(interactions, events, callgraphDb)
    │   └── Returns: List<BehavioralFlow>
    │
    ├── GapDetector.Detect(interactions, flows, callgraphDb)
    │   └── Returns: List<GapFinding>
    │
    └── QaReportGenerator.Generate(all_results)
        └── Returns: Markdown report
```

---

## Example Output

For ProxiCraft, the automated analysis should produce:

```markdown
# QA Analysis Report: ProxiCraft v1.2.1

## Executive Summary
- **34 Harmony patches** discovered (5 attribute-based, 29 runtime)
- **2 events** fired by mod
- **0 XML changes**
- **0 issues found** ✅

## Key Findings

### GetItemCount Coverage: COMPLETE ✅
Both overloads patched:
- `GetItemCount(ItemValue)` → Postfix adds container items
- `GetItemCount(int)` → Postfix adds container items
Delegation check: Each implemented independently (correct approach)

### Challenge Tracker: COMPLETE ✅
All challenge objective types covered via event-driven architecture:
- Mod fires `DragAndDropItemChanged` when containers change
- All 3 gather-type challenges subscribe to this event
- Base class `HandleUpdatingCurrent` postfixed → covers all derivatives

### CanReload Inheritance: COMPLETE ✅
```
ItemActionRanged.CanReload ← PATCHED
  └── ItemActionCatapult.CanReload → calls base.CanReload() ✅
```
All ranged weapons covered.

## Behavioral Flow: Container → Challenge Update

```
Container.SlotChanged
  └── [POSTFIX: LootContainer_SlotChanged_Patch]
      └── FIRES: DragAndDropItemChanged
          └── Subscribers:
              ├── ChallengeObjectiveGather.ItemsChangedInternal ✅
              ├── ChallengeObjectiveGatherByTag.ItemsChangedInternal ✅
              └── ChallengeObjectiveGatherIngredient.ItemsChangedInternal ✅
```
**Net Result:** Challenge progress updates include container items ✅
```

---

## Success Criteria

The automated QA should:

1. **Find all ProxiCraft patches** (currently only 5 found, should be ~30+)
2. **Correctly trace event flows** 
3. **NOT flag the challenge tracker as a bug** (unlike manual analysis)
4. **Identify if there ARE any actual gaps**
5. **Generate human-readable report** suitable for mod documentation
