# Clear-TempFiles.ps1
# Removes temp files from common locations to free up disk space.
# Run as Administrator for full coverage.

$locations = @(
    $env:TEMP,
    $env:TMP,
    "C:\Windows\Temp",
    "C:\Windows\Prefetch"
)

$totalFreed = 0

foreach ($path in $locations) {
    if (-not (Test-Path $path)) { continue }

    $before = (Get-ChildItem $path -Recurse -Force -ErrorAction SilentlyContinue |
               Measure-Object -Property Length -Sum).Sum

    Get-ChildItem $path -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    $after = (Get-ChildItem $path -Recurse -Force -ErrorAction SilentlyContinue |
              Measure-Object -Property Length -Sum).Sum

    $freed = [math]::Round((($before - $after) / 1MB), 2)
    $totalFreed += $freed
    Write-Host "  [ok] $path — freed $freed MB"
}

Write-Host ""
Write-Host "Total freed: $totalFreed MB" -ForegroundColor Green
