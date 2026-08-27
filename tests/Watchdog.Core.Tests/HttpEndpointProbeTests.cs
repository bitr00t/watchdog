using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Watchdog.Core.Tests;

/// <summary>
/// Exercises the probe against a local WireMock server instead of real endpoints.
/// </summary>
/// <remarks>
/// xUnit creates a new instance of the test class for every test. The constructor is
/// therefore the counterpart of JUnit's <c>@BeforeEach</c> and <c>Dispose</c> that of
/// <c>@AfterEach</c>. xUnit has no dedicated attributes for either.
/// </remarks>
public sealed class HttpEndpointProbeTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly HttpClient _httpClient = new();

    [Fact]
    public async Task ProbeAsync_reports_success_for_expected_status_and_body()
    {
        _server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("status: healthy"));

        var result = await Probe().ProbeAsync(Endpoint("/health") with { BodyContains = "healthy" });

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.FailureReason);
        Assert.True(result.Latency >= Latency.Zero);
    }

    [Fact]
    public async Task ProbeAsync_reports_failure_for_unexpected_status()
    {
        _server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));

        var result = await Probe().ProbeAsync(Endpoint("/health"));

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("503", result.FailureReason ?? string.Empty);
    }

    [Fact]
    public async Task ProbeAsync_reports_failure_when_body_lacks_the_expected_text()
    {
        _server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("status: degraded"));

        var result = await Probe().ProbeAsync(Endpoint("/health") with { BodyContains = "healthy" });

        Assert.False(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("healthy", result.FailureReason ?? string.Empty);
    }

    [Fact]
    public async Task ProbeAsync_applies_the_typed_body_assertion()
    {
        _server
            .Given(Request.Create().WithPath("/health").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""{"status":"degraded","uptimeSeconds":7}"""));

        var endpoint = Endpoint("/health") with
        {
            BodyAssertion = BodyAssertion.Json<HealthReport>(
                report => report.Status == "healthy",
                "status is healthy"),
        };

        var result = await Probe().ProbeAsync(endpoint);

        Assert.False(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("status is healthy", result.FailureReason ?? string.Empty);
    }

    private sealed record HealthReport(string Status, int UptimeSeconds);

    [Fact]
    public async Task ProbeAsync_reports_a_timeout_when_the_response_takes_too_long()
    {
        _server
            .Given(Request.Create().WithPath("/slow").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(2)));

        var endpoint = Endpoint("/slow") with { Timeout = TimeSpan.FromMilliseconds(200) };

        var result = await Probe().ProbeAsync(endpoint);

        Assert.False(result.IsSuccess);
        Assert.Null(result.StatusCode);
        Assert.Contains("Timeout", result.FailureReason ?? string.Empty);
    }

    [Fact]
    public async Task ProbeAsync_propagates_cancellation_from_the_caller()
    {
        _server
            .Given(Request.Create().WithPath("/slow").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(2)));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Cancellation from the outside is not a check result but an exception. That is
        // exactly what the exception filter inside the probe distinguishes.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Probe().ProbeAsync(Endpoint("/slow"), cancellation.Token));
    }

    private HttpEndpointProbe Probe() => new(_httpClient);

    private EndpointConfig Endpoint(string path) => new()
    {
        Id = new CheckId("test"),
        Url = new Uri($"{_server.Url}{path}"),
    };

    public void Dispose()
    {
        _server.Dispose();
        _httpClient.Dispose();
    }
}
