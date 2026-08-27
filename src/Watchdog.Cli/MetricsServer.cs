using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchdog.Core;

namespace Watchdog.Cli;

/// <summary>
/// Serves the latest statistics for a Prometheus scraper.
/// </summary>
/// <remarks>
/// <see cref="HttpListener"/> keeps this to the base class library; pulling in ASP.NET Core
/// for a single read only route would change the shape of the whole project.
///
/// The prefix binds to <c>localhost</c> on purpose. On Windows, listening on a hostname or
/// on <c>+</c> requires a URL reservation created with an elevated
/// <c>netsh http add urlacl</c>, while <c>localhost</c> works for a normal user. Exposing
/// the endpoint to other machines is therefore a deliberate extra step, which is the right
/// default for something that has no authentication.
/// </remarks>
internal sealed class MetricsServer(
    StatisticsSnapshot snapshot,
    WatchdogConfiguration configuration,
    ILogger<MetricsServer> logger) : IHostedService, IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();

    private Task? _acceptLoop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Prefixes.Add($"http://localhost:{configuration.Metrics.Port}/");
        _listener.Start();

        // Not awaited: the loop runs for the lifetime of the service. StopAsync waits for it.
        _acceptLoop = AcceptLoopAsync(_stopping.Token);

        logger.LogInformation(
            "Metrics available at http://localhost:{Port}{Path}",
            configuration.Metrics.Port,
            configuration.Metrics.Path);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        // GetContextAsync predates cancellation tokens and cannot be cancelled. Stopping the
        // listener is what unblocks it, by making the pending call throw. Plenty of older
        // APIs need this treatment, and the exception is the expected path rather than a bug.
        _listener.Stop();

        if (_acceptLoop is { } loop)
        {
            await loop.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _listener.Close();
        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            try
            {
                await RespondAsync(context).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpListenerException or IOException)
            {
                // A scraper that hangs up mid response is routine and not worth a warning.
                logger.LogDebug("Metrics request failed: {Reason}", exception.Message);
            }
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        var path = request.Url?.AbsolutePath ?? "/";
        var isMetricsPath = string.Equals(path, configuration.Metrics.Path, StringComparison.Ordinal);

        if (!isMetricsPath || !string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            response.StatusCode = isMetricsPath ? 405 : 404;
            response.Close();
            return;
        }

        var payload = Encoding.UTF8.GetBytes(PrometheusFormatter.Format(snapshot.Current));

        response.StatusCode = 200;
        response.ContentType = PrometheusFormatter.ContentType;
        response.ContentLength64 = payload.Length;

        await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);

        response.Close();
    }
}
