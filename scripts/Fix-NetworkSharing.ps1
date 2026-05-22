# Fix-NetworkSharing.ps1
# Enables Windows file/printer sharing and creates a local sharing user.
# Language-safe: uses SIDs instead of localized group names.
# Run as Administrator.

#Requires -RunAsAdministrator

param(
    [string]$Username = "Admin"
)

$Password = Read-Host "Password for user '$Username'" -AsSecureString

Write-Host "Configuring network sharing..." -ForegroundColor Cyan

# Set network profile to Private (required for sharing)
Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private
Write-Host "  [ok] Network profile set to Private"

# Enable discovery and file sharing firewall rules (language-independent matching)
Get-NetFirewallRule | Where-Object { $_.Group -like "*Network*Discovery*" } |
    Set-NetFirewallRule -Enabled True
Get-NetFirewallRule | Where-Object { $_.Group -like "*File*Printer*" } |
    Set-NetFirewallRule -Enabled True
Write-Host "  [ok] Firewall rules enabled"

# Start and auto-start required services
$services = "FDResPub", "fdPHost", "SSDPSRV", "upnphost"
foreach ($svc in $services) {
    Set-Service $svc -StartupType Automatic -ErrorAction SilentlyContinue
    Start-Service $svc -ErrorAction SilentlyContinue
}
Write-Host "  [ok] Discovery services started"

# Enable NetBIOS over TCP/IP
Get-CimInstance Win32_NetworkAdapterConfiguration -Filter "IPEnabled=TRUE" |
    ForEach-Object { $_.SetTcpipNetbios(1) | Out-Null }
Write-Host "  [ok] NetBIOS enabled"

# SMB: disable v1, enable v2
Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force
Set-SmbServerConfiguration -EnableSMB2Protocol $true  -Force
Write-Host "  [ok] SMB2 enabled, SMB1 disabled"

# Allow guest/anonymous access (needed for cross-machine sharing without credentials)
reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v everyoneincludesanonymous /t REG_DWORD /d 1 /f | Out-Null
reg add "HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters" /v AllowInsecureGuestAuth /t REG_DWORD /d 1 /f | Out-Null
Write-Host "  [ok] Guest access allowed"

# Create sharing user if it doesn't exist
if (-not (Get-LocalUser -Name $Username -ErrorAction SilentlyContinue)) {
    New-LocalUser -Name $Username -Password $Password -FullName "Sharing User" -PasswordNeverExpires $true
    Write-Host "  [ok] User '$Username' created"
} else {
    Write-Host "  [skip] User '$Username' already exists"
}

# Add to built-in Users group (SID S-1-5-32-545 — language-independent)
$group = Get-LocalGroup | Where-Object { $_.SID -eq "S-1-5-32-545" }
if ($group) {
    Add-LocalGroupMember -Group $group.Name -Member $Username -ErrorAction SilentlyContinue
    Write-Host "  [ok] Added '$Username' to '$($group.Name)'"
}

# Clear cached credentials so stale auth doesn't block reconnection
cmdkey /list | ForEach-Object {
    if ($_ -like "*Target:*") {
        $target = ($_ -split "Target:\s*")[1].Trim()
        cmdkey /delete:$target | Out-Null
    }
}
Write-Host "  [ok] Cached credentials cleared"

Write-Host ""
Write-Host "Done. Restart your PC to apply all changes." -ForegroundColor Green
