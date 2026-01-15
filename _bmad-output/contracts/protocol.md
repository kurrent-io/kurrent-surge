---
version: '2.1'
date: '2026-01-07'
status: 'Draft'
author: 'Sergio'
protobuf_edition: '2024'
package: 'kurrentdb.protocol.v2.processors'
based_on:
  - openspec/changes/add-processors/contracts/protocol.md (v1.9)
changes:
  - 'v2.1: Added positions field to Disconnect message, added FILTER_TIMEOUT trigger, optimized disconnect flow'
---

# Kurrent Processors Protocol Specification

## Table of Contents

1. [Overview](#1-overview)
2. [File Structure](#2-file-structure)
3. [Service Definition](#3-service-definition)
4. [Worker ↔ Server Messages](#4-worker--server-messages)
5. [Processor Management](#5-processor-management)
6. [Checkpoint Management](#6-checkpoint-management)
7. [Shared Types](#7-shared-types)
8. [Error Codes](#8-error-codes)
9. [Message Flows](#9-message-flows)

---

## 1. Overview

### 1.1 Protocol Characteristics

| Characteristic     | Value                                 |
|--------------------|---------------------------------------|
| **Transport**      | gRPC                                  |
| **Serialization**  | Protocol Buffers Edition 2024         |
| **Primary RPC**    | Bidirectional streaming (`Process`)   |
| **State Transfer** | None (workers use shared state store) |

### 1.2 Key Design Decisions

| Decision                        | Rationale                                       |
|---------------------------------|-------------------------------------------------|
| **No upload/download RPCs**     | Workers commit state directly to shared store   |
| **Single bidirectional stream** | Simplifies protocol, maintains ordering         |
| **Edition 2024**                | Explicit presence, default values, future-proof |
| **UNSPECIFIED = 0**             | Detection of unset enum values                  |

### 1.3 Protobuf Edition 2024 Features

| Feature                 | Benefit                                   |
|-------------------------|-------------------------------------------|
| Explicit field presence | All fields have `HasXxx` properties       |
| Default values          | `[default = value]` for non-zero defaults |
| UNSPECIFIED pattern     | Enums detect unset vs explicit zero       |
| Wire compatibility      | Binary format identical to proto3         |

---

## 2. File Structure

```
proto/kurrentdb/protocol/v2/processors/
├── shared.proto       # Common messages and enums
├── processors.proto   # Service and streaming protocol
├── events.proto       # Entity stream events
└── errors.proto       # Error codes and details
```

| File               | Purpose                                              |
|--------------------|------------------------------------------------------|
| `shared.proto`     | Records, settings, enums shared across service       |
| `processors.proto` | ProcessorService definition and Process RPC messages |
| `events.proto`     | Events for processor entity streams (event sourcing) |
| `errors.proto`     | Error codes with gRPC status mappings                |

---

## 3. Service Definition

```protobuf
service ProcessorService {
    // Main bidirectional stream for control and record flow.
    rpc Process(stream WorkerMessage) returns (stream ServerMessage);

    // NOTE: No upload/download RPCs.
    // Workers commit state directly to shared state store.

    // Processor management.
    rpc GetProcessor(GetProcessorRequest) returns (GetProcessorResponse);
    rpc ListProcessors(ListProcessorsRequest) returns (ListProcessorsResponse);
    rpc DeleteProcessor(DeleteProcessorRequest) returns (DeleteProcessorResponse);

    // Checkpoint management.
    rpc TriggerSavepoint(TriggerSavepointRequest) returns (TriggerSavepointResponse);
    rpc ListCheckpoints(ListCheckpointsRequest) returns (ListCheckpointsResponse);
}
```

### 3.1 RPC Summary

| RPC                | Type                    | Purpose                                       |
|--------------------|-------------------------|-----------------------------------------------|
| `Process`          | Bidirectional streaming | Worker↔Server coordination                    |
| `GetProcessor`     | Unary                   | Retrieve processor info                       |
| `ListProcessors`   | Unary                   | List processors with filtering                |
| `DeleteProcessor`  | Unary                   | Remove processor (fails if workers connected) |
| `TriggerSavepoint` | Unary                   | Create manual checkpoint                      |
| `ListCheckpoints`  | Unary                   | Get checkpoint history                        |

---

## 4. Worker ↔ Server Messages

### 4.1 Worker → Server

```protobuf
message WorkerMessage {
    oneof message {
        Connect connect = 1;
        Disconnect disconnect = 2;
        WorkAccepted work_accepted = 3;
        CheckpointComplete checkpoint_complete = 4;
    }
}
```

#### Connect

Initiate a processing session.

```protobuf
message Connect {
    // Processor identifier. Workers with same ID share work.
    string processor_id = 1;

    // Unique identifier for this worker instance.
    string worker_id = 2;

    // Processor settings. Creates processor if doesn't exist.
    ProcessorSettings settings = 3;
}
```

#### Disconnect

End session gracefully. For graceful shutdown, includes final positions to avoid barrier round-trip.

```protobuf
message Disconnect {
    DisconnectReason reason = 1;

    // Optional: final checkpoint positions for graceful shutdown.
    // If provided, server persists these before confirming disconnect.
    // Key: partition number, Value: log position.
    map<int32, int64> positions = 2;
}

enum DisconnectReason {
    DISCONNECT_REASON_UNSPECIFIED = 0;
    DISCONNECT_REASON_SHUTDOWN = 1;    // Graceful shutdown
    DISCONNECT_REASON_ERROR = 2;       // Worker error
}
```

#### WorkAccepted

Signal ready to receive records after WorkAssigned.

```protobuf
message WorkAccepted {}
```

#### CheckpointComplete

Acknowledge checkpoint after committing state to shared store.

```protobuf
message CheckpointComplete {
    // The checkpoint being acknowledged.
    int64 checkpoint_id = 1;

    // Last processed position per partition.
    // Key: partition number, Value: log position.
    map<int32, int64> positions = 2;
}
```

### 4.2 Server → Worker

```protobuf
message ServerMessage {
    oneof message {
        WorkAssigned work_assigned = 1;
        Disconnected disconnected = 2;
        Records records = 3;
        CheckpointBarrier checkpoint_barrier = 4;
    }
}
```

#### WorkAssigned

Partition assignment (initial or rebalance).

```protobuf
message WorkAssigned {
    // Partitions this worker is responsible for.
    repeated int32 partitions = 1;

    // Checkpoint to resume from. Worker claims partitions from shared store.
    // Absent if new processor with no checkpoint.
    int64 checkpoint_id = 2;
}
```

#### Records

Batch of records to process.

```protobuf
message Records {
    repeated Record records = 1;
}
```

#### CheckpointBarrier

Trigger checkpoint (Chandy-Lamport barrier).

```protobuf
message CheckpointBarrier {
    // Checkpoint identifier (log position at barrier).
    int64 checkpoint_id = 1;

    // What triggered this checkpoint.
    CheckpointTrigger trigger = 2;
}
```

#### Disconnected

Session terminated.

```protobuf
message Disconnected {
    DisconnectedReason reason = 1;
    string message = 2;
}

enum DisconnectedReason {
    DISCONNECTED_REASON_UNSPECIFIED = 0;
    DISCONNECTED_REASON_REQUESTED = 1;          // Worker requested
    DISCONNECTED_REASON_SERVER_SHUTDOWN = 2;    // Server shutting down
    DISCONNECTED_REASON_CHECKPOINT_TIMEOUT = 3; // Failed to ack in time
    DISCONNECTED_REASON_ERROR = 4;              // Protocol error
}
```

---

## 5. Processor Management

### 5.1 GetProcessor

```protobuf
message GetProcessorRequest {
    string processor_id = 1;
}

message GetProcessorResponse {
    ProcessorInfo processor = 1;
}

message ProcessorInfo {
    string processor_id = 1;
    ProcessorSettings settings = 2;
    ProcessorState state = 3;
    repeated WorkerInfo workers = 4;
    CheckpointInfo last_checkpoint = 5;
    ProcessorStats stats = 6;
    google.protobuf.Timestamp create_time = 7;
}

enum ProcessorState {
    PROCESSOR_STATE_UNSPECIFIED = 0;
    PROCESSOR_STATE_RUNNING = 1;
    PROCESSOR_STATE_IDLE = 2;
    PROCESSOR_STATE_CHECKPOINTING = 3;
    PROCESSOR_STATE_REBALANCING = 4;
}
```

### 5.2 ListProcessors

```protobuf
message ListProcessorsRequest {
    ProcessorState state = 1;          // Filter by state (optional)
    int32 page_size = 2 [default = 100];
    string page_token = 3;
}

message ListProcessorsResponse {
    repeated ProcessorSummary processors = 1;
    string next_page_token = 2;
}

message ProcessorSummary {
    string processor_id = 1;
    ProcessorState state = 2;
    int32 worker_count = 3;
    int64 last_checkpoint_id = 4;
    google.protobuf.Timestamp create_time = 5;
    google.protobuf.Timestamp update_time = 6;
}
```

### 5.3 DeleteProcessor

```protobuf
message DeleteProcessorRequest {
    string processor_id = 1;
}

message DeleteProcessorResponse {}
```

**Note:** Fails with `PROCESSOR_HAS_ACTIVE_WORKERS` if workers are connected.

---

## 6. Checkpoint Management

### 6.1 TriggerSavepoint

```protobuf
message TriggerSavepointRequest {
    string processor_id = 1;
}

message TriggerSavepointResponse {
    // Checkpoint ID assigned to this savepoint.
    int64 checkpoint_id = 1;
}
```

**Note:** Savepoints complete asynchronously. Use `ListCheckpoints` to check status.

### 6.2 ListCheckpoints

```protobuf
message ListCheckpointsRequest {
    string processor_id = 1;
    int32 page_size = 2 [default = 100];
    string page_token = 3;
}

message ListCheckpointsResponse {
    repeated CheckpointSummary checkpoints = 1;  // Newest first
    string next_page_token = 2;
}

message CheckpointSummary {
    int64 checkpoint_id = 1;
    google.protobuf.Timestamp create_time = 2;
    int64 duration_ms = 3;
    int64 state_size = 4;
    CheckpointTrigger trigger = 5;
    CheckpointStatus status = 6;
    bool is_savepoint = 7;
    int32 partition_count = 8;
}

enum CheckpointStatus {
    CHECKPOINT_STATUS_UNSPECIFIED = 0;
    CHECKPOINT_STATUS_IN_PROGRESS = 1;
    CHECKPOINT_STATUS_COMPLETED = 2;
    CHECKPOINT_STATUS_FAILED = 3;
}
```

---

## 7. Shared Types

### 7.1 Settings

```protobuf
message ProcessorSettings {
    int32 max_parallelism = 1 [default = 128];  // Range: 1-32768
    SubscriptionSettings subscription = 2;
    BatchingSettings batching = 3;
    CheckpointSettings checkpoint = 4;
}

message SubscriptionSettings {
    string filter = 1;                    // e.g., "stream_id LIKE 'orders-%'"
    string partition_key_selector = 2;    // Required when max_parallelism > 1
    InitialPosition initial_position = 3 [default = INITIAL_POSITION_LATEST];
    int64 start_position = 4;             // Override start position
}

message BatchingSettings {
    int32 max_size = 1 [default = 100];
    int64 timeout_ms = 2 [default = 100];
}

message CheckpointSettings {
    int64 interval_ms = 1 [default = 30000];
    int32 threshold = 2 [default = 10000];
    int64 timeout_ms = 3 [default = 60000];
    int32 retention_count = 4 [default = 3];
    CheckpointFailurePolicy failure_policy = 5 [default = CHECKPOINT_FAILURE_POLICY_CONTINUE];
}
```

### 7.2 Enums

```protobuf
enum InitialPosition {
    INITIAL_POSITION_UNSPECIFIED = 0;
    INITIAL_POSITION_LATEST = 1;    // New records only
    INITIAL_POSITION_EARLIEST = 2;  // All records from beginning
}

enum CheckpointTrigger {
    CHECKPOINT_TRIGGER_UNSPECIFIED = 0;
    CHECKPOINT_TRIGGER_INTERVAL = 1;       // Time elapsed
    CHECKPOINT_TRIGGER_THRESHOLD = 2;      // Record count
    CHECKPOINT_TRIGGER_CAUGHT_UP = 3;      // Reached end of log
    CHECKPOINT_TRIGGER_REBALANCE = 4;      // Worker membership changed
    CHECKPOINT_TRIGGER_SAVEPOINT = 5;      // User-triggered (never auto-deleted)
    CHECKPOINT_TRIGGER_FILTER_TIMEOUT = 6; // Filter matched nothing for configured duration
}

enum CheckpointFailurePolicy {
    CHECKPOINT_FAILURE_POLICY_UNSPECIFIED = 0;
    CHECKPOINT_FAILURE_POLICY_CONTINUE = 1;        // Disconnect failed worker, continue
    CHECKPOINT_FAILURE_POLICY_FAIL_PROCESSOR = 2;  // Stop all workers
}
```

### 7.3 Records

```protobuf
message Record {
    string record_id = 1;
    RecordPosition position = 2;
    SchemaInfo schema = 3;
    bytes data = 4;
    map<string, google.protobuf.Value> properties = 5;
    google.protobuf.Timestamp timestamp = 6;
}

message RecordPosition {
    int64 position = 1;          // Global log position
    string stream = 2;           // Stream name
    int64 stream_revision = 3;   // Position within stream
}

message SchemaInfo {
    SchemaFormat format = 1;
    string name = 2;             // e.g., "OrderCreated"
    string id = 3;               // Schema registry ID
}

enum SchemaFormat {
    SCHEMA_FORMAT_UNSPECIFIED = 0;
    SCHEMA_FORMAT_JSON = 1;
    SCHEMA_FORMAT_PROTOBUF = 2;
    SCHEMA_FORMAT_AVRO = 3;
    SCHEMA_FORMAT_BYTES = 4;
}
```

### 7.4 Worker and Checkpoint Info

```protobuf
message WorkerInfo {
    string worker_id = 1;
    string session_id = 2;
    google.protobuf.Timestamp connect_time = 3;
    repeated int32 partitions = 4;
    map<int32, int64> positions = 5;
    int64 processed_record_count = 6;
    int64 processed_bytes = 7;
    int64 lag_records = 8;
    int64 lag_bytes = 9;
    int64 lag_time_ms = 10;
    google.protobuf.Timestamp last_record_time = 11;
}

message CheckpointInfo {
    int64 checkpoint_id = 1;
    map<int32, int64> positions = 2;
    google.protobuf.Timestamp create_time = 3;
    map<string, string> metadata = 4;
}
```

---

## 8. Error Codes

```protobuf
enum ProcessorsError {
    PROCESSORS_ERROR_UNSPECIFIED = 0;
    PROCESSORS_ERROR_PROCESSOR_NOT_FOUND = 1;           // NOT_FOUND
    PROCESSORS_ERROR_PROCESSOR_ALREADY_EXISTS = 2;      // ALREADY_EXISTS
    PROCESSORS_ERROR_PROCESSOR_NOT_RUNNING = 3;         // FAILED_PRECONDITION
    PROCESSORS_ERROR_WORKER_ALREADY_CONNECTED = 4;      // ALREADY_EXISTS
    PROCESSORS_ERROR_CHECKPOINT_NOT_FOUND = 5;          // NOT_FOUND
    PROCESSORS_ERROR_CHECKPOINT_TIMEOUT = 6;            // DEADLINE_EXCEEDED
    PROCESSORS_ERROR_CHECKPOINT_FAILED = 7;             // ABORTED
    PROCESSORS_ERROR_INVALID_PARTITION_RANGE = 8;       // INVALID_ARGUMENT
    PROCESSORS_ERROR_PARTITION_NOT_ASSIGNED = 9;        // PERMISSION_DENIED
    PROCESSORS_ERROR_REBALANCE_IN_PROGRESS = 10;        // ABORTED
    PROCESSORS_ERROR_SETTINGS_MISMATCH = 11;            // FAILED_PRECONDITION
    PROCESSORS_ERROR_PROCESSOR_HAS_ACTIVE_WORKERS = 12; // FAILED_PRECONDITION
}
```

---

## 9. Message Flows

### 9.1 Worker Connection

```
Worker                                         Server
   │                                              │
   │─── Connect(processor_id, worker_id, settings)│
   │                                              │
   │◄── WorkAssigned(partitions, checkpoint_id) ──│
   │                                              │
   │  [Claim partitions from shared store]        │
   │                                              │
   │─── WorkAccepted ────────────────────────────►│
   │                                              │
   │◄── Records([...]) ───────────────────────────│
   │◄── Records([...]) ───────────────────────────│
   │                                              │
```

### 9.2 Checkpoint

```
Worker                                         Server
   │                                              │
   │  [Checkpoint triggered]                      │
   │                                              │
   │◄── CheckpointBarrier(id=100, INTERVAL) ──────│
   │                                              │
   │  [Commit state to shared store]              │
   │                                              │
   │─── CheckpointComplete(id=100, positions) ───►│
   │                                              │
   │  [All workers acked → checkpoint COMPLETE]   │
   │                                              │
```

### 9.3 Rebalancing (Worker Join)

```
Worker 1              Server              Worker 2 (new)
   │                    │                      │
   │                    │◄─── Connect ─────────│
   │                    │                      │
   │◄─ CheckpointBarrier(REBALANCE) ───────────│
   │                    │                      │
   │  [Commit state]    │                      │
   │                    │                      │
   │── CheckpointComplete ────────────────────►│
   │                    │                      │
   │  [Redistribute partitions]                │
   │                    │                      │
   │◄─ WorkAssigned([0-63]) ───────────────────│
   │                    │── WorkAssigned([64-127]) ─►│
   │                    │                      │
   │                    │  [Claim from store]  │
   │                    │                      │
   │── WorkAccepted ───────────────────────────│
   │                    │◄── WorkAccepted ─────│
   │                    │                      │
```

### 9.4 Graceful Disconnect

**Optimized flow** - Worker includes final positions in Disconnect to avoid barrier round-trip:

```
Worker                                         Server
   │                                              │
   │─── Disconnect(SHUTDOWN, positions) ─────────►│
   │                                              │
   │  [Server persists checkpoint from positions] │
   │                                              │
   │◄── Disconnected(REQUESTED) ──────────────────│
   │                                              │
   │  [Connection closed]                         │
   │                                              │
```

**Multi-worker scenario** - Other workers still receive REBALANCE barrier:

```
Worker 1 (leaving)    Server              Worker 2 (staying)
   │                    │                      │
   │── Disconnect ─────►│                      │
   │   (positions)      │                      │
   │                    │◄─ CheckpointBarrier ─│
   │                    │   (REBALANCE)        │
   │◄─ Disconnected ────│                      │
   │   (REQUESTED)      │                      │
   │                    │── CheckpointComplete►│
   │                    │                      │
   │                    │◄─ WorkAssigned ──────│
   │                    │   (new partitions)   │
   │                    │                      │
```

### 9.5 Failure Recovery

```
Worker (restarted)                            Server
   │                                              │
   │  [Worker crashed and restarted]              │
   │                                              │
   │─── Connect(processor_id, worker_id) ────────►│
   │                                              │
   │  [Find last checkpoint]                      │
   │                                              │
   │◄── WorkAssigned(partitions, checkpoint_id) ──│
   │                                              │
   │  [Claim partitions from shared store]        │
   │                                              │
   │─── WorkAccepted ────────────────────────────►│
   │                                              │
   │  [Resume from checkpoint position]           │
   │                                              │
   │◄── Records([...]) ───────────────────────────│
   │                                              │
```

---

## Appendix A: Shared State Store Model

This protocol assumes workers have access to a **shared state store** that all workers can access directly. This eliminates the need for state transfer RPCs.

### State Operations

| Operation   | When                 | Description                              |
|-------------|----------------------|------------------------------------------|
| **Commit**  | On CheckpointBarrier | Worker commits current state to store    |
| **Claim**   | On WorkAssigned      | Worker claims partitions from store      |
| **Release** | On partition loss    | Worker releases partitions back to store |

### Benefits

| Benefit                | Description                                 |
|------------------------|---------------------------------------------|
| No state transfer RPCs | Reduced latency and bandwidth               |
| Immediate state access | Any worker can access any partition's state |
| Simplified rebalancing | Claim/release instead of upload/download    |
| Large state support    | Store handles arbitrarily large state       |

### Server's Role

The server still:
- Coordinates barrier timing (Chandy-Lamport)
- Tracks which checkpoint is complete
- Manages partition assignments
- Does **NOT** handle state transfer
