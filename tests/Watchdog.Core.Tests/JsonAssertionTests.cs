using Xunit;

namespace Watchdog.Core.Tests;

public sealed class JsonAssertionTests
{
    private sealed record HealthReport(string Status, int UptimeSeconds);

    [Fact]
    public void Validate_accepts_a_body_that_satisfies_the_predicate()
    {
        var assertion = BodyAssertion.Json<HealthReport>(
            report => report.Status == "healthy",
            "status is healthy");

        Assert.Null(assertion.Validate("""{"status":"healthy","uptimeSeconds":42}"""));
    }

    [Fact]
    public void Validate_matches_property_names_case_insensitively()
    {
        var assertion = BodyAssertion.Json<HealthReport>(
            report => report.UptimeSeconds == 42,
            "uptime is 42");

        Assert.Null(assertion.Validate("""{"Status":"healthy","UPTIMESECONDS":42}"""));
    }

    [Fact]
    public void Validate_reports_the_description_when_the_predicate_fails()
    {
        var assertion = BodyAssertion.Json<HealthReport>(
            report => report.Status == "healthy",
            "status is healthy");

        var failure = assertion.Validate("""{"status":"degraded","uptimeSeconds":42}""");

        Assert.Contains("status is healthy", failure ?? string.Empty);
    }

    [Fact]
    public void Validate_reports_unparsable_json_with_the_expected_properties()
    {
        var assertion = BodyAssertion.Json<HealthReport>(report => true, "anything");

        var failure = assertion.Validate("not json at all");

        Assert.Contains("HealthReport", failure ?? string.Empty);
        Assert.Contains("UptimeSeconds", failure ?? string.Empty);
    }

    [Fact]
    public void Validate_reports_an_empty_body()
    {
        var assertion = BodyAssertion.Json<HealthReport>(report => true, "anything");

        Assert.Contains("empty", assertion.Validate("   ") ?? string.Empty);
    }

    [Fact]
    public void Validate_reports_a_json_null_literal()
    {
        var assertion = BodyAssertion.Json<HealthReport>(report => true, "anything");

        Assert.Contains("null", assertion.Validate("null") ?? string.Empty);
    }

    [Fact]
    public void Constructor_rejects_a_missing_description()
    {
        Assert.Throws<ArgumentException>(
            () => BodyAssertion.Json<HealthReport>(report => true, "  "));
    }
}
