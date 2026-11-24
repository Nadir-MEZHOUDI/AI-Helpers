# Agent (MyApp.Agent) and the desktop-app side

The agent is a `BackgroundService` that keeps one outbound SignalR connection to the gateway and answers the methods
listed in `AgentMethods` with the desktop solution's own services. The same class runs in two hosts:

1. **Inside the desktop app** (default): started at app startup when the tenant is linked.
2. **As a Windows service** (optional): answers phones while the desktop app is closed. Both read the same settings file
   in `%ProgramData%\{ProgramDataDir}\mobile.json`; when the service runs, the desktop app does not start a second agent.

```xml
<!-- MyApp.Agent.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>  <!-- net10.0-windows if the services need it -->
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyApp.Contract\MyApp.Contract.csproj" />
    <ProjectReference Include="..\MyApp.Services\MyApp.Services.csproj" /> <!-- the desktop app's services/DTOs -->
  </ItemGroup>
  <ItemGroup>
    <None Update="install-agent.ps1" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

The agent must reference the **services library**, not the desktop UI project. If the desktop app keeps its queries in
forms/view models, first move what the phone needs into a services class library (with DTOs) that both use.

## AgentOptions.cs

```csharp
public class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>Written by the desktop app when the tenant links its phones. Holds the <c>Agent</c> section, so the Windows service uses the same key.</summary>
    public static string SharedSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "{ProgramDataDir}", "mobile.json");

    [Required, Url] public string GatewayUrl { get; set; } = string.Empty;
    [Required] public string AgentKey { get; set; } = string.Empty;
}

/// <summary>Options set in code rather than read from configuration: the desktop app hosting the agent, tests.</summary>
public sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
```

If the local DB connection depends on a per-user environment variable, add a `DbOptions` section for the service:
a service runs as LocalSystem and does not see user variables.

## ForeverRetryPolicy.cs

```csharp
/// <summary>The default policy gives up after 4 attempts (~45 s); a site may lose internet for hours. This one retries forever.</summary>
public class ForeverRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => DelayFor(retryContext.PreviousRetryCount);

    public static TimeSpan DelayFor(long previousAttempts)
    {
        var delay = previousAttempts < Delays.Length ? Delays[previousAttempts] : MaxDelay;
        // Jitter, so hundreds of agents do not all reconnect in the same second after a gateway restart.
        return delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
    }
}
```

## AgentWorker.cs

```csharp
/// <summary>
/// Keeps one outbound connection to the gateway, so the site needs no open port. Short drops are handled by
/// SignalR's automatic reconnect; this loop restarts the connection when it is fully closed
/// (gateway down at startup, key rejected, reconnect aborted).
/// </summary>
public class AgentWorker(IOptionsMonitor<AgentOptions> options, AgentHandlers handlers, ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = BuildConnection();
        long failedStarts = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = CheckInterval;
            if (connection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await connection.StartAsync(stoppingToken);
                    logger.LogInformation("Connected to the gateway {Url}", options.CurrentValue.GatewayUrl);
                    failedStarts = 0;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    logger.LogError("The gateway rejected the agent key.");
                    wait = TimeSpan.FromMinutes(1);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    wait = ForeverRetryPolicy.DelayFor(failedStarts++);
                    logger.LogWarning("Gateway unreachable ({Error}), next try in {Delay:N0} s", ex.Message, wait.TotalSeconds);
                }
            }

            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        await connection.StopAsync(CancellationToken.None);
    }

    private HubConnection BuildConnection()
    {
        var url = new Uri(new Uri(options.CurrentValue.GatewayUrl), AgentHub.Route);
        var connection = new HubConnectionBuilder()
            // Read on every (re)connection: a key replaced by the desktop app is picked up without a restart.
            .WithUrl(url, o => o.AccessTokenProvider = () => Task.FromResult<string?>(options.CurrentValue.AgentKey))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .WithKeepAliveInterval(TimeSpan.FromSeconds(15))
            .WithServerTimeout(TimeSpan.FromSeconds(30))
            .Build();

        handlers.Register(connection);
        connection.Reconnecting += ex => { logger.LogWarning("Connection lost ({Error}), reconnecting...", ex?.Message); return Task.CompletedTask; };
        connection.Reconnected += _ => { logger.LogInformation("Reconnected to the gateway"); return Task.CompletedTask; };
        connection.Closed += ex => { logger.LogWarning("Connection closed ({Error})", ex?.Message); return Task.CompletedTask; };
        return connection;
    }
}
```

## AgentHandlers.cs

```csharp
/// <summary>Answers the gateway with the same services the desktop app uses. Only the methods registered here can be called.</summary>
public class AgentHandlers(ILogger<AgentHandlers> logger)
{
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
    private readonly AlertTracker _alerts = new(logger); // only with push

