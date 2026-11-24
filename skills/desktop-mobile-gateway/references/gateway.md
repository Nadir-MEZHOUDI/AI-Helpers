# Gateway (MyApp.Contract, MyApp.Gateway.Data, MyApp.Gateway)

ASP.NET Core minimal APIs, vertical slices under `Features/` (Agents, Auth, Link, Admin, Data, Push).
Packages: `Microsoft.AspNetCore.Authentication.JwtBearer`, `Npgsql.EntityFrameworkCore.PostgreSQL`,
`EFCore.NamingConventions`, `Microsoft.EntityFrameworkCore.Design` (gateway only, PrivateAssets all),
`Lib.Net.Http.WebPush` (only with push). Tests add `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Sqlite`,
`Microsoft.AspNetCore.SignalR.Client`.

## Project files

```xml
<!-- MyApp.Contract.csproj: shared by gateway, agent and desktop app. Plain net10.0. -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier></RuntimeIdentifier>
  </PropertyGroup>
</Project>

<!-- MyApp.Gateway.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier></RuntimeIdentifier>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyApp.Contract\MyApp.Contract.csproj" />
    <ProjectReference Include="..\MyApp.Gateway.Data\MyApp.Gateway.Data.csproj" />
    <!-- The phone app, served as static files on the same origin as the API. -->
    <ProjectReference Include="..\MyApp.Mobile\MyApp.Mobile.csproj" />
  </ItemGroup>
</Project>
```

`<RuntimeIdentifier></RuntimeIdentifier>` overrides a desktop `Directory.Build.props` that sets `win-x64` for everything
(see `deploy.md` for the matching condition in `Directory.Build.props`).

## Contract (MyApp.Contract/AgentProtocol.cs)

```csharp
namespace MyApp.Contract;

/// <summary>What the gateway may ask an agent. The agent only answers these methods: this list is the allowlist.</summary>
public static class AgentMethods
{
    public const string GetInfo = nameof(GetInfo);
    public const string GetDashboard = nameof(GetDashboard);
    // one constant per page/feature...

    /// <summary>Polled by the gateway for phone notifications. Each alert is returned only once.</summary>
    public const string GetAlerts = nameof(GetAlerts);
}

public static class AgentHub
{
    public const string Route = "/hubs/agent";
}

/// <summary>Both bounds are whole, inclusive days. <c>To = null</c> means up to today.</summary>
public record PeriodQuery(DateTime? From, DateTime? To);

/// <param name="Text">Search text, for the lists that have one.</param>
public record PageQuery(DateTime? From, DateTime? To, int Skip = 0, int Take = PageQuery.DefaultTake, string? Text = null)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public int SafeSkip => Math.Max(0, Skip);
    public int SafeTake => Math.Clamp(Take, 1, MaxTake);
}

public record Page<T>(int Total, IReadOnlyList<T> Items);

public record AgentInfo(string? TenantName, string AgentVersion, DateTime ServerTime);

/// <param name="Kind">One of <see cref="All"/>: routes with a free {kind} segment are checked against it.</param>
public record StatsQuery(string Kind, DateTime? From, DateTime? To)
{
    public static readonly IReadOnlyList<string> All = ["jours", "semaines", "mois", "annees"];
}

/// <param name="Key">Stable identity of the event: the agent never returns the same key twice.</param>
/// <param name="Url">Page of the phone app to open, relative to its root (e.g. <c>rejets</c>).</param>
public record AgentAlert(string Key, string Title, string Body, string Url);
```

`MyApp.Contract/MobileLink.cs` (only with self-registration / pairing):

```csharp
public static class LinkRoutes
{
    public const string Register = "/api/link/register"; // anonymous: the signed license file is the proof
    public const string Codes = "/api/link/codes";       // agent key
    public const string Devices = "/api/link/devices";   // agent key
}

public record RegisterTenantRequest(string License);
public record RegisterTenantResponse(Guid TenantId, string Name, string AgentKey); // key shown once
public record PairingCodeResponse(string Code, DateTime ExpiresAt, bool AgentOnline); // ABCD-2345
public record LinkedDevice(Guid Id, string Name, DateTime CreatedAt, DateTime? LastSeenAt);
```

## Data (MyApp.Gateway.Data)

