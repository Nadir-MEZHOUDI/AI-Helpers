# AI-Helpers installer for Windows
# Usage: irm https://raw.githubusercontent.com/Nadir-MEZHOUDI/AI-Helpers/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

$RepoUrl  = "https://github.com/Nadir-MEZHOUDI/AI-Helpers"
$TmpDir   = Join-Path $env:TEMP "AI-Helpers-install"
$SkillsDst = Join-Path $env:USERPROFILE ".claude\skills"

Write-Host "AI-Helpers installer" -ForegroundColor Cyan
Write-Host "--------------------"

# Clone
if (Test-Path $TmpDir) { Remove-Item -Recurse -Force $TmpDir }
Write-Host "Cloning $RepoUrl ..."
git clone --depth 1 $RepoUrl $TmpDir | Out-Null

# Skills
$SkillsSrc = Join-Path $TmpDir "skills"
if (Test-Path $SkillsSrc) {
    New-Item -ItemType Directory -Force $SkillsDst | Out-Null
    foreach ($skill in Get-ChildItem $SkillsSrc -Directory) {
        $dest = Join-Path $SkillsDst $skill.Name
        if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
        Copy-Item -Recurse $skill.FullName $dest
        Write-Host "  [skill] $($skill.Name)" -ForegroundColor Green
    }
}

# Cleanup
Remove-Item -Recurse -Force $TmpDir

Write-Host ""
Write-Host "Done. Restart Claude Code to pick up new skills." -ForegroundColor Cyan
