# Message Compression Extension Packages — Implementation Plan

## Overview

Add message-level compression to the Kurrent .NET SDK as **extension packages**. Compression sits
between serialization and the wire: `Serialize → Compress → Append` and `Read → Decompress → Deserialize`.
A `$compression` metadata entry signals the algorithm so consumers decompress transparently.

## Compression Algorithms

Three packages, each one self-contained:

| Package | Algorithm | Dependency |
|---|---|---|
| `Kurrent.Client.Compression.Zstd` | Zstandard | `ZstdSharp.Port` (managed, no native binaries) |
| `Kurrent.Client.Compression.Gzip` | GZip | `System.IO.Compression` (built-in) |
| `Kurrent.Client.Compression.Brotli` | Brotli | `System.IO.Compression.Brotli` (built-in) |

Why these three:
- **Zstd** — best speed/ratio trade-off, widely used for event payloads
- **GZip** — universal, zero external deps
- **Brotli** — highest ratio of the built-ins, zero external deps

## Architecture

### Core abstractions (in `Kurrent.Client`)

Add a thin compression hook in the **existing client**, so extension packages can plug in without
modifying internals.

**1. `IMessageCompressor` interface** — `src/Kurrent.Client/Schema/Compression/IMessageCompressor.cs`

```csharp
namespace Kurrent.Client.Schema.Compression;

public interface IMessageCompressor {
    /// A stable identifier stored in $compression metadata (e.g. "zstd", "gzip", "br").
    string CompressionType { get; }

    ReadOnlyMemory<byte> Compress(ReadOnlyMemory<byte> data);
    ReadOnlyMemory<byte> Decompress(ReadOnlyMemory<byte> data);
}
```

Sync-only by design — compression is CPU-bound, no async needed. `ReadOnlyMemory<byte>` matches the
existing serializer return type.

**2. `IMessageCompressionProvider` interface** — `src/Kurrent.Client/Schema/Compression/IMessageCompressionProvider.cs`

```csharp
namespace Kurrent.Client.Schema.Compression;

public interface IMessageCompressionProvider {
    /// The compressor used for outgoing messages (write-side). Null = no compression.
    IMessageCompressor? WriteCompressor { get; }

    /// Get a compressor by type name for incoming messages (read-side).
    /// Returns null if the type is unknown (records written without compression).
    IMessageCompressor? GetCompressor(string compressionType);
}
```

**3. `MessageCompressionProvider`** — `src/Kurrent.Client/Schema/Compression/MessageCompressionProvider.cs`

Default implementation. Holds a dictionary of registered compressors keyed by `CompressionType`
and an optional write compressor.

**4. `$compression` metadata key** — add to `SystemMetadataKeys`

```csharp
public const string Compression = "$compression";
```

Also add it to `IsKnownSystemMetadataKey`.

### Write path integration

In `StreamsProtocolMapper.ToContract(AppendRecord, ct)` and the legacy `StreamsClientV2Mapper.Map`,
**after** serialization and **before** building the gRPC contract:

```csharp
// after: var data = await serializer.Serialize(...)
if (CompressionProvider?.WriteCompressor is { } compressor) {
    data = compressor.Compress(data);
    source.Metadata.With(SystemMetadataKeys.Compression, compressor.CompressionType);
}
```

The mapper already has access to metadata via `source.Metadata.ToProperties()`.

### Read path integration

In `RecordDecoder.Decode(Record, ct)`, **before** deserialization:

```csharp
var recordData = record.Data;

if (record.Metadata.TryGetValue(SystemMetadataKeys.Compression, out var compressionType)
    && compressionType is string type
    && CompressionProvider?.GetCompressor(type) is { } compressor) {
    recordData = compressor.Decompress(recordData);
}

// then deserialize using recordData instead of record.Data
```

### Plumbing: wiring the provider into existing components

- Add `IMessageCompressionProvider?` to `ClientModuleBase` (alongside `SerializerProvider`)
- Thread it through `StreamsProtocolMapper` and `RecordDecoder` constructors
- Expose `WithCompression(IMessageCompressor compressor)` on `KurrentClientOptionsBuilder` (or a
  fluent extension method from each extension package)
- Null by default — no compression unless user opts in

### Extension packages

Each package is a single project under `src/`:

