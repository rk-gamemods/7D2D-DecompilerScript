# Decompile-7D2D.ps1
# Extracts 7 Days to Die game code and data for reference and diffing
# Uses git versioning to track changes between game updates
#
# USAGE:
#   .\Decompile-7D2D.ps1                              # Uses defaults, auto-detects game
#   .\Decompile-7D2D.ps1 -GamePath "D:\Games\7D2D"    # Custom game path
#   .\Decompile-7D2D.ps1 -OutputPath ".\GameCode"    # Custom output path
#   .\Decompile-7D2D.ps1 -NoGit                       # Skip git versioning
#   .\Decompile-7D2D.ps1 -Force                       # Force overwrite (destructive without git!)
#   .\Decompile-7D2D.ps1 -CodeOnly                    # Skip data files, decompile code only
#   .\Decompile-7D2D.ps1 -DataOnly                    # Skip decompilation, copy data files only
#
# REQUIREMENTS:
#   - .NET SDK (for ilspycmd) - https://dotnet.microsoft.com/download
#   - Git (optional but recommended) - https://git-scm.com/download/win
#
# OUTPUT STRUCTURE:
#   7D2DCodebase/
#   ├── Assembly-CSharp/       # Decompiled main game code
#   ├── Assembly-CSharp-firstpass/
#   ├── 0Harmony/
#   └── Data/                  # Game data files (XML configs, etc.)
#       └── Config/            # All XML configuration files
#           ├── blocks.xml
#           ├── items.xml
#           ├── recipes.xml
#           ├── XUi/           # UI definitions
#           └── ...
#
# VERSION TRACKING:
#   WITH GIT (recommended):
#     - All code and data in one folder, git tracks history
#     - Run script after each game update
#     - Use `git diff HEAD~1` to see ALL changes (code + XML)
#
#   WITHOUT GIT:
#     - Creates versioned folders: 7D2DCodebase_v2.5, 7D2DCodebase_v2.6
#     - Use VS Code folder compare to diff versions
#     - Takes more disk space

param(
    [string]$GamePath,      # Path to 7D2D install (auto-detected if not specified)
    [string]$OutputPath,    # Where to put decompiled code (defaults to .\7D2DCodebase)
    [switch]$Force,         # Force overwrite without git
    [switch]$NoGit,         # Skip git versioning entirely
    [switch]$NoDiff,        # Skip showing diff summary after commit
    [switch]$CodeOnly,      # Skip data files, decompile code only
    [switch]$DataOnly       # Skip decompilation, copy data files only
)

$ErrorActionPreference = "Stop"

# Key assemblies to decompile
$assemblies = @(
    "Assembly-CSharp.dll",           # Main game code
    "Assembly-CSharp-firstpass.dll", # Additional game code
    "0Harmony.dll"                   # Harmony library (for reference)
)

# Data folders to copy (relative to game install)
$dataFolders = @(
    "Data\Config"                    # All XML configs (blocks, items, recipes, XUi, etc.)
)

# Auto-detect game path if not specified
function Find-GamePath {
    $commonPaths = @(
        "C:\Steam\steamapps\common\7 Days To Die",
        "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die",
        "C:\Program Files\Steam\steamapps\common\7 Days To Die",
        "D:\Steam\steamapps\common\7 Days To Die",
        "D:\SteamLibrary\steamapps\common\7 Days To Die",
        "E:\SteamLibrary\steamapps\common\7 Days To Die",
        "F:\SteamLibrary\steamapps\common\7 Days To Die"
    )
    
    foreach ($path in $commonPaths) {
        $managedPath = Join-Path $path "7DaysToDie_Data\Managed\Assembly-CSharp.dll"
        if (Test-Path $managedPath) {
            return $path
        }
    }
    
    return $null
}

# Set defaults
if (-not $GamePath) {
    $GamePath = Find-GamePath
    if (-not $GamePath) {
        Write-Host "ERROR: Could not auto-detect 7 Days to Die installation." -ForegroundColor Red
        Write-Host "Please specify -GamePath parameter:" -ForegroundColor Yellow
        Write-Host '  .\Decompile-7D2D.ps1 -GamePath "C:\Path\To\7 Days To Die"' -ForegroundColor Gray
        exit 1
    }
    Write-Host "Auto-detected game at: $GamePath" -ForegroundColor Green
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot "7D2DCodebase"
}

$managedPath = Join-Path $GamePath "7DaysToDie_Data\Managed"

