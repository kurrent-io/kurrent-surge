
# Kurrent .NET SDK

[![Build](https://github.com/kurrent-io/kurrent-sdk-dotnet/actions/workflows/build.yml/badge.svg)](https://github.com/kurrent-io/kurrent-sdk-dotnet/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

The .NET SDK for the [Kurrent](https://kurrent.io) platform.

This repository contains the official .NET packages for building on Kurrent — a cloud-native event streaming platform. 
There is no meta-package; each package is independently installable, so you pull in only what your application needs.

## Packages

| Package                                       | What it does                                                                                                         | Install                                 |
|-----------------------------------------------|----------------------------------------------------------------------------------------------------------------------|-----------------------------------------|
| [Kurrent.Client](src/Kurrent.Client/)         | Append and read records, manage streams, work with the schema registry, and interact with connectors                 | `dotnet add package Kurrent.Client`     |
| [Kurrent.Processors](src/Kurrent.Processors/) | Subscribe to and process records from the log with automatic checkpointing, partitioning, and exactly-once semantics | `dotnet add package Kurrent.Processors` |

See each package's README for quick-start guides, code samples, and detailed feature documentation.

## Documentation

- [API Reference](https://developers.kurrent.io/clients/)
- [Examples](./examples)

## Requirements

- .NET 10.0
- KurrentDB 24.10 or later

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## Community

- [Discord](https://discord.gg/kurrent)
- [Discussions](https://github.com/kurrent-io/Kurrent.Client/discussions)

## License

This project is licensed under the Apache License 2.0 — see the [LICENSE](LICENSE) file for details.
