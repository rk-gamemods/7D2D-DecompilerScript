# Decompile-7D2D.ps1
# Decompiles 7 Days to Die game assemblies for code reference
# Uses git versioning to track changes between game updates
#
# USAGE:
#   .\Decompile-7D2D.ps1                              # Uses defaults, auto-detects game
#   .\Decompile-7D2D.ps1 -GamePath "D:\Games\7D2D"    # Custom game path
#   .\Decompile-7D2D.ps1 -OutputPath ".\GameCode"    # Custom output path
#   .\Decompile-7D2D.ps1 -NoGit                       # Skip git versioning
#   .\Decompile-7D2D.ps1 -Force                       # Force overwrite (destructive without git!)
#
# REQUIREMENTS:
#   - .NET SDK (for ilspycmd) - https://dotnet.microsoft.com/download
#   - Git (optional but recommended) - https://git-scm.com/download/win
#
# VERSION TRACKING:
#   WITH GIT (recommended):
#     - All code in one folder, git tracks history
#     - Run script after each game update
#     - Use `git diff HEAD~1` to see changes
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
    [switch]$NoDiff         # Skip showing diff summary after commit
)

$ErrorActionPreference = "Stop"

# Key assemblies to decompile
$assemblies = @(
    "Assembly-CSharp.dll",           # Main game code
    "Assembly-CSharp-firstpass.dll", # Additional game code
    "0Harmony.dll"                   # Harmony library (for reference)
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

Write-Host "=== 7 Days to Die Code Decompiler ===" -ForegroundColor Cyan
Write-Host "Game Path: $GamePath"
Write-Host "Output Path: $OutputPath"
Write-Host ""

# Try to get game version from version.txt or assembly
function Get-GameVersion {
    param([string]$GamePath)
    
    # Try version.txt first
    $versionFile = Join-Path $GamePath "version.txt"
    if (Test-Path $versionFile) {
        $version = (Get-Content $versionFile -Raw).Trim()
        if ($version) { return $version }
    }
    
    # Try to get from Assembly-CSharp.dll file version
    $assemblyPath = Join-Path $GamePath "7DaysToDie_Data\Managed\Assembly-CSharp.dll"
    if (Test-Path $assemblyPath) {
        $fileInfo = (Get-Item $assemblyPath).VersionInfo
        # Try ProductVersion first (often more descriptive), then FileVersion
        if ($fileInfo.ProductVersion) { return "v$($fileInfo.ProductVersion)" }
        if ($fileInfo.FileVersion) { return "v$($fileInfo.FileVersion)" }
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

# Check if ILSpyCmd is installed
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
if (Test-Path $OutputPath) {
    if ($isGitRepo) {
        # Git repo exists - safe to update in place (history preserved)
        Write-Host "Git repo exists - will update and commit changes..." -ForegroundColor Yellow
        Push-Location $OutputPath
        try {
            # Remove everything except .git
            Get-ChildItem -Path $OutputPath -Exclude ".git" | Remove-Item -Recurse -Force
        } finally {
            Pop-Location
        }
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
            # Now remove content (git has it saved)
            Get-ChildItem -Path $OutputPath -Exclude ".git" | Remove-Item -Recurse -Force
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

# Create a README
$readmePath = Join-Path $OutputPath "README.md"
$gitSection = if ($gitAvailable -and -not $NoGit) {
@"

## Git Versioning

This folder is its own git repository. Each decompile creates a commit with the game version.

**View version history:**
``````powershell
cd 7D2DCodebase
git log --oneline
``````

**See what changed between versions:**
``````powershell
git diff HEAD~1 --stat                    # Summary of changed files
git diff HEAD~1 -- XUiM_PlayerInventory.cs  # Specific file diff
git diff HEAD~1 -- "*.cs" | head -200     # First 200 lines of all changes
``````

**Compare specific versions:**
``````powershell
git log --oneline                         # Find commit hashes
git diff abc123 def456 -- SomeClass.cs    # Compare two versions
``````
"@
} else { "" }

@"
# 7 Days to Die Decompiled Source

This directory contains decompiled source code from the game for reference purposes.

**Generated:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Game Version:** $gameVersion
**Game Path:** $GamePath

## Contents

- ``Assembly-CSharp/`` - Main game code (TileEntity, XUi, EntityPlayer, etc.)
- ``Assembly-CSharp-firstpass/`` - Additional game systems
- ``0Harmony/`` - Harmony patching library
$gitSection

## Usage

Search this codebase using VS Code search (Ctrl+Shift+F) or grep to find:
- Class definitions
- Method signatures
- How game systems work

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

## Regenerating

Run the decompile script to update after a game update:
``````powershell
.\Decompile-7D2D.ps1
``````
$(if ($gitAvailable -and -not $NoGit) { "The script will automatically commit the new version and show a diff summary." } else { "" })
"@ | Set-Content $readmePath

Write-Host ""
Write-Host "=== Decompilation Complete ===" -ForegroundColor Cyan
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
Write-Host "  3. Or use grep_search with includePattern"
Write-Host ""

# Show summary
$totalFiles = (Get-ChildItem $OutputPath -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue).Count
$totalSize = (Get-ChildItem $OutputPath -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Total: $totalFiles .cs files, $($totalSize.ToString('F1')) MB" -ForegroundColor Cyan
