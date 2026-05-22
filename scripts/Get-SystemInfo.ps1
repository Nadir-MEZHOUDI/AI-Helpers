# Get-SystemInfo.ps1
# Prints a quick system summary: OS, CPU, RAM, disks, and uptime.

$os    = Get-CimInstance Win32_OperatingSystem
$cpu   = Get-CimInstance Win32_Processor | Select-Object -First 1
$disks = Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3"

$uptime = (Get-Date) - $os.LastBootUpTime
$ramTotal = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
$ramFree  = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
$ramUsed  = [math]::Round($ramTotal - $ramFree, 1)

Write-Host ""
Write-Host "=== System Info ===" -ForegroundColor Cyan
Write-Host "  OS      : $($os.Caption) $($os.OSArchitecture)"
Write-Host "  Version : $($os.Version)"
Write-Host "  Uptime  : $($uptime.Days)d $($uptime.Hours)h $($uptime.Minutes)m"
Write-Host ""
Write-Host "  CPU     : $($cpu.Name.Trim())"
Write-Host "  Cores   : $($cpu.NumberOfCores) cores / $($cpu.NumberOfLogicalProcessors) logical"
Write-Host ""
Write-Host "  RAM     : $ramUsed GB used / $ramTotal GB total"
Write-Host ""
Write-Host "  Disks:"
foreach ($disk in $disks) {
    $total = [math]::Round($disk.Size / 1GB, 1)
    $free  = [math]::Round($disk.FreeSpace / 1GB, 1)
    $used  = [math]::Round($total - $free, 1)
    $pct   = [math]::Round(($used / $total) * 100)
    Write-Host "    $($disk.DeviceID)  $used GB / $total GB used ($pct%)"
}
Write-Host ""
