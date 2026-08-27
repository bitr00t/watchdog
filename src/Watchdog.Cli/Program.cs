using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchdog.Cli;
using Watchdog.Core;
using Watchdog.Persistence;

// Composition root. Everything below only registers types; nothing is constructed until the
// host resolves the first hosted service.

const string DefaultConfigurationPath = "watchdog.json";

var configurationPath = args.Length > 0 ? args[0] : DefaultConfigurationPath;

WatchdogConfiguration configuration;

try
{
    // Loaded before the host exists on purpose: a broken configuration should produce one
    // clear message and exit code 2, not a DI resolution failure wrapped in a stack trace.
    configuration = await ConfigurationLoader.LoadAsync(configurationPath);
}
catch (ConfigurationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// The configuration object and its parts are registered separately, so a consumer can ask
// for exactly the slice it needs instead of taking the whole tree and reaching into it.
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(configuration.Options);
builder.Services.AddSingleton(configuration.Retry);

builder.Services.AddSingleton(new CheckHistory(capacityPerEndpoint: 50));
builder.Services.AddSingleton<StatisticsSnapshot>();

// One HttpClient for the whole process. IHttpClientFactory exists mainly to rotate handlers
// so that long lived clients do not hold on to stale DNS entries; PooledConnectionLifetime
// solves the same problem directly and keeps Microsoft.Extensions.Http out of the picture.
builder.Services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    AutomaticDecompression = DecompressionMethods.All,
})
{
    // The timeout is applied per endpoint inside the probe, not globally on the client.
    Timeout = System.Threading.Timeout.InfiniteTimeSpan,
});

builder.Services.AddSingleton<HttpEndpointProbe>();

// The built-in container has no decorator support, so the wrapping is spelled out in a
// factory. Third party containers and Scrutor add a Decorate() call for this; the manual
// version costs three lines and keeps the dependency list short.
builder.Services.AddSingleton<IEndpointProbe>(provider =>
{
    var retry = provider.GetRequiredService<RetryConfiguration>();

    return new ResilientEndpointProbe(
        provider.GetRequiredService<HttpEndpointProbe>(),
        retry.MaxAttempts,
        retry.Delay);
});

// WatchdogEngine keeps the last known status per endpoint, so it has to be a singleton.
// Registering it as transient would hand every consumer its own state and silently break
// the StatusChanged event.
builder.Services.AddSingleton<WatchdogEngine>();

// Registration order is start order. Both subscribers have to be attached before the worker
// produces its first round; shutdown runs in reverse, so they detach after it finishes.
// AddDbContextFactory registers the factory as a singleton and the context itself as
// transient, which is what a long running process needs: one short lived unit of work per
// operation instead of a context that lives as long as the application.
if (configuration.Storage.Enabled)
{
    builder.Services.AddDbContextFactory<WatchdogDbContext>(dbOptions =>
        dbOptions.UseSqlite($"Data Source={configuration.Storage.DatabasePath}"));

    builder.Services.AddSingleton<SqliteCheckStore>();
    builder.Services.AddSingleton<ICheckResultStore>(provider =>
        provider.GetRequiredService<SqliteCheckStore>());

    builder.Services.AddHostedService<DatabaseInitializer>();
}
else
{
    builder.Services.AddSingleton<ICheckResultStore, NullCheckResultStore>();
}

builder.Services.AddHostedService<ConsoleReporter>();
builder.Services.AddHostedService<FileLogger>();

if (configuration.Metrics.Enabled)
{
    builder.Services.AddHostedService<MetricsServer>();
}

builder.Services.AddHostedService<WatchdogWorker>();

using var host = builder.Build();

// RunAsync blocks until the host stops, either because the worker asked for shutdown or
// because Ctrl+C was pressed. The host installs that handler itself.
await host.RunAsync();

return Environment.ExitCode;
