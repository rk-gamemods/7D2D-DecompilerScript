# 7D2D Mod Maintenance Toolkit - Python CLI

A toolkit for analyzing 7 Days to Die game code and checking mod compatibility.

## Installation

```bash
cd toolkit/7d2d-tools
pip install -e .
```

For development:
```bash
pip install -e ".[dev]"
```

## Commands

```bash
# Build call graph database
7d2d-tools build --source /path/to/7D2DCodebase --output game.db

# Find callers of a method
7d2d-tools callers GetItemCount --db game.db

# Find callees of a method
7d2d-tools callees OnItemAdded --db game.db

# Trace call chain between methods
7d2d-tools chain CraftingManager.Craft Bag.DecItem --db game.db

# Search method bodies
7d2d-tools search "inventory" --db game.db

# Check mod compatibility
7d2d-tools compat ProxiCraft BeyondStorage2 --db game.db
```

## Output Format

All commands output JSON for easy parsing by AI tools and scripts.
