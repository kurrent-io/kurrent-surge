# Append API Redesign: Split Single-Stream and Multi-Stream Paths

**Date:** 2026-03-27
**Status:** Draft
**Author:** Sergio + Claude

## Problem

The current `AppendRecordsRequest` tries to serve both single-stream and multi-stream appends through one type.
This creates split-brain logic in validators, builders, and error handling. Every layer asks "is this single or
multi?" and behaves differently. The thin-sugar approach (single-stream as an extension over multi-stream types)
was tried and rejected --- it pushes the split into every consumer.

## Decision

Two independent request/response pairs. No shared base. No shared interface. Each path has its own types,
builders, validation rules, and client method. Both map to the same gRPC wire format internally.

## Request Types

### AppendToStreamRequest

Single-stream append. All records go to one stream. Transactional for that stream.

```csharp
public record AppendToStreamRequest(
    StreamName Stream,
    ImmutableArray<AppendRecord> Records,
    ImmutableArray<ConsistencyCheck> Checks
);
```

- `Stream` is the target stream for all records.
- `Checks` includes both the optional expected-state check (produced by the builder's `ExpectingState`) and any
  additional consistency checks the user adds explicitly.
- The request type itself has no `ExpectedStreamState` property --- `ExpectingState` is builder sugar that
  produces a `StreamStateCheck` at build time.

### AppendRecordsRequest

Multi-stream atomic append. Each record carries its own stream. Transactional across all streams.

```csharp
public record AppendRecordsRequest(
    ImmutableArray<AppendRecord> Records,
    ImmutableArray<ConsistencyCheck> Checks
);
```

- Each `AppendRecord` must have `Stream != Unset`.
- `Checks` are explicit --- there's no builder shortcut for expected state because there's no single target stream.

## Response Types

### AppendToStreamResponse

```csharp
public record AppendToStreamResponse(
    StreamName Stream,
    Revision Revision,
    LogPosition Position
);
```

Single revision for the single target stream.

### AppendRecordsResponse

```csharp
public record AppendRecordsResponse(
    StreamRevision[] Revisions,
    LogPosition Position
);
```

One `StreamRevision` per affected stream.

## Client Methods

```csharp
public partial class StreamsClient {
    ValueTask<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AppendRecordsResponse> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default);
}
```

### AppendToStream behavior

Before sending to the wire:

1. Stamps `request.Stream` onto every record's `Stream` property. Always, regardless of what the records
   already have. This is belt-and-suspenders --- the builder does it at build time, the method does it again
   because the request might not have come from a builder.
2. Validates record invariants (RecordId, Value, DataFormat).
3. Maps to the gRPC wire format.

### AppendRecords behavior

Before sending to the wire:

1. Validates every record has `Stream != Unset`.
2. Validates record invariants (RecordId, Value, DataFormat).
3. Maps to the gRPC wire format.

Both methods map to the same underlying gRPC `AppendRecords` call. The mapper knows how to convert either
request type into the protobuf contract.

## Request Builders

Purpose-built collectors. Not based on `OptionsBuilder<TBuilder, TOptions>`.

`OptionsBuilder` is a transform fold --- it accumulates `Func<T, T>` lambdas and reduces them. That pattern
fits configuration objects. Request builders are collectors: gather records, gather checks, build. A direct
list-based builder avoids unnecessary closure allocations and intermediate object copies.

### AppendToStreamRequestBuilder

Created via `AppendToStreamRequest.For(stream)`.

```csharp
public class AppendToStreamRequestBuilder {
    StreamName _stream;
    readonly List<AppendRecord> _records = [];
    readonly List<ConsistencyCheck> _checks = [];
    SchemaDataFormat? _dataFormatOverride;
    readonly Dictionary<string, object> _metadataOverrides = [];

    // Target stream state
    public AppendToStreamRequestBuilder ExpectingState(ExpectedStreamState state);

    // Consistency checks
    public AppendToStreamRequestBuilder WithCheck(ConsistencyCheck check);
    public AppendToStreamRequestBuilder WithChecks(params ConsistencyCheck[] checks);

    // Records
    public AppendToStreamRequestBuilder WithRecord(AppendRecord record);
    public AppendToStreamRequestBuilder WithRecord(AppendRecordBuilder builder);
    public AppendToStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> configure);
    public AppendToStreamRequestBuilder WithRecord(object value, SchemaDataFormat? dataFormat = null);
    public AppendToStreamRequestBuilder WithRecords(params AppendRecord[] records);
    public AppendToStreamRequestBuilder WithRecords(IEnumerable<AppendRecord> records);

    // Batch-level overrides (applied to all records at build time)
    public AppendToStreamRequestBuilder WithDataFormat(SchemaDataFormat dataFormat);
    public AppendToStreamRequestBuilder WithMetadata(string key, object value);

    public AppendToStreamRequest Build();
}
```

**`Build()` behavior:**

1. Throws if no records were added.
2. Stamps `_stream` onto every record.
3. If `_dataFormatOverride` is set, overrides `DataFormat` on every record.
4. Upserts `_metadataOverrides` keys into every record's metadata (record-level values for the same key get
   overwritten by builder-level values).
5. Validates record invariants (RecordId, Value, DataFormat) --- no stream validation needed.
6. If `ExpectingState` was called, converts to a `StreamStateCheck` for `_stream` and prepends to checks.
7. Returns `AppendToStreamRequest` with immutable arrays.

### AppendRecordsRequestBuilder

Created via `AppendRecordsRequest.New()`.

