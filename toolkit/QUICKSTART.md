# 7D2D Toolkit Quick Start

## Installation

```bash
# Build tools
cd toolkit/CallGraphExtractor
dotnet build -c Release

cd ../QueryDb
dotnet build -c Release

cd ../XmlIndexer
dotnet build -c Release
```

## Database Location

All data is in a single unified database:
```
toolkit/callgraph_full.db
```

## Common Queries

### Code Analysis (QueryDb)

```bash
cd toolkit/QueryDb

# Search for code
dotnet run -- "../callgraph_full.db" search "MethodName"

# Find callers
dotnet run -- "../callgraph_full.db" callers "Class.Method"

# Find callees
dotnet run -- "../callgraph_full.db" callees "Class.Method"

# Trace call chain
dotnet run -- "../callgraph_full.db" chain "StartMethod" "EndMethod"

# Find implementations
dotnet run -- "../callgraph_full.db" impl "InterfaceMethod"

# Performance hotspots
dotnet run -- "../callgraph_full.db" perf

# Mod compatibility check
dotnet run -- "../callgraph_full.db" compat
```

### XML/Entity Search (XmlIndexer)

```bash
cd toolkit/XmlIndexer

# Search entities by semantic description
dotnet run -- search-semantic "healing items"

# Get entity info
dotnet run -- entity item gunPistol

# List definitions
dotnet run -- list items
```

### Direct SQL Queries

```bash
# Custom SQL query
dotnet run -- "../callgraph_full.db" sql "SELECT * FROM methods WHERE name LIKE '%Update%' LIMIT 10"
```

## Database Tables

### Core Tables
| Table | Purpose |
|-------|---------|
| `methods` | All game methods (39K+) |
| `types` | All game types (4.7K+) |
| `calls` | Method call graph (117K+) |
| `method_bodies` | Full-text searchable code |

### Ecosystem Tables
| Table | Purpose |
|-------|---------|
| `xml_definitions` | Game XML definitions (15K+) |
| `xml_properties` | XML property values (65K+) |
| `semantic_mappings` | AI-generated descriptions (20K+) |
| `ecosystem_entities` | Unified entity view |

### Mod Analysis Tables
| Table | Purpose |
|-------|---------|
| `mods` | Registered mods |
| `mod_xml_operations` | XML changes by mods |
| `mod_csharp_deps` | Code dependencies |
| `harmony_patches` | Harmony patch targets |

## For AI Assistants

See [AI_CONTEXT.md](../AI_CONTEXT.md) for:
- Database schema details
- Query patterns
- Common use cases