Entities (all `Guid Id = Guid.CreateVersion7()`, `DateTime CreatedAt = DateTime.UtcNow`):

- `Tenant`: `Name` (200), `Guid? LicenseId` (unique), `AgentKeyHash` (64, unique), `IsActive = true`, `LastSeenAt?`, `Users`, `Devices`.
- `MobileUser`: `TenantId`, `UserName` (100, unique, stored lower-case), `PasswordHash`, `IsActive`.
- `MobileDevice`: `TenantId`, `Name` (100), `TokenHash` (64, unique), `LastSeenAt?`, `RevokedAt?` (logout or removed in the desktop app).
- `PairingCode`: `TenantId`, `CodeHash` (64, unique), `ExpiresAt`. One pending per tenant.
- `PushSubscription`: `TenantId`, `Guid? DeviceId` (cascade delete), `Endpoint` (1000, unique), `P256dh` (200), `Auth` (100).
- `VapidKey`: `int Id` (ValueGeneratedNever, single row 1), `PublicKey`, `PrivateKey` (100 each).

```csharp
/// <summary>Used only by <c>dotnet ef</c> to create migrations.</summary>
public class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql("Host=localhost;Database=myapp_gateway")
            .UseSnakeCaseNamingConvention()
            .Options);
}
```

Migrations: `dotnet ef migrations add Init -p MyApp.Gateway.Data -s MyApp.Gateway.Data`. The gateway runs
`MigrateAsync()` on start for Npgsql and `EnsureCreatedAsync()` otherwise (tests use SQLite).

## Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GatewayOptions>().BindConfiguration(GatewayOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddDbContextFactory<GatewayDbContext>(o => o
    // Retries cover PostgreSQL still starting when the gateway boots (both restarted with the VPS).
    .UseNpgsql(builder.Configuration.GetConnectionString("Gateway"), npgsql => npgsql.EnableRetryOnFailure())
    .UseSnakeCaseNamingConvention());

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = AuthEndpoints.SigningKey(jwt),
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        o.Events = new JwtBearerEvents { OnTokenValidated = AuthEndpoints.RejectRevokedDeviceAsync };
    })
    .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AgentKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddSignalR(o =>
{
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    // Agent answers (pages of rows) are larger than the 32 KB default.
    o.MaximumReceiveMessageSize = 4 * 1024 * 1024;
});

var authRequestsPerMinute = builder.Configuration.GetSection(GatewayOptions.Section).Get<GatewayOptions>()?.AuthRequestsPerMinute ?? 10;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authRequestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Only Nginx on the same host can reach the container (port bound to 127.0.0.1), so trust it.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<AgentInvoker>();
builder.Services.AddSingleton<IPasswordHasher<MobileUser>, PasswordHasher<MobileUser>>();
// Push (optional):
builder.Services.AddHttpClient(nameof(PushSender));
builder.Services.AddSingleton<PushSender>();
builder.Services.AddHostedService<AlertPoller>();

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<GatewayDbContext>>().CreateDbContextAsync())
{
    if (db.Database.IsNpgsql())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();
app.MapHub<AgentConnectionHub>(AgentHub.Route);
app.MapAuthEndpoints();
app.MapLinkEndpoints();
app.MapAdminEndpoints();
app.MapDataEndpoints();
app.MapPushEndpoints();

// Phone app. Unknown API paths stay 404 instead of returning the app's index.html.
app.MapStaticAssets();
app.MapFallbackToFile("index.html");
foreach (var prefix in new[] { "api", "admin", "hubs" })
    app.MapFallback($"{prefix}/{{**rest}}", () => Results.NotFound());

app.Run();

public partial class Program;
```

## Options

```csharp
public class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>Sent in the <c>X-Admin-Key</c> header to manage tenants and users.</summary>
    [Required, MinLength(32)] public string AdminKey { get; set; } = string.Empty;

    /// <summary>How long a phone request waits for the agent before giving up.</summary>
    [Range(1, 300)] public int AgentTimeoutSeconds { get; set; } = 30;

    /// <summary>SmartApp licensing public key (PEM): verifies the license the desktop app sends to register. Not a secret.</summary>
    [Required] public string LicensePublicKey { get; set; } = "<copy the PEM from SmartApp.Licensing>";
    [Required] public string LicenseProduct { get; set; } = "{product-slug}";

    [Range(1, 60)] public int PairingCodeMinutes { get; set; } = 10;

    /// <summary>Sign-in, pairing and registration attempts allowed per IP address and minute.</summary>
    [Range(1, 10_000)] public int AuthRequestsPerMinute { get; set; } = 10;

    [Range(1, 1440)] public int AlertPollMinutes { get; set; } = 5;

    /// <summary>VAPID contact given to the browsers' push services (a mailto: or https: URL).</summary>
    [Required] public string PushSubject { get; set; } = "https://{product-website}";
}

