# Patch Weaver - XPath Conflict Resolution Tool

## Vision Statement

Create a tool that allows 7 Days to Die players to automatically detect, understand, and resolve mod conflicts through an intelligent XPath-aware system that can generate compatibility patches.

**Why This Matters:** No such tool exists for 7D2D today. Players currently rely on forum posts, manual XML editing, and trial-and-error to resolve conflicts.

---

## Problem Analysis

### How 7D2D Mods Work

Unlike Bethesda games (file replacement) or Paradox games (text file overrides), 7D2D uses **XPath patches**:

```xml
<!-- This doesn't replace entityclasses.xml - it surgically modifies it -->
<set xpath="/entity_classes/entity_class[@name='playerMale']/.../CarryCapacity/@value">36</set>
```

### Current Player Experience

1. Install multiple mods
2. Game crashes or behaves unexpectedly
3. Search forums for "Mod A + Mod B compatibility"
4. Manually create `zzzzPatch` folder with resolution XML
5. Repeat until stable

### The Gap

| Existing Tools | What They Do | Why It Doesn't Help 7D2D |
|---------------|--------------|--------------------------|
| **Mod Organizer 2** | File-level conflict detection | 7D2D uses XPath, not file replacement |
| **Vortex** | Basic mod installation | No conflict awareness at all |
| **Irony Mod Manager** | Deep conflict solver | Only for Paradox games |

---

## Proposed Solution: Patch Weaver

### Core Capabilities

1. **Smart Conflict Detection**
   - Parse all mod XPath operations
   - Identify TRUE conflicts (same xpath + same operation)
   - Distinguish from complementary changes (same entity, different properties)

2. **Conflict Visualization**
   ```
   CONFLICT: CarryCapacity value
   ────────────────────────────────────────
   Mod A (AGF Backpack):     36
   Mod B (BiggerBags):       50
   Mod C (RealisticCarry):   24
   
   Load Order Winner: Mod C (loads last alphabetically)
   
   Your choice: [A] [B] [C] [Custom: ___]
   ```

