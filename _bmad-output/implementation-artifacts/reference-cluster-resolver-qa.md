# Reference: ClusterResolver Q&A Session

**Created:** 2026-01-09
**Purpose:** Detailed Q&A explaining ClusterResolver behavior, seed handling, subchannel management, and topology discovery patterns.
**Related:** `tech-spec-kurrent-grpc-loadbalancing.md`

---

## Table of Contents

- [Q1: How Does the ClusterResolver Work?](#q1-how-does-the-clusterresolver-work)
- [Q2: How Does It Handle Unavailable Endpoints?](#q2-how-does-it-handle-unavailable-endpoints)
- [Q3: Seed Selection and Topology Source Activation](#q3-seed-selection-and-topology-source-activation)
- [Q4: Subchannel Caching and Reuse](#q4-subchannel-caching-and-reuse)
- [Q5: Seed Lifecycle After Initial Discovery](#q5-seed-lifecycle-after-initial-discovery)

---

## Q1: How Does the ClusterResolver Work?

### What is a Resolver?

Think of a resolver like a **phone book for your cluster**. When your app wants to talk to the database, it doesn't know which server to call - the resolver's job is to figure that out and keep the list updated.

### The Big Picture

```
┌─────────────────────────────────────────────────────────────────┐
│                        ClusterResolver                          │
│                                                                 │
│  1. "Hey TopologySource, go find out who's in the cluster"      │
│                          │                                      │
│                          ▼                                      │
│  2. TopologySource discovers nodes and writes to a Channel      │
│                          │                                      │
│                          ▼                                      │
│  3. Resolver reads from Channel, tells gRPC "here's the list"   │
│                          │                                      │
│                          ▼                                      │
│  4. gRPC creates connections (Subchannels) to each node         │
└─────────────────────────────────────────────────────────────────┘
```

### How It Works Step-by-Step

#### Step 1: Startup

When the resolver starts, it creates a **Channel** (think of it as a mailbox):

```csharp
// The resolver owns this "mailbox" for topology updates
_topologyChannel = Channel.CreateUnbounded<ClusterTopology>();
```

The `TopologySource` will drop messages into this mailbox whenever it discovers new cluster info.

#### Step 2: Discovery Loop

The resolver tells the `TopologySource` to start discovering:

```csharp
// "Go find the cluster nodes and write what you find to my mailbox"
_ = Task.Run(() => RunSourceWithRetryAsync(context));
```

Meanwhile, the resolver sits and waits for messages:

```csharp
// "I'll keep reading from my mailbox forever"
await foreach (var topology in _topologyChannel.Reader.ReadAllAsync(ct)) {
    // Got new topology! Tell gRPC about it
    var addresses = topology.Nodes
        .Where(n => n.IsEligible)  // Only healthy nodes
        .Select(n => new BalancerAddress(n.EndPoint.Host, n.EndPoint.Port, ...))
        .ToArray();

    listener(ResolverResult.ForResult(addresses));  // "Hey gRPC, here's the updated list!"
}
```

---

## Q2: How Does It Handle Unavailable Endpoints?

There are **four layers** of protection for handling unavailable endpoints:

### Layer 1: Discovery Retry Loop

When the `TopologySource` fails to reach a seed node, the resolver retries with **exponential backoff**:

```
Attempt 1: Try to discover → FAILED (node1 is down)
           Wait 100ms

Attempt 2: Try to discover → FAILED (node2 is down)
           Wait 200ms (doubled)

Attempt 3: Try to discover → SUCCESS! (node3 responded)
           Got cluster info: [node1: down, node2: down, node3: leader]
```

The code looks like this:

```csharp
async Task RunSourceWithRetryAsync(TopologyContext context) {
    var attempt = 0;
    var backoff = _resilience.InitialBackoff;  // Starts at 100ms

    while (!ct.IsCancellationRequested) {
        try {
            attempt++;
            await _source.RunAsync(_topologyChannel.Writer, context);
            break;  // Success!
        }
        catch (Exception ex) {
            if (attempt >= _resilience.MaxDiscoveryAttempts) {
                // Give up after 10 attempts
                throw new DiscoveryException(attempt, ex);
            }

            await Task.Delay(backoff, ct);
            backoff = backoff * 2;  // Double the wait time (max 5s)
        }
    }
}
```

### Layer 2: Filtering Unhealthy Nodes

Even when discovery succeeds, some nodes might be unhealthy. The resolver **filters them out**:

```csharp
var addresses = topology.Nodes
    .Where(n => n.IsEligible)  // ← Only Ready or Isolated nodes pass through
    .Select(...)
    .ToArray();
```

So if you have 3 nodes but one is `Stopping`, only 2 addresses go to gRPC.

### Layer 3: Reactive Refresh (The RefreshTriggerHandler)

What if a node goes down **after** discovery? That's where the `RefreshTriggerHandler` comes in:

```
Your App                    RefreshTriggerHandler              ClusterResolver
    │                              │                                │
    │──── gRPC call ──────────────►│                                │
    │                              │──── forward to node ──────────►│
    │                              │◄─── UNAVAILABLE error ─────────│
    │                              │                                │
    │                              │ "Hmm, StatusCode.Unavailable"  │
    │                              │ "That matches my refresh rule" │
    │                              │                                │
    │                              │──── RequestRefresh() ─────────►│
    │                              │                                │
    │◄─── error (you retry) ───────│         "Got it, re-discovering│
    │                              │          cluster now..."       │
```

The handler watches for specific errors:

```csharp
catch (RpcException ex) when (_shouldRefresh(ex)) {
    // "This error means the cluster topology might have changed!"
    _trigger.RequestRefresh();
    throw;  // Still throw - let the caller retry
}
```

By default, it triggers on `StatusCode.Unavailable` (code 14).

### Layer 4: Debouncing

If 100 requests all fail at once, we don't want 100 refresh attempts. The `RefreshTrigger` **debounces**:

```csharp
public void RequestRefresh() {
    lock (_lock) {
        var now = DateTime.UtcNow;
        if (now - _lastRefresh < _debounceInterval)  // 100ms window
            return;  // "Already refreshing, ignore this one"

        _lastRefresh = now;
    }

    _onRefresh();  // Actually trigger refresh
}
```

### Visual Timeline: Node Goes Down

```
Time    Event                                    What Happens
─────   ─────────────────────────────────────    ─────────────────────────────
T+0     Cluster healthy: [A✓, B✓, C✓]           All 3 nodes available

T+10s   Node B crashes                           (Nothing yet - we don't know)

T+12s   Your app calls ReadStream()             Picker picks node B
        Request fails: UNAVAILABLE

T+12s   RefreshTriggerHandler sees error        Calls _trigger.RequestRefresh()

T+12s   ClusterResolver.Refresh() called        Re-runs topology discovery

T+13s   Discovery completes                     New topology: [A✓, B✗, C✓]
        Resolver filters to eligible            Reports to gRPC: [A, C]

T+13s   gRPC updates Subchannels                Removes B, keeps A and C

T+13s   Your app retries ReadStream()           Picker picks A or C → Success!
```

### Key Takeaways

1. **The resolver owns the channel** - TopologySource just writes to it
2. **Discovery retries with backoff** - Doesn't give up immediately
3. **Only eligible nodes are reported** - Unhealthy nodes filtered out
4. **Errors trigger refresh** - `RefreshTriggerHandler` watches for failures
5. **Debouncing prevents storms** - Multiple failures don't cause multiple refreshes

The beauty is your app code doesn't need to know any of this - it just makes gRPC calls and the infrastructure handles the rest!

---

## Q3: Seed Selection and Topology Source Activation

### The Seed Selection Process

You have multiple seeds for redundancy. The resolver should try them **in order** (or shuffled) until one responds:

```
Seeds: [node1:2113, node2:2113, node3:2113]

┌─────────────────────────────────────────────────────────────────┐
│  Try node1 ──► TIMEOUT (down)                                   │
│       │                                                         │
│       ▼                                                         │
│  Try node2 ──► TIMEOUT (down)                                   │
│       │                                                         │
│       ▼                                                         │
│  Try node3 ──► SUCCESS! Got response                            │
│       │                                                         │
│       ▼                                                         │
│  Activate TopologySource with node3 as the "working" endpoint   │
│       │                                                         │
│       ▼                                                         │
│  Establish live connection for topology updates                 │
└─────────────────────────────────────────────────────────────────┘
```

### Two-Phase Process

**Phase 1: Find a Working Seed**

```csharp
// Pseudocode for what SHOULD happen in the resolver
async Task<DnsEndPoint> FindWorkingSeedAsync(CancellationToken ct) {
    // Shuffle seeds for load distribution
    var shuffledSeeds = _seeds.OrderBy(_ => Random.Shared.Next()).ToArray();

    foreach (var seed in shuffledSeeds) {
        try {
            // Quick health check - can we reach this seed?
            await ProbeSeedAsync(seed, ct);
            return seed;  // Found one!
        }
        catch (Exception ex) {
            _logger?.LogDebug(ex, "Seed {Seed} unavailable, trying next", seed);
        }
    }

    throw new DiscoveryException("All seeds unavailable");
}
```

**Phase 2: Activate Source with Working Seed**

Once we find a working seed, we activate the `TopologySource` which establishes the **live connection**:

```
                    ┌──────────────────────────────────────┐
                    │  TopologySource (Streaming)          │
                    │                                      │
  Working Seed ────►│  Establishes gRPC streaming call     │
  (node3:2113)      │  to cluster gossip/watch endpoint    │
                    │                                      │
                    │  Server pushes updates:              │
                    │  ┌────────────────────────────┐      │
                    │  │ "node1 is back!"           │──────┼────► Channel
                    │  │ "node2 became leader!"     │──────┼────► Channel
                    │  │ "node4 joined cluster!"    │──────┼────► Channel
                    │  └────────────────────────────┘      │
                    └──────────────────────────────────────┘
```

### Streaming vs Polling

The `TopologySource` abstraction supports both patterns:

#### Polling (PollingTopologySource)

```
Every 30s: "Hey server, what's the cluster look like now?"
           ───────────────────────────────────────────────►
           ◄─── Here's the current state: [A, B, C]

           (wait 30s)

           "Hey server, what's the cluster look like now?"
           ───────────────────────────────────────────────►
           ◄─── Here's the current state: [A, B, C, D]  ← D joined!
```

#### Streaming (TopologySource base class)

```
"I want to subscribe to topology changes"
───────────────────────────────────────────────────────────►

◄─── Initial state: [A, B, C]

     (connection stays open, server pushes when things change)

◄─── Update: node D joined
◄─── Update: node B became leader
◄─── Update: node A is stopping

     (continuous stream until connection breaks)
```

### The Full Corrected Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        ClusterResolver                                  │
│                                                                         │
│  1. SEED DISCOVERY LOOP                                                 │
│     ┌─────────────────────────────────────────────────────────────┐    │
│     │ for attempt = 1 to MaxAttempts:                             │    │
│     │   for each seed in shuffle(seeds):                          │    │
│     │     try:                                                    │    │
│     │       probe seed → SUCCESS → use this seed                  │    │
│     │     catch:                                                  │    │
│     │       continue to next seed                                 │    │
│     │   all seeds failed → wait(backoff) → retry                  │    │
│     └─────────────────────────────────────────────────────────────┘    │
│                              │                                          │
│                              ▼                                          │
│  2. ACTIVATE TOPOLOGY SOURCE with working seed                          │
│     ┌─────────────────────────────────────────────────────────────┐    │
│     │ TopologySource.RunAsync(channelWriter, context)             │    │
│     │                                                             │    │
│     │ For STREAMING: Opens persistent connection, receives pushes │    │
│     │ For POLLING:   Loops every N seconds querying for state     │    │
│     └─────────────────────────────────────────────────────────────┘    │
│                              │                                          │
│                              ▼                                          │
│  3. CONSUME TOPOLOGY UPDATES (runs forever)                             │
│     ┌─────────────────────────────────────────────────────────────┐    │
│     │ await foreach (var topology in channel.ReadAllAsync()):     │    │
│     │   filter eligible nodes                                     │    │
│     │   report to gRPC LoadBalancer                               │    │
│     └─────────────────────────────────────────────────────────────┘    │
│                                                                         │
│  4. IF CONNECTION BREAKS                                                │
│     ┌─────────────────────────────────────────────────────────────┐    │
│     │ Go back to step 1 (seed discovery)                          │    │
│     │ The working seed might be down now - find a new one         │    │
│     └─────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

### What Happens When the Live Connection Breaks?

```
Time    Event
─────   ──────────────────────────────────────────────────────────────
T+0     Connected to node3, streaming topology updates

T+60s   node3 crashes! Stream breaks with UNAVAILABLE

T+60s   TopologySource.RunAsync() throws exception

T+60s   Resolver catches it, goes back to seed discovery:
        - Try node1 → SUCCESS (it's back!)
        - Activate source with node1
        - New live connection established

T+61s   Streaming continues from node1
```

### The Key Insight

The `TopologySource` doesn't manage the "which seed" logic - that's the **resolver's job**:

| Component | Responsibility |
|-----------|----------------|
| **ClusterResolver** | Find working seed, handle reconnection, retry logic |
| **TopologySource** | Given a working endpoint, discover/stream topology |
| **Channel<ClusterTopology>** | Mailbox between source and resolver |

---

## Q4: Subchannel Caching and Reuse

### Seeds vs Topology Nodes - Different Things!

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│  SEEDS (for discovery)              TOPOLOGY NODES (actual cluster)     │
│  ────────────────────               ───────────────────────────────     │
│                                                                         │
│  Used ONLY to find the cluster      These are what we connect to        │
│  No subchannels created             Subchannels created for these       │
│                                                                         │
│  [seed1, seed2, seed3]              [nodeA, nodeB, nodeC, nodeD]        │
│        │                                      │                         │
│        │                                      │                         │
│        ▼                                      ▼                         │
│   "Hey seed1, who's in              "Create subchannels to              │
│    the cluster?"                     nodeA, nodeB, nodeC, nodeD"        │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

**Seeds** = Phone numbers to call and ask "who's in the cluster?"
**Topology nodes** = The actual servers we create connections to

### Who Creates Subchannels?

The **LoadBalancer** creates subchannels, NOT the resolver:

```
ClusterResolver                     ClusterLoadBalancer
      │                                    │
      │ "Here are the addresses:           │
      │  [nodeA:2113, nodeB:2113,          │
      │   nodeC:2113, nodeD:2113]"         │
      │────────────────────────────────────►│
      │                                    │
      │                                    │ For each address:
      │                                    │   - Is there an existing subchannel?
      │                                    │   - If no: Create one
      │                                    │   - If yes: Reuse it
      │                                    │
      │                                    ▼
      │                            ┌─────────────────┐
      │                            │ Subchannels:    │
      │                            │  → nodeA:2113   │
      │                            │  → nodeB:2113   │
      │                            │  → nodeC:2113   │
      │                            │  → nodeD:2113   │
      │                            └─────────────────┘
```

### Subchannel Lifecycle - gRPC Handles This!

gRPC's `SubchannelsLoadBalancer` (our base class) is smart about reuse:

```csharp
// Inside gRPC's SubchannelsLoadBalancer (simplified)
protected override void UpdateAddresses(IReadOnlyList<BalancerAddress> addresses) {
    var newAddresses = addresses.ToHashSet();
    var existingAddresses = _subchannels.Keys.ToHashSet();

    // Remove subchannels for nodes that left
    foreach (var removed in existingAddresses.Except(newAddresses)) {
        _subchannels[removed].Dispose();  // Close connection
        _subchannels.Remove(removed);
    }

    // Add subchannels for new nodes
    foreach (var added in newAddresses.Except(existingAddresses)) {
        var subchannel = Controller.CreateSubchannel(new SubchannelOptions {
            Addresses = { added }
        });
        subchannel.RequestConnection();  // Start connecting
        _subchannels[added] = subchannel;
    }

    // Existing nodes? Their subchannels are REUSED - no change needed!
}
```

### Visual: Topology Change with Reuse

```
TIME T+0: Initial topology discovered
──────────────────────────────────────────────────────────────────────────

Topology: [nodeA ✓, nodeB ✓, nodeC ✓]

LoadBalancer creates:
  Subchannel-1 ──────► nodeA:2113  [CONNECTING → READY]
  Subchannel-2 ──────► nodeB:2113  [CONNECTING → READY]
  Subchannel-3 ──────► nodeC:2113  [CONNECTING → READY]


TIME T+30s: Topology update - nodeD joined, nodeB left
──────────────────────────────────────────────────────────────────────────

Topology: [nodeA ✓, nodeC ✓, nodeD ✓]  (nodeB gone, nodeD new)

LoadBalancer:
  Subchannel-1 ──────► nodeA:2113  [READY]      ← REUSED (no change)
  Subchannel-2 ──────► nodeB:2113  [DISPOSED]   ← REMOVED (node left)
  Subchannel-3 ──────► nodeC:2113  [READY]      ← REUSED (no change)
  Subchannel-4 ──────► nodeD:2113  [CONNECTING] ← NEW (node joined)


TIME T+60s: nodeA crashes, detected via error
──────────────────────────────────────────────────────────────────────────

Topology: [nodeC ✓, nodeD ✓]  (nodeA removed)

LoadBalancer:
  Subchannel-1 ──────► nodeA:2113  [DISPOSED]   ← REMOVED
  Subchannel-3 ──────► nodeC:2113  [READY]      ← REUSED
  Subchannel-4 ──────► nodeD:2113  [READY]      ← REUSED
```

### What's Inside a Subchannel?

A subchannel manages the actual HTTP/2 connection:

```
┌─────────────────────────────────────────────────────────────────┐
│  Subchannel (to nodeA:2113)                                     │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  HTTP/2 Connection                                       │   │
│  │  ┌─────────────────────────────────────────────────┐    │   │
│  │  │ Stream 1: gRPC call (ReadStream)                │    │   │
│  │  │ Stream 3: gRPC call (AppendToStream)            │    │   │
│  │  │ Stream 5: gRPC call (ReadStream)                │    │   │
│  │  │ ...multiplexed streams...                       │    │   │
│  │  └─────────────────────────────────────────────────┘    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  State: READY | CONNECTING | TRANSIENT_FAILURE | IDLE          │
│  Address: nodeA:2113                                            │
│  Attributes: { NodeId: abc-123, Role: Leader, Status: Ready }   │
└─────────────────────────────────────────────────────────────────┘
```

### Summary Table

| Question | Answer |
|----------|--------|
| **Subchannels for seeds?** | NO - seeds are only for discovery queries |
| **Subchannels for topology nodes?** | YES - one per eligible node |
| **Who creates them?** | LoadBalancer (via `Controller.CreateSubchannel()`) |
| **Are they reused?** | YES - if node stays in topology, subchannel is kept |
| **When disposed?** | When node leaves topology or connection fails permanently |
| **Connection pooling?** | Each subchannel = one HTTP/2 connection with multiplexed streams |

### The Big Picture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│   Seeds                    Resolver                    LoadBalancer     │
│   ──────                   ────────                    ────────────     │
│                                                                         │
│   [seed1]                     │                             │           │
│   [seed2]  ──► "Find cluster" │                             │           │
│   [seed3]                     │                             │           │
│                               │                             │           │
│                               ▼                             │           │
│                        TopologySource                       │           │
│                        (gossip/watch)                       │           │
│                               │                             │           │
│                               │ Topology: [A, B, C, D]      │           │
│                               │                             │           │
│                               ▼                             │           │
│                     ResolverResult.ForResult(               │           │
│                       [A:2113, B:2113, C:2113, D:2113]      │           │
│                     )         │                             │           │
│                               │                             │           │
│                               └────────────────────────────►│           │
│                                                             │           │
│                                                             ▼           │
│                                                    ┌────────────────┐   │
│                                                    │ Subchannels:   │   │
│                                                    │  SC-1 → A:2113 │   │
│   NO subchannels for seeds!                        │  SC-2 → B:2113 │   │
│   They're just used to ASK                         │  SC-3 → C:2113 │   │
│   who's in the cluster.                            │  SC-4 → D:2113 │   │
│                                                    └────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Q5: Seed Lifecycle After Initial Discovery

### The Lifecycle of Seeds vs Discovered Nodes

**Question:** Once one of the seeds returns a topology, do we never use the seeds anymore?

**Answer:** Mostly yes, but seeds are your emergency fallback.

```
PHASE 1: Bootstrap (seeds are essential)
────────────────────────────────────────────────────────────────────────

  Seeds: [seed1, seed2, seed3]
            │
            ▼
  "seed2, who's in the cluster?"
            │
            ▼
  seed2 responds: "Topology: [nodeA, nodeB, nodeC, nodeD, nodeE]"
            │
            ▼
  SUCCESS! Now we know the actual cluster members.


PHASE 2: Ongoing Discovery (seeds ignored, use discovered nodes)
────────────────────────────────────────────────────────────────────────

  From now on, topology queries go to DISCOVERED nodes:

  [nodeA, nodeB, nodeC, nodeD, nodeE]  ← These are our "working set"
            │
            ▼
  Next gossip query: "nodeC, what's the current topology?"
            │
            ▼
  nodeC responds: "Topology: [nodeA, nodeB, nodeC, nodeD, nodeE, nodeF]"
                                                              ↑
                                                        nodeF joined!

  Seeds? Sitting in a drawer. Not needed right now.


PHASE 3: Disaster - All Discovered Nodes Fail (seeds to the rescue!)
────────────────────────────────────────────────────────────────────────

  [nodeA ✗, nodeB ✗, nodeC ✗, nodeD ✗, nodeE ✗]  ← All unreachable!
            │
            ▼
  "Uh oh, none of the discovered nodes respond..."
  "Let me try the original seeds again!"
            │
            ▼
  Seeds: [seed1, seed2, seed3]
            │
            ▼
  seed3 responds: "Topology: [nodeX, nodeY, nodeZ]"  ← Completely new cluster!
            │
            ▼
  Back to Phase 2 with new working set: [nodeX, nodeY, nodeZ]
```

### Why This Design?

| Reason | Explanation |
|--------|-------------|
| **Efficiency** | Discovered nodes are the actual cluster - they have fresh topology info |
| **Load distribution** | We can randomize which discovered node we query |
| **Resilience** | Seeds are static config - cluster might have changed entirely |
| **Fallback safety** | If discovered nodes all fail, seeds are our "known good starting point" |

### The Legacy Behavior (What We're Preserving)

From the legacy `GossipChannelSelector`:

```csharp
// Simplified logic
async Task<DnsEndPoint> DiscoverAsync() {
    for (var attempt = 1; attempt <= MaxDiscoverAttempts; attempt++) {

        // Try each node in our "working set" (discovered nodes, or seeds initially)
        foreach (var channel in _channelCache.GetRandomOrderSnapshot()) {
            try {
                var clusterInfo = await _gossipClient.GetAsync(channel);

                // SUCCESS! Update our working set with discovered members
                _channelCache.UpdateCache(clusterInfo.Members);  // ← Replace with discovered nodes

                return SelectNode(clusterInfo);
            }
            catch {
                // This node failed, try next
            }
        }

        // ALL nodes in working set failed!
        // Fall back to seeds
        _channelCache.UpdateCache(_settings.GossipSeeds);  // ← Back to seeds!

        await Task.Delay(DiscoveryInterval);
    }

    throw new DiscoveryException("Max attempts reached");
}
```

### Visual Timeline

```
Time    Working Set                        What Happens
─────   ──────────────────────────────     ─────────────────────────────────
T+0     [seed1, seed2, seed3]              Initial: only seeds known

T+1s    [seed1, seed2, seed3]              Try seed1 → TIMEOUT

T+2s    [seed1, seed2, seed3]              Try seed2 → SUCCESS!
                                           Got topology: [A, B, C, D]

T+2s    [A, B, C, D]                       Working set REPLACED with discovered
                                           Seeds? Put away, not needed.

T+32s   [A, B, C, D]                       Periodic refresh: query node C
                                           Got topology: [A, B, C, D, E]

T+32s   [A, B, C, D, E]                    Working set updated (E joined)

T+60s   [A, B, C, D, E]                    Refresh: query node A → SUCCESS

T+90s   [A, B, C, D, E]                    Network partition! All nodes down.
                                           Try A → FAIL
                                           Try B → FAIL
                                           Try C → FAIL
                                           Try D → FAIL
                                           Try E → FAIL

T+91s   [seed1, seed2, seed3]              ALL FAILED → Reseed with originals!

T+92s   [seed1, seed2, seed3]              Try seed1 → SUCCESS!
                                           Got topology: [X, Y, Z]

T+92s   [X, Y, Z]                          New working set. Back to normal.
```

### In Our New Design

The **ClusterResolver** manages the working set, not the TopologySource. The source is simple - it just queries whatever endpoint it's given.

```csharp
// ClusterResolver manages the working set
internal sealed class ClusterResolver : Resolver {
    ClusterTopology? _lastTopology;

    IReadOnlyList<DnsEndPoint> GetWorkingEndpoints() {
        // Use discovered nodes if we have them, otherwise seeds
        var endpoints = _lastTopology?.Nodes
            .Where(n => n.IsEligible)
            .Select(n => n.EndPoint)
            .ToArray()
            ?? _seeds;

        // Shuffle for load distribution
        return endpoints.OrderBy(_ => Random.Shared.Next()).ToArray();
    }

    async Task RunSourceWithRetryAsync(TopologyContext context) {
        while (!context.CancellationToken.IsCancellationRequested) {
            var endpoints = GetWorkingEndpoints();

            foreach (var endpoint in endpoints) {
                try {
                    // Create context with current endpoint
                    var endpointContext = context with {
                        Channel = CreateChannelForEndpoint(endpoint)
                    };

                    await _source.RunAsync(_topologyChannel.Writer, endpointContext);
                    return; // Source completed normally
                }
                catch {
                    // Try next endpoint
                }
            }

            // All discovered nodes failed? Fall back to seeds
            if (_lastTopology != null) {
                _lastTopology = null; // Clear working set, will use seeds next iteration
            }

            await Task.Delay(_resilience.InitialBackoff, context.CancellationToken);
        }
    }
}

// TopologySource is simple - just queries what it's given
public abstract class PollingTopologySource : TopologySource {
    protected abstract ValueTask<ClusterTopology> ReadAsync(TopologyContext context);
    // No working set logic here - that's the resolver's job
}
```

### Summary

| Question | Answer |
|----------|--------|
| **After first topology, do we use seeds?** | No, we use discovered nodes |
| **Are seeds completely forgotten?** | No! They're kept as emergency fallback |
| **When do seeds come back?** | When ALL discovered nodes fail |
| **Why not always use seeds?** | Discovered nodes have fresher info, better load distribution |

Seeds are like the **emergency phone number on the wall** - you don't use it daily, but you're glad it's there when everything else fails!

---

## Design Implications

Based on this Q&A session, the following design decisions are confirmed:

### ClusterResolver Responsibilities

1. **Owns the topology channel** - Single point of control for topology updates
2. **Manages seed selection** - Tries seeds in order/shuffled until one works
3. **Handles reconnection** - Goes back to seed discovery when source fails
4. **Filters eligible nodes** - Only reports Ready/Isolated nodes to LoadBalancer

### TopologySource Responsibilities

1. **Discovers topology** - Protocol-specific discovery logic
2. **Writes to channel** - Continuous updates (streaming) or periodic (polling)
3. **Manages working set** - Uses discovered nodes, falls back to seeds
4. **Does NOT manage subchannels** - That's the LoadBalancer's job

### LoadBalancer Responsibilities

1. **Creates subchannels** - One per eligible topology node
2. **Reuses subchannels** - Keeps existing when topology updates
3. **Disposes stale subchannels** - Removes when nodes leave topology
4. **Creates pickers** - New picker on topology change

### Subchannel Management

1. **NO subchannels for seeds** - Seeds are discovery-only
2. **One subchannel per topology node** - Created by LoadBalancer
3. **Automatic reuse** - gRPC's SubchannelsLoadBalancer handles this
4. **HTTP/2 multiplexing** - Each subchannel = one connection with many streams
