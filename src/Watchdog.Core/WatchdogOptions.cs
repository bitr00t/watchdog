namespace Watchdog.Core;

/// <summary>
/// Scheduling options for <see cref="WatchdogEngine"/>.
/// </summary>
public sealed record WatchdogOptions
{
    /// <summary>
    /// Delay between the end of one round and the start of the next one.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Upper bound of probes running at the same time within a single round.
    /// </summary>
    public int MaxConcurrency { get; init; } = 8;

    /// <summary>
    /// Number of rounds to run, or <c>null</c> to keep running until cancelled.
    /// </summary>
    public int? Rounds { get; init; }
}
