---
version: '1.0'
date: '2026-01-08'
status: 'Approved'
author: 'Team'
based_on:
  - .project/reference/ideas/ReadOperations-PositionUnification.md
---

# KurrentDB Position Model Reference

## Executive Summary

This document describes the position model in KurrentDB, explaining the relationship between global log positions and stream revisions. The key insight: **LogPosition is the universal cursor** - unique, ordered, and sufficient for all read operations. Stream selection is handled via filters, not position types.

---

## Table of Contents

1. [Position Types](#1-position-types)
2. [LogPosition Characteristics](#2-logposition-characteristics)
3. [Comparison with Other Systems](#3-comparison-with-other-systems)
4. [Key Insights](#4-key-insights)
5. [Design Principles](#5-design-principles)
6. [Checkpointing](#6-checkpointing)
7. [Lag Monitoring Implications](#7-lag-monitoring-implications)

---

## 1. Position Types

KurrentDB operates with a dual-position model:

| Position Type | Scope | Description |
|---------------|-------|-------------|
| **LogPosition** | Global | Absolute position in the global transaction log ($all) |
| **StreamRevision** | Stream-local | Sequential position within a specific stream (0, 1, 2, ...) |

Every record contains BOTH positions:

```csharp
public record Record {
    public LogPosition Position { get; init; }           // Global position
    public StreamName Stream { get; init; }              // Stream context
    public StreamRevision StreamRevision { get; init; }  // Stream-local position
}
```

---

## 2. LogPosition Characteristics

### 2.1 Structure

LogPosition is represented as a single `long` value derived from the commit position in the transaction log:

```csharp
public readonly record struct LogPosition {
    public long Value { get; init; }

    public static readonly LogPosition Earliest = new(0);
    public static readonly LogPosition Latest = new(long.MaxValue);
    public static readonly LogPosition Unset = new(-1000);
}
```

### 2.2 Key Properties

| Property | Description |
|----------|-------------|
| **Unique** | Every record has a unique LogPosition across the entire database |
| **Ordered** | Monotonically increasing (higher value = later in log) |
| **Has Gaps** | Not sequential - gaps exist between consecutive records |
| **Self-Sufficient** | Can seek to any position without additional context |

### 2.3 Why Gaps Exist

LogPosition may have gaps due to:

1. **Variable record sizes** - Records occupy different byte ranges
2. **Scavenged/deleted records** - Removed records leave gaps
3. **System records** - Internal records may be filtered out
4. **Transaction boundaries** - Commit boundaries create gaps

**Critical implication**: `position_B - position_A ≠ record count between A and B`

```
Example:
Position:     100    150    200    350    400
Record #:      1      2      3      4      5

400 - 100 = 300 (position difference)
But only 5 records exist!
```

---

## 3. Comparison with Other Systems

### 3.1 Apache Kafka

```
Topic: "orders"
Partition: 3
Offset: 12345 (sequential within partition)
```

| Aspect | Kafka | KurrentDB |
|--------|-------|-----------|
| Position type | Partition-local offset | Global LogPosition |
| Global position | None | LogPosition |
| Sequential | Yes (per partition) | No (has gaps) |
| Checkpoint | topic + partition + offset | LogPosition only |

### 3.2 Apache Pulsar

```
MessageId Structure:
+------------------+------------------+------------------+------------------+
|   ledgerId (u64) |   entryId (u64)  | partition (i32)  | batchIndex (i32) |
+------------------+------------------+------------------+------------------+
```

| Aspect | Pulsar | KurrentDB |
|--------|--------|-----------|
| Position type | Composite MessageId | LogPosition (single value) |
| Self-contained | Yes (all context in MessageId) | Yes (LogPosition is unique) |
| Context required | No | No |

**Key parallel**: Both have self-sufficient positions that can locate any record without additional context.

### 3.3 NATS JetStream

NATS tracks two sequences:
1. **Stream Sequence**: Where in the stream (like LogPosition)
2. **Consumer Sequence**: Delivery attempts (different concept)

| Aspect | NATS | KurrentDB |
|--------|------|-----------|
| Position tracking | Dual sequence | Single LogPosition |
| Consumer state | Server-managed | Client-managed |

### 3.4 Summary: Position Models

| System | Global Position | Local Position | Checkpoint |
|--------|-----------------|----------------|------------|
| **KurrentDB** | LogPosition ✓ | StreamRevision | LogPosition |
| **Kafka** | None | Partition offset | topic + partition + offset |
| **Pulsar** | MessageId (composite) | N/A | MessageId |
| **NATS** | Stream sequence | N/A | Stream sequence |

---

## 4. Key Insights

### Insight 1: Every Record Has Both Positions

Records always contain LogPosition AND StreamRevision:
- LogPosition → Read Record → Extract Stream + StreamRevision
- We can always resolve from global to stream-local position

### Insight 2: LogPosition Is Universal and Ordered

Even in future partitioned streams:
- LogPosition remains unique across entire database
- LogPosition is ordered (monotonic with gaps allowed)
- LogPosition can serve as checkpoint for any read type

### Insight 3: StreamRevision Requires Context

Just like:
- Pulsar's `entryId` requires `ledgerId`
- Kafka's `offset` requires `topic + partition`

KurrentDB's `StreamRevision` is **meaningless without `StreamName`**.

### Insight 4: Position vs Filter Semantics

Two different concepts that should not be conflated:

| Concept | Purpose | Example |
|---------|---------|---------|
| **Position** | WHERE to start reading | LogPosition.Earliest |
| **Filter** | WHAT records to include | TargetStream("orders") |

### Insight 5: Protocol Direction (V2)

The V2 protocol validates this design:
- Requests: `start_position` (global) + `filter` (selection)
- Responses: Full context (position + stream + revision)

---

## 5. Design Principles

### 5.1 LogPosition as Universal Cursor

```csharp
public record ReadOptions {
    public LogPosition Start { get; init; } = LogPosition.Earliest;
    public IRecordFilter Filter { get; init; } = RecordFilter.None;
}
```

- **Input**: LogPosition (where to start)
- **Selection**: Filter (what to include)
- **Output**: Full RecordPosition (all context)

### 5.2 Filter for Selection, Not Position

```csharp
// Read from $all, filter to specific stream
var options = new ReadOptions {
    Start = LogPosition.Earliest,
    Filter = RecordFilter.ForStream("orders")
};

// Read with complex filter
var options = new ReadOptions {
    Start = lastCheckpoint,
    Filter = RecordFilter.Expression("stream LIKE 'orders-%' AND type = 'OrderPlaced'")
};
```

### 5.3 RecordPosition for Full Context

Records return complete position information:

```csharp
public readonly record struct RecordPosition {
    public LogPosition    LogPosition    { get; init; }  // For seeking
    public StreamName     Stream         { get; init; }  // Context
    public StreamRevision StreamRevision { get; init; }  // Stream-local
    public PartitionId    PartitionId    { get; init; }  // Future
}
```

---

## 6. Checkpointing

### 6.1 Simple Checkpointing

For most use cases, checkpoint only LogPosition:

```csharp
LogPosition checkpoint;
await foreach (var record in stream) {
    await ProcessRecord(record);
    checkpoint = record.Position;  // Just LogPosition
}

// Save checkpoint
await SaveCheckpoint(checkpoint);

// Resume
var options = new ReadOptions {
    Start = checkpoint,
    Filter = originalFilter
};
```

### 6.2 Full Context Checkpointing (Optional)

For debugging or validation, save full context:

```csharp
var fullCheckpoint = new {
    Position = record.Position,        // LogPosition (required for resume)
    Stream = record.Stream,            // Context (optional, for validation)
    Revision = record.StreamRevision,  // Context (optional, for debugging)
    Partition = record.PartitionId     // Context (optional, future)
};
```

---

## 7. Lag Monitoring Implications

Because LogPosition has gaps, lag calculation requires care:

### 7.1 What We CAN Measure

| Metric | Formula | Reliability |
|--------|---------|-------------|
| **Byte/Position lag** | `head.Value - current.Value` | Approximate (gaps affect accuracy) |
| **Time lag** | `head.Timestamp - current.Timestamp` | Accurate (if timestamps available) |

### 7.2 What Requires Server Query

| Metric | Formula | Cost |
|--------|---------|------|
| **Record count lag** | `COUNT(*) WHERE pos > current` | Expensive (requires index scan) |

### 7.3 Recommended Approach

```csharp
public record LagMetrics {
    // Primary metric (cheap, approximate)
    public long PositionLag => HeadPosition.Value - CurrentPosition.Value;

    // Secondary metric (if available)
    public TimeSpan? TimeLag => HeadTimestamp - CurrentTimestamp;

    // Avoid unless critical (expensive)
    // public long RecordCountLag => server.CountRecordsBetween(current, head);
}
```

### 7.4 Multi-Partition Lag

For workers with multiple partitions:

```csharp
// Per-partition positions
var positions = new Dictionary<int, LogPosition> {
    [0] = 1050,
    [1] = 2030,
    [2] = 850
};

// Min watermark: slowest partition (for "all partitions at least here")
var minWatermark = positions.Values.Min();  // 850

// Max watermark: fastest partition (for barrier ID / resume point)
var maxWatermark = positions.Values.Max();  // 2030

// Lag per partition
foreach (var (partition, position) in positions) {
    var lag = headPosition.Value - position.Value;
    Console.WriteLine($"Partition {partition} lag: {lag}");
}
```

---

## Appendix A: StreamRevision Compatibility

For APIs requiring stream revision (e.g., read from specific revision):

```csharp
public async Task<Records> ReadStream(
    StreamName stream,
    StreamRevision revision,
    IRecordFilter? filter = null)
{
    // 1. Resolve StreamRevision to LogPosition
    LogPosition start = revision switch {
        { IsMin: true } => LogPosition.Earliest,
        { IsMax: true } => LogPosition.Latest,
        _ => await ResolveStreamRevisionToLogPosition(stream, revision)
    };

    // 2. Build combined filter
    var combinedFilter = filter != null
        ? RecordFilter.Combine(RecordFilter.ForStream(stream), filter)
        : RecordFilter.ForStream(stream);

    // 3. Use core LogPosition-based read
    return await ReadRecords(new ReadOptions {
        Start = start,
        Filter = combinedFilter
    });
}
```

---

## Appendix B: Lessons Learned

1. **Simplicity wins** - After exploring complex solutions, `LogPosition + Filter` emerged as the simplest and best design.

2. **Don't fight the platform** - KurrentDB has global positions. Use them directly rather than abstracting them away.

3. **Composition over unification** - Don't try to unify fundamentally different concepts (position vs filter). Embrace their differences and compose them cleanly.

4. **Protocol drives design** - V2 protocol's `start_position + filter` approach validated the direction.

5. **Wrappers for compatibility** - Keep the core API simple, add compatibility wrappers for convenience.

---

## References

### External Documentation
- EventStoreDB Reading Events: https://developers.eventstore.com/clients/grpc/reading-events/
- Apache Pulsar MessageId: https://pulsar.apache.org/docs/
- Kafka Consumer API: https://kafka.apache.org/documentation/
- NATS JetStream: https://docs.nats.io/nats-concepts/jetstream

### Internal Files
- LogPosition.cs: `src/.../Modules/Streams/Models/LogPosition.cs`
- RecordPosition.cs: `src/.../Modules/Streams/Models/RecordPosition.cs`
- V1 Protocol: `proto/kurrentdb/protocol/v1/streams.proto`
- V2 Protocol: `proto/kurrentdb/protocol/v2/streams/streams.proto`

---

## Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-08 | Team | Initial BMAD version, cleaned from ReadOperations-PositionUnification.md |
