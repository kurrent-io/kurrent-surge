---
stepsCompleted: [1, 2, 3, 4, 5]
inputDocuments:
  - docs/index.md
  - .project/project.md
  - openspec/changes/add-processors/contracts/protocol.md (v1.9)
  - .project/specs/001-processors/plan.md (v2.5)
  - .project/specs/001-processors/spec.md (v1.0)
documentCounts:
  briefs: 0
  research: 0
  brainstorming: 1
  projectDocs: 5
workflowType: 'prd'
lastStep: 5
project_name: 'Kurrent.Client'
user_name: 'Sergio'
date: '2025-12-25'
version: '1.0'
status: 'Draft'
canonical_docs:
  - _bmad-output/prd.md (this file)
  - _bmad-output/architecture.md
  - _bmad-output/contracts/protocol.md
  - _bmad-output/research/references.md
supersedes:
  - .project/specs/001-processors/ (to be archived)
  - openspec/changes/add-processors/ (to be archived)
architectural_decisions:
  - "Two sides: Client SDK + Server Protocol"
  - "Ports and Adapters (Hexagonal Architecture)"
  - "Target: Kurrent Client v2.0"
  - "Shared state store (technology-agnostic) for worker state"
  - "Server-coordinated checkpoints via Chandy-Lamport barriers"
  - "Workers report per-partition positions to server on checkpoint"
  - "No RPC-based state transfer (no upload/download RPCs)"
  - "Must follow: ClientModuleBase, Result pattern, IVariant, feature-based namespaces"
---

# Product Requirements Document: Kurrent Processors

