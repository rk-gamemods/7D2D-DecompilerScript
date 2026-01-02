# Behavioral Flow Analysis — Toolkit Enhancement Design

## Problem Statement

The current toolkit traces **static call graphs** - who calls whom in the vanilla code. But this misses:

1. **Harmony patch effects** - A postfix can completely change what a method returns
2. **Event-driven flows** - `DragAndDropItemChanged += handler` means the handler runs when the event fires
3. **Order of operations** - Prefix → Original → Postfix, and how they chain
4. **Net player-facing behavior** - What actually happens from the user's perspective

**Example of the gap:**

The toolkit flagged `ChallengeObjectiveGatherByTag` as "not patched" because it calls `Bag.GetItemCount()` directly. But:
- ProxiCraft fires `DragAndDropItemChanged` event when containers change
- `ChallengeObjectiveGatherByTag` subscribes to this event
- Event triggers `HandleUpdatingCurrent()` 
- ProxiCraft's postfix on `ChallengeObjectiveGather.HandleUpdatingCurrent` adds container counts

**Net result:** It works! But static analysis couldn't see this.

---

## Design Goals

### 1. Answer Player-Facing Questions

Instead of "what calls what", answer:
- "When I move items to a container, what updates?" 
- "Does the challenge tracker count container items?"
- "What happens when I click Craft?"

### 2. Trace Complete Flows

Show the FULL chain including:
```
TRIGGER: Player moves item to container
  └─► LootContainer_SlotChanged_Patch (ProxiCraft Postfix)
      └─► Fires: DragAndDropItemChanged event
          └─► Subscriber: ChallengeObjectiveGather.ItemsChangedInternal
              └─► Calls: HandleUpdatingCurrent()
                  └─► [VANILLA] Counts from Bag + Toolbelt
                  └─► [POSTFIX] Adds container items via ContainerManager
                      └─► Sets: Current field with total count
NET RESULT: Challenge progress includes container items ✓
```

### 3. Understand Effective Behavior

When a method has patches:
```
Method: XUiM_PlayerInventory.GetItemCount(ItemValue)
  Prefix: (none)
  Original: Returns bag.count + toolbelt.count  
  Postfix: ProxiCraft adds ContainerManager.GetItemCount()
  
EFFECTIVE: Returns inventory + nearby container items
```

---

## New Database Tables

### `event_subscriptions`
Track who subscribes to what events.

```sql
CREATE TABLE event_subscriptions (
    id INTEGER PRIMARY KEY,
    subscriber_method_id INTEGER,       -- Method that subscribes
    subscriber_type TEXT NOT NULL,      -- Type containing the subscriber
    event_owner_type TEXT NOT NULL,     -- Type that owns the event  
    event_name TEXT NOT NULL,           -- Event field name
    handler_method TEXT NOT NULL,       -- Method invoked when event fires
    is_mod INTEGER DEFAULT 0,           -- 1 if from mod code
    mod_id INTEGER,                     -- FK to mods if is_mod=1
    file_path TEXT,
    line_number INTEGER,
    FOREIGN KEY (subscriber_method_id) REFERENCES methods(id),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);
```

**Example data:**
| subscriber_type | event_owner_type | event_name | handler_method |
|-----------------|------------------|------------|----------------|
| ChallengeObjectiveGather | EntityPlayerLocal | DragAndDropItemChanged | ItemsChangedInternal |
| ChallengeObjectiveGatherByTag | EntityPlayerLocal | DragAndDropItemChanged | ItemsChangedInternal |
| LootContainer_SlotChanged_Patch | XUiC_LootContainer | HandleLootSlotChangedEvent | Postfix |

### `event_fires`
Track where events are invoked/fired.

```sql
CREATE TABLE event_fires (
    id INTEGER PRIMARY KEY,
    firing_method_id INTEGER,           -- Method that fires the event
    firing_type TEXT NOT NULL,          -- Type containing the fire
    event_owner_type TEXT NOT NULL,     -- Type that owns the event
    event_name TEXT NOT NULL,           -- Event being fired
    is_mod INTEGER DEFAULT 0,           -- 1 if from mod code
    mod_id INTEGER,                     -- FK to mods if is_mod=1
    file_path TEXT,
    line_number INTEGER,
    FOREIGN KEY (firing_method_id) REFERENCES methods(id),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);
```

### `effective_methods`
Cache the "effective" behavior of methods with patches.

