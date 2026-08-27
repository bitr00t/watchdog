namespace Watchdog.Core;

/// <summary>
/// Aggregated view over the retained results of a single endpoint.
/// </summary>
public sealed record EndpointStatistics
{
    public required CheckId Id { get; init; }

    public required int Total { get; init; }

    public required int FailureCount { get; init; }

    /// <summary>
    /// Share of successful checks between 0 and 1.
    /// </summary>
    public required double SuccessRate { get; init; }

    public required Latency AverageLatency { get; init; }

    public required Latency P95Latency { get; init; }

    /// <summary>
    /// Failures at the end of the retained window, so the current outage length.
    /// </summary>
    public required int ConsecutiveFailures { get; init; }

    public bool IsHealthy => ConsecutiveFailures == 0 && SuccessRate >= 0.95;
}
