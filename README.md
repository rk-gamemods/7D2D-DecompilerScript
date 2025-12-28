# 7D2D Decompiler Script

A PowerShell utility for decompiling 7 Days to Die game assemblies. Designed to help mod developers track code changes between game updates.

## Features

- **Auto-detects** 7 Days to Die installation (common Steam paths)
- **Git integration** - automatically tracks version history with commits
- **Version diffing** - see exactly what changed between game updates
- **Non-destructive** - without git, creates versioned folders instead of overwriting
- **Auto-installs** ILSpyCmd if not present

## Quick Start

```powershell
# Clone this repo
git clone https://github.com/rk-gamemods/7D2D-DecompilerScript.git
cd 7D2D-DecompilerScript

# Run the decompiler
.\Decompile-7D2D.ps1
```

The script will:
1. Auto-detect your 7D2D installation
2. Detect game version
3. Decompile game assemblies to `7D2DCodebase/`
4. Initialize git and commit (if git is available)

## Requirements

- **PowerShell 5.1+** (included with Windows 10/11)
- **.NET SDK** - for ILSpyCmd ([download](https://dotnet.microsoft.com/download))
- **Git** (optional but recommended) - for version tracking ([download](https://git-scm.com/download/win))

## Usage

### Basic Usage

```powershell
.\Decompile-7D2D.ps1
```

### Custom Game Path

```powershell
.\Decompile-7D2D.ps1 -GamePath "D:\Games\7 Days To Die"
```

### Custom Output Path

```powershell
.\Decompile-7D2D.ps1 -OutputPath "C:\MyMods\GameCode"
```

### Skip Git Versioning

```powershell
.\Decompile-7D2D.ps1 -NoGit
```

### Force Overwrite (destructive without git!)

```powershell
.\Decompile-7D2D.ps1 -Force
```

## Version Tracking

### With Git (Recommended)

When git is available, all decompiled code goes in a single folder with git history:

```powershell
# After game update, just run again
.\Decompile-7D2D.ps1

# See what changed
cd 7D2DCodebase
git log --oneline                           # Version history
git diff HEAD~1 --stat                      # Summary of changed files
git diff HEAD~1 -- XUiM_PlayerInventory.cs  # Specific file changes
```

### Without Git

Creates versioned folders to preserve previous versions:

```
7D2DCodebase/           # First decompile (v2.5)
7D2DCodebase_v2.6.123/  # Second decompile (v2.6)
```

Use VS Code's folder compare feature to diff between versions.

## What Gets Decompiled

| Assembly | Contents |
|----------|----------|
| `Assembly-CSharp.dll` | Main game code - TileEntity, XUi, EntityPlayer, etc. |
| `Assembly-CSharp-firstpass.dll` | Additional game systems |
| `0Harmony.dll` | Harmony patching library (reference) |

## Use Cases

### After a Game Update

1. Run `.\Decompile-7D2D.ps1`
2. Check the diff summary
3. Search for classes your mod patches to see if they changed

### Finding Method Signatures

```powershell
cd 7D2DCodebase
# Use VS Code search (Ctrl+Shift+F) or grep
Select-String -Path "Assembly-CSharp\*.cs" -Pattern "GetItemCount"
```

### Understanding Game Systems

The decompiled code shows exactly how the game works internally. Search for:
- Class definitions
- Method implementations
- Event handlers
- How systems connect

## Key Classes for Modding

### Container/Storage
- `TileEntityLootContainer` - Basic loot containers
- `TileEntitySecureLootContainer` - Lockable containers
- `TileEntityComposite` - Modern composite containers
- `TEFeatureStorage` - Storage feature for composites
- `ITileEntityLootable` - Interface for lootable containers

### UI System
- `XUi` - Main UI controller
- `XUiC_LootContainer` - Loot container UI
- `XUiC_ItemStackGrid` - Item grid base class
- `XUiM_PlayerInventory` - Player inventory manager

### Player
- `EntityPlayerLocal` - Local player
- `Bag` - Inventory bag class

### Challenges
- `ChallengeObjectiveGather` - Gather objective
- `ChallengeClass` - Challenge definitions

## Troubleshooting

### "Could not auto-detect 7 Days to Die installation"

Specify the path manually:
```powershell
.\Decompile-7D2D.ps1 -GamePath "C:\Your\Path\To\7 Days To Die"
```

### ".NET SDK not found"

Install from https://dotnet.microsoft.com/download

### "Failed to install ilspycmd"

Install manually:
```powershell
dotnet tool install -g ilspycmd
```

### Git not tracking changes

Make sure git is installed and in your PATH:
```powershell
git --version
```

## License

MIT License - Use freely for your modding projects.

## Related Projects

- [ProxiCraft](https://github.com/rk-gamemods/7D2D-ProxiCraft) - Craft from nearby containers mod
