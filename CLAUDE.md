
## Session Logging

At the end of every **successful** session (work completed, not interrupted), append one entry to `AI_SESSIONS.md` at the repo root, then commit all changes without asking. Rules:

- **Append only** — never edit a previous entry, even if the date is the same. Every session = a new block.
- **English always** — regardless of the language used during the session.
- **Add a `## Month YYYY` header** if the month changed since the last entry.
- Use this template:

```
### YYYY-MM-DD HH:MM — {Tool} ({Model})

**What was done**
- up to 5 bullet points

**What wasn't completed**
- up to 3 bullet points (omit if everything was completed)

**Modified files**
- output of `git diff --name-only HEAD`
```

After appending, stage all modified/untracked files and `AI_SESSIONS.md`, then commit:
`git commit -m "session: YYYY-MM-DD HH:MM — {Tool}"`

When the user says "write the session summary" (or equivalent), run all of the above.
