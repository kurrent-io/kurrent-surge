# System Architecture Document
## Kurrent Processors

---

## Document Control

| Field              | Value                                    |
|--------------------|------------------------------------------|
| **Product**        | Kurrent Processors                       |
| **Author(s)**      | Sergio Silveira                          |
| **Status**         | Draft                                    |
| **Last Updated**   | January 2026                             |
| **PRD Reference**  | `product-brief.md`                       |
| **Feature Spec**   | `feature-spec.md`                        |

---

## 1. Overview

This document provides a high-level architectural map of Kurrent Processors. For detailed design, see `feature-spec.md`. For product context, see `product-brief.md`.

### 1.1 Quick Reference

| Aspect                   | Summary                                          | Detail Location      |
|--------------------------|--------------------------------------------------|----------------------|
| Worker Protocol          | Bidirectional streaming for records/checkpoints  | `feature-spec.md#3`  |
| Coordinated Checkpointing| Chandy-Lamport barrier-based snapshots           | `feature-spec.md#3.3`|
| API Contracts            | Interfaces, messages, builder                    | `feature-spec.md#5`  |
| Error Handling           | Connection and runtime errors                    | `feature-spec.md#8`  |

---

## 2. System Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         KURRENT PROCESSORS                                   │
│                                                                              │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                        KurrentDB Server                              │   │
│   │                                                                      │   │
│   │   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐            │   │
│   │   │   $all Log  │───►│  Processor  │───►│  Checkpoint │            │   │
│   │   │             │    │   Service   │    │  Coordinator│            │   │
│   │   └─────────────┘    └──────┬──────┘    └─────────────┘            │   │
│   │                             │                                       │   │
│   └─────────────────────────────┼───────────────────────────────────────┘   │
│                                 │ gRPC (bidirectional)                       │
│                                 │                                            │
│         ┌───────────────────────┼───────────────────────┐                   │
│         │                       │                       │                   │
│         ▼                       ▼                       ▼                   │
│   ┌───────────┐           ┌───────────┐           ┌───────────┐            │
│   │  Worker A │           │  Worker B │           │  Worker C │            │
│   │ [P0-P42]  │           │ [P43-P85] │           │ [P86-P127]│            │
│   │           │           │           │           │           │            │
│   │ ┌───────┐ │           │ ┌───────┐ │           │ ┌───────┐ │            │
│   │ │IRecord│ │           │ │IRecord│ │           │ │IRecord│ │            │
│   │ │Process│ │           │ │Process│ │           │ │Process│ │            │
│   │ └───────┘ │           │ └───────┘ │           │ └───────┘ │            │
│   └───────────┘           └───────────┘           └───────────┘            │
│    (autonomous)            (autonomous)            (autonomous)             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

Workers are autonomous agents that connect via gRPC to receive records and participate in coordinated checkpoints. Each worker owns a range of partitions and runs user-defined `IRecordProcessor` logic.

---

## 3. Component Inventory

For detailed interface contracts, see `feature-spec.md#5`.

### 3.1 Client-Side (SDK)

| Component           | Role                              |
|---------------------|-----------------------------------|
| ProcessorWorker     | Lifecycle orchestrator            |
| IProcessorGateway   | Connection factory                |
| IWorkerSession      | Bidirectional message stream      |
| IRecordProcessor    | User processing logic             |

### 3.2 Server-Side (Future)

| Component              | Role                           |
|------------------------|--------------------------------|
| Processor Service      | Record routing, sessions       |
| Checkpoint Coordinator | Barrier distribution           |

### 3.3 Storage

| Store              | Purpose                          |
|--------------------|----------------------------------|
| $all Log           | Source of records (KurrentDB)    |
| Checkpoint Store   | Position persistence (user)      |

---

## 4. Key Flows

For detailed sequence diagrams, see `feature-spec.md#4`.

### 4.1 Processing Flow (Summary)

```
Worker → Gateway.Connect() → Session created → WorkOrder received
       → Worker sends WorkerReady → RecordBatches flow → Process records
```

### 4.2 Checkpoint Flow (Summary)

