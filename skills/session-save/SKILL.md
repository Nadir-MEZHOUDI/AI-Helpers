---
name: session-save
description: Saves the current session as a permanent handoff file so a future agent can resume exactly where this one left off. Use when the user runs /session-save {topic} — creates a structured checkpoint file in the repo's /sessions/ folder. Also use when the user says "save session", "create checkpoint", "save handoff", or any equivalent phrasing.
argument-hint: "{topic} — the feature or task this session was working on (e.g. add-auth, fix-payments)"
---

Create a structured session checkpoint file so a fresh agent can continue this work without re-deriving context.

## Step 1 — Resolve the topic

If the user passed an argument, use it as the topic slug (lowercase, hyphens, no spaces).

If no argument was passed, infer the topic from the conversation (the main feature or task being worked on). Confirm with the user before proceeding.

## Step 2 — Resolve the filename

Date format: `dd-MM-yy` (e.g. `22-05-26`).

Target filename: `{topic}_{dd-MM-yy}_session.md`

Check if `sessions/{topic}_{dd-MM-yy}_session.md` already exists in the repo root.

- If it does not exist → use that filename.
- If it exists → try `{topic}_{dd-MM-yy}_2_session.md`, then `_3_`, and so on until a free slot is found.

## Step 3 — Ensure the sessions/ folder exists

Check if `sessions/` exists at the repo root. If not, create it.

Check if `sessions/.gitkeep` exists. If not, create it (empty file) so the folder is tracked by git even when empty.

## Step 4 — Compose the checkpoint

Write the file using this exact structure. Do NOT duplicate content already captured in git history, ADRs, PRDs, or plan files — reference those by relative path or URL instead.

Redact any sensitive information (API keys, passwords, tokens, PII).

```markdown
# {topic} — Session Handoff ({YYYY-MM-DD})

## Goal
<!-- One paragraph: what this session was trying to achieve and why. -->

## Current State
<!-- Where things stand right now. What works, what's broken, what's half-done.
     Be specific — a new agent reads this first and needs an honest snapshot. -->

## Open Tasks
<!-- Bulleted checklist of what's left to do, in priority order. -->
- [ ] ...

## Key Decisions
<!-- Decisions made this session with brief rationale.
     Prevents the next agent from re-litigating settled choices. -->
- **Decision**: rationale

## Critical Files
<!-- file:line references the next agent must read before touching anything.
     Only list files that are non-obvious or have hidden constraints. -->
- `path/to/file.cs:42` — why it matters

## Suggested Skills
<!-- Skills the next agent should invoke to continue this work. -->
- `/skill-name` — why

## Context Scratchpad
<!-- Gotchas, env quirks, constraints, anything that doesn't fit above.
     Leave empty if nothing. -->
```

## Step 5 — Write the file

Save the composed content to `sessions/{filename}` at the repo root.

## Step 6 — Report

Tell the user:
- The full path of the file created.
- How to reference it in a new session (e.g. "Start your next session with: read sessions/{filename} then continue from the Open Tasks section").

Do not append anything to `AI_SESSIONS.md` — this skill is independent of session logging.
