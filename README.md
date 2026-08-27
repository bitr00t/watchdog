# Watchdog

A small command line tool that periodically probes HTTP endpoints: status code, response
time and, optionally, an assertion over the response body. Failed checks are retried,
results are aggregated per endpoint, exposed to Prometheus and persisted to SQLite.

The project is a learning exercise. It was written to pick up C# and .NET coming from about
ten years of Java, and it deliberately leans on concepts that have no direct counterpart
there: properties, LINQ and `IQueryable`, async/await and `IAsyncEnumerable`, delegates and
events, nullable reference types, structs as true value types, generics without type
erasure, and extension methods.

That is also why the history is worth more than the current state of the code. It was built
in nine steps, each one a branch and a pull request, each adding one idea and the tests for
it. The pull requests carry the reasoning behind the design decisions, and the code is
commented with the same intent: not what a line does, but why it looks the way it does in
C# and not the way it would in Java.

## How it was built

| Step | What it added |
| --- | --- |
| 1 | A single probe: value types, records, nullable reference types, `HttpClient` |
| 2 | Parallel execution on an interval, yielded as an `IAsyncEnumerable` |
| 3 | A bounded history and LINQ statistics as extension methods |
| 4 | Events with two independent subscribers, and Polly retries |
| 5 | Typed body assertions, with per type metadata cached in a generic static class |
| 6 | A validated configuration file instead of a hard coded endpoint list |
| 7 | The generic host, dependency injection and hosted services |
| 8 | A Prometheus scrape endpoint |
| 9 | Persistence with EF Core and SQLite |

## Layout

| Project | Purpose |
| --- | --- |
| `src/Watchdog.Core` | Domain logic, no console output |
| `src/Watchdog.Persistence` | EF Core context and SQLite store |
| `src/Watchdog.Cli` | Host, composition root and hosted services |
| `tests/Watchdog.Core.Tests` | xUnit tests against WireMock and SQLite |

## Requirements

.NET SDK 10.0 or newer. For a different SDK version, adjust `TargetFramework` in
`Directory.Build.props`.

## Build and run

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Watchdog.Cli
dotnet run --project src/Watchdog.Cli -- C:\path\to\other.json
```

The endpoints come from `watchdog.json`, which is copied next to the executable on build.
The first command line argument overrides the path. The sample configuration performs three
rounds, prints a summary and exits with code 1 on purpose: two of its four endpoints are
expected to fail. Ctrl+C stops it earlier. An invalid configuration exits with code 2 and
lists every problem it found.

## Configuration

```json
{
  "intervalSeconds": 10,
  "maxConcurrency": 4,
  "rounds": 3,
  "logFile": "watchdog.log",
  "metrics": { "enabled": true, "port": 9464, "path": "/metrics" },
  "storage": { "enabled": true, "databasePath": "watchdog.db", "retentionDays": 7 },
  "retry": { "maxAttempts": 2, "delayMilliseconds": 250 },
  "endpoints": [
    {
      "id": "github-status",
      "url": "https://www.githubstatus.com/api/v2/status.json",
      "jsonPath": "/status/indicator",
      "jsonEquals": "none"
    }
  ]
}
```

Only `endpoints` with an `id` and a `url` is required; everything else has a default.
Per endpoint, `expectedStatus`, `timeoutSeconds`, `bodyContains` and the pair
`jsonPath`/`jsonEquals` are optional. Omitting `rounds` runs until cancelled. Comments and
trailing commas are tolerated.

## Metrics

With `metrics.enabled` set, the statistics are exposed in the Prometheus text exposition
format:

```powershell
curl http://localhost:9464/metrics
```

One gauge family per measurement, labelled by endpoint: `watchdog_up`,
`watchdog_checks_retained`, `watchdog_check_failures`, `watchdog_check_success_ratio`,
`watchdog_consecutive_failures`, `watchdog_latency_average_milliseconds` and
`watchdog_latency_p95_milliseconds`. All of them describe the retained window, not the whole
run. The listener binds to `localhost` only.

## Storage

With `storage.enabled` set, every round is written to a SQLite database. The schema is
created on startup with `EnsureCreated`, and checks older than `retentionDays` are removed
at the same time. Changing the model means deleting the file; a project that needs to keep
its data would use EF Core migrations instead.

The store also answers queries over the persisted history: an aggregate per endpoint since a
point in time, the most recent failures of one endpoint, and a percentile over its
latencies. The aggregation is translated to SQL rather than evaluated in memory, and the one
query that cannot be translated crosses back into memory deliberately, after the filtering
has already run in the database.

## Status

Feature complete for what it set out to be. Possible next steps: source generated JSON and
a native AOT build, EF Core migrations instead of `EnsureCreated`, and hot reloading the
configuration through `IOptionsMonitor`.