public class JwtOptions
{
    public const string Section = "Jwt";
    [Required, MinLength(32)] public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "myapp-gateway";
    public string Audience { get; set; } = "myapp-mobile";
    [Range(1, 24 * 30)] public int LifetimeHours { get; set; } = 12;
}
```

`appsettings.json` keeps `AdminKey` and `SigningKey` empty; production values come from `.env` via compose
(`Gateway__AdminKey`, `Jwt__SigningKey`, `ConnectionStrings__Gateway`).

## Features/Agents

### TenantClaims.cs
```csharp
public static class TenantClaims
{
    public const string TenantId = "tenant_id";

    public static Guid GetTenantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(TenantId), out var id)
            ? id
            : throw new InvalidOperationException("The user has no tenant_id claim.");
}
```

### AgentKeys.cs
```csharp
public static class AgentKeys
{
    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>The key is 256 random bits, so a plain SHA-256 (no salt, no stretching) is enough.</summary>
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
```

### AgentKeyAuthenticationHandler.cs
```csharp
/// <summary>Authenticates the agent with its key, sent as a bearer token.</summary>
public class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IDbContextFactory<GatewayDbContext> dbFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AgentKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = ReadKey();
        if (string.IsNullOrEmpty(key))
            return AuthenticateResult.NoResult();

        var hash = AgentKeys.Hash(key);
        await using var db = await dbFactory.CreateDbContextAsync(Context.RequestAborted);
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.AgentKeyHash == hash && t.IsActive)
            .Select(t => new { t.Id, t.Name })
            .FirstOrDefaultAsync(Context.RequestAborted);
        if (tenant is null)
            return AuthenticateResult.Fail("Invalid agent key.");

        var identity = new ClaimsIdentity(
            [new Claim(TenantClaims.TenantId, tenant.Id.ToString()), new Claim(ClaimTypes.Name, tenant.Name)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private string? ReadKey()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();
        // WebSocket clients that cannot set headers send the token in the query string.
        return Request.Query["access_token"].ToString();
    }
}
```

### AgentRegistry.cs
```csharp
/// <summary>Which SignalR connection serves each tenant. One agent per tenant: the newest wins.</summary>
public class AgentRegistry
{
    private readonly ConcurrentDictionary<Guid, HubCallerContext> _agents = new();

    public string? GetConnectionId(Guid tenantId) => _agents.TryGetValue(tenantId, out var c) ? c.ConnectionId : null;
    public bool IsOnline(Guid tenantId) => _agents.ContainsKey(tenantId);

    public void Register(Guid tenantId, HubCallerContext context)
    {
        HubCallerContext? previous = null;
        _agents.AddOrUpdate(tenantId, context, (_, old) => { previous = old; return context; });
        // A second agent with the same key (old PC, copied install): drop the old one.
        if (previous is not null && previous.ConnectionId != context.ConnectionId)
            previous.Abort();
    }

    public void Unregister(Guid tenantId, string connectionId)
    {
        if (_agents.TryGetValue(tenantId, out var current) && current.ConnectionId == connectionId)
            _agents.TryRemove(KeyValuePair.Create(tenantId, current));
    }

