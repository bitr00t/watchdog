using System.Runtime.CompilerServices;

namespace Watchdog.Core;

/// <summary>
/// Runs every configured endpoint in parallel and repeats that round on an interval.
/// </summary>
public sealed class WatchdogEngine(IEndpointProbe probe, WatchdogOptions? options = null)
{
    private readonly IEndpointProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly WatchdogOptions _options = options ?? new WatchdogOptions();

    /// <summary>
    /// Produces one <see cref="CheckRound"/> per pass until the configured number of rounds
    /// is reached or the caller cancels.
    /// </summary>
    /// <remarks>
    /// This method is deliberately not an iterator itself. Argument validation inside an
    /// <c>async IAsyncEnumerable</c> method would not run when the method is called, but only
    /// on the first <c>MoveNextAsync</c>. Splitting the validation from the iterator body is
    /// the standard way to make the exception surface where the caller expects it.
    /// </remarks>
    public IAsyncEnumerable<CheckRound> RunAsync(
        IReadOnlyList<EndpointConfig> endpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (endpoints.Count == 0)
        {
            throw new ArgumentException("At least one endpoint is required.", nameof(endpoints));
        }

        return RunCoreAsync(endpoints, cancellationToken);
    }

    /// <remarks>
    /// <c>[EnumeratorCancellation]</c> lets callers attach a token through
    /// <c>WithCancellation(...)</c> on the enumerable instead of passing it as an argument.
    /// Without the attribute that call would silently have no effect.
    /// </remarks>
    private async IAsyncEnumerable<CheckRound> RunCoreAsync(
        IReadOnlyList<EndpointConfig> endpoints,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);
        var number = 0;

        while (true)
        {
            number++;
            var startedAt = DateTimeOffset.UtcNow;

            var results = await RunRoundAsync(endpoints, cancellationToken).ConfigureAwait(false);

            yield return new CheckRound
            {
                Number = number,
                StartedAt = startedAt,
                Results = results,
            };

            if (_options.Rounds is { } limit && number >= limit)
            {
                yield break;
            }

            if (!await WaitForNextTickAsync(timer, cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Probes every endpoint concurrently, limited by <see cref="WatchdogOptions.MaxConcurrency"/>.
    /// </summary>
    private async Task<IReadOnlyList<CheckResult>> RunRoundAsync(
        IReadOnlyList<EndpointConfig> endpoints,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        // Select with an async lambda produces IEnumerable<Task<CheckResult>>. Nothing runs
        // yet: LINQ is lazy, and the tasks are only created once something enumerates the
        // sequence. Task.WhenAll does that enumeration, which is why every probe starts here
        // and not one line earlier.
        var probes = endpoints.Select(async endpoint =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await _probe.ProbeAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        // WhenAll keeps the order of the input sequence, independent of completion order.
        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the next tick and reports whether the loop should continue.
    /// </summary>
    /// <remarks>
    /// Cancellation is turned into a regular <c>false</c> here. The reason is a C# rule:
    /// a <c>yield return</c> cannot sit inside a <c>try</c> block that has a <c>catch</c>,
    /// so the iterator above cannot catch this itself.
    /// </remarks>
    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
