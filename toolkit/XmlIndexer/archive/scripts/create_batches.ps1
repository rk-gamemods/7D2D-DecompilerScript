#!/usr/bin/env pwsh
# Create 20 game entity batches from sample_trace.jsonl

$sourceFile = "sample_trace.jsonl"
$batchMap = @{
    81=8001; 82=8101; 83=8201; 84=8301; 85=8401
    86=8501; 87=8601; 88=8701; 89=8801; 90=8901
    91=9001; 92=9101; 93=9201; 94=9301; 95=9401
    96=9501; 97=9601; 98=9701; 99=9801; 100=9901
}

Write-Host "Loading source file..."
$allLines = @(Get-Content $sourceFile)
Write-Host "Total lines in source: $($allLines.Count)"

$successCount = 0
foreach ($bn in 81..100) {
    $startLine = $batchMap[$bn]
    $endLine = $startLine + 99
    $paddedBn = "{0:D3}" -f $bn
    $outFile = "batch_$($paddedBn).jsonl"
    
    try {
        $extracted = @($allLines[($startLine-1)..($endLine-1)])
        $extracted | Out-File -FilePath $outFile -Encoding UTF8 -Force
        $successCount++
        Write-Host "[OK] batch_$($paddedBn).jsonl ($($extracted.Count) lines)"
    } catch {
        Write-Host "[ERROR] Failed to create batch_$($paddedBn).jsonl: $_"
    }
}

Write-Host "`nSummary: Created $successCount of 20 batches"
