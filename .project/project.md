# Kurrent .NET SDK

.NET monorepo for the Kurrent platform SDK. Kurrent is a platform, not a database. 

There is no meta-package; developers install only what they need.

- Kurrent.Client: gRPC client library for reading and writing streams to KurrentDB
- Kurrent.Processors: stream processing library for processing streams (uses Kurrent.Client under the hood)

## Greenfield Project

This project is a greenfield project. There is no legacy code, and no existing user base. We have the freedom to 
design the API and implementation with modern practices and patterns, without needing to maintain backward 
compatibility or support legacy platforms.

## Code Style

### Style Precedence

- Code conventions in [project-code-conventions.md](project-code-conventions.md) override file-local style (e.g. `.editorconfig`).
- When conventions are silent, match the majority pattern in the file you are editing.

## Testing

*** Run all Unit tests:***
```sh
dotnet test -c Release --ignore-exit-code 8 --results-directory ./artifacts/test-results \
-- --treenode-filter "/*/*/*/*[Category!=Integration]" \
--adaptive-metrics --report-trx --coverage --output Detailed --log-level Trace
```

*** Run all Integration tests:***
```sh
dotnet test -c Release --ignore-exit-code 8 --results-directory ./artifacts/test-results \
-- --treenode-filter "/*/*/*/*[Category=Integration]" \
--adaptive-metrics --report-trx --coverage --output Detailed --log-level Trace --fail-fast 
```

*** Run a single test class:***
```sh
dotnet test -c Release --results-directory ./artifacts/test-results \
-- --treenode-filter "/*/*/ReadTests/*" \
--adaptive-metrics --report-trx --coverage --output Detailed --log-level Trace
```

*** Run a category of tests: (eg. Streams)***
```sh
dotnet test -c Release --ignore-exit-code 8 --results-directory ./artifacts/test-results \
-- --treenode-filter "/*/*/*/*[Category=SerDe]" \
--adaptive-metrics --report-trx --coverage --output Detailed --log-level Trace
```

One-time setup: `sudo ./scripts/gencert.sh` generates TLS certs for secure test mode. FluentDocker containers are
reused across runs.

## Tools

`tools/` contains .NET 10 file-based apps (single `.cs` files with `#:` directives). Tools with a `ToolCommandName`
property are registered as local tools in `.config/dotnet-tools.json`.

After cloning, restore once with `dotnet tool restore`.

### Running tools

| Method              | Example                                    | Notes                          |
|---------------------|--------------------------------------------|--------------------------------|
| Local tool          | `dotnet semver-bump check`                 | Requires `dotnet tool restore` |
| dnx                 | `dnx semver-bump check`                    | No install needed              |
| Script              | `dotnet run tools/semver-bump.cs -- check` | Runs from source directly      |

### Available tools

| Script                | Command            | Description                                    |
|-----------------------|--------------------|------------------------------------------------|
| `semver-bump.cs`      | `semver-bump`      | Compute next version from conventional commits |
| `benchmark-report.cs` | `benchmark-report` | Generate HTML report from BenchmarkDotNet JSON |
| `nuwrap.cs`           | `nuwrap`           | NuGet package wrapper utility                  |

## Documentation

`docs/` — VuePress 2 site for the SDK section of https://docs.kurrent.io/ (streams, processors, registry,
serialization). 

Run locally with `./scripts/run-docs.sh` (handles Node, pnpm, and dependency setup).

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                        | When to read                                             |
|-------------------------------------------------|----------------------------------------------------------|
| [Architecture](project-architecture.md)         | Before structural changes, new modules, or protocol work |
| [Code Conventions](project-code-conventions.md) | Before writing new code in unfamiliar areas              |
| [Kurrent.Client](project-client.md)             | Before working on the client library                     |
| [Kurrent.Processors](project-processors.md)     | Before working on stream processing                      |
| [Testing](project-testing.md)                   | Before running tests or changing test infrastructure     |
