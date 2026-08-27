using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchdog.Core;
using Watchdog.Persistence;

namespace Watchdog.Cli;

/// <summary>
/// Creates the database if needed and removes checks past the retention period.
/// </summary>
/// <remarks>
/// Registered before the worker so the schema exists before the first round is saved.
/// Hosted services start in registration order, which makes that ordering explicit rather
/// than a matter of luck.
/// </remarks>
internal sealed class DatabaseInitializer(
    SqliteCheckStore store,
    WatchdogConfiguration configuration,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var retention = TimeSpan.FromDays(configuration.Storage.RetentionDays);
        var removed = await store.PruneAsync(retention, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "History at {Path}, {Removed} checks older than {Days} days removed",
            configuration.Storage.DatabasePath,
            removed,
            configuration.Storage.RetentionDays);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
