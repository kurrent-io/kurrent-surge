# Testing

## Overview

All test projects use [TUnit](https://tunit.dev/) on top of `Microsoft.Testing.Platform` (not VSTest). The recommended
way to run tests is `dotnet run`, not `dotnet test`. While `dotnet test` works as a compatibility shim, `dotnet run`
launches the test app directly and gives full access to TUnit's CLI.

## Test Projects

| Project | TFMs | Purpose |
|---|---|---|
| `Kurrent.Client.Tests` | net8.0, net9.0, net10.0 | Unit tests for the client library |
| `Kurrent.Client.Integration.Tests` | net10.0 | Integration tests (live KurrentDB) |
| `Kurrent.Processors.Tests` | net8.0, net9.0, net10.0 | Unit tests for Processors |
| `Kurrent.Processors.Integration.Tests` | net8.0, net9.0, net10.0 | Integration tests for Processors |
| `Variant.Tests` | net8.0, net9.0, net10.0 | Unit tests for Variant unions |
| `Kurrent.Testing` | net8.0, net9.0, net10.0 | Shared test infrastructure (not runnable) |
| `Kurrent.SDK.Testing` | net8.0, net9.0, net10.0 | Aspire-based SDK testing |
| `Kurrent.Testing.Datasets` | net8.0, net9.0, net10.0 | Fake/sample domain data |

`Kurrent.Testing` is a shared library referenced by all test projects. It provides the testkit harness, containers,
assertions, logging, and configuration infrastructure. It is not a runnable test project.

## Running Tests

### Why `dotnet run` Instead of `dotnet test`

TUnit uses `Microsoft.Testing.Platform`, not VSTest. `dotnet run` launches the test app directly and provides:

- Full access to all TUnit CLI flags without the `--` separator awkwardness
- Direct process execution (no VSTest adapter overhead)
- Cleaner output and better error reporting
- Native support for `--coverage`, `--report-trx`, and other extensions

`dotnet test` still works but acts as a VSTest compatibility shim. TUnit flags must go after a `--` separator, some
flags may not pass through correctly, and the experience is degraded.

### Basic Commands

```sh
# run all tests in a project
dotnet run --project test/Kurrent.Client.Tests/

# run with a specific framework
dotnet run --project test/Kurrent.Client.Tests/ --framework net8.0

# run in release mode
dotnet run --project test/Kurrent.Client.Tests/ -c Release

# run pre-built tests (skip compilation)
dotnet run --project test/Kurrent.Client.Tests/ -c Release --no-build

# run with detailed output and trace logging
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --output Detailed --log-level Trace --disable-logo

# run integration tests (require live KurrentDB)
dotnet run --project test/Kurrent.Client.Integration.Tests/
```

### Flag Placement

Everything before `--` is a `dotnet` SDK flag. Everything after `--` is a TUnit flag.

```sh
dotnet run --project <path> --framework <tfm> -c <config> --no-build \
  -- --treenode-filter "..." --output Detailed --fail-fast --disable-logo
```

SDK flags (before `--`): `--project`, `--framework` / `-f`, `-c` / `--configuration`, `--no-build`, `--no-restore`

TUnit flags (after `--`): `--treenode-filter`, `--output`, `--log-level`, `--fail-fast`, `--disable-logo`,
`--maximum-parallel-tests`, `--timeout`, `--coverage`, `--report-trx`, etc.

## Test Filtering

TUnit uses tree-node filters instead of VSTest's `--filter` syntax. The pattern is:

```
/Assembly/Namespace/Class/Method[Property=Value]
```

Use `*` as wildcard for segments you don't care about.

### Filter by Class

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --treenode-filter "/*/*/ReadTests/*"
```

### Filter by Method

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --treenode-filter "/*/*/ReadTests/reads_events_from_stream"
```

### Filter by Category

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --treenode-filter "/*/*/*/*[Category=Streams]"
```

### Exclude a Category

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --treenode-filter "/*/*/*/*[Category!=Integration]"
```

### Multiple Filters (OR)

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --treenode-filter "/*/*/ClassA/*|/*/*/ClassB/*"
```

### Multiple Properties (AND)

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  -- --treenode-filter "/*/*/*/*[Category=Integration][Category=Streams]"
```

### List Tests Without Running

```sh
dotnet run --project test/Kurrent.Client.Tests/ -- --list-tests
```

### Available Categories

| Category | Projects |
|---|---|
| `Streams` | Client.Tests, Client.Integration.Tests |
| `Read` | Client.Integration.Tests |
| `Append` | Client.Integration.Tests |
| `Delete` | Client.Integration.Tests |
| `Truncate` | Client.Integration.Tests |
| `Tombstone` | Client.Integration.Tests |
| `StreamInfo` | Client.Integration.Tests |
| `Registry` | Client.Integration.Tests |
| `Indexes` | Client.Integration.Tests |
| `Connectors` | Client.Integration.Tests |
| `Processors` | Processors.Tests, Processors.Integration.Tests |
| `Filters` | Client.Tests |
| `QueryExpressionNormalizer` | Client.Tests |
| `DuckDB` | Client.Tests |
| `Legacy` | Client.Tests |
| `Locks` | Client.Tests |
| `Client` | Client.Tests |
| `Configuration` | Client.Tests |
| `SerDe` | Client.Tests |
| `Variant` | Variant.Tests |
| `Testing` | Kurrent.Testing |
| `Integration` | Client.Integration.Tests, Processors.Integration.Tests |

## Command-Line Flags Reference

### Execution Control

| Flag | Description | Example |
|---|---|---|
| `--fail-fast` | Stop after first failure | `-- --fail-fast` |
| `--timeout <value>` | Global timeout (`5m`, `30s`, `1h`) | `-- --timeout 5m` |
| `--maximum-parallel-tests <n>` | Limit concurrent tests (0 = unlimited) | `-- --maximum-parallel-tests 4` |
| `--ignore-exit-code <codes>` | Don't treat these exit codes as failure | `-- --ignore-exit-code 8` |
| `--minimum-expected-tests <n>` | Fail if fewer tests discovered | `-- --minimum-expected-tests 10` |
| `--exit-on-process-exit <pid>` | Exit if dependent process terminates | `-- --exit-on-process-exit 1234` |

### Output & Reporting

| Flag | Description | Example |
|---|---|---|
| `--output <mode>` | `Normal` (failures only) or `Detailed` (all tests, real-time) | `-- --output Detailed` |
| `--log-level <level>` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` | `-- --log-level Trace` |
| `--disable-logo` | Suppress TUnit ASCII art | `-- --disable-logo` |
| `--no-progress` | Suppress progress reporting | `-- --no-progress` |
| `--no-ansi` | Disable ANSI color codes | `-- --no-ansi` |
| `--results-directory <path>` | Output directory for results | `-- --results-directory ./artifacts` |
| `--adaptive-metrics` | Show adaptive metrics | `-- --adaptive-metrics` |

### Test Reports

| Flag | Description | Example |
|---|---|---|
| `--report-trx` | Generate TRX report | `-- --report-trx` |
| `--report-trx-filename <name>` | Custom TRX filename | `-- --report-trx-filename results.trx` |
| `--github-reporter-style <style>` | `collapsible` (default) or `full` | `-- --github-reporter-style full` |

### Code Coverage

| Flag | Description | Example |
|---|---|---|
| `--coverage` | Enable code coverage collection | `-- --coverage` |
| `--coverage-output <path>` | Coverage report output path | `-- --coverage-output ./coverage/` |
| `--coverage-output-format <fmt>` | `coverage`, `xml`, or `cobertura` | `-- --coverage-output-format cobertura` |
| `--coverage-settings <file>` | XML config for coverage parameters | `-- --coverage-settings coverage.xml` |

### Discovery & Diagnostics

| Flag | Description | Example |
|---|---|---|
| `--list-tests` | List all tests without running | `-- --list-tests` |
| `--treenode-filter <expr>` | Filter tests by tree-node pattern | `-- --treenode-filter "/*/*/MyClass/*"` |
| `--reflection` | Use reflection-based discovery (instead of source gen) | `-- --reflection` |
| `--diagnostic` | Enable diagnostic logging (Trace level) | `-- --diagnostic` |
| `--diagnostic-verbosity <level>` | Diagnostic log level | `-- --diagnostic-verbosity Debug` |
| `--diagnostic-output-directory <p>` | Diagnostic log output dir | `-- --diagnostic-output-directory ./logs` |
| `--info` | Show test app configuration details | `-- --info` |
| `--help` | Show CLI help | `-- --help` |

## Environment Variables

### TUnit Variables

| Variable | Description | Equivalent Flag |
|---|---|---|
| `TUNIT_DISABLE_LOGO` | Suppress ASCII art logo | `--disable-logo` |
| `TUNIT_MAX_PARALLEL_TESTS` | Limit concurrent tests (0 = unlimited) | `--maximum-parallel-tests` |
| `TUNIT_DISABLE_GITHUB_REPORTER` | Disable GitHub Actions test summary | — |
| `TUNIT_GITHUB_REPORTER_STYLE` | `collapsible` (default) or `full` | `--github-reporter-style` |
| `TUNIT_DISABLE_HTML_REPORTER` | Disable HTML report generation | — |
| `TUNIT_DISABLE_JUNIT_REPORTER` | Disable JUnit XML reporter | — |
| `TUNIT_ENABLE_JUNIT_REPORTER` | Enable JUnit XML reporter | — |
| `JUNIT_XML_OUTPUT_PATH` | Custom JUnit XML output path | — |
| `TUNIT_ENABLE_IDE_STREAMING` | Enable real-time IDE test explorer output | — |

### Platform Variables

| Variable | Description |
|---|---|
| `TESTINGPLATFORM_TELEMETRY_OPTOUT` | Disable Microsoft Testing Platform telemetry |
| `TESTINGPLATFORM_UI_LANGUAGE` | Language for platform messages (e.g. `en-us`) |

### Project-Specific Variables

| Variable | Description |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Selects `appsettings.{env}.json` (defaults to `Development`) |
| `KURRENTDB_*` | Any `KURRENTDB_`-prefixed var is merged into the KurrentDB container config |

**Precedence:** CLI flags > environment variables > configuration files.

## Test Infrastructure

### Testkit Harness

All runnable test projects wire up the testkit via `TestkitWireUp.cs`:

```csharp
[assembly: TUnit.Core.Executors.TestExecutor<TestkitTestExecutor>]
```

This provides:
- Per-test GUID injection and OTel metadata
- Scoped Serilog logger per test
- `RpcException` translation into human-readable failures
- Automatic container lifecycle management

### Property Injection

TUnit supports AOT-compatible property injection using `required` properties:

```csharp
public class MyTests {
    [ClassDataSource<KurrentDBTestContainer>(Shared = SharedType.PerTestSession)]
    public required KurrentDBTestContainer Container { get; init; }

    [Test]
    public async Task my_test() {
        // Container is initialized and available
    }
}
```

**Sharing modes:**

| Mode | Behavior |
|---|---|
| `SharedType.PerTestSession` | Single instance for the entire test run |
| `SharedType.PerClass` | One instance per test class |
| `SharedType.Keyed` | Shared by key identifier |
| *(none)* | New instance per test |

Objects implementing `IAsyncInitializer` are initialized automatically. Objects implementing `IAsyncDisposable` are
disposed when their sharing scope ends.

### Dependency Injection

TUnit supports two DI approaches:

- **`IClassConstructor`** — Low-level, direct control over test class instantiation
- **`DependencyInjectionDataSourceAttribute<TScope>`** — Scoped DI container integration (works with
  `Microsoft.Extensions.DependencyInjection`, Autofac, etc.)

### Containers

`Kurrent.Testing` uses FluentDocker (not TestContainers) for container management:

- Containers are reused across runs (`KeepContainer().KeepRunning().ReuseIfExists()`)
- Container settings bind from `appsettings.json` under `TestContainers:{ServiceName}`
- Default KurrentDB image: `docker.kurrent.io/kurrent-latest/kurrentdb:latest`
- Default port: `2113`
- Supports insecure and secure (TLS) modes

### Observability

Start the observability stack before running tests:

```sh
cd test/Kurrent.Testing && docker compose up -d
```

| Service | URL | Port |
|---|---|---|
| Seq (logs) | http://localhost:5341 | 5341 |
| Aspire Dashboard (telemetry) | http://localhost:18888 | 18888 |
| OTLP gRPC endpoint | — | 4317 |
| OTLP HTTP endpoint | — | 4318 |

## Troubleshooting

### Tests don't run / "no tests found"

- Verify the project has `<IsTestProject>true</IsTestProject>` (set in `test/Directory.Build.props` for all projects)
- Check that TUnit is referenced (directly or transitively via `Kurrent.Testing`)
- Run `dotnet run --project <path> -- --list-tests` to see discovered tests
- Run `dotnet run --project <path> -- --info` to see test app configuration

### Filter matches nothing

- Verify the filter pattern: `/Assembly/Namespace/Class/Method[Property=Value]`
- Use `--list-tests` to see the actual tree structure
- Wildcards are `*` for a single segment; use `/*/*/ClassName/*` not `**/ClassName/*`
- Category names are case-sensitive
- OR uses `|`, AND uses consecutive `[Prop=A][Prop=B]`

### TUnit flags not recognized

- With `dotnet run`: flags go after `--` (e.g. `dotnet run --project path -- --fail-fast`)
- With `dotnet test`: flags also go after `--` (e.g. `dotnet test -- --fail-fast`)
- Don't mix SDK flags and TUnit flags on the same side of `--`
- If using `dotnet test`, some TUnit-specific flags may not pass through correctly — switch to `dotnet run`

### Tests hang or time out

- Check if a container failed to start: look at Docker logs
- Use `--timeout 5m` to set a global timeout
- Use `--maximum-parallel-tests 1` to isolate concurrency issues
- Check `--log-level Trace` output for where execution stalls

### Integration tests fail with gRPC "Unimplemented"

- The KurrentDB server version doesn't support the RPC being called
- Check the Docker image tag and version
- Pull the latest image: `docker pull docker.kurrent.io/kurrent-latest/kurrentdb:latest`
- Remove stale containers: `docker rm -f` the old KurrentDB container

### Coverage or TRX reports not generated

- These require extension packages. Verify `Microsoft.Testing.Extensions.CodeCoverage` is in `Directory.Packages.props`
- Use `dotnet run` not `dotnet test` — coverage flags (`--coverage`) are TUnit flags
- Check `--results-directory` for output location

### Tests pass locally but fail in CI

- Check the framework — CI may run a different TFM
- Check for stale Docker images in CI (no persistent cache)
- Environment variables may differ — check `KURRENTDB_*` and `ASPNETCORE_ENVIRONMENT`
- Use `--output Detailed --log-level Trace` in CI for full diagnostics

### One-time setup

```sh
# generate TLS certs for secure test mode
sudo ./scripts/gencert.sh

# start observability services
cd test/Kurrent.Testing && docker compose up -d

# restore tools (needed for local tool commands)
dotnet tool restore
```
