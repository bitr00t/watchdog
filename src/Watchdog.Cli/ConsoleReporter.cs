using Microsoft.Extensions.Hosting;
using Watchdog.Core;

namespace Watchdog.Cli;

/// <summary>
/// Renders rounds and status transitions to the console.
/// </summary>
/// <remarks>
/// Implemented as an <see cref="IHostedService"/> so the host owns the subscription window:
/// <see cref="StartAsync"/> attaches the handlers, <see cref="StopAsync"/> detaches them.
/// Hosted services start in registration order and stop in reverse, which is exactly what
/// this needs, since the reporter has to be listening before the worker produces its first
/// round and must still be listening while the worker winds down.
/// </remarks>
internal sealed class ConsoleReporter(WatchdogEngine engine) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        engine.RoundCompleted += OnRoundCompleted;
        engine.StatusChanged += OnStatusChanged;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Detaching still matters even at shutdown: the engine is a singleton and keeps a
        // strong reference to every handler that was never removed.
        engine.RoundCompleted -= OnRoundCompleted;
        engine.StatusChanged -= OnStatusChanged;

        return Task.CompletedTask;
    }

    private static void OnRoundCompleted(object? sender, RoundCompletedEventArgs eventArgs)
    {
        var round = eventArgs.Round;

        Console.WriteLine(
            $"Round {round.Number} at {round.StartedAt.ToLocalTime():HH:mm:ss}, "
            + $"{round.FailureCount} of {round.Results.Count} failed, "
            + $"slowest {round.SlowestLatency}");

        foreach (var result in round.Results)
        {
            Console.WriteLine(Format(result));
        }

        Console.WriteLine();
    }

    private static void OnStatusChanged(object? sender, StatusChangedEventArgs eventArgs)
    {
        var transition = eventArgs switch
        {
            { Previous: CheckStatus.Unknown, Current: CheckStatus.Healthy } => "is up",
            { Previous: CheckStatus.Unknown, Current: CheckStatus.Failing } => "is down",
            { Current: CheckStatus.Healthy } => "recovered",
            _ => "went down",
        };

        Console.WriteLine($"  * {eventArgs.Id.Value} {transition}");
    }

    private static string Format(CheckResult result)
    {
        var marker = result.IsSuccess ? "OK  " : "FAIL";
        var status = result.StatusCode?.ToString() ?? "---";

        // Alignment and number format live inside the interpolated string:
        // ,-18 left aligned over 18 characters, ,9:F1 right aligned over 9 with one decimal.
        var line = $"{marker} {result.Id.Value,-18} {status,4} {result.Latency.Milliseconds,9:F1} ms";

        return result.FailureReason is null ? line : $"{line}  <- {result.FailureReason}";
    }
}
