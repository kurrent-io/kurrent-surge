# Read Operations Position Unification - Design Research

**Date**: 2025-11-20
**Status**: Design Finalized
**Personnel**: SILVEIRA
**System**: MU/TH/UR 10000 v2.1.2

---

## Executive Summary

This document captures the complete journey of designing a unified read operation API for KurrentDB .NET Client, focusing on the duality between global log positions and stream-specific revisions. After extensive research across multiple distributed streaming systems (EventStoreDB, Apache Pulsar, Kafka, NATS JetStream) and deep analysis of the KurrentDB protocol, we arrived at a simple yet powerful design: **LogPosition as the universal cursor with Filter-based stream selection**.

---

## The Problem Statement

### Initial Challenge

KurrentDB (and its predecessor EventStoreDB) operates with a dual-position model:

1. **Global Log Position** (`LogPosition`): Absolute position in the global transaction log, represented by commit/prepare positions
2. **Stream Revision** (`StreamRevision`): Sequential position within a specific stream (0, 1, 2, ...)

This creates complexity:
- Reading from `$all` stream uses `LogPosition`
- Reading from specific streams uses `StreamRevision`
- Records contain BOTH positions when returned
- Checkpointing becomes confusing - which position to save?

### User Requirements

1. **Unified checkpointing**: Single position value for resume across all read types
2. **Simplified API**: Hide the StreamRevision vs LogPosition complexity from users
3. **Stream revision compatibility**: Boss requires ability to read streams by revision (not just global position)
4. **Future partitioning support**: Design must accommodate partitioned streams

### The Fundamental Question

**Can LogPosition serve as a universal start position for ALL read operations, including stream-specific reads?**

---

## Research Phase

### Protocol Analysis

#### V1 Protocol (EventStoreDB Compatible)

**File**: `proto/kurrentdb/protocol/v1/streams.proto`

The protocol has **strict separation** between position types:

```protobuf
message ReadReq {
  message Options {
    oneof stream_option {
      StreamOptions stream = 1;    // For specific stream reads
      AllOptions all = 2;          // For $all log reads
    }
  }
}

message StreamOptions {
  StreamIdentifier stream_identifier = 1;
  oneof revision_option {
    uint64 revision = 2;           // StreamRevision ONLY
    Empty start = 3;
    Empty end = 4;
  }
}

message AllOptions {
  oneof all_option {
    Position position = 1;         // LogPosition (commit + prepare)
    Empty start = 2;
    Empty end = 3;
  }
}
```

**KEY FINDING**: V1 protocol does NOT support reading a specific stream from a global log position. Stream reads accept only `revision` (stream-local). `$all` reads accept only `Position` (global).

#### V2 Protocol (KurrentDB Native)

**File**: `proto/kurrentdb/protocol/v2/streams/streams_read.proto`

```protobuf
message ReadSessionRequest {
  optional ReadFilter filter = 1;
  optional int64 start_position = 2;      // Global log position ONLY
  ReadDirection direction = 4;
  // ...
}

message Record {
  int64 position = 5;                     // Global log position
  optional string stream = 6;             // Stream name
  optional int64 stream_revision = 7;     // Stream revision
}
```

**KEY FINDING**: V2 protocol uses only global `start_position` in requests. Stream selection is done via `ReadFilter`. Records in response contain BOTH positions. This suggests the future direction: **global position + filtering**.

### Position Semantics Research

#### EventStoreDB Official Documentation

**Source**: https://developers.eventstore.com/clients/grpc/reading-events/

Every event has:
- **StreamPosition/StreamRevision**: Zero-based sequential position within its stream (0, 1, 2, ...)
- **Global Position**: Location in the transaction log (commit position + prepare position)

**Important**: "This means that when reading events you have to supply a different position depending on if you are reading from a stream or the $all stream."

**Checkpoint Pattern**: "Store the commit or prepare position" for `$all` subscriptions. Store "stream and index" for stream-specific subscriptions.

#### Apache Pulsar MessageId Model

