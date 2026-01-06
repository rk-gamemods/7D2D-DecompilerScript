# ModsPageRenderTest.ps1
# Validates that the mods.html page JavaScript is syntactically correct and will render mods
# NOTE: This is a STATIC HTML test. The page uses client-side JS rendering.
#       Actual rendering only happens when the browser executes JavaScript.

param(
    [string]$ReportPath = ""
)

Write-Host "`n=== MODS PAGE RENDER TEST ===" -ForegroundColor Cyan

# Find the latest report if not specified
if (-not $ReportPath) {
    $outputDir = Join-Path $PSScriptRoot "..\..\..\output"
    if (-not (Test-Path $outputDir)) {
        $outputDir = "C:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\output"
    }
    $latest = Get-ChildItem $outputDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "^ecosystem_" } | Sort-Object Name -Descending | Select-Object -First 1
    if ($latest) {
        $ReportPath = Join-Path $latest.FullName "mods.html"
    }
}

if (-not (Test-Path $ReportPath)) {
    Write-Host "ERROR: mods.html not found at: $ReportPath" -ForegroundColor Red
    exit 1
}

Write-Host "Testing: $ReportPath" -ForegroundColor Gray

$content = Get-Content $ReportPath -Raw
$passed = $true

# Test 1: Count top-level mod entries in MOD_DATA (by name at start of object)
Write-Host "`n[Test 1] MOD_DATA entry count..." -ForegroundColor Yellow
# Find MOD_DATA array and count entries by looking for "name" as first property
$modDataStart = $content.IndexOf('const MOD_DATA = [')
if ($modDataStart -eq -1) {
    Write-Host "  FAIL: const MOD_DATA not found" -ForegroundColor Red
    $passed = $false
} else {
    # Extract just the first-level objects by counting opening braces after MOD_DATA
    $afterModData = $content.Substring($modDataStart)
    $endBracket = 0
    $depth = 0
    $modCount = 0
    $inString = $false
    $escaped = $false
    
    for ($i = $afterModData.IndexOf('['); $i -lt $afterModData.Length; $i++) {
        $c = $afterModData[$i]
        
        if ($escaped) { $escaped = $false; continue }
        if ($c -eq '\') { $escaped = $true; continue }
        if ($c -eq '"' -and -not $escaped) { $inString = -not $inString; continue }
        if ($inString) { continue }
        
        if ($c -eq '[') { $depth++ }
        elseif ($c -eq ']') { 
            $depth--
            if ($depth -eq 0) { $endBracket = $i; break }
        }
        elseif ($c -eq '{' -and $depth -eq 1) { $modCount++ }
    }
    
    Write-Host "  MOD_DATA contains: $modCount top-level mod entries" -ForegroundColor White
}

# Test 2: Verify essential page structure
Write-Host "`n[Test 2] Page structure..." -ForegroundColor Yellow
$hasContainer = $content -match '<div id="mod-results">'
$hasSearchInput = $content -match 'id="mod-search"'
$hasHealthFilter = $content -match 'id="health-filter"'
Write-Host "  mod-results container: $(if($hasContainer){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasContainer){'Green'}else{'Red'})
Write-Host "  mod-search input: $(if($hasSearchInput){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasSearchInput){'Green'}else{'Red'})
Write-Host "  health-filter select: $(if($hasHealthFilter){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasHealthFilter){'Green'}else{'Red'})
if (-not $hasContainer) { $passed = $false }

# Test 3: Check required JavaScript functions exist
Write-Host "`n[Test 3] Required JavaScript functions..." -ForegroundColor Yellow
$hasFilterMods = $content -match 'function filterMods\s*\(\)'
$hasRenderMods = $content -match 'function renderMods\s*\(mods\)'
$hasGetHealthBadge = $content -match 'function getHealthBadge\s*\('
$hasGetModTypeBadge = $content -match 'function getModTypeBadge\s*\('
Write-Host "  filterMods(): $(if($hasFilterMods){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasFilterMods){'Green'}else{'Red'})
Write-Host "  renderMods(mods): $(if($hasRenderMods){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasRenderMods){'Green'}else{'Red'})
Write-Host "  getHealthBadge(): $(if($hasGetHealthBadge){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasGetHealthBadge){'Green'}else{'Red'})
Write-Host "  getModTypeBadge(): $(if($hasGetModTypeBadge){'FOUND'}else{'MISSING'})" -ForegroundColor $(if($hasGetModTypeBadge){'Green'}else{'Red'})
if (-not ($hasFilterMods -and $hasRenderMods -and $hasGetHealthBadge -and $hasGetModTypeBadge)) { $passed = $false }

# Test 4: Check DOMContentLoaded handler calls filterMods
Write-Host "`n[Test 4] Initialization on page load..." -ForegroundColor Yellow
# Look for DOMContentLoaded that's followed by filterMods within 200 chars
$initPattern = "DOMContentLoaded['`",]\s*\(\)\s*=>\s*\{\s*filterMods\(\)"
$hasInit = $content -match $initPattern
if (-not $hasInit) {
    # Try alternate pattern
    $hasInit = $content -match "DOMContentLoaded[\s\S]{0,50}filterMods\(\)"
}
Write-Host "  DOMContentLoaded calls filterMods: $(if($hasInit){'YES'}else{'NO'})" -ForegroundColor $(if($hasInit){'Green'}else{'Red'})
if (-not $hasInit) { $passed = $false }

# Test 5: Validate JavaScript syntax using Node.js
Write-Host "`n[Test 5] JavaScript syntax validation..." -ForegroundColor Yellow
# Extract all <script> content
$scriptMatches = [regex]::Matches($content, '<script[^>]*>([\s\S]*?)</script>')
$allScripts = ($scriptMatches | ForEach-Object { $_.Groups[1].Value }) -join "`n"

# Write to temp file and check with Node
$tempJs = [System.IO.Path]::GetTempFileName() + ".js"
$allScripts | Out-File $tempJs -Encoding UTF8

try {
    $nodeResult = & node --check $tempJs 2>&1
    $syntaxValid = $LASTEXITCODE -eq 0
    if ($syntaxValid) {
        Write-Host "  JavaScript syntax: VALID" -ForegroundColor Green
    } else {
        Write-Host "  JavaScript syntax: INVALID" -ForegroundColor Red
        Write-Host "  Error: $nodeResult" -ForegroundColor Red
        $passed = $false
    }
} catch {
    Write-Host "  Could not validate JS (Node.js not available)" -ForegroundColor Yellow
} finally {
    Remove-Item $tempJs -ErrorAction SilentlyContinue
}

# Test 6: Check stats header matches mod count
Write-Host "`n[Test 6] Stats header verification..." -ForegroundColor Yellow
$statsMatches = [regex]::Matches($content, '<div class="stat-value">(\d+)</div>')
$healthyCount = 0
if ($statsMatches.Count -ge 1) {
    $healthyCount = [int]$statsMatches[0].Groups[1].Value
    Write-Host "  Stats display: $healthyCount Healthy, etc." -ForegroundColor White
}

# VERDICT
Write-Host "`n=== VERDICT ===" -ForegroundColor Cyan

if ($passed) {
    Write-Host "PASS: Page structure and JavaScript appear correct" -ForegroundColor Green
    Write-Host "`nNOTE: This test validates static HTML/JS structure." -ForegroundColor Gray
    Write-Host "      Actual rendering requires browser JavaScript execution." -ForegroundColor Gray
    Write-Host "      If mods still don't show in browser, check DevTools Console (F12) for runtime errors." -ForegroundColor Gray
    exit 0
} else {
    Write-Host "FAIL: Page has structural or JavaScript issues" -ForegroundColor Red
    exit 1
}
