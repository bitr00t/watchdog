namespace Watchdog.Core;

/// <summary>
/// Keeps the most recent results per endpoint.
/// </summary>
/// <remarks>
/// Not thread safe on purpose. The engine yields one round at a time and a single consumer
/// feeds this history, so a lock would only add cost. If that ever changes, the type to
/// reach for is <c>ConcurrentDictionary</c> plus a lock around the per-endpoint queue.
/// </remarks>
public sealed class CheckHistory
{
    private readonly Dictionary<CheckId, Queue<CheckResult>> _byEndpoint = [];

    public CheckHistory(int capacityPerEndpoint = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityPerEndpoint, 1);
        CapacityPerEndpoint = capacityPerEndpoint;
    }

    /// <summary>
    /// Number of results retained per endpoint. Older ones are dropped.
    /// </summary>
    public int CapacityPerEndpoint { get; }

    /// <summary>
    /// Every endpoint seen so far.
    /// </summary>
    public IReadOnlyCollection<CheckId> Ids => _byEndpoint.Keys;

    /// <summary>
    /// All retained results across all endpoints, oldest first per endpoint.
    /// </summary>
    public IEnumerable<CheckResult> All => _byEndpoint.Values.SelectMany(queue => queue);

    public void Add(CheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // CollectionsMarshal-free variant: TryGetValue plus a single insert. The dictionary
        // uses EqualityComparer<CheckId>.Default, which picks up the IEquatable<CheckId>
        // implementation the record struct generates. No boxing, unlike a Java HashMap key.
        if (!_byEndpoint.TryGetValue(result.Id, out var queue))
        {
            queue = new Queue<CheckResult>(CapacityPerEndpoint);
            _byEndpoint[result.Id] = queue;
        }

        queue.Enqueue(result);

        while (queue.Count > CapacityPerEndpoint)
        {
            queue.Dequeue();
        }
    }

    public void Add(CheckRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        foreach (var result in round.Results)
        {
            Add(result);
        }
    }

    /// <summary>
    /// Retained results for one endpoint, oldest first. Empty when the endpoint is unknown.
    /// </summary>
    public IReadOnlyList<CheckResult> For(CheckId id) =>
        _byEndpoint.TryGetValue(id, out var queue)
            ? [.. queue]
            : [];
}
