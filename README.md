# Watchdog

A small command line tool that periodically probes HTTP endpoints: status code, response
time and, optionally, an expected substring in the response body.

The project is a learning exercise and deliberately leans on C# concepts that have no
direct counterpart in Java: properties, LINQ, async/await, delegates and events, nullable
reference types, structs as true value types, generics without type erasure and extension
methods.

## Layout

| Project | Purpose |
| --- | --- |
| `src/Watchdog.Core` | Domain logic, no console output |
| `src/Watchdog.Persistence` | EF Core context and SQLite store |
| `src/Watchdog.Cli` | Host, composition root and hosted services |
| `tests/Watchdog.Core.Tests` | xUnit tests against a WireMock server |

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

## Storage

With `storage.enabled` set, every round is written to a SQLite database. The schema is
created on startup with `EnsureCreated`, and checks older than `retentionDays` are removed
at the same time. Changing the model means deleting the file; a project that needs to keep
its data would use EF Core migrations instead.

The store also answers queries over the persisted history: an aggregate per endpoint since a
point in time, the most recent failures of one endpoint, and a percentile over its
latencies.

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

## Status

Step 9: completed rounds are persisted to SQLite through EF Core, and the store answers
aggregate queries that are translated to SQL rather than evaluated in memory.

Possible next steps: EF Core migrations instead of EnsureCreated, hot reloading the
configuration through IOptionsMonitor, and native AOT publishing, which would mean replacing
the reflection based JSON handling with a source generator.
