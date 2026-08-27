using Xunit;

using static Watchdog.Core.Tests.TestData;

namespace Watchdog.Core.Tests;

public sealed class PrometheusFormatterTests
{
    [Fact]
    public void Format_emits_help_and_type_before_the_samples_of_a_family()
    {
        var output = PrometheusFormatter.Format(Statistics("a"));

        var lines = output.Split('\n');

        Assert.Equal("# HELP watchdog_up 1 when the endpoint is currently not failing, 0 otherwise.", lines[0]);
        Assert.Equal("# TYPE watchdog_up gauge", lines[1]);
        Assert.StartsWith("watchdog_up{endpoint=\"a\"} ", lines[2]);
    }

    [Fact]
    public void Format_keeps_the_samples_of_one_family_together()
    {
        var output = PrometheusFormatter.Format(Statistics("a", "b"));

        Assert.Contains(
            "watchdog_up{endpoint=\"a\"} 1\nwatchdog_up{endpoint=\"b\"} 1\n",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_uses_line_feeds_only()
    {
        var output = PrometheusFormatter.Format(Statistics("a"));

        Assert.DoesNotContain("\r", output, StringComparison.Ordinal);
        Assert.EndsWith("\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_writes_numbers_with_the_invariant_culture()
    {
        var statistics = new[]
        {
            new[] { Result("a", success: true), Result("a", success: true), Result("a", success: true), Result("a", success: false) }
                .Summarize(new CheckId("a")),
        };

        var output = PrometheusFormatter.Format(statistics);

        Assert.Contains("watchdog_check_success_ratio{endpoint=\"a\"} 0.75\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_reports_a_failing_endpoint_as_down()
    {
        var statistics = new[]
        {
            new[] { Result("a", success: false) }.Summarize(new CheckId("a")),
        };

        var output = PrometheusFormatter.Format(statistics);

        Assert.Contains("watchdog_up{endpoint=\"a\"} 0\n", output, StringComparison.Ordinal);
        Assert.Contains("watchdog_consecutive_failures{endpoint=\"a\"} 1\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_escapes_quotes_and_backslashes_in_label_values()
    {
        var statistics = new[]
        {
            new[] { Result("""a"b\c""", success: true) }.Summarize(new CheckId("""a"b\c""")),
        };

        var output = PrometheusFormatter.Format(statistics);

        Assert.Contains("""watchdog_up{endpoint="a\"b\\c"} 1""", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_still_emits_the_families_without_any_endpoint()
    {
        var output = PrometheusFormatter.Format([]);

        Assert.Contains("# TYPE watchdog_up gauge\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("watchdog_up{", output, StringComparison.Ordinal);
    }

    private static EndpointStatistics[] Statistics(params string[] ids) =>
        [.. ids.Select(id => new[] { Result(id, success: true) }.Summarize(new CheckId(id)))];
}
