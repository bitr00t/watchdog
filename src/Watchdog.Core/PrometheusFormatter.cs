using System.Globalization;
using System.Text;

namespace Watchdog.Core;

/// <summary>
/// Renders endpoint statistics in the Prometheus text exposition format.
/// </summary>
/// <remarks>
/// Written by hand rather than pulled from a package. The format is a few lines of rules,
/// and doing it manually shows what a client actually receives. The alternatives are worth
/// knowing: prometheus-net offers the usual counter and gauge objects, and
/// <c>System.Diagnostics.Metrics</c> with an OpenTelemetry exporter is the direction .NET is
/// heading, roughly where Micrometer sits in the Java world.
///
/// Three rules bite in practice. Lines end with a single line feed, so
/// <c>Environment.NewLine</c> is wrong on Windows. Numbers are formatted with the invariant
/// culture, because a German machine would otherwise emit <c>0,95</c>. And all samples of a
/// metric family have to be adjacent, which is why this iterates families on the outside and
/// endpoints on the inside.
/// </remarks>
public static class PrometheusFormatter
{
    /// <summary>
    /// Content type a scraper expects for this format.
    /// </summary>
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    // An array of tuples carrying a selector delegate: adding a metric is one line here,
    // and the rendering loop below never changes.
    private static readonly (string Name, string Help, Func<EndpointStatistics, double> Select)[] Families =
    [
        ("watchdog_up",
            "1 when the endpoint is currently not failing, 0 otherwise.",
            statistics => statistics.ConsecutiveFailures == 0 ? 1 : 0),

        ("watchdog_checks_retained",
            "Number of check results currently retained for the endpoint.",
            statistics => statistics.Total),

        ("watchdog_check_failures",
            "Number of failed checks among the retained ones.",
            statistics => statistics.FailureCount),

        ("watchdog_check_success_ratio",
            "Share of successful checks among the retained ones, between 0 and 1.",
            statistics => statistics.SuccessRate),

        ("watchdog_consecutive_failures",
            "Number of failures at the end of the retained window.",
            statistics => statistics.ConsecutiveFailures),

        ("watchdog_latency_average_milliseconds",
            "Average response time over the retained checks.",
            statistics => statistics.AverageLatency.Milliseconds),

        ("watchdog_latency_p95_milliseconds",
            "95th percentile response time over the retained checks.",
            statistics => statistics.P95Latency.Milliseconds),
    ];

    public static string Format(IReadOnlyList<EndpointStatistics> statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        var builder = new StringBuilder();

        foreach (var (name, help, select) in Families)
        {
            builder.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            builder.Append("# TYPE ").Append(name).Append(" gauge").Append('\n');

            foreach (var entry in statistics)
            {
                builder
                    .Append(name)
                    .Append("{endpoint=\"")
                    .Append(EscapeLabelValue(entry.Id.Value))
                    .Append("\"} ")
                    .Append(select(entry).ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a label value as the exposition format requires.
    /// </summary>
    private static string EscapeLabelValue(string value)
    {
        if (value.AsSpan().IndexOfAny('\\', '"', '\n') < 0)
        {
            return value;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
