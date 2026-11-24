using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyApp.Contract;
using MyApp.Gateway.Data;
using MyApp.Gateway.Features.Admin;
using MyApp.Gateway.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyApp.Tests.Gateway;

/// <summary>
/// The real gateway on Kestrel (real HTTP and WebSockets, like in production) with an in-memory SQLite database.
/// </summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    public const string AdminKey = "test-admin-key-0123456789-0123456789";
    public const int AgentTimeoutSeconds = 2;

    public const string LicenseProduct = "{product-slug}";

    /// <summary>Stands in for the SmartApp licensing server's signing key.</summary>
    public static readonly ECDsa LicenseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private readonly SqliteConnection _db = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;

    public Uri BaseAddress { get; private set; } = null!;
    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await _db.OpenAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Gateway", "unused");
            b.UseSetting("Gateway:AdminKey", AdminKey);
            b.UseSetting("Gateway:AgentTimeoutSeconds", AgentTimeoutSeconds.ToString());
            b.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-0123456789-0123456789");
            b.UseSetting("Gateway:LicensePublicKey", LicenseKey.ExportSubjectPublicKeyInfoPem());
            // Every test signs in from 127.0.0.1.
            b.UseSetting("Gateway:AuthRequestsPerMinute", "10000");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IDbContextOptionsConfiguration<GatewayDbContext>>();
                s.RemoveAll<DbContextOptions<GatewayDbContext>>();
                s.RemoveAll<IDbContextFactory<GatewayDbContext>>();
                s.AddDbContextFactory<GatewayDbContext>(o => o.UseSqlite(_db));
            });
        });
        _factory.UseKestrel(0);
        _factory.StartServer();
        BaseAddress = _factory.ClientOptions.BaseAddress;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AdminEndpoints.AdminKeyHeader, AdminKey);
        return client;
    }

    /// <summary>A new pharmacy with one phone user, as the admin would set it up.</summary>
    public async Task<TestPharmacy> CreatePharmacyAsync()
    {
        using var admin = CreateAdminClient();
        var name = $"Pharmacie {Guid.NewGuid():N}";
        var created = await (await admin.PostAsJsonAsync("/admin/pharmacies", new CreatePharmacyRequest(name)))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<AgentKeyResponse>();

        var userName = $"user-{Guid.NewGuid():N}";
        const string password = "Mot2Passe!";
        (await admin.PostAsJsonAsync($"/admin/pharmacies/{created!.Id}/users", new CreateUserRequest(userName, password)))
            .EnsureSuccessStatusCode();

        return new TestPharmacy(created.Id, created.Name, created.AgentKey, userName, password);
    }

    /// <summary>An HTTP client logged in as the pharmacy's phone user.</summary>
    public async Task<HttpClient> LoginAsync(TestPharmacy pharmacy)
    {
        var client = CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(pharmacy.UserName, pharmacy.Password)))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    /// <summary>A license file as the SmartApp licensing server issues it (<c>SL1.payload.signature</c>).</summary>
    public static string CreateLicense(
        Guid? licenseId = null,
        string? customer = "Pharmacie Test",
        string product = LicenseProduct,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? offlineUntil = null,
        ECDsa? signingKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            licenseId = licenseId ?? Guid.NewGuid(),
            product,
            serialHint = "ABCDE",
            customer,
            edition = "standard",
            features = Array.Empty<string>(),
            machineId = "machine",
            issuedAt = now,
            expiresAt = expiresAt ?? now.AddYears(1),
            refreshAfter = now.AddDays(1),
            offlineUntil = offlineUntil ?? now.AddDays(30)
        });
        var signingInput = "SL1." + Base64Url.EncodeToString(payload);
        var signature = (signingKey ?? LicenseKey).SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }

    /// <summary>What the desktop app does on first use of « Application mobile ».</summary>
    public async Task<RegisterPharmacyResponse> RegisterAsync(string license)
    {
        using var client = CreateClient();
        return (await (await client.PostAsJsonAsync(LinkRoutes.Register, new RegisterPharmacyRequest(license)))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<RegisterPharmacyResponse>())!;
    }

    /// <summary>the desktop app's calls with its agent key.</summary>
    public HttpClient CreatePharmacyClient(string agentKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentKey);
        return client;
    }

    /// <summary>A minimal agent: the test registers the handlers it needs before starting it.</summary>
    public HubConnection CreateAgentConnection(string agentKey) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(BaseAddress, AgentHub.Route), o => o.AccessTokenProvider = () => Task.FromResult<string?>(agentKey))
            .Build();

    public async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(100);
        }
    }
}

public record TestPharmacy(Guid Id, string Name, string AgentKey, string UserName, string Password);

[CollectionDefinition(Name)]
public class GatewayCollection : ICollectionFixture<GatewayFixture>
{
    public const string Name = "Gateway";
}
