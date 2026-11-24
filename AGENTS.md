# Agent Instructions

## What Matters First

- Read `AI_SESSIONS.md` for a log of what previous AI sessions did and left incomplete.

---

## Session Logging

At the end of every session, append one entry to `AI_SESSIONS.md` at the repo root. Rules:

- **Append only** — never edit a previous entry.
- **English always** — regardless of the language used during the session.
- **Add a `## Month YYYY` header** if the month changed since the last entry.
- Use this template:

```
### YYYY-MM-DD — {Tool} ({Model})

**What was done**
- up to 5 bullet points

**What wasn't completed**
- up to 3 bullet points (omit if everything was completed)

**Modified files**
- output of `git diff --name-only HEAD`
```

When the user says "write the session summary" (or equivalent), generate and append this entry.
