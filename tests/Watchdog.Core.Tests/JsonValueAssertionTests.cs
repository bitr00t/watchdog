using Xunit;

namespace Watchdog.Core.Tests;

public sealed class JsonValueAssertionTests
{
    private const string Body = """
        {
          "status": { "indicator": "none", "description": "All Systems Operational" },
          "components": [ { "name": "API" }, { "name": "Git" } ],
          "incidents": 0
        }
        """;

    [Fact]
    public void Validate_accepts_a_matching_value()
    {
        Assert.Null(new JsonValueAssertion("/status/indicator", "none").Validate(Body));
    }

    [Fact]
    public void Validate_addresses_array_elements_by_index()
    {
        Assert.Null(new JsonValueAssertion("/components/1/name", "Git").Validate(Body));
    }

    [Fact]
    public void Validate_compares_non_string_values_by_their_text()
    {
        Assert.Null(new JsonValueAssertion("/incidents", "0").Validate(Body));
    }

    [Fact]
    public void Validate_reports_the_actual_value_on_a_mismatch()
    {
        var failure = new JsonValueAssertion("/status/indicator", "major").Validate(Body);

        Assert.Contains("'none'", failure ?? string.Empty);
        Assert.Contains("'major'", failure ?? string.Empty);
    }

    [Fact]
    public void Validate_reports_a_path_that_does_not_exist()
    {
        var failure = new JsonValueAssertion("/status/missing", "none").Validate(Body);

        Assert.Contains("no value at", failure ?? string.Empty);
    }

    [Fact]
    public void Validate_reports_a_body_that_is_not_json()
    {
        var failure = new JsonValueAssertion("/status", "none").Validate("not json");

        Assert.Contains("not valid JSON", failure ?? string.Empty);
    }

    [Fact]
    public void Constructor_rejects_an_empty_path()
    {
        Assert.Throws<ArgumentException>(() => new JsonValueAssertion("///", "none"));
    }
}
