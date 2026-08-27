namespace Watchdog.Core;

/// <summary>
/// Holds the latest aggregated statistics for readers on other threads.
/// </summary>
/// <remarks>
/// <see cref="CheckHistory"/> is deliberately not thread safe, and a metrics endpoint reads
/// from whatever thread happens to serve the request. Rather than putting a lock around
/// every write to the history, the producer publishes an immutable list here and readers
/// take whatever the current one is.
///
/// <c>Volatile.Write</c> and <c>Volatile.Read</c> are what make that safe. The reference
/// assignment itself is atomic on every platform .NET supports, but without the volatile
/// pair the compiler or the CPU may reorder it against the writes that built the list, and
/// a reader could observe the new reference before the contents behind it. The lists handed
/// in must never be mutated afterwards.
/// </remarks>
public sealed class StatisticsSnapshot
{
    private IReadOnlyList<EndpointStatistics> _current = [];

    /// <summary>
    /// The most recently published statistics, or an empty list before the first round.
    /// </summary>
    public IReadOnlyList<EndpointStatistics> Current => Volatile.Read(ref _current);

    public void Update(IReadOnlyList<EndpointStatistics> statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        Volatile.Write(ref _current, statistics);
    }
}