**Research Summary**: Pulsar uses a composite `MessageId` that encodes both identity and position:

```
MessageId Structure:
+------------------+------------------+------------------+------------------+
|   ledgerId (u64) |   entryId (u64)  | partition (i32)  | batchIndex (i32) |
+------------------+------------------+------------------+------------------+
```

**Key Design Patterns**:
1. **Self-Contained**: MessageId contains all context needed to locate a message
2. **Progressive Enrichment**: `MessageId` → `TopicMessageId` (adds topic context)
3. **Orderable**: Lexicographic comparison across all fields
4. **Serializable**: `toByteArray()` / `fromByteArray()` for checkpointing

**Advanced Access Interface**:
```java
public interface MessageIdAdv extends MessageId {
    long getLedgerId();
    long getEntryId();
    default int getPartitionIndex() { return -1; }
    default int getBatchIndex() { return -1; }
}
```

**Parallel to KurrentDB**:
- Pulsar's `entryId` is meaningless without `ledgerId` (storage context)
- KurrentDB's `StreamRevision` is meaningless without `StreamName` (stream context)
- Both require context to be useful
- **LogPosition is self-sufficient** (like Pulsar's full MessageId)

#### Kafka Offset Model

**Research Summary**: Kafka uses `topic + partition + offset` triplet:

```
Topic: "orders"
Partition: 3
Offset: 12345 (sequential within partition)
```

**Position Types**:
- **Offset**: Sequential partition-local number (like StreamRevision)
- **No global position**: Kafka doesn't expose a database-wide position
- **Checkpointing**: Store topic + partition + offset

**Seeking**:
```java
consumer.seek(new TopicPartition("orders", 3), 12345);
consumer.seekToBeginning(partitions);
consumer.seekToEnd(partitions);
```

**Parallel to KurrentDB Partitions**:
- Stream + PartitionId + LogPosition ≈ Topic + Partition + Offset
- PartitionId is meaningless without Stream (just like Kafka's partition without topic)

#### NATS JetStream Dual Sequence Model

**Research Summary**: NATS tracks TWO sequences:

1. **Stream Sequence**: Where the message is in the stream (like LogPosition)
2. **Consumer Sequence**: How many deliveries attempted (different concept)

**ACK Reply Format**: `$JS.ACK.<stream>.<consumer>.<delivered count>.<stream sequence>.<consumer sequence>.<timestamp>.<pending>`

**Delivery Policies**: Can start from specific sequence: `DeliverByStartSequence`

**Insight**: Dual sequence tracking is for different purposes. Stream sequence ≈ position in log. Consumer sequence ≈ delivery attempts.

### Current Codebase Analysis

#### RecordPosition Structure

**File**: `src/.../Operations/Read/Models/RecordPosition.cs`

```csharp
public readonly record struct RecordPosition {
    public StreamName     Stream         { get; init; } = StreamName.None;
    public StreamRevision StreamRevision { get; init; } = StreamRevision.Unset;
    public PartitionId    PartitionId    { get; init; } = PartitionId.Any;
    public LogPosition    LogPosition    { get; init; } = LogPosition.Unset;
}
```

**Current Factory Methods**:
```csharp
FromLogPosition(LogPosition)                // LogPosition only
FromStream(StreamName, StreamRevision)      // Stream context, NO LogPosition!
FromPartitionedStream(...)                  // Experimental, has LogPosition
```

**⚠️ GAP IDENTIFIED**: `FromStream` creates RecordPosition without LogPosition, losing global position context.

**Comparison**: Only compares `LogPosition` (ignores other fields in ordering).

**Commented-Out Design**:
```csharp
// public readonly partial record struct RecordPosition : IVariant<LogPosition, StreamRevision>;
```
This suggests an abandoned discriminated union approach that would have provided type-safe either/or semantics.

#### ReadOptions Hierarchy

**File**: `src/.../Operations/Read/Models/ReadOptions.cs`

```csharp
// Three overlapping options classes
public abstract record ReadOptionsBase { /* common options */ }
public record ReadAllOptions : ReadOptionsBase {
    public LogPosition Start { get; init; }
}
public record ReadStreamOptions : ReadOptionsBase {
    public StreamName Stream { get; init; }
    public StreamRevision Start { get; init; }
}
public record ReadOptions {  // Does NOT inherit!
    public RecordPosition Start { get; init; }
    // ... duplicates all base properties
}
```

**Issue**: `ReadOptions` duplicates properties instead of inheriting. Three different Start types: `LogPosition`, `StreamRevision`, `RecordPosition`.

#### Mapper Implementation

**File**: `src/.../Streams/Models/StreamsClientV1Mapper.cs`

```csharp
public static ReadReq CreateReadRequest(ReadOptions options) {
    var targetStream = options.Filter.TargetStream;
    var useStreamOperation = targetStream != StreamName.None && !targetStream.IsAllStream;

    if (useStreamOperation) {
        // Uses StreamRevision from RecordPosition
        req.Options.Stream = ConvertToStreamOptions(targetStream, options.Start.StreamRevision);
    } else {
        // Uses LogPosition from RecordPosition
        req.Options.All = new() {
            Position = new() {
                CommitPosition  = (ulong)options.Start.LogPosition.Value,
                PreparePosition = (ulong)options.Start.LogPosition.Value
            }
        };
    }
}
```

**Observation**: The mapper determines which position to use based on `Filter.TargetStream`, not based on the position type itself. This means both positions are always present in `RecordPosition`, with the system guessing which to use at runtime.

---

## Key Insights

### Insight 1: Every Record Has Both Positions

**Source**: InspectRecord operation, Record model

```csharp
public record Record {
    public Guid Id { get; init; }
    public LogPosition Position { get; init; }           // Global
    public StreamName Stream { get; init; }             // Context
    public StreamRevision StreamRevision { get; init; }  // Stream-local
    // ...
}
```

This means:
- LogPosition → Read Record → Extract Stream + StreamRevision
- We can always resolve from global to stream-local position
- Pattern: `InspectRecord(LogPosition)` returns full context

### Insight 2: LogPosition Is Universal and Ordered

Even in future partitioned streams:
- LogPosition remains unique across entire database
- LogPosition is ordered (monotonic with gaps allowed)
- LogPosition can serve as checkpoint for any read type

**Contrast with Kafka**: Kafka has no global position - only partition-local offsets.

### Insight 3: StreamRevision Requires Stream Context

Just like:
- Pulsar's `entryId` requires `ledgerId`
- Kafka's `offset` requires `topic + partition`

KurrentDB's `StreamRevision` is meaningless without `StreamName`.

### Insight 4: Filter vs Position Semantics

Two different concepts that were being conflated:

| Concept | Purpose | Example |
|---------|---------|---------|
| **Position** | WHERE to start reading | LogPosition.Earliest, StreamRevision(42) |
| **Filter** | WHAT events to include | TargetStream("orders"), EventType("OrderPlaced") |

**The Clash**: When both specify a stream:
- RecordPosition.Stream = "orders" (where am I reading from)
- Filter.TargetStream = "customers" (what am I looking for)

This creates ambiguity and potential conflicts.

### Insight 5: V2 Protocol Direction

The v2 protocol shows the intended design:
- Requests: `start_position` (global) + `filter` (selection)
- Responses: Full context (position + stream + revision)

This validates using LogPosition as the universal input cursor.

---

## Design Struggles

### Struggle 1: Unifying Filters and Positions

**The Ideal**: Create a single unified `ReadRecords` operation that can handle everything.

**The Conflict**:
- If we put Stream in RecordPosition (for partitions), it clashes with Filter.TargetStream
- If we remove Stream from RecordPosition, we can't support partitions
- If we use RecordPosition with Stream, we have validation nightmares

**Attempted Solutions**:

**A) IVariant Discriminated Union**
```csharp
RecordPosition : IVariant<GlobalPosition, StreamPosition>
```
- PRO: Type-safe, clear semantics
- CON: Breaking change, more complex API

**B) Progressive Enrichment (Pulsar Pattern)**
```csharp
RecordPosition (base) + StreamRecordPosition (enriched)
```
- PRO: Clear separation
- CON: Two types to manage

