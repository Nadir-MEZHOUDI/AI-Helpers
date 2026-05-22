# Fix-WiFi.ps1
# Resets the network stack, disables WiFi/USB power saving, and restarts
# wireless adapters to fix unstable or dropping WiFi connections.
# Auto-elevates to Administrator if needed.

$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal   = New-Object Security.Principal.WindowsPrincipal($currentUser)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    $psExe = (Get-Process -Id $PID).Path
    Start-Process $psExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Write-Host "Running as Administrator" -ForegroundColor Green

# Reset network stack
Write-Host "Resetting network stack..." -ForegroundColor Cyan
netsh winsock reset | Out-Null
netsh int ip reset   | Out-Null
ipconfig /flushdns   | Out-Null
Write-Host "  [ok] Winsock, IP stack, and DNS cache reset"

# Disable USB selective suspend (prevents adapter from sleeping mid-connection)
Write-Host "Disabling USB selective suspend..." -ForegroundColor Cyan
$SUB_USB             = "2a737441-1930-4402-8d77-b2bebba308a3"
$USB_SELECTIVE_SUSPEND = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"
powercfg /SETACVALUEINDEX SCHEME_CURRENT $SUB_USB $USB_SELECTIVE_SUSPEND 0
powercfg /SETDCVALUEINDEX SCHEME_CURRENT $SUB_USB $USB_SELECTIVE_SUSPEND 0
Write-Host "  [ok] USB selective suspend disabled"

# Set wireless adapter to Maximum Performance power mode
Write-Host "Setting wireless adapter to maximum performance..." -ForegroundColor Cyan
$SUB_WIFI        = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1"
$WIFI_POWER_SAVE = "12bbebe6-58d6-4636-95bb-3217ef867c1a"
powercfg /SETACVALUEINDEX SCHEME_CURRENT $SUB_WIFI $WIFI_POWER_SAVE 0
powercfg /SETDCVALUEINDEX SCHEME_CURRENT $SUB_WIFI $WIFI_POWER_SAVE 0
powercfg /SETACTIVE SCHEME_CURRENT
Write-Host "  [ok] WiFi power saving disabled"

# Restart all wireless adapters
Write-Host "Restarting WiFi adapters..." -ForegroundColor Cyan
$wifiAdapters = Get-NetAdapter | Where-Object {
    $_.InterfaceDescription -match "Wi-Fi|Wireless|802\.11|WLAN|Realtek|TP-Link|USB" -or
    $_.Name -match "Wi-Fi|Wireless|WLAN"
}

if ($wifiAdapters) {
    foreach ($adapter in $wifiAdapters) {
        Write-Host "  Restarting: $($adapter.Name)"
        Restart-NetAdapter -Name $adapter.Name -Confirm:$false
        Start-Sleep -Seconds 2
    }
} else {
    Write-Host "  No WiFi adapter detected automatically." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Restart Windows to complete the netsh reset." -ForegroundColor Yellow
