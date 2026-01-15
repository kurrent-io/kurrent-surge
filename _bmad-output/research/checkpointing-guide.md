# Checkpointing for KurrentDB
## A Progressive Guide: From Single Worker Today to Distributed Tomorrow

---

## Part 1: The Current State (What Exists Today)

### 1.1 Today's Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         TODAY'S MODEL                                    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                      KurrentDB Server                            │    │
│  │                                                                   │    │
│  │   Stream: "orders"                                               │    │
│  │   [E0, E1, E2, E3, E4, E5, E6, E7, E8, E9, ...]                │    │
│  │                                                                   │    │
│  │   Subscription: "order-processor"                                │    │
│  │   └── Current position: 7                                        │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              │ gRPC (subscribe, read, ack)              │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                    User's Application                            │    │
│  │                                                                   │    │
│  │   while (true) {                                                 │    │
│  │       batch = subscription.ReadBatch();                          │    │
│  │       foreach (event in batch) {                                 │    │
│  │           Process(event);           // User's logic              │    │
│  │           UpdateMyState(event);     // User manages this!        │    │
│  │       }                                                          │    │
│  │       subscription.Ack(batch);      // Commit position           │    │
│  │   }                                                              │    │
│  │                                                                   │    │
│  │   State: ??? (user's problem - maybe a database, maybe memory)  │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  Constraints:                                                            │
│  • ONE worker per subscription (no parallelism yet)                     │
│  • User manages their own state                                         │
│  • If worker crashes: position is saved, but state might be lost       │
│  • User must figure out recovery themselves                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 The Problem Users Face Today

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    USER'S DILEMMA                                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  User wants to: Count orders per customer                               │
│                                                                          │
│  User's code:                                                            │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  var counts = new Dictionary<string, int>();  // In memory!     │    │
│  │                                                                   │    │
│  │  foreach (var event in subscription.ReadBatch()) {              │    │
│  │      var order = Parse(event);                                   │    │
│  │      counts[order.CustomerId]++;                                 │    │
│  │  }                                                               │    │
│  │  subscription.Ack();                                             │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  What happens on crash?                                                  │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  Timeline:                                                        │    │
│  │  ───────────────────────────────────────────────────────────     │    │
│  │  Process E5 → counts["alice"] = 10                               │    │
│  │  Process E6 → counts["bob"] = 5                                  │    │
│  │  Process E7 → counts["alice"] = 11                               │    │
│  │  Ack(E7)    → position saved as 7                                │    │
│  │  Process E8 → counts["alice"] = 12                               │    │
│  │  CRASH!                                                          │    │
│  │                                                                   │    │
│  │  On restart:                                                      │    │
│  │  • Position = 7 (from server)                                    │    │
│  │  • counts = {} (was in memory)                                   │    │
│  │  • Resume from E8... but counts are GONE!                        │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  User's options (all bad):                                              │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  Option A: Replay from beginning                                 │    │
│  │  • Reset position to 0, reprocess all events                    │    │
│  │  • Could take hours/days for large streams                      │    │
│  │                                                                   │    │
│  │  Option B: Use external database for state                      │    │
│  │  • Write every state change to PostgreSQL/Redis                 │    │
│  │  • Slow (network round-trip per event)                          │    │
│  │  • Still not atomic with position commit!                       │    │
│  │                                                                   │    │
│  │  Option C: Accept data loss                                     │    │
│  │  • Shrug                                                         │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Part 2: What You're Building

### 2.1 The New Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         NEW MODEL                                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                      KurrentDB Server                            │    │
│  │                                                                   │    │
│  │   Event Streams (existing):                                      │    │
│  │   ├── "orders" [E0, E1, E2, ...]                                │    │
│  │   └── "inventory" [E0, E1, E2, ...]                             │    │
│  │                                                                   │    │
│  │   NEW - Checkpoint Streams (state storage):                      │    │
│  │   └── "$checkpoint-{subscription-id}"                           │    │
│  │       [                                                          │    │
│  │         {type: "StateSnapshot", checkpointId: 41, data: <bytes>}│    │
│  │         {type: "StateSnapshot", checkpointId: 42, data: <bytes>}│    │
│  │       ]                                                          │    │
│  │                                                                   │    │
│  │   NEW - Checkpoint Coordinator:                                  │    │
│  │   ├── Triggers checkpoints (time-based or event-count)          │    │
│  │   ├── Injects barriers into event stream                        │    │
│  │   ├── Receives acknowledgments from workers                     │    │
│  │   ├── Marks checkpoints as complete                             │    │
│  │   └── Orchestrates recovery on failure                          │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              │ gRPC                                      │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │              Worker (Framework + User Code)                      │    │
│  │                                                                   │    │
│  │  ┌───────────────────────────────────────────────────────────┐   │    │
│  │  │              Your Framework (NEW)                          │   │    │
│  │  │                                                             │   │    │
│  │  │  Local State Store:                                        │   │    │
│  │  │  ┌─────────────────────────────────────────────────────┐   │   │    │
│  │  │  │  Table: kv_state                                     │   │   │    │
│  │  │  │  ┌─────────────┬─────────────┐                      │   │   │    │
│  │  │  │  │ key         │ value       │                      │   │   │    │
│  │  │  │  ├─────────────┼─────────────┤                      │   │   │    │
│  │  │  │  │ cust:alice  │ 12          │                      │   │   │    │
│  │  │  │  │ cust:bob    │ 5           │                      │   │   │    │
│  │  │  │  └─────────────┴─────────────┘                      │   │   │    │
│  │  │  │                                                      │   │   │    │
│  │  │  │  User can also create custom tables!                │   │   │    │
│  │  │  └─────────────────────────────────────────────────────┘   │   │    │
│  │  │                                                             │   │    │
│  │  │  On checkpoint:                                            │   │    │
│  │  │  1. Commit state to shared store                           │   │    │
│  │  │  2. Send CheckpointComplete with positions to server       │   │    │
│  │  │                                                             │   │    │
│  │  │  On recovery:                                              │   │    │
│  │  │  1. Claim partitions from shared store                     │   │    │
│  │  │  2. Resume from checkpoint position                        │   │    │
│  │  │                                                             │   │    │
│  │  └───────────────────────────────────────────────────────────┘   │    │
│  │                              │                                    │    │
│  │                              │ State API                          │    │
│  │                              ▼                                    │    │
│  │  ┌───────────────────────────────────────────────────────────┐   │    │
│  │  │              User's Code (simple!)                         │   │    │
│  │  │                                                             │   │    │
│  │  │  processor.OnEvent(event, ctx) {                          │   │    │
│  │  │      var count = ctx.State.Get<int>($"cust:{event.CustomerId}");│  │
│  │  │      ctx.State.Set($"cust:{event.CustomerId}", count + 1);│   │    │
│  │  │                                                             │   │    │
│  │  │      // Or use SQL for complex queries:                    │   │    │
│  │  │      var topCustomers = ctx.State.Query(                  │   │    │
│  │  │          "SELECT key, value FROM kv_state ORDER BY value DESC LIMIT 10"│
│  │  │      );                                                    │   │    │
│  │  │  }                                                         │   │    │
│  │  │                                                             │   │    │
│  │  │  // User doesn't think about checkpointing!               │   │    │
│  │  │  // Framework handles it automatically                     │   │    │
│  │  │                                                             │   │    │
│  │  └───────────────────────────────────────────────────────────┘   │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 The Two Phases of Your Implementation

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    IMPLEMENTATION PHASES                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  PHASE 1: Single Worker (simpler, build this first)                     │
│  ══════════════════════════════════════════════════                     │
│                                                                          │
│  • One worker per subscription (current constraint)                     │
│  • Checkpointing is straightforward:                                    │
│    - No coordination between workers needed                             │
│    - No barriers needed (only one consumer)                            │
│    - Just: snapshot state, save position                               │
│                                                                          │
│      Server                              Worker                          │
│        │                                    │                            │
│        │   "time to checkpoint"             │                            │
│        │───────────────────────────────────►│                            │
│        │                                    │                            │
│        │                                    │ 1. Stop processing        │
│        │                                    │ 2. Commit state to store  │
│        │                                    │                            │
│        │◄───────────────────────────────────│ 3. Send CheckpointComplete│
│        │   (positions per partition)        │                            │
│        │                                    │                            │
│        │ 4. Mark checkpoint complete        │                            │
│        │                                    │                            │
│        │   "checkpoint done, resume"        │                            │
│        │───────────────────────────────────►│                            │
│        │                                    │ 5. Resume processing      │
│        │                                    │                            │
│                                                                          │
│  ═══════════════════════════════════════════════════════════════════    │
│                                                                          │
│  PHASE 2: Multiple Workers (complex, build later)                       │
│  ════════════════════════════════════════════════                       │
│                                                                          │
│  • Multiple workers share a subscription (like Kafka consumer groups)   │
│  • Need coordination:                                                    │
│    - Barriers flow through the event stream                            │
│    - All workers must checkpoint at same logical point                 │
│    - Server waits for ALL workers to ack before completing             │
│                                                                          │
│      Server              Worker A           Worker B          Worker C  │
│        │                    │                   │                 │     │
│        │  send barrier      │                   │                 │     │
│        │═══════════════════►│═══════════════════│═════════════════│     │
│        │  (to all workers)  │                   │                 │     │
│        │                    │                   │                 │     │
│        │                    │ see barrier       │                 │     │
│        │                    │ commit state      │                 │     │
│        │◄───────────────────│                   │                 │     │
│        │  CheckpointComplete│                   │                 │     │
│        │                    │                   │ see barrier     │     │
│        │                    │                   │ commit state    │     │
│        │◄───────────────────────────────────────│                 │     │
│        │                    CheckpointComplete  │                 │     │
│        │                    │                   │                 │     │
│        │                    │                   │     see barrier │     │
│        │                    │                   │     commit state│     │
│        │◄─────────────────────────────────────────────────────────│     │
│        │                                         CheckpointComplete│     │
│        │                    │                   │                 │     │
│        │ All acked!         │                   │                 │     │
│        │ Mark complete      │                   │                 │     │
│        │                    │                   │                 │     │
│        │  "checkpoint done" │                   │                 │     │
│        │═══════════════════►│═══════════════════│═════════════════│     │
│        │                    │                   │                 │     │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Part 3: Deep Dive - Single Worker Checkpointing

Let's focus on Phase 1 since that's what you can build now.

### 3.1 The State Lifecycle

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    STATE LIFECYCLE (SINGLE WORKER)                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  STARTUP                                                                 │
│  ═══════                                                                 │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  1. Worker connects to server                                    │    │
│  │  2. Server: "Your last checkpoint was #41 at position 1800"     │    │
│  │  3. Worker claims partitions from shared state store            │    │
│  │  4. Worker: "Ready, I'll start from position 1801"              │    │
│  │  5. Begin processing                                             │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  PROCESSING                                                              │
│  ══════════                                                              │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  Loop:                                                           │    │
│  │  1. Read batch of events from server                            │    │
│  │  2. For each event:                                              │    │
│  │     - Call user's processor                                      │    │
│  │     - User reads/writes state (goes to local store)             │    │
│  │  3. Update current position                                      │    │
│  │  4. If checkpoint requested: do checkpoint (see below)          │    │
│  │  5. Continue                                                     │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  CHECKPOINT                                                              │
│  ══════════                                                              │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  Triggered by: server barrier (time interval OR event count)    │    │
│  │                                                                   │    │
│  │  1. Finish current batch (don't checkpoint mid-batch)           │    │
│  │  2. Commit state to shared store via CommitAsync(checkpointId)  │    │
│  │  3. Send CheckpointComplete(checkpoint_id, positions) to server │    │
│  │  4. Server marks checkpoint complete when all workers ack       │    │
│  │  5. Resume processing                                            │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  FAILURE + RECOVERY                                                      │
│  ═════════════════                                                       │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  Worker crashes at position 1900                                 │    │
│  │  Last checkpoint: #42 at position 1847                          │    │
│  │                                                                   │    │
│  │  On restart:                                                     │    │
│  │  1. Connect to server                                            │    │
│  │  2. Server: "Last checkpoint #42 at position 1847"              │    │
│  │  3. Claim partitions from shared store (state already there)    │    │
│  │  4. Resume from position 1848                                    │    │
│  │                                                                   │    │
│  │  Events 1848-1900 are reprocessed, but that's OK:               │    │
│  │  - State is consistent with position 1847                       │    │
│  │  - Reprocessing from 1848 rebuilds correct state                │    │
│  │  - No duplicates, no data loss                                  │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Shared State Store

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SHARED STATE STORE                                    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  The state store is SHARED and accessible by all workers.               │
│  This eliminates upload/download RPCs - workers commit directly.        │
│                                                                          │
│  Worker's View of State Store                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  State is partitioned by key group (partition):                  │    │
│  │  ┌───────────┬─────────────┬─────────────┬──────────────┐       │    │
│  │  │ partition │ key         │ value       │ updated_at   │       │    │
│  │  ├───────────┼─────────────┼─────────────┼──────────────┤       │    │
│  │  │ 12        │ cust:alice  │ {...}       │ 1234567890   │       │    │
│  │  │ 42        │ cust:carol  │ {...}       │ 1234567891   │       │    │
│  │  │ 12        │ cust:dave   │ {...}       │ 1234567892   │       │    │
│  │  └───────────┴─────────────┴─────────────┴──────────────┘       │    │
│  │                                                                   │    │
│  │  Workers only access partitions they own!                        │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  Key Operations:                                                        │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  GetState<T>(partition) - Get/create state for a partition      │    │
│  │  CommitAsync(checkpointId) - Persist all pending changes        │    │
│  │  ClaimPartitionsAsync(partitions, checkpointId) - Take ownership│    │
│  │  ReleasePartitionsAsync(partitions) - Release ownership         │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  Why Shared Store (not RPC transfer)?                                   │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  • No upload/download latency on rebalancing                    │    │
│  │  • Server not a bottleneck for state                            │    │
│  │  • Simpler protocol (fewer RPCs)                                │    │
│  │  • State always available to any worker                         │    │
│  │  • Enables larger state sizes                                   │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.3 The State API

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    STATE API DESIGN                                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Simple Key-Value API (covers 80% of use cases):                        │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  public interface IStateContext                                  │    │
│  │  {                                                                │    │
│  │      // Basic operations                                         │    │
│  │      T? Get<T>(string key);                                     │    │
│  │      void Set<T>(string key, T value);                          │    │
│  │      void Delete(string key);                                   │    │
│  │      bool Exists(string key);                                   │    │
│  │                                                                   │    │
│  │      // Atomic operations                                        │    │
│  │      T GetOrSet<T>(string key, Func<T> factory);               │    │
│  │      T Update<T>(string key, Func<T?, T> updater);             │    │
│  │                                                                   │    │
│  │      // Batch operations                                         │    │
│  │      IEnumerable<(string Key, T Value)> GetByPrefix<T>(string prefix);│
│  │      void DeleteByPrefix(string prefix);                        │    │
│  │                                                                   │    │
│  │      // Advanced: Direct SQL access                             │    │
│  │      IEnumerable<T> Query<T>(string sql, object? parameters = null);│  │
│  │      void Execute(string sql, object? parameters = null);       │    │
│  │  }                                                               │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  User's Code:                                                            │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  public class OrderProcessor : IRecordProcessor                  │    │
│  │  {                                                               │    │
│  │      public ValueTask ProcessRecords(ProcessRecordsContext ctx) │    │
│  │      {                                                           │    │
│  │          foreach (var record in ctx.Records)                    │    │
│  │          {                                                       │    │
│  │              // Simple key-value                                 │    │
│  │              var key = $"customer:{order.CustomerId}";          │    │
│  │              var count = state.Get<int>(key);                   │    │
│  │              state.Set(key, count + 1);                         │    │
│  │          }                                                       │    │
│  │          return ValueTask.CompletedTask;                        │    │
│  │      }                                                           │    │
│  │  }                                                               │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Part 4: The Server Side

### 4.1 What KurrentDB Server Needs to Do

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SERVER RESPONSIBILITIES                               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Server-Side Components:                                                │
│                                                                          │
│  1. CHECKPOINT COORDINATOR                                              │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  Responsibilities:                                               │    │
│  │  • Decide when to checkpoint (configurable: time/count-based)   │    │
│  │  • Send CheckpointBarrier to all workers                        │    │
│  │  • Track checkpoint progress (who has acked)                    │    │
│  │  • Mark checkpoints as complete when all workers ack            │    │
│  │  • Handle timeouts (worker didn't ack in time)                 │    │
│  │                                                                   │    │
│  │  Key Flow:                                                       │    │
│  │  1. Server: checkpoint_id = current_log_position                │    │
│  │  2. Server: send CheckpointBarrier(checkpoint_id) to all workers│    │
│  │  3. Workers: commit state, send CheckpointComplete(positions)   │    │
│  │  4. Server: all acks received → checkpoint COMPLETE             │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  2. PARTITION ASSIGNER                                                  │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  Responsibilities:                                               │    │
│  │  • Calculate partition assignments on worker join/leave         │    │
│  │  • Use consistent hashing for minimal redistribution            │    │
│  │  • Send WorkAssigned messages with partition ranges             │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  3. SESSION MANAGER                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                                                                   │    │
│  │  Responsibilities:                                               │    │
│  │  • Track connected workers and their sessions                   │    │
│  │  • Handle connect/disconnect lifecycle                          │    │
│  │  • Detect worker failures via heartbeat timeout                 │    │
│  │                                                                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  NOTE: Server does NOT handle state upload/download!                    │
│  Workers commit state directly to shared store.                         │
│  Server only coordinates checkpoints and collects acks with positions. │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Part 5: Summary - What You're Building

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    COMPONENTS TO BUILD                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  SERVER SIDE (in KurrentDB):                                            │
│  ════════════════════════════                                           │
│                                                                          │
│  □ ProcessorService (gRPC)                                              │
│    ├── Process() - bidirectional stream for records and commands       │
│    ├── GetProcessor() - get processor info                             │
│    ├── ListProcessors() - list all processors                          │
│    ├── DeleteProcessor() - delete processor                            │
│    ├── TriggerSavepoint() - manual checkpoint                          │
│    └── ListCheckpoints() - list checkpoints                            │
│                                                                          │
│  □ Checkpoint Coordinator                                               │
│    ├── Send CheckpointBarrier to all workers                           │
│    ├── Collect CheckpointComplete with positions                       │
│    └── Mark checkpoint complete when all ack                           │
│                                                                          │
│  □ Partition Assigner                                                   │
│    ├── Calculate assignments on worker join/leave                      │
│    └── Send WorkAssigned messages                                      │
│                                                                          │
│  ─────────────────────────────────────────────────────────────────────  │
│                                                                          │
│  CLIENT SIDE (SDK/Framework):                                           │
│  ═══════════════════════════                                            │
│                                                                          │
│  □ Worker Runtime                                                       │
│    ├── Connect to server                                               │
│    ├── Handle partition assignment                                     │
│    ├── Read record batches                                             │
│    ├── Call user's processor                                           │
│    └── Handle checkpoint barriers                                      │
│                                                                          │
│  □ Shared State Store (ISharedStateStore)                              │
│    ├── GetState<T>(partition)                                          │
│    ├── CommitAsync(checkpointId)                                       │
│    ├── ClaimPartitionsAsync(partitions, checkpointId)                  │
│    └── ReleasePartitionsAsync(partitions)                              │
│                                                                          │
│  □ Checkpoint Handler                                                   │
│    ├── On barrier: commit state to shared store                        │
│    ├── Send CheckpointComplete with positions to server                │
│    └── Track positions per partition                                   │
│                                                                          │
│  □ User-Facing API                                                      │
│    ├── IRecordProcessor { ProcessRecords(context) }                    │
│    └── KurrentProcessorBuilder (fluent configuration)                  │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Questions?

The design uses a **shared state store** model where:

1. **Workers commit state directly** - no upload/download RPCs
2. **Server coordinates checkpoints** - sends barriers, collects acks
3. **Chandy-Lamport barriers** - ensure consistent distributed snapshots

Does this feel right? Anything you'd change before we finalize?
