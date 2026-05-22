---
name: session-logging
description: Sets up the AI session logging system on a project and writes session summary entries. Use when the user runs /session-logging to initialize the system on a new project (creates AI_SESSIONS.md and writes session logging instructions into AGENTS.md and/or CLAUDE.md). Also use when the user says "write the session summary", "write session log", "log the session", or any end-of-session summary request — in that case, append a new entry to AI_SESSIONS.md.
---

This skill has two modes. Detect which one from context:

- **`/session-logging` with no prior setup** → **Init mode**: set up the system on this project.
- **"write the session summary"** (or similar) → **Append mode**: write and append the current session's entry.

---

## Init Mode

Run when the user invokes `/session-logging` to set up a new project. Do all steps in order.

### Step 1 — Create AI_SESSIONS.md

Check if `AI_SESSIONS.md` exists at the repo root.

If it already exists and already contains session logging content, tell the user it's already set up and stop. Do not overwrite.

If it doesn't exist, create it with this exact content:

```markdown
# AI Sessions Log

Each AI agent appends one entry at the end of its session. Never edit a previous entry.

## Entry format

​```
### YYYY-MM-DD — {Tool} ({Model})

**What was done**
- up to 5 bullet points

**What wasn't completed**
- up to 3 bullet points (omit section if everything was completed)

**Modified files**
- list from `git diff --name-only HEAD`
​```

Rules:
- Write in English regardless of the session language.
- Add a `## Month YYYY` header when the month changes.
- Keep bullets tight — one line each.

---
```

### Step 2 — Update AGENTS.md (if it exists)

Check if `AGENTS.md` exists.

If yes:
1. Find the "What Matters First" section and add this line at the top of its list (skip if already present):
   ```
   - Read `AI_SESSIONS.md` for a log of what previous AI sessions did and left incomplete.
   ```

2. Append this section at the very end of AGENTS.md (skip if "Session Logging" section already present):

```markdown

## Session Logging

At the end of every session, append one entry to `AI_SESSIONS.md` at the repo root. Rules:

- **Append only** — never edit a previous entry.
- **English always** — regardless of the language used during the session.
- **Add a `## Month YYYY` header** if the month changed since the last entry.
- Use this template:

​```
### YYYY-MM-DD — {Tool} ({Model})

**What was done**
- up to 5 bullet points

**What wasn't completed**
- up to 3 bullet points (omit if everything was completed)

**Modified files**
- output of `git diff --name-only HEAD`
​```

When the user says "write the session summary" (or equivalent), generate and append this entry.
```

### Step 3 — Update CLAUDE.md (if it exists)

Check if `CLAUDE.md` exists.

If yes and AGENTS.md also exists:
Append a brief reference at the end (skip if already present):

```markdown

## Session Logging

At the end of every session, append a summary to `AI_SESSIONS.md`. See that file or `AGENTS.md → Session Logging` for the format. Never edit previous entries.
```

If yes but AGENTS.md does NOT exist:
Append the full instructions (same block as the AGENTS.md version above) since CLAUDE.md is the only guide an AI agent will read.

### Step 4 — Report

Tell the user exactly what was created or modified, and what was skipped (already present). One line per action.

---

## Append Mode

Run when the user asks to write the session summary at the end of a session.

### Step 1 — Gather modified files

Run:
```
git diff --name-only HEAD
```

If the repo has uncommitted new files, also run:
```
git status --short
```
and include untracked files that were clearly created this session (marked `??`).

### Step 2 — Compose the entry

Write in English. Use this structure:

```
### YYYY-MM-DD — {Tool} ({Model})

**What was done**
- (up to 5 bullets — what actually changed or was decided, not what was discussed)

**What wasn't completed**
- (up to 3 bullets — only if something was left incomplete; omit the section entirely if everything was done)

**Modified files**
- (one file per line from git output)
```

For Tool: use the agent tool name (e.g., `Claude Code`, `Codex`, `OpenCode`).
For Model: use the model ID from the system prompt (e.g., `claude-sonnet-4-6`).

Keep bullets tight — one line each. Do not narrate the conversation; describe outcomes.

### Step 3 — Check for month header

Read the last few lines of `AI_SESSIONS.md`. If the current month (`## Month YYYY`) is not already the most recent header, prepend `## Month YYYY` before your entry.

### Step 4 — Append to AI_SESSIONS.md

Append the entry (and month header if needed) to the end of `AI_SESSIONS.md`. Never modify any line above the append point.

Confirm to the user with: "Session summary appended to AI_SESSIONS.md."
