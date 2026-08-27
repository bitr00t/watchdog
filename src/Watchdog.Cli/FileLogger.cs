using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchdog.Core;

namespace Watchdog.Cli;

/// <summary>
/// Appends one line per check to the configured log file.
/// </summary>
/// <remarks>
/// The second subscriber exists to make the point of events concrete: neither the engine nor
/// the console reporter knows this class, and adding or removing it changes nothing else.
///
/// Note the split between startup and runtime failures. Opening the file happens in
/// <see cref="StartAsync"/> and is allowed to throw, which aborts host startup with a clear
/// message. A write that fails later is contained, because losing the log is not a reason to
/// stop monitoring.
/// </remarks>
internal sealed class FileLogger(
    WatchdogEngine engine,
    WatchdogConfiguration configuration,
    ILogger<FileLogger> logger) : IHostedService, IDisposable
{
    private StreamWriter? _writer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _writer = new StreamWriter(configuration.LogFilePath, append: true) { AutoFlush = true };
        engine.RoundCompleted += OnRoundCompleted;

        logger.LogInformation("Writing check log to {Path}", configuration.LogFilePath);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        engine.RoundCompleted -= OnRoundCompleted;

        return Task.CompletedTask;
    }

    public void Dispose() => _writer?.Dispose();

    private void OnRoundCompleted(object? sender, RoundCompletedEventArgs eventArgs)
    {
        if (_writer is not { } writer)
        {
            return;
        }

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

                writer.WriteLine(
                    $"{timestamp};{eventArgs.Round.Number};{result.Id.Value};{outcome};{status};{latency};{result.FailureReason}");
            }
        }
        catch (IOException exception)
        {
            // Handlers run synchronously and in sequence, so an exception escaping here would
            // also stop every handler that subscribed after this one.
            logger.LogWarning("Log write failed: {Reason}", exception.Message);
        }
    }
}
