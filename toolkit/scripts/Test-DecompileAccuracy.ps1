# Test-DecompileAccuracy.ps1
# Validates the impact of providing reference assemblies to ILSpy decompiler
# Compares decompilation quality with and without -r flag
#
# USAGE:
#   .\Test-DecompileAccuracy.ps1 -GamePath "D:\Games\7D2D" -ModsPath "D:\Games\7D2D\Mods"
#   .\Test-DecompileAccuracy.ps1  # Uses defaults from parent workspace

param(
    [string]$GamePath,
    [string]$ModsPath,
    [string]$OutputPath = "..\..\..\..\temp_analysis\decompile_comparison"
)

$ErrorActionPreference = "Stop"

# Auto-detect paths if not specified
if (-not $GamePath) {
    $commonPaths = @(
        "C:\Steam\steamapps\common\7 Days To Die",
        "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die",
        "D:\Steam\steamapps\common\7 Days To Die",
        "D:\SteamLibrary\steamapps\common\7 Days To Die"
    )
    foreach ($path in $commonPaths) {
        if (Test-Path (Join-Path $path "7DaysToDie_Data\Managed\Assembly-CSharp.dll")) {
            $GamePath = $path
            break
        }
    }
    if (-not $GamePath) {
        Write-Host "ERROR: Could not auto-detect game path. Use -GamePath parameter." -ForegroundColor Red
        exit 1
    }
}

if (-not $ModsPath) {
    $ModsPath = Join-Path $GamePath "Mods"
}

$ManagedPath = Join-Path $GamePath "7DaysToDie_Data\Managed"

# Validate paths
if (-not (Test-Path $ManagedPath)) {
    Write-Host "ERROR: Managed folder not found: $ManagedPath" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $ModsPath)) {
    Write-Host "ERROR: Mods folder not found: $ModsPath" -ForegroundColor Red
    exit 1
}

# Check ilspycmd
$ilspyCmd = Get-Command "ilspycmd" -ErrorAction SilentlyContinue
if (-not $ilspyCmd) {
    Write-Host "ERROR: ilspycmd not found. Install with: dotnet tool install -g ilspycmd" -ForegroundColor Red
    exit 1
}

# Resolve output path to absolute
$OutputPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputPath))

Write-Host "=== ILSpy Reference Path Validation ===" -ForegroundColor Cyan
Write-Host "Game Path:    $GamePath"
Write-Host "Mods Path:    $ModsPath"
Write-Host "Managed Path: $ManagedPath"
Write-Host "Output Path:  $OutputPath"
Write-Host ""

# Find all C# mods (folders containing .dll files)
$csharpMods = @()
Get-ChildItem $ModsPath -Directory | ForEach-Object {
    $modPath = $_.FullName
    $dllFiles = Get-ChildItem $modPath -Filter "*.dll" -Recurse -ErrorAction SilentlyContinue | 
                Where-Object { $_.Name -notmatch "^(0Harmony|Mono\.|System\.|Unity)" }
    if ($dllFiles) {
        $csharpMods += @{
            Name = $_.Name
            Path = $modPath
            DLLs = $dllFiles
        }
    }
}

if ($csharpMods.Count -eq 0) {
    Write-Host "ERROR: No C# mods found in $ModsPath" -ForegroundColor Red
    exit 1
}

Write-Host "Found $($csharpMods.Count) C# mods:" -ForegroundColor Green
$csharpMods | ForEach-Object { Write-Host "  - $($_.Name) ($($_.DLLs.Count) DLL(s))" }
Write-Host ""

