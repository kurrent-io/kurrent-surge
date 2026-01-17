
---

<a href="https://kurrent.io">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/kurrent-io/.github/main/profile/KurrentLogo-White.png">
    <source media="(prefers-color-scheme: light)" srcset="https://raw.githubusercontent.com/kurrent-io/.github/main/profile/KurrentLogo-Black.png">
    <img alt="Kurrent" src="https://raw.githubusercontent.com/kurrent-io/.github/main/profile/KurrentLogo-Plum.png" height="35%" width="35%">
  </picture>
</a>

---

# Kurrent .NET Client

[![Build](https://github.com/kurrent-io/Kurrent-Client-DotNet/actions/workflows/build.yml/badge.svg)](https://github.com/kurrent-io/Kurrent-Client-DotNet/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

The official .NET client for [KurrentDB](https://kurrent.io) — an event-native database engineered for modern software applications and event-driven architectures.

## Features

- **Streams** — Append, read, and subscribe to event streams
- **Record Processors** — Build stateful event processors with automatic checkpointing
- **SQL Filter Expressions** — Filter records using familiar SQL-like syntax
- **Automatic SerDe** — Built-in serialization for JSON and Protobuf with schema registry integration
- **Schema Registry** — First-class schema management and validation
- **Dependency Injection** — Native ASP.NET Core integration
- **Observability** — Built-in OpenTelemetry support
- **Multi-target** — Supports .NET 8.0, 9.0, and 10.0

## Installation

```shell
dotnet add package KurrentDB.Client
```

## Connection String

The client uses a URI-based connection string format:

```
kurrentdb://localhost:2113
kurrentdb://user:password@localhost:2113?tls=true
kurrentdb+discover://node1:2113,node2:2113,node3:2113
```

| Parameter | Description | Default |
|-----------|-------------|---------|
| `tls` | Enable TLS encryption | `true` |
| `tlsVerifyCert` | Verify TLS certificate | `true` |
| `keepAliveInterval` | Keep-alive interval (ms) | `10000` |
| `keepAliveTimeout` | Keep-alive timeout (ms) | `10000` |

## Documentation

- [Getting Started Guide](https://developers.kurrent.io/clients/grpc/getting-started/)
- [API Reference](https://developers.kurrent.io/clients/grpc/)
- [Examples](./examples)

## Requirements

- .NET 8.0, 9.0, or 10.0
- KurrentDB 24.10 or later

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## Community

- [Discord](https://discord.gg/kurrent)
- [Discussions](https://github.com/kurrent-io/Kurrent-Client-DotNet/discussions)

## License

This project is licensed under the Apache License 2.0 — see the [LICENSE](LICENSE) file for details.
