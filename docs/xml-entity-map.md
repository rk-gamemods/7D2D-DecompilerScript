# XML Entity Interconnection Map

This document explains entity relationships in 7D2D XML and how to use the XmlIndexer database to avoid mod conflicts.

> **Note**: Static tables have been replaced by live database queries. Build the database with `XmlIndexer full-analyze` for up-to-date counts.

## Quick Start: Common Queries

### How many entities of each type exist?
```sql
SELECT * FROM v_entity_counts;
```

### What are the riskiest entities to modify?
```sql
SELECT * FROM v_inheritance_hotspots WHERE risk_level IN ('CRITICAL', 'HIGH');
```

### What depends on a specific entity?
```bash
XmlIndexer impact-analysis ecosystem.db buff buffCoffeeBuzz
```

### What buffs are most referenced?
```sql
SELECT * FROM v_most_referenced_buffs LIMIT 20;
```

---

## Entity Cross-Reference Overview

Entities reference each other in predictable patterns:

```
entitygroups.xml
     │ group_member references → entity_classes
     ▼
entityclasses.xml
     │ ├── extends → other entity_classes
     │ ├── HandItem → items
     │ └── triggered_effect → buffs
     ▼
items.xml
     │ ├── extends → other items
     │ ├── triggered_effect → buffs
     │ └── property → sounds
     ▼
recipes.xml
     │ ├── ingredient → items
     │ └── output → items
     ▼
loot.xml
     │ └── loot_entry → items
     ▼
buffs.xml
     │ ├── RemoveBuff → buffs
     │ ├── AddBuff → buffs
     │ └── PlaySound → sounds
     ▼
blocks.xml
     └── extends → other blocks (heavy inheritance)
```

### Query: Reference Types Distribution
```sql
SELECT * FROM v_reference_type_counts;
```

---

## XPath Conflict Risk Patterns

### Danger Zone: Wildcard Entity Selectors

**CRITICAL RISK** - These XPaths match many entities:

```xml
<!-- Matches ALL zombies with "Screamer" anywhere in name -->
//entitygroup[contains(text(),'zombieScreamer')]

<!-- Matches ALL blocks with churchBell anywhere in name -->
/blocks/block[contains(@name, 'churchBell')]
```

### Safe vs Dangerous XPath Patterns

| Pattern | Risk | Why |
|---------|------|-----|
| `[@name='exact']` | Safe | Exact match only |
| `[starts-with(@name,'prefix')]` | Medium | Matches all with prefix |
| `[contains(@name,'text')]` | HIGH | Matches anything with text |
| No predicate | CRITICAL | Matches ALL elements |

---

## Common Conflict Scenarios

### Scenario 1: Extends Chain Break

**Problem**: Modifying a base entity affects all children.

```xml
<!-- meleeHandMaster is extended by 52+ items -->
<remove xpath="/items/item[@name='meleeHandMaster']/property[@name='DamageBonus']"/>
<!-- ALL extending items lose DamageBonus -->
```

**Check first**:
```sql
SELECT * FROM v_inheritance_hotspots WHERE base_entity = 'meleeHandMaster';
```

### Scenario 2: Removing Referenced Entity

**Problem**: Removing an entity that others reference.

```xml
<remove xpath="/buffs/buff[@name='buffInjuryBleeding']"/>
<!-- But 12 buffs have triggered_effect referencing this buff -->
```

**Check first**:
```bash
XmlIndexer impact-analysis ecosystem.db buff buffInjuryBleeding
```

### Scenario 3: Effect Operation Mismatch

**Problem**: Different mods use different operations on same effect.

```xml
<!-- ModA: Uses perc_add -->
<passive_effect name="DamageModifier" operation="perc_add" value="0.5"/>

<!-- ModB: Uses perc_set -->
<passive_effect name="DamageModifier" operation="perc_set" value="1.5"/>
```

**Check conflicts**:
```bash
XmlIndexer detect-conflicts ecosystem.db
```

### Scenario 4: Partial Effect Modifications

**Problem**: Changing `@value` without `@operation` (or vice versa).

```xml
<!-- Original: base_add with value="10" -->

<!-- ModA: Only changes value -->
<set xpath=".../@value">50</set>

<!-- ModB: Only changes operation -->
<set xpath=".../@operation">perc_add</set>

<!-- Result after ModB: perc_add with "50" = 5000% bonus! -->
```

**Always** set both `@operation` and `@value` together.

### Scenario 5: Buff Loops

**Problem**: Buff A adds Buff B, Buff B removes Buff A, creating a loop.

**Check buff chains**:
```bash
XmlIndexer impact-analysis ecosystem.db buff buffYourBuff
```

---

## Entity Naming Conventions

### Safe Naming for New Entities

**DO:**
```
MyMod_zombieCustom          - Prefix with mod name
zombieCustom_MyMod          - Suffix with mod name
```

**DON'T:**
```
zombieScreamerCustom        - Matches wildcards for "zombieScreamer"
gunPistolModified           - Might match [contains(@name,'gunPistol')]
```

---

## XmlIndexer Workflow

### Full Analysis
```bash
XmlIndexer full-analyze "<game_path>" "<mods_folder>" ecosystem.db
XmlIndexer build-dependency-graph ecosystem.db
```

### Impact Analysis
```bash
# What depends on this buff?
XmlIndexer impact-analysis ecosystem.db buff buffCoffeeBuzz

# What depends on this item?
XmlIndexer impact-analysis ecosystem.db item gunPistolT3
```

### Conflict Detection
```bash
# XPath-level conflicts (JSON output)
XmlIndexer detect-conflicts ecosystem.db

# Indirect conflicts summary
sqlite3 ecosystem.db "SELECT * FROM v_conflict_summary"
```

---

## Database Views Reference

These views are created by the schema for common queries:

| View | Purpose |
|------|---------|
| `v_entity_counts` | Count of each entity type |
| `v_inheritance_hotspots` | Entities with many dependents |
| `v_reference_type_counts` | Distribution of reference types |
| `v_most_referenced_buffs` | Buffs referenced most often |
| `v_conflict_summary` | Indirect conflict severity counts |
| `v_mod_interaction_matrix` | Which mod pairs have most conflicts |

---

## Summary: Golden Rules

1. **Prefix new entities** with your mod name to avoid XPath collisions
2. **Use exact XPath matching** (`[@name='exact']`) instead of wildcards
3. **Check inheritance first** with `impact-analysis` before modifying base entities
4. **Append instead of set** when adding to effect_groups
5. **Check references** with `impact-analysis` before removing any entity
6. **Build the dependency graph** to enable comprehensive conflict detection

---

## See Also

- Run `XmlIndexer --help` for all commands
- Query `sqlite3 ecosystem.db ".schema"` for table structures
- [modding-patterns.md](../knowledge/modding-patterns.md) - Recommended patterns
