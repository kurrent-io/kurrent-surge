# Kurrent .NET SDK

.NET monorepo for the Kurrent platform SDK. Kurrent is a platform, not a database. There is no meta-package;
developers install only what they need.

## Code Style

### Style Precedence

- Code conventions in [project-code-conventions.md](project-code-conventions.md) override file-local style (e.g. `.editorconfig`).
- When conventions are silent, match the majority pattern in the file you are editing.

## Stack

- C# 14 (`LangVersion: preview`), .NET 8.0/9.0/10.0 multi-target
- gRPC + Protocol Buffers (dual protocol: v1 legacy + v2 native)
- System.Text.Json (AOT-ready, no Newtonsoft)
- `.slnx` solution format, Central Package Management
- MinVer for git-tag versioning (prefix `v`, phase `alpha`)

## Protocols

- **V2 gRPC (native)** — All append operations, schema registry, processor management
- **V1 gRPC (legacy)** — All read and stream management operations. Compatibility layer for EventStoreDB;
  will be replaced by V2 as RPCs are implemented

## Testing

```sh
# test all (TUnit flags go after -- separator)
dotnet test test/Kurrent.Client.Tests/ \
  --framework net8.0 \
  -- --disable-logo --fail-fast --adaptive-metrics \
  --output Detailed --log-level Trace

# test single class
dotnet test test/Kurrent.Client.Tests/ \
  --framework net8.0 \
  -- --disable-logo --fail-fast --adaptive-metrics \
  --output Detailed --log-level Trace \
  --treenode-filter "/*/*/ReadTests/*"

# test by category
dotnet test test/Kurrent.Client.Tests/ \
  --framework net8.0 \
  -- --disable-logo --fail-fast --adaptive-metrics \
  --treenode-filter "/*/*/*/*[Category=Streams]"
```

One-time setup: `sudo ./scripts/gencert.sh` generates TLS certs for secure test mode. FluentDocker containers are
reused across runs.

Observability: `cd test/Kurrent.Testing && docker compose up -d` starts Seq (logs, port 5341) and Aspire Dashboard
(telemetry, port 18888).

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

| Script               | Command            | Description                                    |
|----------------------|--------------------|------------------------------------------------|
| `semver-bump.cs`     | `semver-bump`      | Compute next version from conventional commits |
| `benchmark-report.cs`| `benchmark-report` | Generate HTML report from BenchmarkDotNet JSON |
| `nuwrap.cs`          | `nuwrap`           | NuGet package wrapper utility                  |

## Documentation

`docs/` — VuePress 2 site for the SDK section of https://docs.kurrent.io/ (streams, processors, registry,
serialization). Run locally with `./scripts/run-docs.sh` (handles Node, pnpm, and dependency setup).

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                        | When to read                                             |
|-------------------------------------------------|----------------------------------------------------------|
| [Architecture](project-architecture.md)         | Before structural changes, new modules, or protocol work |
| [Code Conventions](project-code-conventions.md) | Before writing new code in unfamiliar areas              |
| [Kurrent.Client](project-client.md)             | Before working on the client library                     |
| [Kurrent.Processors](project-processors.md)     | Before working on stream processing                      |
