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

var engine = new WatchdogEngine(new HttpEndpointProbe(httpClient), options);
var history = new CheckHistory(capacityPerEndpoint: 50);

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
    // await foreach pulls one round at a time. The engine only starts the next round once
    // this loop asks for it, so a slow consumer cannot pile up work in the background.
    await foreach (var round in engine.RunAsync(endpoints, applicationLifetime.Token))
    {
        history.Add(round);

        Console.WriteLine(
            $"Round {round.Number} at {round.StartedAt.ToLocalTime():HH:mm:ss}, "
            + $"{round.FailureCount} of {round.Results.Count} failed, "
            + $"slowest {round.SlowestLatency}");

        foreach (var result in round.Results)
        {
            Console.WriteLine(Format(result));
        }

        Console.WriteLine();

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