```
Gateway → CheckpointBarrier (on trigger) → Worker snapshots state
        → Worker sends CheckpointComplete(positions)
```

---

## 5. Deployment Overview

### 5.1 Environments

| Environment | Purpose                | Infrastructure                   |
|-------------|------------------------|----------------------------------|
| Development | Local development      | Docker (KurrentDB) + local worker|
| Staging     | Pre-production testing | KurrentDB cluster + K8s workers  |
| Production  | Live traffic           | KurrentDB cluster + K8s workers  |

### 5.2 Deployment Model

```
┌─────────────────────────────────────────────────────────────────┐
│                    DEPLOYMENT TOPOLOGY                           │
│                                                                  │
│   ┌───────────────────────────────────────────────────────┐     │
│   │              KurrentDB Cluster                         │     │
│   │   ┌─────────┐  ┌─────────┐  ┌─────────┐              │     │
│   │   │ Node 1  │  │ Node 2  │  │ Node 3  │              │     │
│   │   │ (leader)│  │(follower)│ │(follower)│              │     │
│   │   └─────────┘  └─────────┘  └─────────┘              │     │
│   └───────────────────────────────────────────────────────┘     │
│                              │                                   │
│                              │ gRPC                              │
│                              ▼                                   │
│   ┌───────────────────────────────────────────────────────┐     │
│   │              Worker Deployment (K8s / VMs / etc.)      │     │
│   │   ┌─────────┐  ┌─────────┐  ┌─────────┐              │     │
│   │   │Worker A │  │Worker B │  │Worker C │              │     │
│   │   │  Pod    │  │  Pod    │  │  Pod    │              │     │
│   │   └─────────┘  └─────────┘  └─────────┘              │     │
│   └───────────────────────────────────────────────────────┘     │
│                                                                  │
│   Workers deploy independently — scale via K8s HPA, etc.        │
└─────────────────────────────────────────────────────────────────┘
```

---

## 6. Cross-Cutting Conventions

### 6.1 API Conventions

| Convention     | Standard                                |
|----------------|-----------------------------------------|
| API Style      | gRPC bidirectional streaming            |
| Versioning     | Protocol via gateway implementation     |
| Authentication | KurrentDB connection credentials        |
| Error Format   | Domain exceptions (see `feature-spec.md#5.4`)|

### 6.2 Data Conventions

| Convention | Standard                            |
|------------|-------------------------------------|
| ID Format  | Strong-typed structs (SessionId, Partition, LogPosition) |
| Timestamps | DateTimeOffset (UTC)                |
| Naming     | PascalCase for C#, snake_case for proto |

### 6.3 Observability Conventions

| Convention | Standard                            |
|------------|-------------------------------------|
| Logging    | Structured logging (ILogger)        |
| Metrics    | OpenTelemetry                       |
| Tracing    | OpenTelemetry                       |

---

## 7. Decision Log

| ADR       | Decision                                        | Date         |
|-----------|-------------------------------------------------|--------------|
| (TBD)     | Gateway abstraction (Ports & Adapters pattern)  | January 2026 |
| (TBD)     | Chandy-Lamport simplification (single source)   | January 2026 |
| (TBD)     | IVariant discriminated unions for messages      | January 2026 |

---

## 8. Spec Index

### 8.1 Feature Specs

| Spec                           | Status | Last Updated  |
|--------------------------------|--------|---------------|
| Feature Spec: Kurrent Processors | Draft  | January 2026  |

### 8.2 Platform Specs

| Spec                           | Status | Last Updated  |
|--------------------------------|--------|---------------|
| (None yet)                     |        |               |

### 8.3 Related Documents

| Document                              | Link                                           |
|---------------------------------------|------------------------------------------------|
| Product Brief                         | `product-brief.md`                             |
| Feature Spec                          | `feature-spec.md`                              |
| Technical Design (LegacyGateway)      | `_bmad-output/design/processor-gateway.md`     |
| Technical Architecture (detailed)     | `_bmad-output/architecture.md`                 |
| Research Documents                    | `_bmad-output/research/`                       |
