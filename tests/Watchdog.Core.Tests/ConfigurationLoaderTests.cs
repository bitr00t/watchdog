using Xunit;

namespace Watchdog.Core.Tests;

public sealed class ConfigurationLoaderTests
{
    private const string Minimal = """
        {
          "endpoints": [
            { "id": "a", "url": "https://example.com/" }
          ]
        }
        """;

    [Fact]
    public void Parse_fills_in_the_defaults_of_omitted_settings()
    {
        var configuration = ConfigurationLoader.Parse(Minimal);

        Assert.Equal(TimeSpan.FromSeconds(30), configuration.Options.Interval);
        Assert.Equal(8, configuration.Options.MaxConcurrency);
        Assert.Null(configuration.Options.Rounds);
        Assert.Equal(2, configuration.Retry.MaxAttempts);
        Assert.Equal("watchdog.log", configuration.LogFilePath);
        Assert.False(configuration.Metrics.Enabled);
        Assert.Equal(9464, configuration.Metrics.Port);
        Assert.Equal("/metrics", configuration.Metrics.Path);
        Assert.False(configuration.Storage.Enabled);
        Assert.Equal("watchdog.db", configuration.Storage.DatabasePath);
        Assert.Equal(7, configuration.Storage.RetentionDays);

        var endpoint = Assert.Single(configuration.Endpoints);
        Assert.Equal(200, endpoint.ExpectedStatus);
        Assert.Equal(TimeSpan.FromSeconds(5), endpoint.Timeout);
        Assert.Null(endpoint.BodyContains);
        Assert.Null(endpoint.BodyAssertion);
    }

    [Fact]
    public void Parse_reads_every_supported_setting()
    {
        var configuration = ConfigurationLoader.Parse("""
            {
              "intervalSeconds": 15,
              "maxConcurrency": 3,
              "rounds": 2,
              "logFile": "checks.log",
              "metrics": { "enabled": true, "port": 9500, "path": "/stats" },
              "storage": { "enabled": true, "databasePath": "history.db", "retentionDays": 30 },
              "retry": { "maxAttempts": 4, "delayMilliseconds": 100 },
              "endpoints": [
                {
                  "id": "a",
                  "url": "https://example.com/health",
                  "expectedStatus": 204,
                  "timeoutSeconds": 9,
                  "bodyContains": "ok",
                  "jsonPath": "/status/indicator",
                  "jsonEquals": "none"
                }
              ]
            }
            """);

        Assert.Equal(TimeSpan.FromSeconds(15), configuration.Options.Interval);
        Assert.Equal(3, configuration.Options.MaxConcurrency);
        Assert.Equal(2, configuration.Options.Rounds);
        Assert.Equal(4, configuration.Retry.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(100), configuration.Retry.Delay);
        Assert.Equal("checks.log", configuration.LogFilePath);
        Assert.True(configuration.Metrics.Enabled);
        Assert.Equal(9500, configuration.Metrics.Port);
        Assert.Equal("/stats", configuration.Metrics.Path);
        Assert.True(configuration.Storage.Enabled);
        Assert.Equal("history.db", configuration.Storage.DatabasePath);
        Assert.Equal(30, configuration.Storage.RetentionDays);

        var endpoint = Assert.Single(configuration.Endpoints);
        Assert.Equal("a", endpoint.Id.Value);
        Assert.Equal(204, endpoint.ExpectedStatus);
        Assert.Equal(TimeSpan.FromSeconds(9), endpoint.Timeout);
        Assert.Equal("ok", endpoint.BodyContains);
        Assert.NotNull(endpoint.BodyAssertion);
    }

    [Fact]
    public void Parse_tolerates_comments_and_trailing_commas()
    {
        var configuration = ConfigurationLoader.Parse("""
            {
              // the endpoints to watch
              "endpoints": [
                { "id": "a", "url": "https://example.com/" },
              ],
            }
            """);

        Assert.Single(configuration.Endpoints);
    }

    [Fact]
    public void Parse_collects_every_problem_instead_of_stopping_at_the_first()
    {
        var exception = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Parse("""
            {
              "intervalSeconds": 0,
              "maxConcurrency": 0,
              "endpoints": [
                { "url": "https://example.com/" },
                { "id": "b", "url": "not a url" },
                { "id": "c", "url": "https://example.com/", "expectedStatus": 999 }
              ]
            }
            """));

        Assert.Equal(5, exception.Errors.Count);
        Assert.Contains(exception.Errors, error => error.Contains("intervalSeconds"));
        Assert.Contains(exception.Errors, error => error.Contains("endpoints[0].id"));
        Assert.Contains(exception.Errors, error => error.Contains("endpoints[1].url"));
        Assert.Contains(exception.Errors, error => error.Contains("endpoints[2].expectedStatus"));
    }

    [Fact]
    public void Parse_rejects_an_invalid_metrics_section()
    {
        var exception = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Parse("""
            {
              "metrics": { "port": 70000, "path": "stats" },
              "endpoints": [
                { "id": "a", "url": "https://example.com/" }
              ]
            }
            """));

        Assert.Contains(exception.Errors, error => error.Contains("metrics.port"));
        Assert.Contains(exception.Errors, error => error.Contains("metrics.path"));
    }

    [Fact]
    public void Parse_rejects_duplicate_endpoint_ids()
    {
        var exception = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Parse("""
            {
              "endpoints": [
                { "id": "a", "url": "https://example.com/1" },
                { "id": "A", "url": "https://example.com/2" }
              ]
            }
            """));

        Assert.Contains(exception.Errors, error => error.Contains("used more than once"));
    }

    [Fact]
    public void Parse_rejects_an_empty_endpoint_list()
    {
        var exception = Assert.Throws<ConfigurationException>(
            () => ConfigurationLoader.Parse("""{ "endpoints": [] }"""));

        Assert.Contains(exception.Errors, error => error.Contains("at least one entry"));
    }

    [Fact]
    public void Parse_requires_jsonPath_and_jsonEquals_together()
    {
        var exception = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Parse("""
            {
              "endpoints": [
                { "id": "a", "url": "https://example.com/", "jsonPath": "/status" }
              ]
            }
            """));

        Assert.Contains(exception.Errors, error => error.Contains("jsonEquals is required"));
    }

    [Fact]
    public void Parse_reports_malformed_json_with_the_source_name()
    {
        var exception = Assert.Throws<ConfigurationException>(
            () => ConfigurationLoader.Parse("{ not json", "watchdog.json"));

        Assert.Contains("watchdog.json", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_reads_a_file_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watchdog-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, Minimal);

        try
        {
            var configuration = await ConfigurationLoader.LoadAsync(path);

            Assert.Single(configuration.Endpoints);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_reports_a_missing_file_as_a_configuration_problem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watchdog-{Guid.NewGuid():N}.json");

        var exception = await Assert.ThrowsAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(path));

        Assert.Contains(path, exception.Message);
    }
}
