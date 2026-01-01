# AI Context for 7D2D Mod Maintenance Toolkit

This document provides guidance for AI assistants (like GitHub Copilot) on how to effectively use the 7D2D toolkit database to answer questions about game code, debug mod issues, and analyze mod compatibility.

## Overview

The toolkit maintains a SQLite database (`callgraph.db`) containing:
- **4,725 types** — All classes, structs, interfaces, enums from game code
- **39,342 methods** — Every method with signature, location, body text
- **117,757 internal calls** — Call graph edges within game code
- **47,514 external calls** — Calls to Unity, .NET, and third-party libraries
- **131,960 XML definitions** — Properties from game config files (items.xml, blocks.xml, etc.)
- **Mod data** — Harmony patches and XML changes from parsed mods

## Tool Location

```
7D2D-DecompilerScript/toolkit/QueryDb/
```

Run commands from this directory:
```powershell
cd toolkit/QueryDb
dotnet run -- "<database-path>" <command> [args]
```

Database is typically at: `7D2D-DecompilerScript/callgraph.db`

---

## Command Reference

### Summary Statistics
```
dotnet run -- callgraph.db
```
Shows database overview: table counts, types by assembly, external call targets, mods parsed.

### Custom SQL
```
dotnet run -- callgraph.db sql "SELECT * FROM methods WHERE name = 'DecItem'"
```
Run any SQL query directly. Use for complex queries not covered by convenience commands.

### Find Callers
```
dotnet run -- callgraph.db callers <method-name>
dotnet run -- callgraph.db callers "TypeName.MethodName"
```
Find all methods that call the specified method. If method name is ambiguous, lists all matches with suggestions to be more specific.

**Use case:** "What calls this method I want to patch?"

### Find Callees
```
dotnet run -- callgraph.db callees <method-name>
dotnet run -- callgraph.db callees "PlayerMoveController.Update"
```
Find all methods called by the specified method. Shows both internal (game code) and external (Unity/BCL) calls.

**Use case:** "What does this method do? What does it depend on?"

### Full-Text Search
```
dotnet run -- callgraph.db search <keyword>
dotnet run -- callgraph.db search "GetComponent AND Update"
```
Search method bodies for keywords. Uses FTS5 with porter stemming. Shows snippets with highlighted matches.

**Use case:** "Where is this pattern used in the codebase?"

### Find Call Path
```
dotnet run -- callgraph.db chain <from-method> <to-method>
```
Find call path between two methods using recursive CTE (depth limit 10).

**Use case:** "How does execution flow from A to B?"

### Find Implementations
```
dotnet run -- callgraph.db impl <method-name>
```
Find all implementations/overrides of a method across the type hierarchy.

**Use case:** "What classes override this virtual method? Am I patching all of them?"

### Check Mod Compatibility
```
dotnet run -- callgraph.db compat
```
Analyze all mods in database for conflicts:
- Methods patched by multiple mods (Harmony conflicts)
- XML paths modified by multiple mods
- Load order suggestions (Transpilers before Prefix/Postfix)

**Use case:** "Will these mods conflict?"

### Performance Analysis
```
dotnet run -- callgraph.db perf [category]
```
Categories: `updates`, `getcomponent`, `find`, `strings`, `linq`, or omit for all.

- `updates` — Largest Update/LateUpdate/FixedUpdate methods by code size
- `getcomponent` — Update methods calling GetComponent (should be cached)
- `find` — Methods using FindObjectsOfType (O(n) expensive)
- `strings` — Update methods with string allocations (GC pressure)
- `linq` — LINQ in hot paths (allocations)

**Use case:** "What are performance bottlenecks in game code?"

### XML Query
```
dotnet run -- callgraph.db xml <item-or-property>
dotnet run -- callgraph.db xml gunPistol
```
Look up XML definitions and find code that accesses them.

**Use case:** "What properties does this item have? What code reads this property?"

### JSON Output
Add `--json` flag to any command for structured output:
```
dotnet run -- callgraph.db callers DecItem --json
```

---

## Common Query Patterns

### When User Asks: "What happens when X?"

1. Use `callers` to find entry points
2. Use `callees` to trace execution flow
3. Use `chain` if you need path between specific methods

Example: "What happens when player eats an item?"
```bash
# Find the eat action
dotnet run -- callgraph.db search "ItemActionEat"
# See what it calls
dotnet run -- callgraph.db callees "ItemActionEat.OnHoldingUpdate"
```

### When User Asks: "Where is X defined/used?"

1. Use `search` for keyword in method bodies
2. Use `callers` if you know the method name
3. Use `sql` for complex queries

Example: "Where is Stacknumber used?"
```bash
# Search for the string
dotnet run -- callgraph.db search "Stacknumber"
# Or via XML property access
dotnet run -- callgraph.db sql "SELECT * FROM xml_property_access WHERE property_name = 'Stacknumber'"
```

### When User Asks: "Will my patch affect X?"

1. Use `callees` to see what the patched method touches
2. Use `callers` to see what calls the patched method
3. Use `impl` to check for other implementations you might miss

Example: "If I patch GetItemCount, what else might be affected?"
```bash
dotnet run -- callgraph.db callers GetItemCount
dotnet run -- callgraph.db impl GetItemCount
```

### When User Asks: "Why isn't my patch working?"

Common issues this toolkit can detect:

