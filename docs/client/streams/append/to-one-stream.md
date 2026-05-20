---
order: 1
title: To one stream
---

# Append to one stream

<CopyMarkdownButton />

`AppendStream` writes one or more records to a single, named stream. The destination is set once on the request, every record in the batch is stamped with it, and the whole batch commits as one transaction. Reach for [`AppendRecords`](across-streams.md) when a single transaction needs to span more than one stream.

## Append a single record

Create an `AppendRecord` and pass it to `AppendStream`. The record's `Stream` is the target.

```csharp
AppendRecord record = new() {
    Stream = "order-123",
    Value  = new { Product = "Widget", Quantity = 5 }
};

var result = await client.Streams.AppendStream(record);

Console.WriteLine("Stream:   {0}", result.Stream);
Console.WriteLine("Revision: {0}", result.Revision);
Console.WriteLine("Position: {0}", result.Position);
```

Payloads serialize as JSON by default. The stream is created on first append. The same record type works for events, commands, snapshots, or any other payload shape.

The response carries the target stream, the new revision, and the log position of the last appended record.

## Append a batch to one stream

`AppendStreamRequest.For(stream)` collects multiple records destined for the same stream. They commit together or not at all, and any record's own `Stream` property is overwritten with the request's target.

```csharp
var request = AppendStreamRequest.For("order-456")
    .AddMessage(new { Product = "Gadget", Quantity = 2 })
    .AddMessage(new { ConfirmedAt = DateTimeOffset.UtcNow })
    .AddMessage(new { TrackingId = "TRK-789" })
    .Build();

var result = await client.Streams.AppendStream(request);
```

`AddMessage` is the payload-only shortcut. Reach for `AddRecord` when you need to control more than just the payload.

## Attach metadata to a record

Pass a fully populated `AppendRecord` when you need more control than `AddMessage` provides:

```csharp
var request = AppendStreamRequest.For("order-456")
    .AddRecord(new AppendRecord {
        RecordId  = Guid.NewGuid(),
        Value     = new { Product = "Gadget" },
        Metadata  = new Metadata()
            .With("correlation-id", correlationId)
            .With("causation-id", causationId),
        Timestamp = DateTimeOffset.UtcNow
    })
    .Build();
```

The destination stream comes from the request, so the record itself does not need its own stream set.

## Guard against concurrent writes

`ExpectedStreamState` tells the server what state the stream must be in before the append is accepted. The append is rejected when the actual state differs.

::: tabs#expected-state

@tab Any state

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectedState(ExpectedStreamState.Any)
    .AddMessage(payload)
    .Build();
```

The default. The append succeeds whether or not the stream exists.

@tab Stream must not exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectedState(ExpectedStreamState.NoStream)
    .AddMessage(new { Product = "Widget", Quantity = 5 })
    .Build();
```

Throws `StreamAlreadyExistsException` if the stream already has records. Use this for the first record on a stream that must not collide.

@tab Stream must exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectedState(ExpectedStreamState.Exists)
    .AddMessage(payload)
    .Build();
```

Throws `StreamNotFoundException` when the stream does not exist.

@tab Match revision

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectedState(Revision.From(4))
    .AddMessage(payload)
    .Build();
```

Throws `StreamRevisionConflictException` if the current revision is anything other than 4. This is the strongest guarantee, intended for writers contending on the same stream.

The usual pattern is to read the stream's current revision and append against it:

```csharp{1,4}
var last = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Backwards, true, default);

var request = AppendStreamRequest.For("order-123")
    .ExpectedState(last.Revision)
    .AddMessage(payload)
    .Build();

var result = await client.Streams.AppendStream(request);
```

`Revision` converts implicitly to `ExpectedStreamState`, so passing `last.Revision` works directly.

:::

## Choose a payload format

JSON is the default. Override per record with `DataFormat`.

::: tabs#data-formats

@tab JSON

The default. No format call required.

```csharp
AppendRecord jsonRecord = new() {
    Stream = "orders",
    Value  = orderPayload
};
```

@tab Bytes

For payloads you have already serialized.

```csharp
var bytesRecord = AppendRecord.New()
    .Stream("blobs")
    .Value(rawBytes)
    .DataFormat(SchemaDataFormat.Bytes)
    .Build();
```

@tab Protobuf

For messages generated from `.proto` definitions.

```csharp
var protoRecord = AppendRecord.New()
    .Stream("orders")
    .Value(protobufMessage)
    .DataFormat(SchemaDataFormat.Protobuf)
    .Build();
```

@tab Avro

For Avro-encoded payloads.

```csharp
var avroRecord = AppendRecord.New()
    .Stream("orders")
    .Value(avroPayload)
    .DataFormat(SchemaDataFormat.Avro)
    .Build();
```

:::

## Handle append errors

`AppendStream` throws a typed exception when the server rejects the append. Concurrency failures carry the stream and, where relevant, both the expected and actual revision.

```csharp
try {
    await client.Streams.AppendStream(record, Revision.From(4));
}
catch (StreamRevisionConflictException ex) {
    Console.WriteLine("Expected {0}, was {1}", ex.ExpectedRevision, ex.ActualRevision);
}
```

A single-stream append collapses any state mismatch into one typed exception. The multi-stream surface in [`AppendRecords`](across-streams.md) cannot do this and instead surfaces violations as a list.

See [Error handling](../../error-handling.md) for the full throwing model, validation, and cancellation.

## Reference

Every append compares an *expected* state against the *actual* state of the target stream and rejects the operation on a mismatch.

### Expected stream state values

| Value              | Check performed                    |
|--------------------|------------------------------------|
| `Any`              | None, always succeeds              |
| `NoStream`         | Stream must not exist              |
| `Exists`           | Stream must exist                  |
| `Revision.From(n)` | Stream revision must match exactly |

### Append target state matrix

A tombstoned stream rejects every append. The check passes only where marked with the success glyph below.

<table>
<thead>
<tr><th rowspan="2">Expected</th><th colspan="4" align="center">Actual</th></tr>
<tr><th align="center">Revision M</th><th align="center">No stream</th><th align="center">Deleted</th><th align="center">Tombstoned</th></tr>
</thead>
<tbody>
<tr><td><code>Revision.From(N)</code></td><td align="center">✅ if M = N</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
<tr><td><code>NoStream</code></td><td align="center">❌</td><td align="center">✅</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Any</code></td><td align="center">✅</td><td align="center">✅</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Exists</code></td><td align="center">✅</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
<tr><td><code>Deleted</code></td><td align="center">❌</td><td align="center">❌</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Tombstoned</code></td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
</tbody>
</table>

### Append-specific exceptions

| Exception                                   | Cause                                                       |
|---------------------------------------------|-------------------------------------------------------------|
| `StreamNotFoundException`                   | `Exists` was expected, stream does not exist                |
| `StreamAlreadyExistsException`              | `NoStream` was expected, stream already has records         |
| `StreamDeletedException`                    | Stream was soft-deleted                                     |
| `StreamTombstonedException`                 | Stream was permanently deleted                              |
| `StreamRevisionConflictException`           | Exact revision was expected, current revision differs       |
| `MaxAppendRecordSizeExceededException`      | A single record exceeds the server size limit               |
| `MaxAppendTransactionSizeExceededException` | The transaction exceeds the server size limit               |
