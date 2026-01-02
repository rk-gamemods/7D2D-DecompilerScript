# 7D2D XML Indexer Design

## Problem Statement

The current QueryDb toolkit only indexes C# code. However:
- Most 7D2D mods are XML-only (modifying items, blocks, entities, etc.)
- XML mods can break C# mods by removing/modifying things C# code depends on
- XML mod load order is critical and hard to analyze
- No way to detect XML↔C# conflicts

## Solution: XML Definition Database

Create a companion database that indexes:
1. **All XML definitions** from the game's `Data/Config/` folder
2. **C# code references** to XML-defined entities
3. **Cross-references** between XML files
4. **Mod XML patches** and what they affect

---

## Game XML Files to Index

| File | Defines | Key Elements |
|------|---------|--------------|
| `items.xml` | Items, weapons, tools | `<item name="...">` |
| `blocks.xml` | Blocks, terrain | `<block name="...">` |
| `entityclasses.xml` | Zombies, animals, NPCs | `<entity_class name="...">` |
| `entitygroups.xml` | Spawn groups | `<entitygroup name="...">` |
| `buffs.xml` | Buffs/debuffs | `<buff name="...">` |
| `recipes.xml` | Crafting recipes | `<recipe name="...">` |
| `loot.xml` | Loot containers/groups | `<lootcontainer id="...">`, `<lootgroup name="...">` |
| `progression.xml` | Skills, perks, books | `<skill name="...">`, `<perk name="...">` |
| `sounds.xml` | Sound definitions | `<SoundDataNode name="...">` |
| `vehicles.xml` | Vehicle definitions | `<vehicle name="...">` |
| `traders.xml` | Trader inventories | `<trader_info id="...">` |
| `quests.xml` | Quest definitions | `<quest id="...">` |
| `gameevents.xml` | Event sequences | `<action_sequence name="...">` |
| `gamestages.xml` | Game stage config | `<gamestage>` |
| `spawning.xml` | Biome spawn rules | `<spawn>` |
| `biomes.xml` | Biome definitions | `<biome name="...">` |
| `materials.xml` | Material properties | `<material id="...">` |
| `painting.xml` | Paint textures | `<paint name="...">` |
| `rwgmixer.xml` | RWG tile rules | `<prefab_rule name="...">` |
| `localization.txt` | UI strings | Key-value pairs |

---

## Database Schema

### Table: `xml_definitions`
```sql
CREATE TABLE xml_definitions (
    id INTEGER PRIMARY KEY,
    definition_type TEXT NOT NULL,  -- 'item', 'block', 'entity_class', 'buff', etc.
    name TEXT NOT NULL,             -- The name/id of the definition
    file_path TEXT NOT NULL,        -- Source XML file
    line_number INTEGER,            -- Line in source file
    extends TEXT,                   -- Parent definition if using Extends
    properties TEXT,                -- JSON of key properties
    full_xml TEXT                   -- Full XML snippet for reference
);
CREATE INDEX idx_def_type_name ON xml_definitions(definition_type, name);
CREATE INDEX idx_def_name ON xml_definitions(name);
```

### Table: `xml_properties`
```sql
CREATE TABLE xml_properties (
    id INTEGER PRIMARY KEY,
    definition_id INTEGER REFERENCES xml_definitions(id),
    property_name TEXT NOT NULL,
    property_value TEXT,
    property_class TEXT,            -- For nested <property class="Action0"> etc.
    line_number INTEGER
);
CREATE INDEX idx_prop_name ON xml_properties(property_name);
CREATE INDEX idx_prop_value ON xml_properties(property_value);
```

### Table: `xml_references`
```sql
CREATE TABLE xml_references (
    id INTEGER PRIMARY KEY,
    source_type TEXT NOT NULL,      -- 'xml' or 'csharp'
    source_file TEXT NOT NULL,
    source_line INTEGER,
    target_type TEXT NOT NULL,      -- 'item', 'block', 'buff', etc.
    target_name TEXT NOT NULL,
    reference_context TEXT          -- How it's referenced (property value, extends, etc.)
);
CREATE INDEX idx_ref_target ON xml_references(target_type, target_name);
```

