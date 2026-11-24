using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyApp.Agent;
using MyApp.Contract;
using MyApp.Gateway.Features.Admin;
using MyApp.Gateway.Features.Agents;
using MyApp.Gateway.Features.Auth;
using MyApp.Gateway.Features.Officine;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MyApp.Tests.Gateway;

[Collection(GatewayCollection.Name)]
public class GatewayTests(GatewayFixture gateway)
{
    private record Row(string Num, decimal Montant);

    [Fact]
    public async Task Phone_request_is_answered_by_the_pharmacy_agent()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        PageQuery? received = null;
        await using var agent = gateway.CreateAgentConnection(pharmacy.AgentKey);
        agent.On<PageQuery, Page<Row>>(AgentMethods.GetRejets, q =>
        {
            received = q;
            return Task.FromResult(new Page<Row>(1, [new Row("B-001", 1250.50m)]));
        });
        await agent.StartAsync();
        using var phone = await gateway.LoginAsync(pharmacy);

        var response = await phone.GetAsync("/api/officine/rejets?from=2026-11-01&to=2026-11-22&skip=0&take=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<Page<Row>>(JsonSerializerOptions.Web);
        page!.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Should().Be(new Row("B-001", 1250.50m));
        received.Should().Be(new PageQuery(new DateTime(2026, 11, 1), new DateTime(2026, 11, 22), 0, 10));
    }

    [Fact]
    public async Task Phone_only_reaches_its_own_pharmacy()
    {
        var mine = await gateway.CreatePharmacyAsync();
        var other = await gateway.CreatePharmacyAsync();
        await using var myAgent = AgentAnsweringInfo(mine.AgentKey, "Mine");
        await using var otherAgent = AgentAnsweringInfo(other.AgentKey, "Other");
        await myAgent.StartAsync();
        await otherAgent.StartAsync();
        using var phone = await gateway.LoginAsync(mine);

        var info = await phone.GetFromJsonAsync<AgentInfo>("/api/officine/info", JsonSerializerOptions.Web);

        info!.Officine.Should().Be("Mine");
    }

    [Fact]
    public async Task Offline_pharmacy_returns_503()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        using var phone = await gateway.LoginAsync(pharmacy);

        var response = await phone.GetAsync("/api/officine/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var status = await phone.GetFromJsonAsync<OfficineStatus>("/api/officine/status", JsonSerializerOptions.Web);
        status!.Online.Should().BeFalse();
    }

    [Fact]
    public async Task Agent_error_returns_424_with_the_agent_message()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        await using var agent = gateway.CreateAgentConnection(pharmacy.AgentKey);
        agent.On<PeriodQuery, object>(AgentMethods.GetDashboard, _ => throw new HubException("Base CHIFA indisponible"));
        await agent.StartAsync();
        using var phone = await gateway.LoginAsync(pharmacy);

