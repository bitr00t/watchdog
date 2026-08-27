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
```

The sample run performs three rounds, prints a summary and exits with code 1 on purpose:
two of the three demo endpoints are expected to fail. Ctrl+C stops it earlier.

## Status

Step 4: the engine publishes StatusChanged and RoundCompleted events, and the CLI attaches
two independent subscribers to them, a console reporter and a file logger writing
`watchdog.log`. Failing checks are retried through a Polly pipeline before they count as a
failure.