```sql
CREATE TABLE effective_methods (
    id INTEGER PRIMARY KEY,
    method_id INTEGER NOT NULL,         -- FK to methods
    has_prefix INTEGER DEFAULT 0,
    has_postfix INTEGER DEFAULT 0,  
    has_transpiler INTEGER DEFAULT 0,
    has_finalizer INTEGER DEFAULT 0,
    prefix_mods TEXT,                   -- JSON array of mod names with prefixes
    postfix_mods TEXT,                  -- JSON array of mod names with postfixes
    vanilla_behavior TEXT,              -- Summary of what vanilla does
    effective_behavior TEXT,            -- Summary of net effect with patches
    FOREIGN KEY (method_id) REFERENCES methods(id)
);
```

### `behavioral_flows`
Pre-computed flows from triggers to outcomes.

```sql
CREATE TABLE behavioral_flows (
    id INTEGER PRIMARY KEY,
    trigger_description TEXT NOT NULL,  -- "Player moves item to container"
    trigger_method_id INTEGER,          -- Initial method/event
    outcome_description TEXT NOT NULL,  -- "Challenge progress updates"
    outcome_method_id INTEGER,          -- Final method affected
    flow_json TEXT NOT NULL,            -- Full flow as JSON tree
    mods_involved TEXT,                 -- JSON array of mod names
    FOREIGN KEY (trigger_method_id) REFERENCES methods(id),
    FOREIGN KEY (outcome_method_id) REFERENCES methods(id)
);
```

---

## New Extraction: Event Subscription Parser

Add to `CallGraphExtractor` or new `EventFlowExtractor.cs`:

### Pattern: Event Subscription
```csharp
// Match: eventOwner.EventName += HandlerMethod;
// Match: eventOwner.EventName -= HandlerMethod;
// Match: player.DragAndDropItemChanged += ItemsChangedInternal;
```

**Detection via Roslyn:**
```csharp
foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
{
    if (assignment.OperatorToken.Kind() == SyntaxKind.PlusEqualsToken ||
        assignment.OperatorToken.Kind() == SyntaxKind.MinusEqualsToken)
    {
        // Left side: eventOwner.EventName
        if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
        {
            var eventName = memberAccess.Name.Identifier.Text;
            // Check if this is an event field via semantic model
            var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (symbol is IEventSymbol eventSymbol)
            {
                // Right side: handler method
                var handler = assignment.Right.ToString();
                // Record the subscription
            }
        }
    }
}
```

### Pattern: Event Invocation/Fire
```csharp
// Match: EventName?.Invoke()
// Match: EventDelegate?.DynamicInvoke()  
// Match: eventField.GetValue(player) as Delegate; delegate?.DynamicInvoke()
```

---

## New CLI Command: `flow`

### Usage
```bash
# Show what happens when a trigger occurs
QueryDb callgraph.db flow "container slot change"

# Show all flows that affect a target
QueryDb callgraph.db flow --affects "challenge tracker"

# Trace from method to outcome
QueryDb callgraph.db flow --from "HandleLootSlotChangedEvent" --to "Current"
```

### Output Format
```
═══ Flow: Container Item Change → Challenge Tracker Update ═══

TRIGGER: XUiC_LootContainer.HandleLootSlotChangedEvent called
  │
  ├─► [POSTFIX: ProxiCraft.LootContainer_SlotChanged_Patch]
  │   │  Fires event: DragAndDropItemChanged
  │   │
  │   └─► [SUBSCRIBERS]
  │       ├─► ChallengeObjectiveGather.ItemsChangedInternal
  │       │   └─► Calls: HandleUpdatingCurrent()
  │       │       ├─► [VANILLA] bag.GetItemCount() + toolbelt.GetItemCount()
  │       │       └─► [POSTFIX: ProxiCraft] + ContainerManager.GetItemCount()
  │       │           └─► Sets: base.Current = total
  │       │
  │       ├─► ChallengeObjectiveGatherByTag.ItemsChangedInternal
  │       │   └─► Calls: HandleUpdatingCurrent()
  │       │       └─► [VANILLA] bag.GetItemCount(tags) + toolbelt.GetItemCount(tags)
  │       │       └─► [POSTFIX: ProxiCraft] + ContainerManager.GetItemCountByTags()
  │       │           └─► Sets: base.Current = total
  │       │
  │       └─► ChallengeObjectiveGatherIngredient.ItemsChangedInternal
  │           └─► (same pattern)

NET RESULT: ✓ All challenge types update to include container items

MODS INVOLVED: ProxiCraft (2 patches)
```

---

## New CLI Command: `effective`

Show the effective behavior of a method including all patches.

### Usage
```bash
QueryDb callgraph.db effective "XUiM_PlayerInventory.GetItemCount"
```

