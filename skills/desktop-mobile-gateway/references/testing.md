# Testing and verification

Templates in `assets/tests/` (from SmartCHIFA; rename `Pharmacy` → `Tenant`, `/api/officine` → the new group).

## Gateway tests (xUnit + FluentAssertions)

`GatewayFixture` runs the **real gateway on Kestrel** (real HTTP and WebSockets, like production) with in-memory SQLite:

- `WebApplicationFactory<Program>().WithWebHostBuilder(...)`, `UseSetting` for `ConnectionStrings:Gateway` ("unused"),
  `Gateway:AdminKey`, `Gateway:AgentTimeoutSeconds` = 2, `Jwt:SigningKey`, `Gateway:LicensePublicKey` (a test ECDSA key),
  `Gateway:AuthRequestsPerMinute` = 10000 (all tests come from 127.0.0.1).
- Replace the DbContext: `RemoveAll<IDbContextOptionsConfiguration<GatewayDbContext>>()`, `RemoveAll<DbContextOptions<...>>()`,
  `RemoveAll<IDbContextFactory<...>>()`, then `AddDbContextFactory(o => o.UseSqlite(openConnection))`.
- `_factory.UseKestrel(0); _factory.StartServer();` → `BaseAddress`.
- Helpers: `CreateClient`, `CreateAdminClient`, `CreateTenantAsync` (tenant + user via admin endpoints), `LoginAsync`,
  `CreateLicense(...)` (signs an `SL1.` license with the test key), `RegisterAsync(license)`, `CreateTenantClient(agentKey)`,
  `CreateAgentConnection(agentKey)` (a bare `HubConnection`; each test registers only the handlers it needs), `WaitUntilAsync`.
- `[Collection(GatewayCollection.Name)]` shares one fixture.

Tests to have from day one (all exist in `GatewayTests.cs`, `LinkTests` pattern in SmartCHIFA):
- Phone request answered by the agent, with the exact query the agent receives.
- Phone only reaches its own tenant (two agents, two tenants).
- Offline tenant → 503 and `/status` says offline.
- Agent `HubException` → 424 with the agent's message.
- Agent without the method → 424 "update needed" (theory over a few routes).
- Slow agent → 503 "not answering" (sleep `AgentTimeoutSeconds + 3`).
- `{kind}` routes forward the kind and refuse unknown kinds (404).
- Unknown agent key → 401 on hub start; real `AgentWorker` reconnects after `registry.Disconnect`; replacing the key disconnects for good.
- Admin endpoints need the admin key; wrong password 401; phone API needs a token.
- `/` and app routes serve `index.html` (contains `_framework/blazor.webassembly.js`); unknown `/api/*` and `/admin/*` are 404.
- Linking: register with a valid license (and reinstall gives a new key, old one rejected), wrong product / expired / bad signature 403,
  pairing code single-use and expiring, device login, logout revokes, removed device's JWT rejected at once.
- Push: subscribe rejects non push-service endpoints; gone subscriptions are deleted; poller sends agent alerts.

Agent unit tests (`AgentTests.cs`): retry policy delays, `ToPage` clamps, period defaults, `AlertTracker.Compute` cases.

Service tests against the real local DB (mark them with a trait, e.g. `[Trait("Category", "DbFunctional")]`, and skip when
the DB is absent): every new agent method returns sane data and totals match their rows.

Run: `dotnet test MyApp.Tests --filter "Category!=Performance"`.

## Local end-to-end harness (git-ignored `artifacts/e2e`)

`E2E-Program.cs`: the real gateway on `http://localhost:5080` with a SQLite **file**, `UseEnvironment("Development")`,
creates a tenant and a user through the admin API, writes the generated password to `artifacts/e2e/credentials.txt`,
then starts a real `AgentWorker` on the developer's local database. Prints `READY <url>`.

- Start in the background: `dotnet run --project artifacts/e2e/E2E.csproj`.
- Every start makes a new tenant and password: clear `localStorage` in the browser and sign in again.
- Kill `E2E.exe` before running the gateway tests if a port conflicts.
- Never paste the generated credentials in chat; read them from the file when signing in.
- To test the desktop app against it: set `MYAPP_GATEWAY_URL=http://localhost:5080` for the desktop process.

## Browser verification (built-in browser pane)

1. `preview_start` with the harness URL (or a `.claude/launch.json` entry that attaches to it).
2. Sign in (username/password form), then for each new page: `read_page`/`get_page_text` for content,
   `read_console_messages` for errors, a screenshot at `resize_window` mobile (375×812) and desktop (≥1024).
3. Check: numbers formatted (thousands separator visible), labels not overlapping, charts colored, tap/selection works,
   empty period shows the empty message, offline agent (stop the harness agent) shows the 503 message with Retry.
4. Reset the viewport (`preset: desktop`) when done.

## After deploy

- `/health` 200; a protected route 401 without token; the app route 200.
- Gateway logs show the developer's agent "Agent connected" again after the container restart.
- Tell the user what was not verified (e.g. signing in on production with their phone).
