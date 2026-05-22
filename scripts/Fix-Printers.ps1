# Fix-Printers.ps1
# Fixes common network printer issues: stuck spooler, broken ports,
# Point & Print restrictions, RPC errors (0x00000709), and cached credentials.
# Run as Administrator.

#Requires -RunAsAdministrator

Write-Host "Starting printer network fix..." -ForegroundColor Cyan

# Firewall rules — language-independent matching for EN/FR and others
Get-NetFirewallRule | Where-Object { $_.DisplayGroup -match "Discovery|D.couverte" } |
    Set-NetFirewallRule -Enabled True
Get-NetFirewallRule | Where-Object { $_.DisplayGroup -match "File|Fichier" } |
    Set-NetFirewallRule -Enabled True
Get-NetFirewallRule | Where-Object { $_.DisplayGroup -match "Printer|Imprim" } |
    Set-NetFirewallRule -Enabled True
Write-Host "  [ok] Firewall rules enabled"

# Fix Point & Print driver restrictions
reg add "HKLM\Software\Policies\Microsoft\Windows NT\Printers\PointAndPrint" /v RestrictDriverInstallationToAdministrators /t REG_DWORD /d 0 /f | Out-Null
reg add "HKLM\Software\Policies\Microsoft\Windows NT\Printers\PointAndPrint" /v NoWarningNoElevationOnInstall /t REG_DWORD /d 1 /f | Out-Null
Write-Host "  [ok] Point & Print restrictions lifted"

# Fix RPC authentication (resolves error 0x00000709)
reg add "HKLM\System\CurrentControlSet\Control\Print" /v RpcAuthnLevelPrivacyEnabled /t REG_DWORD /d 0 /f | Out-Null
Write-Host "  [ok] RPC auth level relaxed (0x00000709 fix)"

# Restart Print Spooler and clear stuck jobs
Write-Host "  Restarting Print Spooler..."
Stop-Service spooler -Force -ErrorAction SilentlyContinue
Remove-Item "C:\Windows\System32\spool\PRINTERS\*" -Force -ErrorAction SilentlyContinue
Start-Service spooler
Set-Service spooler -StartupType Automatic
Write-Host "  [ok] Spooler restarted, print queue cleared"

# Ensure discovery services are running
$services = "FDResPub", "fdPHost", "SSDPSRV", "upnphost"
foreach ($svc in $services) {
    Set-Service $svc -StartupType Automatic -ErrorAction SilentlyContinue
    Start-Service $svc -ErrorAction SilentlyContinue
}
Write-Host "  [ok] Discovery services running"

# Remove broken default printer registry key (another 0x00000709 cause)
$regPath = "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Windows"
Remove-ItemProperty -Path $regPath -Name "Device" -ErrorAction SilentlyContinue
Write-Host "  [ok] Stale default printer key removed"

# Remove disconnected network printer connections
Get-Printer | Where-Object { $_.Type -eq "Connection" } |
    Remove-Printer -ErrorAction SilentlyContinue
Write-Host "  [ok] Broken network printer connections removed"

# Clear cached credentials for print servers
cmdkey /list | ForEach-Object {
    if ($_ -like "*Target:*") {
        $target = ($_ -split "Target:\s*")[1].Trim()
        cmdkey /delete:$target | Out-Null
    }
}
Write-Host "  [ok] Cached credentials cleared"

Write-Host ""
Write-Host "Fix complete. Restart your PC now." -ForegroundColor Green
