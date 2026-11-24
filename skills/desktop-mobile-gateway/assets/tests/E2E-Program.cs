// Local end-to-end harness: the real gateway (SQLite file) on http://localhost:5080 and an agent on the local database.
using System.Net.Http.Json;
using MyApp.Agent;
using MyApp.Gateway.Data;
using MyApp.Gateway.Features.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

const string adminKey = "e2e-admin-key-0123456789-0123456789-xyz";
var dbFile = Path.Combine(AppContext.BaseDirectory, "gateway-e2e.db");
File.Delete(dbFile);

var factory = new WebApplicationFactory<MyApp.Gateway.GatewayOptions>().WithWebHostBuilder(b =>
{
    b.UseEnvironment("Development");
    b.UseSetting("ConnectionStrings:Gateway", "unused");
    b.UseSetting("Gateway:AdminKey", adminKey);
    b.UseSetting("Gateway:AlertPollMinutes", "1");
    b.UseSetting("Jwt:SigningKey", "e2e-signing-key-0123456789-0123456789-0123456789");
    b.ConfigureServices(s =>
    {
        s.RemoveAll<IDbContextOptionsConfiguration<GatewayDbContext>>();
        s.RemoveAll<DbContextOptions<GatewayDbContext>>();
        s.RemoveAll<IDbContextFactory<GatewayDbContext>>();
        s.AddDbContextFactory<GatewayDbContext>(o => o.UseSqlite($"DataSource={dbFile}"));
    });
});
factory.UseKestrel(5080);
factory.StartServer();
var baseAddress = factory.ClientOptions.BaseAddress;
Console.WriteLine($"Listening on {baseAddress}");

using var admin = new HttpClient { BaseAddress = baseAddress };
admin.DefaultRequestHeaders.Add(AdminEndpoints.AdminKeyHeader, adminKey);
var pharmacy = (await (await admin.PostAsJsonAsync("/admin/pharmacies", new CreatePharmacyRequest("Pharmacie de test")))
    .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<AgentKeyResponse>())!;
var password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
(await admin.PostAsJsonAsync($"/admin/pharmacies/{pharmacy.Id}/users", new CreateUserRequest("e2e", password))).EnsureSuccessStatusCode();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "credentials.txt"), $"user: e2e\npassword: {password}\n");

var loggers = LoggerFactory.Create(l => l.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
var agent = new AgentWorker(
    new FixedOptionsMonitor<AgentOptions>(new AgentOptions { GatewayUrl = baseAddress.ToString(), AgentKey = pharmacy.AgentKey }),
    new AgentHandlers(loggers.CreateLogger<AgentHandlers>()),
    loggers.CreateLogger<AgentWorker>());
await agent.StartAsync(CancellationToken.None);

Console.WriteLine($"READY {baseAddress}");
await Task.Delay(Timeout.Infinite);
