# Database Schema Documentation

This document describes the unified database schema for `callgraph_full.db`.

## Schema Version: 3

## Table Overview

| Category | Table | Description | Rows |
|----------|-------|-------------|------|
| **Code** | types | Classes, structs, interfaces | 4,725 |
| **Code** | methods | Method definitions | 39,342 |
| **Code** | calls | Internal call graph | 117,757 |
| **Code** | external_calls | Calls to Unity/BCL | 47,514 |
| **Code** | method_bodies | FTS5 searchable code | 32,013 |
| **Ecosystem** | xml_definitions | Items, blocks, buffs | 15,534 |
| **Ecosystem** | xml_properties | Property values | 65,267 |
| **Ecosystem** | xml_references | Cross-references | 47,332 |
| **Ecosystem** | semantic_mappings | AI descriptions | 20,462 |
| **Ecosystem** | ecosystem_entities | Unified entity view | 15,534 |
| **Mods** | mods | Registered mods | 14 |
| **Mods** | mod_xml_operations | XML changes | 55 |
| **Mods** | mod_csharp_deps | Code dependencies | 104 |
| **Mods** | harmony_patches | Patch targets | 0 |
| **Events** | event_declarations | Event definitions | 307 |
| **Events** | event_subscriptions | Event handlers | 1,917 |
| **Events** | event_fires | Event triggers | 407 |
| **Cache** | query_cache | Query results | 0 |
| **AI** | method_summaries | Method descriptions | 0 |

## Entity Relationship Diagram

```
                    +-------------+
                    |   types     |
                    +-------------+
                          |
                          | 1:N
                          v
+-------------+    +-------------+    +----------------+
| method_     |<---|   methods   |--->| method_        |
| bodies      |    +-------------+    | summaries      |
+-------------+          |            +----------------+
                         |
           +-------------+-------------+
           |             |             |
           v             v             v
    +----------+   +----------+  +-----------+
    |  calls   |   | external |  | xml_prop_ |
    |          |   | _calls   |  | access    |
    +----------+   +----------+  +-----------+

+----------------+     +------------------+
| xml_definitions|<--->| xml_properties   |
+----------------+     +------------------+
        |
        v
+----------------+     +------------------+
| ecosystem_     |     | semantic_        |
| entities       |     | mappings         |
+----------------+     +------------------+

+----------+     +------------------+     +----------------+
|   mods   |---->| mod_xml_         |     | harmony_       |
|          |     | operations       |     | patches        |
+----------+     +------------------+     +----------------+
     |
     +---------->+------------------+
                 | mod_csharp_deps  |
                 +------------------+

+------------------+
| event_           |     +------------------+
| declarations     |---->| event_           |
+------------------+     | subscriptions    |
        |                +------------------+
        v
+------------------+
| event_fires      |
+------------------+
```

## Table Details

### Code Analysis

#### types
Game types (classes, structs, interfaces, enums).

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Primary key |
| name | TEXT | Simple name |
| namespace | TEXT | Namespace |
| full_name | TEXT | Fully qualified name |
| kind | TEXT | class/struct/interface/enum |
| base_type | TEXT | Parent class |
| assembly | TEXT | Source assembly |

#### methods
Method definitions with metadata.

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Primary key |
| type_id | INTEGER | FK to types |
| name | TEXT | Method name |
| signature | TEXT | Full signature |
| return_type | TEXT | Return type |
| file_path | TEXT | Source file |
| line_number | INTEGER | Line in source |

#### calls
Internal call graph edges.

| Column | Type | Description |
|--------|------|-------------|
| caller_id | INTEGER | FK to methods |
| callee_id | INTEGER | FK to methods |
| call_type | TEXT | direct/virtual/delegate |

### Ecosystem

#### xml_definitions
Game entity definitions from XML.

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Primary key |
| definition_type | TEXT | item/block/buff/etc |
| name | TEXT | Entity name |
| file_path | TEXT | Source XML file |
| extends | TEXT | Parent definition |

#### semantic_mappings
AI-generated entity descriptions.

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Primary key |
| entity_type | TEXT | Type of entity |
| entity_name | TEXT | Entity name |
| layman_description | TEXT | Plain English |
| technical_description | TEXT | Technical details |
| player_impact | TEXT | Gameplay effects |

### Events

#### event_declarations
Events defined in game code.

| Column | Type | Description |
|--------|------|-------------|
| owning_type | TEXT | Type that owns event |
| event_name | TEXT | Event field name |
| delegate_type | TEXT | Delegate signature |

#### event_subscriptions
Event subscription points.

| Column | Type | Description |
|--------|------|-------------|
| subscriber_type | TEXT | Subscribing type |
| event_owner_type | TEXT | Event owner |
| event_name | TEXT | Event name |
| handler_method | TEXT | Handler method |

## Example Queries

### Find methods by name
```sql
SELECT t.name, m.name, m.signature
FROM methods m
JOIN types t ON m.type_id = t.id
WHERE m.name LIKE '%Update%';
```

### Get entity with properties
```sql
SELECT d.name, p.property_name, p.property_value
FROM xml_definitions d
JOIN xml_properties p ON d.id = p.definition_id
WHERE d.name = 'gunPistol';
```

### Search semantic descriptions
```sql
SELECT entity_type, entity_name, layman_description
FROM semantic_mappings
WHERE layman_description LIKE '%healing%';
```

### Find event handlers
```sql
SELECT subscriber_type, handler_method
FROM event_subscriptions
WHERE event_name = 'ItemRemoved';
```
