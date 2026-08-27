namespace Watchdog.Core;

/// <summary>
/// Query helpers over sequences of <see cref="CheckResult"/>.
/// </summary>
/// <remarks>
/// Extension methods are plain static methods; the <c>this</c> modifier on the first
/// parameter only changes how they may be called. Three consequences worth internalizing:
/// they are bound at compile time and cannot be overridden, an instance method with the
/// same signature always wins, and the extended type does not have to know about them.
/// That is how LINQ itself is built, and why it can extend an interface as narrow as
/// <c>IEnumerable&lt;T&gt;</c> without every implementer changing.
/// </remarks>
public static class CheckResultExtensions
{
    /// <summary>
    /// Share of successful checks between 0 and 1. An empty sequence counts as fully healthy.
    /// </summary>
    public static double SuccessRate(this IEnumerable<CheckResult> results)
    {
        var snapshot = Materialize(results);

        return snapshot.Count == 0
            ? 1.0
            : (double)snapshot.Count(result => result.IsSuccess) / snapshot.Count;
    }

    public static Latency AverageLatency(this IEnumerable<CheckResult> results)
    {
        var snapshot = Materialize(results);

        return snapshot.Count == 0
            ? Latency.Zero
            : new Latency(snapshot.Average(result => result.Latency.Milliseconds));
    }

    /// <summary>
    /// Nearest-rank percentile of the observed latencies.
    /// </summary>
    public static Latency PercentileLatency(this IEnumerable<CheckResult> results, int percentile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percentile, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);

        // OrderBy uses Comparer<Latency>.Default, which resolves to the IComparable<Latency>
        // implementation on the struct. No selector lambda needed.
        var ordered = Materialize(results)
            .Select(result => result.Latency)
            .Order()
            .ToArray();

        if (ordered.Length == 0)
        {
            return Latency.Zero;
        }

        var rank = (int)Math.Ceiling(percentile / 100.0 * ordered.Length);

        return ordered[Math.Clamp(rank - 1, 0, ordered.Length - 1)];
    }

    /// <summary>
    /// The most recent <paramref name="count"/> results, oldest first.
    /// </summary>
    public static IEnumerable<CheckResult> Recent(this IEnumerable<CheckResult> results, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        // Deferred: nothing is enumerated until the caller iterates the returned sequence.
        return results.TakeLast(count);
    }

    /// <summary>
    /// Number of failures at the end of the sequence, so the length of the current outage.
    /// </summary>
    public static int ConsecutiveFailures(this IEnumerable<CheckResult> results) =>
        Materialize(results)
            .Reverse()
            .TakeWhile(result => !result.IsSuccess)
            .Count();

    public static EndpointStatistics Summarize(this IEnumerable<CheckResult> results, CheckId id)
    {
        var snapshot = Materialize(results);

        return new EndpointStatistics
        {
            Id = id,
            Total = snapshot.Count,
            FailureCount = snapshot.Count(result => !result.IsSuccess),
            SuccessRate = snapshot.SuccessRate(),
            AverageLatency = snapshot.AverageLatency(),
            P95Latency = snapshot.PercentileLatency(95),
            ConsecutiveFailures = snapshot.ConsecutiveFailures(),
        };
    }

    /// <summary>
    /// One summary per endpoint, worst first.
    /// </summary>
    public static IReadOnlyList<EndpointStatistics> Summarize(this CheckHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        // Query syntax for the grouping part, method syntax for the ordering. Both compile
        // to the same calls; query syntax simply has no keyword for OrderByDescending with
        // a second key, so mixing them is idiomatic rather than a compromise.
        var summaries =
            from result in history.All
            group result by result.Id into endpoint
            select endpoint.Summarize(endpoint.Key);

        return summaries
            .OrderByDescending(statistics => statistics.ConsecutiveFailures)
            .ThenByDescending(statistics => statistics.FailureCount)
            .ThenBy(statistics => statistics.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Enumerates the sequence at most once.
    /// </summary>
    /// <remarks>
    /// LINQ is lazy, so every aggregate above would otherwise walk the source again, and a
    /// source backed by I/O would be hit again with it. Materializing once is the cheap fix.
    /// </remarks>
    private static IReadOnlyList<CheckResult> Materialize(IEnumerable<CheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results as IReadOnlyList<CheckResult> ?? results.ToArray();
    }
}
