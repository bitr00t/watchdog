using Xunit;

namespace Watchdog.Core.Tests;

public sealed class WatchdogEngineTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void RunAsync_rejects_an_empty_endpoint_list_immediately()
    {
        var engine = new WatchdogEngine(FakeEndpointProbe.AlwaysSucceeding());

        // The exception has to surface on the call itself, not on the first iteration.
        // That only works because RunAsync is not an iterator method.
        Assert.Throws<ArgumentException>(() => engine.RunAsync([]));
    }

    [Fact]
    public async Task RunAsync_stops_after_the_configured_number_of_rounds()
    {
        var probe = FakeEndpointProbe.AlwaysSucceeding();
        var engine = new WatchdogEngine(probe, new WatchdogOptions
        {
            Interval = TimeSpan.FromMilliseconds(10),
            Rounds = 3,
        });

        var endpoints = Endpoints(2);
        var rounds = new List<CheckRound>();

        await foreach (var round in engine.RunAsync(endpoints))
        {
            rounds.Add(round);
        }

        Assert.Equal(3, rounds.Count);
        Assert.Equal(new[] { 1, 2, 3 }, rounds.Select(round => round.Number).ToArray());
        Assert.Equal(6, probe.CallCount);
        Assert.All(rounds, round => Assert.True(round.AllSucceeded));
    }

    [Fact]
    public async Task RunAsync_returns_results_in_endpoint_order()
    {
        // Later endpoints answer faster, so completion order is the reverse of input order.
        var probe = new FakeEndpointProbe(async (endpoint, cancellationToken) =>
        {
            var index = int.Parse(endpoint.Id.Value[^1..]);
            await Task.Delay(50 * (4 - index), cancellationToken);
            return FakeEndpointProbe.Success(endpoint);
        });

        var engine = new WatchdogEngine(probe, new WatchdogOptions { Rounds = 1 });

        var round = await SingleRoundAsync(engine, Endpoints(4));

        Assert.Equal(
            new[] { "endpoint-1", "endpoint-2", "endpoint-3", "endpoint-4" },
            round.Results.Select(result => result.Id.Value).ToArray());
    }

    [Fact]
    public async Task RunAsync_probes_the_endpoints_of_a_round_concurrently()
    {
        var endpoints = Endpoints(4);

        // Every probe blocks until all four have arrived. If the engine ran sequentially,
        // the first probe would wait forever and the test would time out.
        var allArrived = new TaskCompletionSource();
        var arrived = 0;

        var probe = new FakeEndpointProbe(async (endpoint, cancellationToken) =>
        {
            if (Interlocked.Increment(ref arrived) == endpoints.Count)
            {
                allArrived.SetResult();
            }

            await allArrived.Task.WaitAsync(TestTimeout, cancellationToken);
            return FakeEndpointProbe.Success(endpoint);
        });

        var engine = new WatchdogEngine(probe, new WatchdogOptions { Rounds = 1 });

        var round = await SingleRoundAsync(engine, endpoints);

        Assert.Equal(4, round.Results.Count);
        Assert.True(round.AllSucceeded);
    }

    [Fact]
    public async Task RunAsync_never_exceeds_the_configured_concurrency()
    {
        var running = 0;
        var peak = 0;

        var probe = new FakeEndpointProbe(async (endpoint, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref running);

            // Interlocked.CompareExchange in a loop is the lock-free way to keep a maximum.
            int observed;
            while (current > (observed = Volatile.Read(ref peak))
                && Interlocked.CompareExchange(ref peak, current, observed) != observed)
            {
            }

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref running);

            return FakeEndpointProbe.Success(endpoint);
        });

        var engine = new WatchdogEngine(probe, new WatchdogOptions { Rounds = 1, MaxConcurrency = 2 });

        await SingleRoundAsync(engine, Endpoints(8));

        Assert.InRange(Volatile.Read(ref peak), 1, 2);
    }

    [Fact]
    public async Task RunAsync_ends_cleanly_when_the_caller_cancels()
    {
        var probe = FakeEndpointProbe.AlwaysSucceeding();
        var engine = new WatchdogEngine(probe, new WatchdogOptions
        {
            Interval = TimeSpan.FromMilliseconds(10),
            Rounds = null,
        });

        using var cancellation = new CancellationTokenSource();
        var rounds = 0;

        // Cancelling between two rounds ends the enumeration instead of throwing.
        await foreach (var _ in engine.RunAsync(Endpoints(2), cancellation.Token))
        {
            if (++rounds == 2)
            {
                await cancellation.CancelAsync();
            }
        }

        Assert.Equal(2, rounds);
    }

    private static async Task<CheckRound> SingleRoundAsync(
        WatchdogEngine engine,
        IReadOnlyList<EndpointConfig> endpoints)
    {
        await foreach (var round in engine.RunAsync(endpoints))
        {
            return round;
        }

        throw new InvalidOperationException("The engine produced no round.");
    }

    private static EndpointConfig[] Endpoints(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new EndpointConfig
            {
                Id = new CheckId($"endpoint-{index}"),
                Url = new Uri($"https://localhost/endpoint-{index}"),
            })
            .ToArray();
}