### Table: `csharp_xml_lookups`
```sql
-- C# code that looks up XML definitions
CREATE TABLE csharp_xml_lookups (
    id INTEGER PRIMARY KEY,
    method_name TEXT NOT NULL,
    file_path TEXT NOT NULL,
    line_number INTEGER,
    lookup_type TEXT NOT NULL,      -- 'item', 'block', 'entity', 'buff', etc.
    lookup_pattern TEXT,            -- The string/pattern being looked up
    is_dynamic BOOLEAN              -- true if lookup uses variable (can't determine statically)
);
CREATE INDEX idx_lookup_type ON csharp_xml_lookups(lookup_type);
CREATE INDEX idx_lookup_pattern ON csharp_xml_lookups(lookup_pattern);
```

---

## C# → XML Lookup Patterns to Detect

### Items
```csharp
ItemClass.GetItem("itemName")
ItemClass.GetItemClass("itemName")  
new ItemValue(ItemClass.GetItem("itemName").Id)
ItemClass.GetForId(id)
```

### Blocks
```csharp
Block.GetBlockByName("blockName")
Block.GetBlockValue("blockName")
Block.list[Block.GetBlockByName("blockName").blockID]
```

### Entities
```csharp
EntityClass.list["entityName"]
EntityFactory.CreateEntity("entityName", ...)
EntityGroups.GetEnemyGroup("groupName")
```

### Buffs
```csharp
BuffManager.GetBuff("buffName")
BuffClass.list["buffName"]
entityPlayer.Buffs.AddBuff("buffName")
entityPlayer.Buffs.HasBuff("buffName")
```

### Sounds
```csharp
Manager.Play("soundName", ...)
entity.PlayOneShot("soundName")
```

### Properties (Generic)
```csharp
Properties.GetString("propertyName")
Properties.GetInt("propertyName")
Properties.GetFloat("propertyName")
Properties.ParseString("propertyName")
Properties.Values["propertyName"]
```

### Recipes
```csharp
CraftingManager.GetRecipe("recipeName")
Recipe.GetRecipes()
```

### Quests
```csharp
QuestClass.GetQuest("questId")
```

### Game Events
```csharp
GameEventManager.Current.HandleAction("eventName", ...)
```

---

## Mod XML Analysis

### Supported XPath Operations
| Operation | Effect | Risk Level |
|-----------|--------|------------|
| `<set xpath="...">` | Modifies existing value | Medium |
| `<append xpath="...">` | Adds new content | Low |
| `<insertAfter xpath="...">` | Adds after target | Low |
| `<insertBefore xpath="...">` | Adds before target | Low |
| `<remove xpath="...">` | **Deletes content** | **HIGH** |
| `<removeattribute xpath="...">` | Removes attribute | High |
| `<setattribute xpath="...">` | Changes attribute | Medium |
| `<csv xpath="..." op="add">` | Adds to CSV list | Low |
| `<csv xpath="..." op="remove">` | Removes from CSV | High |

### Conflict Detection Rules

1. **Removal Conflicts**
   - If mod A removes `/items/item[@name='X']`
   - And C# code calls `ItemClass.GetItem("X")`
   - → **CRITICAL CONFLICT**

2. **Property Modification Conflicts**
   - If mod A sets `<property name="Class" value="X"/>`
   - And C# code checks for specific class
   - → **Potential conflict**

3. **Load Order Conflicts**
   - If mod A appends to `item[@name='X']`
   - And mod B removes `item[@name='X']`
   - Order matters: B after A = item removed, B before A = error

---

## Implementation Plan

### Phase 1: XML Parser (xml_indexer.py or XmlIndexer.cs)

```
XmlIndexer/
├── XmlIndexer.csproj
├── Program.cs
├── Parsers/
│   ├── ItemsParser.cs
│   ├── BlocksParser.cs
│   ├── EntityClassesParser.cs
│   ├── BuffsParser.cs
│   ├── RecipesParser.cs
│   ├── SoundsParser.cs
│   └── GenericXmlParser.cs
├── Database/
│   ├── XmlDatabase.cs
│   └── Schema.sql
└── Analysis/
    ├── CSharpXmlReferences.cs
    └── ModConflictDetector.cs
```

### Phase 2: C# Reference Extractor

Enhance existing callgraph builder to also extract:
- String literals passed to XML lookup methods
- Track which methods use XML-defined entities

### Phase 3: QueryDb Enhancement