    /// <summary>Closes the agent connection, e.g. after its key was replaced.</summary>
    public void Disconnect(Guid tenantId)
    {
        if (_agents.TryRemove(tenantId, out var context))
            context.Abort();
    }
}
```

The registry is in memory: one gateway instance. Scaling out would need a backplane; not needed so far.

### AgentConnectionHub.cs
```csharp
/// <summary>The agent keeps this connection open. The gateway calls the agent through it; the agent never calls the gateway.</summary>
[Authorize(AuthenticationSchemes = AgentKeyAuthenticationHandler.SchemeName)]
public class AgentConnectionHub(AgentRegistry registry, IDbContextFactory<GatewayDbContext> dbFactory, ILogger<AgentConnectionHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User!.GetTenantId();
        registry.Register(tenantId, Context);
        logger.LogInformation("Agent connected: {Tenant} ({TenantId})", Context.User!.Identity?.Name, tenantId);
        await TouchAsync(tenantId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenantId = Context.User!.GetTenantId();
        registry.Unregister(tenantId, Context.ConnectionId);
        logger.LogInformation(exception, "Agent disconnected: {Tenant} ({TenantId})", Context.User!.Identity?.Name, tenantId);
        await TouchAsync(tenantId);
    }

    private async Task TouchAsync(Guid tenantId)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastSeenAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            // "Last seen" is informational: never fail the agent connection for it.
            logger.LogWarning(ex, "Could not update LastSeenAt for {TenantId}", tenantId);
        }
    }
}
```

### AgentInvoker.cs (the heart; keep the status codes)
```csharp
/// <summary>Forwards a phone request to the tenant's agent and returns its JSON answer untouched.</summary>
public class AgentInvoker(AgentRegistry registry, IHubContext<AgentConnectionHub> hub, IOptions<GatewayOptions> options, ILogger<AgentInvoker> logger)
{
    public async Task<IResult> InvokeAsync(Guid tenantId, string method, object? argument, CancellationToken requestAborted)
    {
        var connectionId = registry.GetConnectionId(tenantId);
        if (connectionId is null)
            return Offline();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.AgentTimeoutSeconds));
        try
        {
            var client = hub.Clients.Client(connectionId);
            var result = argument is null
                ? await client.InvokeAsync<JsonElement>(method, timeout.Token)
                : await client.InvokeAsync<JsonElement>(method, argument, timeout.Token);
            return Results.Json(result);
        }
        // SignalR reports a cancelled invocation as a HubException, not an OperationCanceledException.
        catch (Exception ex) when (ex is OperationCanceledException or HubException
                                   && timeout.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Agent {TenantId} did not answer {Method} in time", tenantId, method);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "<The site> is not answering", detail: "<Timed out, try again.>");
        }
        // Not 502/504: Cloudflare replaces those with its own HTML page and the phone loses the message.
        catch (HubException ex) when (IsUnknownMethod(ex))
        {
            logger.LogWarning("Agent {TenantId} is too old for {Method}: {Error}", tenantId, method, ex.Message);
            return Results.Problem(statusCode: StatusCodes.Status424FailedDependency,
                title: "<Update {ProductName} needed>", detail: "<Update {ProductName} on the computer to use this page.>");
        }
        catch (HubException ex)
        {
            logger.LogWarning("Agent {TenantId} failed {Method}: {Error}", tenantId, method, ex.Message);
            return Results.Problem(statusCode: StatusCodes.Status424FailedDependency, title: "<Error on the computer side>", detail: ex.Message);
        }
        catch (IOException)
        {
            return Offline(); // the agent disconnected while we were waiting
        }
    }

    /// <summary>For background work: the agent's answer, or default when it is offline, slow or fails (logged).</summary>
    public async Task<T?> TryCallAsync<T>(Guid tenantId, string method, CancellationToken ct)
    {
        var connectionId = registry.GetConnectionId(tenantId);
        if (connectionId is null)
            return default;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.AgentTimeoutSeconds));
        try
        {
            return await hub.Clients.Client(connectionId).InvokeAsync<T>(method, timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is OperationCanceledException or HubException or IOException)
        {
            logger.LogDebug("Agent {TenantId} did not answer {Method}: {Error}", tenantId, method, ex.Message);
            return default;
        }
    }

    // What the SignalR client answers when it has no handler for the method.
    private static bool IsUnknownMethod(HubException ex) =>
        ex.Message.Contains("failed to parse argument", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("didn't provide a result", StringComparison.OrdinalIgnoreCase);

    private static IResult Offline() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "<Site offline>", detail: "<The computer is off or has no internet.>");
}
```

SmartCHIFA French texts: "La pharmacie ne répond pas" / "Le délai d'attente a expiré, réessayez." /
"Mise à jour de SmartCHIFA nécessaire" / "Mettez à jour SmartCHIFA sur l'ordinateur de la pharmacie pour utiliser cette page." /
"Erreur côté pharmacie" / "Pharmacie hors ligne" / "L'ordinateur de la pharmacie est éteint ou n'a pas d'internet."

## Features/Data (what the phone reads)

```csharp
public record TenantStatus(string Name, bool Online, DateTime? LastSeenAt);