        var response = await phone.GetAsync("/api/officine/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.FailedDependency);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Base CHIFA indisponible");
    }

    [Fact]
    public async Task Stats_routes_pass_the_kind_and_period_and_refuse_unknown_kinds()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        StatsQuery? asked = null;
        await using var agent = gateway.CreateAgentConnection(pharmacy.AgentKey);
        agent.On<StatsQuery, object>(AgentMethods.GetStats, q => { asked = q; return Task.FromResult<object>(new { }); });
        agent.On(AgentMethods.GetRecentDays, () => Task.FromResult<object>(Array.Empty<object>()));
        await agent.StartAsync();
        using var phone = await gateway.LoginAsync(pharmacy);

        (await phone.GetAsync("/api/officine/stats/mois?from=2026-01-01")).EnsureSuccessStatusCode();
        (await phone.GetAsync("/api/officine/stats/recent")).EnsureSuccessStatusCode();
        var unknown = await phone.GetAsync("/api/officine/stats/drop-table");

        asked.Should().Be(new StatsQuery("mois", new DateTime(2026, 1, 1), null));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/officine/factures/61001126")]
    [InlineData("/api/officine/patients/relance")]
    public async Task Agent_without_the_method_asks_for_an_update(string url)
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        await using var agent = gateway.CreateAgentConnection(pharmacy.AgentKey);
        await agent.StartAsync();
        using var phone = await gateway.LoginAsync(pharmacy);

        var response = await phone.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.FailedDependency);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Mise à jour de MyProduct");
    }

    [Fact]
    public async Task Slow_agent_returns_503()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        await using var agent = gateway.CreateAgentConnection(pharmacy.AgentKey);
        agent.On<PeriodQuery, object>(AgentMethods.GetDashboard, async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(GatewayFixture.AgentTimeoutSeconds + 3));
            return new object();
        });
        await agent.StartAsync();
        using var phone = await gateway.LoginAsync(pharmacy);

        var response = await phone.GetAsync("/api/officine/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ne répond pas");
    }

    [Fact]
    public async Task Agent_with_unknown_key_is_rejected()
    {
        await using var agent = gateway.CreateAgentConnection("not-a-valid-key");

        var start = () => agent.StartAsync();

        (await start.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Real_agent_reconnects_by_itself_after_the_connection_drops()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        var registry = gateway.Services.GetRequiredService<AgentRegistry>();
        using var worker = CreateWorker(pharmacy.AgentKey);
        await worker.StartAsync(CancellationToken.None);

        try
        {
            await gateway.WaitUntilAsync(() => Task.FromResult(registry.IsOnline(pharmacy.Id)));
            var firstConnection = registry.GetConnectionId(pharmacy.Id);

            registry.Disconnect(pharmacy.Id);

            await gateway.WaitUntilAsync(() => Task.FromResult(
                registry.IsOnline(pharmacy.Id) && registry.GetConnectionId(pharmacy.Id) != firstConnection));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Replacing_the_agent_key_disconnects_the_old_agent_for_good()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        var registry = gateway.Services.GetRequiredService<AgentRegistry>();
        using var worker = CreateWorker(pharmacy.AgentKey);
        await worker.StartAsync(CancellationToken.None);

        try
        {
            await gateway.WaitUntilAsync(() => Task.FromResult(registry.IsOnline(pharmacy.Id)));
            using var admin = gateway.CreateAdminClient();

            (await admin.PostAsync($"/admin/pharmacies/{pharmacy.Id}/agent-key", null)).EnsureSuccessStatusCode();
            await Task.Delay(TimeSpan.FromSeconds(4));

            registry.IsOnline(pharmacy.Id).Should().BeFalse();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Admin_endpoints_require_the_admin_key()
    {
        using var client = gateway.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/pharmacies", new CreatePharmacyRequest("Intrus"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_is_refused()
    {
        var pharmacy = await gateway.CreatePharmacyAsync();
        using var client = gateway.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(pharmacy.UserName, "wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Phone_api_requires_a_token()
    {
        using var client = gateway.CreateClient();

        var response = await client.GetAsync("/api/officine/status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/rejets")]
    public async Task Phone_app_is_served_on_its_routes(string path)
    {
        using var client = gateway.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("_framework/blazor.webassembly.js");
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/admin/unknown")]
    public async Task Unknown_api_paths_are_404_not_the_phone_app(string path)
    {
        using var client = gateway.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HubConnection AgentAnsweringInfo(string agentKey, string officine)
    {
        var agent = gateway.CreateAgentConnection(agentKey);
        agent.On(AgentMethods.GetInfo, () => Task.FromResult(new AgentInfo(officine, "test", DateTime.Now)));
        return agent;
    }

    private AgentWorker CreateWorker(string agentKey) =>
        new(new FixedOptionsMonitor<AgentOptions>(new AgentOptions { GatewayUrl = gateway.BaseAddress.ToString(), AgentKey = agentKey }),
            new AgentHandlers(NullLogger<AgentHandlers>.Instance),
            NullLogger<AgentWorker>.Instance);
}
