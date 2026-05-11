# Experimental Drone — Capability Delta vs Production

Systematic comparison of what the experimental drone does and does not carry over
from the production `Drone` + `SubscriptionSource`. Read alongside `DESIGN.md`.

## Deliberately removed

| Capability | Production | Experimental | Rationale |
|---|---|---|---|
| `OnError` hook | `IProcessor.OnError` | Removed | No useful action besides logging; user can catch inside `ProcessRecord` |
| `OnComplete` hook | `IProcessor.OnComplete` | Removed | Redundant with final `OnCheckpoint(Shutdown\|Rebalance)` which carries positions |
| `IRecordBatchProcessor` | Full batched dispatch path | Not wired | Backlog item 6 — slots into `RunDispatchAsync` as a second case |
| `ProgressTracker` | `_progress.TrackRecord/TrackCheckpoint` | `DroneProgress.Empty` everywhere | Backlog item 7 — wire production `ProgressTracker` when telemetry becomes load-bearing |

## Semantic design differences

Not gaps — different on purpose. Each has a rationale that should be validated or
documented as a deliberate departure.

### Position snapshot timing

Production stamps positions at **drain time** — barriers are written with
`PartitionPositions.Empty` and get their positions filled when read from the channel
(`SnapshotPositionsLocked()` in the Subscribe reader). By the time the checkpoint is
yielded, dispatch of any preceding record has completed and called `Track`, so the
snapshot always reflects post-dispatch state.

Experimental stamps positions at **injection time** — `InjectCheckpoint` calls
`_tracker.Snapshot()` and bakes positions into the barrier when it's written. A
concurrent `HandleRecord` → `Track` could still be in flight, making the snapshot
potentially stale by one record.

This is the position-snapshot race flagged in DESIGN.md (backlog item 5). Production
sidesteps it by deferring the snapshot; experimental needs to either adopt the same
approach or prove the race is benign (at-least-once redelivery covers the gap).

### Track locking

Production holds `_writeLock` for batch `Track(IEnumerable<Record>)` so
`InjectCheckpoint`'s snapshot is atomic — it sees either pre-batch or post-batch
state, never partial. Single-record `Track(Record)` is unlocked (same race window
as experimental, but mitigated by drain-time stamping).

Experimental has all `Track` calls unlocked. Combined with injection-time stamping,
partial snapshots are theoretically possible.

### Checkpoint IDs

Production uses `HybridLogicalClock` — monotonic, timestamp-bearing, globally
orderable. Experimental uses `Guid.NewGuid()` — random, no ordering guarantee.
Matters if the checkpoint store or consumers rely on ID ordering.

### State management

Production uses a mutable `DroneStatus` property with `Interlocked.Exchange` as
the start-reentrance guard. Experimental uses a `StateMachine<DroneState>` with
`Lock`-protected transitions and `TryTransition` as the atomic drain guard. The
FSM approach is strictly better — it makes invalid transitions unrepresentable.

## Missing capabilities

Not mentioned in DESIGN.md's "not yet implemented" section. Organized roughly by
impact.

### Correctness / resilience

| # | Capability | Production behavior | Experimental gap |
|---|---|---|---|
| 1 | CancellationToken propagation | `DroneContext with { CancellationToken = stoppingToken }` — user's `ProcessRecord` sees the host token | Never set — user sees `CancellationToken.None` |
| 2 | Shutdown token override | Shutdown checkpoint commits use `CancellationToken.None` so they complete even after host cancellation | Passes `_stopping.Token` — forced disposal cancels the final commit |
| 3 | Record-skipping after stop | Sets `stopping = true`, skips `Record` messages, only passes `Checkpoint` through | Keeps dispatching all records until the channel drains |
| 4 | `CheckpointFailurePolicy` | `Fail` vs continue-and-log when `OnCheckpoint` hook throws | Always swallows hook failures and commits unconditionally |
| 5 | Debug mode | Rethrows hook failures when `Options.Debug` is true (surfaces errors in tests) | Swallows everything — test failures in `OnCheckpoint` are silent |
| 6 | Pre-Connected cancellation | Faults channel with `OperationCanceledException` if cancellation fires before `Connected` — prevents `OnCheckpoint(Shutdown)` without preceding `OnConnected` | No guard — shutdown barrier can fire before `OnConnected` |
| 7 | Safe processor wrappers | Wraps user processor in `SafeRecordProcessor` / `SafeRecordBatchProcessor` | Calls `_processor` directly — unhandled user exceptions in edge paths could propagate unexpectedly |

### Observability / testability

| # | Capability | Production behavior | Experimental gap |
|---|---|---|---|
| 8 | Telemetry / logging | `DroneTelemetry` with structured logging at every lifecycle point | Zero observability — no `ILogger`, no telemetry |
| 9 | `TimeProvider` | Injected, used by `ProgressTracker` and `HybridLogicalClock` | Not injected — `PeriodicTimer` and `DateTimeOffset.UtcNow` use system clock, not testable with fake time |
| 10 | Start reentrance guard | `Interlocked.Exchange(ref _started, 1)` with a purposeful error message | `_fsm.Transition(Starting)` throws on double-start but with a generic FSM message |

### Subscription-layer completeness

These live behind the `ISubscription` seam and can be deferred if the seam is the
boundary for v1.

| # | Capability | Production behavior | Experimental gap |
|---|---|---|---|
| 11 | Explicit start position | Supports `options.Subscription.StartPosition` with `InspectRecord` resolution and store reset | Always loads from checkpoint store |
| 12 | gRPC error translation | Maps `RpcException` to domain exceptions (`StreamNotFoundException`, `StreamTombstonedException`, etc.) | Raw exceptions propagate through |
| 13 | Client-side record filtering | Applies `filter.IsMatch(record)` after partition tagging | Yields everything — no filtering beyond what the server provides |
| 14 | `SequenceGenerator` | Assigns monotonic sequence numbers to records | Not present |

## Priority assessment

Items 1–7 are correctness/resilience gaps that should be addressed before the
experimental drone replaces production. Items 8–10 are observability and testability.
Items 11–14 are subscription-layer completeness behind the `ISubscription` seam —
lowest priority if the real `Subscription` implementation handles them internally.
