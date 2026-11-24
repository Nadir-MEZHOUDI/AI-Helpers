---
name: desktop-mobile-gateway
description: Add a phone app (Blazor WebAssembly PWA) and a cloud gateway to an existing on-premise .NET desktop app whose data stays on the customer's PC, as done for SmartCHIFA. The desktop app (or a Windows service) runs an agent that keeps an outbound SignalR connection to the gateway; the phone calls the gateway, which forwards each request live to that agent. Use when the user asks to "add a mobile app / gateway / agent like CHIFA", to let customers see their desktop data on their phone, or to repeat the CHIFA.Gateway + CHIFA.Mobile work in another project.
---

# Desktop app → Gateway → Phone (PWA)

Reproduces the SmartCHIFA architecture in another .NET desktop product. Read this file fully, then the reference for the phase you are in. Every reference holds working code taken from SmartCHIFA, renamed with the placeholders below. `assets/` holds whole files to copy (phone app shell, CSS, deploy files, test fixture, E2E harness); see `assets/README.md`. The original source is `D:\Projects\Best 2026\CHIFA.Pro` when in doubt.

## The architecture

```
Phone (Blazor WASM PWA) ──HTTPS/JWT──► Gateway (ASP.NET Core, Docker on the VPS, behind Nginx + Cloudflare)
                                          │  stores only: tenants, phones, push subscriptions, keys (hashed)
                                          │  never stores business data
                                          ▼  SignalR (WebSocket), opened BY the agent: no port open at the customer
                                       Agent (inside the desktop app, or a Windows service when the app is closed)
                                          │  calls the same services the desktop app uses
                                          ▼
                                       Local database (PostgreSQL / SQL Server / ...)
```

- The gateway calls `Clients.Client(id).InvokeAsync<JsonElement>(method, arg)` and returns the agent's JSON untouched.
- The agent registers one handler per allowed method (`AgentMethods`). That list is the allowlist: nothing else can be asked.
- The tenant always comes from the token (`tenant_id` claim), never from the request.
- The phone app is served by the gateway itself (same origin: no CORS, one deploy).

## Placeholders

| Placeholder | Meaning | SmartCHIFA value |
|---|---|---|
| `MyApp` | Solution/project prefix | `CHIFA` |
| `Tenant` | One customer installation (pharmacy, shop, clinic...) | `Pharmacy` |
| `tenant_id` | JWT claim with the tenant id | `pharmacy_id` |
| `{gateway-host}` | Public host of the gateway | `chifa-gateway.smartappdz.org` |
| `{port}` | Loopback port of the container on the VPS | `8094` |
| `{ProductName}` | Name shown to users | `SmartCHIFA` |
| `{product-slug}` | SmartApp licensing product slug | `chifa-pro` |
| `{ProgramDataDir}` | Folder in `%ProgramData%` shared by app and service | `SmartCHIFA` |

User-facing messages are in the product's language (French in SmartCHIFA). Code, comments and logs are in English.

## Step 0: questions to ask before writing code

Ask these with AskUserQuestion; do not guess:
1. Gateway host name and VPS port (check `ssh vps "docker ps"` for ports already taken).
2. Which data the phone should show first (list the desktop screens; pick 3–5 for v1).
3. How a tenant registers: **self-registration with the SmartApp license file** (preferred when the product uses SmartApp.Licensing) or **admin-created** with `X-Admin-Key`.
4. How phones sign in: **pairing code + QR shown in the desktop app** (preferred), username/password, or both.
5. Push notifications in v1 or later, and which events.
6. Should the agent also run as a Windows service (answers when the desktop app is closed)?
7. Brand: colors, logo, fonts (reuse the product website if it exists).

Then read the desktop solution: data access (EF Core / linq2db), existing services and DTOs the screens use, whether they are singletons with shared state (see lessons), how the app starts, and how it is packaged (Velopack).

## Phases (one commit each, tests green before moving on)

