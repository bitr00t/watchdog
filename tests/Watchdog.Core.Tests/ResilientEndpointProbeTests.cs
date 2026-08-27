using Xunit;

namespace Watchdog.Core.Tests;

public sealed class ResilientEndpointProbeTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.FromMilliseconds(1);

    private static readonly EndpointConfig Endpoint = new()
    {
        Id = new CheckId("a"),
        Url = new Uri("https://localhost/a"),
    };

    [Fact]
    public async Task ProbeAsync_does_not_retry_a_successful_check()
    {
        var inner = FakeEndpointProbe.AlwaysSucceeding();
        var probe = new ResilientEndpointProbe(inner, maxRetryAttempts: 2, delay: NoDelay);

        var result = await probe.ProbeAsync(Endpoint);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_retries_until_the_check_succeeds()
    {
        var attempts = 0;

        var inner = new FakeEndpointProbe((endpoint, _) =>
        {
            attempts++;

            return Task.FromResult(attempts < 3
                ? FakeEndpointProbe.Failure(endpoint)
                : FakeEndpointProbe.Success(endpoint));
        });

        var probe = new ResilientEndpointProbe(inner, maxRetryAttempts: 2, delay: NoDelay);

        var result = await probe.ProbeAsync(Endpoint);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_returns_the_last_failure_once_the_retries_are_exhausted()
    {
        var inner = new FakeEndpointProbe((endpoint, _) =>
            Task.FromResult(FakeEndpointProbe.Failure(endpoint)));

        var probe = new ResilientEndpointProbe(inner, maxRetryAttempts: 2, delay: NoDelay);

        var result = await probe.ProbeAsync(Endpoint);

        Assert.False(result.IsSuccess);

        // One initial attempt plus two retries. A failing check stays a result and never
        // turns into an exception, so the caller sees the last outcome.
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public void Constructor_rejects_a_retry_count_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResilientEndpointProbe(FakeEndpointProbe.AlwaysSucceeding(), maxRetryAttempts: 0));
    }
}