3. **Patch Generation**
   - Creates valid 7D2D mod folder: `zzzzz_PatchWeaver_Resolution/`
   - Generates `ModInfo.xml` with metadata
   - Outputs XPath operations that enforce user preferences
   - Loads LAST due to `zzzzz` prefix

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      PATCH WEAVER FLOW                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [1] ANALYZE                                                    │
│  ├─ XmlIndexer scans all mods                                   │
│  ├─ Extracts xpath, operation, content                          │
│  └─ Stores in ecosystem.db                                      │
│                                                                 │
│  [2] DETECT                                                     │
│  ├─ Hash xpath + operation_type                                 │
│  ├─ Group by exact match                                        │
│  ├─ Filter: same xpath + same op = CONFLICT                     │
│  └─ Filter: same xpath + different op = USUALLY OK              │
│                                                                 │
│  [3] RESOLVE (Interactive)                                      │
│  ├─ Show each conflict with all competing values                │
│  ├─ Show current "winner" (alphabetical load order)             │
│  ├─ Let user pick: [Mod A] [Mod B] [Custom] [Skip]              │
│  └─ Record preferences                                          │
│                                                                 │
│  [4] GENERATE                                                   │
│  ├─ Create zzzzz_PatchWeaver_Resolution/                        │
│  ├─ Generate ModInfo.xml                                        │
│  ├─ Generate Config/*.xml with SET operations                   │
│  └─ Each SET overrides the conflict with user's choice          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Technical Implementation

### Database Schema Additions

```sql
-- Add load_order column (derived from alphabetical folder name)
ALTER TABLE mods ADD COLUMN load_order INTEGER;

-- Add conflict tracking table
CREATE TABLE xpath_conflicts (
    id INTEGER PRIMARY KEY,
    xpath_hash TEXT NOT NULL,        -- Hash of normalized xpath + operation
    xpath TEXT NOT NULL,
    operation TEXT NOT NULL,         -- set, append, remove, etc.
    conflict_type TEXT,              -- REAL_CONFLICT, COMPLEMENTARY, RESOLVED
    resolution_mod_id INTEGER,       -- Which mod "wins" if resolved
    custom_value TEXT,               -- User's custom value if provided
    FOREIGN KEY (resolution_mod_id) REFERENCES mods(id)
);

-- Link conflicts to mods
CREATE TABLE conflict_participants (
    conflict_id INTEGER,
    mod_id INTEGER,
    value TEXT,                      -- What this mod sets the value to
    PRIMARY KEY (conflict_id, mod_id),
    FOREIGN KEY (conflict_id) REFERENCES xpath_conflicts(id),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);
```

### CLI Commands

```bash
# Detect conflicts with smart filtering
XmlIndexer detect-conflicts ecosystem.db

# Interactive conflict resolution wizard
XmlIndexer weave-conflicts ecosystem.db

# Generate patch mod from saved resolutions
XmlIndexer generate-patch ecosystem.db "C:\...\7 Days To Die\Mods"

# One-shot: detect, resolve interactively, generate
XmlIndexer patch-wizard ecosystem.db "C:\...\7 Days To Die\Mods"
```

### Conflict Classification

| Scenario | Classification | Action |
|----------|---------------|--------|
| Same exact xpath, same operation (SET) | **REAL_CONFLICT** | User must choose |
| Same exact xpath, different operations (SET vs APPEND) | **POTENTIAL_ISSUE** | Warn, usually OK |
| Same entity, different xpath suffix | **COMPLEMENTARY** | No action needed |
| Same entity, same property, different conditions | **COMPLEX** | Manual review |

### Example Conflict Detection

**Input (two mods):**
```xml
<!-- AGF Backpack -->
<set xpath="/entity_classes/entity_class[@name='playerMale']/effect_group/passive_effect[@name='CarryCapacity']/@value">36</set>

<!-- BiggerBags -->
<set xpath="/entity_classes/entity_class[@name='playerMale']/effect_group/passive_effect[@name='CarryCapacity']/@value">50</set>
```

**Detection:**
```
XPATH:     /entity_classes/entity_class[@name='playerMale']/effect_group/passive_effect[@name='CarryCapacity']/@value
OPERATION: SET
HASH:      sha256(normalize(xpath) + "SET") = abc123...

Participants:
  - AGF-V2-Backpack72Plus-v3.2.0 (load order: 1) → 36
  - BiggerBags (load order: 5) → 50

Current Winner: BiggerBags (loads last)
Classification: REAL_CONFLICT
```

---

## Generated Patch Example

**Output: `Mods/zzzzz_PatchWeaver_Resolution/`**

```
zzzzz_PatchWeaver_Resolution/
├── ModInfo.xml
└── Config/
    └── entityclasses.xml
```

**ModInfo.xml:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<xml>
    <Name value="PatchWeaver Resolution" />
    <DisplayName value="PatchWeaver Conflict Resolution" />
    <Description value="Auto-generated mod conflict resolution. Created by Patch Weaver on 2026-01-02." />
    <Author value="Patch Weaver" />
    <Version value="1.0.0" />
</xml>
```

**Config/entityclasses.xml:**
```xml
<configs>
    <!-- Conflict: CarryCapacity value -->
    <!-- Competitors: AGF Backpack (36), BiggerBags (50) -->
    <!-- Resolution: User chose AGF Backpack value -->
    <set xpath="/entity_classes/entity_class[@name='playerMale']/effect_group/passive_effect[@name='CarryCapacity']/@value">36</set>
</configs>
```

---

## Competitive Analysis

### Mod Organizer 2 (with 7D2D plugin)

**Source:** https://www.nexusmods.com/7daystodie/articles/1053 (by FlufferNutterSandwich)

**Status:** ✅ MO2 works for 7D2D but with significant limitations

**What MO2 + 7D2D Plugin DOES:**
- ✅ Virtual file system - runs mods without modifying game folder
- ✅ Profile system - switch between mod lists (Darkness Falls, Rebirth, etc.)
- ✅ Enable/disable mods quickly
- ✅ Wabbajack support for sharing mod lists
- ✅ Download integration with NexusMods

**What MO2 + 7D2D Plugin DOES NOT DO:**
- ❌ **No XPath conflict detection** - just file-level management
- ❌ **No load order control in UI** - shows Data tab but "You cannot manually adjust this Load Order"
- ❌ **No patch generation** - author says "I will not troubleshoot your garbage pile of random Mod compatibility issues"
- ❌ **Requires external tool (SMOOT) for load order** - "SMOOT is for advanced-users only!"

**Key Quote from Article:**
> "How does Load Order work for 7DtD with MO2? On the right side of the MO2 application, you will see a Data tab. This will list all of the current Mods you have Enabled in their current normal alphanumerical Load Order. **You cannot manually adjust this Load Order.** If you want to change the forced Load Order for MO2, you must use Donavan's SMOOT."

**SMOOT (referenced tool):**
- https://github.com/DonovanMods/smoot
- "Seven days to die Mod Order Optimization Tool"
- Handles load order but NOT conflict detection or resolution

### Irony Mod Manager

**Status:** Paradox games only, not applicable to 7D2D

**What we can learn from Irony:**
- UI patterns for conflict resolution
- FIOS/LIOS load order concepts
- Merge strategy options

---

## 🎯 Our Unique Value Proposition

After investigating MO2's 7D2D support, **Patch Weaver fills a clear gap:**

| Capability | MO2 + 7D2D | Vortex | Manual | **Patch Weaver** |
|------------|-----------|--------|--------|------------------|
| Virtual mod management | ✅ | ✅ | ❌ | N/A (analysis tool) |
| Profile switching | ✅ | ❌ | ❌ | N/A |
| **XPath conflict detection** | ❌ | ❌ | ❌ | ✅ |
| **Shows competing values** | ❌ | ❌ | ❌ | ✅ |
| **Identifies real vs false conflicts** | ❌ | ❌ | Manual | ✅ |
| **Generates resolution patches** | ❌ | ❌ | Manual | ✅ |
| Load order visualization | Shows but can't change | ❌ | ❌ | ✅ |

**Bottom Line:** MO2 is a mod FILE manager. Patch Weaver is a mod CONFLICT analyzer and resolver.

They are **complementary tools** - users could use MO2 for installation/profiles AND Patch Weaver for conflict analysis/resolution.

---

## Development Phases

### Phase 1: Smart Conflict Detection (MVP)
- [ ] Add `load_order` column to mods table
- [ ] Create conflict detection algorithm (exact xpath match)
- [ ] Add `detect-conflicts` command
- [ ] Show real conflicts vs complementary changes

### Phase 2: Resolution Tracking
- [ ] Add `xpath_conflicts` and `conflict_participants` tables
- [ ] Store user preferences
- [ ] Track resolution status

### Phase 3: Interactive Wizard
- [ ] `weave-conflicts` command with interactive prompts
- [ ] Show competing values
- [ ] Allow custom values
- [ ] Save preferences to database

### Phase 4: Patch Generation
- [ ] `generate-patch` command
- [ ] Create valid mod folder structure
- [ ] Generate ModInfo.xml
- [ ] Generate XPath operations

### Phase 5: User Interface
- [ ] Decision pending - see UI Options below

---

## UI Options Analysis

**Status: ⏳ DEFERRED** - Focus on core functionality first, revisit UI after logic is working.

**Future possibility:** In-game mod "Mod Conflict Patcher" using XUi during mod loading phase.

### Quick Reference

| Option | Effort | Notes |
|--------|--------|-------|
| CLI/TUI | Low | Spectre.Console, good for power users |
| MO2 Plugin | High | Architecture mismatch, risky |
| Web UI | Medium | Server hosting concerns |
| In-Game Mod | Medium-High | Best UX, uses native XUi, potential standalone mod |

**Current approach:** CLI commands for our workflow, UI for other users can come later.

---

## Open Questions

1. **Complex XPath predicates** - How to normalize `//entity_class[starts-with(@name,'zombie')]` for matching?

2. **APPEND conflicts** - When two mods APPEND different things to same parent, is that a conflict?

3. **Conditional requirements** - Mods with `<requirement>` blocks - same xpath but different conditions?

4. **Version tracking** - If mod updates, do we invalidate saved resolutions?

5. **Mod removal** - If user removes a conflicting mod, auto-regenerate patch?

---

## Success Metrics

- User can identify all real conflicts in < 30 seconds
- Resolution wizard completes in < 5 minutes for typical mod list
- Generated patches work without manual editing
- Zero false positives (no "conflicts" that aren't actually conflicts)
- Community adoption and feedback

---

## References

- [7D2D XPath Modding Wiki](https://7daystodie.fandom.com/wiki/XPath_Explained)
- [Irony Mod Manager](https://github.com/bcssov/IronyModManager) - Inspiration for conflict solver UI
- [Mod Organizer 2](https://github.com/ModOrganizer2/modorganizer) - File conflict concepts
