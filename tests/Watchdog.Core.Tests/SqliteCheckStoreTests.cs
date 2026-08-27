using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Watchdog.Core;
using Watchdog.Persistence;
using Xunit;

namespace Watchdog.Core.Tests;

/// <summary>
/// Runs against a real SQLite database held in memory.
/// </summary>
/// <remarks>
/// The in-memory database lives as long as its connection, so the connection is opened once
/// and shared by every context the factory hands out. This is not the EF in-memory provider:
/// that one fakes a database and would happily accept queries no relational provider can
/// translate, which is exactly what these tests are meant to catch.
/// </remarks>
public sealed class SqliteCheckStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    private SqliteCheckStore _store = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WatchdogDbContext>()
            .UseSqlite(_connection)
            .Options;

        _store = new SqliteCheckStore(new TestContextFactory(options));

        await _store.InitializeAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task SaveAsync_writes_one_row_per_result()
    {
        await _store.SaveAsync(Round(1, ("a", true, 10), ("b", false, 20)));

        var summaries = await _store.SummarizeSinceAsync(DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(2, summaries.Count);
        Assert.Equal(new[] { "a", "b" }, summaries.Select(summary => summary.EndpointId).ToArray());
    }

    [Fact]
    public async Task SummarizeSinceAsync_aggregates_per_endpoint_in_the_database()
    {
        await _store.SaveAsync(Round(1, ("a", true, 10), ("a", true, 30), ("a", false, 50)));

        var summary = Assert.Single(await _store.SummarizeSinceAsync(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(30, summary.AverageLatencyMilliseconds);
        Assert.Equal(2.0 / 3.0, summary.SuccessRate, 10);
    }

    [Fact]
    public async Task SummarizeSinceAsync_ignores_rows_before_the_cutoff()
    {
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow.AddHours(-3), ("a", true, 10)));
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow, ("a", true, 20)));

        var summary = Assert.Single(await _store.SummarizeSinceAsync(DateTimeOffset.UtcNow.AddHours(-1)));

        Assert.Equal(1, summary.Total);
        Assert.Equal(20, summary.AverageLatencyMilliseconds);
    }

    [Fact]
    public async Task RecentFailuresAsync_returns_the_newest_failures_of_one_endpoint()
    {
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow.AddMinutes(-3), ("a", false, 10)));
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow.AddMinutes(-2), ("a", true, 10)));
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow.AddMinutes(-1), ("a", false, 30)));
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow, ("b", false, 40)));

        var failures = await _store.RecentFailuresAsync(new CheckId("a"), 5);

        Assert.Equal(2, failures.Count);
        Assert.Equal(30, failures[0].LatencyMilliseconds);
    }

    [Fact]
    public async Task PercentileAsync_computes_the_percentile_over_the_persisted_latencies()
    {
        var results = Enumerable.Range(1, 100)
            .Select(value => ("a", true, (double)value))
            .ToArray();

        await _store.SaveAsync(Round(1, results));

        Assert.Equal(new Latency(95), await _store.PercentileAsync(new CheckId("a"), 95));
        Assert.Equal(Latency.Zero, await _store.PercentileAsync(new CheckId("unknown"), 95));
    }

    [Fact]
    public async Task PruneAsync_removes_only_the_rows_past_the_retention_period()
    {
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow.AddDays(-10), ("a", true, 10)));
        await _store.SaveAsync(RoundAt(DateTimeOffset.UtcNow, ("a", true, 20)));

        var removed = await _store.PruneAsync(TimeSpan.FromDays(7));

        Assert.Equal(1, removed);

        var summary = Assert.Single(await _store.SummarizeSinceAsync(DateTimeOffset.UtcNow.AddDays(-30)));
        Assert.Equal(1, summary.Total);
    }

    private static CheckRound Round(int number, params (string Id, bool Success, double LatencyMs)[] results) =>
        RoundAt(DateTimeOffset.UtcNow, results, number);

    private static CheckRound RoundAt(
        DateTimeOffset startedAt,
        params (string Id, bool Success, double LatencyMs)[] results) =>
        RoundAt(startedAt, results, 1);

    private static CheckRound RoundAt(
        DateTimeOffset startedAt,
        (string Id, bool Success, double LatencyMs)[] results,
        int number) => new()
        {
            Number = number,
            StartedAt = startedAt,
            Results =
            [
                .. results.Select(entry => new CheckResult
                {
                    Id = new CheckId(entry.Id),
                    StartedAt = startedAt,
                    Latency = new Latency(entry.LatencyMs),
                    StatusCode = entry.Success ? 200 : 503,
                    FailureReason = entry.Success ? null : "Status 503, expected 200",
                })
            ],
        };
	
	/// <summary>
    /// Hands out a fresh context per call, all sharing the one open in-memory connection.
    /// </summary>
    private sealed class TestContextFactory(DbContextOptions<WatchdogDbContext> options)
        : IDbContextFactory<WatchdogDbContext>
    {
        public WatchdogDbContext CreateDbContext() => new(options);

        public Task<WatchdogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
