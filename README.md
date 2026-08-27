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

.NET SDK 9.0 or newer. For a different SDK version, adjust `TargetFramework` in
`Directory.Build.props`, for example to `net10.0`.

## Build and run

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Watchdog.Cli
```

The sample run performs three rounds, prints a summary and exits with code 1 on purpose:
two of the three demo endpoints are expected to fail. Ctrl+C stops it earlier.

## Status

Step 3: results are retained per endpoint in a bounded history and aggregated into per
endpoint statistics (success rate, average and p95 latency, current outage length). The CLI
prints a summary once the run ends. Events and notification sinks follow in step 4.
