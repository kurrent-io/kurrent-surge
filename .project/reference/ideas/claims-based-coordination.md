# Claims-Based Coordination: Deep Dive

A detailed analysis of claims/leases-based coordination for distributed stream processors, comparing it with centralized Gateway coordination.

**Version:** 1.0
**Status:** Research / Reference
**Date:** January 2026
**Related:** [Processor Source Design](./processor-source-design.md)

---

## Table of Contents

- [Overview](#overview)
- [How Claims-Based Coordination Works](#how-claims-based-coordination-works)
- [The Greedy Worker Problem](#the-greedy-worker-problem)
- [Distribution Solutions](#distribution-solutions)
  - [Solution 1: Max Claims Per Worker](#solution-1-max-claims-per-worker)
  - [Solution 2: Worker Registry + Dynamic Quota](#solution-2-worker-registry--dynamic-quota)
  - [Solution 3: Cooperative Release](#solution-3-cooperative-release)
  - [Solution 4: Segment Affinity (Hash-Based)](#solution-4-segment-affinity-hash-based)
  - [Solution 5: Consistent Hashing](#solution-5-consistent-hashing)
- [Axon Framework's Approach](#axon-frameworks-approach)
- [Gateway vs Claims-Based Comparison](#gateway-vs-claims-based-comparison)
- [Why Gateway is Better for Distribution](#why-gateway-is-better-for-distribution)
- [When Claims-Based Makes Sense](#when-claims-based-makes-sense)
- [Summary](#summary)

---

## Overview

Claims-based coordination is a **leaderless coordination** pattern where workers self-organize using database locks/claims instead of a central coordinator. This document explores how it works, its limitations, and how it compares to centralized coordination.

**Key Systems Using This Pattern:**
- Axon Framework (when not using Axon Server)
- Some Kafka consumer implementations
- Various distributed job schedulers

---

## How Claims-Based Coordination Works

### The Core Concept

Workers coordinate by writing claims to a shared database table. Each claim has:
- **Owner**: Which worker holds the claim
- **Timestamp**: When the claim was last extended
- **Timeout**: Claims expire if not extended (heartbeat)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              CLAIMS-BASED COORDINATION OVERVIEW                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Token Table (in database):                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  processor_name │ segment │ token      │ owner    │ timestamp       │    │
│  │  ───────────────┼─────────┼────────────┼──────────┼─────────────────│    │
│  │  orders         │ 0       │ {pos:1234} │ worker-1 │ 2024-01-15 10:00│    │
│  │  orders         │ 1       │ {pos:5678} │ worker-2 │ 2024-01-15 10:01│    │
│  │  orders         │ 2       │ {pos:9012} │ worker-1 │ 2024-01-15 10:00│    │
│  │  orders         │ 3       │ {pos:3456} │ NULL     │ (unclaimed)     │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### The Claim Protocol

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              CLAIM PROTOCOL                                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. CLAIM: Worker attempts to claim unclaimed or expired segment            │
│     ┌─────────────────────────────────────────────────────────────────┐     │
│     │  UPDATE tokens                                                   │     │
│     │  SET owner = 'worker-3', timestamp = NOW()                      │     │
│     │  WHERE processor_name = 'orders'                                 │     │
│     │    AND segment = 3                                               │     │
│     │    AND (owner IS NULL OR timestamp < NOW() - INTERVAL '10s')    │     │
│     └─────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  2. EXTEND: Owner periodically extends claim (heartbeat)                    │
│     ┌─────────────────────────────────────────────────────────────────┐     │
│     │  UPDATE tokens                                                   │     │
│     │  SET timestamp = NOW()                                          │     │
│     │  WHERE processor_name = 'orders'                                 │     │
│     │    AND segment = 0                                               │     │
│     │    AND owner = 'worker-1'                                        │     │
│     └─────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  3. RELEASE: Worker releases claim on shutdown                              │
│     ┌─────────────────────────────────────────────────────────────────┐     │
│     │  UPDATE tokens                                                   │     │
│     │  SET owner = NULL                                                │     │
│     │  WHERE processor_name = 'orders'                                 │     │
│     │    AND segment = 0                                               │     │
│     │    AND owner = 'worker-1'                                        │     │
│     └─────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  4. STEAL: If claim expires, another worker can take it                     │
│     Worker-3 sees segment 0's timestamp is old → claims it                  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Startup Race Condition

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              STARTUP: WORKERS RACE TO CLAIM SEGMENTS                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Worker-1 starts                    Worker-2 starts (same time)            │
│      │                                   │                                   │
│      ▼                                   ▼                                   │
│  "Which segments are unclaimed?"    "Which segments are unclaimed?"         │
│      │                                   │                                   │
│      ▼                                   ▼                                   │
│  SELECT * FROM tokens               SELECT * FROM tokens                    │
│  WHERE owner IS NULL                WHERE owner IS NULL                     │
│  OR timestamp < NOW() - 10s         OR timestamp < NOW() - 10s              │
│      │                                   │                                   │
│      │ Sees: [0,1,2,3] available        │ Sees: [0,1,2,3] available         │
│      │                                   │                                   │
│      ▼                                   ▼                                   │
│  Try claim segment 0                Try claim segment 0                     │
│  UPDATE tokens SET owner='w1'       UPDATE tokens SET owner='w2'            │
│  WHERE segment=0 AND owner IS NULL  WHERE segment=0 AND owner IS NULL       │
│      │                                   │                                   │
│      │ ✓ Success (1 row affected)       │ ✗ Fail (0 rows - already claimed)│
│      │                                   │                                   │
│      ▼                                   ▼                                   │
│  Try claim segment 1                Try claim segment 1                     │
│  UPDATE... ✓ Success                UPDATE... ✗ Fail                        │
│      │                                   │                                   │
│      ▼                                   ▼                                   │
│  Now owns: [0, 1]                   Try claim segment 2                     │
│                                     UPDATE... ✓ Success                     │
│                                          │                                   │
│                                          ▼                                   │
│                                     Try claim segment 3                     │
│                                     UPDATE... ✓ Success                     │
│                                          │                                   │
│                                          ▼                                   │
│                                     Now owns: [2, 3]                        │
│                                                                              │
│  RESULT: Work distributed without coordinator!                              │
│  Worker-1: segments 0, 1                                                    │
│  Worker-2: segments 2, 3                                                    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### The Heartbeat/Extend Loop

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              HEARTBEAT MECHANISM                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Configuration:                                                              │
│  • Claim timeout: 10 seconds (if no heartbeat, claim expires)               │
│  • Heartbeat interval: 5 seconds (extend claim before timeout)              │
│                                                                              │
│  Worker loop:                                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  while (running) {                                                   │    │
│  │      // Process events for my segments                               │    │
│  │      foreach (segment in mySegments) {                              │    │
│  │          var events = fetchEvents(segment, lastPosition);           │    │
│  │          processEvents(events);                                      │    │
│  │          updateToken(segment, newPosition);                         │    │
│  │      }                                                               │    │
│  │                                                                      │    │
│  │      // Heartbeat: extend claims every 5 seconds                    │    │
│  │      if (timeSinceLastHeartbeat > 5s) {                            │    │
│  │          foreach (segment in mySegments) {                          │    │
│  │              extendClaim(segment);  // UPDATE timestamp = NOW()     │    │
│  │          }                                                           │    │
│  │          timeSinceLastHeartbeat = 0;                                │    │
│  │      }                                                               │    │
│  │  }                                                                   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Timeline:                                                                   │
│                                                                              │
│  t=0s    t=5s    t=10s   t=15s   t=20s                                     │
│    │       │       │       │       │                                        │
│    ▼       ▼       ▼       ▼       ▼                                        │
│  claim   extend  extend  extend  extend   ← Worker healthy                  │
│    │       │       │       │       │                                        │
│  ──●───────●───────●───────●───────●──────►                                 │
│    │       │       │       │                                                │
│    └───────┴───────┴───────┘                                                │
│        claim valid window                                                    │
│        (10 seconds from last extend)                                        │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Failure Detection & Recovery

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              FAILURE SCENARIO                                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  t=0     Worker-1 claims segment 0, timestamp = 00:00:00                    │
│  t=5     Worker-1 extends claim, timestamp = 00:00:05                       │
│  t=7     Worker-1 CRASHES (no more heartbeats)                              │
│  t=10    Worker-1's claim is still valid (00:00:05 + 10s = 00:00:15)       │
│  t=15    Worker-1's claim EXPIRES                                           │
│  t=16    Worker-2 checks for unclaimed segments                             │
│          Sees: segment 0, timestamp=00:00:05, that's > 10s ago!            │
│  t=16    Worker-2 claims segment 0:                                         │
│          UPDATE tokens SET owner='w2', timestamp=NOW()                      │
│          WHERE segment=0 AND timestamp < NOW() - 10s                        │
│  t=16    Worker-2 now owns segment 0, resumes from last token position     │
│                                                                              │
│  Timeline:                                                                   │
│                                                                              │
│  Worker-1: ──●───────●───────✗ (crash)                                      │
│              claim   extend                                                  │
│                              │                                               │
│                              │  ← 10 second timeout window                  │
│                              │                                               │
│  Worker-2:                   └─────────────────●──────────►                 │
│                                              steal                           │
│                                              claim                           │
│                                                                              │
│  Total failover time: ~10-15 seconds (claim timeout + detection interval)  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## The Greedy Worker Problem

The fundamental challenge with claims-based coordination is **distribution**. Without a coordinator, workers can claim more than their fair share.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              THE GREEDY WORKER PROBLEM                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Scenario: 4 segments, Worker-1 starts first                                │
│                                                                              │
│  t=0: Worker-1 starts                                                        │
│       Claims segment 0 ✓                                                    │
│       Claims segment 1 ✓                                                    │
│       Claims segment 2 ✓                                                    │
│       Claims segment 3 ✓                                                    │
│       Worker-1 now owns ALL segments                                        │
│                                                                              │
│  t=5: Worker-2 starts                                                        │
│       Queries for unclaimed segments...                                      │
│       Finds: NONE (all claimed by Worker-1)                                 │
│       Worker-2 sits idle!                                                   │
│                                                                              │
│  t=10: Worker-3 starts                                                       │
│        Also sits idle!                                                       │
│                                                                              │
│  Result: 1 worker doing all the work, 2 workers doing nothing               │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  Worker-1: [seg-0] [seg-1] [seg-2] [seg-3]  ← Overloaded            │    │
│  │  Worker-2: (nothing)                         ← Idle                  │    │
│  │  Worker-3: (nothing)                         ← Idle                  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  This is BAD. No automatic rebalancing!                                     │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Distribution Solutions

### Solution 1: Max Claims Per Worker

The simplest approach: limit how many segments each worker can claim.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              MAX CLAIMS LIMIT                                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Configuration: maxSegmentsPerWorker = 2                                    │
│                                                                              │
│  t=0: Worker-1 starts                                                        │
│       Claims segment 0 ✓                                                    │
│       Claims segment 1 ✓                                                    │
│       Tries segment 2... STOP (already at max=2)                           │
│                                                                              │
│  t=5: Worker-2 starts                                                        │
│       Claims segment 2 ✓                                                    │
│       Claims segment 3 ✓                                                    │
│       Done (at max=2)                                                       │
│                                                                              │
│  Result:                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  Worker-1: [seg-0] [seg-1]                                          │    │
│  │  Worker-2: [seg-2] [seg-3]                                          │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Problems:**
- What if 3 workers, 4 segments?
  - With max=2: Worker-3 gets nothing!
  - With max=1: 4 segments / 3 workers = 1 segment idle!
- Too static - doesn't adapt to cluster size.

---

### Solution 2: Worker Registry + Dynamic Quota

Track active workers and calculate fair share dynamically.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              WORKER REGISTRY + DYNAMIC QUOTA                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Additional table: worker_registry                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  worker_id │ last_seen           │ segments_owned                   │    │
│  │  ──────────┼─────────────────────┼────────────────────────────────  │    │
│  │  worker-1  │ 2024-01-15 10:00:05 │ 2                                │    │
│  │  worker-2  │ 2024-01-15 10:00:03 │ 2                                │    │
│  │  worker-3  │ 2024-01-15 10:00:04 │ 0                                │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Algorithm:                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  1. Register self in worker_registry                                │    │
│  │  2. Count active workers: N = 3                                     │    │
│  │  3. Count total segments: S = 4                                     │    │
│  │  4. Calculate fair share: ceil(S / N) = ceil(4/3) = 2              │    │
│  │  5. Only claim up to fair share                                     │    │
│  │  6. If I have MORE than fair share, release excess                 │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Example with 3 workers, 4 segments:                                        │
│  • Fair share = ceil(4/3) = 2 segments each                                │
│  • Worker-1: claims 2, stops                                                │
│  • Worker-2: claims 2, stops (4 total claimed)                             │
│  • Worker-3: 0 available... but wait!                                      │
│                                                                              │
│  Still doesn't rebalance. Need cooperative release.                         │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

### Solution 3: Cooperative Release

Workers periodically check if they should release segments to help others.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              COOPERATIVE RELEASE                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Workers periodically check if they should RELEASE segments                 │
│                                                                              │
│  Rebalancing check (every 30 seconds):                                      │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  async Task MaybeRebalance() {                                       │    │
│  │      var activeWorkers = CountActiveWorkers();  // 3                │    │
│  │      var totalSegments = CountTotalSegments();  // 4                │    │
│  │      var mySegments = CountMySegments();        // 2                │    │
│  │                                                                      │    │
│  │      var fairShare = (int)Math.Ceiling(totalSegments / activeWorkers);│   │
│  │      // fairShare = ceil(4/3) = 2                                   │    │
│  │                                                                      │    │
│  │      // Am I holding more than my fair share?                       │    │
│  │      if (mySegments > fairShare) {                                  │    │
│  │          // Release excess segments                                  │    │
│  │          var toRelease = mySegments - fairShare;                    │    │
│  │          ReleaseSegments(toRelease);                                │    │
│  │      }                                                               │    │
│  │                                                                      │    │
│  │      // Are there starving workers? (workers with 0 segments)       │    │
│  │      var starvingWorkers = CountWorkersWithZeroSegments();          │    │
│  │      if (starvingWorkers > 0 && mySegments > 1) {                   │    │
│  │          // Be nice, release one for them                           │    │
│  │          ReleaseSegments(1);                                        │    │
│  │      }                                                               │    │
│  │  }                                                                   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Timeline:                                                                   │
│  t=0:  Worker-1 starts, claims [0,1]                                        │
│  t=5:  Worker-2 starts, claims [2,3]                                        │
│  t=10: Worker-3 starts, claims nothing (all taken)                          │
│  t=30: Rebalance check!                                                     │
│        - Worker-1 sees Worker-3 is starving, releases segment 1            │
│        - Worker-3 claims segment 1                                          │
│  t=35: Distribution: W1:[0], W2:[2,3], W3:[1]                              │
│  t=60: Another rebalance check                                              │
│        - Worker-2 has 2, others have 1                                      │
│        - Worker-2 releases segment 3                                        │
│        - Worker-1 claims segment 3                                          │
│  t=65: Final: W1:[0,3], W2:[2], W3:[1]                                     │
│                                                                              │
│  Problem: Slow! Takes multiple rebalance cycles.                            │
│  Problem: Released segment might be claimed by wrong worker.                │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

### Solution 4: Segment Affinity (Hash-Based)

Workers only claim segments that "belong" to them by hash.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              SEGMENT AFFINITY                                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Each worker has a numeric ID (from config or assigned):                    │
│  • Worker-1 → ID 0                                                          │
│  • Worker-2 → ID 1                                                          │
│  • Worker-3 → ID 2                                                          │
│                                                                              │
│  Claiming rule:                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  canClaim(segmentId) = (segmentId % totalWorkers) == myWorkerId     │    │
│  │                                                                      │    │
│  │  With 3 workers:                                                     │    │
│  │  • Segment 0: 0 % 3 = 0 → Worker-0's                                │    │
│  │  • Segment 1: 1 % 3 = 1 → Worker-1's                                │    │
│  │  • Segment 2: 2 % 3 = 2 → Worker-2's                                │    │
│  │  • Segment 3: 3 % 3 = 0 → Worker-0's                                │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Result (deterministic!):                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  Worker-0: [seg-0] [seg-3]                                          │    │
│  │  Worker-1: [seg-1]                                                   │    │
│  │  Worker-2: [seg-2]                                                   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Problems:**
- Need to know total worker count in advance!
- If totalWorkers changes, ALL assignments change!
- Worker IDs must be pre-assigned (0, 1, 2, ...)
- This is basically doing Gateway's job in config instead of at runtime.

---

### Solution 5: Consistent Hashing

Use consistent hashing ring - adding/removing workers only affects nearby segments.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              CONSISTENT HASHING                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Hash ring:                                                                  │
│                                                                              │
│               seg-0                                                          │
│                 │                                                            │
│        ┌────────┴────────┐                                                  │
│        │                 │                                                   │
│    Worker-1          seg-3                                                   │
│        │                 │                                                   │
│        │     ┌───────────┤                                                  │
│        │     │           │                                                   │
│    seg-1  Worker-3   Worker-2                                               │
│        │     │           │                                                   │
│        └─────┴───────────┤                                                  │
│                          │                                                   │
│                       seg-2                                                  │
│                                                                              │
│  Each segment is owned by the next worker clockwise on the ring.            │
│                                                                              │
│  Adding Worker-4:                                                            │
│  • Only segments between Worker-3 and Worker-4 move                         │
│  • Other segments stay put                                                  │
│  • Minimal disruption on cluster changes                                    │
│                                                                              │
│  Still need workers to know about each other to build the ring!             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Axon Framework's Approach

Axon uses a combination of techniques:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              AXON'S APPROACH                                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. Worker registers in token table (implicit registry)                     │
│                                                                              │
│  2. On startup, worker tries to claim ONE unclaimed segment                 │
│     (not greedy - just one at a time)                                       │
│                                                                              │
│  3. After processing for a while, check if more segments available          │
│     If yes AND I have capacity, claim another                               │
│                                                                              │
│  4. Periodic "work stealing" check:                                         │
│     - If segment has no recent progress (token not advancing)               │
│     - AND claim is old but being extended (worker alive but stuck)          │
│     - Consider it "slow" and eligible for stealing                          │
│                                                                              │
│  5. With Axon Server (centralized coordinator):                             │
│     Server does all the assignment - much simpler!                          │
│                                                                              │
│  Key insight: Axon's claims-based mode is for SIMPLE cases.                │
│  For proper distribution, they recommend Axon Server (coordinator).         │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### The Thundering Herd Problem

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              THUNDERING HERD                                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Scenario: 10 workers start simultaneously, 4 segments available            │
│                                                                              │
│  t=0: All 10 workers query for unclaimed segments                          │
│       All see: [0, 1, 2, 3] available                                       │
│       All try to claim segment 0 SIMULTANEOUSLY                             │
│                                                                              │
│  Database sees 10 concurrent UPDATE statements:                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  W1: UPDATE tokens SET owner='w1' WHERE segment=0 AND owner IS NULL │    │
│  │  W2: UPDATE tokens SET owner='w2' WHERE segment=0 AND owner IS NULL │    │
│  │  W3: UPDATE tokens SET owner='w3' WHERE segment=0 AND owner IS NULL │    │
│  │  ...                                                                 │    │
│  │  W10: UPDATE tokens SET owner='w10' WHERE segment=0 AND owner IS NULL│   │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Result: Only ONE succeeds (database serializes), 9 fail and retry          │
│                                                                              │
│  Mitigations:                                                                │
│  • Randomized backoff before claiming                                       │
│  • Claim random segment first (not always segment 0)                        │
│  • Stagger worker startup                                                   │
│  • Accept some wasted DB queries at startup                                 │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Gateway vs Claims-Based Comparison

| Aspect | Gateway (Centralized) | Claims-Based (Decentralized) |
|--------|----------------------|------------------------------|
| **Architecture** | Centralized coordinator | Peer-to-peer |
| **Single point of failure** | Gateway is SPOF (needs HA) | No SPOF (database is shared) |
| **Failover speed** | Instant (Gateway detects) | Slow (wait for claim timeout) |
| **Initial distribution** | Gateway assigns evenly | Race condition, needs mitigation |
| **On worker join** | Gateway reassigns instantly | Slow (cooperative release) |
| **On worker leave** | Gateway reassigns instantly | Wait for claim timeout |
| **Even distribution** | Guaranteed | Best effort |
| **Rebalancing speed** | Instant | Minutes (multiple cycles) |
| **Coordination overhead** | Persistent connections | Periodic DB queries |
| **Complexity** | In coordinator | In every worker |
| **Thundering herd** | No (coordinator decides) | Yes (mitigated with backoff) |
| **Scaling** | Gateway can bottleneck | Scales naturally |
| **Consistency** | Strong (Gateway decides) | Eventual (race conditions) |
| **Deployment** | Need Gateway service | Just workers + database |

---

## Why Gateway is Better for Distribution

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              GATEWAY DISTRIBUTION                                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  t=0: Worker-1 connects to Gateway                                          │
│       Gateway: "You get segments [0, 1, 2, 3]" (only worker)               │
│                                                                              │
│  t=5: Worker-2 connects to Gateway                                          │
│       Gateway: "Rebalancing..."                                             │
│       Gateway → Worker-1: "Release segments [2, 3]"                        │
│       Gateway → Worker-2: "You get segments [2, 3]"                        │
│       INSTANT rebalancing!                                                  │
│                                                                              │
│  t=10: Worker-3 connects to Gateway                                         │
│        Gateway: "Rebalancing..."                                            │
│        Gateway → Worker-1: "Release segment [1]"                           │
│        Gateway → Worker-2: "Release segment [3]"                           │
│        Gateway → Worker-3: "You get segments [1, 3]"                       │
│        Final: W1:[0], W2:[2], W3:[1,3]                                     │
│                                                                              │
│  t=15: Worker-2 crashes                                                      │
│        Gateway detects immediately (connection closed)                      │
│        Gateway → Worker-1: "You get segment [2]"                           │
│        Gateway → Worker-3: "No change"                                     │
│        INSTANT failover!                                                    │
│                                                                              │
│  Key advantages:                                                             │
│  • Gateway has GLOBAL VIEW of all workers                                   │
│  • Can make optimal decisions                                               │
│  • No race conditions                                                       │
│  • Instant rebalancing                                                      │
│  • Instant failover                                                         │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## When Claims-Based Makes Sense

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              WHEN TO USE CLAIMS-BASED                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  GOOD FIT:                                                                   │
│  ✓ Simple deployments (don't want separate coordinator service)            │
│  ✓ Low throughput (failover latency acceptable)                             │
│  ✓ Already have SQL database for projections                                │
│  ✓ Workers are stateless (just need position)                               │
│  ✓ Eventual consistency acceptable                                          │
│  ✓ Development/testing environments                                         │
│  ✓ Single-worker processors                                                 │
│                                                                              │
│  POOR FIT:                                                                   │
│  ✗ High throughput (10-15 second failover too slow)                        │
│  ✗ Complex coordination (barriers, 2PC checkpoints)                         │
│  ✗ Need instant rebalancing                                                 │
│  ✗ Streaming with exactly-once (checkpoint coordination)                   │
│  ✗ Large clusters (thundering herd, coordination overhead)                 │
│                                                                              │
│  KURRENTDB CONTEXT:                                                          │
│  Our barrier-based checkpointing needs coordinated snapshots.               │
│  Claims-based doesn't naturally support "all workers checkpoint now".       │
│  Gateway model better fits our 2PC checkpoint protocol.                     │
│                                                                              │
│  However, claims-based could be a "lite" mode for:                          │
│  • Simple projections without exactly-once requirements                     │
│  • Single-worker processors                                                  │
│  • When users don't want to run Gateway                                     │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Hybrid Possibility

Some systems support both modes:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              HYBRID: GATEWAY + CLAIMS FALLBACK                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Mode 1: Gateway-coordinated (default, full features)                       │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  • Barrier-based checkpoints                                         │    │
│  │  • 2PC with sinks                                                    │    │
│  │  • Instant rebalancing                                               │    │
│  │  • Requires Gateway service                                          │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Mode 2: Claims-based (standalone, reduced features)                        │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  • Position-only checkpoints                                         │    │
│  │  • No 2PC (idempotent projections only)                             │    │
│  │  • Slower failover                                                   │    │
│  │  • No Gateway needed                                                 │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  User chooses based on requirements:                                        │
│                                                                              │
│  // Full features with Gateway                                              │
│  var processor = new ProcessorBuilder()                                     │
│      .ConnectToGateway("kurrentdb://localhost:2113")                       │
│      .Build();                                                              │
│                                                                              │
│  // Standalone with claims (simpler, fewer features)                        │
│  var processor = new ProcessorBuilder()                                     │
│      .UseClaimsCoordination(postgresConnectionString)                      │
│      .Build();                                                              │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Summary

Claims-based coordination struggles with distribution because:

1. **No global view** - Each worker only sees database state, not real-time cluster state
2. **Race conditions** - Multiple workers compete for same segments
3. **Slow rebalancing** - Must wait for cooperative release or claim timeout
4. **Best effort** - Can't guarantee even distribution without coordinator

This is why Axon recommends Axon Server for production, and uses claims-based only for simple/development scenarios.

**For KurrentDB Processors:**
- Gateway model is preferred for barrier-based checkpoint coordination
- Claims-based could be a future "lite" mode for simple projection use cases
- No decision made - documented for reference

---

## References

- [Axon Framework Documentation - Event Processors](https://docs.axoniq.io/reference-guide/axon-framework/events/event-processors)
- [Axon Framework - Token Store](https://docs.axoniq.io/reference-guide/axon-framework/events/event-processors/streaming#token-store)
- [Consistent Hashing - Wikipedia](https://en.wikipedia.org/wiki/Consistent_hashing)
