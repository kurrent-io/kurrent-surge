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
# test all (TUnit requires dotnet run, NOT dotnet test)
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0

# test single class
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ReadTests/*"

# test by category
dotnet run --project test/Kurrent.Client.Tests/ \
  ... \
  --treenode-filter "/*/*/*/*[Category=Streams]"
```

One-time setup: `sudo ./scripts/gencert.sh` generates TLS certs for secure test mode. FluentDocker containers are
reused across runs.

Observability: `cd test/Kurrent.Testing && docker compose up -d` starts Seq (logs, port 5341) and Aspire Dashboard
(telemetry, port 18888).

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                        | When to read                                             |
|-------------------------------------------------|----------------------------------------------------------|
| [Architecture](project-architecture.md)         | Before structural changes, new modules, or protocol work |
| [Code Conventions](project-code-conventions.md) | Before writing new code in unfamiliar areas              |
| [Kurrent.Client](project-client.md)             | Before working on the client library                     |
| [Kurrent.Processors](project-processors.md)     | Before working on stream processing                      |
