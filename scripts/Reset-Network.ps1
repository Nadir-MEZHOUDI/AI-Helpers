# Reset-Network.ps1
# Flushes DNS, resets Winsock, releases and renews IP.
# Run as Administrator.

#Requires -RunAsAdministrator

Write-Host "Resetting network..." -ForegroundColor Cyan

ipconfig /release | Out-Null
Write-Host "  [ok] IP released"

ipconfig /flushdns | Out-Null
Write-Host "  [ok] DNS cache flushed"

netsh winsock reset | Out-Null
Write-Host "  [ok] Winsock reset"

netsh int ip reset | Out-Null
Write-Host "  [ok] IP stack reset"

ipconfig /renew | Out-Null
Write-Host "  [ok] IP renewed"

Write-Host ""
Write-Host "Done. Restart the machine for Winsock changes to take effect." -ForegroundColor Green