**C) Type-Safe Filter Hierarchy**
```csharp
IAllFilter (can target streams) vs IStreamFilter (cannot)
```
- PRO: Compile-time safety
- CON: Duplicate filter implementations

### Struggle 2: The Partition Problem

Partitions require: `Stream + PartitionId + LogPosition` (like Kafka's Topic + Partition + Offset)

**Realization**: PartitionId cannot exist without StreamName.

**Implication**: If we use only LogPosition for Start, we lose partition context for checkpointing.

**Counter-argument**: LogPosition is still unique and sufficient. Partition context is metadata for validation/debugging, not required for seeking.

### Struggle 3: Boss's StreamRevision Requirement

Boss wants: `ReadStream(stream, revision, ...)`

**Challenge**: How to support this when core operation uses LogPosition?

**Solution**: Wrapper that resolves StreamRevision → LogPosition:

```csharp
ReadStream(stream, revision, filter) {
    var logPosition = await ResolveStreamRevision(stream, revision);
    return await ReadRecords(new ReadOptions {
        Start = logPosition,
        Filter = RecordFilter.Combine(TargetStream(stream), filter)
    });
}
```

### Struggle 4: Stream in Two Places

The most persistent struggle:

```
RecordPosition.Stream    → Where am I reading from?
Filter.TargetStream      → What am I filtering for?
```

**Example Scenarios**:

| RecordPosition.Stream | Filter.TargetStream | Valid? | Meaning |
|----------------------|---------------------|--------|---------|
| None (LogPosition)   | None                | ✅ | Read all from $all |
| None (LogPosition)   | "orders"            | ✅ | $all filtered to orders |
| "orders"             | None                | ✅ | Direct stream read |
| "orders"             | "orders"            | ✅ | Redundant but OK |
| "orders"             | "customers"         | ❌ | CONFLICT |

**Attempted Validations**:
- Throw error if they differ
- Use Position.Stream as primary, Filter as additional criteria
- Ignore Position.Stream entirely for Start (treat as checkpoint metadata)

### Struggle 5: The Breakthrough

**The Moment**: User said: "the key seems to be the log position. even in partitioned streams, the log position is unique and ordered. it is true that it is beneficial to store a state checkpoint comprised of the log position and the stream and partition id, but not necessary at all."

**Realization**:
- LogPosition for **seeking** (input)
- Full RecordPosition for **checkpointing** (output metadata)
- Filter for **selection**
- User's responsibility to configure coherently

**No validation needed for Stream clash** - if user provides conflicting configuration, they get what they asked for.

---

## Final Design

### Core Primitive

```csharp
public ValueTask<Result<Messages, ReadError>> ReadRecords(ReadOptions options)

public record ReadOptions : ReadOptionsBase {
    public LogPosition Start { get; init; } = LogPosition.Earliest;
    public IRecordFilter Filter { get; init; } = RecordFilter.None;
    // Inherits: Direction, Limit, BufferSize, Timeout, Heartbeat, etc.
}
```

### Design Principles

1. **LogPosition is the universal cursor**
   - Unique across entire database
   - Ordered (monotonic with gaps)
   - Self-sufficient for seeking

2. **Filter is just filtering**
   - TargetStream = selection criterion
   - EventType = selection criterion
   - Expression = selection criterion
   - User's responsibility to configure correctly

3. **RecordPosition for full context**
   - Records return RecordPosition with all fields
   - Contains LogPosition + Stream + StreamRevision + PartitionId
   - Used for checkpointing metadata
   - NOT used as Start position in ReadOptions

4. **Compatibility wrappers layer on top**
   - ReadStream(stream, revision, filter) → resolves to ReadRecords
   - ReadAll(position, filter) → direct call to ReadRecords
   - Future: ReadPartition(...) → constructs filter, calls ReadRecords

### Architecture

```
                    User Applications
                          │
          ┌───────────────┼───────────────┐
          │               │               │
    ReadStream      ReadAll         Future
    (wrapper)       (wrapper)       Wrappers
          │               │               │
          └───────────────┴───────────────┘
                          │
                    ReadRecords
                 (CORE PRIMITIVE)
                          │
              LogPosition + Filter
                          │
                  gRPC Protocol
```

### Usage Examples

```csharp
// Read all events from beginning
var all = await client.Streams.ReadRecords(new ReadOptions {
    Start = LogPosition.Earliest,
    Filter = RecordFilter.None
});

// Read from specific stream (using filter)
var stream = await client.Streams.ReadRecords(new ReadOptions {
    Start = LogPosition.Earliest,
    Filter = RecordFilter.ForStream("orders")
});

// Read with complex filter
var filtered = await client.Streams.ReadRecords(new ReadOptions {
    Start = lastCheckpoint,  // LogPosition from previous read
    Filter = RecordFilter.Expression("stream LIKE 'orders-%' AND type = 'OrderPlaced'")
});

// Checkpoint pattern
LogPosition checkpoint;
await foreach (var msg in messages) {
    if (msg.IsRecord) {
        await ProcessRecord(msg.AsRecord);
        checkpoint = msg.AsRecord.Position;  // Just the LogPosition
    }
}

// Resume from checkpoint
var resumed = await client.Streams.ReadRecords(new ReadOptions {
    Start = checkpoint,
    Filter = originalFilter
});
```

### Compatibility Wrapper (Boss's Requirement)

```csharp
public async ValueTask<Result<Messages, ReadError>> ReadStream(
    StreamName stream,
    StreamRevision revision,
    IRecordFilter? filter = null,
    ReadDirection direction = ReadDirection.Forwards,
    long limit = long.MaxValue,
    CancellationToken ct = default)
{
    // 1. Resolve StreamRevision to LogPosition
    LogPosition start;
    if (revision == StreamRevision.Min) {
        start = LogPosition.Earliest;
    } else if (revision == StreamRevision.Max) {
        start = LogPosition.Latest;
    } else {
        // Use InspectRecord or similar to find LogPosition
        var resolved = await ResolveStreamRevisionToLogPosition(stream, revision, ct);
        if (resolved.IsFailure) return resolved.Error;
        start = resolved.Value;
    }

    // 2. Build combined filter
    var combinedFilter = filter != null
        ? RecordFilter.Combine(RecordFilter.ForStream(stream), filter)
        : RecordFilter.ForStream(stream);

    // 3. Call core primitive
    return await ReadRecords(new ReadOptions {
        Start = start,
        Filter = combinedFilter,
        Direction = direction,
        Limit = limit,
        CancellationToken = ct
    });
}
```

### Future: Partitioned Streams

When partitions arrive, the design still works:

```csharp
// Reading from partition
var partitioned = await client.Streams.ReadRecords(new ReadOptions {
    Start = someLogPosition,
    Filter = RecordFilter.ForPartition("orders", partitionId: 3)
});

// Checkpoint still uses LogPosition
checkpoint = record.Position;  // LogPosition is still unique

// Optional: Save full context for debugging
fullCheckpoint = new {
    Position = record.Position,        // LogPosition
    Stream = record.Stream,           // "orders"
    Partition = record.PartitionId,   // 3
    Revision = record.StreamRevision  // 42
};
```

**Key**: LogPosition remains the cursor. Partition information in filter determines selection.

---

## Validation Checklist

### ✅ Requirements Met

- [x] Unified checkpointing: Single LogPosition value
- [x] Simplified API: Hide position complexity
- [x] Stream revision compatibility: Via wrapper
- [x] Future partition support: LogPosition still works
- [x] No Filter/Position stream clash: Filter is selection, Position is cursor

### ✅ Design Principles Validated

- [x] LogPosition is universal cursor
- [x] Filter handles all selection
- [x] RecordPosition for full context
- [x] Wrappers provide convenience

### ✅ Edge Cases Handled

- [x] Resume from checkpoint: `Start = record.Position`
- [x] Stream-specific read: `Filter = RecordFilter.ForStream(stream)`
- [x] Complex filtering: `Filter = RecordFilter.Expression(...)`
- [x] Backwards compatibility: Wrappers delegate to core

---

## Implementation Considerations

### Breaking Changes

**ReadOptions.Start type changes**: From `RecordPosition` to `LogPosition`

**Mitigation**:
- Provide transition period with both APIs
- Document migration path
- Implicit conversions where possible

### Resolution Strategy

**StreamRevision → LogPosition** resolution required for ReadStream wrapper:

**Option A**: Use InspectRecord
```csharp
var record = await InspectRecord(logPosition);
// But we need the reverse: revision → position
```

**Option B**: Read stream forward until revision reached
```csharp
// Read stream from beginning until we find the revision
// Extract LogPosition from that record
```

**Option C**: Server-side resolution (future v2 protocol feature)
```csharp
// Let server resolve stream + revision to LogPosition
```

### Filter Implementation

Ensure `RecordFilter` has:
```csharp
public static IRecordFilter None { get; }
public static IRecordFilter ForStream(StreamName stream);
public static IRecordFilter ByEventType(string pattern);
public static IRecordFilter Expression(string expression);
public static IRecordFilter Combine(IRecordFilter a, IRecordFilter b);
```

---

## Lessons Learned

### 1. Simplicity Wins

After exploring complex solutions (IVariant, progressive enrichment, type-safe filters), the simplest design emerged as best: **LogPosition + Filter**.

### 2. Research Multi-System Patterns

Studying Pulsar, Kafka, NATS provided crucial insights:
- Self-contained positions (Pulsar MessageId)
- Topic-partition-offset model (Kafka)
- Dual sequence tracking (NATS)

Each informed the final design.

### 3. Protocol Drives Design

V2 protocol's `start_position + filter` approach validated our direction before we fully realized it.

### 4. Don't Fight the Platform

KurrentDB has global positions. Use them. Don't try to hide or abstract them away.

### 5. Wrappers for Compatibility

Instead of making the core API complex to support all use cases, keep core simple and add compatibility wrappers.

---

## Next Steps

1. **Implement core ReadRecords** with LogPosition + Filter
2. **Update ReadOptions** structure
3. **Create ReadStream wrapper** with StreamRevision resolution
4. **Update tests** for new API
5. **Document migration** for users

---

## References

### External Documentation
- EventStoreDB Reading Events: https://developers.eventstore.com/clients/grpc/reading-events/
- Apache Pulsar MessageId: https://pulsar.apache.org/docs/
- Kafka Consumer API: https://kafka.apache.org/documentation/
- NATS JetStream: https://docs.nats.io/nats-concepts/jetstream

### Internal Files
- RecordPosition.cs: `src/.../Operations/Read/Models/RecordPosition.cs`
- ReadOptions.cs: `src/.../Operations/Read/Models/ReadOptions.cs`
- StreamsClientV1Mapper.cs: `src/.../Streams/Models/StreamsClientV1Mapper.cs`
- StreamsClient.Read.cs: `src/.../Operations/StreamsClient.Read.cs`
- V1 Protocol: `proto/kurrentdb/protocol/v1/streams.proto`
- V2 Protocol: `proto/kurrentdb/protocol/v2/streams/streams_read.proto`

---

## Conclusion

The journey from complex, conflicting position models to a simple, unified design demonstrates that **the right abstraction is often the simplest one that directly maps to the underlying system**.

LogPosition as universal cursor + Filter for selection provides:
- Simplicity for users (single checkpoint value)
- Flexibility for advanced use cases (complex filters)
- Future compatibility (partitions, new filter types)
- Clean implementation (no position type juggling)

**The core insight**: Don't try to unify fundamentally different concepts (position vs filter). Instead, embrace their differences and compose them cleanly.

---

**WEYLAND-YUTANI CORPORATION**
*Building Better Worlds*