    public void Register(HubConnection connection)
    {
        connection.On(AgentMethods.GetInfo, () => RunAsync(AgentMethods.GetInfo, GetInfoAsync));

        connection.On<PeriodQuery, DashboardDto>(AgentMethods.GetDashboard, q =>
            RunAsync(AgentMethods.GetDashboard, () => StatsService.Instance.GetDashboardAsync(ToPeriod(q.From, q.To))));

        // Paged list: the service returns everything for the period, the agent pages it.
        connection.On<PageQuery, Page<OrderDto>>(AgentMethods.GetOrders, q =>
            RunAsync(AgentMethods.GetOrders, async () =>
                ToPage((await OrderService.Instance.GetOrdersAsync(ToPeriod(q.From, q.To))).OrderByDescending(o => o.Date), q)));

        // Search: the service pages in SQL and returns (total, items).
        connection.On<PageQuery, Page<CustomerRowDto>>(AgentMethods.SearchCustomers, q =>
            RunAsync(AgentMethods.SearchCustomers, async () =>
            {
                var (total, items) = await CustomerService.Instance.SearchAsync(q.Text, q.SafeSkip, q.SafeTake);
                return new Page<CustomerRowDto>(total, items);
            }));

        connection.On<string, OrderFullDto?>(AgentMethods.GetOrder, id =>
            RunAsync(AgentMethods.GetOrder, () => OrderService.Instance.GetOrderAsync(id)));

        connection.On<List<AgentAlert>>(AgentMethods.GetAlerts, () => RunAsync(AgentMethods.GetAlerts, _alerts.GetNewAlertsAsync));
    }

    private static async Task<AgentInfo> GetInfoAsync() =>
        new(await SettingsService.Instance.GetTenantNameAsync(), Version, DateTime.Now);

    public static Page<T> ToPage<T>(IEnumerable<T> items, PageQuery query)
    {
        var all = items as IReadOnlyCollection<T> ?? items.ToList();
        return new Page<T>(all.Count, all.Skip(query.SafeSkip).Take(query.SafeTake).ToList());
    }

    /// <summary>
    /// The SignalR client sends an exception's message to the gateway as is. Log the real error here
    /// and send a message that is safe to show on the phone.
    /// </summary>
    private async Task<T> RunAsync<T>(string method, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException) // or SqlException...
        {
            logger.LogError(ex, "{Method}: database unavailable", method);
            throw new HubException("<The database is not answering on the computer.>");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Method} failed", method);
            throw new HubException("<Internal agent error.>");
        }
    }
}
```

`connection.On<TArg, TResult>` with a `Func<TArg, Task<TResult>>` is the overload that returns a value to the gateway.
Argument records must be the **same contract types** the gateway sends (serialized as JSON, camelCase).

## Windows service host (Program.cs)

```csharp
var builder = Host.CreateApplicationBuilder(args);

// As a Windows service the working directory is System32: read appsettings.json next to the exe.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile(AgentOptions.SharedSettingsPath, optional: true, reloadOnChange: true);
builder.Services.AddWindowsService(o => o.ServiceName = "MyApp.Agent");