Add new commands:
```bash
# Find what references an XML definition
QueryDb.exe xml_full.db refs "itemRepairKit"

# Find what a C# method depends on (XML-wise)
QueryDb.exe xml_full.db deps "XUiM_Vehicle.RepairVehicle"

# Check if a mod's removals conflict with C# code
QueryDb.exe xml_full.db check-mod "path/to/mod"

# List all XML definitions of a type
QueryDb.exe xml_full.db list items

# Find definitions with a specific property
QueryDb.exe xml_full.db find-prop "HeatMapStrength"
```

---

## Example Queries

### "What C# code uses itemRepairKit?"
```sql
SELECT source_file, source_line, reference_context
FROM csharp_xml_lookups
WHERE lookup_type = 'item' AND lookup_pattern = 'itemRepairKit';
```

### "What would break if I remove zombieScreamer?"
```sql
-- Find all XML references
SELECT * FROM xml_references 
WHERE target_type = 'entity_class' AND target_name = 'zombieScreamer';

-- Find all C# references
SELECT * FROM csharp_xml_lookups
WHERE lookup_type = 'entity' AND lookup_pattern LIKE '%zombieScreamer%';
```

### "What does this mod's remove xpath affect?"
Given: `<remove xpath="/blocks/block/property[@name='HeatMapStrength']"/>`

```sql
-- Find all definitions that have this property
SELECT d.definition_type, d.name, p.property_value
FROM xml_definitions d
JOIN xml_properties p ON d.id = p.definition_id
WHERE p.property_name = 'HeatMapStrength';

-- Find C# code that reads this property
SELECT * FROM csharp_xml_lookups
WHERE lookup_pattern = 'HeatMapStrength';
```

---

## Libraries & Dependencies

### For C# Implementation
- `System.Xml.Linq` - Built-in LINQ to XML (perfect for this)
- `Microsoft.Data.Sqlite` - Already using for callgraph
- `Microsoft.CodeAnalysis` - Already using for C# parsing

### No External Dependencies Needed
.NET's built-in XML libraries are more than sufficient:
```csharp
using System.Xml.Linq;

var doc = XDocument.Load("items.xml");
var items = doc.Descendants("item")
    .Select(e => new {
        Name = e.Attribute("name")?.Value,
        Properties = e.Elements("property")
            .ToDictionary(p => p.Attribute("name")?.Value, 
                          p => p.Attribute("value")?.Value)
    });
```

---

## Integration with Existing Toolkit

### Option A: Separate Database
- `callgraph_full.db` - C# code analysis (existing)
- `xml_definitions.db` - XML data analysis (new)
- QueryDb queries both when needed

### Option B: Combined Database
- Add XML tables to existing callgraph database
- Single database for all queries
- Easier cross-referencing

**Recommendation: Option B** - Combined database makes cross-reference queries much simpler.

---

## Example Conflict Report

```
=== MOD CONFLICT ANALYSIS: zzzzzzByteblazarsNoHeatGeneration ===

Analyzing: Config/blocks.xml
  <remove xpath="/blocks/block/property[@name='Class' and @value='TorchHeatMap']"/>
  
  [!] This removes 'Class' property with value 'TorchHeatMap' from blocks
  
  Affected definitions: 12 blocks
    - blockCandleWall
    - blockTorchWall
    - blockCampfire
    ... (9 more)
  
  C# Code Dependencies:
    [NONE FOUND] - No C# code directly references 'TorchHeatMap' class
  
  Risk: LOW - Pure XML feature, no C# dependencies

---
  <remove xpath="/blocks/block/property[starts-with(@name,'HeatMap')]"/>
  
  [!] This removes all HeatMap* properties from blocks
  
  Affected properties: 
    - HeatMapStrength (47 blocks)
    - HeatMapTime (12 blocks)
    - HeatMapWorldTime (3 blocks)
  
  C# Code Dependencies:
    [NONE FOUND] - HeatMap properties appear to be XML-only
  
  Risk: LOW

=== SUMMARY ===
  Total modifications: 2
  High-risk conflicts: 0
  Medium-risk conflicts: 0
  Low-risk items: 2
  
  VERDICT: This mod appears safe to use.
```

---

## Priority Implementation Order

1. **XML Definition Parser** - Parse base game XML into database
2. **C# Lookup Extractor** - Find all `GetItem`, `GetBlock`, etc. calls
3. **Cross-Reference Builder** - Link C# lookups to XML definitions
4. **Mod Analyzer** - Parse mod XML patches and check for conflicts
5. **QueryDb Commands** - Add XML query commands

Estimated effort: 2-3 days for full implementation
