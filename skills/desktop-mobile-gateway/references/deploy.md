# Deploy (Docker on the VPS, Nginx, Cloudflare, Azure DevOps)

Templates in `assets/deploy/` (renamed to `myapp-gateway`; `{gateway-host}`, `<vps-user>`, `<github-user>` to fill;
port `8094` is SmartCHIFA's, **pick a free one**: `ssh vps "docker ps --format '{{.Ports}}'"`).

## Directory.Build.props (desktop solutions)

Desktop solutions often force `net10.0-windows` + `win-x64` for every project. The Blazor WASM project must not get a
RuntimeIdentifier, or its publish output has no `_framework`:

```xml
<PropertyGroup Condition="'$(UsingMicrosoftNETSdkBlazorWebAssembly)' != 'true'">
  <TargetFramework>net10.0-windows</TargetFramework>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
</PropertyGroup>
```

Gateway, Gateway.Data and Contract set `<TargetFramework>net10.0</TargetFramework><RuntimeIdentifier></RuntimeIdentifier>` themselves (Linux container).

## Files

| File | Notes |
|---|---|
| `MyApp.Gateway/Dockerfile` | Build context = repo root. Restore with only the `.csproj` files first (layer cache), then copy sources, `dotnet publish /p:UseAppHost=false`. Runtime `aspnet:10.0`, non-root `APP_UID`, port 8080. Copies `Directory.Build.props`. |
| `.dockerignore` (`dockerignore.txt` in assets) | Deny all, then allow only `Directory.Build.props`, Contract, Gateway, Gateway.Data, Mobile; exclude `bin/`, `obj/`, `launchSettings.json`. |
| `deploy/gateway/docker-compose.yml` | Image `ghcr.io/<github-user>/myapp-gateway:latest`, `127.0.0.1:${GATEWAY_WEB_PORT}:8080`, env from `.env` with `:?` guards (`ConnectionStrings__Gateway`, `Gateway__AdminKey`, `Jwt__SigningKey`), external network `shared-net` with the shared `postgres` container, `restart: unless-stopped`. |
| `deploy/gateway/bootstrap-vps.sh` | One-time, idempotent: creates `~/myapp-gateway/.env` (chmod 600, random hex secrets via `openssl rand`), creates the DB role and database in the shared `postgres` container. Run: `ssh vps 'bash -s' < deploy/gateway/bootstrap-vps.sh`. Never overwrites `.env`. |
| `deploy/gateway/nginx-myapp-gateway.conf` | 80 → 301 https; 443 with the Cloudflare origin certificate; `map $http_cf_connecting_ip` → real client IP in `X-Forwarded-For`; WebSocket upgrade headers (`$connection_upgrade` map exists globally on the VPS); `proxy_read_timeout 120s` (keep-alive pings every 15 s keep SignalR alive); `client_max_body_size 1m`. |
| `azure-pipelines-gateway.yml` | Trigger on `master` with `paths` filter (Gateway, Gateway.Data, Contract, Mobile, deploy/gateway, the yml). Stage Image: `Docker@2` login `ghcr-login`, buildAndPush tags `latest` + build number. Stage Deploy: `SSH@0` check `.env` exists (else print the bootstrap command and fail), `CopyFilesOverSSH@0` compose file, `SSH@0` pull + `up -d --no-build --remove-orphans` + poll `/health` 30×2 s, print logs on failure. Service connections `ghcr-login` and `vps-ssh` already exist in the Azure DevOps org; share them with the new project. |

## First deploy checklist

1. Cloudflare DNS: `A`/`CNAME` for the gateway host, **proxied** (orange), SSL mode Full (strict) with the origin certificate.
2. `ssh vps 'bash -s' < deploy/gateway/bootstrap-vps.sh`.
3. Nginx: copy the conf to `/etc/nginx/sites-available/`, symlink to `sites-enabled`, `sudo nginx -t && sudo systemctl reload nginx` (ask the user to run sudo steps if needed).
4. Create the pipeline in Azure DevOps from `azure-pipelines-gateway.yml` (`az pipelines create ... --yml-path azure-pipelines-gateway.yml`).
5. Push (only with the user's go-ahead), watch the run:
   `az pipelines runs list --org https://dev.azure.com/<org> --project <project> --branch master --query-order QueueTimeDesc --top 3`.
6. Verify: `curl -s -o /dev/null -w "%{http_code}" https://{gateway-host}/health` → 200; a protected route → 401;
   `ssh vps "cd ~/myapp-gateway && docker compose logs --tail 50 web"` shows "Agent connected" once a desktop app is linked.

## Desktop release

The agent ships inside the desktop installer (Velopack). Customers only get new agent handlers after a desktop release,
so the gateway must keep working with old agents (424 "update needed", see lessons). Restarting the local desktop app
after a rebuild is enough to test on the developer's own PC.

## Not solved yet (known warnings)

- ASP.NET Data Protection keys are not persisted in the container (warning at startup). Harmless while no cookies/antiforgery are used; mount a volume and `PersistKeysToFileSystem` if that changes.
