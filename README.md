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

---

## Repository layout

```
AI-Helpers/
├── install.ps1          ← Windows installer
├── install.sh           ← Linux / macOS installer
├── skills/
│   └── session-logging/ ← Claude Code skill
├── prompts/
├── commands/
└── helpers/
```

## Adding a new skill

1. Create a folder under `skills/your-skill-name/`
2. Add a `SKILL.md` with the required frontmatter (`name`, `description`)
3. Commit and push — the installer will pick it up automatically on the next install/update