1. **Wrong overload patched** — Use `impl` to find all overloads
2. **Called from unexpected context** — Use `callers` to see all call sites
3. **Multiple implementations** — Use `impl` to check type hierarchy
4. **Event re-entrancy** — Use `callees` on event handlers

### When User Asks: "Are these mods compatible?"

```bash
dotnet run -- callgraph.db compat
```
Check for:
- Same method patched by multiple mods
- Same XML xpath modified by multiple mods
- Load order issues

---

## Key Tables (for custom SQL)

```sql
-- Types (classes, structs, etc.)
SELECT * FROM types WHERE name LIKE '%Player%';

-- Methods with signatures and locations
SELECT * FROM methods WHERE name = 'Update' AND type_id IN 
  (SELECT id FROM types WHERE name LIKE '%Controller%');

-- Internal call graph
SELECT caller.name, callee.name 
FROM calls c
JOIN methods caller ON c.caller_id = caller.id
JOIN methods callee ON c.callee_id = callee.id;

-- External calls (Unity/BCL)
SELECT target_type, target_method, COUNT(*) 
FROM external_calls 
GROUP BY target_type, target_method 
ORDER BY COUNT(*) DESC;

-- Method bodies (FTS5)
SELECT * FROM method_bodies WHERE method_bodies MATCH 'GetComponent';

-- XML definitions
SELECT * FROM xml_definitions WHERE element_name = 'gunPistol';

-- Code → XML property access
SELECT * FROM xml_property_access WHERE property_name = 'Stacknumber';

-- Harmony patches
SELECT * FROM harmony_patches WHERE target_method = 'DecItem';

-- XML changes from mods
SELECT * FROM xml_changes WHERE file_name = 'items.xml';
```

---

## Efficiency Tips for AI Assistants

### Don't dump source files
Instead of reading thousands of lines of source code, query the database:
- Use `callers`/`callees` to understand call flow
- Use `search` to find relevant code snippets
- Result: 500 tokens instead of 50,000

### Handle ambiguous method names
Many methods share names (e.g., 610 methods named "Update"). The `callers` and `callees` commands will:
1. Detect ambiguity
2. List all matches with types
3. Suggest using "TypeName.MethodName" format

### Use specific queries
```bash
# Bad: Will return 610 methods
callers Update

# Good: Specific type
callers "PlayerMoveController.Update"

# Good: Search for context
search "PlayerMoveController AND Update"
```

### Combine commands
For complex questions, run multiple commands:
```bash
# "What writes to player inventory?"
search "bag.DecItem OR inventory.DecItem"
callers "Bag.DecItem"
callers "Inventory.DecItem"
```

---

## Real-World Example Queries

### Find all crafting-related methods
```bash
dotnet run -- callgraph.db search "Craft"
dotnet run -- callgraph.db sql "SELECT t.name, m.name, m.file_path, m.line_number 
  FROM methods m JOIN types t ON m.type_id = t.id 
  WHERE m.name LIKE '%Craft%' OR t.name LIKE '%Craft%'"
```

### Find what changes item stack counts
```bash
dotnet run -- callgraph.db callers "ItemStack.set_count"
dotnet run -- callgraph.db search "count ="
dotnet run -- callgraph.db callers DecItem
dotnet run -- callgraph.db callers IncItem
```

### Understand item property loading
```bash
# What properties does an item have?
dotnet run -- callgraph.db xml gunHandgunT1Pistol

# What code reads these properties?
dotnet run -- callgraph.db sql "SELECT * FROM xml_property_access WHERE property_name = 'Stacknumber'"
```

### Identify performance issues in Update loops
```bash
dotnet run -- callgraph.db perf
# Focus on specific issue:
dotnet run -- callgraph.db perf getcomponent
```

### Check mod compatibility before installation
```bash
# Parse mods first (via CallGraphExtractor with --mods flag)
# Then check:
dotnet run -- callgraph.db compat
```

---

## Troubleshooting

### "No results" when expecting matches
- Check spelling/case (searches are case-insensitive but method names must be exact)
- Try `search` instead of `callers` for substring matching
- Use wildcards in SQL: `WHERE name LIKE '%ItemCount%'`

### "Method not found" 
- The method may not be in game code (could be in Unity or BCL)
- Use `sql` to check: `SELECT * FROM methods WHERE name LIKE '%MethodName%'`
- Check `external_calls` table for Unity/BCL calls

### "Path not found" with `chain`
- Methods may not be connected, or path is longer than depth limit (10)
- Try intermediate steps: find callers of target, then chain from source to those

### Large result sets
- Add `LIMIT 50` to SQL queries
- Use more specific search terms
- Use type-qualified method names

---

## Summary

| Question Type | Primary Command | Supporting Commands |
|--------------|-----------------|---------------------|
| "What calls X?" | `callers` | `search`, `sql` |
| "What does X call?" | `callees` | - |
| "Where is X used?" | `search` | `callers`, `sql` |
| "How do I get from A to B?" | `chain` | `callers`, `callees` |
| "What implements X?" | `impl` | `sql` |
| "Will these mods conflict?" | `compat` | `sql` |
| "What's slow in game code?" | `perf` | `callees` |
| "What are X's properties?" | `xml` | `search` |

The toolkit turns "dump 200K tokens of source" into "query and get 500 tokens". Use it.