# Validate game path
if (-not (Test-Path (Join-Path $managedPath "Assembly-CSharp.dll"))) {
    Write-Host "ERROR: Invalid game path. Could not find Assembly-CSharp.dll" -ForegroundColor Red
    Write-Host "Expected: $managedPath\Assembly-CSharp.dll" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== 7 Days to Die Code & Data Extractor ===" -ForegroundColor Cyan
Write-Host "Game Path: $GamePath"
Write-Host "Output Path: $OutputPath"
if ($CodeOnly) { Write-Host "Mode: Code only (skipping data files)" -ForegroundColor Yellow }
if ($DataOnly) { Write-Host "Mode: Data only (skipping decompilation)" -ForegroundColor Yellow }
Write-Host ""

# Try to get game version from various sources
function Get-GameVersion {
    param([string]$GamePath)
    
    # Try version.txt first (some versions have this)
    $versionFile = Join-Path $GamePath "version.txt"
    if (Test-Path $versionFile) {
        $version = (Get-Content $versionFile -Raw).Trim()
        if ($version -and $version -notmatch "^\s*$") { return $version }
    }
    
    # Try Steam manifest for build ID
    # Look for appmanifest_251570.acf (7D2D's Steam app ID)
    $steamAppsPath = Split-Path (Split-Path $GamePath -Parent) -Parent
    $manifestPath = Join-Path $steamAppsPath "appmanifest_251570.acf"
    if (Test-Path $manifestPath) {
        $content = Get-Content $manifestPath -Raw
        if ($content -match '"buildid"\s+"(\d+)"') {
            return "build-$($Matches[1])"
        }
    }
    
    # Try to get from Assembly-CSharp.dll file version
    $assemblyPath = Join-Path $GamePath "7DaysToDie_Data\Managed\Assembly-CSharp.dll"
    if (Test-Path $assemblyPath) {
        $fileInfo = (Get-Item $assemblyPath).VersionInfo
        # Only use if not 0.0.0.0
        if ($fileInfo.ProductVersion -and $fileInfo.ProductVersion -ne "0.0.0.0") { 
            return "v$($fileInfo.ProductVersion)" 
        }
        if ($fileInfo.FileVersion -and $fileInfo.FileVersion -ne "0.0.0.0") { 
            return "v$($fileInfo.FileVersion)" 
        }
    }
    
    # Fallback to date
    return "unknown-$(Get-Date -Format 'yyyyMMdd')"
}

$gameVersion = Get-GameVersion -GamePath $GamePath
Write-Host "Detected Game Version: $gameVersion" -ForegroundColor Cyan
Write-Host ""

# Check if git is available
$gitAvailable = $false
if (-not $NoGit) {
    $gitCmd = Get-Command "git" -ErrorAction SilentlyContinue
    if ($gitCmd) {
        $gitAvailable = $true
        Write-Host "Git available - will track version history" -ForegroundColor Green
    } else {
        Write-Host "Git not found - version tracking disabled" -ForegroundColor Yellow
        Write-Host "Install git to enable automatic diff tracking between game updates" -ForegroundColor Gray
    }
}

# Check if ILSpyCmd is installed (only needed if decompiling)
if (-not $DataOnly) {
    $ilspyCmd = Get-Command "ilspycmd" -ErrorAction SilentlyContinue
    if (-not $ilspyCmd) {
        Write-Host "ILSpyCmd not found. Installing via dotnet tool..." -ForegroundColor Yellow
        
        # Check if dotnet is available
        $dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
        if (-not $dotnetCmd) {
            Write-Host "ERROR: .NET SDK not found. Please install from https://dotnet.microsoft.com/download" -ForegroundColor Red
            exit 1
        }
        
        dotnet tool install -g ilspycmd
        
        # Refresh PATH
        $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
        
        $ilspyCmd = Get-Command "ilspycmd" -ErrorAction SilentlyContinue
        if (-not $ilspyCmd) {
            Write-Host "ERROR: Failed to install ilspycmd. Please install manually:" -ForegroundColor Red
            Write-Host "  dotnet tool install -g ilspycmd" -ForegroundColor Yellow
            exit 1
        }
    }
    Write-Host "Using ILSpyCmd: $($ilspyCmd.Source)" -ForegroundColor Green
}

# Check if this is a git-versioned codebase (has previous commits)
$isGitRepo = $gitAvailable -and (Test-Path (Join-Path $OutputPath ".git"))
$hasPreviousVersion = $false
$previousCommit = $null

if ($isGitRepo) {
    Push-Location $OutputPath
    try {
        $previousCommit = git rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $previousCommit) {
            $hasPreviousVersion = $true
            $previousVersion = git log -1 --format="%s" 2>$null
            Write-Host "Previous version found: $previousVersion" -ForegroundColor Yellow
        }
    } finally {
        Pop-Location
    }
}

