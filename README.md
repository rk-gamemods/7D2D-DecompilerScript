# 7D2D Mod Ecosystem Analyzer

Analyze your 7 Days to Die mods, detect conflicts, generate compatibility reports.

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [7 Days to Die](https://store.steampowered.com/app/251570/7_Days_to_Die/)
- Windows 10/11

## Quick Start

```cmd
git clone https://github.com/rk-gamemods/7D2D-DecompilerScript.git
cd 7D2D-DecompilerScript
copy config.example.txt config.txt   &:: Edit GAME_PATH in config.txt
RunAnalysis.bat
```

Report opens in your browser.

---

## What RunAnalysis.bat Does

```
┌─────────────────┐
│  config.txt     │ ← Your game/mods paths
└────────┬────────┘
         ▼
┌─────────────────┐
│ Validate paths  │ ← Checks game install exists
└────────┬────────┘
         ▼
┌─────────────────┐
│ Build toolkit   │ ← First run only (~30s)
└────────┬────────┘
         ▼
┌─────────────────┐
│ Analyze mods    │ ← Index XML, detect Harmony patches
└────────┬────────┘
         ▼
┌─────────────────┐
│ Detect conflicts│ ← Find XML/C# collisions
└────────┬────────┘
         ▼
┌─────────────────┐
│ Generate report │ ← HTML in ./output/
└────────┬────────┘
         ▼
┌─────────────────┐
│ Open browser    │
└─────────────────┘
```

**Output:** `./output/ecosystem_YYYY-MM-DD_HHMM/index.html`

---

## Config File

```ini
# config.txt
GAME_PATH=C:\Steam\steamapps\common\7 Days To Die
MODS_PATH=C:\Steam\steamapps\common\7 Days To Die\Mods
CODEBASE_PATH=                        # Optional: path to decompiled code
```

---

## Optional: Game Code Analysis

For the **Game Code** report page, decompile the game first:

```powershell
.\Decompile-7D2D.ps1
```

Then set `CODEBASE_PATH=.\7D2DCodebase` in config.txt.

---

## Advanced: Manual CLI

```powershell
cd toolkit/XmlIndexer
dotnet run -- report "C:\...\7 Days To Die" "C:\...\Mods" ./output --open
dotnet run -- refs ecosystem.db item gunPistol
dotnet run -- search ecosystem.db "*medical*"
```

See [USER_GUIDE.md](toolkit/XmlIndexer/USER_GUIDE.md) for all commands.

---

## Troubleshooting

| Error | Fix |
|-------|-----|
| `config.txt not found` | `copy config.example.txt config.txt` |
| `Game path invalid` | Check GAME_PATH has `Data\Config` folder |
| `.NET not found` | Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `Build failed` | Run `cd toolkit\XmlIndexer && dotnet build -c Release` |

---

## License

MIT
