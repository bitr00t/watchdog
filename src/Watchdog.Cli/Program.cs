using Watchdog.Cli;
using Watchdog.Core;

// Top-level statements: no class, no Main method, no string array parameter. The compiler
// generates the scaffolding. args, await and return (as the exit code) are still available.

// Exactly one HttpClient for the entire lifetime of the application.
using var httpClient = new HttpClient
{
    // The timeout is applied per endpoint inside the probe, not globally on the client.
    Timeout = System.Threading.Timeout.InfiniteTimeSpan,
};

var options = new WatchdogOptions
{
    Interval = TimeSpan.FromSeconds(10),
    MaxConcurrency = 4,

    // Set this to null to keep going until Ctrl+C.
    Rounds = 3,
};

// The retry decorator wraps the plain probe. Neither the engine nor the probe knows about
// the other's existence, which is the whole point of IEndpointProbe.
IEndpointProbe probe = new ResilientEndpointProbe(
    new HttpEndpointProbe(httpClient),
    maxRetryAttempts: 2,
    delay: TimeSpan.FromMilliseconds(250));

var engine = new WatchdogEngine(probe, options);
var history = new CheckHistory(capacityPerEndpoint: 50);

// Two subscribers that know nothing about each other. Removing either one changes nothing
// about the engine or the other subscriber.
using var reporter = new ConsoleReporter(engine);
using var logger = new FileLogger(engine, "watchdog.log");

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
    new()
    {
        // A real JSON status endpoint, asserted through a typed predicate rather than a
        // substring. The type argument is all the deserializer needs; no class token has to
        // be threaded through the call.
        Id = new CheckId("github-status"),
        Url = new Uri("https://www.githubstatus.com/api/v2/status.json"),
        BodyAssertion = BodyAssertion.Json<GitHubStatus>(
            status => status.Status.Indicator == "none",
            "GitHub reports no incident"),
    },
];

// Ctrl+C shuts down cleanly instead of killing the process.
using var applicationLifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    applicationLifetime.Cancel();
};

static void PrintSummary(CheckHistory history)
{
    var summaries = history.Summarize();

    if (summaries.Count == 0)
    {
        return;
    }

    Console.WriteLine("Summary");
    Console.WriteLine($"{"endpoint",-18} {"checks",6} {"ok",7} {"avg",10} {"p95",10}  state");

    foreach (var statistics in summaries)
    {
        Console.WriteLine(
            $"{statistics.Id.Value,-18} {statistics.Total,6} {statistics.SuccessRate,7:P0} "
            + $"{statistics.AverageLatency.Milliseconds,10:F1} {statistics.P95Latency.Milliseconds,10:F1}  "
            + (statistics.IsHealthy ? "healthy" : $"{statistics.ConsecutiveFailures} failing in a row"));
    }
}

Console.WriteLine($"Watching {endpoints.Length} endpoints every {options.Interval.TotalSeconds:F0} s");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

var failedRounds = 0;

try
{
    // Rendering moved into ConsoleReporter, so this loop only keeps the history and the
    // exit code. await foreach still drives the schedule: the engine starts the next round
    // only once this loop asks for it.
    await foreach (var round in engine.RunAsync(endpoints, applicationLifetime.Token))
    {
        history.Add(round);

        if (!round.AllSucceeded)
        {
            failedRounds++;
        }
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C during a running round cancels the in-flight requests. Cancellation between
    // two rounds ends the enumeration without an exception.
    Console.WriteLine("Stopped.");
}

PrintSummary(history);

return failedRounds == 0 ? 0 : 1;

// Types have to come after the top-level statements. Records match the JSON shape by
// property name; the deserializer is configured to ignore casing.
internal sealed record GitHubStatus(GitHubStatusDetail Status);

internal sealed record GitHubStatusDetail(string Indicator, string Description);