/// <summary>Each call is answered live by the agent; the gateway stores nothing. The tenant comes from the token.</summary>
public static class DataEndpoints
{
    public static void MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/data").RequireAuthorization();

        api.MapGet("/status", StatusAsync); // answered by the gateway itself: online badge, last seen
        api.MapGet("/info", (ClaimsPrincipal user, AgentInvoker agent, CancellationToken ct) =>
            agent.InvokeAsync(user.GetTenantId(), AgentMethods.GetInfo, null, ct));
        api.MapGet("/dashboard", (DateTime? from, DateTime? to, ClaimsPrincipal user, AgentInvoker agent, CancellationToken ct) =>
            agent.InvokeAsync(user.GetTenantId(), AgentMethods.GetDashboard, new PeriodQuery(from, to), ct));

        MapPaged(api, "/orders", AgentMethods.GetOrders);
        api.MapGet("/orders/{id}", (string id, ClaimsPrincipal user, AgentInvoker agent, CancellationToken ct) =>
            agent.InvokeAsync(user.GetTenantId(), AgentMethods.GetOrder, id, ct));

        api.MapGet("/stats/{kind}", async (string kind, DateTime? from, DateTime? to, ClaimsPrincipal user, AgentInvoker agent, CancellationToken ct) =>
            StatsQuery.All.Contains(kind)
                ? await agent.InvokeAsync(user.GetTenantId(), AgentMethods.GetStats, new StatsQuery(kind, from, to), ct)
                : Results.NotFound());
    }

    private static void MapPaged(RouteGroupBuilder api, string route, string method) =>
        api.MapGet(route, (DateTime? from, DateTime? to, int? skip, int? take, string? q, ClaimsPrincipal user, AgentInvoker agent, CancellationToken ct) =>
            agent.InvokeAsync(user.GetTenantId(), method, new PageQuery(from, to, skip ?? 0, take ?? PageQuery.DefaultTake, q), ct));

    private static async Task<IResult> StatusAsync(ClaimsPrincipal user, IDbContextFactory<GatewayDbContext> dbFactory, AgentRegistry registry, CancellationToken ct)
    {
        var tenantId = user.GetTenantId();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.LastSeenAt }).FirstOrDefaultAsync(ct);
        return tenant is null ? Results.NotFound() : Results.Ok(new TenantStatus(tenant.Name, registry.IsOnline(tenantId), tenant.LastSeenAt));
    }
}
```

SmartCHIFA named the group `/api/officine`; pick a domain word for the new product.

## Features/Auth

```csharp
public record LoginRequest(string UserName, string Password);
/// <param name="DeviceToken">Only when a phone is linked: it keeps it to sign in again without a code.</param>
public record LoginResponse(string AccessToken, DateTime ExpiresAt, string Tenant, string? DeviceToken = null);
public record PairRequest(string Code, string? DeviceName);
public record DeviceLoginRequest(string DeviceToken);

public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";
    public const string DeviceIdClaim = "device_id";

    // Verified against when the user does not exist, so both cases take the same time.
    private static readonly string DummyHash = new PasswordHasher<MobileUser>().HashPassword(null!, "not-a-real-password");

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync).RequireRateLimiting(LoginRateLimit).AllowAnonymous();
        app.MapPost("/api/auth/pair", PairAsync).RequireRateLimiting(LoginRateLimit).AllowAnonymous();
        app.MapPost("/api/auth/device", DeviceLoginAsync).RequireRateLimiting(LoginRateLimit).AllowAnonymous();
        app.MapPost("/api/auth/logout", LogoutAsync).RequireAuthorization();
    }
