# Assets

Files copied from SmartCHIFA (`D:\Projects\Best 2026\CHIFA.Pro`) and lightly renamed. They compile once the names
are adapted; they are starting points, not a package.

- `mobile/` — the Blazor WASM app shell: `Session`, `Api`, `Fmt`, layout, components, login, notifications, service workers,
  manifest, full CSS. `SmartCHIFA` → `MyProduct`, JS `window.chifa` → `window.app`. Domain pages (`Rejets`, `Statistiques`)
  and `Api.cs` routes (`api/officine/...`) are examples to replace. Texts are French.
- `deploy/` — Dockerfile, `dockerignore.txt` (rename to `.dockerignore` at the repo root), compose, VPS bootstrap, Nginx,
  Azure DevOps pipeline. Fill `{gateway-host}`, `<vps-user>`, `<github-user>`, and pick a free port instead of 8094.
- `tests/` — `GatewayFixture`, gateway and agent tests, local E2E harness (`E2E-Program.cs` → `artifacts/e2e/Program.cs`).
  Still use `Pharmacy` names: rename to the new tenant word.

The original source stays the reference when something here is unclear: `CHIFA.Gateway`, `CHIFA.Gateway.Data`,
`CHIFA.Contract`, `CHIFA.Agent`, `CHIFA.Mobile`, `CHIFA.Tests/Gateway`, `CHIFA Pro/Mobile` in that repository.
