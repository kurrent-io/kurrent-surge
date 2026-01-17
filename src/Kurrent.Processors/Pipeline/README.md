# Kurrent Processors - Sinks & Collectors

## The Model

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  STARTUP (Builder)                                                          │
│                                                                             │
│  KurrentProcessors                                                          │
│      .Create<OrderEvent>(new OrderProcessor())                              │
│      .ConnectTo("esdb://...")                                               │
│      .AddSink(kafkaSink)           ◄── Configure sinks here                 │
│      .AddSink(transactionalSink)                                            │
│      .Build()                                                               │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  RUNTIME (ProcessorWorker)                                                  │
│                                                                             │
│  ┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐     │
│  │ IRecordProcessor│ ───► │   ICollector    │ ───► │ SinkCoordinator │     │
│  │   (your code)   │      │   (emit here)   │      │   (internal)    │     │
│  └─────────────────┘      └─────────────────┘      └────────┬────────┘     │
│                                                              │              │
│                                           ┌──────────────────┼──────────┐  │
│                                           ▼                  ▼          │  │
│                                      ┌─────────┐        ┌─────────┐     │  │
│                                      │  Sink   │        │  Sink   │     │  │
│                                      │ (Kafka) │        │ (Kafka) │     │  │
│                                      │  A.L.O  │        │  E.O.   │     │  │
│                                      └─────────┘        └─────────┘     │  │
└─────────────────────────────────────────────────────────────────────────────┘
```

## User Experience

**Your processor code - just emit to collector:**

```csharp
public class OrderProcessor : IRecordProcessor<OrderEvent> {
    public async ValueTask ProcessRecords(ProcessRecordsContext<OrderEvent> ctx) {
        foreach (var record in ctx.Records) {
            var order = record.GetData<OrderPlaced>();
            
            // Just emit - don't care about sinks
            await ctx.Collector.EmitAsync(new OrderEvent(order.Id, "processed"));
        }
    }
}
```

**Startup - configure sinks:**

```csharp
var worker = KurrentProcessors
    .Create<OrderEvent>(new OrderProcessor())
    .ConnectTo("esdb://localhost:2113")
    .AddSink(new KafkaSink<OrderEvent>(options))              // at-least-once
    .AddSink(new KafkaTransactionalSink<OrderEvent>(txnOpts)) // exactly-once
    .Build();

await worker.RunAsync();
```

## What Happens on Checkpoint

The runtime handles everything automatically:

```
Barrier arrives
      │
      ▼
SinkCoordinator.PrepareCheckpointAsync()
      │
      ├─► KafkaSink: FlushAsync() ─────────► messages sent
      │
      └─► KafkaTransactionalSink: PrepareCommitAsync() ─► committable returned
      │
      ▼
Store checkpoint (positions + committables)
      │
      ▼
SinkCoordinator.CommitAsync()
      │
      └─► KafkaTransactionalSink: CommitAsync() ─► messages visible
      │
      ▼
Continue processing
```

## File Structure

```
├── IRecordProcessor.cs          # User implements this
├── KurrentProcessorBuilder.cs   # Configure sinks at startup
├── ProcessorWorker.cs           # Runtime orchestration (coordinates sinks)
├── Contexts.cs                  # ProcessRecordsContext<T>, etc.
├── Types.cs                     # Record, LogPosition, etc.
└── Sinks/
    ├── ISink.cs                 # ISink<T>, ITransactionalSink<T>
    ├── ICollector.cs            # ICollector<T> - user emits to this
    ├── SinkCoordinator.cs       # Internal - routes to sinks, coordinates checkpoints
    └── Kafka/
        ├── KafkaSinkOptions.cs
        ├── KafkaSink.cs         # At-least-once (flush on checkpoint)
        └── KafkaTransactionalSink.cs  # Exactly-once (2PC)
```

## Sink Types

| Type | Guarantee | Runtime Behavior |
|------|-----------|------------------|
| `ISink<T>` | At-least-once | `FlushAsync()` on checkpoint |
| `ITransactionalSink<T>` | Exactly-once | `PrepareCommitAsync()` → checkpoint → `CommitAsync()` |

## Key Points

1. **User code never manages sinks directly** - just emit to collector
2. **Sinks configured at startup** - via builder `.AddSink(...)`
3. **Runtime coordinates checkpoints** - PrepareCommit/Commit handled automatically
4. **Recovery is automatic** - uncommitted transactions committed on startup
