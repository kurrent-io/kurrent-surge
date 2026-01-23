# KurrentDB Processors - Architecture Review Notes

**Date:** January 21, 2026
**Document Reviewed:** `processors.md` v2.6
**Reviewers:** Jeremy Miller, Yves Reynhout, David Fowler

---

## Executive Summary

The Architects Team reviewed the KurrentDB Processors design document. **Consensus: solid V1 design ready for implementation** with minor clarifications and additions needed before production deployment.

The design correctly applies proven distributed systems patterns (Chandy-Lamport checkpointing, partition-based scaling) while avoiding over-engineering (no DAGs, no server-hosted workers, no exactly-once sinks).

---

## What Works Well

### Protocol & Architecture

| Aspect | Assessment | Reviewer |
|--------|------------|----------|
| Chandy-Lamport barrier protocol | Proven, simple, correct choice | Jeremy Miller |
| Single input channel per worker (no barrier alignment) | Huge simplification vs Flink | Jeremy Miller |
| Hive Mind autonomous worker model | Right choice - workers coordinate, server doesn't control | Jeremy Miller |
| Partition abstraction with fixed count | Deterministic assignment, minimal state movement on rebalance | Jeremy Miller |
| Single bidirectional gRPC stream per worker | Avoids connection proliferation, natural backpressure | David Fowler |
| Flush-then-Barrier batching | Simple protocol, simple worker implementation | David Fowler |
| Client-side parallelism pattern | Server doesn't care where workers run - elegant | David Fowler |

### Documentation Quality

| Aspect | Assessment | Reviewer |
|--------|------------|----------|
| Delivery guarantee boundary | Explicit and honest - internal state vs external systems | Yves Reynhout |
| Section 15 (Delivery Guarantees) | One of the best explanations of at-least-once with exactly-once state | Yves Reynhout |
| Idempotency strategies | Practical examples developers need | Yves Reynhout |
| Position tracking examples | FlushUpTo(8) returning [1,3,5,7] is a good illustration | Yves Reynhout |
| Glossary | Excellent - wish more design docs had one | Yves Reynhout |

### Design Decisions

| Decision | Rationale Validated | Reviewer |
|----------|---------------------|----------|
| Minimal `IRecordProcessor` interface | Lowest common denominator - works for all use cases | Jeremy Miller |
| Deferred incremental checkpoints | Fair for MVP - clear path to add later | Jeremy Miller |
| No aggregate abstraction in core | Users who want it can layer it on top (~20 lines) | Jeremy Miller |

---

## Questions Requiring Clarification

### Q1: `$partition` Property Source
**Raised by:** David Fowler

**Question:** Where and when is the `$partition` property set on records?

**Context:** The example shows `record.Properties["$partition"].GetInt32()` but doesn't specify whether this is set server-side during routing or client-side.

**Recommendation:** Must be server-set before sending to workers. If workers calculate partitions, risk of inconsistency during rebalancing.

**Action:** Add explicit statement in Section 12 (Client-Side Component Design) clarifying server sets this property.

---

### Q2: Checkpoint State Storage Location
**Raised by:** Jeremy Miller

**Question:** Is checkpoint state stored in the same PostgreSQL/KurrentDB cluster or a separate store?

**Context:** For Marten, everything goes to PostgreSQL to simplify operations. Separate checkpoint storage adds operational overhead.

**Action:** Document the recommended deployment topology and trade-offs of co-located vs separate checkpoint storage.

---

### Q3: State Serialization Format
**Raised by:** Jeremy Miller

**Question:** What serialization format for state snapshots? JSON? MessagePack? Protobuf?

**Context:** Parquet mentioned for DuckDB snapshots, but in-memory state format unspecified. Matters for:
- State size (compression)
- Schema evolution (version tolerance)
- Cross-platform compatibility

**Recommendation:** System.Text.Json with source generation for most cases. Document the contract in `IStateSerializer`.

**Action:** Add serialization format guidance to Section 7 (Managing Worker State).

---

### Q4: Lamport Timestamp Persistence
**Raised by:** David Fowler

**Question:** Do workers persist their last seen BarrierId across restarts?

**Context:** Section 6.4 mentions `max(local, received)` rule for restart recovery, but workers need to persist their last seen BarrierId for this to work.

**Action:** Clarify whether `IStateStore` contract includes BarrierId persistence or if this is separate.

---

### Q5: Recovery Position Source
**Raised by:** David Fowler

**Question:** On recovery, where is "the server's position when it sent the barrier" stored?

**Context:** Section 6.3 Example 4 shows workers reporting different positions (10, 11, 8). Document says "We resume from the server's position" but doesn't specify where this is recorded.

**Action:** Clarify checkpoint metadata structure includes the global resume position.

---

## Concerns & Recommendations

### C1: Checkpoint Timeout Window Behavior
**Raised by:** David Fowler
**Severity:** Medium

**Concern:** During checkpoint timeout window, what happens? Are records still flowing to other workers while waiting for a slow worker?

**Current behavior:** Workers snapshot, upload, then send BarrierAck. Slow upload = high checkpoint latency.

**Options to consider:**
1. **Async upload:** Worker snapshots, sends BarrierAck with upload token, continues processing while upload happens in background
2. **Accept current design:** Checkpoint completion blocks on slowest worker (simpler)

**Recommendation:** Document the current behavior explicitly. Consider async upload for Phase 2 if checkpoint latency becomes an issue.

---

### C2: Missing Reset/Replay Operations
**Raised by:** Yves Reynhout, David Fowler
**Severity:** High (for production operations)

**Concern:** No API for resetting a processor to replay from a specific position.

