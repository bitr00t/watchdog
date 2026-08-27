namespace Watchdog.Persistence;

/// <summary>
/// Aggregate over the persisted checks of one endpoint.
/// </summary>
/// <remarks>
/// A positional record on purpose. The projection in the store builds this inside an
/// expression tree, and a constructor call translates cleanly, while an object initializer
/// over init-only properties does not.
/// </remarks>
public sealed record EndpointSummary(
    string EndpointId,
    int Total,
    int Failures,
    double AverageLatencyMilliseconds,
    DateTimeOffset LastCheckedAt)
{
    public double SuccessRate => Total == 0 ? 1 : (double)(Total - Failures) / Total;
}