**Author:** Sergio
**Date:** 2025-12-25
**Version:** 1.0
**Status:** Draft

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Problem Statement](#2-problem-statement)
3. [Goals and Non-Goals](#3-goals-and-non-goals)
4. [User Stories](#4-user-stories)
5. [Functional Requirements](#5-functional-requirements)
6. [Non-Functional Requirements](#6-non-functional-requirements)
7. [Technical Constraints](#7-technical-constraints)
8. [Success Metrics](#8-success-metrics)
9. [Dependencies](#9-dependencies)
10. [Risks and Mitigations](#10-risks-and-mitigations)
11. [Open Questions](#11-open-questions)
12. [Related Documents](#12-related-documents)

---

## 1. Executive Summary

Kurrent Processors is a distributed stream processing framework for Kurrent.Client v2.0 that enables scalable, fault-tolerant processing of event streams from KurrentDB.

### The Hive Mind Model

Unlike traditional job schedulers, KurrentDB doesn't deploy or manage workers. Workers are **autonomous**—they connect when ready, disconnect when done. The processor emerges from workers coordinating through the server. This is similar to Apache Flink's Reactive Mode (2021).

### Key Architectural Decisions

| Decision | Description |
|----------|-------------|
| **Two-Sided Design** | Client SDK (worker implementation) + Server Protocol (gRPC coordination) |
| **Shared State Store** | Workers commit state directly to a shared store (technology-agnostic) |
| **Server-Coordinated Checkpoints** | Chandy-Lamport barriers; server collects per-partition positions from all workers |
| **No State Transfer RPCs** | Workers access shared store directly; no upload/download RPCs needed |
| **Partition-Based Distribution** | Up to 32,768 partitions with consistent hashing |

### Value Propositions

| Stakeholder | Value |
|-------------|-------|
| **Developers** | Simple `IRecordProcessor` interface, automatic scaling, built-in checkpointing |
| **Operations** | OTEL-based observability, graceful scaling, automatic failure recovery |
| **Business** | Reduced infrastructure complexity, horizontal scalability |

---

## 2. Problem Statement

### Current State

KurrentDB provides event sourcing and stream storage but lacks native distributed processing capabilities. Users must build custom consumer infrastructure for:

- Parallel processing across workers
- Fault-tolerant checkpointing
- Automatic partition rebalancing
- Stateful aggregations

### Core Challenges

| Challenge | Description |
|-----------|-------------|
| **Distributing Stateful Work** | Records with same key must go to same worker (to access same state). How do you parallelize without losing consistency? |
| **Recovering from Failures** | When a worker crashes mid-processing, how do you recover without data loss or duplicate processing? |
| **Scaling Without Downtime** | Adding/removing workers requires redistributing state. How do you do this without stopping everything? |
| **Coordinating Distributed Checkpoints** | Multiple workers must checkpoint at a consistent point. How do you coordinate without a global "stop the world" pause? |

### Solution Approach

A native processor framework using:

1. **Partitions** (key groups) as the unit of work distribution
2. **Chandy-Lamport barriers** for consistent distributed checkpoints
3. **Shared state store** for worker state (eliminates state transfer RPCs)
4. **Server coordination** for checkpoint completion tracking

---

## 3. Goals and Non-Goals

### Goals

| Priority | Goal | Success Criteria |
|----------|------|------------------|
| P0 | **Fault Tolerance** | Recover from any worker failure without data loss |
| P0 | **Elastic Scaling** | Add/remove workers dynamically without restart or data loss |
| P0 | **Stateful Processing** | Support local state with automatic redistribution on scaling |
| P0 | **At-Least-Once Delivery** | Every record delivered at least once (duplicates possible after recovery) |
| P0 | **Exactly-Once State** | State reflects each record exactly once through coordinated checkpoints |
| P1 | **Simple API** | Single gRPC stream; minimal worker implementation complexity |
| P1 | **Observable** | Lag metrics, throughput, checkpoint statistics via OTEL |
| P2 | **Pluggable Storage** | State store is an abstraction (technology-agnostic) |

### Non-Goals

| Non-Goal | Reason |
|----------|--------|
| **Operator DAGs** | No chaining filter→map→aggregate; workers receive raw records |
| **Server-hosted workers** | We coordinate, not execute; workers run externally |
| **Exactly-once to external systems** | Worker's responsibility; we guarantee exactly-once state internally |
| **Multi-region replication** | Checkpoints are local to a single KurrentDB cluster |
| **Stream-to-stream transformations** | Write side is out of scope |

---

## 4. User Stories

### US-1: Stateless Record Processing

**As a** developer
**I want to** process records from KurrentDB as they arrive
**So that** I can react to events in real-time without managing infrastructure

**Acceptance Criteria:**

1. WHEN a worker connects with a processor_id and filter
   THE SYSTEM SHALL begin delivering matching records to the worker

2. WHEN a record is written to KurrentDB that matches the filter
   THE SYSTEM SHALL deliver it to a connected worker within the configured batch timeout

3. WHEN no checkpoint exists
   THE SYSTEM SHALL start from the configured initial position (latest or earliest)

---

### US-2: Distributed Processing with Multiple Workers

**As a** developer
**I want** multiple workers to share the processing load
**So that** I can scale throughput horizontally

**Acceptance Criteria:**

1. WHEN multiple workers connect with the same processor_id
   THE SYSTEM SHALL distribute partitions among them

2. WHEN a new worker connects
   THE SYSTEM SHALL rebalance partitions across all workers

3. WHEN a worker disconnects gracefully
   THE SYSTEM SHALL reassign its partitions to remaining workers

4. WHEN partitions are reassigned
   THE SYSTEM SHALL ensure no records are lost or duplicated within the checkpoint boundary

---

### US-3: Stateful Processing with Checkpoints

**As a** developer
**I want to** maintain state across records with guaranteed consistency
**So that** I can build aggregations, windowed computations, and stateful transformations

**Acceptance Criteria:**

1. WHEN the checkpoint interval elapses
   THE SYSTEM SHALL initiate a checkpoint across all workers via barrier

2. WHEN a worker receives a barrier
   THE SYSTEM SHALL allow the worker to commit its state to the shared store

3. WHEN a worker completes its checkpoint
   THE SYSTEM SHALL send `CheckpointComplete` with per-partition positions to server

4. WHEN all workers acknowledge a checkpoint
   THE SYSTEM SHALL mark the checkpoint as complete

5. WHEN a worker fails to acknowledge within the timeout
   THE SYSTEM SHALL handle according to the configured failure policy

---

### US-4: Failure Recovery

**As a** developer
**I want** automatic recovery from worker failures
**So that** my processing continues without manual intervention or data loss

**Acceptance Criteria:**

1. WHEN a worker crashes
   THE SYSTEM SHALL detect the failure (stream error, heartbeat timeout)

2. WHEN a failure is detected
   THE SYSTEM SHALL reassign the failed worker's partitions

3. WHEN partitions are reassigned after failure
   THE SYSTEM SHALL provide the last checkpoint_id so new worker can claim state from shared store

4. WHEN recovering from checkpoint
   THE SYSTEM SHALL resume processing from the checkpoint position (replaying records if needed)

---

### US-5: Graceful Scaling

**As a** developer
**I want to** add or remove workers without losing progress
**So that** I can scale based on load without downtime

**Acceptance Criteria:**

1. WHEN a new worker connects
   THE SYSTEM SHALL trigger a checkpoint before rebalancing (if workers are active)

2. WHEN a worker requests graceful disconnect
   THE SYSTEM SHALL complete the current checkpoint before reassigning partitions

3. WHEN partitions move between workers
   THE SYSTEM SHALL provide checkpoint_id so workers can claim/release from shared store

---

### US-6: Manual Savepoints

**As an** operator
**I want to** create manual savepoints that are never auto-deleted
**So that** I can safely upgrade workers or recover to known-good states

**Acceptance Criteria:**

1. WHEN an operator triggers a savepoint via `TriggerSavepoint` RPC
   THE SYSTEM SHALL initiate a checkpoint marked as savepoint

2. WHEN a savepoint completes
   THE SYSTEM SHALL retain it regardless of retention policy

3. WHEN connecting with a specific checkpoint_id
   THE SYSTEM SHALL restore from that checkpoint (if it exists)

---

### US-7: Monitoring and Observability

**As an** operator
**I want** visibility into processor health, lag, and checkpoint status
**So that** I can detect and respond to issues proactively

**Acceptance Criteria:**

1. WHEN requested via API
   THE SYSTEM SHALL return processor state, worker count, and last checkpoint info

2. WHEN requested via API
   THE SYSTEM SHALL return per-worker lag metrics (records, bytes, time behind)

3. WHEN requested via API
   THE SYSTEM SHALL return checkpoint history with duration and state size

---

## 5. Functional Requirements

### 5.1 Two-Sided Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         SERVER SIDE                              │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────────────┐ │
│  │   Record    │  │  Checkpoint  │  │      Partition          │ │
│  │   Reader    │  │  Coordinator │  │      Assigner           │ │
│  └─────────────┘  └──────────────┘  └─────────────────────────┘ │
│         │                │                      │                │
│         └────────────────┼──────────────────────┘                │
│                          │                                       │
│                    gRPC Bidirectional Stream                     │
└──────────────────────────┼───────────────────────────────────────┘
                           │
┌──────────────────────────┼───────────────────────────────────────┐
│                          │              CLIENT SIDE              │
│  ┌───────────────────────┴────────────────────────────────────┐ │
│  │                    ProcessorWorker                          │ │
│  │  ┌─────────────┐  ┌──────────────┐  ┌───────────────────┐  │ │
│  │  │   gRPC      │  │  Checkpoint  │  │  IRecordProcessor │  │ │
│  │  │   Client    │  │   Handler    │  │   (User Code)     │  │ │
│  │  └─────────────┘  └──────────────┘  └───────────────────┘  │ │
│  │                          │                                  │ │
│  │                    Shared State Store                       │ │
│  │                    (Technology-Agnostic)                    │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

### 5.2 Server-Side Components

| Component | Responsibility |
|-----------|----------------|
| **Record Reader** | Reads from $all, applies filters, routes to workers by partition |
| **Checkpoint Coordinator** | Sends barriers, collects acks, marks checkpoint complete when all workers respond |
| **Partition Assigner** | Calculates partition assignments using consistent hashing |
| **Session Manager** | Tracks worker connections, handles connect/disconnect lifecycle |

### 5.3 Client-Side Components

| Component | Responsibility |
|-----------|----------------|
| **ProcessorWorker** | Main entry point; manages connection lifecycle |
| **gRPC Client** | Maintains bidirectional stream with server |
| **Checkpoint Handler** | Commits state to shared store on barrier, sends `CheckpointComplete` |
| **IRecordProcessor** | User-implemented interface for processing records |
| **Shared State Store** | Technology-agnostic state storage (user provides implementation) |

### 5.4 Protocol RPCs

| RPC | Direction | Purpose |
|-----|-----------|---------|
| `Process` | Bidirectional streaming | Worker↔Server coordination and record delivery |
| `GetProcessor` | Unary | Retrieve processor configuration and status |
| `ListProcessors` | Unary | List all processors with optional filtering |
| `DeleteProcessor` | Unary | Remove processor (fails if workers connected) |
| `TriggerSavepoint` | Unary | Create manual checkpoint (never auto-deleted) |
| `ListCheckpoints` | Unary | Get checkpoint history |

**Note:** No `UploadCheckpoint` or `DownloadCheckpoint` RPCs. Workers commit state directly to shared store.

### 5.5 Message Types (Process RPC)

**Worker → Server:**

| Message | Purpose |
|---------|---------|
| `Connect` | Join processor with settings |
| `WorkAccepted` | Signal ready to receive records |
| `CheckpointComplete` | Acknowledge barrier, report per-partition positions |
| `Disconnect` | Graceful shutdown request |

**Server → Worker:**

| Message | Purpose |
|---------|---------|
| `WorkAssigned` | Partition assignment with checkpoint_id for state claim |
| `Records` | Batch of records to process |
| `CheckpointBarrier` | Trigger checkpoint (Chandy-Lamport barrier) |
| `Disconnected` | Session terminated |

---

## 6. Non-Functional Requirements

### 6.1 Performance

| Metric | Target |
|--------|--------|
| Checkpoint latency (p99) | < 100ms for state < 1MB |
| Record delivery latency | < 10ms from write to worker receipt |
| Rebalance time | < 5s for partition reassignment |
| Max parallelism | 32,768 partitions |

### 6.2 Scalability

| Metric | Target |
|--------|--------|
| Workers per processor | Up to 1,000 |
| Partitions per processor | Up to 32,768 |
| Records per second (aggregate) | 100,000+ depending on worker capacity |

### 6.3 Reliability

| Metric | Target |
|--------|--------|
| Checkpoint success rate | > 99.9% under normal conditions |
| Recovery time objective (RTO) | < 30s from failure detection to resumed processing |
| Data loss | Zero (at-least-once guarantee) |

### 6.4 Observability

| Category | Implementation |
|----------|----------------|
| Metrics | OTEL Metrics API |
| Tracing | OTEL Tracing with span propagation |
| Logging | Structured logging via `ILogger<T>` |
| Health | ASP.NET Health Checks integration |

---

## 7. Technical Constraints

### 7.1 Codebase Constraints (from .project/project.md)

| Constraint | Requirement |
|------------|-------------|
| Base class | Inherit `ClientModuleBase` |
| Error handling | Return `Result<TValue, TError>` (no exceptions) |
| Async | `ConfigureAwait(false)` everywhere |
| Namespaces | Feature-based (`Kurrent.Client.Processors`) |
| Testing | TUnit with snake_case methods |
| Style | K&R braces, 4-space indentation |

### 7.2 Protocol Constraints

| Constraint | Requirement |
|------------|-------------|
| Protobuf | Edition 2024 with explicit presence |
| gRPC | Bidirectional streaming for `Process` RPC |
| Versioning | Proto package `kurrentdb.protocol.v2.processors` |
| Enums | UNSPECIFIED = 0 pattern |

### 7.3 State Store Constraints

| Constraint | Requirement |
|------------|-------------|
| Interface | Technology-agnostic `ISharedStateStore` |
| Access | All workers can access shared store directly |
| Checkpoint | Workers commit on barrier, report positions to server |
| Claim/Release | Workers claim partitions from store on assignment |

---

## 8. Success Metrics

### 8.1 Adoption Metrics

| Metric | Target (6 months) |
|--------|-------------------|
| Active processors | 100+ in production |
| Weekly active users | 50+ developers |
| Documentation NPS | > 40 |

### 8.2 Technical Metrics

| Metric | Target |
|--------|--------|
| Test coverage | > 80% |
| P99 latency | < 10ms overhead |
| MTTR (failure recovery) | < 60s |

---

## 9. Dependencies

### 9.1 Internal Dependencies

| Dependency | Status |
|------------|--------|
| `KurrentClient` | Existing |
| `ClientModuleBase` | Existing |
| `Result<T, E>` pattern | Existing |
| Streams module | Existing |

### 9.2 External Dependencies

| Dependency | Version | Purpose |
|------------|---------|---------|
| Grpc.Net.Client | 2.x | gRPC communication |
| OpenTelemetry | 1.x | Observability |

---

## 10. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Shared store performance at scale | Medium | High | Benchmark early, design for pluggable implementations |
| Checkpoint coordination complexity | Medium | Medium | Comprehensive integration tests |
| Breaking changes in proto | Low | High | Versioned packages, deprecation policy |
| Learning curve for users | Medium | Medium | Examples, documentation, templates |

---

## 11. Open Questions

| Question | Owner | Status |
|----------|-------|--------|
| Default checkpoint interval? | Sergio | Pending (30s proposed) |
| Max state size per partition? | Sergio | Pending |
| Default shared store implementation? | Sergio | Pending |

---

## 12. Related Documents

### Canonical BMAD Documents

| Document | Purpose |
|----------|---------|
| [Architecture](./architecture.md) | Technical architecture and design |
| [Protocol Specification](./contracts/protocol.md) | gRPC protocol definition |
| [Research References](./research/references.md) | Background and references |

### Superseded Documents (To Be Archived)

| Document | Status |
|----------|--------|
| `.project/specs/001-processors/` | Superseded by BMAD docs |
| `openspec/changes/add-processors/` | Superseded by BMAD docs |
