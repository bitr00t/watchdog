namespace Watchdog.Core;

/// <summary>
/// Outcome of a single endpoint check.
/// </summary>
public sealed record CheckResult
{
    public required CheckId Id { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required Latency Latency { get; init; }

    /// <summary>
    /// HTTP status code, or <c>null</c> when no response arrived at all.
    /// </summary>
    /// <remarks>
    /// <c>int?</c> is <c>Nullable&lt;int&gt;</c> and therefore a struct itself. There is no
    /// wrapper type like Java's <c>Integer</c> and no autoboxing involved.
    /// </remarks>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Reason for the failure, or <c>null</c> when the check passed.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Computed property without a backing field. In Java this would be an
    /// <c>isSuccess()</c> method.
    /// </summary>
    public bool IsSuccess => FailureReason is null;
}
