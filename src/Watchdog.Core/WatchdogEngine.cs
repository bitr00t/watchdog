using System.Runtime.CompilerServices;

namespace Watchdog.Core;

/// <summary>
/// Runs every configured endpoint in parallel and repeats that round on an interval.
/// </summary>
public sealed class WatchdogEngine(IEndpointProbe probe, WatchdogOptions? options = null)
{
    private readonly IEndpointProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly WatchdogOptions _options = options ?? new WatchdogOptions();

    // Last known state per endpoint. This makes the engine stateful: one instance drives
    // one monitoring session and is not meant to be shared between concurrent sessions.
    private readonly Dictionary<CheckId, CheckStatus> _lastStatus = [];

    /// <summary>
    /// Raised when an endpoint flips between healthy and failing, including the first
    /// observation, which moves it away from <see cref="CheckStatus.Unknown"/>.
    /// </summary>
    /// <remarks>
    /// The <c>event</c> keyword is what separates this from a public delegate field:
    /// subscribers may only add and remove handlers, and nobody outside this class can
    /// raise the event or clear the invocation list.
    ///
    /// Handlers run synchronously on the thread that enumerates the rounds, in subscription
    /// order, and an exception in one handler prevents the remaining ones from running.
    /// Subscribers are therefore expected to be quick and to swallow their own failures.
    /// </remarks>
    public event EventHandler<StatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Raised once per completed round, before the round is yielded to the consumer.
    /// </summary>
    public event EventHandler<RoundCompletedEventArgs>? RoundCompleted;

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

            var round = new CheckRound
            {
                Number = number,
                StartedAt = startedAt,
                Results = results,
            };

            Publish(round);

            yield return round;

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
    /// Notifies subscribers about status transitions and the completed round.
    /// </summary>
    private void Publish(CheckRound round)
    {
        foreach (var result in round.Results)
        {
            var current = result.IsSuccess ? CheckStatus.Healthy : CheckStatus.Failing;
            var previous = _lastStatus.GetValueOrDefault(result.Id, CheckStatus.Unknown);

            if (previous == current)
            {
                continue;
            }

            _lastStatus[result.Id] = current;

            // ?.Invoke reads the delegate field once and calls it only when at least one
            // handler is attached. Writing "if (StatusChanged != null) StatusChanged(...)"
            // instead would leave a window in which the last handler unsubscribes between
            // the check and the call, and the invocation would throw.
            StatusChanged?.Invoke(this, new StatusChangedEventArgs(previous, current, result));
        }

        RoundCompleted?.Invoke(this, new RoundCompletedEventArgs(round));
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
