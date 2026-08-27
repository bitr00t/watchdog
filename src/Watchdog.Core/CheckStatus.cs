namespace Watchdog.Core;

/// <summary>
/// Health state of an endpoint as seen by the engine.
/// </summary>
public enum CheckStatus
{
    /// <summary>No result observed yet.</summary>
    Unknown = 0,

    Healthy = 1,

    Failing = 2,
}