```

- `PairAsync`: normalize the code (`PairingCodes.Normalize`), find a non-expired `PairingCode` of an active tenant by hash
  (400 ProblemDetails "invalid or expired code, show a new one in {ProductName}" otherwise), create a `MobileDevice` with
  `TokenHash = AgentKeys.Hash(token)` where `token = AgentKeys.Generate()`, delete the code, return
  `LoginResponse(jwt, expiresAt, tenant.Name, token)`.
- `DeviceLoginAsync`: find the non-revoked device of an active tenant by token hash (401 otherwise), set `LastSeenAt`, return a new JWT (no device token).
- `LogoutAsync`: if the token has `device_id`, set `RevokedAt` and delete its push subscriptions; 204.
- `LoginAsync`: lower-case user name, `DummyHash` check when unknown (timing), `SuccessRehashNeeded` → rehash; claims `sub`, `name`, `tenant_id`.
- Device claims: `sub` = device id, `name` = device name, `device_id`, `tenant_id`.

```csharp
    /// <summary>JWT check for phones: a device removed in the desktop app or logged out loses access at once.</summary>
    public static async Task RejectRevokedDeviceAsync(TokenValidatedContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue(DeviceIdClaim), out var deviceId))
            return;
        var dbFactory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<GatewayDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        var active = await db.MobileDevices.AnyAsync(
            d => d.Id == deviceId && d.RevokedAt == null && d.Tenant.IsActive, context.HttpContext.RequestAborted);
        if (!active)
            context.Fail("This phone was unlinked.");
    }

    public static string NormalizeUserName(string userName) => userName.Trim().ToLowerInvariant();
    public static SymmetricSecurityKey SigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));

    private static string CreateToken(Claim[] claims, DateTime expiresAt, JwtOptions options) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer, Audience = options.Audience, Expires = expiresAt,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(SigningKey(options), SecurityAlgorithms.HmacSha256)
        });
}
```

Device names come from the phone's user agent (see `mobile.md`, Login page), trimmed to 100 chars, default "Phone".

## Features/Link (desktop app ↔ gateway)

### PairingCodes.cs
```csharp
/// <summary>Eight characters without look-alikes 0/O, 1/I/L, shown as ABCD-2345. 31^8, single use, minutes, rate-limited.</summary>
public static class PairingCodes
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static string Display(string code) => $"{code[..4]}-{code[4..]}";

    /// <summary>Accepts what people type: any case, with or without the dash or spaces.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var code = new string(input.Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());
        return code.Length == Length && code.All(Alphabet.Contains) ? code : null;
    }

    public static string Hash(string normalizedCode) => AgentKeys.Hash(normalizedCode);
}
```

### LicenseTokens.cs (SmartApp license file `SL1.<payload>.<signature>`, ECDSA P-256/SHA-256)
```csharp
public sealed record VerifiedLicense(Guid LicenseId, string? Customer);

public static class LicenseTokens
{
    private const string Prefix = "SL1.";
    private const int FormatVersion = 1;

    public static VerifiedLicense? Verify(string? token, string publicKeyPem, string product, DateTimeOffset now, out string error)
    {
        error = "<Invalid license.>";
        token = token?.Trim();
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length > 16 * 1024)
            return null;
        var separator = token.LastIndexOf('.');
        if (separator <= Prefix.Length)
            return null;