# Create output directory structure
if (Test-Path $OutputPath) {
    Write-Host "Cleaning previous output..." -ForegroundColor Yellow
    Remove-Item $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

# Initialize git repo for diff tracking
Push-Location $OutputPath
git init | Out-Null
Pop-Location

# Results storage
$results = @()

# Process each mod
foreach ($mod in $csharpMods) {
    Write-Host "Processing: $($mod.Name)" -ForegroundColor Cyan
    
    $modOutputDir = Join-Path $OutputPath $mod.Name
    New-Item -ItemType Directory -Path $modOutputDir -Force | Out-Null
    
    foreach ($dll in $mod.DLLs) {
        $dllName = [System.IO.Path]::GetFileNameWithoutExtension($dll.Name)
        $decompileDir = Join-Path $modOutputDir $dllName
        
        Write-Host "  Decompiling: $($dll.Name)" -ForegroundColor Gray
        
        # === PASS A: Without reference path (baseline) ===
        $startTime = Get-Date
        & ilspycmd $dll.FullName -p -o $decompileDir -lv Latest 2>&1 | Out-Null
        $baselineTime = ((Get-Date) - $startTime).TotalSeconds
        
        # Count warnings in baseline
        $baselineWarnings = @{
            IL_Comments = 0
            UnknownType = 0
            ExpectedO = 0
            TotalLines = 0
            Files = 0
        }
        
        Get-ChildItem $decompileDir -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
            if ($content) {
                $baselineWarnings.Files++
                $baselineWarnings.TotalLines += ($content -split "`n").Count
                $baselineWarnings.IL_Comments += ([regex]::Matches($content, "//IL_[0-9a-fA-F]+:")).Count
                $baselineWarnings.UnknownType += ([regex]::Matches($content, "Unknown result type")).Count
                $baselineWarnings.ExpectedO += ([regex]::Matches($content, "Expected O, but got")).Count
            }
        }
        
        # Commit baseline
        Push-Location $OutputPath
        git add -A | Out-Null
        git commit -m "baseline: $($mod.Name)/$dllName (no references)" --allow-empty | Out-Null
        Pop-Location
        
        # === PASS B: With reference path ===
        Remove-Item $decompileDir -Recurse -Force -ErrorAction SilentlyContinue
        
        $startTime = Get-Date
        & ilspycmd $dll.FullName -p -o $decompileDir -lv Latest -r $ManagedPath 2>&1 | Out-Null
        $withRefsTime = ((Get-Date) - $startTime).TotalSeconds
        
        # Count warnings with references
        $withRefsWarnings = @{
            IL_Comments = 0
            UnknownType = 0
            ExpectedO = 0
            TotalLines = 0
            Files = 0
        }
        
        Get-ChildItem $decompileDir -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
            if ($content) {
                $withRefsWarnings.Files++
                $withRefsWarnings.TotalLines += ($content -split "`n").Count
                $withRefsWarnings.IL_Comments += ([regex]::Matches($content, "//IL_[0-9a-fA-F]+:")).Count
                $withRefsWarnings.UnknownType += ([regex]::Matches($content, "Unknown result type")).Count
                $withRefsWarnings.ExpectedO += ([regex]::Matches($content, "Expected O, but got")).Count
            }
        }
        
        # Commit with references
        Push-Location $OutputPath
        git add -A | Out-Null
        git commit -m "with-refs: $($mod.Name)/$dllName" --allow-empty | Out-Null
        Pop-Location
        
        # Calculate improvement
        $ilReduction = if ($baselineWarnings.IL_Comments -gt 0) {
            [math]::Round((1 - $withRefsWarnings.IL_Comments / $baselineWarnings.IL_Comments) * 100, 1)
        } else { 0 }
        
        $unknownReduction = if ($baselineWarnings.UnknownType -gt 0) {
            [math]::Round((1 - $withRefsWarnings.UnknownType / $baselineWarnings.UnknownType) * 100, 1)
        } else { 0 }
        
        # Store result
        $results += [PSCustomObject]@{
            Mod = $mod.Name
            DLL = $dllName
            Files = $baselineWarnings.Files
            Lines = $baselineWarnings.TotalLines
            IL_Before = $baselineWarnings.IL_Comments
            IL_After = $withRefsWarnings.IL_Comments
            IL_Reduction = "$ilReduction%"
            Unknown_Before = $baselineWarnings.UnknownType
            Unknown_After = $withRefsWarnings.UnknownType
            Unknown_Reduction = "$unknownReduction%"
            Time_Before = "{0:F2}s" -f $baselineTime
            Time_After = "{0:F2}s" -f $withRefsTime
        }
        
        # Show inline result
        $color = if ($ilReduction -gt 0 -or $unknownReduction -gt 0) { "Green" } else { "Gray" }
        Write-Host "    IL warnings: $($baselineWarnings.IL_Comments) -> $($withRefsWarnings.IL_Comments) ($ilReduction% reduction)" -ForegroundColor $color
        Write-Host "    Unknown type: $($baselineWarnings.UnknownType) -> $($withRefsWarnings.UnknownType) ($unknownReduction% reduction)" -ForegroundColor $color
    }
}

Write-Host ""
Write-Host "=== RESULTS SUMMARY ===" -ForegroundColor Cyan
Write-Host ""

# Display results table
$results | Format-Table -AutoSize

# Calculate totals
$totalIL_Before = ($results | Measure-Object -Property IL_Before -Sum).Sum
$totalIL_After = ($results | Measure-Object -Property IL_After -Sum).Sum
$totalUnknown_Before = ($results | Measure-Object -Property Unknown_Before -Sum).Sum
$totalUnknown_After = ($results | Measure-Object -Property Unknown_After -Sum).Sum

$totalIL_Reduction = if ($totalIL_Before -gt 0) { [math]::Round((1 - $totalIL_After / $totalIL_Before) * 100, 1) } else { 0 }
$totalUnknown_Reduction = if ($totalUnknown_Before -gt 0) { [math]::Round((1 - $totalUnknown_After / $totalUnknown_Before) * 100, 1) } else { 0 }

Write-Host "=== AGGREGATE ===" -ForegroundColor Yellow
Write-Host "Total IL warnings:      $totalIL_Before -> $totalIL_After ($totalIL_Reduction% reduction)"
Write-Host "Total Unknown type:     $totalUnknown_Before -> $totalUnknown_After ($totalUnknown_Reduction% reduction)"
Write-Host ""

# Save results to file
$resultsFile = Join-Path $OutputPath "comparison_results.txt"
@"
ILSpy Reference Path Validation Results
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

Game Path:    $GamePath
Mods Path:    $ModsPath
Managed Path: $ManagedPath

=== PER-MOD RESULTS ===
$($results | Format-Table -AutoSize | Out-String)

=== AGGREGATE ===
Total IL warnings:      $totalIL_Before -> $totalIL_After ($totalIL_Reduction% reduction)
Total Unknown type:     $totalUnknown_Before -> $totalUnknown_After ($totalUnknown_Reduction% reduction)

=== GIT HISTORY ===
Use 'git log --oneline' in the output folder to see commits.
Use 'git diff <commit1> <commit2>' to compare specific versions.

Output folder: $OutputPath
"@ | Set-Content $resultsFile

Write-Host "Results saved to: $resultsFile" -ForegroundColor Green
Write-Host ""
Write-Host "To explore diffs:" -ForegroundColor Yellow
Write-Host "  cd `"$OutputPath`""
Write-Host "  git log --oneline"
Write-Host "  git diff HEAD~1 --stat"
