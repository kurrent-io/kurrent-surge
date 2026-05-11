# Experimental Drone — Design Notes

A redesign of `Kurrent.Processors.Drone` and friends, kept side-by-side with the
production code so the two can be compared directly. Builds clean against the same
contracts (`IRecordProcessor`, `ProcessorOptions`, `ICheckpointStore`, `SubscribeMessage`).

## Topology

```
Drone
├── ISubscription   ← yields SubscribeMessages from the server (Connected | Record | Checkpoint)
├── Checkpointer    ← loads/persists positions, runs the interval timer, writes barriers
└── Channel<SubscribeMessage>
     ├ writer ← subscription pump  (in Drone.RunSubscriptionPumpAsync)
     ├ writer ← Checkpointer       (interval / manual / shutdown / rebalance barriers)
     └ reader → Drone.RunDispatchAsync
```

Two writers, one reader, one channel. Records and barriers flow through the same
FIFO so `OnCheckpoint` always sees positions consistent with the records preceding it.
Chandy–Lamport asynchronous barrier snapshotting, scoped to a single drone.

## Why simpler than the production `Drone`

| Concern | Production | Experimental |
|---|---|---|
| Position state | Lives on `ISubscriptionSource` next to gRPC, lock, timer, channel writer | Lives on `Checkpointer` — one component, one job |
| `ISubscriptionSource` responsibilities | gRPC + tracker + interval timer + write lock + channel + barrier injection + lifecycle (≈350 lines) | None of the above. The experimental `ISubscription` is a 30-line interface that yields messages. |
| Channel completer | `ISubscriptionSource` (because it owns the writer) | `Checkpointer` only. Sole-completer rule enforced by construction. |
| State machine | `DroneStatus` only — coarse public enum, no `Draining` representation | Internal `DroneState` (6 states with `Draining`) drives behavior; public `DroneStatus` (5 values) is a projection |
| Reentrance guard for `Drain` | Ad-hoc, requires careful reasoning about race windows | `_fsm.TryTransition(Draining)` is atomic — only the caller that wins the transition proceeds |
| Lifecycle hooks | `OnConnected` + `OnCheckpoint` + `OnError` + `OnComplete` | `OnConnected` + `OnCheckpoint` only |
| Position-loading owner | Drone’s caller passes a checkpoint in | Checkpointer loads from the store itself; Drone just hands it the partitions |

## Why each change

### Checkpointer owns its positions
The production `ISubscriptionSource` is doing five jobs: gateway plumbing, tracker
state, interval timer, store writes, and lifecycle. They have different cadences and
different failure modes; bundling them into one class is what makes that file 350
lines and forces a manual `_writeLock` through everything.

The experimental split moves position state to a single 90-line `Checkpointer` with a
small contract:

- `Start(ct)` — load the latest checkpoint from the store, build the tracker, start
  the interval timer if `AutoCheckpoint` is on. Returns the start positions for the
  subscription to begin from.
- `Track(record | position)` — advance the tracker for a partition (idempotent).
- `InjectCheckpoint(trigger, reason)` — write a barrier into the channel; on
  `Shutdown` / `Rebalance`, atomically complete the writer.
- `CompleteCheckpoint(id, positions)` — persist positions to the store and flush
  the tracker up to them.

The Drone hands the Checkpointer its partition list at construction (derived from
`MaxParallelism`) and then asks `Start` to do the rest. The Drone never touches the
store directly.

### `ISubscription` is a subscription, not a kitchen sink
The experimental interface is exactly:

```csharp
IAsyncEnumerable<SubscribeMessage> Subscribe(RecordPosition[] startPositions, CancellationToken ct);
```

The implementation (`Subscription.cs`) opens the gRPC stream, yields messages,
and stamps partitions on records. Everything else — interval timer, tracker,
barriers, store — lives in the Checkpointer.

### One channel, one completer
The production code holds the channel inside `Subscribe(...)`’s `IAsyncEnumerable`
body and uses `_writeLock` to serialize the shutdown protocol. The experimental
code makes `Checkpointer` the single completer. Other writers (the subscription
pump) `WriteAsync` records but never call `Complete()`. On shutdown, only the
Checkpointer’s `InjectCheckpoint(Shutdown|Rebalance)` writes the final barrier
and closes the writer — atomically under the `Lock`.

### State vs. Status separated
`DroneStatus` is a public enum for observers (5 values, including `Unspecified`).
`DroneState` is an internal FSM (6 values, including `Draining`) that drives
behavior. Production code only has `DroneStatus`; without `Draining`, the
shutdown reentrance guard becomes either a lock-plus-flag or a race. The
experimental design uses `_fsm.TryTransition(Draining)` — atomic by construction
— and projects to `DroneStatus.Processing` for the public surface.

### Fewer lifecycle hooks
- `OnError` — removed. No useful action besides logging; the user can catch in
  `ProcessRecord` if they want to log there.
- `OnComplete` — removed. Redundant with the final `OnCheckpoint(Trigger=Shutdown
  or Rebalance)`, which carries the same "we're stopping" signal plus the
  positions to act on.
- `OnConnected` — kept. Useful when subscription liveness matters (warm caches,
  flip a UI indicator).
- `OnCheckpoint` — kept. The user’s only data-path observability hook.

### `.NET 9+` `Lock` instead of monitor on object
Both `StateMachine<T>` and `Checkpointer` use the new `System.Threading.Lock`.
Better semantics, stronger guarantees, the modern default.

## Topology vs. behavior at a glance

| | Production | Experimental |
|---|---|---|
| Lines of code (roughly) | ~700 across `Drone.cs`, `SubscriptionSource.cs`, `MessageProcessor.cs` | ~400 across the experimental folder |
| Public types | `Drone`, `ISubscriptionSource`, `SubscriptionSource` (internal), … | `Drone`, `Checkpointer`, `ISubscription`, `Subscription` |
| Files | many | seven |

## Not yet implemented (and why that's fine)

- **Rebalance orchestrator.** Production has none either. The seam is `Drone.Drain(reason)`
  followed by recreate-and-`Start()`. When leases land, the lease coordinator calls
  into that.
- **Disconnect / Goodbye.** Dropped intentionally. The final
  `OnCheckpoint(Shutdown|Rebalance)` carries the positions; the next drone reads
  them from the same store.
- **Progress / metrics.** `DroneProgress.Empty` everywhere. Wire up the production
  `ProgressTracker` if telemetry becomes load-bearing.
- **Batch processor.** Only `IRecordProcessor` is wired. `IRecordBatchProcessor`
  would slot in as a second case in `RunDispatchAsync` (or a wrapper that batches
  before dispatch), same as the production split.

## File map

| File | Role |
|---|---|
| `Drone.cs` | Coordinator. Owns the channel, FSM, dispatch loop, and subscription pump. |
| `Checkpointer.cs` | Position state. Loads from the store, runs the timer, writes barriers, sole channel completer. |
| `ISubscription.cs` | The subscription seam (yields `SubscribeMessage`). |
| `Subscription.cs` | `ISubscription` impl over the V1 streams gRPC subscription, with client-side partition hashing. |
| `DroneState.cs` | Internal FSM enum (`Created`/`Starting`/`Running`/`Draining`/`Stopped`/`Faulted`). |
| `StateMachine.cs` | Generic FSM helper with `Lock`-protected transitions. |
| `DESIGN.md` | This file. |
