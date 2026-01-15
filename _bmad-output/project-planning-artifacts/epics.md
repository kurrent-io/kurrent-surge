---
stepsCompleted: [1]
inputDocuments:
  - _bmad-output/prd.md
  - _bmad-output/architecture.md
  - _bmad-output/contracts/protocol.md
  - _bmad-output/research/design.md
  - _bmad-output/research/checkpointing-guide.md
  - _bmad-output/research/parallelism-key-groups.md
---

# Kurrent.Client - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for Kurrent.Client Processors, decomposing the requirements from the PRD and Architecture into implementable stories.

## Requirements Inventory

### Functional Requirements

**US-1: Stateless Record Processing**
- FR1.1: WHEN a worker connects with a processor_id and filter, THE SYSTEM SHALL begin delivering matching records to the worker
- FR1.2: WHEN a record is written to KurrentDB that matches the filter, THE SYSTEM SHALL deliver it to a connected worker within the configured batch timeout
- FR1.3: WHEN no checkpoint exists, THE SYSTEM SHALL start from the configured initial position (latest or earliest)

**US-2: Distributed Processing with Multiple Workers**
- FR2.1: WHEN multiple workers connect with the same processor_id, THE SYSTEM SHALL distribute partitions among them
- FR2.2: WHEN a new worker connects, THE SYSTEM SHALL rebalance partitions across all workers
- FR2.3: WHEN a worker disconnects gracefully, THE SYSTEM SHALL reassign its partitions to remaining workers
- FR2.4: WHEN partitions are reassigned, THE SYSTEM SHALL ensure no records are lost or duplicated within the checkpoint boundary

**US-3: Stateful Processing with Checkpoints**
- FR3.1: WHEN the checkpoint interval elapses, THE SYSTEM SHALL initiate a checkpoint across all workers via barrier
- FR3.2: WHEN a worker receives a barrier, THE SYSTEM SHALL allow the worker to commit its state to the shared store
- FR3.3: WHEN a worker completes its checkpoint, THE SYSTEM SHALL send CheckpointComplete with per-partition positions to server
- FR3.4: WHEN all workers acknowledge a checkpoint, THE SYSTEM SHALL mark the checkpoint as complete
- FR3.5: WHEN a worker fails to acknowledge within the timeout, THE SYSTEM SHALL handle according to the configured failure policy

**US-4: Failure Recovery**
- FR4.1: WHEN a worker crashes, THE SYSTEM SHALL detect the failure (stream error, heartbeat timeout)
- FR4.2: WHEN a failure is detected, THE SYSTEM SHALL reassign the failed worker's partitions
- FR4.3: WHEN partitions are reassigned after failure, THE SYSTEM SHALL provide the last checkpoint_id so new worker can claim state from shared store
- FR4.4: WHEN recovering from checkpoint, THE SYSTEM SHALL resume processing from the checkpoint position (replaying records if needed)

**US-5: Graceful Scaling**
- FR5.1: WHEN a new worker connects, THE SYSTEM SHALL trigger a checkpoint before rebalancing (if workers are active)
- FR5.2: WHEN a worker requests graceful disconnect, THE SYSTEM SHALL complete the current checkpoint before reassigning partitions
- FR5.3: WHEN partitions move between workers, THE SYSTEM SHALL provide checkpoint_id so workers can claim/release from shared store

**US-6: Manual Savepoints**
- FR6.1: WHEN an operator triggers a savepoint via TriggerSavepoint RPC, THE SYSTEM SHALL initiate a checkpoint marked as savepoint
- FR6.2: WHEN a savepoint completes, THE SYSTEM SHALL retain it regardless of retention policy
- FR6.3: WHEN connecting with a specific checkpoint_id, THE SYSTEM SHALL restore from that checkpoint (if it exists)

**US-7: Monitoring and Observability**
- FR7.1: WHEN requested via API, THE SYSTEM SHALL return processor state, worker count, and last checkpoint info
- FR7.2: WHEN requested via API, THE SYSTEM SHALL return per-worker lag metrics (records, bytes, time behind)
- FR7.3: WHEN requested via API, THE SYSTEM SHALL return checkpoint history with duration and state size

**Protocol RPCs (from PRD Section 5.4)**
- FR-RPC1: Process - Bidirectional streaming for Worker↔Server coordination and record delivery
- FR-RPC2: GetProcessor - Unary RPC to retrieve processor configuration and status
- FR-RPC3: ListProcessors - Unary RPC to list all processors with optional filtering
- FR-RPC4: DeleteProcessor - Unary RPC to remove processor (fails if workers connected)
- FR-RPC5: TriggerSavepoint - Unary RPC to create manual checkpoint (never auto-deleted)
- FR-RPC6: ListCheckpoints - Unary RPC to get checkpoint history

**Message Types - Worker → Server (from PRD Section 5.5)**
- FR-MSG1: Connect - Join processor with settings
- FR-MSG2: WorkAccepted - Signal ready to receive records
- FR-MSG3: CheckpointComplete - Acknowledge barrier, report per-partition positions
- FR-MSG4: Disconnect - Graceful shutdown request