```csharp
public class AppendRecordsRequestBuilder {
    readonly List<AppendRecord> _records = [];
    readonly List<ConsistencyCheck> _checks = [];
    SchemaDataFormat? _dataFormatOverride;
    readonly Dictionary<string, object> _metadataOverrides = [];

    // Consistency checks
    public AppendRecordsRequestBuilder WithCheck(ConsistencyCheck check);
    public AppendRecordsRequestBuilder WithChecks(params ConsistencyCheck[] checks);

    // Records
    public AppendRecordsRequestBuilder WithRecord(AppendRecord record);
    public AppendRecordsRequestBuilder WithRecord(AppendRecordBuilder builder);
    public AppendRecordsRequestBuilder WithRecord(Action<AppendRecordBuilder> configure);
    public AppendRecordsRequestBuilder WithRecord(object value, SchemaDataFormat? dataFormat = null);
    public AppendRecordsRequestBuilder WithRecords(params AppendRecord[] records);
    public AppendRecordsRequestBuilder WithRecords(IEnumerable<AppendRecord> records);

    // Batch-level overrides
    public AppendRecordsRequestBuilder WithDataFormat(SchemaDataFormat dataFormat);
    public AppendRecordsRequestBuilder WithMetadata(string key, object value);

    public AppendRecordsRequest Build();
}
```

**`Build()` behavior:**

1. Throws if no records were added.
2. If `_dataFormatOverride` is set, overrides `DataFormat` on every record.
3. Upserts `_metadataOverrides` keys into every record's metadata.
4. Validates every record has `Stream != Unset`.
5. Validates record invariants (RecordId, Value, DataFormat).
6. Returns `AppendRecordsRequest` with immutable arrays.

### Note on WithRecord(object value, ...) in AppendRecordsRequestBuilder

This overload is available but the record still needs a stream. The user must construct records with streams
set (via `AppendRecord.ToStream(stream).WithValue(value).Build()`) or use the `Action<AppendRecordBuilder>`
overload and call `ForStream` inside it.

## AppendRecord Validation

`AppendRecord.Validate()` checks only record-level invariants:

- `RecordId != Guid.Empty`
- `Value != null`
- `DataFormat != Unspecified`
- If `Value` is `byte[]`, `DataFormat` must be `Bytes`

The `bool validateStream` parameter is removed. Stream validation is the responsibility of the request builders
and client methods, not the record.

`AppendRecordBuilder.Build()` calls `Validate()` (without stream check). A record with `Stream = Unset` is
valid at the record level.

## AppendRecordBuilder

Unchanged. Builds a single `AppendRecord` with `ForStream`, `WithValue`, `WithDataFormat`, `WithMetadata`,
`WithId`, `WithTimestamp`. `Build()` validates record invariants only.

## Builder Execute Extensions

Both builders get an `ExecuteAsync` extension method that calls `Build()` and dispatches to the client in one
step. This avoids forcing users to create an intermediate request variable for simple flows.

```csharp
// Single-stream: build + execute in one call
var response = await AppendToStreamRequest.For("my-stream")
    .WithRecord(myRecord)
    .ExpectingState(ExpectedStreamState.NoStream)
    .ExecuteAsync(client.Streams, cancellationToken);

// Multi-stream: same pattern
var response = await AppendRecordsRequest.New()
    .WithRecord(AppendRecord.ToStream("stream-a").WithValue(eventA).Build())
    .WithRecord(AppendRecord.ToStream("stream-b").WithValue(eventB).Build())
    .ExecuteAsync(client.Streams, cancellationToken);
```

These are extension methods on the builder types, not instance methods, so the builders stay decoupled from
`StreamsClient`. The extension lives alongside the other `StreamsClient` extensions.

## What Gets Deleted

- `SingleStreamAppendBuilder` --- replaced by `AppendToStreamRequestBuilder`
- `MultiStreamAppendBuilder` --- replaced by `AppendRecordsRequestBuilder`
- `AppendRecordsSuccess` --- replaced by `AppendToStreamResponse` + `AppendRecordsResponse`
- `AppendStreamRequest` (obsolete) --- removed
- `AppendStreamSuccess` (obsolete) --- removed
- `AppendSessionSuccess` (obsolete) --- removed
- `AppendRecord.Validate(bool validateStream)` flag --- becomes `Validate()` with no parameters
- Extension methods in `StreamsClient.Append.Extensions.cs` --- to be rethought for the new types

## Out of Scope

- **StreamsAppender** --- the reusable appender/producer concept is a separate design surface. It layers on
  top of the request types and client methods. Defer to a follow-up design.
- **Convenience overloads** --- extension methods and shorthand overloads on `StreamsClient` for common
  patterns (e.g., `AppendToStream(stream, expectedState, records)`). Design after the core types are stable.
- **Error handling changes** --- exception types and how consistency violations map to exceptions per path.
  The current exception types likely need adjustment for the split, but that's implementation detail.

## Wire Format

Both paths map to the same gRPC `AppendRecords` RPC. The `StreamsProtocolMapper` gains two `ToContract`
overloads (one per request type) that both produce the same `Contracts.AppendRecordsRequest` protobuf message.

For `AppendToStreamRequest`, the mapper reads `Stream` from the request and stamps it onto each wire record
(though the client method already did this --- the mapper just serializes what it gets).

For `AppendRecordsRequest`, the mapper reads `Stream` from each individual `AppendRecord`.

Response mapping: the mapper gains two `ToModel` paths. For single-stream, it extracts the single stream's
final revision. For multi-stream, it returns the full array of `StreamRevision` entries.
