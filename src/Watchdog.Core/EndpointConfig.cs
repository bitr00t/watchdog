namespace Watchdog.Core;

/// <summary>
/// Describes an endpoint that should be monitored.
/// </summary>
/// <remarks>
/// <c>required</c> forces the caller to set the property in the object initializer,
/// <c>init</c> makes it immutable afterwards. Together they replace the builder one
/// would hand-write in Java for the same effect.
/// </remarks>
public sealed record EndpointConfig
{
    public required CheckId Id { get; init; }

    public required Uri Url { get; init; }

    public int ExpectedStatus { get; init; } = 200;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Optional substring that has to appear in the response body.
    /// <c>null</c> means the body is not inspected at all.
    /// </summary>
    /// <remarks>
    /// The question mark is compiler metadata and does not exist at runtime. Unlike Java's
    /// <c>Optional</c> it costs neither an allocation nor an unwrapping call.
    /// </remarks>
    public string? BodyContains { get; init; }
}
