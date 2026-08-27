using Watchdog.Cli;
using Watchdog.Core;

// Top-level statements: no class, no Main method, no string array parameter. The compiler
// generates the scaffolding. args, await and return (as the exit code) are still available.

const string DefaultConfigurationPath = "watchdog.json";

// args is available without declaring it. The first argument overrides the config path.
var configurationPath = args.Length > 0 ? args[0] : DefaultConfigurationPath;

WatchdogConfiguration configuration;

try
{
    configuration = await ConfigurationLoader.LoadAsync(configurationPath);
}
catch (ConfigurationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

// Exactly one HttpClient for the entire lifetime of the application.
using var httpClient = new HttpClient
{
    // The timeout is applied per endpoint inside the probe, not globally on the client.
    Timeout = System.Threading.Timeout.InfiniteTimeSpan,
};

// The retry decorator wraps the plain probe. Neither the engine nor the probe knows about
// the other's existence, which is the whole point of IEndpointProbe.
IEndpointProbe probe = new ResilientEndpointProbe(
    new HttpEndpointProbe(httpClient),
    configuration.Retry.MaxAttempts,
    configuration.Retry.Delay);

var engine = new WatchdogEngine(probe, configuration.Options);
var history = new CheckHistory(capacityPerEndpoint: 50);

// Two subscribers that know nothing about each other. Removing either one changes nothing
// about the engine or the other subscriber.
using var reporter = new ConsoleReporter(engine);
using var logger = new FileLogger(engine, "watchdog.log");

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

Console.WriteLine(
    $"Watching {configuration.Endpoints.Count} endpoints from '{configurationPath}' "
    + $"every {configuration.Options.Interval.TotalSeconds:F0} s");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

var failedRounds = 0;

try
{
    // Rendering moved into ConsoleReporter, so this loop only keeps the history and the
    // exit code. await foreach still drives the schedule: the engine starts the next round
    // only once this loop asks for it.
    await foreach (var round in engine.RunAsync(configuration.Endpoints, applicationLifetime.Token))
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
