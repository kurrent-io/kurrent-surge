# Kurrent.Surge.Core

The contract layer for the [KurrentDB](https://kurrent.io) .NET SDK. It holds the client interfaces
and the data types they accept and return: options, results, value objects, enums, and filters. It
has **no gRPC, no protobuf, and no transport dependencies**.

Reference this package to depend on the Kurrent client contract without the gRPC client.
`Kurrent.Surge` references `Kurrent.Surge.Core` and implements these interfaces.

## Installation

```shell
dotnet add package Kurrent.Surge.Core
```

### Supported frameworks

- .NET 10.0 or a subsequent version

## What's inside

- **Client interfaces** — `IStreamsClient`, `IAdminClient`, `IIndexesClient`, `IRegistryClient`, `IConnectorsClient`
- **Value types** — `StreamName`, `Revision`, `LogPosition`, `RecordPosition`, `Record`, `Metadata`
- **Options, results, and filters** — the data types those interfaces accept and return
- **Codecs** — `ISchemaCodec` and `SchemaCodecProvider`, with the JSON and protobuf codecs
- **AOT compatible** — Native-AOT-clean, BCL-only

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
