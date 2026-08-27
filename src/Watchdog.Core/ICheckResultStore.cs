namespace Watchdog.Core;

/// <summary>
/// Persists completed rounds.
/// </summary>
/// <remarks>
/// Declared here so the core stays free of any storage technology, and implemented in
/// Watchdog.Persistence. Saving is deliberately not wired to the RoundCompleted event:
/// event handlers return void, so an asynchronous one would have to be fire and forget, and
/// nobody would notice a failed write. The worker awaits this instead.
/// </remarks>
public interface ICheckResultStore
{
    Task SaveAsync(CheckRound round, CancellationToken cancellationToken = default);
}

/// <summary>
/// Does nothing, for runs configured without storage.
/// </summary>
/// <remarks>
/// A null object rather than a nullable dependency. The worker has no branch for the
/// disabled case, and the decision lives in exactly one place, the composition root.
/// </remarks>
public sealed class NullCheckResultStore : ICheckResultStore
{
    public Task SaveAsync(CheckRound round, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
