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

The sample run performs three rounds and exits with code 1 on purpose: two of the three
demo endpoints are expected to fail. Ctrl+C stops it earlier.

## Status

Step 2: all endpoints of a round are probed in parallel, bounded by a configurable
concurrency limit, and the round repeats on an interval until a round limit is reached or
the caller cancels. History, statistics and events follow in the next steps.
