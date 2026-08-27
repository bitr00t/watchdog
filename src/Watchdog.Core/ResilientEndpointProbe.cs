using Polly;
using Polly.Retry;

namespace Watchdog.Core;

/// <summary>
/// Decorates another probe with a Polly retry pipeline.
/// </summary>
/// <remarks>
/// A failing check is a returned <see cref="CheckResult"/>, not an exception, so the retry
/// predicate inspects the outcome rather than a thrown type. Polly calls that handling a
/// result, and it is the reason the pipeline is typed as
/// <c>ResiliencePipeline&lt;CheckResult&gt;</c> instead of the untyped variant.
///
/// Worth keeping in mind: retries multiply the worst case duration. With two retries and a
/// five second timeout an endpoint can occupy its slot for roughly fifteen seconds plus the
/// backoff delays, so the interval should stay comfortably above that.
/// </remarks>
public sealed class ResilientEndpointProbe : IEndpointProbe
{
    private readonly IEndpointProbe _inner;
    private readonly ResiliencePipeline<CheckResult> _pipeline;

    public ResilientEndpointProbe(
        IEndpointProbe inner,
        int maxRetryAttempts = 2,
        TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetryAttempts, 1);

        _inner = inner;

        _pipeline = new ResiliencePipelineBuilder<CheckResult>()
            .AddRetry(new RetryStrategyOptions<CheckResult>
            {
                // static on the lambda promises the compiler that nothing is captured, so
                // the delegate is allocated once instead of per call.
                ShouldHandle = new PredicateBuilder<CheckResult>()
                    .HandleResult(static result => !result.IsSuccess),
                MaxRetryAttempts = maxRetryAttempts,
                Delay = delay ?? TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    public async Task<CheckResult> ProbeAsync(
        EndpointConfig endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return await _pipeline
            .ExecuteAsync(
                async token => await _inner.ProbeAsync(endpoint, token).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