**`Kurrent.Client.Compression.Zstd/`**
- References `Kurrent.Client` + `ZstdSharp.Port`
- `ZstdMessageCompressor : IMessageCompressor` (CompressionType = `"zstd"`)
- Extension method: `builder.UseZstdCompression(CompressionLevel? level = null)`

**`Kurrent.Client.Compression.Gzip/`**
- References `Kurrent.Client` only
- `GzipMessageCompressor : IMessageCompressor` (CompressionType = `"gzip"`)
- Extension method: `builder.UseGzipCompression(CompressionLevel? level = null)`

**`Kurrent.Client.Compression.Brotli/`**
- References `Kurrent.Client` only
- `BrotliMessageCompressor : IMessageCompressor` (CompressionType = `"br"`)
- Extension method: `builder.UseBrotliCompression(CompressionLevel? level = null)`

Each extension method:
1. Creates the compressor instance
2. Registers it in the `MessageCompressionProvider` as both the write compressor and a read-side
   decompressor

### Read-side auto-detection

When consuming, the `$compression` metadata tells which compressor to use. The compression provider
on the consumer must have the matching compressor registered. Multiple can be registered for read
(e.g. if migrating from gzip to zstd):

```csharp
// producer uses zstd
var client = KurrentClient.New
    .ConnectionString("kurrentdb://localhost:2113")
    .UseZstdCompression()
    .Build();

// consumer supports both (reads zstd and gzip)
var client = KurrentClient.New
    .ConnectionString("kurrentdb://localhost:2113")
    .UseZstdCompression()           // write + read
    .AddGzipDecompression()         // read-only
    .Build();
```

## File Changes Summary

### New files

| # | File | Purpose |
|---|---|---|
| 1 | `src/Kurrent.Client/Schema/Compression/IMessageCompressor.cs` | Core interface |
| 2 | `src/Kurrent.Client/Schema/Compression/IMessageCompressionProvider.cs` | Provider interface |
| 3 | `src/Kurrent.Client/Schema/Compression/MessageCompressionProvider.cs` | Default implementation |
| 4 | `src/Kurrent.Client.Compression.Zstd/Kurrent.Client.Compression.Zstd.csproj` | Zstd package project |
| 5 | `src/Kurrent.Client.Compression.Zstd/ZstdMessageCompressor.cs` | Zstd compressor |
| 6 | `src/Kurrent.Client.Compression.Zstd/KurrentClientBuilderExtensions.cs` | Zstd fluent API |
| 7 | `src/Kurrent.Client.Compression.Gzip/Kurrent.Client.Compression.Gzip.csproj` | Gzip package project |
| 8 | `src/Kurrent.Client.Compression.Gzip/GzipMessageCompressor.cs` | Gzip compressor |
| 9 | `src/Kurrent.Client.Compression.Gzip/KurrentClientBuilderExtensions.cs` | Gzip fluent API |
| 10 | `src/Kurrent.Client.Compression.Brotli/Kurrent.Client.Compression.Brotli.csproj` | Brotli package project |
| 11 | `src/Kurrent.Client.Compression.Brotli/BrotliMessageCompressor.cs` | Brotli compressor |
| 12 | `src/Kurrent.Client.Compression.Brotli/KurrentClientBuilderExtensions.cs` | Brotli fluent API |

### Modified files

| # | File | Change |
|---|---|---|
| 1 | `SystemMetadataKeys.cs` | Add `$compression` key |
| 2 | `StreamsProtocolMapper` (Append.Mappers.cs) | Compress after serialize |
| 3 | `StreamsClientV2Mapper` (Append.Mappers.cs) | Compress after serialize (legacy path) |
| 4 | `Record.cs` (RecordDecoder) | Decompress before deserialize |
| 5 | Client option plumbing files | Thread `IMessageCompressionProvider` through |
| 6 | `Kurrent.Client.slnx` | Add new projects |
| 7 | `Directory.Packages.props` | Add `ZstdSharp.Port` version |

## Implementation Order

1. Core abstractions in `Kurrent.Client` (`IMessageCompressor`, provider, `$compression` key)
2. Write path integration (mapper changes)
3. Read path integration (decoder changes)
4. Client options plumbing
5. Gzip extension package (no deps — fastest to verify end-to-end)
6. Brotli extension package
7. Zstd extension package
8. Solution file and package management updates
9. Build verification