builder.Services.AddOptions<AgentOptions>().BindConfiguration(AgentOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<AgentHandlers>();
builder.Services.AddHostedService<AgentWorker>();
// configure the local DB connection string here (from a "Db" section when the service cannot see user env vars)

await builder.Build().RunAsync();
```

`appsettings.json`: `Agent.GatewayUrl` = `https://{gateway-host}`, `Agent.AgentKey` empty (comes from `mobile.json`),
`Logging.LogLevel` `Microsoft.AspNetCore.SignalR` and `Microsoft.AspNetCore.Http.Connections` at Warning.

### Ship the agent next to the desktop app (desktop .csproj)

```xml
<Target Name="BuildAgentForService" AfterTargets="Build" Condition="'$(DesignTimeBuild)' != 'true'">
  <MSBuild Projects="..\MyApp.Agent\MyApp.Agent.csproj" Targets="Publish"
           Properties="Configuration=$(Configuration);RuntimeIdentifier=win-x64;SelfContained=false;PublishDir=$(OutDir)agent\" />
</Target>
<Target Name="PublishAgentForService" AfterTargets="Publish">
  <MSBuild Projects="..\MyApp.Agent\MyApp.Agent.csproj" Targets="Publish"
           Properties="Configuration=$(Configuration);RuntimeIdentifier=win-x64;SelfContained=false;PublishDir=$(PublishDir)agent\" />
</Target>
```

### install-agent.ps1 (in the agent project, copied to output)

Windows PowerShell 5.1 compatible (what every customer PC has), `#Requires -RunAsAdministrator`. The desktop app runs it with
`Verb = "runas"` (one UAC prompt; exit code 1223 = cancelled). Steps:

1. Transcript to `%ProgramData%\{ProgramDataDir}\agent-install.log`.
2. Keep the previous `appsettings.json` DB section if any; stop and `sc.exe delete` the old service, wait until gone.
3. `-Uninstall`: remove the folder and stop.
4. Copy the shipped `agent\` folder to `%ProgramFiles%\{ProductName}\Agent` (the app folder is replaced at each update, and
   only administrators may change what a LocalSystem service runs).
5. Patch DB host/port in `appsettings.json` from parameters.
6. `sc.exe create MyApp.Agent binPath= "<exe>" start= delayed-auto DisplayName= '...'`, `sc.exe description`,
   `sc.exe failure MyApp.Agent reset= 86400 actions= restart/5000/restart/10000/restart/60000`.
7. `Start-Service` and wait for Running (30 s).

Desktop side `AgentService` helper: `State` = NotInstalled / Stopped / Running / **Outdated** (the installed exe version is older
than the shipped one → offer "update the service" after each app update), `InstallAsync()`, `UninstallAsync()`.

## Desktop app side (MobileLink.cs + a form)

```csharp
/// <summary>
/// Links phones to this tenant. On first use the app registers with its signed license and receives the agent key;
/// it keeps it in AgentOptions.SharedSettingsPath, runs the agent, and asks the gateway for one-time pairing codes.
/// When the Windows service runs, it reads the same file and the app does not start a second agent.
/// </summary>
internal static class MobileLink
{
    private const string DefaultGatewayUrl = "https://{gateway-host}";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim RegisterLock = new(1, 1);
    private static AgentWorker? _agent;

    /// <summary>MYAPP_GATEWAY_URL points to a test server (the local E2E harness).</summary>
    public static string GatewayUrl =>
        (Environment.GetEnvironmentVariable("MYAPP_GATEWAY_URL") is { Length: > 0 } url ? url : DefaultGatewayUrl).TrimEnd('/');

    /// <summary>Opened by the QR code: the phone app fills the code in by itself.</summary>
    public static string PhoneUrl(string code) => $"{GatewayUrl}/login?code={Uri.EscapeDataString(code)}";

    public static void StartAgent() { if (Load() is { } s) RestartAgent(s); }   // call at app startup
    public static async Task StopAgentAsync() { /* Interlocked.Exchange(ref _agent, null), StopAsync, Dispose */ }

    public static Task<PairingCodeResponse> NewCodeAsync() => SendAsync<PairingCodeResponse>(HttpMethod.Post, LinkRoutes.Codes);
    public static Task<List<LinkedDevice>> DevicesAsync() => SendAsync<List<LinkedDevice>>(HttpMethod.Get, LinkRoutes.Devices);
    public static Task RemoveDeviceAsync(Guid id) => SendAsync<object>(HttpMethod.Delete, $"{LinkRoutes.Devices}/{id}");
```

- `SendAsync`: load settings (or register when missing or for another gateway URL), call with `Bearer <agentKey>`;
  on **401** register again once (key replaced elsewhere) and retry. Map `HttpRequestException`/`TaskCanceledException` to a
  user-facing "server unreachable, check this PC's internet" exception; non-success to "server answered an error (code)".
- `RegisterAsync` (under `RegisterLock`): read the license file (no license → explain), POST `LinkRoutes.Register`; 403 →
  show the ProblemDetails `title`; save `{ Agent: { GatewayUrl, AgentKey }, TenantId, Tenant }` atomically
  (write `.tmp` then `File.Move(overwrite)`), then `RestartAgent`.
- `RestartAgent`: on a background task, stop the current agent; if the Windows service runs, stop there; otherwise build
  `new AgentWorker(new FixedOptionsMonitor<AgentOptions>(...), new AgentHandlers(logger), logger)` with the app's logger
  (Serilog → `SerilogLoggerFactory`) and `StartAsync`.
- Call `MobileLink.StartAgent()` once the main window is up, and `StopAgentAsync()` on exit.

The form ("Application mobile"): a big QR code of `PhoneUrl(code)` (QRCoder), the code in large letters with its expiry
countdown, "new code" button, whether the agent runs in the app or the service (with install/update service button),
and the list of linked devices with a "remove" button. Steps text: "1. On the phone, scan the QR code or open {gateway-host}…".

## AlertTracker (only with push)

The agent decides what is new; the gateway keeps nothing. Pattern:

- State file `%ProgramData%\{ProgramDataDir}\alerts.json` shared by the app and the service, so a restart or switch never repeats an alert.
- `GetNewAlertsAsync()` under a `SemaphoreSlim`: load state once, query the services, call a **pure** `Compute(state, data..., now)`
  that returns the alerts and updates the state, then save (best effort: `.tmp` + move; log IO errors).
- **First run only records** what already exists, so installing the agent does not report months of history.
- Above N new items, send one summary alert instead of N.
- Daily alerts (e.g. reminders) keep `LastDay` in the state and do not fire before a set hour.
- Each alert has a stable `Key` (e.g. `paid:<ids>`, `chronic:2026-11-24`) used as the notification tag.
- Unit-test `Compute` directly (first run, new item, many items, daily once).
