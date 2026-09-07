# Kurrent.Surge.Core

The contract layer for the [KurrentDB](https://kurrent.io) .NET SDK. It holds the client interfaces
and the data types they accept and return: options, results, value objects, enums, and filters. It
has **no gRPC and no transport dependencies**.

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
- **Codecs** — `ISchemaCodec` and `SchemaCodecProvider`, with the JSON, Protobuf, and Avro codecs
- **Message declaration** — `[MessageType<T>("wire-name")]`, `[assembly: MessageCatalog(...)]`,
  `[CustomMessageCatalog]`, `SchemaName`, `SchemaDataFormat`, and the naming strategies. The catalog
  generator ships inside this package as an analyzer
- **AOT compatible** — Native-AOT-clean

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-surge)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-surge/blob/main/LICENSE.md)
