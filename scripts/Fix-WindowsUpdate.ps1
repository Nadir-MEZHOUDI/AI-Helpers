# Fix-WindowsUpdate.ps1
# Resets Windows Update components when updates are stuck or failing.
# Run as Administrator.

#Requires -RunAsAdministrator

Write-Host "Resetting Windows Update components..." -ForegroundColor Cyan

$services = @("wuauserv", "cryptSvc", "bits", "msiserver")

# Stop services
foreach ($svc in $services) {
    Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
    Write-Host "  [stopped] $svc"
}

# Rename cache folders (forces a clean rebuild)
$folders = @(
    "C:\Windows\SoftwareDistribution",
    "C:\Windows\System32\catroot2"
)
foreach ($folder in $folders) {
    if (Test-Path $folder) {
        $backup = "$folder.bak"
        if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
        Rename-Item $folder $backup -Force
        Write-Host "  [renamed] $folder -> $($folder).bak"
    }
}

# Re-register DLLs
$dlls = @(
    "atl.dll","urlmon.dll","mshtml.dll","shdocvw.dll","browseui.dll",
    "jscript.dll","vbscript.dll","scrrun.dll","msxml.dll","msxml3.dll",
    "msxml6.dll","actxprxy.dll","softpub.dll","wintrust.dll","dssenh.dll",
    "rsaenh.dll","gpkcsp.dll","sccbase.dll","slbcsp.dll","cryptdlg.dll",
    "oleaut32.dll","ole32.dll","shell32.dll","initpki.dll","wuapi.dll",
    "wuaueng.dll","wuaueng1.dll","wucltui.dll","wups.dll","wups2.dll",
    "wuweb.dll","qmgr.dll","qmgrprxy.dll","wucltux.dll","muweb.dll","wuwebv.dll"
)
foreach ($dll in $dlls) {
    regsvr32.exe /s $dll
}
Write-Host "  [ok] DLLs re-registered"

# Restart services
foreach ($svc in $services) {
    Start-Service -Name $svc -ErrorAction SilentlyContinue
    Write-Host "  [started] $svc"
}

Write-Host ""
Write-Host "Done. Run Windows Update now." -ForegroundColor Green
