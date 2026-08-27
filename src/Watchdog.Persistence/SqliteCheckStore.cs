using Microsoft.EntityFrameworkCore;
using Watchdog.Core;

namespace Watchdog.Persistence;

/// <summary>
/// Stores and queries check results in SQLite.
/// </summary>
/// <remarks>
/// A <c>DbContext</c> is a unit of work and is not thread safe, so this takes an
/// <see cref="IDbContextFactory{TContext}"/> and creates one per operation. Injecting a
/// context into a singleton would be the classic captive dependency: one context alive for
/// the whole process, its change tracker growing without bound.
/// </remarks>
public sealed class SqliteCheckStore(IDbContextFactory<WatchdogDbContext> contextFactory) : ICheckResultStore
{
    /// <summary>
    /// Creates the database file and schema if they do not exist yet.
    /// </summary>
    /// <remarks>
    /// EnsureCreated is the shortcut: it builds the schema from the model and does nothing
    /// afterwards, so a later model change means deleting the file. Real projects use
    /// migrations (<c>dotnet ef migrations add</c>), which produce versioned, reviewable
    /// schema changes, roughly what Flyway or Liquibase do in the Java world.
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(CheckRound round, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(round);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Select runs in memory here: round.Results is a plain list, so this is ordinary
        // LINQ to Objects and the lambda is compiled to a delegate like any other.
        var records = round.Results.Select(result => new CheckRecord
        {
            EndpointId = result.Id.Value,
            RoundNumber = round.Number,
            StartedAt = result.StartedAt.ToUniversalTime(),
            LatencyMilliseconds = result.Latency.Milliseconds,
            StatusCode = result.StatusCode,
            IsSuccess = result.IsSuccess,
            FailureReason = Truncate(result.FailureReason, 500),
        });

        context.Checks.AddRange(records);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Aggregates the persisted checks per endpoint, entirely in the database.
    /// </summary>
    /// <remarks>
    /// Nothing in this method runs the lambdas. Against an <c>IQueryable</c> the compiler
    /// builds an <c>Expression&lt;Func&lt;...&gt;&gt;</c>, a data structure describing the
    /// code, and the provider walks it to produce SQL. Only <c>ToListAsync</c> sends
    /// anything to the database, and only the aggregated rows come back.
    ///
    /// This is the single largest difference to Java. A Java lambda is always an
    /// implementation of a functional interface; there is no form in which the compiler
    /// hands the library the shape of the expression instead of its behaviour.
    /// </remarks>
    public async Task<IReadOnlyList<EndpointSummary>> SummarizeSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var utc = since.ToUniversalTime();

        return await context.Checks
            .Where(record => record.StartedAt >= utc)
            .GroupBy(record => record.EndpointId)
            .Select(group => new EndpointSummary(
                group.Key,
                group.Count(),
                group.Count(record => !record.IsSuccess),
                group.Average(record => record.LatencyMilliseconds),
                group.Max(record => record.StartedAt)))
            .OrderBy(summary => summary.EndpointId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The most recent failures of one endpoint, newest first.
    /// </summary>
    public async Task<IReadOnlyList<CheckRecord>> RecentFailuresAsync(
        CheckId id,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.Checks
            .AsNoTracking()
            .Where(record => record.EndpointId == id.Value && !record.IsSuccess)
            .OrderByDescending(record => record.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Nearest-rank percentile of the persisted latencies of one endpoint.
    /// </summary>
    /// <remarks>
    /// SQLite has no percentile function and the provider cannot invent one, so the
    /// computation happens in memory. The important part is where the boundary sits: the
    /// filter and the column selection are translated, so only one <c>double</c> column of
    /// the matching rows crosses the wire. Writing the filter after the boundary instead
    /// would fetch the whole table and throw most of it away, which is the classic way to
    /// make an ORM look slow.
    /// </remarks>
    public async Task<Latency> PercentileAsync(
        CheckId id,
        int percentile,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percentile, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var latencies = await context.Checks
            .AsNoTracking()
            .Where(record => record.EndpointId == id.Value)
            .Select(record => record.LatencyMilliseconds)
            .OrderBy(value => value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Everything from here on is LINQ to Objects again.
        if (latencies.Count == 0)
        {
            return Latency.Zero;
        }

        var rank = (int)Math.Ceiling(percentile / 100.0 * latencies.Count);

        return new Latency(latencies[Math.Clamp(rank - 1, 0, latencies.Count - 1)]);
    }

    /// <summary>
    /// Deletes checks older than the retention period and reports how many rows went.
    /// </summary>
    /// <remarks>
    /// ExecuteDeleteAsync issues a single DELETE statement. The alternative, loading the
    /// entities and calling RemoveRange, would materialize every row, track it and send one
    /// statement per row.
    /// </remarks>
    public async Task<int> PruneAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var cutoff = DateTimeOffset.UtcNow - retention;

        return await context.Checks
            .Where(record => record.StartedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength
            ? value[..maxLength]
            : value;
}
