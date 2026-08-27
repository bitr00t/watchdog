using Xunit;

using static Watchdog.Core.Tests.TestData;

namespace Watchdog.Core.Tests;

public sealed class CheckHistoryTests
{
    [Fact]
    public void For_returns_an_empty_list_for_an_unknown_endpoint()
    {
        var history = new CheckHistory();

        Assert.Empty(history.For(new CheckId("never-seen")));
    }

    [Fact]
    public void Add_keeps_the_results_of_different_endpoints_apart()
    {
        var history = new CheckHistory();

        history.Add(Result("a", success: true));
        history.Add(Result("b", success: false));
        history.Add(Result("a", success: true));

        Assert.Equal(2, history.For(new CheckId("a")).Count);
        Assert.Single(history.For(new CheckId("b")));
        Assert.Equal(3, history.All.Count());
    }

    [Fact]
    public void Add_drops_the_oldest_result_once_the_capacity_is_reached()
    {
        var history = new CheckHistory(capacityPerEndpoint: 3);

        for (var latency = 1; latency <= 5; latency++)
        {
            history.Add(Result("a", success: true, latencyMs: latency));
        }

        var retained = history.For(new CheckId("a"));

        Assert.Equal(3, retained.Count);
        Assert.Equal(
            new[] { 3.0, 4.0, 5.0 },
            retained.Select(result => result.Latency.Milliseconds).ToArray());
    }

    [Fact]
    public void Add_accepts_a_whole_round()
    {
        var history = new CheckHistory();

        history.Add(new CheckRound
        {
            Number = 1,
            StartedAt = DateTimeOffset.UtcNow,
            Results = [Result("a", success: true), Result("b", success: false)],
        });

        Assert.Equal(2, history.Ids.Count);
    }

    [Fact]
    public void Constructor_rejects_a_capacity_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CheckHistory(0));
    }
}
