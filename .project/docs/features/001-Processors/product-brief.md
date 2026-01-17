# Product Brief: Kurrent Processors

---

## Document Info

| Field            | Value              |
|------------------|--------------------|
| **Product Name** | Kurrent Processors |
| **Author**       | Sergio Silveira    |
| **Date**         | January 2026       |
| **Status**       | Draft              |

---

## 1. Problem Statement

Data engineers building event-driven applications on KurrentDB spend significant time managing stream processing infrastructure. When they need to build projections, maintain read models, or run process managers, they're forced to choose between building custom retry/checkpoint logic (error-prone, weeks of work) or integrating a separate stream processing cluster like Apache Flink (operational overhead, infrastructure cost, learning curve). Neither option lets them focus on their actual business logic.

---

## 2. Target Users

| Persona       | Description                                                                              | Key Need                                                     |
|---------------|------------------------------------------------------------------------------------------|--------------------------------------------------------------|
| **Primary**   | Data engineers at companies using KurrentDB for event sourcing (mid-size to enterprise) | Process event streams reliably without managing separate infrastructure |
| **Secondary** | Platform/DevOps teams responsible for running KurrentDB-based systems                   | Simple deployment model with built-in fault tolerance and observability |

---

## 3. Product Vision (One-Liner)

> **Kurrent Processors** enables data engineers to build distributed stream processing pipelines with exactly-once state semantics by embedding processing coordination directly in KurrentDB — no separate cluster required.

---

## 4. Goals & Success Metrics

| Goal                           | Metric                              | Target                              |
|--------------------------------|-------------------------------------|-------------------------------------|
| Fast time-to-value             | Time to first working processor     | < 30 minutes (down from days)       |
| Reliable processing            | Checkpoint success rate             | > 99.9% under normal conditions     |
| Fast recovery                  | Recovery time after failure         | < 30 seconds                        |
| Developer adoption             | Active processors in production     | 50+ within 6 months of GA           |
| Operational simplicity         | Infrastructure components to manage | 0 additional (embedded in KurrentDB)|

---

## 5. Core Features (MVP)

### Phase 1: V2 Preview (Single Worker)

| Feature                    | User Story                                                                                         | Priority      |
|----------------------------|----------------------------------------------------------------------------------------------------|---------------|
| Worker Connection Protocol | As a data engineer, I want to connect a worker to a processor so that I can start processing records | **Must Have** |
| Coordinated Checkpointing  | As a data engineer, I want automatic checkpoints so that I can recover from failures without data loss | **Must Have** |
| Record Batching            | As a data engineer, I want records delivered in batches so that I can process efficiently           | **Must Have** |
| Position Tracking          | As a data engineer, I want progress tracked automatically so that I resume from where I left off    | **Must Have** |
| Fluent Builder API         | As a data engineer, I want a simple API so that I can configure processors without boilerplate      | **Must Have** |
| Multiple Checkpoint Triggers | As a data engineer, I want checkpoints on interval/threshold/caught-up so that I have flexible control | **Should Have** |

### Phase 2: Full V2 (Multi-Worker) — Roadmap

| Feature                    | User Story                                                                                        | Priority      |
|----------------------------|---------------------------------------------------------------------------------------------------|---------------|
| Multi-Worker Coordination  | As a data engineer, I want multiple workers so that I can scale processing horizontally           | **Must Have** |
| Elastic Scaling            | As a data engineer, I want workers to join/leave dynamically so that I can scale based on load    | **Must Have** |
| Rebalance Checkpoints      | As a data engineer, I want state to redistribute automatically so that scaling is seamless        | **Must Have** |
| Shared State Store         | As a data engineer, I want distributed state so that workers can share state during rebalancing   | **Should Have** |
| Savepoint API              | As a data engineer, I want manual checkpoints so that I can upgrade or migrate safely             | **Could Have** |

---

## 6. Out of Scope (This Release)

- **Operator DAGs (filter→map→aggregate chains):** Complex transformations are not in scope. Users implement logic in `IRecordProcessor`. Reason: Keeps the model simple; DAGs can be added later if demand exists.

- **Event-time processing / watermarks:** No event-time windows or out-of-order handling. Reason: KurrentDB's $all log is already ordered; primary use cases don't need event-time semantics.

- **Server-hosted workers:** Workers are autonomous (connect from anywhere). Reason: Simplifies deployment model; users control their own worker lifecycle.

- **Exactly-once to external systems:** Deduplication for external APIs/databases is the worker's responsibility. Reason: Each sink has different semantics; cannot generalize.

- **Multi-region replication:** Single cluster only. Reason: Future work; requires cross-cluster coordination.

---

## 7. Open Questions / Risks

### Open Questions

- Should we provide built-in state store implementations (Redis, DuckDB) or only the interface?
- What's the optimal default for `max_parallelism` — 128 or 256?
- Should V2 Preview support multiple workers in the same process (client-side parallelism)?

### Risks

| Risk                                        | Impact                               | Mitigation                                                |
|---------------------------------------------|--------------------------------------|-----------------------------------------------------------|
| Large state slows checkpoints               | Degraded checkpoint latency          | Shared store design; incremental checkpoints planned for future |
| Worker crash during checkpoint              | Checkpoint may fail                  | Configurable failure policy (continue vs fail processor)  |
| Server protocol not ready for GA            | Delays multi-worker support          | LegacyGateway enables V2 Preview without server changes   |
| Complexity of Chandy-Lamport implementation | Bugs in distributed coordination     | Simplified topology (single source) reduces complexity    |

---

## 8. Next Steps

| Action                                       | Owner           | Due Date    |
|----------------------------------------------|-----------------|-------------|
| Review brief with engineering team           | Sergio Silveira | January 2026|
| Complete LegacyGateway implementation        | Engineering     | January 2026|
| User testing with internal projections       | Engineering     | February 2026|
| Create full PRD if V2 Preview validates      | Product         | February 2026|
| Define server protocol for multi-worker      | Architecture    | Q2 2026     |

---

## References

| Document                                  | Location                                      |
|-------------------------------------------|-----------------------------------------------|
| Technical Design (LegacyGateway)          | `_bmad-output/design/processor-gateway.md`    |
| Research: Checkpointing                   | `_bmad-output/research/checkpointing-guide.md`|
| Research: Flink Comparison                | `_bmad-output/research/flink-comparison.md`   |
| Research: Parallelism & Key Groups        | `_bmad-output/research/parallelism-key-groups.md` |
| Full Feature Design                       | `.project/reference/features/processors.md`   |
