# Demo Report

This folder contains a sample ecosystem report demonstrating the XmlIndexer output format.

## Purpose

This demo report serves as documentation to show:
- Report structure and page layout
- Navigation between report sections
- Data visualization capabilities
- Search and filter functionality

## Generating Your Own Report

To generate a fresh report for your mod collection:

```powershell
cd toolkit/XmlIndexer
dotnet run -- ecosystem --mods "C:\path\to\your\mods" --output "./my-report"
```

## Report Pages

| Page | Description |
|------|-------------|
| `index.html` | Dashboard with ecosystem overview and statistics |
| `mods.html` | All mods with health status, dependencies, and details |
| `entities.html` | Game entities modified by mods |
| `conflicts.html` | Potential conflicts between mods |
| `dependencies.html` | Dependency graph visualization |
| `csharp.html` | C# code analysis (Harmony patches, class extensions) |
| `gamecode.html` | Game code references and usage |
| `glossary.html` | Terminology and definitions |

## Note

The demo report in this folder is a static snapshot for documentation purposes.
Run XmlIndexer on your own mod collection to get current analysis.