# Sanitize version for folder name (remove invalid chars)
$safeVersion = $gameVersion -replace '[<>:"/\\|?*]', '_' -replace '\s+', '_'

# Handle existing output directory
# For partial runs (-CodeOnly/-DataOnly), we only clear what we're replacing
# For full runs, we clear everything
$isPartialRun = $CodeOnly -or $DataOnly

if (Test-Path $OutputPath) {
    if ($isGitRepo) {
        # Git repo exists - safe to update in place (history preserved)
        Write-Host "Git repo exists - will update and commit changes..." -ForegroundColor Yellow
        if (-not $isPartialRun) {
            # Full run - clear everything except .git
            Push-Location $OutputPath
            try {
                Get-ChildItem -Path $OutputPath -Exclude ".git" | Remove-Item -Recurse -Force
            } finally {
                Pop-Location
            }
        } elseif ($DataOnly) {
            # Data only - just clear the Data folder
            $dataPath = Join-Path $OutputPath "Data"
            if (Test-Path $dataPath) {
                Remove-Item $dataPath -Recurse -Force
            }
        }
        # CodeOnly doesn't need to clear anything - decompiler overwrites
    } elseif ($gitAvailable -and -not $NoGit) {
        # Git available but folder isn't a repo yet - initialize git to preserve history
        Write-Host "Converting existing folder to git repo for version tracking..." -ForegroundColor Yellow
        Push-Location $OutputPath
        try {
            git init | Out-Null
            git add -A | Out-Null
            git commit -m "Previous version (pre-existing)" | Out-Null
            $isGitRepo = $true
            $hasPreviousVersion = $true
            if (-not $isPartialRun) {
                # Full run - clear everything except .git
                Get-ChildItem -Path $OutputPath -Exclude ".git" | Remove-Item -Recurse -Force
            } elseif ($DataOnly) {
                # Data only - just clear the Data folder
                $dataPath = Join-Path $OutputPath "Data"
                if (Test-Path $dataPath) {
                    Remove-Item $dataPath -Recurse -Force
                }
            }
        } finally {
            Pop-Location
        }
    } elseif ($Force) {
        # No git, -Force specified - warn and overwrite
        Write-Host "WARNING: Overwriting without version control. Previous code will be LOST!" -ForegroundColor Red
        Write-Host "         Install git to preserve version history between updates." -ForegroundColor Yellow
        Start-Sleep -Seconds 2  # Give user a moment to cancel
        Remove-Item $OutputPath -Recurse -Force
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    } else {
        # No git, no -Force - create versioned folder instead
        $versionedPath = "${OutputPath}_${safeVersion}"
        if (Test-Path $versionedPath) {
            Write-Host "Output already exists for this version: $versionedPath" -ForegroundColor Yellow
            Write-Host "Use -Force to overwrite, or delete manually." -ForegroundColor Gray
            exit 0
        }
        Write-Host "Creating versioned folder (no git available)..." -ForegroundColor Yellow
        Write-Host "  Previous: $OutputPath" -ForegroundColor Gray
        Write-Host "  New:      $versionedPath" -ForegroundColor Gray
        Write-Host ""
        Write-Host "TIP: Install git to keep all versions in one folder with diff support" -ForegroundColor Cyan
        $OutputPath = $versionedPath
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }
} else {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Decompile each assembly
if (-not $DataOnly) {
    foreach ($assembly in $assemblies) {
        $dllPath = Join-Path $managedPath $assembly
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($assembly)
        $outputDir = Join-Path $OutputPath $assemblyName
        
        if (-not (Test-Path $dllPath)) {
            Write-Host "SKIP: $assembly not found" -ForegroundColor Yellow
            continue
        }
        
        Write-Host ""
        Write-Host "Decompiling $assembly..." -ForegroundColor Cyan
        Write-Host "  Source: $dllPath"
        Write-Host "  Output: $outputDir"
        
        $startTime = Get-Date
        
        # ILSpyCmd options:
        # -p / --project : Generate .csproj file
        # -o / --outputdir : Output directory
        # -lv / --languageversion : C# version (Latest)
        try {
            & ilspycmd $dllPath -p -o $outputDir -lv Latest 2>&1 | ForEach-Object {
                if ($_ -match "error|Error|ERROR") {
                    Write-Host "  $_" -ForegroundColor Red
                }
            }
            
            $elapsed = (Get-Date) - $startTime
            $fileCount = (Get-ChildItem $outputDir -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue).Count
            Write-Host "  Done! $fileCount .cs files in $($elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
        }
        catch {
            Write-Host "  ERROR: $_" -ForegroundColor Red
        }
    }
}

# Copy game data files
if (-not $CodeOnly) {
    Write-Host ""
    Write-Host "Copying game data files..." -ForegroundColor Cyan
    
    foreach ($dataFolder in $dataFolders) {
        $sourcePath = Join-Path $GamePath $dataFolder
        $destPath = Join-Path $OutputPath $dataFolder
        
        if (-not (Test-Path $sourcePath)) {
            Write-Host "SKIP: $dataFolder not found" -ForegroundColor Yellow
            continue
        }
        
        Write-Host ""
        Write-Host "Copying $dataFolder..." -ForegroundColor Cyan
        Write-Host "  Source: $sourcePath"
        Write-Host "  Output: $destPath"
        
        $startTime = Get-Date
        
        try {
            # Create destination directory
            New-Item -ItemType Directory -Path $destPath -Force | Out-Null
            
            # Copy all files recursively
            Copy-Item -Path "$sourcePath\*" -Destination $destPath -Recurse -Force
            
            $elapsed = (Get-Date) - $startTime
            $fileCount = (Get-ChildItem $destPath -Recurse -File -ErrorAction SilentlyContinue).Count
            $folderSize = (Get-ChildItem $destPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
            Write-Host "  Done! $fileCount files ($($folderSize.ToString('F1')) MB) in $($elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
        }
        catch {
            Write-Host "  ERROR: $_" -ForegroundColor Red
        }
    }
}

# Create a README
$readmePath = Join-Path $OutputPath "README.md"
$gitSection = if ($gitAvailable -and -not $NoGit) {
@"

## Git Versioning

This folder is its own git repository. Each extraction creates a commit with the game version.

**View version history:**
``````powershell
cd 7D2DCodebase
git log --oneline
``````

**See what changed between versions:**
``````powershell
git diff HEAD~1 --stat                      # Summary of ALL changed files
git diff HEAD~1 -- "*.cs"                   # Code changes only
git diff HEAD~1 -- "Data/Config/*.xml"      # XML changes only
git diff HEAD~1 -- Data/Config/items.xml    # Specific XML file
``````

**Compare specific versions:**
``````powershell
git log --oneline                           # Find commit hashes
git diff abc123 def456 -- SomeClass.cs      # Compare two versions
``````
"@
} else { "" }

@"
# 7 Days to Die Game Reference

This directory contains decompiled source code AND game data files for reference purposes.

**Generated:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Game Version:** $gameVersion
**Game Path:** $GamePath

## Contents

### Decompiled Code
- ``Assembly-CSharp/`` - Main game code (TileEntity, XUi, EntityPlayer, etc.)
- ``Assembly-CSharp-firstpass/`` - Additional game systems
- ``0Harmony/`` - Harmony patching library

### Game Data
- ``Data/Config/`` - XML configuration files
  - ``blocks.xml`` - Block definitions
  - ``items.xml`` - Item definitions
  - ``recipes.xml`` - Crafting recipes
  - ``buffs.xml`` - Buff/effect definitions
  - ``entityclasses.xml`` - Entity definitions
  - ``loot.xml`` - Loot tables
  - ``progression.xml`` - Skill/perk trees
  - ``quests.xml`` - Quest definitions
  - ``traders.xml`` - Trader inventories
  - ``XUi/`` - UI layout definitions
  - ``Localization.txt`` - All game text strings
  - ...and more
$gitSection

## Usage

Search this codebase using VS Code search (Ctrl+Shift+F) or grep to find:
- Class definitions and method signatures
- How game systems work
- XML property names and values
- Item/block internal names

## Key Classes for Modding

### Container/Storage
- ``TileEntityLootContainer`` - Basic loot containers
- ``TileEntitySecureLootContainer`` - Lockable containers
- ``TileEntityComposite`` - Modern composite containers
- ``TEFeatureStorage`` - Storage feature for composites
- ``ITileEntityLootable`` - Interface for lootable containers

### UI System
- ``XUi`` - Main UI controller
- ``XUiC_LootContainer`` - Loot container UI
- ``XUiC_ItemStackGrid`` - Item grid base class
- ``XUiWindowGroup`` - Window group management
- ``LocalPlayerUI`` - Player UI access

### Player/Inventory
- ``EntityPlayerLocal`` - Local player
- ``XUiM_PlayerInventory`` - Player inventory manager
- ``Bag`` - Inventory bag class

### Challenges
- ``ChallengeObjectiveGather`` - Gather objective
- ``ChallengeClass`` - Challenge definitions

## Key XML Files for Modding

- ``items.xml`` - Add/modify items, set properties like damage, durability
- ``blocks.xml`` - Block definitions, destruction effects, loot
- ``recipes.xml`` - Crafting recipes and requirements
- ``buffs.xml`` - Status effects, food buffs, injuries
- ``progression.xml`` - Skills, perks, and their effects
- ``loot.xml`` - What spawns in containers
- ``Localization.txt`` - All displayed text (for translations)

## Regenerating

Run the script after each game update to see what changed:
``````powershell
.\Decompile-7D2D.ps1
``````
$(if ($gitAvailable -and -not $NoGit) { "The script will automatically commit the new version and show a diff summary." } else { "" })

Options:
- ``-CodeOnly`` - Skip data files, only decompile code
- ``-DataOnly`` - Skip decompilation, only copy data files
"@ | Set-Content $readmePath

Write-Host ""
Write-Host "=== Extraction Complete ===" -ForegroundColor Cyan
Write-Host "Output: $OutputPath" -ForegroundColor Green
Write-Host ""

# Git versioning (if available)
if ($gitAvailable -and -not $NoGit) {
    if (-not $isGitRepo) {
        Write-Host "Initializing git repository for version tracking..." -ForegroundColor Yellow
        Push-Location $OutputPath
        try {
            git init | Out-Null
            git add -A | Out-Null
            git commit -m "Game version: $gameVersion" | Out-Null
            Write-Host "Created initial commit for $gameVersion" -ForegroundColor Green
        } finally {
            Pop-Location
        }
    } else {
        # Commit changes
        Push-Location $OutputPath
        try {
            git add -A | Out-Null
            
            # Check if there are changes to commit
            $hasChanges = git status --porcelain
            if ($hasChanges) {
                git commit -m "Game version: $gameVersion" | Out-Null
                Write-Host "Committed changes for $gameVersion" -ForegroundColor Green
                
                # Show diff summary if we had a previous version
                if ($hasPreviousVersion -and -not $NoDiff) {
                    Write-Host ""
                    Write-Host "=== Changes from previous version ===" -ForegroundColor Cyan
                    
                    # Get summary statistics
                    $diffStat = git diff --stat HEAD~1 HEAD 2>$null
                    if ($diffStat) {
                        # Show file change summary (limited)
                        $lines = $diffStat -split "`n"
                        $totalLine = $lines[-1]
                        $fileLines = $lines[0..([Math]::Min(19, $lines.Count - 2))]
                        
                        Write-Host ""
                        Write-Host "Changed files (top 20):" -ForegroundColor Yellow
                        $fileLines | ForEach-Object { Write-Host "  $_" }
                        if ($lines.Count -gt 22) {
                            Write-Host "  ... and more" -ForegroundColor Gray
                        }
                        Write-Host ""
                        Write-Host $totalLine -ForegroundColor Cyan
                    }
                    
                    Write-Host ""
                    Write-Host "To see full diff:" -ForegroundColor Yellow
                    Write-Host "  cd $OutputPath" -ForegroundColor Gray
                    Write-Host "  git diff HEAD~1 --stat" -ForegroundColor Gray
                    Write-Host "  git diff HEAD~1 -- SomeFile.cs" -ForegroundColor Gray
                }
            } else {
                Write-Host "No changes detected from previous version" -ForegroundColor Yellow
            }
        } finally {
            Pop-Location
        }
    }
} elseif (-not $NoGit -and -not $gitAvailable) {
    Write-Host "TIP: Install git to track changes between game versions" -ForegroundColor Gray
    Write-Host "     https://git-scm.com/download/win" -ForegroundColor Gray
}

Write-Host ""
Write-Host "You can now search the codebase in VS Code:" -ForegroundColor Yellow
Write-Host "  1. Open folder: $OutputPath"
Write-Host "  2. Use Ctrl+Shift+F to search all files"
Write-Host "  3. Search .cs files for code, .xml files for game data"
Write-Host ""

# Show summary
$csFiles = (Get-ChildItem $OutputPath -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue).Count
$xmlFiles = (Get-ChildItem $OutputPath -Filter "*.xml" -Recurse -ErrorAction SilentlyContinue).Count
$totalSize = (Get-ChildItem $OutputPath -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Total: $csFiles .cs files, $xmlFiles .xml files, $($totalSize.ToString('F1')) MB" -ForegroundColor Cyan
