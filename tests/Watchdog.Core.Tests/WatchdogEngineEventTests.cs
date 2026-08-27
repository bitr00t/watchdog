using Xunit;

namespace Watchdog.Core.Tests;

public sealed class WatchdogEngineEventTests
{
    [Fact]
    public async Task RoundCompleted_is_raised_once_per_round()
    {
        var engine = new WatchdogEngine(
            FakeEndpointProbe.AlwaysSucceeding(),
            new WatchdogOptions { Interval = TimeSpan.FromMilliseconds(10), Rounds = 3 });

        var rounds = new List<int>();
        engine.RoundCompleted += (_, eventArgs) => rounds.Add(eventArgs.Round.Number);

        await DrainAsync(engine, Endpoints("a"));

        Assert.Equal(new[] { 1, 2, 3 }, rounds.ToArray());
    }

    [Fact]
    public async Task StatusChanged_reports_the_first_observation_as_a_transition()
    {
        var engine = new WatchdogEngine(
            FakeEndpointProbe.AlwaysSucceeding(),
            new WatchdogOptions { Interval = TimeSpan.FromMilliseconds(10), Rounds = 2 });

        var transitions = new List<(CheckStatus Previous, CheckStatus Current)>();
        engine.StatusChanged += (_, eventArgs) => transitions.Add((eventArgs.Previous, eventArgs.Current));

        await DrainAsync(engine, Endpoints("a"));

        // Two rounds, but only the first one changes anything.
        Assert.Equal(new[] { (CheckStatus.Unknown, CheckStatus.Healthy) }, transitions.ToArray());
    }

    [Fact]
    public async Task StatusChanged_is_raised_on_every_flip_in_both_directions()
    {
        var outcomes = new Queue<bool>(new[] { true, false, false, true });

        var probe = new FakeEndpointProbe((endpoint, _) => Task.FromResult(
            outcomes.Dequeue()
                ? FakeEndpointProbe.Success(endpoint)
                : FakeEndpointProbe.Failure(endpoint)));

        var engine = new WatchdogEngine(
            probe,
            new WatchdogOptions { Interval = TimeSpan.FromMilliseconds(10), Rounds = 4 });

        var transitions = new List<(CheckStatus Previous, CheckStatus Current)>();
        engine.StatusChanged += (_, eventArgs) => transitions.Add((eventArgs.Previous, eventArgs.Current));

        await DrainAsync(engine, Endpoints("a"));

        Assert.Equal(
            new[]
            {
                (CheckStatus.Unknown, CheckStatus.Healthy),
                (CheckStatus.Healthy, CheckStatus.Failing),
                (CheckStatus.Failing, CheckStatus.Healthy),
            },
            transitions.ToArray());
    }

    [Fact]
    public async Task Every_subscriber_of_a_multicast_event_is_invoked()
    {
        var engine = new WatchdogEngine(
            FakeEndpointProbe.AlwaysSucceeding(),
            new WatchdogOptions { Rounds = 1 });

        var first = 0;
        var second = 0;

        engine.RoundCompleted += (_, _) => first++;
        engine.RoundCompleted += (_, _) => second++;

        await DrainAsync(engine, Endpoints("a"));

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public async Task A_removed_handler_is_no_longer_invoked()
    {
        var engine = new WatchdogEngine(
            FakeEndpointProbe.AlwaysSucceeding(),
            new WatchdogOptions { Interval = TimeSpan.FromMilliseconds(10), Rounds = 2 });

        var calls = 0;

        // The handler has to be stored in a variable: -= compares delegates, and two lambdas
        // written out separately are never equal even when their bodies are identical.
        void Handler(object? sender, RoundCompletedEventArgs eventArgs)
        {
            calls++;

            if (eventArgs.Round.Number == 1)
            {
                engine.RoundCompleted -= Handler;
            }
        }

        engine.RoundCompleted += Handler;

        await DrainAsync(engine, Endpoints("a"));

        Assert.Equal(1, calls);
    }

    private static async Task DrainAsync(WatchdogEngine engine, IReadOnlyList<EndpointConfig> endpoints)
    {
        await foreach (var _ in engine.RunAsync(endpoints))
        {
        }
    }

    private static EndpointConfig[] Endpoints(params string[] ids) =>
        [.. ids.Select(id => new EndpointConfig
        {
            Id = new CheckId(id),
            Url = new Uri($"https://localhost/{id}"),
        })];
}
