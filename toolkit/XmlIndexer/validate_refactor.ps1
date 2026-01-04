# validate_refactor.ps1
# Compares two ecosystem.db files to detect regressions during refactoring
#
# Usage: .\validate_refactor.ps1 -BaselineDb "baseline.db" -NewDb "new.db"
#        .\validate_refactor.ps1 -BaselineDb "baseline_output/ecosystem.db" -NewDb "test_output/ecosystem.db"

param(
    [Parameter(Mandatory=$true)]
    [string]$BaselineDb,
    
    [Parameter(Mandatory=$true)]
    [string]$NewDb
)

# Verify files exist
if (-not (Test-Path $BaselineDb)) {
    Write-Host "❌ ERROR: Baseline database not found: $BaselineDb" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $NewDb)) {
    Write-Host "❌ ERROR: New database not found: $NewDb" -ForegroundColor Red
    exit 1
}

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "              REFACTOR VALIDATION REPORT" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Baseline: $BaselineDb" -ForegroundColor Gray
Write-Host "New:      $NewDb" -ForegroundColor Gray
Write-Host ""

$tests = @(
    @{ Name = "harmony_patches"; Query = "SELECT COUNT(*) FROM harmony_patches"; Critical = $true },
    @{ Name = "mod_xml_operations"; Query = "SELECT COUNT(*) FROM mod_xml_operations"; Critical = $true },
    @{ Name = "mod_csharp_deps"; Query = "SELECT COUNT(*) FROM mod_csharp_deps"; Critical = $true },
    @{ Name = "harmony_conflicts"; Query = "SELECT COUNT(*) FROM harmony_conflicts"; Critical = $false },
    @{ Name = "game_code_analysis"; Query = "SELECT COUNT(*) FROM game_code_analysis"; Critical = $false },
    @{ Name = "mods"; Query = "SELECT COUNT(*) FROM mods"; Critical = $true },
    @{ Name = "xml_definitions"; Query = "SELECT COUNT(*) FROM xml_definitions"; Critical = $true }
)

$allPassed = $true
$criticalFailed = $false

Write-Host "TEST RESULTS:" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────────────────────"

foreach ($test in $tests) {
    try {
        $baseline = [int](sqlite3 $BaselineDb $test.Query 2>$null)
        $new = [int](sqlite3 $NewDb $test.Query 2>$null)
    }
    catch {
        Write-Host "⚠️  SKIP: $($test.Name) - Query failed (table may not exist)" -ForegroundColor Yellow
        continue
    }
    
    $diff = [math]::Abs($new - $baseline)
    $pct = if ($baseline -gt 0) { [math]::Round(($diff / $baseline) * 100, 1) } else { 0 }
    
    $threshold = if ($test.Critical) { 5 } else { 10 }  # Critical tests have tighter tolerance
    
    if ($pct -gt $threshold) {
        $marker = if ($test.Critical) { "❌ FAIL" } else { "⚠️  WARN" }
        Write-Host "$marker`: $($test.Name.PadRight(25)) Baseline: $($baseline.ToString().PadLeft(6)), New: $($new.ToString().PadLeft(6)) ($pct% diff)" -ForegroundColor $(if ($test.Critical) { "Red" } else { "Yellow" })
        $allPassed = $false
        if ($test.Critical) { $criticalFailed = $true }
    } else {
        Write-Host "✅ PASS: $($test.Name.PadRight(25)) Baseline: $($baseline.ToString().PadLeft(6)), New: $($new.ToString().PadLeft(6)) ($pct% diff)" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

if ($criticalFailed) {
    Write-Host ""
    Write-Host "⛔ CRITICAL REGRESSION DETECTED - DO NOT COMMIT" -ForegroundColor Red
    Write-Host ""
    Write-Host "One or more critical metrics have regressed beyond the 5% threshold." -ForegroundColor Red
    Write-Host "Review your changes and fix the regression before committing." -ForegroundColor Red
    Write-Host ""
    Write-Host "To rollback:" -ForegroundColor Yellow
    Write-Host "  git checkout -- ." -ForegroundColor Gray
    Write-Host "  # OR" -ForegroundColor Gray
    Write-Host "  git stash" -ForegroundColor Gray
    exit 1
}
elseif (-not $allPassed) {
    Write-Host ""
    Write-Host "⚠️  WARNINGS DETECTED - Review before committing" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Non-critical metrics have changed. This may be expected if you're" -ForegroundColor Yellow
    Write-Host "intentionally modifying detection logic. Verify the changes are correct." -ForegroundColor Yellow
    exit 0
}
else {
    Write-Host ""
    Write-Host "✅ ALL TESTS PASSED - Safe to commit" -ForegroundColor Green
    Write-Host ""
    Write-Host "Suggested commit:" -ForegroundColor Cyan
    Write-Host "  git add -A" -ForegroundColor Gray
    Write-Host '  git commit -m "refactor(phase-N): <description>"' -ForegroundColor Gray
    exit 0
}