**Message Types - Server → Worker (from PRD Section 5.5)**
- FR-MSG5: WorkAssigned - Partition assignment with checkpoint_id for state claim
- FR-MSG6: Records - Batch of records to process
- FR-MSG7: CheckpointBarrier - Trigger checkpoint (Chandy-Lamport barrier)
- FR-MSG8: Disconnected - Session terminated

### NonFunctional Requirements

**NFR-PERF: Performance (PRD Section 6.1)**
- NFR1: Checkpoint latency (p99) < 100ms for state < 1MB
- NFR2: Record delivery latency < 10ms from write to worker receipt
- NFR3: Rebalance time < 5s for partition reassignment
- NFR4: Max parallelism support for 32,768 partitions

**NFR-SCALE: Scalability (PRD Section 6.2)**
- NFR5: Support up to 1,000 workers per processor
- NFR6: Support up to 32,768 partitions per processor
- NFR7: Support 100,000+ records per second (aggregate) depending on worker capacity

**NFR-REL: Reliability (PRD Section 6.3)**
- NFR8: Checkpoint success rate > 99.9% under normal conditions
- NFR9: Recovery time objective (RTO) < 30s from failure detection to resumed processing
- NFR10: Zero data loss (at-least-once guarantee)

**NFR-OBS: Observability (PRD Section 6.4)**
- NFR11: OTEL Metrics API integration for all metrics
- NFR12: OTEL Tracing with span propagation across worker-server boundary
- NFR13: Structured logging via ILogger<T>
- NFR14: ASP.NET Health Checks integration

**NFR-CODE: Codebase Constraints (PRD Section 7.1)**
- NFR15: Inherit ClientModuleBase for module implementation
- NFR16: Return Result<TValue, TError> (no exceptions for expected errors)
- NFR17: Use ConfigureAwait(false) on all awaits
- NFR18: Feature-based namespaces (Kurrent.Client.Processors)
- NFR19: TUnit testing framework with snake_case test methods
- NFR20: K&R braces, 4-space indentation

**NFR-PROTO: Protocol Constraints (PRD Section 7.2)**
- NFR21: Protobuf Edition 2024 with explicit presence
- NFR22: gRPC bidirectional streaming for Process RPC
- NFR23: Proto package kurrentdb.protocol.v2.processors
- NFR24: UNSPECIFIED = 0 enum pattern

### Additional Requirements

**From Architecture - Partitioning (Section 3)**
- AR1: Implement IPartitionAssigner interface for key-to-partition mapping
- AR2: Fixed partition count (max_parallelism) set at processor creation, never changes
- AR3: Deterministic key mapping: same key → same partition (always)
- AR4: State stored per partition in shared store

**From Architecture - Checkpointing (Section 4)**
- AR5: Implement Chandy-Lamport barrier protocol (simplified - single source log)
- AR6: Support checkpoint triggers: INTERVAL, THRESHOLD, CAUGHT_UP, REBALANCE, SAVEPOINT
- AR7: Checkpoint barrier position vs worker position tracking (filtering support)
- AR8: Implement CheckpointFailurePolicy: CONTINUE and FAIL_PROCESSOR modes

**From Architecture - State Management (Section 6)**
- AR9: Implement ISharedStateStore interface (technology-agnostic)
- AR10: GetState<TState>(partition) - Get or create state for partition
- AR11: HasState(partition) - Check if partition has state
- AR12: CommitAsync(checkpointId) - Commit state on barrier arrival
- AR13: ClaimPartitionsAsync(partitions, checkpointId) - Claim partitions on WorkAssigned
- AR14: ReleasePartitionsAsync(partitions) - Release partitions on reassignment

**From Architecture - Client SDK (Section 8)**
- AR15: Implement IRecordProcessor interface with lifecycle methods
- AR16: ProcessRecords (required) - Main processing callback
- AR17: OnInitialize, OnCheckpoint, OnPartitionsAssigned, OnShutdown (optional)
- AR18: Fluent builder API: KurrentProcessors.Create().ConnectTo().WithProcessorId().RunAsync()
- AR19: Position tracking via IWorkerPositionTracker interface

**From Architecture - Server Components (Section 7)**
- AR20: Event-sourced Processor aggregate stored in $processors-{id} stream
- AR21: ProcessorState enum: Idle, Running, Checkpointing, Rebalancing
- AR22: Session Manager for worker connection lifecycle

**From Architecture - Observability (Section 10)**
- AR23: Server metrics: records.processed, checkpoint.count, checkpoint.duration, workers.count
- AR24: Client metrics: records.processed, lag.records, lag.bytes, lag.time
- AR25: Lag calculation: lag_records, lag_bytes, lag_time

**From Research - Design Decisions**
- AR26: Hive Mind Model - Workers are autonomous, server coordinates but doesn't control
- AR27: No RPC-based state transfer (no upload/download RPCs)
- AR28: Workers report per-partition positions to server on checkpoint
- AR29: Client-side parallelism pattern for multiple workers in same process

### FR Coverage Map

{{requirements_coverage_map}}

## Epic List

{{epics_list}}
