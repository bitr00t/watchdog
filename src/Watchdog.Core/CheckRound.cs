namespace Watchdog.Core;

/// <summary>
/// Results of one complete pass over every configured endpoint.
/// </summary>
public sealed record CheckRound
{
    public required int Number { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// One result per endpoint, in the order the endpoints were configured.
    /// </summary>
    public required IReadOnlyList<CheckResult> Results { get; init; }

    // Computed properties backed by LINQ. They are evaluated on every access, which is
    // fine for a handful of endpoints but worth remembering once a round holds thousands.
    public int FailureCount => Results.Count(result => !result.IsSuccess);

    public bool AllSucceeded => Results.All(result => result.IsSuccess);

    /// <summary>
    /// Slowest response of the round.
    /// </summary>
    /// <remarks>
    /// Works because <see cref="Latency"/> implements <c>IComparable&lt;Latency&gt;</c>:
    /// LINQ's <c>Max</c> falls back to <c>Comparer&lt;T&gt;.Default</c>, which picks that up.
    /// </remarks>
    public Latency SlowestLatency => Results.Count == 0
        ? Latency.Zero
        : Results.Max(result => result.Latency);
}
