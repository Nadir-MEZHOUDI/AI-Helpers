# AI Sessions Log

Each AI agent appends one entry at the end of its session. Never edit a previous entry.

## Entry format

```
### YYYY-MM-DD — {Tool} ({Model})

**What was done**
- up to 5 bullet points

**What wasn't completed**
- up to 3 bullet points (omit section if everything was completed)

**Modified files**
- list from `git diff --name-only HEAD`
```

Rules:
- Write in English regardless of the session language.
- Add a `## Month YYYY` header when the month changes.
- Keep bullets tight — one line each.

---

## May 2026

### 2026-05-25 19:14 — Claude Code (claude-sonnet-4-6)

**What was done**
- Added `HH:MM` timestamp to session entry headers so same-day sessions are distinguishable
- Enforced strict append-only rule: each session always creates a new block, even if the date is identical to a previous entry
- Added Step 5 auto-commit: after appending the entry, stages all modified files and commits without asking the user
- Added trigger condition: Append mode only fires at the end of a successful session — not mid-session, not during a question, not when interrupted
- Updated `CLAUDE.md` to match the new format and auto-commit behavior

**Modified files**
- skills/session-logging/SKILL.md
- CLAUDE.md
