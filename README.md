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
| `src/Watchdog.Cli` | Entry point and rendering |
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

## Status

Step 6: the endpoint list and all scheduling settings come from a configuration file that is
validated on load, reporting every problem at once. Body checks are available both as a
compile time typed assertion in code and as a path based one from configuration.

Possible next steps: dependency injection with a hosted service, an HTTP or Prometheus
endpoint exposing the statistics, and persistence for the history.
