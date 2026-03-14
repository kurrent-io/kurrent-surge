# Message Compression — Implementation Plan

## Overview

Add message-level compression to the Kurrent .NET SDK. Compression sits between serialization and
the wire: `Serialize → Compress → Append` and `Read → Decompress → Deserialize`. A `$compression`
metadata entry signals the algorithm so consumers decompress transparently.

## Compression Algorithms

| Algorithm | Location | Dependency |
|---|---|---|
| GZip | Built into `Kurrent.Client` | `System.IO.Compression` (runtime, zero deps) |
| Brotli | Built into `Kurrent.Client` | `System.IO.Compression` (runtime, zero deps) |
| Zstandard | Separate `Kurrent.Client.Compression.Zstd` package | `ZstdSharp.Port` (external NuGet) |

GZip and Brotli use only BCL types — no extra packages. Only Zstd requires an external dependency
(`ZstdSharp.Port`), so it ships as a separate extension package.

## Architecture

### 1. `IMessageCompressor` interface

Sync-only — compression is CPU-bound, data already in memory. Each compressor owns its type string.

```csharp
namespace Kurrent.Client.Schema.Compression;

public interface IMessageCompressor {
    /// Stable identifier stored in $compression metadata.
    string CompressionType { get; }

    ReadOnlyMemory<byte> Compress(ReadOnlyMemory<byte> data);
    ReadOnlyMemory<byte> Decompress(ReadOnlyMemory<byte> data);
}
```

### 2. Compressor implementations

Each compressor defines its own `Type` constant — no centralized `CompressionType` class. This is
extensible: any future package brings its own compressor with its own constant.

```csharp
// In Kurrent.Client
public class GzipMessageCompressor : IMessageCompressor {
    public const string Type = "gzip";
    string IMessageCompressor.CompressionType => Type;
}

public class BrotliMessageCompressor : IMessageCompressor {
    public const string Type = "br";
    string IMessageCompressor.CompressionType => Type;
}

// In Kurrent.Client.Compression.Zstd
public class ZstdMessageCompressor : IMessageCompressor {
    public const string Type = "zstd";
    string IMessageCompressor.CompressionType => Type;
}
```

### 3. Compression provider

Registry of compressors. Gzip + Brotli always registered for decompression. External compressors
(Zstd) must be explicitly registered.

```csharp
public interface IMessageCompressionProvider {
    IMessageCompressor? DefaultCompressor { get; }
    IMessageCompressor? GetCompressor(string compressionType);
}
```

### 4. `$compression` metadata key

```csharp
// in SystemMetadataKeys
public const string Compression = "$compression";
```

### 5. Decompression (read-side)

- **Gzip + Brotli**: always registered, automatic, zero config
- **Zstd**: user must explicitly register (separate package with external dep)

In `RecordDecoder.Decode()`, before deserialization:
```
1. Check $compression metadata
2. Look up compressor from provider
3. Decompress data
4. Deserialize
```

### 6. Compression (write-side)

Two levels:
- **Default compressor** on client (via `ConfigureCompression(...)`)
- **Per-operation override** on `AppendRecordsRequest` (via `.CompressWith(...)`)
- Per-operation wins over default, both optional

In `StreamsProtocolMapper.ToContract()`, after serialization:
```
1. Check if AppendRecordsRequest has explicit compression → use that
2. Else check if client has default compressor → use that
3. Else no compression
4. If compressing: compress data, set $compression metadata
```

### 7. Configuration API

Sub-builder pattern, matching `ConfigureConnection`, `ConfigureSchema`, etc.

```csharp
// Default compression for all appends
var client = KurrentClient.New
    .ConnectionString("kurrentdb://localhost:2113")
    .ConfigureCompression(compression => compression
        .UseGzip()
    )
    .Build();

// Zstd (from extension package) with read support for old gzip records
var client = KurrentClient.New
    .ConnectionString("kurrentdb://localhost:2113")
    .ConfigureCompression(compression => compression
        .UseZstd()                    // default for appends + registered for decompression
    )
    .Build();
// gzip + brotli decompression always available — no config needed

// Consumer that doesn't compress but can read zstd
var client = KurrentClient.New
    .ConnectionString("kurrentdb://localhost:2113")
    .ConfigureCompression(compression => compression
        .AddZstdDecompression()       // only registers for decompression
    )
    .Build();
```

### 8. Per-operation compression override

```csharp
var request = AppendRecordsRequest.ToStream("orders")
    .CompressWith(GzipMessageCompressor.Type)     // override for this operation
    .Records(record1, record2)
    .Build();

await client.Streams.AppendRecords(request, ct);
```

## File Changes Summary

### New files

| # | File | Purpose |
|---|---|---|
| 1 | `src/Kurrent.Client/Schema/Compression/IMessageCompressor.cs` | Core interface |
| 2 | `src/Kurrent.Client/Schema/Compression/MessageCompressionProvider.cs` | Provider (always has gzip+brotli) |
| 3 | `src/Kurrent.Client/Schema/Compression/GzipMessageCompressor.cs` | GZip compressor (built-in) |
| 4 | `src/Kurrent.Client/Schema/Compression/BrotliMessageCompressor.cs` | Brotli compressor (built-in) |
| 5 | `src/Kurrent.Client/Options/Compression/CompressionOptionsBuilder.cs` | ConfigureCompression sub-builder |
| 6 | `src/Kurrent.Client.Compression.Zstd/Kurrent.Client.Compression.Zstd.csproj` | Zstd package project |
| 7 | `src/Kurrent.Client.Compression.Zstd/ZstdMessageCompressor.cs` | Zstd compressor |
| 8 | `src/Kurrent.Client.Compression.Zstd/CompressionOptionsBuilderExtensions.cs` | `.UseZstd()` / `.AddZstdDecompression()` |

### Modified files

| # | File | Change |
|---|---|---|
| 1 | `KurrentClientOptions.cs` | Add `Compression` options property |
| 2 | `KurrentClientOptionsBuilder.cs` | Add `ConfigureCompression(...)` |
| 3 | `KurrentClient.cs` | Wire compression provider from options |
| 4 | `ClientModuleBase.cs` | Expose compression provider to modules |
| 5 | `SystemMetadataKeys.cs` | Add `$compression` key |
| 6 | `StreamsProtocolMapper.cs` | Accept compression provider |
| 7 | `StreamsClient.Append.Mappers.cs` | Compress after serialize |
| 8 | `StreamsClient.cs` | Pass compression provider to mapper |
| 9 | `Record.cs` (RecordDecoder) | Decompress before deserialize |
| 10 | `StreamsClient.Append.Models.cs` | Add `CompressionType` to `AppendRecordsRequest` |
| 11 | `StreamsClient.Append.Models.Builders.cs` | Add `.CompressWith()` to request builder |
| 12 | `Kurrent.Client.slnx` | Add Zstd project |
| 13 | `Directory.Packages.props` | Add `ZstdSharp.Port` version |

## Implementation Order

1. Core interface + provider + built-in compressors (gzip, brotli)
2. `$compression` metadata key
3. Compression options + builder (`ConfigureCompression`)
4. Wire provider through client → modules → mapper/decoder
5. Write path integration (mapper: compress after serialize)
6. Read path integration (decoder: decompress before deserialize)
7. Per-operation compression on `AppendRecordsRequest`
8. Zstd extension package
9. Solution + package management updates
10. Build verification
