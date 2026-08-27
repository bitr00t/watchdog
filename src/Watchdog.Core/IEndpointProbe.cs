namespace Watchdog.Core;

/// <summary>
/// Executes exactly one check against a single endpoint.
/// </summary>
/// <remarks>
/// The abstraction exists so that later steps (retry policies, a fake probe in tests) can
/// be inserted without touching the scheduling logic.
/// </remarks>
public interface IEndpointProbe
{
    Task<CheckResult> ProbeAsync(EndpointConfig endpoint, CancellationToken cancellationToken = default);
}
