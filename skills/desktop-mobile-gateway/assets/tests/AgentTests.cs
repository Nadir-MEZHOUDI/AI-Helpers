using MyApp.Agent;
using MyApp.Contract;
using Microsoft.AspNetCore.SignalR.Client;

namespace MyApp.Tests.Gateway;

public class AgentTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(4, 30)]
    [InlineData(5, 60)]
    [InlineData(10_000, 60)]
    public void Retry_policy_never_gives_up_and_waits_at_most_a_minute(long previousRetries, int expectedSeconds)
    {
        var delay = new ForeverRetryPolicy().NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetries });

        delay.Should().NotBeNull();
        delay!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(expectedSeconds))
            .And.BeLessThan(TimeSpan.FromSeconds(expectedSeconds + 1));
    }

    [Fact]
    public void Page_is_clamped_to_the_maximum_size()
    {
        var page = AgentHandlers.ToPage(Enumerable.Range(1, 500), new PageQuery(null, null, Skip: -5, Take: 10_000));

        page.Total.Should().Be(500);
        page.Items.Should().HaveCount(PageQuery.MaxTake);
        page.Items[0].Should().Be(1);
    }

    [Fact]
    public void Page_skips_and_takes()
    {
        var page = AgentHandlers.ToPage(Enumerable.Range(1, 30), new PageQuery(null, null, Skip: 20, Take: 25));

        page.Total.Should().Be(30);
        page.Items.Should().Equal(Enumerable.Range(21, 10));
    }

    [Fact]
    public void Missing_from_keeps_the_default_period_start()
    {
        var period = AgentHandlers.ToPeriod(new PeriodQuery(null, new DateTime(2026, 11, 22)));

        period.From.Should().Be(DateTime.Today.AddYears(-2));
        period.To.Should().Be(new DateTime(2026, 11, 22));
    }
}