**Current state:** `DeleteProcessor` exists but is destructive. To rebuild a projection from scratch:
1. Delete checkpoint
2. Reset processor to position 0
3. Reprocess everything

**Without atomic reset:** Risk of workers reconnecting to stale state.

**Recommended operations:**
```
ResetProcessor(processorId, position)
  1. Trigger checkpoint (save current state as savepoint)
  2. Disconnect all workers gracefully
  3. Reset position (keep processor configuration)
  4. Allow workers to reconnect from new position

GetCheckpoint(checkpointId)
  - Inspect saved state for debugging

RestoreCheckpoint(checkpointId)
  - Revert to a previous savepoint
```

**Action:** Add to Phase 2 roadmap as production operations requirements.

---

### C3: Missing Observability Metrics
**Raised by:** David Fowler
**Severity:** Medium

**Concern:** Current OTEL metrics (Section 14) are a good start but missing critical production debugging metrics.

**Recommended additions:**

| Metric | Type | Purpose |
|--------|------|---------|
| `checkpoint.upload.duration` | Histogram | Separate from overall checkpoint duration |
| `partition.rebalance.count` | Counter | How often are partitions moving? |
| `worker.connection.duration` | Histogram | How long do workers stay connected? |
| `records.pending` | Gauge | Records buffered but not yet sent to workers |

**Rationale:** "Why are checkpoints slow?" could be upload time, worker processing time, or coordinator overhead. Need metrics to disambiguate.

**Action:** Add to Phase 4 (Observability) roadmap.

---

### C4: Partition Key Design Guidance
**Raised by:** Yves Reynhout
**Severity:** Medium

**Concern:** No guidance on designing partition keys. Easy to make mistakes.

**Recommended guidance section:**
- Choose keys that map to business consistency boundaries
- Avoid hot keys (one customer with 10x more records causes skew)
- Consider composite keys for multi-tenant scenarios
- Example: `customer-123-us` vs `customer-123-eu` — same partition or different?

**Action:** Add "Partition Key Design" section after Section 3 (Distributing Stateful Work).

---

### C5: gRPC Stream Reliability
**Raised by:** David Fowler
**Severity:** Medium

**Concern:** Long-lived bidirectional streams need keepalives, reconnection logic, and careful handling of partial failures.

**Current state:** "Reconnection with backoff" mentioned in Phase 3 roadmap.

**Recommendation:**
- Require workers to send periodic heartbeats (not just waiting for server pings)
- Document expected behavior when streams silently go dead
- Consider this critical path for Phase 3, not nice-to-have

**Action:** Elevate reconnection handling priority in Phase 3.

---

### C6: Optional Aggregate Abstraction Layer
**Raised by:** Jeremy Miller, Yves Reynhout
**Severity:** Low (nice-to-have)

**Context:** Current `IRecordProcessor` is stream-processing style. Users from traditional event sourcing (Marten, EventStoreDB) might expect aggregate-style API.

**Current design (stream processing):**
```csharp
public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
    foreach (var record in ctx.Records) {
        var state = ctx.GetState<MyState>(partition);
        // manually apply
    }
}
```

**Traditional aggregate pattern:**
```csharp
public class OrderAggregate {
    public void Apply(OrderPlaced e) { ... }
    public void Apply(OrderShipped e) { ... }
}
```

**Recommendation:** Keep core minimal. Consider separate package `Kurrent.Processors.Aggregates` for users who want familiar aggregate patterns.

**Action:** Add to backlog for post-V1 consideration.

---

### C7: Getting Started Documentation
**Raised by:** Yves Reynhout
**Severity:** Low (adoption concern)

**Concern:** Document assumes readers understand distributed systems concepts. For broader adoption, need progressive introduction.

**Recommendation:** Add "Getting Started" section showing:
1. Simple stateless processor
2. Add state
3. Add partitioning
4. Add checkpointing

Build understanding incrementally.

**Action:** Add to documentation roadmap.

---

## Action Items Summary

### Must Have (Before Production)

| ID | Action | Owner | Phase |
|----|--------|-------|-------|
| A1 | Clarify `$partition` property is server-set | Doc update | 1 |
| A2 | Document checkpoint storage topology options | Doc update | 1 |
| A3 | Specify state serialization format | Doc update | 1 |
| A4 | Clarify BarrierId persistence requirements | Doc update | 1 |
| A5 | Document global resume position in checkpoint metadata | Doc update | 1 |
| A6 | Add `ResetProcessor` operation | Implementation | 2 |
| A7 | Add checkpoint inspection APIs | Implementation | 2 |

### Should Have (Production Quality)

| ID | Action | Owner | Phase |
|----|--------|-------|-------|
| A8 | Add partition key design guidance section | Doc update | 2 |
| A9 | Add missing observability metrics | Implementation | 4 |
| A10 | Elevate gRPC reconnection handling priority | Planning | 3 |
| A11 | Document checkpoint timeout window behavior | Doc update | 2 |

### Nice to Have (Post-V1)

| ID | Action | Owner | Phase |
|----|--------|-------|-------|
| A12 | Optional aggregate abstraction package | Implementation | Backlog |
| A13 | Getting Started progressive documentation | Doc update | Backlog |

---

## Final Verdict

**Ship V1 as designed.** The architecture is sound, the protocol is correct, and the trade-offs are explicit. Address the "Must Have" documentation clarifications before implementation begins. Add production operations features (`ResetProcessor`, additional metrics) in Phase 2.

> "This is event sourcing that works in the real world." — Jeremy Miller

---

*Next review scheduled after Phase 1 implementation.*
