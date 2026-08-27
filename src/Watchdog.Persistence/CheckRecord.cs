namespace Watchdog.Persistence;

/// <summary>
/// One persisted check result.
/// </summary>
/// <remarks>
/// A mutable class with plain properties, unlike the immutable records in the core. Entity
/// Framework materializes instances by setting properties and tracks changes by comparing
/// against a snapshot, so an entity is one of the few places in this project where
/// mutability is the right answer rather than a compromise.
///
/// It is also a separate type from <c>CheckResult</c> on purpose. A storage schema and a
/// domain model change for different reasons, and mapping between them costs a few lines
/// once instead of constraining both forever.
/// </remarks>
public sealed class CheckRecord
{
    public int Id { get; set; }

    public required string EndpointId { get; set; }

    public int RoundNumber { get; set; }

    /// <summary>
    /// Always stored in UTC.
    /// </summary>
    /// <remarks>
    /// DateTime rather than DateTimeOffset, even though the domain uses the latter. SQLite
    /// has no date type and keeps values as ISO text, so ordering is lexicographic, and a
    /// column whose rows could carry different offsets would sort wrongly. The provider
    /// refuses to compare or order DateTimeOffset for exactly that reason instead of
    /// producing quietly incorrect results. Normalizing to UTC on write removes the problem
    /// and the offset along with it.
    /// </remarks>
    public DateTime StartedAt { get; set; }

    public double LatencyMilliseconds { get; set; }

    public int? StatusCode { get; set; }

    public bool IsSuccess { get; set; }

    public string? FailureReason { get; set; }
}
