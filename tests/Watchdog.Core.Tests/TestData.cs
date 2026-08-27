namespace Watchdog.Core.Tests;

/// <summary>
/// Shared builders for test results.
/// </summary>
internal static class TestData
{
    public static CheckResult Result(string id, bool success, double latencyMs = 10) => new()
    {
        Id = new CheckId(id),
        StartedAt = DateTimeOffset.UtcNow,
        Latency = new Latency(latencyMs),
        StatusCode = success ? 200 : 503,
        FailureReason = success ? null : "Status 503, expected 200",
    };
}
