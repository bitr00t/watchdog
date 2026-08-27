namespace Watchdog.Core.Tests;

/// <summary>
/// Test double that delegates every probe to a caller-supplied function.
/// </summary>
/// <remarks>
/// No mocking framework needed. A delegate is a first-class type in C#, so the behaviour
/// of the fake is simply a constructor argument. In Java this would be a functional
/// interface plus a lambda; here <c>Func&lt;...&gt;</c> is built into the language.
/// </remarks>
internal sealed class FakeEndpointProbe(
    Func<EndpointConfig, CancellationToken, Task<CheckResult>> handler) : IEndpointProbe
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<CheckResult> ProbeAsync(EndpointConfig endpoint, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return handler(endpoint, cancellationToken);
    }

    /// <summary>
    /// Creates a fake that immediately reports success for every endpoint.
    /// </summary>
    public static FakeEndpointProbe AlwaysSucceeding() =>
        new((endpoint, _) => Task.FromResult(Success(endpoint)));

    public static CheckResult Failure(EndpointConfig endpoint, string reason = "Status 503, expected 200") => new()
    {
        Id = endpoint.Id,
        StartedAt = DateTimeOffset.UtcNow,
        Latency = Latency.Zero,
        StatusCode = 503,
        FailureReason = reason,
    };

    public static CheckResult Success(EndpointConfig endpoint) => new()
    {
        Id = endpoint.Id,
        StartedAt = DateTimeOffset.UtcNow,
        Latency = Latency.Zero,
        StatusCode = endpoint.ExpectedStatus,
    };
}
