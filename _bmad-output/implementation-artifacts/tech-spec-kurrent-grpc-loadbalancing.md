# Tech-Spec: Kurrent.Grpc.LoadBalancing

**Created:** 2026-01-09
**Status:** Ready for Development
**Author:** Sergio + Claude (Party Mode Review: Winston, Amelia, Murat)

---

## Table of Contents

- [Overview](#overview)
  - [Problem Statement](#problem-statement)
  - [Solution](#solution)
  - [Scope](#scope)
- [Context for Development](#context-for-development)
  - [Codebase Patterns](#codebase-patterns)
  - [Files to Reference](#files-to-reference)
  - [Technical Decisions](#technical-decisions)
- [Architecture](#architecture)
  - [Component Diagram](#component-diagram)
  - [Data Flow](#data-flow)
- [Core Model: Two-Axis Node State](#core-model-two-axis-node-state)
  - [Design Philosophy](#design-philosophy)
  - [The Two Axes](#the-two-axes)
  - [NodeRole Enum](#noderole-enum)
  - [NodeStatus Enum](#nodestatus-enum)
  - [ClusterNode Struct](#clusternode-struct)
  - [ClusterTopology Record](#clustertopology-record)
  - [TopologyContext Record](#topologycontext-record)
- [Topology Sources](#topology-sources)
  - [TopologySource Abstract Base](#topologysource-abstract-base)
  - [PollingTopologySource Abstract](#pollingtopologysource-abstract)
- [Configuration](#configuration)
  - [LoadBalancingOptions](#loadbalancingoptions)
  - [ResilienceOptions](#resilienceoptions)
  - [RefreshPolicy](#refreshpolicy)
- [gRPC Infrastructure](#grpc-infrastructure)
  - [RefreshOnErrorInterceptor](#refreshonerrorinterceptor)
  - [ClusterResolver](#clusterresolver)
  - [ClusterLoadBalancer](#clusterloadbalancer)
  - [ClusterPicker](#clusterpicker)
  - [Factories](#factories)
- [Builder Pattern](#builder-pattern)
  - [LoadBalancingBuilder Non-DI](#loadbalancingbuilder-non-di)
  - [GrpcLoadBalancedChannel Entry Point](#grpcloadbalancedchannel-entry-point)
  - [LoadBalancingServiceBuilder DI](#loadbalancingservicebuilder-di)
  - [ServiceCollection Extensions](#servicecollection-extensions)
- [Exceptions](#exceptions)
  - [DiscoveryException](#discoveryexception)
- [Implementation Plan](#implementation-plan)
  - [File Structure](#file-structure)
  - [Tasks](#tasks)
  - [Acceptance Criteria](#acceptance-criteria)
- [Additional Context](#additional-context)
  - [Dependencies](#dependencies)
  - [Testing Strategy](#testing-strategy)
  - [Usage Examples](#usage-examples)
  - [Notes](#notes)

---

## Overview

### Problem Statement

The current KurrentDB .NET client uses a legacy gossip-based cluster discovery and node selection implementation that:

| Problem | Impact |
|---------|--------|
| **Reinvents gRPC load balancing** | ~2000 lines of custom code duplicating framework functionality |
| **Uses single shared channel** | All requests serialize through one HTTP/2 connection |
| **Blocks on first call** | Discovery can take up to 30s in worst case |
| **Reactive-only refresh** | Stale topology until failure occurs |
| **Allocation-heavy hot path** | LINQ allocations on every node selection |
| **All-or-nothing reconnection** | One bad node affects all clients |
| **No gRPC ecosystem integration** | Missing retry policies, health checking |
| **Hard to test** | Tight coupling, no DI |

### Solution

Create a **generic, protocol-agnostic** cluster topology and load balancing framework (`Kurrent.Grpc.LoadBalancing`) that:

1. **Uses native gRPC infrastructure** - `Resolver` / `LoadBalancer` / `SubchannelPicker`
2. **Provides clean abstractions** - `TopologySource` for pluggable discovery
3. **Enables zero-allocation hot path** - Pre-sorted picker with atomic index
4. **Supports DI and non-DI** - Builder pattern for both scenarios
5. **Is protocol-agnostic** - No database-specific concepts in the generic framework

### Scope

**In Scope:**
- Generic load balancing framework (`Kurrent.Grpc.LoadBalancing`)
- Two-Axis node state model (Role + Status)
- `TopologySource` and `PollingTopologySource` abstractions
- `ClusterResolver`, `ClusterLoadBalancer`, `ClusterPicker` implementations
- JSON-serializable configuration options
- Builder pattern for DI and non-DI usage
- `RefreshPolicy` for error-triggered topology refresh

**Out of Scope:**
- KurrentDB-specific implementation (`Kurrent.Client.LoadBalancing`) - **separate spec**
- Gossip protocol implementation
- VNodeState mapping to Role/Status
- Connection string parsing
- OpenTelemetry integration
- gRPC health checking protocol
- Metrics collection

---

## Context for Development

### Codebase Patterns

**Code Style (from project.md):**
- K&R brace style, 4-space indentation
- `ConfigureAwait(false)` everywhere in library code
- XML documentation on all public APIs
- `readonly record struct` for value types
- `sealed` classes by default

**Namespace Convention:**
```
Kurrent.Grpc.LoadBalancing           # Generic framework
Kurrent.Grpc.LoadBalancing.Model     # Core model types
Kurrent.Grpc.LoadBalancing.Sources   # Topology sources
Kurrent.Grpc.LoadBalancing.Config    # Configuration
Kurrent.Grpc.LoadBalancing.Infra     # gRPC integration
```

**Project Structure:**
```
src/Kurrent.Grpc.LoadBalancing/
├── Model/
├── Sources/
├── Configuration/
├── Infrastructure/
├── Builder/
├── Extensions/
└── Exceptions/
```

### Files to Reference

| File | Purpose |
|------|---------|
| `_bmad-output/implementation-artifacts/reference-legacy-gossip-discovery.md` | Complete legacy behavior documentation |
| `_bmad-output/implementation-artifacts/reference-cluster-resolver-qa.md` | Q&A explaining resolver, seeds, subchannels, working set |
| `src/KurrentDB.Client/Legacy/RipAndTear/Client/Gossip/NodeSelector.cs` | Node selection logic to preserve |
| `src/KurrentDB.Client/Legacy/RipAndTear/Client/Gossip/NodePreferenceComparers.cs` | Priority ordering per preference |
| `src/KurrentDB.Client/Legacy/RipAndTear/Client/Gossip/GossipChannelSelector.cs` | Discovery algorithm (retry loop) |
| `proto/kurrentdb/protocol/v1/gossip.proto` | Gossip protocol definition |

### Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Node model** | `ClusterNode` struct (not interface) | Universal abstraction, protocol adapters create instances |
| **State model** | Two-Axis (Role + Status) | Protocol-agnostic, self-documenting eligibility |
| **Picking strategy** | `IClusterNodePickingStrategy` interface | Abstracts gRPC's complex picker API, provides clean context |
| **Topology delivery** | `ChannelWriter<ClusterTopology>` | Decouples producer from consumer, supports streaming |
| **Channel ownership** | Resolver owns `Channel<T>` | Single point of control, source restart doesn't break flow |
| **Refresh trigger** | gRPC `Interceptor` | Catches RpcException at gRPC layer; HTTP handlers operate too early |
| **URI scheme** | `cluster:///` | Generic framework, protocol-agnostic |
| **Options design** | JSON-serializable POCOs | Supports `IConfiguration` binding from appsettings.json |
| **Builder design** | Fluent API with `Build()` | Handles non-serializable config (delegates, instances) |
| **Default RefreshPolicy** | From `ResilienceOptions.RefreshOnStatusCodes` | Config-friendly int[] defaults to `[14]` (Unavailable) |
| **Picker abstraction** | `PickNodeResult` wraps gRPC `PickResult` | Hides gRPC naming weirdness, provides domain-specific failures |

---

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Kurrent.Grpc.LoadBalancing (Generic Framework)                             │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Core Model                                                          │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────────┐  │   │
│  │  │ NodeRole    │  │ NodeStatus   │  │ ClusterNode                │  │   │
│  │  │ • Leader    │  │ • Starting   │  │ • NodeId, EndPoint         │  │   │
│  │  │ • Follower  │──│ • Syncing    │──│ • Role, Status             │  │   │
│  │  │ • ReadOnly  │  │ • Ready      │  │ • CanServeReads            │  │   │
│  │  │ • Manager   │  │ • Isolated   │  │ • CanAcceptWrites          │  │   │
│  │  │ • Resigning │  │ • Stopping   │  │ • Metadata                 │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────────────────────┘  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Topology Sources                                                    │   │
│  │  ┌─────────────────────────┐     ┌──────────────────────────────┐   │   │
│  │  │ TopologySource          │     │ ClusterTopology              │   │   │
│  │  │ • RunAsync()            │────►│ • Nodes: ClusterNode[]       │   │   │
│  │  └───────────┬─────────────┘     │ • Timestamp                  │   │   │
│  │              │                   └──────────────────────────────┘   │   │
│  │              ▼                                                       │   │
│  │  ┌─────────────────────────┐                                        │   │
│  │  │ PollingTopologySource   │                                        │   │
│  │  │ • ReadAsync() abstract  │                                        │   │
│  │  │ • PollingInterval       │                                        │   │
│  │  └─────────────────────────┘                                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  gRPC Integration                                                    │   │
│  │  ┌─────────────────────────────────────────────────────────────┐    │   │
│  │  │  ClusterResolver : Resolver                                  │    │   │
│  │  │  • Owns Channel<ClusterTopology>                             │    │   │
│  │  │  • Discovery retry loop                                      │    │   │
│  │  └──────────────────────────┬──────────────────────────────────┘    │   │
│  │                              │                                       │   │
│  │                              ▼                                       │   │
│  │  ┌─────────────────────────────────────────────────────────────┐    │   │
│  │  │  ClusterLoadBalancer : LoadBalancer                          │    │   │
│  │  │  • Creates/manages Subchannels                               │    │   │
│  │  │  • Creates ClusterPicker on topology change                  │    │   │
│  │  └──────────────────────────┬──────────────────────────────────┘    │   │
│  │                              │                                       │   │
│  │                              ▼                                       │   │
│  │  ┌─────────────────────────────────────────────────────────────┐    │   │
│  │  │  ClusterPicker : SubchannelPicker                            │    │   │
│  │  │  • Wraps IClusterNodePickingStrategy                         │    │   │
│  │  │  • Provides SubchannelNode[] + HttpRequest to strategy       │    │   │
│  │  │  • Converts PickNodeResult → gRPC PickResult                 │    │   │
│  │  └─────────────────────────────────────────────────────────────┘    │   │
│  │                                                                      │   │
│  │  ┌─────────────────────────────────────────────────────────────┐    │   │
│  │  │  IClusterNodePickingStrategy                                 │    │   │
│  │  │  • Pick(in PickNodeContext) → PickNodeResult                 │    │   │
│  │  │  • Built-in: LeaderOnly, Default, RoundRobin                 │    │   │
│  │  └─────────────────────────────────────────────────────────────┘    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
TopologySource.RunAsync()
         │
         │ Protocol-specific discovery
         │
         ▼
ClusterTopology { Nodes[], Timestamp }
         │
         │ writer.WriteAsync()
         ▼
Channel<ClusterTopology>  ← Owned by ClusterResolver
         │
         │ reader.ReadAllAsync()
         ▼
ClusterResolver.ConsumeAsync()
         │
         │ Map to BalancerAddress[] (with ClusterNode in attributes)
         ▼
ClusterLoadBalancer.UpdateAddresses()
         │
         │ Create/update Subchannels
         ▼
ClusterPicker (wraps strategy)
         │
         │ Pick(PickContext)
         │   └─► Build SubchannelNode[]
         │   └─► Call strategy.Pick(in PickNodeContext)
         │   └─► Convert PickNodeResult → PickResult
         ▼
Subchannel → gRPC Call
```

---

## Core Model: Two-Axis Node State

### Design Philosophy

The Two-Axis Architecture separates **what a node is authorized to do** (Role) from **how it is currently performing** (Status). This creates a protocol-agnostic interface suitable for any distributed database or service mesh.

**Why Two Axes?**

| Single-Axis Problem | Two-Axis Solution |
|---------------------|-------------------|
| "PreLeader" - is it starting or syncing? | Role=Leader, Status=Syncing |
| "ReadOnlyLeaderless" - healthy or not? | Role=ReadOnly, Status=Isolated |
| 11+ exclusion states to memorize | `IsEligible` = Status is Ready or Isolated |
| Protocol-specific terminology | Universal Role/Status vocabulary |

### The Two Axes

```
         Status (Health)
         ─────────────────────────────────►
         Starting  Syncing  Ready  Isolated  Stopping  Stopped
    │
    │    None      ○         ○       ○         ○         ○        ○
R   │
o   │    Leader    ○         ○       ●         ○         ○        ○
l   │
e   │    Follower  ○         ○       ●         ●         ○        ○
    │
(A  │    ReadOnly  ○         ○       ●         ●         ○        ○
u   │
t   │    Manager   ○         ○       ○         ○         ○        ○
h   │
)   │    Resigning ○         ○       ○         ○         ○        ○
    ▼

● = Eligible for client traffic
○ = Not eligible
```

### NodeRole Enum

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Represents the functional role a node is AUTHORIZED to perform.
/// This is the "what" axis - what the node can do.
/// </summary>
public enum NodeRole {
    /// <summary>No defined role yet (initializing, unknown).</summary>
    None = 0,

    /// <summary>Authoritative for writes and coordination (primary/master).</summary>
    Leader = 1,

    /// <summary>Voting member that replicates data (secondary/replica).</summary>
    Follower = 2,

    /// <summary>Non-voting, read-scaling member (read replica).</summary>
    ReadOnly = 3,

    /// <summary>Administrative/orchestration only (not for client traffic).</summary>
    Manager = 4,

    /// <summary>Leader handing off authority (stepping down).</summary>
    Resigning = 5
}
```

### NodeStatus Enum

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Represents the node's synchronization HEALTH and operational progress.
/// This is the "how" axis - how the node is performing.
/// </summary>
public enum NodeStatus {
    /// <summary>Booting or discovering peers.</summary>
    Starting = 0,

    /// <summary>Catching up on logs (replicating data).</summary>
    Syncing = 1,

    /// <summary>Fully operational and current.</summary>
    Ready = 2,

    /// <summary>Healthy but lost quorum contact (can serve stale reads).</summary>
    Isolated = 3,

    /// <summary>Graceful shutdown in progress.</summary>
    Stopping = 4,

    /// <summary>Shutdown complete.</summary>
    Stopped = 5
}
```

### ClusterNode Struct

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Protocol-agnostic representation of a cluster node.
/// Separates "Role" (Authority) from "Status" (Health).
/// </summary>
/// <remarks>
/// <para>
/// This struct is the universal abstraction for all cluster node types.
/// Protocol-specific adapters create <see cref="ClusterNode"/> instances
/// from their native representations.
/// </para>
/// <para>
/// The <see cref="Metadata"/> dictionary handles protocol-specific extras
/// without polluting the core model.
/// </para>
/// </remarks>
public readonly record struct ClusterNode {
    /// <summary>Unique identifier for this node instance.</summary>
    public required Guid NodeId { get; init; }

    /// <summary>Network endpoint for connecting to this node.</summary>
    public required DnsEndPoint EndPoint { get; init; }

    /// <summary>The functional role this node is authorized to perform.</summary>
    public required NodeRole Role { get; init; }

    /// <summary>The node's current health and synchronization status.</summary>
    public required NodeStatus Status { get; init; }

    /// <summary>
    /// Protocol-specific metadata (e.g., original state enum, term, epoch).
    /// </summary>
    public ImmutableDictionary<string, object> Metadata { get; init; }
        = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Can this node serve read requests?
    /// True when node is Ready or Isolated (stale reads acceptable).
    /// </summary>
    public bool CanServeReads => Status is NodeStatus.Ready or NodeStatus.Isolated;

    /// <summary>
    /// Can this node accept write requests?
    /// True only when node is Leader AND Ready.
    /// </summary>
    public bool CanAcceptWrites => Role == NodeRole.Leader && Status == NodeStatus.Ready;

    /// <summary>
    /// Is this node eligible for client traffic?
    /// True when node is Ready or Isolated.
    /// </summary>
    public bool IsEligible => Status is NodeStatus.Ready or NodeStatus.Isolated;
}
```

### ClusterTopology Record

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Immutable snapshot of cluster topology at a point in time.
/// </summary>
/// <remarks>
/// This is a value object with cached hash code for efficient comparison.
/// Topology changes trigger picker recreation in the load balancer.
/// </remarks>
public sealed record ClusterTopology {
    /// <summary>All known nodes in the cluster.</summary>
    public required IReadOnlyList<ClusterNode> Nodes { get; init; }

    /// <summary>When this topology snapshot was captured.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Cached hash for efficient equality checks (0 = not computed)
    int _cachedHash;

    /// <summary>Returns nodes filtered by eligibility.</summary>
    public IEnumerable<ClusterNode> GetEligibleNodes() =>
        Nodes.Where(n => n.IsEligible);

    /// <summary>Returns nodes that can serve reads.</summary>
    public IEnumerable<ClusterNode> GetReadableNodes() =>
        Nodes.Where(n => n.CanServeReads);

    /// <summary>Returns nodes that can accept writes.</summary>
    public IEnumerable<ClusterNode> GetWritableNodes() =>
        Nodes.Where(n => n.CanAcceptWrites);

    /// <summary>
    /// Thread-safe cached hash code computation.
    /// </summary>
    public override int GetHashCode() {
        var cached = Volatile.Read(ref _cachedHash);
        if (cached != 0)
            return cached;

        var hash = new HashCode();
        foreach (var node in Nodes)
            hash.Add(node);

        var computed = hash.ToHashCode();
        // Avoid 0 as it means "not computed"
        if (computed == 0) computed = 1;

        Interlocked.CompareExchange(ref _cachedHash, computed, 0);
        return Volatile.Read(ref _cachedHash);
    }
}
```

### TopologyContext Record

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Context provided to topology sources for discovery operations.
/// </summary>
/// <param name="Channel">gRPC channel for making discovery calls.</param>
/// <param name="Seeds">Seed endpoints for initial discovery.</param>
/// <param name="CancellationToken">Token for cancellation.</param>
public sealed record TopologyContext(
    ChannelBase Channel,
    IReadOnlyList<DnsEndPoint> Seeds,
    CancellationToken CancellationToken = default
) {
    /// <summary>Optional logger for discovery operations.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Optional timeout for discovery calls.</summary>
    public TimeSpan? Timeout { get; init; }
}
```

---

## Topology Sources

### TopologySource Abstract Base

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Base class for topology discovery sources.
/// Subclasses implement protocol-specific discovery logic.
/// </summary>
/// <remarks>
/// <para>
/// The topology source is responsible for:
/// <list type="number">
/// <item>Discovering cluster members via protocol-specific means</item>
/// <item>Mapping protocol state to <see cref="ClusterNode"/></item>
/// </list>
/// </para>
/// <para>
/// The resolver owns the <see cref="ChannelWriter{T}"/> and passes it to
/// <see cref="RunAsync"/>. This decouples the producer from the consumer.
/// </para>
/// <para>
/// Node selection/picking is handled separately by <see cref="IClusterNodePickingStrategy"/>,
/// not by the topology source. This separation allows the same topology source to be
/// used with different picking strategies.
/// </para>
/// </remarks>
public abstract class TopologySource {
    /// <summary>
    /// Runs the topology discovery loop, writing updates to the channel.
    /// </summary>
    /// <param name="writer">Channel writer for topology updates.</param>
    /// <param name="context">Discovery context with channel, seeds, cancellation.</param>
    /// <returns>Task that completes when discovery stops.</returns>
    /// <remarks>
    /// This method should run continuously until cancelled, periodically
    /// (or reactively) writing new <see cref="ClusterTopology"/> snapshots.
    /// </remarks>
    protected internal abstract Task RunAsync(
        ChannelWriter<ClusterTopology> writer,
        TopologyContext context);
}
```

### PollingTopologySource Abstract

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Base class for polling-based topology discovery.
/// Subclasses implement <see cref="ReadAsync"/> to fetch current topology.
/// </summary>
/// <remarks>
/// <para>
/// This class handles the polling loop. Subclasses only need to implement
/// the single <see cref="ReadAsync"/> method for their specific protocol.
/// </para>
/// <para>
/// The source is responsible for querying topology from a given endpoint.
/// The <see cref="ClusterResolver"/> manages the "working set" of endpoints
/// (discovered nodes vs seeds fallback). See <c>reference-cluster-resolver-qa.md</c>.
/// </para>
/// </remarks>
public abstract class PollingTopologySource : TopologySource {
    readonly TimeSpan _pollingInterval;

    /// <summary>
    /// Initializes a new polling topology source.
    /// </summary>
    /// <param name="pollingInterval">Interval between polls.</param>
    protected PollingTopologySource(TimeSpan pollingInterval) {
        if (pollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), "Must be positive");
        _pollingInterval = pollingInterval;
    }

    /// <summary>
    /// Reads the current cluster topology.
    /// </summary>
    /// <param name="context">Discovery context.</param>
    /// <returns>Current topology snapshot.</returns>
    /// <remarks>
    /// Implement this method to perform protocol-specific discovery
    /// (e.g., gossip, DNS-SRV, health endpoints).
    /// </remarks>
    protected abstract ValueTask<ClusterTopology> ReadAsync(TopologyContext context);

    /// <inheritdoc/>
    protected internal sealed override async Task RunAsync(
        ChannelWriter<ClusterTopology> writer,
        TopologyContext context) {

        var ct = context.CancellationToken;

        while (!ct.IsCancellationRequested) {
            try {
                var topology = await ReadAsync(context).ConfigureAwait(false);
                await writer.WriteAsync(topology, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                context.Logger?.LogWarning(ex, "Topology read failed, retrying after interval");
            }

            try {
                await Task.Delay(_pollingInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
        }

        writer.TryComplete();
    }
}
```

---

## Configuration

### LoadBalancingOptions

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Configuration options for cluster load balancing.
/// JSON-serializable for IConfiguration binding.
/// </summary>
/// <example>
/// <code>
/// {
///   "LoadBalancing": {
///     "Seeds": ["node1:2113", "node2:2113"],
///     "UseTls": true,
///     "Resilience": {
///       "Timeout": "00:00:05",
///       "MaxDiscoveryAttempts": 10
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class LoadBalancingOptions {
    /// <summary>
    /// Seed endpoints for initial cluster discovery.
    /// Format: "host:port" or "host" (uses default port).
    /// </summary>
    public string[] Seeds { get; set; } = [];

    /// <summary>
    /// Whether to use TLS for connections.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Resilience and retry settings.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();
}
```

### ResilienceOptions

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Resilience settings for discovery and connection management.
/// JSON-serializable for IConfiguration binding.
/// </summary>
public sealed class ResilienceOptions {
    /// <summary>
    /// Timeout for individual discovery calls.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum discovery attempts before giving up.
    /// Default: 10.
    /// </summary>
    public int MaxDiscoveryAttempts { get; set; } = 10;

    /// <summary>
    /// Initial backoff delay between failed discovery rounds.
    /// Default: 100ms.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum backoff delay.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// gRPC status codes that trigger topology refresh.
    /// Default: [14] (Unavailable).
    /// </summary>
    /// <remarks>
    /// When no custom RefreshPolicy is set, this array is converted to
    /// a RefreshPolicy via <see cref="RefreshPolicy.FromStatusCodeInts"/>.
    /// </remarks>
    public int[] RefreshOnStatusCodes { get; set; } = [14]; // StatusCode.Unavailable
}
```

**Sample appsettings.json:**

```json
{
  "LoadBalancing": {
    "Seeds": ["node1:2113", "node2:2113", "node3:2113"],
    "UseTls": true,
    "Resilience": {
      "Timeout": "00:00:05",
      "MaxDiscoveryAttempts": 10,
      "InitialBackoff": "00:00:00.100",
      "MaxBackoff": "00:00:05",
      "RefreshOnStatusCodes": [14]
    }
  }
}
```

### RefreshPolicy

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Determines whether a gRPC exception should trigger topology refresh.
/// </summary>
/// <param name="exception">The exception that occurred.</param>
/// <returns>True if topology should be refreshed.</returns>
public delegate bool ShouldRefreshTopology(RpcException exception);

/// <summary>
/// Factory methods for creating refresh policies.
/// </summary>
public static class RefreshPolicy {
    /// <summary>
    /// Default policy: refresh on StatusCode.Unavailable.
    /// </summary>
    public static readonly ShouldRefreshTopology Default =
        static ex => ex.StatusCode == StatusCode.Unavailable;

    /// <summary>
    /// Never refresh on any error.
    /// </summary>
    public static readonly ShouldRefreshTopology Never =
        static _ => false;

    /// <summary>
    /// Always refresh on any error.
    /// </summary>
    public static readonly ShouldRefreshTopology Always =
        static _ => true;

    /// <summary>
    /// Refresh on specific status codes.
    /// </summary>
    /// <param name="codes">Status codes that trigger refresh.</param>
    public static ShouldRefreshTopology OnStatusCodes(params StatusCode[] codes) {
        var set = codes.ToHashSet();
        return ex => set.Contains(ex.StatusCode);
    }

    /// <summary>
    /// Refresh when exception message contains any of the triggers.
    /// </summary>
    /// <param name="triggers">Substrings to match (case-insensitive).</param>
    public static ShouldRefreshTopology OnMessageContains(params string[] triggers) =>
        ex => triggers.Any(t => ex.Message.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Refresh when exception trailers contain specific key.
    /// </summary>
    /// <param name="key">Trailer key to check.</param>
    public static ShouldRefreshTopology OnTrailerKey(string key) =>
        ex => ex.Trailers.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Refresh if ANY of the policies returns true.
    /// </summary>
    public static ShouldRefreshTopology Any(params ShouldRefreshTopology[] policies) =>
        ex => policies.Any(p => p(ex));

    /// <summary>
    /// Refresh if ALL policies return true.
    /// </summary>
    public static ShouldRefreshTopology All(params ShouldRefreshTopology[] policies) =>
        ex => policies.All(p => p(ex));

    /// <summary>
    /// Creates policy from integer status codes (for config binding).
    /// </summary>
    /// <param name="statusCodes">Integer status codes.</param>
    public static ShouldRefreshTopology FromStatusCodeInts(params int[] statusCodes) =>
        OnStatusCodes(statusCodes.Select(c => (StatusCode)c).ToArray());
}
```

---

## gRPC Infrastructure

### RefreshOnErrorInterceptor

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// gRPC interceptor that triggers topology refresh based on error policy.
/// </summary>
/// <remarks>
/// <para>
/// When a gRPC call fails with an <see cref="RpcException"/> that matches
/// the configured policy, the interceptor invokes the refresh action to
/// trigger topology re-discovery.
/// </para>
/// <para>
/// This is simpler than HTTP-level handlers and correctly catches gRPC errors.
/// </para>
/// </remarks>
internal sealed class RefreshOnErrorInterceptor : Interceptor {
    readonly ShouldRefreshTopology _policy;
    readonly Action _onRefresh;
    readonly ILogger? _logger;

    public RefreshOnErrorInterceptor(
        ShouldRefreshTopology policy,
        Action onRefresh,
        ILogger? logger = null) {

        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _onRefresh = onRefresh ?? throw new ArgumentNullException(nameof(onRefresh));
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) {

        var call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            HandleResponse(call.ResponseAsync),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) {

        var call = continuation(request, context);

        // For streaming, we wrap the response stream to catch errors
        return new AsyncServerStreamingCall<TResponse>(
            new RefreshOnErrorStreamReader<TResponse>(call.ResponseStream, _policy, _onRefresh, _logger),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> responseTask) {
        try {
            return await responseTask.ConfigureAwait(false);
        }
        catch (RpcException ex) when (_policy(ex)) {
            _logger?.LogDebug(ex, "Refresh triggered by RpcException: {StatusCode}", ex.StatusCode);
            _onRefresh();
            throw;
        }
    }

    /// <summary>
    /// Stream reader wrapper that triggers refresh on error.
    /// </summary>
    sealed class RefreshOnErrorStreamReader<T> : IAsyncStreamReader<T> {
        readonly IAsyncStreamReader<T> _inner;
        readonly ShouldRefreshTopology _policy;
        readonly Action _onRefresh;
        readonly ILogger? _logger;

        public RefreshOnErrorStreamReader(
            IAsyncStreamReader<T> inner,
            ShouldRefreshTopology policy,
            Action onRefresh,
            ILogger? logger) {

            _inner = inner;
            _policy = policy;
            _onRefresh = onRefresh;
            _logger = logger;
        }

        public T Current => _inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken) {
            try {
                return await _inner.MoveNext(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException ex) when (_policy(ex)) {
                _logger?.LogDebug(ex, "Refresh triggered by streaming RpcException: {StatusCode}", ex.StatusCode);
                _onRefresh();
                throw;
            }
        }
    }
}
```

### ClusterResolver

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// gRPC Resolver that discovers cluster topology via TopologySource.
/// </summary>
/// <remarks>
/// <para>
/// The resolver owns the <see cref="Channel{T}"/> for topology updates and
/// manages the "working set" of endpoints (discovered nodes vs seeds fallback).
/// </para>
/// <para>
/// Working set pattern:
/// <list type="number">
/// <item>Initially use seeds for discovery</item>
/// <item>After first topology, use discovered nodes for subsequent queries</item>
/// <item>Shuffle endpoints for load distribution</item>
/// <item>Fall back to seeds when all discovered nodes fail</item>
/// </list>
/// </para>
/// <para>
/// See <c>reference-cluster-resolver-qa.md</c> for detailed explanation.
/// </para>
/// </remarks>
internal sealed class ClusterResolver : Resolver {
    readonly TopologySource _source;
    readonly ResilienceOptions _resilience;
    readonly DnsEndPoint[] _seeds;
    readonly bool _useTls;
    readonly ILoggerFactory? _loggerFactory;
    readonly ILogger? _logger;

    readonly Channel<ClusterTopology> _topologyChannel;
    readonly CancellationTokenSource _cts = new();
    readonly object _lock = new();

    // Working set state
    ClusterTopology? _lastTopology;
    CancellationTokenSource? _refreshCts;

    Task? _runTask;
    bool _started;

    public ClusterResolver(
        TopologySource source,
        DnsEndPoint[] seeds,
        ResilienceOptions resilience,
        bool useTls = false,
        ILoggerFactory? loggerFactory = null) {

        _source = source ?? throw new ArgumentNullException(nameof(source));
        _seeds = seeds ?? throw new ArgumentNullException(nameof(seeds));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        _useTls = useTls;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<ClusterResolver>();

        _topologyChannel = Channel.CreateUnbounded<ClusterTopology>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true
            });
    }

    /// <inheritdoc/>
    public override void Start(Action<ResolverResult> listener) {
        if (_started)
            throw new InvalidOperationException("Resolver already started");

        _started = true;
        _runTask = RunAsync(listener);
    }

    /// <inheritdoc/>
    public override void Refresh() {
        _logger?.LogDebug("Refresh requested");

        // Cancel current polling delay to trigger immediate refresh
        lock (_lock) {
            _refreshCts?.Cancel();
        }
    }

    async Task RunAsync(Action<ResolverResult> listener) {
        var ct = _cts.Token;

        // Run source in background with retry logic
        _ = Task.Run(() => RunSourceWithRetryAsync(ct), ct);

        // Consume topology updates
        await foreach (var topology in _topologyChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            // Update working set
            _lastTopology = topology;

            var addresses = topology.Nodes
                .Where(n => n.IsEligible)
                .Select(n => new BalancerAddress(
                    n.EndPoint.Host,
                    n.EndPoint.Port,
                    CreateAttributes(n)))
                .ToArray();

            if (addresses.Length > 0) {
                listener(ResolverResult.ForResult(addresses));
                _logger?.LogDebug("Reported {Count} addresses to load balancer", addresses.Length);
            }
            else {
                _logger?.LogWarning("No eligible nodes in topology");
            }
        }
    }

    async Task RunSourceWithRetryAsync(CancellationToken ct) {
        var attempt = 0;
        var backoff = _resilience.InitialBackoff;

        while (!ct.IsCancellationRequested) {
            try {
                attempt++;
                _logger?.LogDebug("Starting topology source, attempt {Attempt}", attempt);

                // Get working endpoints (discovered nodes or seeds)
                var workingEndpoints = GetWorkingEndpoints();

                // Try each endpoint until one succeeds
                Exception? lastException = null;
                foreach (var endpoint in workingEndpoints) {
                    try {
                        var channel = CreateChannel(endpoint);
                        var context = new TopologyContext(channel, _seeds, ct) {
                            Logger = _logger,
                            Timeout = _resilience.Timeout
                        };

                        // Create cancellable delay token for refresh
                        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        lock (_lock) { _refreshCts = refreshCts; }

                        try {
                            await _source.RunAsync(_topologyChannel.Writer, context).ConfigureAwait(false);
                            attempt = 0; // Reset on success
                            backoff = _resilience.InitialBackoff;
                        }
                        finally {
                            lock (_lock) { _refreshCts = null; }
                        }

                        break; // Source completed normally
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                        throw;
                    }
                    catch (Exception ex) {
                        lastException = ex;
                        _logger?.LogDebug(ex, "Endpoint {Endpoint} failed, trying next", endpoint);
                    }
                }

                if (lastException != null && !ct.IsCancellationRequested) {
                    throw lastException; // All endpoints failed
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                var level = GetLogLevelForAttempt(attempt);
                _logger?.Log(level, ex, "Topology source failed on attempt {Attempt}", attempt);

                if (attempt >= _resilience.MaxDiscoveryAttempts) {
                    _logger?.LogError("Max discovery attempts ({Max}) reached", _resilience.MaxDiscoveryAttempts);
                    throw new DiscoveryException(attempt, ex);
                }

                // Wait before retry (can be cancelled by Refresh())
                try {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lock (_lock) { _refreshCts = delayCts; }
                    try {
                        await Task.Delay(backoff, delayCts.Token).ConfigureAwait(false);
                    }
                    finally {
                        lock (_lock) { _refreshCts = null; }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                    _logger?.LogDebug("Delay cancelled by refresh request");
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _resilience.MaxBackoff.Ticks));
            }
        }
    }

    /// <summary>
    /// Gets the working set of endpoints: discovered nodes if available, otherwise seeds.
    /// Shuffles for load distribution.
    /// </summary>
    IReadOnlyList<DnsEndPoint> GetWorkingEndpoints() {
        var topology = _lastTopology;

        // Use discovered nodes if we have them
        DnsEndPoint[] endpoints;
        if (topology != null && topology.Nodes.Count > 0) {
            endpoints = topology.Nodes
                .Where(n => n.IsEligible)
                .Select(n => n.EndPoint)
                .ToArray();

            if (endpoints.Length > 0) {
                _logger?.LogTrace("Using {Count} discovered nodes for working set", endpoints.Length);
            }
            else {
                endpoints = _seeds;
                _logger?.LogTrace("No eligible discovered nodes, falling back to {Count} seeds", _seeds.Length);
            }
        }
        else {
            endpoints = _seeds;
            _logger?.LogTrace("No topology yet, using {Count} seeds", _seeds.Length);
        }

        // Shuffle for load distribution
        return Shuffle(endpoints);
    }

    static T[] Shuffle<T>(T[] array) {
        var result = new T[array.Length];
        Array.Copy(array, result, array.Length);
        Random.Shared.Shuffle(result);
        return result;
    }

    GrpcChannel CreateChannel(DnsEndPoint endpoint) {
        var scheme = _useTls ? "https" : "http";
        return GrpcChannel.ForAddress($"{scheme}://{endpoint.Host}:{endpoint.Port}");
    }

    static LogLevel GetLogLevelForAttempt(int attempt) => attempt switch {
        1 => LogLevel.Warning,
        _ => LogLevel.Debug
    };

    static BalancerAttributes CreateAttributes(ClusterNode node) {
        var attrs = new BalancerAttributes();
        // Store full node for efficient extraction in picker
        attrs.Set(ClusterNodeAttributes.Node, node);
        // Also store individual values for debugging/logging
        attrs.Set(ClusterNodeAttributes.NodeId, node.NodeId);
        attrs.Set(ClusterNodeAttributes.Role, node.Role);
        attrs.Set(ClusterNodeAttributes.Status, node.Status);
        return attrs;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _cts.Cancel();
            _cts.Dispose();
            _topologyChannel.Writer.TryComplete();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Attribute keys for ClusterNode data on BalancerAddress.
/// </summary>
/// <remarks>
/// <para>
/// Prefer storing the full <see cref="Node"/> attribute when possible.
/// The individual NodeId/Role/Status attributes are kept for backward
/// compatibility and debugging visibility.
/// </para>
/// </remarks>
public static class ClusterNodeAttributes {
    /// <summary>Full ClusterNode (preferred - avoids reconstruction).</summary>
    public static readonly BalancerAttributeKey<ClusterNode> Node = new("cluster-node");

    /// <summary>Node identifier (for debugging/logging).</summary>
    public static readonly BalancerAttributeKey<Guid> NodeId = new("cluster-node-id");

    /// <summary>Node role (for debugging/logging).</summary>
    public static readonly BalancerAttributeKey<NodeRole> Role = new("cluster-node-role");

    /// <summary>Node status (for debugging/logging).</summary>
    public static readonly BalancerAttributeKey<NodeStatus> Status = new("cluster-node-status");
}
```

### Node Picking Strategy

The picking strategy abstracts gRPC's complex `SubchannelPicker` API behind a clean, domain-focused interface.

#### Why Abstract the Picker?

gRPC's native `SubchannelPicker` API has several issues that make it difficult to use directly:

| Problem | gRPC API | Our Abstraction |
|---------|----------|-----------------|
| **Confusing naming** | `PickResult.ForQueue()` (what queue?) | `PickNodeResult.Defer()` |
| **No domain context** | Raw `Subchannel` with attributes | `SubchannelNode` pairs subchannel with `ClusterNode` |
| **Hidden request** | Must extract from `PickContext.Request` | `PickNodeContext.Request` is explicit |
| **Generic failures** | `PickResult.ForFailure(Status)` | `PickNodeResult.NoLeader()`, `.NoFollowers()`, etc. |
| **Complex result types** | Multiple static factory methods | Single `PickNodeResult` with clear semantics |

#### Advantages of the Abstraction

1. **Clean API**: Users implement `Pick(in PickNodeContext)` with all data they need
2. **Full context**: Nodes, subchannels, AND the HTTP request in one place
3. **Domain-specific failures**: Self-documenting error cases
4. **Testable**: Can unit test strategies without gRPC infrastructure
5. **Swappable**: Same topology source works with different strategies

#### Core Types

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// A node paired with its gRPC subchannel.
/// </summary>
public readonly record struct SubchannelNode(Subchannel Subchannel, ClusterNode Node);

/// <summary>
/// Context provided to the picking strategy.
/// </summary>
public readonly record struct PickNodeContext(SubchannelNode[] Nodes, HttpRequestMessage Request);

/// <summary>
/// Result of a node picking operation.
/// Wraps gRPC's PickResult with domain-specific factory methods.
/// </summary>
public readonly struct PickNodeResult {
    private readonly Subchannel? _subchannel;
    private readonly Status? _status;
    private readonly PickNodeResultType _type;

    private enum PickNodeResultType { Subchannel, Fail, Drop, Defer }

    // === Success ===

    /// <summary>Route to the specified subchannel.</summary>
    public static PickNodeResult ForSubchannel(Subchannel subchannel) =>
        new(PickNodeResultType.Subchannel, subchannel, null);

    /// <summary>Route to the specified node's subchannel.</summary>
    public static PickNodeResult ForNode(SubchannelNode node) =>
        ForSubchannel(node.Subchannel);

    // === Defer ===

    /// <summary>Wait for a new picker (topology refresh in progress).</summary>
    public static PickNodeResult Defer() =>
        new(PickNodeResultType.Defer, null, null);

    // === Drop ===

    /// <summary>Fail immediately, bypass retry policy.</summary>
    public static PickNodeResult Drop(string reason) =>
        new(PickNodeResultType.Drop, null, new Status(StatusCode.Unavailable, reason));

    // === Domain-Specific Failures ===

    /// <summary>No leader available for write operations.</summary>
    public static PickNodeResult NoLeader() =>
        Fail(StatusCode.Unavailable, "No leader available for write operations");

    /// <summary>No followers available for read operations.</summary>
    public static PickNodeResult NoFollowers() =>
        Fail(StatusCode.Unavailable, "No followers available for read operations");

    /// <summary>No eligible nodes in cluster.</summary>
    public static PickNodeResult NoEligibleNodes() =>
        Fail(StatusCode.Unavailable, "No eligible nodes in cluster");

    /// <summary>Cluster is not ready.</summary>
    public static PickNodeResult ClusterNotReady() =>
        Fail(StatusCode.Unavailable, "Cluster is not ready");

    // === Generic Failures ===

    /// <summary>Fail with specified status code and message.</summary>
    public static PickNodeResult Fail(StatusCode code, string message) =>
        new(PickNodeResultType.Fail, null, new Status(code, message));

    /// <summary>Fail with Unavailable status and specified message.</summary>
    public static PickNodeResult Fail(string message) =>
        Fail(StatusCode.Unavailable, message);

    // === Internal ===

    /// <summary>Convert to gRPC's PickResult.</summary>
    internal PickResult ToPickResult() => _type switch {
        PickNodeResultType.Subchannel => PickResult.ForSubchannel(_subchannel!),
        PickNodeResultType.Defer      => PickResult.ForQueue(),
        PickNodeResultType.Drop       => PickResult.ForDrop(_status!.Value),
        PickNodeResultType.Fail       => PickResult.ForFailure(_status!.Value),
        _                             => PickResult.ForFailure(new Status(StatusCode.Internal, "Invalid pick result"))
    };

    private PickNodeResult(PickNodeResultType type, Subchannel? subchannel, Status? status) {
        _type = type;
        _subchannel = subchannel;
        _status = status;
    }
}

/// <summary>
/// Strategy for selecting which node to route a request to.
/// </summary>
public interface IClusterNodePickingStrategy {
    /// <summary>
    /// Pick a node for the given request context.
    /// </summary>
    /// <param name="context">The picking context with available nodes and request.</param>
    /// <returns>The pick result indicating which node to use or why picking failed.</returns>
    PickNodeResult Pick(in PickNodeContext context);
}
```

#### Built-in Strategies

**Note:** All built-in strategies are designed for **zero-allocation on the hot path**.
They use simple loops instead of LINQ to avoid iterator allocations per pick.

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Routes all requests to the leader.
/// Use for strict consistency requirements.
/// </summary>
/// <remarks>
/// Zero-allocation: Uses simple loop scan.
/// </remarks>
public sealed class LeaderOnlyStrategy : IClusterNodePickingStrategy {
    public static LeaderOnlyStrategy Instance { get; } = new();

    public PickNodeResult Pick(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Simple loop - no LINQ allocation
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.CanAcceptWrites)
                return PickNodeResult.ForNode(nodes[i]);
        }

        return PickNodeResult.NoLeader();
    }
}

/// <summary>
/// Routes writes to leader, reads to followers with round-robin.
/// Falls back to leader for reads if no followers available.
/// </summary>
/// <remarks>
/// <para>
/// Zero-allocation: Uses simple loops with role priority scanning.
/// </para>
/// <para>
/// Read preference order: ReadOnly → Follower → Leader
/// </para>
/// </remarks>
public sealed class DefaultStrategy : IClusterNodePickingStrategy {
    public static DefaultStrategy Instance { get; } = new();

    long _readOnlyIndex;
    long _followerIndex;
    long _leaderIndex;
    readonly string[] _writeMethodPrefixes;

    public DefaultStrategy() : this("Append", "Delete", "Create", "Set", "Update", "Write", "Put", "Remove") { }

    public DefaultStrategy(params string[] writeMethodPrefixes) {
        _writeMethodPrefixes = writeMethodPrefixes;
    }

    public PickNodeResult Pick(in PickNodeContext context) {
        if (context.Nodes.Length == 0)
            return PickNodeResult.NoEligibleNodes();

        var method = ExtractMethodName(context.Request);
        var isWrite = IsWriteMethod(method);

        return isWrite ? PickForWrite(context) : PickForRead(context);
    }

    PickNodeResult PickForWrite(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Simple loop - no LINQ allocation
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.CanAcceptWrites)
                return PickNodeResult.ForNode(nodes[i]);
        }

        return PickNodeResult.NoLeader();
    }

    PickNodeResult PickForRead(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Count nodes by role (single pass)
        int readOnlyCount = 0, followerCount = 0, leaderCount = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (!nodes[i].Node.CanServeReads) continue;
            switch (nodes[i].Node.Role) {
                case NodeRole.ReadOnly: readOnlyCount++; break;
                case NodeRole.Follower: followerCount++; break;
                case NodeRole.Leader:   leaderCount++;   break;
            }
        }

        // Prefer ReadOnly → Follower → Leader
        if (readOnlyCount > 0) {
            var targetIndex = (int)(Interlocked.Increment(ref _readOnlyIndex) % readOnlyCount);
            return FindNthByRole(nodes, NodeRole.ReadOnly, targetIndex);
        }

        if (followerCount > 0) {
            var targetIndex = (int)(Interlocked.Increment(ref _followerIndex) % followerCount);
            return FindNthByRole(nodes, NodeRole.Follower, targetIndex);
        }

        if (leaderCount > 0) {
            var targetIndex = (int)(Interlocked.Increment(ref _leaderIndex) % leaderCount);
            return FindNthByRole(nodes, NodeRole.Leader, targetIndex);
        }

        return PickNodeResult.NoEligibleNodes();
    }

    static PickNodeResult FindNthByRole(SubchannelNode[] nodes, NodeRole role, int targetIndex) {
        var count = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.CanServeReads && nodes[i].Node.Role == role) {
                if (count == targetIndex)
                    return PickNodeResult.ForNode(nodes[i]);
                count++;
            }
        }
        return PickNodeResult.NoEligibleNodes(); // Shouldn't happen
    }

    bool IsWriteMethod(string method) {
        // Simple loop - no LINQ allocation
        for (var i = 0; i < _writeMethodPrefixes.Length; i++) {
            if (method.StartsWith(_writeMethodPrefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string ExtractMethodName(HttpRequestMessage request) =>
        request.RequestUri?.Segments.LastOrDefault() ?? "";
}

/// <summary>
/// Round-robin across all eligible nodes.
/// No read/write distinction.
/// </summary>
/// <remarks>
/// Zero-allocation: Uses simple loop with modular indexing.
/// </remarks>
public sealed class RoundRobinStrategy : IClusterNodePickingStrategy {
    public static RoundRobinStrategy Instance { get; } = new();

    long _index;

    public PickNodeResult Pick(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Count eligible nodes (single pass)
        var eligibleCount = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.IsEligible)
                eligibleCount++;
        }

        if (eligibleCount == 0)
            return PickNodeResult.NoEligibleNodes();

        // Find the Nth eligible node
        var targetIndex = (int)(Interlocked.Increment(ref _index) % eligibleCount);
        var count = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.IsEligible) {
                if (count == targetIndex)
                    return PickNodeResult.ForNode(nodes[i]);
                count++;
            }
        }

        return PickNodeResult.NoEligibleNodes(); // Shouldn't happen
    }
}
```

### ClusterLoadBalancer

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// gRPC LoadBalancer that manages subchannels based on cluster topology.
/// </summary>
internal sealed class ClusterLoadBalancer : SubchannelsLoadBalancer {
    readonly IClusterNodePickingStrategy _strategy;
    readonly ILogger? _logger;

    public ClusterLoadBalancer(
        IChannelControlHelper controller,
        IClusterNodePickingStrategy strategy,
        ILoggerFactory? loggerFactory = null)
        : base(controller, loggerFactory) {

        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger = loggerFactory?.CreateLogger<ClusterLoadBalancer>();
    }

    /// <inheritdoc/>
    protected override SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels) {
        _logger?.LogDebug("Creating picker with {Count} ready subchannels", readySubchannels.Count);
        return new ClusterPicker(readySubchannels, _strategy, _logger);
    }
}
```

### ClusterPicker

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// SubchannelPicker that wraps <see cref="IClusterNodePickingStrategy"/>.
/// Builds context and delegates to strategy for clean abstraction.
/// </summary>
/// <remarks>
/// <para>
/// This class bridges gRPC's complex <see cref="SubchannelPicker"/> API to our
/// clean <see cref="IClusterNodePickingStrategy"/> abstraction.
/// </para>
/// <para>
/// The picker builds <see cref="SubchannelNode"/> array once at construction
/// (cold path), then each Pick() creates context and delegates to strategy.
/// </para>
/// </remarks>
internal sealed class ClusterPicker : SubchannelPicker {
    readonly SubchannelNode[] _nodes;
    readonly IClusterNodePickingStrategy _strategy;
    readonly ILogger? _logger;

    public ClusterPicker(
        IReadOnlyList<Subchannel> readySubchannels,
        IClusterNodePickingStrategy strategy,
        ILogger? logger = null) {

        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger = logger;

        // Build SubchannelNode[] ONCE at construction (cold path)
        _nodes = readySubchannels
            .Select(sc => new SubchannelNode(sc, ExtractNode(sc)))
            .Where(sn => sn.Node.NodeId != Guid.Empty) // Filter out invalid nodes
            .ToArray();

        _logger?.LogTrace("Picker created with {Count} nodes", _nodes.Length);
    }

    /// <summary>
    /// Picks a subchannel for the next call by delegating to strategy.
    /// </summary>
    public override PickResult Pick(PickContext context) {
        // Build context with nodes and HTTP request
        var pickContext = new PickNodeContext(_nodes, context.Request);

        // Delegate to strategy
        var result = _strategy.Pick(in pickContext);

        // Convert to gRPC PickResult
        return result.ToPickResult();
    }

    static ClusterNode ExtractNode(Subchannel subchannel) {
        var address = subchannel.CurrentAddress;
        if (address == null)
            return default;

        // Prefer full ClusterNode if stored, otherwise reconstruct from parts
        if (address.Attributes.TryGetValue(ClusterNodeAttributes.Node, out var node))
            return node;

        return new ClusterNode {
            NodeId = address.Attributes.TryGetValue(ClusterNodeAttributes.NodeId, out var id) ? id : Guid.Empty,
            EndPoint = new DnsEndPoint(address.EndPoint.Host, address.EndPoint.Port),
            Role = address.Attributes.TryGetValue(ClusterNodeAttributes.Role, out var role) ? role : NodeRole.None,
            Status = address.Attributes.TryGetValue(ClusterNodeAttributes.Status, out var status) ? status : NodeStatus.Starting
        };
    }
}
```

### Factories

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Factory for creating ClusterResolver instances.
/// </summary>
internal sealed class ClusterResolverFactory : ResolverFactory {
    readonly TopologySource _source;
    readonly DnsEndPoint[] _seeds;
    readonly ResilienceOptions _resilience;
    readonly bool _useTls;
    readonly ILoggerFactory? _loggerFactory;

    public override string Name => "cluster";

    public ClusterResolverFactory(
        TopologySource source,
        DnsEndPoint[] seeds,
        ResilienceOptions resilience,
        bool useTls = false,
        ILoggerFactory? loggerFactory = null) {

        _source = source;
        _seeds = seeds;
        _resilience = resilience;
        _useTls = useTls;
        _loggerFactory = loggerFactory;
    }

    public override Resolver Create(ResolverOptions options) =>
        new ClusterResolver(_source, _seeds, _resilience, _useTls, _loggerFactory);
}

/// <summary>
/// Factory for creating ClusterLoadBalancer instances.
/// </summary>
internal sealed class ClusterLoadBalancerFactory : LoadBalancerFactory {
    readonly IClusterNodePickingStrategy _strategy;
    readonly ILoggerFactory? _loggerFactory;

    public override string Name => "cluster";

    public ClusterLoadBalancerFactory(
        IClusterNodePickingStrategy strategy,
        ILoggerFactory? loggerFactory = null) {

        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _loggerFactory = loggerFactory;
    }

    public override LoadBalancer Create(LoadBalancerOptions options) =>
        new ClusterLoadBalancer(options.Controller, _strategy, _loggerFactory);
}
```

---

## Builder Pattern

### LoadBalancingBuilder Non-DI

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Builder for configuring load-balanced gRPC channels without DI.
/// </summary>
public sealed class LoadBalancingBuilder {
    DnsEndPoint[] _seeds = [];
    bool _useTls;
    ResilienceOptions _resilience = new();
    TopologySource? _topologySource;
    IClusterNodePickingStrategy _pickingStrategy = DefaultStrategy.Instance;
    ShouldRefreshTopology? _refreshPolicy;
    ILoggerFactory? _loggerFactory;
    Action<GrpcChannelOptions>? _configureChannel;

    public LoadBalancingBuilder WithSeeds(params string[] endpoints) {
        _seeds = endpoints.Select(ParseEndpoint).ToArray();
        return this;
    }

    public LoadBalancingBuilder WithSeeds(params DnsEndPoint[] endpoints) {
        _seeds = endpoints;
        return this;
    }

    public LoadBalancingBuilder WithResilience(Action<ResilienceOptions> configure) {
        configure?.Invoke(_resilience);
        return this;
    }

    public LoadBalancingBuilder WithPollingTopologySource(PollingTopologySource source) {
        _topologySource = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    public LoadBalancingBuilder WithStreamingTopologySource(TopologySource source) {
        _topologySource = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    /// <summary>
    /// Configures the node picking strategy.
    /// Default: <see cref="DefaultStrategy"/> (writes to leader, reads round-robin).
    /// </summary>
    public LoadBalancingBuilder WithPickingStrategy(IClusterNodePickingStrategy strategy) {
        _pickingStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    public LoadBalancingBuilder WithRefreshPolicy(ShouldRefreshTopology policy) {
        _refreshPolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    public LoadBalancingBuilder WithLoggerFactory(ILoggerFactory? factory) {
        _loggerFactory = factory;
        return this;
    }

    public LoadBalancingBuilder ConfigureChannel(Action<GrpcChannelOptions> configure) {
        _configureChannel = configure;
        return this;
    }

    public LoadBalancingBuilder UseTls(bool useTls = true) {
        _useTls = useTls;
        return this;
    }

    /// <summary>
    /// Builds the load-balanced gRPC channel.
    /// </summary>
    /// <param name="address">
    /// Optional logical cluster address (e.g., "cluster:///my-cluster").
    /// If not provided, a random identifier is generated.
    /// </param>
    public GrpcChannel Build(string? address = null) {
        if (_seeds.Length == 0)
            throw new InvalidOperationException("At least one seed endpoint is required");
        if (_topologySource == null)
            throw new InvalidOperationException("A topology source is required");

        var refreshPolicy = _refreshPolicy
            ?? RefreshPolicy.FromStatusCodeInts(_resilience.RefreshOnStatusCodes);

        var services = new ServiceCollection();

        services.AddSingleton<ResolverFactory>(sp =>
            new ClusterResolverFactory(_topologySource, _seeds, _resilience, _useTls, _loggerFactory));

        services.AddSingleton<LoadBalancerFactory>(sp =>
            new ClusterLoadBalancerFactory(_pickingStrategy, _loggerFactory));

        var serviceProvider = services.BuildServiceProvider();

        var options = new GrpcChannelOptions {
            ServiceProvider = serviceProvider,
            LoggerFactory = _loggerFactory,
            Credentials = _useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig {
                LoadBalancingConfigs = { new LoadBalancingConfig("cluster") }
            }
        };

        _configureChannel?.Invoke(options);

        // Use provided address or generate a random channel identifier
        var channelAddress = address ?? $"cluster:///{Guid.NewGuid().ToString("N")[..8]}";
        return GrpcChannel.ForAddress(channelAddress, options);
    }

    static DnsEndPoint ParseEndpoint(string endpoint) {
        var parts = endpoint.Split(':', 2);
        var host = parts[0];
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 2113;
        return new DnsEndPoint(host, port);
    }
}
```

### GrpcLoadBalancedChannel Entry Point

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Factory methods for creating load-balanced gRPC channels.
/// </summary>
public static class GrpcLoadBalancedChannel {
    /// <summary>
    /// Creates a load-balanced gRPC channel.
    /// </summary>
    /// <param name="address">
    /// Logical cluster address (e.g., "cluster:///my-cluster").
    /// The path component is used as the channel identifier.
    /// </param>
    /// <param name="configure">Builder configuration action.</param>
    public static GrpcChannel ForAddress(string address, Action<LoadBalancingBuilder> configure) {
        var builder = new LoadBalancingBuilder();
        configure(builder);
        return builder.Build(address);
    }

    /// <summary>
    /// Creates a load-balanced gRPC channel with auto-generated identifier.
    /// </summary>
    /// <param name="configure">Builder configuration action.</param>
    public static GrpcChannel Create(Action<LoadBalancingBuilder> configure) {
        var builder = new LoadBalancingBuilder();
        configure(builder);
        return builder.Build();
    }

    public static GrpcChannel FromConfiguration(
        IConfiguration configuration,
        Action<LoadBalancingBuilder>? configure = null) {

        var options = configuration.Get<LoadBalancingOptions>() ?? new();

        var builder = new LoadBalancingBuilder()
            .WithSeeds(options.Seeds)
            .UseTls(options.UseTls)
            .WithResilience(r => {
                r.Timeout = options.Resilience.Timeout;
                r.MaxDiscoveryAttempts = options.Resilience.MaxDiscoveryAttempts;
                r.InitialBackoff = options.Resilience.InitialBackoff;
                r.MaxBackoff = options.Resilience.MaxBackoff;
                r.RefreshOnStatusCodes = options.Resilience.RefreshOnStatusCodes;
            });

        configure?.Invoke(builder);
        return builder.Build();
    }
}
```

### LoadBalancingServiceBuilder DI

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Builder for configuring load-balanced gRPC channels with DI.
/// </summary>
public sealed class LoadBalancingServiceBuilder {
    readonly IServiceCollection _services;
    readonly string _channelId;

    DnsEndPoint[] _seeds = [];
    bool _useTls;
    ResilienceOptions _resilience = new();
    Type? _topologySourceType;
    Type _pickingStrategyType = typeof(DefaultStrategy);
    IClusterNodePickingStrategy? _pickingStrategyInstance;
    ShouldRefreshTopology? _refreshPolicy;
    Action<GrpcChannelOptions>? _configureChannel;

    internal LoadBalancingServiceBuilder(IServiceCollection services, string channelId) {
        _services = services;
        _channelId = channelId;
    }

    public LoadBalancingServiceBuilder WithSeeds(params string[] endpoints) {
        _seeds = endpoints.Select(e => {
            var parts = e.Split(':', 2);
            return new DnsEndPoint(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 2113);
        }).ToArray();
        return this;
    }

    public LoadBalancingServiceBuilder WithResilience(Action<ResilienceOptions> configure) {
        configure?.Invoke(_resilience);
        return this;
    }

    public LoadBalancingServiceBuilder WithPollingTopologySource<TSource>()
        where TSource : PollingTopologySource {

        _topologySourceType = typeof(TSource);
        return this;
    }

    public LoadBalancingServiceBuilder WithStreamingTopologySource<TSource>()
        where TSource : TopologySource {

        _topologySourceType = typeof(TSource);
        return this;
    }

    /// <summary>
    /// Configures the node picking strategy via DI.
    /// Default: <see cref="DefaultStrategy"/>.
    /// </summary>
    public LoadBalancingServiceBuilder WithPickingStrategy<TStrategy>()
        where TStrategy : class, IClusterNodePickingStrategy {

        _pickingStrategyType = typeof(TStrategy);
        _pickingStrategyInstance = null;
        return this;
    }

    /// <summary>
    /// Configures the node picking strategy with an instance.
    /// </summary>
    public LoadBalancingServiceBuilder WithPickingStrategy(IClusterNodePickingStrategy strategy) {
        _pickingStrategyInstance = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    public LoadBalancingServiceBuilder WithRefreshPolicy(ShouldRefreshTopology policy) {
        _refreshPolicy = policy;
        return this;
    }

    public LoadBalancingServiceBuilder ConfigureChannel(Action<GrpcChannelOptions> configure) {
        _configureChannel = configure;
        return this;
    }

    public LoadBalancingServiceBuilder UseTls(bool useTls = true) {
        _useTls = useTls;
        return this;
    }

    public IServiceCollection Build() {
        if (_topologySourceType == null)
            throw new InvalidOperationException("A topology source type is required");

        _services.AddSingleton(_topologySourceType);

        // Register picking strategy
        if (_pickingStrategyInstance != null) {
            _services.AddSingleton(_pickingStrategyInstance);
        }
        else {
            _services.AddSingleton(typeof(IClusterNodePickingStrategy), _pickingStrategyType);
        }

        _services.AddSingleton<ResolverFactory>(sp => {
            var source = (TopologySource)sp.GetRequiredService(_topologySourceType);
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new ClusterResolverFactory(source, _seeds, _resilience, _useTls, loggerFactory);
        });

        _services.AddSingleton<LoadBalancerFactory>(sp => {
            var strategy = sp.GetRequiredService<IClusterNodePickingStrategy>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new ClusterLoadBalancerFactory(strategy, loggerFactory);
        });

        var seeds = _seeds;
        var useTls = _useTls;
        var configureChannel = _configureChannel;
        var channelId = _channelId;

        _services.AddSingleton(sp => {
            var options = new GrpcChannelOptions {
                ServiceProvider = sp,
                LoggerFactory = sp.GetService<ILoggerFactory>(),
                Credentials = useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig {
                    LoadBalancingConfigs = { new LoadBalancingConfig("cluster") }
                }
            };

            configureChannel?.Invoke(options);
            return GrpcChannel.ForAddress($"cluster:///{channelId}", options);
        });

        return _services;
    }
}
```

### ServiceCollection Extensions

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Extension methods for registering cluster load balancing with DI.
/// </summary>
public static class ServiceCollectionExtensions {
    public static LoadBalancingServiceBuilder AddGrpcLoadBalancing(
        this IServiceCollection services,
        string channelId) {

        return new LoadBalancingServiceBuilder(services, channelId);
    }

    public static LoadBalancingServiceBuilder AddGrpcLoadBalancing(
        this IServiceCollection services,
        string channelId,
        IConfiguration configuration) {

        var options = configuration.Get<LoadBalancingOptions>() ?? new();

        return new LoadBalancingServiceBuilder(services, channelId)
            .WithSeeds(options.Seeds)
            .UseTls(options.UseTls)
            .WithResilience(r => {
                r.Timeout = options.Resilience.Timeout;
                r.MaxDiscoveryAttempts = options.Resilience.MaxDiscoveryAttempts;
                r.InitialBackoff = options.Resilience.InitialBackoff;
                r.MaxBackoff = options.Resilience.MaxBackoff;
                r.RefreshOnStatusCodes = options.Resilience.RefreshOnStatusCodes;
            });
    }
}
```

---

## Exceptions

### DiscoveryException

```csharp
namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Exception thrown when cluster discovery fails after exhausting all retry attempts.
/// </summary>
/// <remarks>
/// <para>
/// This exception is thrown by <see cref="ClusterResolver"/> when it cannot
/// establish a connection to any cluster node after the maximum number of
/// discovery attempts (configured via <see cref="ResilienceOptions.MaxDiscoveryAttempts"/>).
/// </para>
/// <para>
/// The exception preserves the number of attempts made and the last underlying
/// exception for diagnostic purposes.
/// </para>
/// </remarks>
public sealed class DiscoveryException : Exception {
    /// <summary>
    /// Gets the number of discovery attempts that were made.
    /// </summary>
    public int AttemptCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveryException"/> class.
    /// </summary>
    /// <param name="attemptCount">The number of discovery attempts made.</param>
    /// <param name="innerException">The last exception that caused discovery to fail.</param>
    public DiscoveryException(int attemptCount, Exception? innerException = null)
        : base($"Failed to discover cluster after {attemptCount} attempts.", innerException) {
        AttemptCount = attemptCount;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveryException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="attemptCount">The number of discovery attempts made.</param>
    /// <param name="innerException">The last exception that caused discovery to fail.</param>
    public DiscoveryException(string message, int attemptCount, Exception? innerException = null)
        : base(message, innerException) {
        AttemptCount = attemptCount;
    }
}
```

---

## Implementation Plan

### File Structure

```
src/Kurrent.Grpc.LoadBalancing/
├── Kurrent.Grpc.LoadBalancing.csproj
├── Model/
│   ├── NodeRole.cs
│   ├── NodeStatus.cs
│   ├── ClusterNode.cs
│   ├── ClusterTopology.cs
│   └── TopologyContext.cs
├── Sources/
│   ├── TopologySource.cs
│   └── PollingTopologySource.cs
├── Picking/
│   ├── SubchannelNode.cs
│   ├── PickNodeContext.cs
│   ├── PickNodeResult.cs
│   ├── IClusterNodePickingStrategy.cs
│   ├── LeaderOnlyStrategy.cs
│   ├── DefaultStrategy.cs
│   └── RoundRobinStrategy.cs
├── Configuration/
│   ├── LoadBalancingOptions.cs
│   ├── ResilienceOptions.cs
│   └── RefreshPolicy.cs
├── Infrastructure/
│   ├── RefreshOnErrorInterceptor.cs
│   ├── ClusterResolver.cs
│   ├── ClusterResolverFactory.cs
│   ├── ClusterLoadBalancer.cs
│   ├── ClusterLoadBalancerFactory.cs
│   ├── ClusterPicker.cs
│   └── ClusterNodeAttributes.cs
├── Builder/
│   ├── LoadBalancingBuilder.cs
│   ├── LoadBalancingServiceBuilder.cs
│   └── GrpcLoadBalancedChannel.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs
└── Exceptions/
    └── DiscoveryException.cs
```

### Tasks

#### Phase 1: Core Model

- [ ] **Task 1.1:** Create `NodeRole.cs` enum
- [ ] **Task 1.2:** Create `NodeStatus.cs` enum
- [ ] **Task 1.3:** Create `ClusterNode.cs` readonly record struct
- [ ] **Task 1.4:** Create `ClusterTopology.cs` sealed record with cached hash
- [ ] **Task 1.5:** Create `TopologyContext.cs` sealed record

#### Phase 2: Topology Sources

- [ ] **Task 2.1:** Create `TopologySource.cs` abstract base
- [ ] **Task 2.2:** Create `PollingTopologySource.cs` abstract class with polling loop

#### Phase 3: Picking Strategy

- [ ] **Task 3.1:** Create `SubchannelNode.cs` readonly record struct
- [ ] **Task 3.2:** Create `PickNodeContext.cs` readonly record struct
- [ ] **Task 3.3:** Create `PickNodeResult.cs` readonly struct with factory methods
- [ ] **Task 3.4:** Create `IClusterNodePickingStrategy.cs` interface
- [ ] **Task 3.5:** Create `LeaderOnlyStrategy.cs` (routes all to leader)
- [ ] **Task 3.6:** Create `DefaultStrategy.cs` (writes to leader, reads round-robin)
- [ ] **Task 3.7:** Create `RoundRobinStrategy.cs` (round-robin all eligible)

#### Phase 4: Configuration

- [ ] **Task 4.1:** Create `LoadBalancingOptions.cs` (JSON-serializable)
- [ ] **Task 4.2:** Create `ResilienceOptions.cs` (JSON-serializable)
- [ ] **Task 4.3:** Create `RefreshPolicy.cs` delegate and all helper methods

#### Phase 5: gRPC Infrastructure

- [ ] **Task 5.1:** Create `RefreshOnErrorInterceptor.cs` gRPC Interceptor
- [ ] **Task 5.2:** Create `ClusterNodeAttributes.cs` with `Node`, `NodeId`, `Role`, `Status` keys
- [ ] **Task 5.3:** Create `ClusterResolver.cs` with retry loop and working set
- [ ] **Task 5.4:** Create `ClusterResolverFactory.cs` (with useTls parameter)
- [ ] **Task 5.5:** Create `ClusterLoadBalancer.cs` (uses `IClusterNodePickingStrategy`)
- [ ] **Task 5.6:** Create `ClusterLoadBalancerFactory.cs` (accepts `IClusterNodePickingStrategy`)
- [ ] **Task 5.7:** Create `ClusterPicker.cs` (wraps strategy, builds context from subchannels)

#### Phase 6: Builder Pattern

- [ ] **Task 6.1:** Create `LoadBalancingBuilder.cs` (non-DI) with `WithPickingStrategy()`
- [ ] **Task 6.2:** Create `GrpcLoadBalancedChannel.cs` entry point
- [ ] **Task 6.3:** Create `LoadBalancingServiceBuilder.cs` (DI) with `WithPickingStrategy<T>()`
- [ ] **Task 6.4:** Create `ServiceCollectionExtensions.cs`

#### Phase 7: Exceptions & Project

- [ ] **Task 7.1:** Create `DiscoveryException.cs`
- [ ] **Task 7.2:** Create `Kurrent.Grpc.LoadBalancing.csproj`

### Acceptance Criteria

#### Core Model
- [ ] **AC 1:** `ClusterNode.IsEligible` returns true only for Ready/Isolated status
- [ ] **AC 2:** `ClusterNode.CanAcceptWrites` returns true only for Leader + Ready
- [ ] **AC 3:** `ClusterNode.CanServeReads` returns true only for Ready/Isolated status

#### Picking Strategy
- [ ] **AC 4:** `IClusterNodePickingStrategy.Pick()` receives `SubchannelNode[]` and `HttpRequestMessage`
- [ ] **AC 5:** `PickNodeResult.NoLeader()` returns correct failure status and message
- [ ] **AC 6:** `PickNodeResult.ForNode()` correctly extracts subchannel from `SubchannelNode`
- [ ] **AC 7:** `LeaderOnlyStrategy` returns `NoLeader()` when no leader present
- [ ] **AC 8:** `DefaultStrategy` routes writes to leader, reads to followers
- [ ] **AC 9:** `RoundRobinStrategy` cycles through all eligible nodes

#### gRPC Infrastructure
- [ ] **AC 10:** `ClusterPicker` builds `SubchannelNode[]` at construction (cold path)
- [ ] **AC 11:** `ClusterPicker.Pick()` delegates to strategy and converts result
- [ ] **AC 12:** `ClusterNodeAttributes.Node` stores full `ClusterNode` for efficient extraction
- [ ] **AC 13:** `ClusterLoadBalancer` creates picker with strategy
- [ ] **AC 14:** `ClusterResolver` manages working set (discovered nodes → seeds fallback)
- [ ] **AC 15:** `ClusterResolver` shuffles endpoints for load distribution

#### Configuration
- [ ] **AC 16:** `RefreshPolicy.OnStatusCodes()` triggers refresh on matching codes
- [ ] **AC 17:** `RefreshPolicy.Any()` combines policies with OR logic
- [ ] **AC 18:** JSON configuration binds correctly to options classes

#### Builder Pattern
- [ ] **AC 19:** Non-DI builder creates working GrpcChannel with strategy
- [ ] **AC 20:** Non-DI builder defaults to `DefaultStrategy.Instance`
- [ ] **AC 21:** DI builder registers `IClusterNodePickingStrategy` in container
- [ ] **AC 22:** DI builder supports both type and instance strategy registration

#### Error Handling
- [ ] **AC 23:** `DiscoveryException` thrown after `MaxDiscoveryAttempts`

---

## Additional Context

### Dependencies

**NuGet Packages:**

| Package | Version | Purpose |
|---------|---------|---------|
| `Grpc.Net.Client` | >= 2.60.0 | gRPC client with Resolver/LoadBalancer support |
| `Microsoft.Extensions.Configuration.Binder` | >= 8.0.0 | IConfiguration binding |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | >= 8.0.0 | DI abstractions |
| `Microsoft.Extensions.Logging.Abstractions` | >= 8.0.0 | ILogger abstractions |
| `System.Collections.Immutable` | >= 8.0.0 | ImmutableDictionary for Metadata |
| `System.Threading.Channels` | >= 8.0.0 | Channel<T> for topology streaming |

### Testing Strategy

**Unit Tests:**

| Component | Test Focus |
|-----------|------------|
| `ClusterNode` | Eligibility properties (IsEligible, CanServeReads, CanAcceptWrites) |
| `ClusterTopology` | GetHashCode caching, filter methods |
| `PollingTopologySource` | Polling loop behavior, cancellation |
| `ClusterResolver` | Working set management (discovered nodes → seeds fallback) |
| `ClusterResolver` | Endpoint shuffling for load distribution |
| `RefreshPolicy` | All policy combinations (OnStatusCodes, Any, All, etc.) |
| `RefreshOnErrorInterceptor` | RpcException triggers refresh via policy |
| `LoadBalancingBuilder` | Validation, Build() output |

**Picking Strategy Tests:**

| Component | Test Focus |
|-----------|------------|
| `SubchannelNode` | Record struct equality, default value handling |
| `PickNodeContext` | Context creation with nodes and request |
| `PickNodeResult` | All factory methods (ForNode, NoLeader, Defer, Drop, Fail) |
| `PickNodeResult.ToPickResult()` | Correct mapping to gRPC PickResult |
| `LeaderOnlyStrategy` | Returns NoLeader when no leader, ForNode when leader exists |
| `DefaultStrategy` | Write routing to leader, read round-robin to followers |
| `RoundRobinStrategy` | Even distribution across eligible nodes |
| `ClusterPicker` | Context building, strategy delegation, result conversion |

**Integration Tests:**

| Scenario | Description |
|----------|-------------|
| Full discovery flow | Mock TopologySource → ClusterResolver → ClusterPicker |
| Strategy integration | Custom strategy → ClusterPicker → PickResult |
| DI registration | Verify all services registered correctly including strategy |
| Configuration binding | appsettings.json → LoadBalancingOptions |
| Refresh on error | RpcException triggers RefreshOnErrorInterceptor |

**Benchmarks:**

| Benchmark | Target |
|-----------|--------|
| `ClusterPicker.Pick()` | 0 bytes allocated, < 50ns |
| `IClusterNodePickingStrategy.Pick()` | 0 bytes allocated (for built-in strategies) |
| `ClusterTopology.GetHashCode()` | Cached after first call |

### Usage Examples

**Non-DI Usage:**

```csharp
// Minimal (uses DefaultStrategy)
var channel = GrpcLoadBalancedChannel.ForAddress("cluster:///my-cluster", builder => builder
    .WithSeeds("node1:2113", "node2:2113", "node3:2113")
    .WithPollingTopologySource(new MyTopologySource(TimeSpan.FromSeconds(30)))
);

// With leader-only strategy
var channel = GrpcLoadBalancedChannel.ForAddress("cluster:///my-cluster", builder => builder
    .WithSeeds("node1:2113", "node2:2113", "node3:2113")
    .WithPollingTopologySource(new MyTopologySource(TimeSpan.FromSeconds(30)))
    .WithPickingStrategy(LeaderOnlyStrategy.Instance)
);

// Full configuration
var channel = GrpcLoadBalancedChannel.ForAddress("cluster:///my-cluster", builder => builder
    .WithSeeds("node1:2113", "node2:2113", "node3:2113")
    .UseTls()
    .WithResilience(r => {
        r.Timeout = TimeSpan.FromSeconds(10);
        r.MaxDiscoveryAttempts = 5;
    })
    .WithPollingTopologySource(new MyTopologySource(TimeSpan.FromSeconds(30)))
    .WithPickingStrategy(new DefaultStrategy("Append", "Delete", "Write"))
    .WithRefreshPolicy(RefreshPolicy.Any(
        RefreshPolicy.OnStatusCodes(StatusCode.Unavailable),
        RefreshPolicy.OnMessageContains("not-leader")
    ))
    .WithLoggerFactory(loggerFactory)
);
```

**Custom Picking Strategy:**

```csharp
// Custom strategy that routes to read-only replicas for specific methods
public sealed class CustomStrategy : IClusterNodePickingStrategy {
    public PickNodeResult Pick(in PickNodeContext context) {
        var method = context.Request.RequestUri?.Segments.LastOrDefault() ?? "";

        // Analytics queries go to read-only replicas
        if (method.StartsWith("Analytics", StringComparison.OrdinalIgnoreCase)) {
            var readOnly = context.Nodes
                .FirstOrDefault(n => n.Node.Role == NodeRole.ReadOnly && n.Node.IsEligible);
            if (readOnly != default)
                return PickNodeResult.ForNode(readOnly);
        }

        // Everything else goes to leader
        var leader = context.Nodes.FirstOrDefault(n => n.Node.CanAcceptWrites);
        return leader == default
            ? PickNodeResult.NoLeader()
            : PickNodeResult.ForNode(leader);
    }
}

// Usage
var channel = GrpcLoadBalancedChannel.ForAddress("cluster:///my-cluster", builder => builder
    .WithSeeds("node1:2113", "node2:2113")
    .WithStreamingTopologySource(new MyStreamingSource())
    .WithPickingStrategy(new CustomStrategy())
);
```

**DI Usage:**

```csharp
// In Program.cs (uses DefaultStrategy)
services.AddGrpcLoadBalancing("my-cluster")
    .WithSeeds("node1:2113", "node2:2113", "node3:2113")
    .WithPollingTopologySource<MyTopologySource>()
    .UseTls()
    .Build();

// With custom strategy via DI
services.AddGrpcLoadBalancing("my-cluster")
    .WithSeeds("node1:2113", "node2:2113", "node3:2113")
    .WithPollingTopologySource<MyTopologySource>()
    .WithPickingStrategy<CustomStrategy>()
    .Build();

// With strategy instance
services.AddGrpcLoadBalancing("my-cluster")
    .WithSeeds("node1:2113", "node2:2113", "node3:2113")
    .WithPollingTopologySource<MyTopologySource>()
    .WithPickingStrategy(LeaderOnlyStrategy.Instance)
    .Build();

// From configuration
services.AddGrpcLoadBalancing("my-cluster", configuration.GetSection("LoadBalancing"))
    .WithPollingTopologySource<MyTopologySource>()
    .Build();

// Inject and use
public class MyService {
    readonly GrpcChannel _channel;
    public MyService(GrpcChannel channel) => _channel = channel;
}
```

### Notes

- This spec covers ONLY the generic framework (`Kurrent.Grpc.LoadBalancing`)
- KurrentDB-specific implementation (`Kurrent.Client.LoadBalancing`) requires a **separate spec**
- Zero-allocation `Pick()` is a **HARD requirement** - must be verified via benchmark
- The Two-Axis model (Role + Status) is the core abstraction - do not deviate
- All configuration options must be JSON-serializable for `IConfiguration` binding
- Legacy gossip behavior documented in `reference-legacy-gossip-discovery.md`
