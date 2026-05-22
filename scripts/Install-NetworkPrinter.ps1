# Install-NetworkPrinter.ps1
# Detects a TCP/IP printer on the LAN and installs it automatically.
# Run as Administrator.

#Requires -RunAsAdministrator

param(
    [string]$PrinterName = "OfficePrinter",
    [string]$DriverName  = "",   # Leave empty to auto-select
    [string]$INFPath     = ""    # Optional: path to .inf driver file
)

Write-Host "Detecting printer IP..." -ForegroundColor Cyan

$arp = arp -a
$ips = ($arp | Select-String "192\.168\." | ForEach-Object {
    ($_ -split "\s+")[1]
}) | Select-Object -Unique

if ($ips.Count -eq 0) {
    Write-Host "No device detected automatically."
    $PrinterIP = Read-Host "Enter printer IP manually"
} else {
    Write-Host "Possible devices found:"
    $i = 1
    foreach ($ip in $ips) {
        Write-Host "  $i - $ip"
        $i++
    }
    $choice = Read-Host "Select number or press Enter to type manually"
    if ($choice -match "^\d+$" -and [int]$choice -le $ips.Count) {
        $PrinterIP = $ips[[int]$choice - 1]
    } else {
        $PrinterIP = Read-Host "Enter printer IP manually"
    }
}

Write-Host "  [ok] Using IP: $PrinterIP" -ForegroundColor Green

# TCP/IP port
$PortName = "IP_$PrinterIP"
if (-not (Get-PrinterPort -Name $PortName -ErrorAction SilentlyContinue)) {
    Write-Host "  Creating TCP/IP port..."
    Add-PrinterPort -Name $PortName -PrinterHostAddress $PrinterIP
}

# Driver
if ($INFPath -ne "") {
    Write-Host "  Installing driver from INF..."
    pnputil /add-driver $INFPath /install
}

if ($DriverName -eq "") {
    $DriverName = (Get-PrinterDriver |
        Where-Object { $_.Name -match "Generic|HP|Canon|Epson" } |
        Select-Object -First 1).Name
}

if (-not $DriverName) {
    Write-Host "No suitable driver found. Install a driver first." -ForegroundColor Red
    exit 1
}

Write-Host "  [ok] Using driver: $DriverName"

# Add printer
if (-not (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue)) {
    Write-Host "  Adding printer '$PrinterName'..."
    Add-Printer -Name $PrinterName -DriverName $DriverName -PortName $PortName
}

# Set as default
(Get-CimInstance Win32_Printer -Filter "Name='$PrinterName'").SetDefaultPrinter() | Out-Null

Write-Host ""
Write-Host "Printer installed and set as default." -ForegroundColor Green
Write-Host "Printing test page..."
rundll32 printui.dll,PrintUIEntry /k /n "$PrinterName"
