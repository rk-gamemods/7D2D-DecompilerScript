# AI Knowledge Base

This folder contains pre-generated knowledge files for AI assistants to quickly understand the 7 Days to Die codebase.

## Files

| File | Description | Use When |
|------|-------------|----------|
| [entities.md](entities.md) | Items, blocks, and properties with descriptions | Understanding game entities |
| [events.md](events.md) | Event system documentation | Working with game events |
| [methods.md](methods.md) | Key methods and call statistics | Finding important methods |
| [modding-patterns.md](modding-patterns.md) | Common Harmony patch patterns | Writing mods |

## Usage for AI Assistants

1. **Before answering questions about entities**: Check `entities.md`
2. **For event-related queries**: Check `events.md`
3. **For method lookup**: Check `methods.md` or use QueryDb
4. **For modding guidance**: Check `modding-patterns.md`

## Data Sources

These files are generated from:
- `toolkit/callgraph_full.db` - Unified database with 20K+ semantic mappings

## Regeneration

To regenerate these files with updated data, use the scripts in `toolkit/XmlIndexer/archive/scripts/`.

## Last Updated

2026-01-02
