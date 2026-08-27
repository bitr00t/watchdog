using System.Diagnostics;
using System.Net;

namespace Watchdog.Core;

/// <summary>
/// Probes an endpoint with an HTTP GET and checks status code, response time and,
/// optionally, the response body.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is injected rather than created here. A fresh instance per
/// request leads to socket exhaustion under load, because the connections linger in
/// TIME_WAIT after the client is disposed. This is one of the most common mistakes in C#.
/// </remarks>
public sealed class HttpEndpointProbe(HttpClient httpClient) : IEndpointProbe
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<CheckResult> ProbeAsync(
        EndpointConfig endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // Two reasons to abort are linked together: this endpoint's timeout and the
        // shutdown of the whole application. The exception filter below tells them apart.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(endpoint.Timeout);

        try
        {
            using var response = await _httpClient
                .GetAsync(endpoint.Url, HttpCompletionOption.ResponseContentRead, timeoutSource.Token)
                .ConfigureAwait(false);

            var body = await response.Content
                .ReadAsStringAsync(timeoutSource.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            return new CheckResult
            {
                Id = endpoint.Id,
                StartedAt = startedAt,
                Latency = Latency.FromStopwatch(stopwatch),
                StatusCode = (int)response.StatusCode,
                FailureReason = Validate(endpoint, response.StatusCode, body),
            };
        }
        // Exception filter: handle this only when the application is NOT shutting down.
        // A cancellation from the outside keeps travelling up the stack. Java has no
        // language construct for this, one would have to inspect and rethrow inside catch.
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failed(
                endpoint,
                startedAt,
                stopwatch,
                $"Timeout after {endpoint.Timeout.TotalMilliseconds:F0} ms");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            return Failed(endpoint, startedAt, stopwatch, $"Network error: {exception.Message}");
        }
    }

    /// <summary>
    /// Returns the failure reason, or <c>null</c> when every expectation is met.
    /// </summary>
    private static string? Validate(EndpointConfig endpoint, HttpStatusCode statusCode, string body)
    {
        var actualStatus = (int)statusCode;

        if (actualStatus != endpoint.ExpectedStatus)
        {
            return $"Status {actualStatus}, expected {endpoint.ExpectedStatus}";
        }

        // Pattern matching against null: inside the if branch the compiler knows that
        // BodyContains is neither null nor empty, and binds it to a local at the same time.
        if (endpoint.BodyContains is { Length: > 0 } expected
            && !body.Contains(expected, StringComparison.Ordinal))
        {
            return $"Body does not contain '{expected}'";
        }

        // The typed assertion runs last because it is the most expensive check.
        return endpoint.BodyAssertion?.Validate(body);
    }

    private static CheckResult Failed(
        EndpointConfig endpoint,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string reason) => new()
        {
            Id = endpoint.Id,
            StartedAt = startedAt,
            Latency = Latency.FromStopwatch(stopwatch),
            StatusCode = null,
            FailureReason = reason,
        };
}
