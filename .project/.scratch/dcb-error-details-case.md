## AppendConsistencyViolationErrorDetails: Map vs Repeated

### Current Design

```protobuf
message AppendConsistencyViolationErrorDetails {
  map<uint32, ConsistencyViolation> violations = 1;
}
```

The map is keyed by the zero-based index of the consistency check in the original
`AppendRecordsRequest.consistency_checks` list.

### The Case for Change

**All consistency checks are always evaluated before abort.** The server does not stop at the first failure; it collects
all violations. However, each violation is self-describing:

- `StreamStateViolation` includes the stream name, expected state, and actual state
- `QueryPredicateViolation` includes a message describing why the predicate failed

**The index-to-violation mapping is redundant.** A client reading "stream `orders-123` expected revision 10, got 5" 
already knows which stream and state failed. It does not need an index to correlate back to the request.

### Proposed Design

```protobuf
message AppendConsistencyViolationErrorDetails {
  repeated ConsistencyViolation violations = 1;
}
```

Benefits:

- **Simpler wire format.** No `map<uint32, ...>` machinery; just a list.
- **Self-contained violations.** Each violation carries all context needed to understand the failure.
- **Cleaner client code.** Iterate and inspect violations directly; no index lookups or correlation logic.
- **No loss of information.** Everything the client needs is still present.

### Trade-off

If a client explicitly needs "which check in my request list failed," that information can live on the
violation itself rather than as a map key:

```protobuf
message ConsistencyViolation {
  // Index of the check in the original consistency_checks list.
  int32 check_index = 3;

  oneof type {
    StreamStateViolation stream_state = 1;
    QueryPredicateViolation query_predicate = 2;
  }
}
```

Same correlation capability, no map. The violation stays self-contained and the container stays a simple list.

### Recommendation

Switch to `repeated ConsistencyViolation violations = 1`. The self-describing nature of violations makes the index 
mapping unnecessary for real-world error handling.
