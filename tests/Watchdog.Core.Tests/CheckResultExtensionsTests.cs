using Xunit;

using static Watchdog.Core.Tests.TestData;

namespace Watchdog.Core.Tests;

public sealed class CheckResultExtensionsTests
{
    [Fact]
    public void SuccessRate_treats_an_empty_sequence_as_healthy()
    {
        Assert.Equal(1.0, Array.Empty<CheckResult>().SuccessRate());
    }

    [Fact]
    public void SuccessRate_counts_the_share_of_successful_checks()
    {
        CheckResult[] results =
        [
            Result("a", success: true),
            Result("a", success: true),
            Result("a", success: true),
            Result("a", success: false),
        ];

        Assert.Equal(0.75, results.SuccessRate());
    }

    [Fact]
    public void PercentileLatency_uses_the_nearest_rank()
    {
        var results = Enumerable.Range(1, 100)
            .Select(value => Result("a", success: true, latencyMs: value))
            .ToArray();

        Assert.Equal(new Latency(95), results.PercentileLatency(95));
        Assert.Equal(new Latency(50), results.PercentileLatency(50));
        Assert.Equal(new Latency(100), results.PercentileLatency(100));
    }

    [Fact]
    public void PercentileLatency_rejects_a_percentile_outside_the_valid_range()
    {
        CheckResult[] results = [Result("a", success: true)];

        Assert.Throws<ArgumentOutOfRangeException>(() => results.PercentileLatency(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => results.PercentileLatency(101));
    }

    [Fact]
    public void ConsecutiveFailures_counts_only_the_failures_at_the_end()
    {
        CheckResult[] results =
        [
            Result("a", success: false),
            Result("a", success: true),
            Result("a", success: false),
            Result("a", success: false),
        ];

        Assert.Equal(2, results.ConsecutiveFailures());
    }

    [Fact]
    public void Recent_returns_the_last_results_oldest_first()
    {
        var results = Enumerable.Range(1, 5)
            .Select(value => Result("a", success: true, latencyMs: value))
            .ToArray();

        Assert.Equal(
            new[] { 4.0, 5.0 },
            results.Recent(2).Select(result => result.Latency.Milliseconds).ToArray());
    }

    [Fact]
    public void Summarize_aggregates_a_single_endpoint()
    {
        CheckResult[] results =
        [
            Result("a", success: true, latencyMs: 10),
            Result("a", success: true, latencyMs: 20),
            Result("a", success: false, latencyMs: 30),
        ];

        var statistics = results.Summarize(new CheckId("a"));

        Assert.Equal(3, statistics.Total);
        Assert.Equal(1, statistics.FailureCount);
        Assert.Equal(1, statistics.ConsecutiveFailures);
        Assert.Equal(new Latency(20), statistics.AverageLatency);
        Assert.False(statistics.IsHealthy);
    }

    [Fact]
    public void Summarize_over_the_history_groups_by_endpoint_and_puts_the_worst_first()
    {
        var history = new CheckHistory();

        history.Add(Result("healthy", success: true));
        history.Add(Result("healthy", success: true));
        history.Add(Result("flaky", success: false));
        history.Add(Result("flaky", success: true));
        history.Add(Result("down", success: false));
        history.Add(Result("down", success: false));

        var summaries = history.Summarize();

        Assert.Equal(
            new[] { "down", "flaky", "healthy" },
            summaries.Select(statistics => statistics.Id.Value).ToArray());

        Assert.Equal(2, summaries[0].ConsecutiveFailures);
        Assert.True(summaries[2].IsHealthy);
    }
}
