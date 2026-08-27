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

## Status

Step 8: the statistics are exposed on a Prometheus scrape endpoint, served by a fourth
hosted service. The monitoring loop publishes an immutable snapshot after every round, so
the endpoint never reads the mutable history.

Possible next steps: persistence for the history, hot reloading the configuration through
IOptionsMonitor, and alerting rules on top of the exported metrics.
