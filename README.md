# 7D2D Decompiler Script

A PowerShell utility for extracting 7 Days to Die game code and data files. Designed to help mod developers track ALL changes between game updates - both code and XML configs.

## Features

- **Extracts everything** - Decompiled C# code AND XML config files
- **Auto-detects** 7 Days to Die installation (common Steam paths)
- **Git integration** - automatically tracks version history with commits
- **Version diffing** - see exactly what changed between game updates (code + data)
- **Non-destructive** - without git, creates versioned folders instead of overwriting
- **Auto-installs** ILSpyCmd if not present
- **Flexible** - can extract code only, data only, or both

## Quick Start

```powershell
# Clone this repo
git clone https://github.com/rk-gamemods/7D2D-DecompilerScript.git
cd 7D2D-DecompilerScript

# Run the extractor
.\Decompile-7D2D.ps1
```

The script will:
1. Auto-detect your 7D2D installation
2. Detect game version
3. Decompile game assemblies to `7D2DCodebase/`
4. Copy game data files (XML configs) to `7D2DCodebase/Data/`
5. Initialize git and commit (if git is available)

## Output Structure

```
7D2DCodebase/
├── Assembly-CSharp/           # Main game code
├── Assembly-CSharp-firstpass/ # Additional game code
├── 0Harmony/                  # Harmony library
├── Data/
│   └── Config/                # All XML configs
│       ├── blocks.xml
│       ├── items.xml
│       ├── recipes.xml
│       ├── buffs.xml
│       ├── loot.xml
│       ├── progression.xml
│       ├── XUi/               # UI definitions
│       ├── Localization.txt   # All game text
│       └── ...
└── README.md
```

## Requirements

- **PowerShell 5.1+** (included with Windows 10/11)
- **.NET SDK** - for ILSpyCmd ([download](https://dotnet.microsoft.com/download))
- **Git** (optional but recommended) - for version tracking ([download](https://git-scm.com/download/win))

## Usage

### Basic Usage (Extract Everything)

```powershell
.\Decompile-7D2D.ps1
```

### Code Only (Skip XML Data)

```powershell
.\Decompile-7D2D.ps1 -CodeOnly
```

### Data Only (Skip Decompilation)

```powershell
.\Decompile-7D2D.ps1 -DataOnly
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

When git is available, everything goes in a single folder with git history:

```powershell
# After game update, just run again
.\Decompile-7D2D.ps1

# See what changed
cd 7D2DCodebase
git log --oneline                             # Version history
git diff HEAD~1 --stat                        # Summary of ALL changes
git diff HEAD~1 -- "*.cs"                     # Code changes only
git diff HEAD~1 -- "Data/Config/*.xml"        # XML changes only
git diff HEAD~1 -- Data/Config/items.xml      # Specific file
```

### Without Git

Creates versioned folders to preserve previous versions:

```
7D2DCodebase/           # First extraction (v2.5)
7D2DCodebase_v2.6.123/  # Second extraction (v2.6)
```

Use VS Code's folder compare feature to diff between versions.

## What Gets Extracted

### Decompiled Code

| Assembly | Contents |
|----------|----------|
| `Assembly-CSharp.dll` | Main game code - TileEntity, XUi, EntityPlayer, etc. |
| `Assembly-CSharp-firstpass.dll` | Additional game systems |
| `0Harmony.dll` | Harmony patching library (reference) |

### Game Data Files

| File | Purpose |
|------|---------|
| `blocks.xml` | Block definitions, destruction effects, loot |
| `items.xml` | Item properties - damage, durability, etc. |
| `recipes.xml` | Crafting recipes and requirements |
| `buffs.xml` | Status effects, food buffs, injuries |
| `entityclasses.xml` | Entity (zombie, animal) definitions |
| `loot.xml` | Loot table definitions |
| `progression.xml` | Skills, perks, and their effects |
| `quests.xml` | Quest definitions |
| `traders.xml` | Trader inventories and prices |
| `sounds.xml` | Sound effect definitions |
| `XUi/*.xml` | UI layout definitions |
| `Localization.txt` | All game text strings |

## Use Cases

### After a Game Update

1. Run `.\Decompile-7D2D.ps1`
2. Check the diff summary - see both code AND XML changes
3. Search for classes/items your mod affects to see if they changed

### Finding Method Signatures

```powershell
cd 7D2DCodebase
# Use VS Code search (Ctrl+Shift+F) or grep
Select-String -Path "Assembly-CSharp\*.cs" -Pattern "GetItemCount"
```

### Finding Item Properties

```powershell
# Search XML for item definitions
Select-String -Path "Data\Config\items.xml" -Pattern "drinkJarPureMineralWater"
```

### Understanding Game Systems

Search for connections between code and data:
- Code shows HOW things work
- XML shows WHAT is configured
- Together, complete picture

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

## Key XML Files for Modding

- **items.xml** - Add/modify items, properties, effects
- **blocks.xml** - Block definitions, what drops when destroyed
- **recipes.xml** - What you can craft and requirements
- **buffs.xml** - Status effects from food, injuries, etc.
- **progression.xml** - Skills, perks, level requirements
- **loot.xml** - What spawns in containers
- **Localization.txt** - All text shown to players

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
- [AudibleBreakingGlassJars](https://github.com/rk-gamemods/7D2D-AudibleBreakingGlassJars) - Sound when jars break