1. **Contract + Gateway skeleton** → `references/gateway.md`
   `MyApp.Contract` (AgentMethods, queries, Page<T>), `MyApp.Gateway.Data` (EF Core, snake_case, migrations), `MyApp.Gateway` (hub, registry, invoker, agent-key auth, JWT, admin endpoints, `/health`). Gateway tests with the real Kestrel fixture (`references/testing.md`).
2. **Agent** → `references/agent.md`
   `MyApp.Agent` worker (forever retry, handlers with safe error messages), hosting inside the desktop app, optional Windows service + install script.
3. **Linking** → `references/gateway.md` (Link/Auth) + `references/agent.md` (desktop side)
   License self-registration, pairing codes, QR code form in the desktop app, device tokens, revocation.
4. **Phone app** → `references/mobile.md`
   PWA shell, Session/Api with automatic token renewal, login by code, layout (drawer on PC, tab bar on phone), first pages, `Fmt`, components.
5. **Deploy** → `references/deploy.md`
   Dockerfile, `.dockerignore`, compose, VPS bootstrap, Nginx, Cloudflare DNS, Azure DevOps pipeline. Deploy only with the user's explicit go-ahead.
6. **Push notifications** (optional) → `references/mobile.md` (service worker, JS) + `references/gateway.md` (PushSender, AlertPoller) + `references/agent.md` (AlertTracker).
7. **More pages** — repeat the "add a data page" recipe below.

## Recipe: add a data page

1. Contract: add `public const string GetX = nameof(GetX);` to `AgentMethods`, and a query record if it takes more than one argument.
2. Service (desktop solution): a method returning a DTO, taking every parameter explicitly (no shared singleton state).
3. Agent: `connection.On<XQuery, XDto>(AgentMethods.GetX, q => RunAsync(AgentMethods.GetX, () => Service.Instance.GetXAsync(...)));`
4. Gateway: `api.MapGet("/x", (..., ClaimsPrincipal user, AgentInvoker agent, CancellationToken ct) => agent.InvokeAsync(user.GetTenantId(), AgentMethods.GetX, new XQuery(...), ct));` For a `{kind}` route, check the value against an allowlist in the contract and return 404 otherwise.
5. Mobile: model record in `Models.cs` (only the fields shown), method in `Api.cs`, page in `Pages/`, entry in `NavItems`.
6. Tests: a gateway test (route forwards the right query, unknown values 404) and a service test against the real DB when one is available.
7. Verify in the browser with the local E2E harness (`references/testing.md`), at phone width and PC width.

## Rules that matter

- Gateway errors for agent problems: **503** offline/timeout, **424** agent error or agent too old. Never 502/504: Cloudflare replaces those bodies with its own HTML page and the phone loses the message.
- An agent older than the gateway answers "failed to parse argument" / "didn't provide a result": the gateway turns that into "update {ProductName} on the PC". Customers update slowly; every new page must degrade this way.
- The agent never sends exception text to the phone: log it, throw `HubException` with a safe message.
- Keys and tokens are 256-bit random, stored as SHA-256 hashes; shown once.
- Linking a phone, revoking it, or replacing the agent key takes effect immediately (JWT `OnTokenValidated` check, `registry.Disconnect`).
- Rate-limit every anonymous auth endpoint per IP; behind Cloudflare the real IP is `CF-Connecting-IP` (Nginx maps it).
- Phone app: `InvariantGlobalization` (smaller download), so format numbers and dates with the `Fmt` helper, never culture-dependent `ToString()`.
- Push/deploy/release only with the user's explicit approval for that change. Never commit `.env`, keys, or `launchSettings.json` secrets.

## Done means

- `dotnet test` green (gateway tests + agent unit tests + service tests).
- E2E harness: real gateway + real agent + real DB, each page checked in the browser pane at 375 px and ≥1024 px, no console errors.
- After deploy: `/health` 200, a protected route 401 without a token, the agent shows "Agent connected" in `docker compose logs`.

Read `references/lessons.md` before starting: it lists every mistake already made once.
