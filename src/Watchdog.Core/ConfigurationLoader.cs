using System.Text.Json;

namespace Watchdog.Core;

/// <summary>
/// Reads and validates a watchdog configuration file.
/// </summary>
/// <remarks>
/// The file is parsed into a separate set of DTOs whose properties are all nullable, and
/// only then mapped onto the domain types. That separation is what makes the nullable
/// annotations honest: outside data genuinely may be missing, while
/// <see cref="EndpointConfig"/> uses <c>required</c> and never holds a half-filled state.
/// Deserializing straight into the domain types would force every one of them to accept
/// nulls that the rest of the program then has to keep re-checking.
/// </remarks>
public static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Reads the configuration from a file.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// The file is missing, unreadable, malformed or invalid.
    /// </exception>
    public static async Task<WatchdogConfiguration> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;

        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Cannot read '{path}': {exception.Message}", exception);
        }

        return Parse(json, path);
    }

    /// <summary>
    /// Parses and validates configuration content that is already in memory.
    /// </summary>
    public static WatchdogConfiguration Parse(string json, string source = "configuration")
    {
        ArgumentNullException.ThrowIfNull(json);

        ConfigurationFile? file;

        try
        {
            file = JsonSerializer.Deserialize<ConfigurationFile>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException($"'{source}' is not valid JSON: {exception.Message}", exception);
        }

        if (file is null)
        {
            throw new ConfigurationException($"'{source}' is empty.");
        }

        var errors = new List<string>();
        var endpoints = MapEndpoints(file, errors);
        var options = MapOptions(file, errors);
        var retry = MapRetry(file.Retry, errors);
        var metrics = MapMetrics(file.Metrics, errors);

        if (errors.Count > 0)
        {
            throw new ConfigurationException(errors);
        }

        return new WatchdogConfiguration
        {
            Options = options,
            Retry = retry,
            Endpoints = endpoints,
            LogFilePath = string.IsNullOrWhiteSpace(file.LogFile) ? "watchdog.log" : file.LogFile,
            Metrics = metrics,
        };
    }

    private static WatchdogOptions MapOptions(ConfigurationFile file, List<string> errors)
    {
        var interval = TimeSpan.FromSeconds(file.IntervalSeconds ?? 30);

        if (interval <= TimeSpan.Zero)
        {
            errors.Add("intervalSeconds must be greater than 0.");
        }

        var maxConcurrency = file.MaxConcurrency ?? 8;

        if (maxConcurrency < 1)
        {
            errors.Add("maxConcurrency must be at least 1.");
        }

        if (file.Rounds is { } rounds && rounds < 1)
        {
            errors.Add("rounds must be at least 1, or omitted to run until cancelled.");
        }

        return new WatchdogOptions
        {
            Interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(30),
            MaxConcurrency = Math.Max(maxConcurrency, 1),
            Rounds = file.Rounds,
        };
    }

    private static RetryConfiguration MapRetry(RetryFile? retry, List<string> errors)
    {
        var maxAttempts = retry?.MaxAttempts ?? 2;
        var delayMs = retry?.DelayMilliseconds ?? 250;

        if (maxAttempts < 1)
        {
            errors.Add("retry.maxAttempts must be at least 1.");
        }

        if (delayMs < 0)
        {
            errors.Add("retry.delayMilliseconds must not be negative.");
        }

        return new RetryConfiguration
        {
            MaxAttempts = Math.Max(maxAttempts, 1),
            Delay = TimeSpan.FromMilliseconds(Math.Max(delayMs, 0)),
        };
    }

    private static MetricsConfiguration MapMetrics(MetricsFile? metrics, List<string> errors)
    {
        var port = metrics?.Port ?? 9464;
        var path = string.IsNullOrWhiteSpace(metrics?.Path) ? "/metrics" : metrics.Path;

        if (port is < 1 or > 65535)
        {
            errors.Add("metrics.port must be between 1 and 65535.");
        }

        if (!path.StartsWith('/'))
        {
            errors.Add("metrics.path must start with a slash.");
        }

        return new MetricsConfiguration
        {
            Enabled = metrics?.Enabled ?? false,
            Port = port is >= 1 and <= 65535 ? port : 9464,
            Path = path.StartsWith('/') ? path : "/metrics",
        };
    }

    private static IReadOnlyList<EndpointConfig> MapEndpoints(ConfigurationFile file, List<string> errors)
    {
        if (file.Endpoints is not { Count: > 0 } entries)
        {
            errors.Add("endpoints must contain at least one entry.");
            return [];
        }

        var duplicates = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry?.Id))
            .GroupBy(entry => entry!.Id!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicates)
        {
            errors.Add($"endpoint id '{duplicate}' is used more than once.");
        }

        var endpoints = new List<EndpointConfig>(entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            var mapped = MapEndpoint(entries[index], index, errors);

            if (mapped is not null)
            {
                endpoints.Add(mapped);
            }
        }

        return endpoints;
    }

    private static EndpointConfig? MapEndpoint(EndpointFile? entry, int index, List<string> errors)
    {
        // The position is part of every message: an id may be exactly what is missing.
        var where = $"endpoints[{index}]";

        if (entry is null)
        {
            errors.Add($"{where} is null.");
            return null;
        }

        var failedBefore = errors.Count;

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            errors.Add($"{where}.id is required.");
        }

        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{where}.url must be an absolute http or https URL.");
        }

        var expectedStatus = entry.ExpectedStatus ?? 200;

        if (expectedStatus is < 100 or > 599)
        {
            errors.Add($"{where}.expectedStatus must be between 100 and 599.");
        }

        var timeoutSeconds = entry.TimeoutSeconds ?? 5;

        if (timeoutSeconds <= 0)
        {
            errors.Add($"{where}.timeoutSeconds must be greater than 0.");
        }

        var assertion = MapAssertion(entry, where, errors);

        if (errors.Count != failedBefore)
        {
            return null;
        }

        return new EndpointConfig
        {
            Id = new CheckId(entry.Id!),
            Url = url!,
            ExpectedStatus = expectedStatus,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            BodyContains = entry.BodyContains,
            BodyAssertion = assertion,
        };
    }

    private static IBodyAssertion? MapAssertion(EndpointFile entry, string where, List<string> errors)
    {
        var hasPath = !string.IsNullOrWhiteSpace(entry.JsonPath);
        var hasValue = entry.JsonEquals is not null;

        return (hasPath, hasValue) switch
        {
            (true, true) => new JsonValueAssertion(entry.JsonPath!, entry.JsonEquals!),
            (false, false) => null,
            (true, false) => Fail($"{where}.jsonEquals is required when jsonPath is set."),
            (false, true) => Fail($"{where}.jsonPath is required when jsonEquals is set."),
        };

        IBodyAssertion? Fail(string message)
        {
            errors.Add(message);
            return null;
        }
    }

    // Every property is nullable on purpose: this is what a file may or may not contain,
    // not what the program needs. Missing values turn into defaults during mapping.
    internal sealed record ConfigurationFile
    {
        public int? IntervalSeconds { get; init; }

        public int? MaxConcurrency { get; init; }

        public int? Rounds { get; init; }

        public string? LogFile { get; init; }

        public RetryFile? Retry { get; init; }

        public MetricsFile? Metrics { get; init; }

        public IReadOnlyList<EndpointFile?>? Endpoints { get; init; }
    }

    internal sealed record MetricsFile
    {
        public bool? Enabled { get; init; }

        public int? Port { get; init; }

        public string? Path { get; init; }
    }

    internal sealed record RetryFile
    {
        public int? MaxAttempts { get; init; }

        public int? DelayMilliseconds { get; init; }
    }

    internal sealed record EndpointFile
    {
        public string? Id { get; init; }

        public string? Url { get; init; }

        public int? ExpectedStatus { get; init; }

        public int? TimeoutSeconds { get; init; }

        public string? BodyContains { get; init; }

        public string? JsonPath { get; init; }

        public string? JsonEquals { get; init; }
    }
}
