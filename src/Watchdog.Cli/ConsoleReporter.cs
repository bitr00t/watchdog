using Watchdog.Core;

namespace Watchdog.Cli;

/// <summary>
/// Renders rounds and status transitions to the console.
/// </summary>
/// <remarks>
/// Subscribing in the constructor and unsubscribing in <see cref="Dispose"/> is the pattern
/// to internalize: an event holds a strong reference to every subscriber, so a handler that
/// is never removed keeps its object alive as long as the publisher lives. That is the
/// classic managed memory leak in C#, and it has no direct Java counterpart because
/// listeners there are ordinary collection entries you remove just as explicitly.
/// </remarks>
internal sealed class ConsoleReporter : IDisposable
{
    private readonly WatchdogEngine _engine;

    public ConsoleReporter(WatchdogEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        _engine = engine;
        _engine.RoundCompleted += OnRoundCompleted;
        _engine.StatusChanged += OnStatusChanged;
    }

    public void Dispose()
    {
        _engine.RoundCompleted -= OnRoundCompleted;
        _engine.StatusChanged -= OnStatusChanged;
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
