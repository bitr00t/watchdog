using System.Globalization;
using Watchdog.Core;

namespace Watchdog.Cli;

/// <summary>
/// Appends one line per check to a log file.
/// </summary>
/// <remarks>
/// The second subscriber exists to make the point of events concrete: neither the engine nor
/// the console reporter knows this class, and adding or removing it changes nothing else.
/// With a plain callback parameter the engine would have to decide up front how many
/// consumers it supports.
/// </remarks>
internal sealed class FileLogger : IDisposable
{
    private readonly WatchdogEngine _engine;
    private readonly StreamWriter _writer;

    public FileLogger(WatchdogEngine engine, string path)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _engine = engine;
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };

        _engine.RoundCompleted += OnRoundCompleted;
    }

    public void Dispose()
    {
        _engine.RoundCompleted -= OnRoundCompleted;
        _writer.Dispose();
    }

    private void OnRoundCompleted(object? sender, RoundCompletedEventArgs eventArgs)
    {
        try
        {
            foreach (var result in eventArgs.Round.Results)
            {
                // Every field is formatted with the invariant culture on purpose. A log file
                // that switches between 12.5 and 12,5 depending on the machine's locale is
                // unparsable, and the current culture is what string interpolation uses by
                // default.
                var timestamp = result.StartedAt.ToString("O", CultureInfo.InvariantCulture);
                var outcome = result.IsSuccess ? "ok" : "fail";
                var status = result.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                var latency = result.Latency.Milliseconds.ToString("F1", CultureInfo.InvariantCulture);

                _writer.WriteLine(
                    $"{timestamp};{eventArgs.Round.Number};{result.Id.Value};{outcome};{status};{latency};{result.FailureReason}");
            }
        }
        catch (IOException exception)
        {
            // A broken sink must not take the monitoring down with it. Handlers run
            // synchronously and in sequence, so an exception escaping here would also stop
            // every handler that subscribed after this one.
            Console.Error.WriteLine($"Log write failed: {exception.Message}");
        }
    }
}