        var signingInput = token[..separator];
        Payload? payload;
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            var signature = Base64Url.DecodeFromChars(token.AsSpan(separator + 1));
            if (!key.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256))
                return null;
            payload = JsonSerializer.Deserialize<Payload>(Base64Url.DecodeFromChars(signingInput.AsSpan(Prefix.Length)), JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            return null;
        }

        if (payload is null || payload.FormatVersion != FormatVersion || payload.LicenseId == Guid.Empty)
            return null;
        if (!string.Equals(payload.Product, product, StringComparison.OrdinalIgnoreCase))
        {
            error = "<This license is not a {ProductName} license.>";
            return null;
        }
        // OfflineUntil is renewed each time the desktop app checks in, so it also catches revoked licenses within days.
        if (payload.ExpiresAt <= now || payload.OfflineUntil <= now)
        {
            error = "<License expired. Check the license online in {ProductName} and try again.>";
            return null;
        }

        error = "";
        return new VerifiedLicense(payload.LicenseId, payload.Customer);
    }

    private sealed record Payload(int FormatVersion, Guid LicenseId, string? Product, string? Customer, DateTimeOffset? ExpiresAt, DateTimeOffset OfflineUntil);
}
```

### LinkEndpoints.cs
```csharp
public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
{
    app.MapPost(LinkRoutes.Register, RegisterAsync).RequireRateLimiting(AuthEndpoints.LoginRateLimit).AllowAnonymous();

    var tenant = app.MapGroup("").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AgentKeyAuthenticationHandler.SchemeName });
    tenant.MapPost(LinkRoutes.Codes, CreateCodeAsync);
    tenant.MapGet(LinkRoutes.Devices, ListDevicesAsync);
    tenant.MapDelete(LinkRoutes.Devices + "/{id:guid}", RemoveDeviceAsync);
}
```

- `RegisterAsync`: verify the license (403 with the reason otherwise). Find the tenant by `LicenseId`: create it
  (name = license customer, max 200) or, if it exists and is active, give it a new agent key and `registry.Disconnect`
  the old agent (reinstall case). Inactive tenant → 403 "disabled, contact SmartApp". Return `RegisterTenantResponse` with the key.
- `CreateCodeAsync`: delete this tenant's codes and all expired ones, insert a new hashed code (retry twice on a unique
  index collision), return `PairingCodeResponse(Display(code), expiresAt, registry.IsOnline(tenantId))`.
- `ListDevicesAsync`: non-revoked devices of the tenant, newest first.
- `RemoveDeviceAsync`: set `RevokedAt` for the device of this tenant; 204 or 404.

## Features/Admin (your own tooling, `X-Admin-Key`)

```csharp
public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
{
    var admin = app.MapGroup("/admin").AllowAnonymous().AddEndpointFilter(RequireAdminKey);
    admin.MapGet("/tenants", ListTenantsAsync);               // + Online from the registry, users count
    admin.MapPost("/tenants", CreateTenantAsync);             // returns the agent key once
    admin.MapPost("/tenants/{id:guid}/agent-key", ReplaceAgentKeyAsync); // + registry.Disconnect(id)
    admin.MapPost("/tenants/{id:guid}/users", CreateUserAsync);          // password >= 8, user name unique (409)
}

private static async ValueTask<object?> RequireAdminKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var http = context.HttpContext;
    var expected = http.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value.AdminKey;
    var provided = http.Request.Headers["X-Admin-Key"].ToString();
    var valid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    return valid ? await next(context) : Results.Unauthorized();
}
```

## Features/Push (optional)

- `GET /api/push/key` → VAPID public key; `POST /api/push/subscribe {endpoint,p256dh,auth}` (endpoint is the identity:
  upsert, set tenant and device); `POST /api/push/unsubscribe {endpoint}`; `POST /api/push/test {endpoint}`.
- Accept only real push services as endpoints (the gateway POSTs to them, so this prevents SSRF):

```csharp
private static readonly string[] PushServiceHosts = ["fcm.googleapis.com", ".push.services.mozilla.com", ".notify.windows.com", ".push.apple.com"];

public static bool IsPushService(Uri uri) =>
    uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
    && PushServiceHosts.Any(h => h.StartsWith('.') ? uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase) : uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));
```

- `PushSender`: `Lib.Net.Http.WebPush` `PushServiceClient` with `VapidAuthentication(public, private) { Subject = PushSubject }`,
  `TimeToLive = 1 day`. Deletes subscriptions answered 404/410. The VAPID pair is created on first use and stored in
  `VapidKeys` (no extra secret to deploy, subscriptions survive restarts):

```csharp
public static VapidKey CreateKey()
{
    using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var p = ec.ExportParameters(includePrivateParameters: true);
    return new VapidKey { Id = 1, PublicKey = Base64Url.EncodeToString([0x04, .. p.Q.X!, .. p.Q.Y!]), PrivateKey = Base64Url.EncodeToString(p.D) };
}
```

- Payload `record PushPayload(string Title, string Body, string Url, string Tag)` serialized with `JsonSerializerOptions.Web`.
- `AlertPoller : BackgroundService`: every `AlertPollMinutes`, load subscriptions of active tenants (device not revoked),
  group by tenant, for each **online** tenant `agent.TryCallAsync<List<AgentAlert>>(id, AgentMethods.GetAlerts, ct)` and send
  each alert to that tenant's subscriptions. Catch and log per iteration so one failure never stops the loop.
  The agent decides what is new; the gateway keeps nothing but subscriptions.
