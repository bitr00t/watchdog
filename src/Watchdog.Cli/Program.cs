using Watchdog.Core;

// Top-level statements: no class, no Main method, no string array parameter. The compiler
// generates the scaffolding. args, await and return (as the exit code) are still available.

// Exactly one HttpClient for the entire lifetime of the application.
using var httpClient = new HttpClient
{
    // The timeout is applied per endpoint inside the probe, not globally on the client.
    Timeout = System.Threading.Timeout.InfiniteTimeSpan,
};

var probe = new HttpEndpointProbe(httpClient);

EndpointConfig[] endpoints =
[
    new()
    {
        Id = new CheckId("example-com"),
        Url = new Uri("https://example.com/"),
        BodyContains = "Example Domain",
    },
    new()
    {
        Id = new CheckId("wrong-status"),
        Url = new Uri("https://example.com/does-not-exist"),
    },
    new()
    {
        Id = new CheckId("unreachable"),
        Url = new Uri("https://127.0.0.1:9/"),
        Timeout = TimeSpan.FromSeconds(2),
    },
];

// Ctrl+C shuts down cleanly instead of killing the process. This is also the first
// encounter with events: += attaches a handler, and callers cannot raise the event
// themselves.
using var applicationLifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    applicationLifetime.Cancel();
};

static string Format(CheckResult result)
{
    var marker = result.IsSuccess ? "OK  " : "FAIL";
    var status = result.StatusCode?.ToString() ?? "---";

    // Alignment and number format live inside the interpolated string:
    // ,-18 left aligned over 18 characters, ,9:F1 right aligned over 9 with one decimal.
    var line = $"{marker} {result.Id.Value,-18} {status,4} {result.Latency.Milliseconds,9:F1} ms";

    return result.FailureReason is null ? line : $"{line}  <- {result.FailureReason}";
}

Console.WriteLine($"Probing {endpoints.Length} endpoints");
Console.WriteLine();

var results = new List<CheckResult>(endpoints.Length);

// Step 1 runs sequentially on purpose. Parallel execution arrives in step 2.
foreach (var endpoint in endpoints)
{
    var result = await probe.ProbeAsync(endpoint, applicationLifetime.Token);
    results.Add(result);
    Console.WriteLine(Format(result));
}

var failed = results.Count(result => !result.IsSuccess);

Console.WriteLine();
Console.WriteLine($"{results.Count - failed} of {results.Count} checks succeeded.");

return failed == 0 ? 0 : 1;
