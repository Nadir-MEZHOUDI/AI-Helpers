# AI-Helpers

A personal collection of Claude Code skills, prompts, commands, and helpers — installable on any machine in one command.

## Install

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/Nadir-MEZHOUDI/AI-Helpers/main/install.ps1 | iex
```

**Linux / macOS:**
```bash
curl -fsSL https://raw.githubusercontent.com/Nadir-MEZHOUDI/AI-Helpers/main/install.sh | bash
```

Restart Claude Code after installing.

## Update

Run the same install command again — it overwrites existing items with the latest versions.

---

## Contents

### Skills (`/skills`)

Skills are invoked with `/skill-name` inside Claude Code.

| Skill | Trigger | What it does |
|---|---|---|
| `session-logging` | `/session-logging` or "write the session summary" | Sets up AI session logging on a new project, or appends a session summary entry to `AI_SESSIONS.md` |

### Prompts (`/prompts`)

_Empty — coming soon._

### Commands (`/commands`)

_Empty — coming soon._

### Helpers (`/helpers`)

_Empty — coming soon._

### PowerShell Scripts (`/scripts`)

Run directly — no installation needed. Scripts that require elevation are marked with ⚠️.

| Script | What it does |
|---|---|
| `Get-SystemInfo.ps1` | Quick summary: OS, CPU, RAM, disks, uptime |
| `Reset-Network.ps1` | ⚠️ Flush DNS, reset Winsock, release/renew IP |
| `Clear-TempFiles.ps1` | ⚠️ Delete temp files from common locations |
| `Fix-WindowsUpdate.ps1` | ⚠️ Reset stuck Windows Update components |
| `Fix-WiFi.ps1` | Auto-elevates — resets network stack, disables WiFi/USB power saving, restarts wireless adapters |
| `Fix-Printers.ps1` | ⚠️ Fixes stuck spooler, broken ports, Point & Print restrictions, RPC error 0x00000709 |
| `Fix-NetworkSharing.ps1` | ⚠️ Enables file/printer sharing, creates sharing user, fixes SMB and firewall rules |
| `Install-NetworkPrinter.ps1` | ⚠️ Auto-detects LAN printer by IP and installs it with TCP/IP port |

**Run a script:**
```powershell
# From the repo root
.\scripts\Get-SystemInfo.ps1

# Scripts requiring elevation — open an admin terminal first
.\scripts\Reset-Network.ps1
```

---

## Repository layout

```
AI-Helpers/
├── install.ps1          ← Windows installer
├── install.sh           ← Linux / macOS installer
├── skills/
│   └── session-logging/ ← Claude Code skill
├── scripts/             ← PowerShell utilities for Windows
├── prompts/
├── commands/
└── helpers/
```

## Adding a new skill

1. Create a folder under `skills/your-skill-name/`
2. Add a `SKILL.md` with the required frontmatter (`name`, `description`)
3. Commit and push — the installer will pick it up automatically on the next install/update

## Adding a new script

1. Drop a `.ps1` file into `scripts/`
2. Add a row to the Scripts table in this README
3. Commit and push
