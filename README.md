# Kurrent .NET SDK

[![Build](https://github.com/kurrent-io/kurrent-surge/actions/workflows/ci.yml/badge.svg)](https://github.com/kurrent-io/kurrent-surge/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

The .NET SDK for the [Kurrent](https://kurrent.io) platform.

This repository contains the official .NET packages for building on Kurrent — a cloud-native event streaming platform. 
There is no meta-package; each package is independently installable, so you pull in only what your application needs.

## Packages

| Package                                                                 | What it does                                                                                          | Install                                              |
|-------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------|--------------------------------------------------------|
| [Kurrent.Surge](src/Kurrent.Surge/)                                     | Append and read records, manage streams, work with the schema registry, drive indexes and connectors, and process records with checkpointing and partitioning | `dotnet add package Kurrent.Surge`                   |
| [Kurrent.Surge.Core](src/Kurrent.Surge.Core/)                           | The contract layer: client interfaces, the data types they accept and return, message declaration and schema names, and the JSON, Protobuf, and Avro codecs — with no transport dependencies | `dotnet add package Kurrent.Surge.Core`              |

See each package's README for quick-start guides, code samples, and detailed feature documentation.

## Documentation

- [API Reference](https://developers.kurrent.io/clients/)
- [Examples](./examples)

## Requirements

- .NET 10.0 or a subsequent version
- KurrentDB 24.10 or a subsequent version

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## Community

- [Discord](https://discord.gg/Phn9pmCw3t)
- [Discussions](https://github.com/kurrent-io/kurrent-surge/discussions)

## License

This project is licensed under the Apache License 2.0 — see the [LICENSE](LICENSE.md) file for details.