### Output
```
═══ Effective Behavior: XUiM_PlayerInventory.GetItemCount(ItemValue) ═══

VANILLA BEHAVIOR:
  Returns: bag.GetItemCount(item) + toolbelt.GetItemCount(item)
  Scope: Player inventory only (backpack + toolbelt)

PATCHES APPLIED:
  ┌─ Priority 400: ProxiCraft.XUiM_PlayerInventory_GetItemCount_Patch
  │  Type: Postfix
  │  Effect: Adds ContainerManager.GetItemCount(Config, item) to result
  │  Condition: When Config.modEnabled && Config.enableForCrafting
  └─

EFFECTIVE BEHAVIOR:
  Returns: bag + toolbelt + nearby containers (within config.range)
  Scope: Player inventory + all in-range storage containers

CALLERS AFFECTED (17):
  - XUiC_RecipeStack.HasIngredients
  - ItemActionEntryCraft.hasItems  
  - RequirementObjectiveGroupCraft.HasPrerequisiteCondition
  - (14 more...)

PLAYER-FACING IMPACT:
  Crafting UI shows materials from nearby containers as available
  "Craft" button enables when total materials sufficient
  Recipe ingredient counts include container items
```

---

## Implementation Plan

### Phase 1: Event Extraction
1. Add `EventSubscriptionExtractor.cs` to CallGraphExtractor
2. Parse both game code AND mod code for `+=` event subscriptions
3. Parse mod code for event fires (via reflection or direct invocation)
4. Store in `event_subscriptions` and `event_fires` tables

### Phase 2: Enhanced Harmony Parsing  
1. Improve `ModParser.cs` to extract more patch details:
   - Parse `[HarmonyPriority]` attribute
   - Parse `[HarmonyBefore]` / `[HarmonyAfter]` attributes
   - Extract patch method body to understand what it does
2. Re-run extraction on ProxiCraft and other mods

### Phase 3: Flow Builder
1. New `FlowBuilder.cs` that combines:
   - Static call graph
   - Event subscription graph
   - Harmony patch overlays
2. Build pre-computed flows for common triggers
3. Store in `behavioral_flows` table

### Phase 4: CLI Commands
1. Add `flow` command to QueryDb
2. Add `effective` command to QueryDb
3. Update documentation

### Phase 5: Validation
1. Test against ProxiCraft's known behaviors
2. Verify the "false positive" challenges are correctly traced
3. Document any remaining gaps

---

## Extraction Improvements Needed

### Current ModParser Gaps

1. **mod_name is empty** - Not extracting mod name from ModInfo.xml properly
2. **Only 9 patches found** - Missing many ProxiCraft patches (should be 30+)
3. **No event parsing** - Completely missing event subscriptions

### Fix Plan

1. **Better ModInfo parsing** - Check for multiple ModInfo formats
2. **Parse entire mod directories** - Not just direct children
3. **Use semantic model for accuracy** - Currently parsing syntax only
4. **Add verbose debugging** - Log what's found vs expected

---

## Success Criteria

The toolkit should be able to:

1. **Correctly trace ProxiCraft's challenge tracker flow**
   - Show that `DragAndDropItemChanged` triggers all challenge types
   - Show the postfix adds container counts
   - Confirm all `ChallengeObjective*` types are covered

2. **Answer "does X work?" questions**
   - "Does crafting use container items?" → Yes, via GetItemCount postfix
   - "Do challenges count container items?" → Yes, via event + postfix
   - "Does trader buying work?" → Yes, via CanSwapItems postfix

3. **Identify REAL gaps** (if any exist)
   - Show methods that have NO patches where they need them
   - Show events that aren't fired when they should be
   - Show flows that break at some point

---

## Example: Validating ProxiCraft Challenge Tracker

After implementation, running:
```bash
QueryDb callgraph.db flow --trigger "container item moved" --affects "challenge"
```

Should output something like:
```
═══ Flow Analysis: Container Change → Challenge Progress ═══

VERIFIED WORKING:
  ✓ ChallengeObjectiveGather - Postfix adds container count
  ✓ ChallengeObjectiveGatherByTag - Event triggers recount  
  ✓ ChallengeObjectiveGatherIngredient - Event triggers recount
  ✓ ChallengeObjectiveHarvest - N/A (tracks harvesting, not inventory)
  ✓ ChallengeObjectiveHarvestByTag - N/A (tracks harvesting, not inventory)

POTENTIAL GAPS: (none found)

CONFIDENCE: HIGH - All inventory-tracking challenges covered via event mechanism
```

This would have caught that our "bugs" were false positives.
