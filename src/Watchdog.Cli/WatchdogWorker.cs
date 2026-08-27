using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchdog.Core;

namespace Watchdog.Cli;

/// <summary>
/// Drives the engine for as long as the host runs.
/// </summary>
/// <remarks>
/// <see cref="BackgroundService"/> is the base class for long running work: the host calls
/// <see cref="ExecuteAsync"/> once and hands it a token that is cancelled on shutdown, which
/// is what Ctrl+C now triggers. The manual CancelKeyPress handler is gone.
///
/// One thing that surprises newcomers: returning from <c>ExecuteAsync</c> does not stop the
/// host. A finite run therefore has to ask for shutdown explicitly through
/// <see cref="IHostApplicationLifetime"/>, otherwise the process would sit idle after the
/// last round.
/// </remarks>
internal sealed class WatchdogWorker(
    WatchdogEngine engine,
    CheckHistory history,
    WatchdogConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<WatchdogWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Watching {Count} endpoints every {Seconds:F0} s",
            configuration.Endpoints.Count,
            configuration.Options.Interval.TotalSeconds);

        var failedRounds = 0;

        try
        {
            await foreach (var round in engine.RunAsync(configuration.Endpoints, stoppingToken))
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
            logger.LogInformation("Stopped before the configured number of rounds was reached");
        }

        PrintSummary(history);

        // Written to the process here and read in Program after the host has stopped.
        Environment.ExitCode = failedRounds == 0 ? 0 : 1;

        lifetime.StopApplication();
    }

    /// <summary>
    /// The table goes to the console directly rather than through the logger: a log line
    /// carries a level, a category and a timestamp, and none of that helps a fixed width
    /// report.
    /// </summary>
    private static void PrintSummary(CheckHistory history)
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
}
