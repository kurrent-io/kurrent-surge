# Kurrent.Abstractions

The contract layer for the [KurrentDB](https://kurrent.io) .NET SDK — the client interfaces and the
data types they accept and return (options, results, value objects, enums, filters), with **no gRPC,
no protobuf, and no transport dependencies**.

Reference this package to depend on the Kurrent client contract without pulling in the gRPC client
implementation. `Kurrent.Client` references `Kurrent.Abstractions` and implements these interfaces.

## Installation

```shell
dotnet add package Kurrent.Abstractions
```

### Supported Frameworks

- .NET 10.0

## What's inside

- **Client interfaces** — `IStreamsClient`, `IAdminClient`, `IIndexesClient`, `IRegistryClient`, `IConnectorsClient`
- **Value types** — `StreamName`, `Revision`, `LogPosition`, `RecordPosition`, `Record`, `Metadata`
- **Options, results, and filters** — the data types those interfaces accept and return
- **AOT Compatible** — Native-AOT-clean, BCL-only

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
