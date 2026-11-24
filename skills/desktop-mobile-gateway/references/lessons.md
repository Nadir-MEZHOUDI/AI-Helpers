# Lessons learned on SmartCHIFA (read before starting)

Each item cost time once. Apply them from the start.

## Gateway and network

- **Cloudflare hides 502/504.** When the origin answers 502 or 504, Cloudflare shows its own HTML error page; the phone got
  "Erreur 502" with no explanation. Use **503** (offline, timeout) and **424** (agent error, agent too old), always as
  ProblemDetails with a human `title` and `detail`.
- **Old agents.** A SignalR client without a handler for a method answers with "Client failed to parse argument(s)" or
  "Client didn't provide a result". Map both to "Update {ProductName} on the computer". Customers update weeks later.
- **Timeout detection.** SignalR reports a cancelled `InvokeAsync` as `HubException`, not `OperationCanceledException`:
  check `timeout.IsCancellationRequested && !requestAborted.IsCancellationRequested`.
- **Message size.** Default SignalR max receive size is 32 KB; pages of rows exceed it. Set 4 MB on the hub.
- **Real client IP.** Behind Cloudflare + Nginx, rate limiting by IP needs `CF-Connecting-IP` mapped in Nginx and
  `UseForwardedHeaders` with known proxies cleared (container bound to 127.0.0.1 only).
- **SPA fallback swallowing API 404s.** `MapFallbackToFile("index.html")` answers unknown `/api/...` with the app. Add
  `MapFallback("api/{**rest}", () => Results.NotFound())` (and `admin`, `hubs`).
- **Route parameters as kinds.** A `/stats/{kind}` route passes user text to the agent: check it against a list in the contract.

## Agent and services

- **Singleton services with shared state race.** The desktop `StatisticsService` was a singleton with a `Period` field set by
  the UI. Two phone calls at once (or phone + desktop) overwrote each other's period. Give every service method used by the
  agent explicit parameters (`Period? period = null` → `period ?? Period` keeps the desktop working).
- **Never leak exceptions.** The SignalR client sends exception messages to the server as is. Wrap every handler in `RunAsync`
  and throw `HubException` with a safe text; log the real one locally.
- **Windows service ≠ user session.** A LocalSystem service does not see user environment variables (DB server name) nor the
  user's working directory (`SetBasePath(AppContext.BaseDirectory)`).
- **Default reconnect gives up after ~45 s.** Use `ForeverRetryPolicy` with jitter and an outer loop for full closes.
- **Two agents, one key.** A copied install connects with the same key: the registry keeps the newest and aborts the old.
- **First run of alerts.** Without "first run only records", installing the agent pushed months of old payments.

## Phone app

- **InvariantGlobalization**: `ToString("N2")` gives English formats and `CultureInfo("fr-FR")` throws or falls back.
  Format with a hand-made `NumberFormatInfo` (`Fmt`).
- **Thousands separator invisible.** U+202F (narrow no-break space, what fr-FR uses) is drawn with almost no width by the
  display font. Use U+00A0.
- **Bar colors hidden.** A stacked-segment color rule covered the per-bar color: render segments only when a bar has more than one value.
- **Chart labels overlapping.** A leftover `white-space: nowrap` on `.col-label`; use `white-space: normal` + `text-align: center`.
- **Browser cache during checks.** After a CSS edit, the old stylesheet may still be served: hard reload / check the computed
  style before concluding the fix failed.
- **Stale token after harness restart.** The E2E harness creates a new tenant each run; the old token in `localStorage` points
  to a tenant that no longer exists ("offline"). Clear `localStorage` and sign in again.
- **beforeinstallprompt fires before Blazor boots.** Capture it in `index.html` and replay it to .NET.
- **iPhone push** only works from the installed PWA (home screen icon), not from Safari.
- **Don't drop existing content** when adding features to a page the user already uses; add sections.

## Build and deploy

- **Blazor WASM + desktop `Directory.Build.props`.** A global `RuntimeIdentifier=win-x64` breaks the WASM publish (no `_framework`).
  Condition it on `UsingMicrosoftNETSdkBlazorWebAssembly`.
- **Pipeline run lookup.** `az pipelines runs list` sorted by default can return an older run: filter by branch and
  `--query-order QueueTimeDesc`, and match `sourceVersion` to the pushed commit.
- **Commit messages on Windows.** PowerShell here-strings piped to `git commit -F -` failed: write the message to a scratchpad
  file and `git commit -F <file>`.
- **DevExpress (or any licensed UI lib) in CI.** The desktop Velopack pipeline failed on a trial license key in the secure file;
  the gateway pipeline is independent. Check the desktop pipeline's licensing before promising customers a release.
- **Secrets.** Keep `.env` only on the VPS; never commit keys (an old `publish.bat` with a storage key ended up on GitHub).
- **Approval.** Push, deploy and desktop releases need the user's explicit go-ahead for that change, every time.
