namespace Watchdog.Core;

/// <summary>
/// Everything a monitoring session needs, as read from a configuration file.
/// </summary>
public sealed record WatchdogConfiguration
{
    public required WatchdogOptions Options { get; init; }

    public required RetryConfiguration Retry { get; init; }

    public required IReadOnlyList<EndpointConfig> Endpoints { get; init; }

    /// <summary>
    /// File the check log is appended to.
    /// </summary>
    public required string LogFilePath { get; init; }

    public required MetricsConfiguration Metrics { get; init; }
}

/// <summary>
/// Settings of the Prometheus scrape endpoint.
/// </summary>
public sealed record MetricsConfiguration
{
    public bool Enabled { get; init; }

    public int Port { get; init; } = 9464;

    public string Path { get; init; } = "/metrics";
}

/// <summary>
/// Retry settings for <see cref="ResilientEndpointProbe"/>.
/// </summary>
public sealed record RetryConfiguration
{
    public int MaxAttempts { get; init; } = 2;

    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(250);
}
