// // =============================================================================
// // Kurrent gRPC Client-Side Load Balancing
// // =============================================================================
// // Custom resolver and load balancer for cluster-aware gRPC routing.
// // Streaming resolver with real-time topology updates via TopologySource.
// // Full control over node selection via IClusterNodePickingStrategy.
// // =============================================================================
//
// using System.Buffers;
// using System.Collections.Concurrent;
// using System.Collections.Immutable;
// using System.Net;
// using System.Runtime.CompilerServices;
// using System.Threading.Channels;
// using Grpc.Core;
// using Grpc.Core.Interceptors;
// using Grpc.Net.Client;
// using Grpc.Net.Client.Balancer;
// using Grpc.Net.Client.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Logging.Abstractions;
// using Microsoft.Extensions.Options;
// using Polly;
//
// namespace Kurrent.Grpc.LoadBalancingV2;
//
// #region Domain Model
//
// /// <summary>
// /// Represents the functional role a node is AUTHORIZED to perform.
// /// This is the "what" axis - what the node can do.
// /// </summary>
// public enum NodeRole {
//     /// <summary>No defined role yet (initializing, unknown).</summary>
//     None = 0,
//
//     /// <summary>Authoritative for writes and coordination (primary/master).</summary>
//     Leader = 1,
//
//     /// <summary>Voting member that replicates data (secondary/replica).</summary>
//     Follower = 2,
//
//     /// <summary>Non-voting, read-scaling member (read replica).</summary>
//     ReadOnly = 3,
//
//     /// <summary>Administrative/orchestration only (not for client traffic).</summary>
//     Manager = 4,
//
//     /// <summary>Leader handing off authority (stepping down).</summary>
//     Resigning = 5
// }
//
// /// <summary>
// /// Represents the node's synchronization HEALTH and operational progress.
// /// This is the "how" axis - how the node is performing.
// /// </summary>
// public enum NodeStatus {
//     /// <summary>Booting or discovering peers.</summary>
//     Starting = 0,
//
//     /// <summary>Catching up on logs (replicating data).</summary>
//     Syncing = 1,
//
//     /// <summary>Fully operational and current.</summary>
//     Ready = 2,
//
//     /// <summary>Healthy but lost quorum contact (can serve stale reads).</summary>
//     Isolated = 3,
//
//     /// <summary>Graceful shutdown in progress.</summary>
//     Stopping = 4,
//
//     /// <summary>Shutdown complete.</summary>
//     Stopped = 5
// }
//
// /// <summary>
// /// Protocol-agnostic representation of a cluster node.
// /// Separates "Role" (Authority) from "Status" (Health).
// /// </summary>
// public readonly record struct ClusterNode {
//     /// <summary>Unique identifier for this node instance.</summary>
//     public required Guid NodeId { get; init; }
//
//     /// <summary>Network endpoint for connecting to this node.</summary>
//     public required DnsEndPoint EndPoint { get; init; }
//
//     /// <summary>The functional role this node is authorized to perform.</summary>
//     public required NodeRole Role { get; init; }
//
//     /// <summary>The node's current health and synchronization status.</summary>
//     public required NodeStatus Status { get; init; }
//
//     /// <summary>
//     /// Protocol-specific metadata (e.g., original state enum, term, epoch).
//     /// </summary>
//     public ImmutableDictionary<string, object> Metadata { get; init; }
//
//     /// <summary>
//     /// Creates a new ClusterNode.
//     /// </summary>
//     public ClusterNode() {
//         Metadata = ImmutableDictionary<string, object>.Empty;
//     }
//
//     /// <summary>
//     /// Can this node serve read requests?
//     /// True when node is Ready or Isolated (stale reads acceptable).
//     /// </summary>
//     public bool CanServeReads =>
//         Status is NodeStatus.Ready or NodeStatus.Isolated;
//
//     /// <summary>
//     /// Can this node accept write requests?
//     /// True only when node is Leader AND Ready.
//     /// </summary>
//     public bool CanAcceptWrites =>
//         Role == NodeRole.Leader && Status == NodeStatus.Ready;
//
//     /// <summary>
//     /// Is this node eligible for client traffic?
//     /// True when node is Ready or Isolated.
//     /// </summary>
//     public bool IsEligible =>
//         Status is NodeStatus.Ready or NodeStatus.Isolated;
//
//     public override string ToString() =>
//         $"{EndPoint} [{Role}/{Status}] (Id={NodeId:N})";
// }
//
// #endregion
//
// #region Topology Source
//
// /// <summary>
// /// Context provided to the TopologySource for cluster discovery.
// /// </summary>
// /// <param name="Channel">A gRPC channel connected to the current seed endpoint for discovery calls.</param>
// /// <param name="Endpoint">The current endpoint being used (useful for logging/debugging).</param>
// /// <param name="CancellationToken">Cancellation token that signals when the resolver is being disposed.</param>
// public readonly record struct TopologyContext(
//     ChannelBase Channel,
//     DnsEndPoint Endpoint,
//     CancellationToken CancellationToken);
//
// /// <summary>
// /// Represents a snapshot of the cluster topology with pre-computed balancer addresses.
// /// Create instances of this and write them to the channel when topology changes.
// /// </summary>
// public readonly struct ClusterTopology {
//     /// <summary>
//     /// The cluster nodes in this topology snapshot.
//     /// </summary>
//     public ClusterNode[] Nodes { get; }
//
//     /// <summary>
//     /// Pre-computed balancer addresses for the resolver.
//     /// Created once during construction for zero-allocation topology updates.
//     /// </summary>
//     internal BalancerAddress[] Addresses { get; }
//
//     /// <summary>
//     /// Creates a topology snapshot with pre-computed balancer addresses.
//     /// Only eligible nodes (Ready or Isolated) will have addresses created.
//     /// Each address has its ClusterNode attached via BalancerAttributes.
//     /// </summary>
//     /// <param name="nodes">All nodes in the cluster.</param>
//     public ClusterTopology(params ClusterNode[] nodes) {
//         Nodes = nodes;
//
//         // Count eligible nodes first to avoid resize allocations
//         var eligibleCount = 0;
//         foreach (var node in nodes) {
//             if (node.IsEligible)
//                 eligibleCount++;
//         }
//
//         // Create addresses with ClusterNode attached via UnsafeAccessor
//         var addresses = new BalancerAddress[eligibleCount];
//         var index = 0;
//         foreach (var node in nodes) {
//             if (node.IsEligible)
//                 addresses[index++] = BalancerAddressExtensions.CreateWithNode(node);
//         }
//
//         Addresses = addresses;
//     }
//
//     /// <summary>
//     /// Creates a topology from an enumerable of nodes.
//     /// </summary>
//     public ClusterTopology(IEnumerable<ClusterNode> nodes)
//         : this(nodes.ToArray()) {
//     }
//
//     /// <summary>
//     /// An empty topology with no nodes.
//     /// </summary>
//     public static ClusterTopology Empty =>
//         new([]);
//
//     /// <summary>
//     /// Number of eligible nodes in this topology.
//     /// </summary>
//     public int EligibleCount =>
//         Addresses.Length;
//
//     /// <summary>
//     /// Total number of nodes in this topology.
//     /// </summary>
//     public int TotalCount =>
//         Nodes.Length;
//
//     public override string ToString() =>
//         $"ClusterTopology({EligibleCount}/{TotalCount} eligible)";
// }
//
// /// <summary>
// /// Abstract base for topology discovery implementations.
// /// Implement <see cref="RunAsync"/> to provide real-time cluster topology updates.
// /// The infrastructure handles retries, seed rotation, and channel management.
// /// </summary>
// public abstract class TopologySource : IAsyncDisposable {
//     /// <summary>
//     /// Runs the topology discovery, writing updates to the channel.
//     /// This method should stream topology updates until cancellation or failure.
//     /// If the stream ends or throws, the infrastructure will retry with potentially a different seed.
//     /// </summary>
//     /// <param name="writer">Channel writer to publish topology updates.</param>
//     /// <param name="context">Context containing the channel to use for discovery calls.</param>
//     public abstract Task RunAsync(ChannelWriter<ClusterTopology> writer, TopologyContext context);
//
//     /// <summary>
//     /// Disposes of any resources used by the topology source.
//     /// </summary>
//     public virtual ValueTask DisposeAsync() {
//         return default;
//     }
//
//     /// <summary>
//     /// Called when a refresh is requested (e.g., after an RPC failure).
//     /// Override to handle refresh requests for polling sources.
//     /// Streaming sources can typically ignore this.
//     /// </summary>
//     public virtual void RequestRefresh() { }
// }
//
// /// <summary>
// /// A topology source that polls for updates at a fixed interval.
// /// Override <see cref="FetchTopologyAsync"/> to implement the polling logic.
// /// </summary>
// public abstract class PollingTopologySource : TopologySource {
//     readonly TimeSpan _interval;
//     readonly SemaphoreSlim _refreshSignal = new(0, 1);
//
//     /// <summary>
//     /// Creates a polling topology source with the specified interval.
//     /// </summary>
//     /// <param name="interval">Time between polls.</param>
//     protected PollingTopologySource(TimeSpan interval) {
//         _interval = interval;
//     }
//
//     /// <summary>
//     /// Fetches the current topology from the cluster.
//     /// Called on each poll interval.
//     /// </summary>
//     /// <param name="context">Context containing the channel to use for discovery calls.</param>
//     /// <returns>The current cluster topology.</returns>
//     protected abstract Task<ClusterTopology> FetchTopologyAsync(TopologyContext context);
//
//     /// <summary>
//     /// Runs the polling loop, fetching topology at each interval.
//     /// </summary>
//     public override async Task RunAsync(ChannelWriter<ClusterTopology> writer, TopologyContext context) {
//         while (!context.CancellationToken.IsCancellationRequested) {
//             var topology = await FetchTopologyAsync(context);
//             await writer.WriteAsync(topology, context.CancellationToken);
//
//             // Wait for interval OR refresh signal, whichever comes first
//             try {
//                 await _refreshSignal.WaitAsync(_interval, context.CancellationToken);
//             }
//             catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) {
//                 break;
//             }
//         }
//     }
//
//     /// <summary>
//     /// Requests an immediate refresh, interrupting the current wait interval.
//     /// </summary>
//     public override void RequestRefresh() {
//         // Release the semaphore if it's not already signaled
//         if (_refreshSignal.CurrentCount == 0) {
//             try {
//                 _refreshSignal.Release();
//             }
//             catch (SemaphoreFullException) {
//                 // Already signaled, ignore
//             }
//         }
//     }
//
//     public override ValueTask DisposeAsync() {
//         _refreshSignal.Dispose();
//         return default;
//     }
// }
//
// #endregion
//
// #region Topology Refresh
//
// /// <summary>
// /// Delegate that determines whether a failed RPC should trigger a topology refresh.
// /// </summary>
// /// <param name="status">The gRPC status code of the failed RPC.</param>
// /// <param name="trailers">The trailing metadata from the failed RPC.</param>
// /// <returns>True if a refresh should be triggered.</returns>
// public delegate bool ShouldRefreshTopology(StatusCode status, Metadata trailers);
//
// /// <summary>
// /// Interceptor that triggers topology refresh on RPC failures matching a predicate.
// /// </summary>
// public sealed class TopologyRefreshInterceptor : Interceptor {
//     readonly Action _requestRefresh;
//     readonly ShouldRefreshTopology _shouldRefresh;
//
//     internal TopologyRefreshInterceptor(Action requestRefresh, ShouldRefreshTopology shouldRefresh) {
//         _requestRefresh = requestRefresh;
//         _shouldRefresh = shouldRefresh;
//     }
//
//     public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
//         TRequest request,
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncUnaryCallContinuation<TRequest, TResponse> continuation) {
//         var call = continuation(request, context);
//
//         return new AsyncUnaryCall<TResponse>(
//             HandleResponse(call.ResponseAsync),
//             call.ResponseHeadersAsync,
//             call.GetStatus,
//             call.GetTrailers,
//             call.Dispose);
//     }
//
//     public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation) {
//         var call = continuation(context);
//
//         return new AsyncClientStreamingCall<TRequest, TResponse>(
//             call.RequestStream,
//             HandleResponse(call.ResponseAsync),
//             call.ResponseHeadersAsync,
//             call.GetStatus,
//             call.GetTrailers,
//             call.Dispose);
//     }
//
//     public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
//         TRequest request,
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) {
//         var call = continuation(request, context);
//
//         return new AsyncServerStreamingCall<TResponse>(
//             new RefreshOnErrorStreamReader<TResponse>(call.ResponseStream, _requestRefresh, _shouldRefresh),
//             call.ResponseHeadersAsync,
//             call.GetStatus,
//             call.GetTrailers,
//             call.Dispose);
//     }
//
//     public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation) {
//         var call = continuation(context);
//
//         return new AsyncDuplexStreamingCall<TRequest, TResponse>(
//             call.RequestStream,
//             new RefreshOnErrorStreamReader<TResponse>(call.ResponseStream, _requestRefresh, _shouldRefresh),
//             call.ResponseHeadersAsync,
//             call.GetStatus,
//             call.GetTrailers,
//             call.Dispose);
//     }
//
//     async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> responseTask) {
//         try {
//             return await responseTask.ConfigureAwait(false);
//         }
//         catch (RpcException ex) when (_shouldRefresh(ex.StatusCode, ex.Trailers)) {
//             _requestRefresh();
//             throw;
//         }
//     }
//
//     sealed class RefreshOnErrorStreamReader<T>(
//         IAsyncStreamReader<T> inner,
//         Action requestRefresh,
//         ShouldRefreshTopology shouldRefresh) : IAsyncStreamReader<T> {
//
//         public T Current => inner.Current;
//
//         public async Task<bool> MoveNext(CancellationToken cancellationToken) {
//             try {
//                 return await inner.MoveNext(cancellationToken).ConfigureAwait(false);
//             }
//             catch (RpcException ex) when (shouldRefresh(ex.StatusCode, ex.Trailers)) {
//                 requestRefresh();
//                 throw;
//             }
//         }
//     }
// }
//
// #endregion
//
// #region Channel Pool
//
// /// <summary>
// /// Manages a pool of reusable gRPC channels for bootstrap/discovery connections.
// /// Channels are cached by endpoint and can be invalidated on failure.
// /// </summary>
// internal sealed class ChannelPool(Func<DnsEndPoint, GrpcChannel> factory) : IDisposable {
//     readonly ConcurrentDictionary<DnsEndPoint, ChannelSlot> _slots = new();
//     int _disposed;
//
//     public GrpcChannel GetChannel(DnsEndPoint endpoint) {
//         var slot = _slots.GetOrAdd(endpoint, _ => new ChannelSlot());
//         return slot.GetOrCreate(endpoint, factory);
//     }
//
//     public void Invalidate(DnsEndPoint endpoint) {
//         if (_slots.TryGetValue(endpoint, out var slot))
//             slot.Invalidate();
//     }
//
//     public void Dispose() {
//         if (Interlocked.Exchange(ref _disposed, 1) == 1)
//             return;
//
//         foreach (var slot in _slots.Values)
//             slot.Dispose();
//
//         _slots.Clear();
//     }
//
//     sealed class ChannelSlot : IDisposable {
//         GrpcChannel? _channel;
// #if NET9_0_OR_GREATER
//         readonly Lock _lock = new();
// #else
//         readonly object _lock = new();
// #endif
//
//         public GrpcChannel GetOrCreate(DnsEndPoint endpoint, Func<DnsEndPoint, GrpcChannel> factory) {
//             lock (_lock)
//                 return _channel ??= factory(endpoint);
//         }
//
//         public void Invalidate() {
//             lock (_lock) {
//                 _channel?.Dispose();
//                 _channel = null;
//             }
//         }
//
//         public void Dispose() =>
//             Invalidate();
//     }
// }
//
// #endregion
//
// #region BalancerAttributes Extensions
//
// /// <summary>
// /// Provides auto-generated keys for attribute types.
// /// One key instance per type, lazily created.
// /// </summary>
// internal static class AttributeKey<T> {
//     public static readonly BalancerAttributesKey<T> Instance = new(typeof(T).FullName!);
// }
//
// /// <summary>
// /// Extension methods for <see cref="BalancerAttributes"/> to provide a cleaner API.
// /// </summary>
// internal static class BalancerAttributesExtensions {
//     extension(BalancerAttributes attributes) {
//         #region Explicit key overloads
//
//         /// <summary>
//         /// Gets the value associated with the specified key, or default if not found.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public TValue? Get<TValue>(BalancerAttributesKey<TValue> key) {
//             return attributes.TryGetValue(key, out var value) ? value : default;
//         }
//
//         /// <summary>
//         /// Gets the value associated with the specified key, or the specified default if not found.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public TValue GetOrDefault<TValue>(BalancerAttributesKey<TValue> key, TValue defaultValue) {
//             return attributes.TryGetValue(key, out var value) ? value : defaultValue;
//         }
//
//         #endregion
//
//         #region Auto-keyed overloads (key derived from type)
//
//         /// <summary>
//         /// Gets the value of type T using an auto-generated key.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public T? Get<T>() {
//             return attributes.TryGetValue(AttributeKey<T>.Instance, out var value) ? value : default;
//         }
//
//         /// <summary>
//         /// Sets a value using an auto-generated key derived from the value's type.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public void Set<T>(T value) {
//             attributes.Set(AttributeKey<T>.Instance, value);
//         }
//
//         #endregion
//     }
// }
//
// #endregion
//
// #region BalancerAddress Extensions
//
// /// <summary>
// /// Extension methods for <see cref="BalancerAddress"/> to work around API limitations.
// /// Uses UnsafeAccessor for AOT-compatible, zero-overhead access to the private _attributes field.
// /// </summary>
// internal static class BalancerAddressExtensions {
//     /// <summary>
//     /// Gets a ref to the private _attributes field of BalancerAddress.
//     /// This is AOT-safe and has zero overhead compared to direct field access.
//     /// </summary>
//     [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_attributes")]
//     static extern ref BalancerAttributes? GetAttributesRef(BalancerAddress address);
//
//     extension(BalancerAddress address) {
//         /// <summary>
//         /// Sets the attributes collection on a BalancerAddress.
//         /// </summary>
//         public void SetAttributes(BalancerAttributes attributes) {
//             GetAttributesRef(address) = attributes;
//         }
//
//         /// <summary>
//         /// Gets or creates the attributes collection for a BalancerAddress.
//         /// </summary>
//         public BalancerAttributes GetOrCreateAttributes() {
//             ref var attributes = ref GetAttributesRef(address);
//             return attributes ??= new BalancerAttributes();
//         }
//
//         #region Explicit key overloads
//
//         /// <summary>
//         /// Gets an attribute value from the address.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public TValue? GetAttribute<TValue>(BalancerAttributesKey<TValue> key) {
//             return address.Attributes.Get(key);
//         }
//
//         /// <summary>
//         /// Sets an attribute value on the address.
//         /// </summary>
//         public void SetAttribute<TValue>(BalancerAttributesKey<TValue> key, TValue value) {
//             address.GetOrCreateAttributes().Set(key, value);
//         }
//
//         #endregion
//
//         #region Auto-keyed overloads (key derived from type)
//
//         /// <summary>
//         /// Gets an attribute value using an auto-generated key.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public T? GetAttribute<T>() {
//             return address.Attributes.Get<T>();
//         }
//
//         /// <summary>
//         /// Sets an attribute value using an auto-generated key derived from the value's type.
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public void SetAttribute<T>(T value) {
//             address.GetOrCreateAttributes().Set(value);
//         }
//
//         #endregion
//     }
//
//     /// <summary>
//     /// Creates a BalancerAddress with the ClusterNode attached as an attribute.
//     /// </summary>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static BalancerAddress CreateWithNode(ClusterNode node) {
//         var address = new BalancerAddress(node.EndPoint.Host, node.EndPoint.Port);
//         address.SetAttribute(node);
//         return address;
//     }
// }
//
// #endregion
//
// #region Picking Strategy Types
//
// /// <summary>
// /// Pairs a gRPC Subchannel with its corresponding ClusterNode.
// /// </summary>
// public readonly record struct SubchannelNode(Subchannel Subchannel, ClusterNode Node);
//
// /// <summary>
// /// Context passed to the picking strategy for each RPC.
// /// </summary>
// public readonly ref struct PickNodeContext {
//     public ReadOnlySpan<SubchannelNode> Nodes { get; }
//     public HttpRequestMessage Request { get; }
//
//     public PickNodeContext(ReadOnlySpan<SubchannelNode> nodes, HttpRequestMessage request) {
//         Nodes = nodes;
//         Request = request;
//     }
// }
//
// /// <summary>
// /// Result of a node picking operation.
// /// Wraps gRPC's PickResult with better naming and common failure cases.
// /// </summary>
// public readonly struct PickNodeResult {
//     readonly Subchannel? _subchannel;
//     readonly Status? _status;
//     readonly PickNodeResultType _type;
//
//     PickNodeResult(PickNodeResultType type, Subchannel? subchannel, Status? status) {
//         _type = type;
//         _subchannel = subchannel;
//         _status = status;
//     }
//
//     enum PickNodeResultType : byte {
//         Subchannel,
//         Fail,
//         Drop,
//         Defer
//     }
//
//     #region Success
//
//     /// <summary>
//     /// Successfully picked a subchannel. The RPC will proceed using this connection.
//     /// </summary>
//     public static PickNodeResult ForSubchannel(Subchannel subchannel) =>
//         new(PickNodeResultType.Subchannel, subchannel, null);
//
//     /// <summary>
//     /// Successfully picked a node. Convenience overload that extracts the subchannel.
//     /// </summary>
//     public static PickNodeResult ForNode(SubchannelNode node) =>
//         new(PickNodeResultType.Subchannel, node.Subchannel, null);
//
//     #endregion
//
//     #region Defer
//
//     /// <summary>
//     /// Defer the pick - the RPC will wait for a new picker to become available.
//     /// Use this for transient unavailability (e.g., cluster starting up, leader election in progress).
//     /// </summary>
//     public static PickNodeResult Defer() =>
//         new(PickNodeResultType.Defer, null, null);
//
//     /// <summary>
//     /// Defer the pick - the RPC will wait for a new picker to become available.
//     /// Alias for <see cref="Defer"/> with a more descriptive name.
//     /// </summary>
//     public static PickNodeResult WaitForReady() =>
//         Defer();
//
//     #endregion
//
//     #region Drop
//
//     /// <summary>
//     /// Drop the RPC immediately. Unlike Fail, this bypasses the retry policy.
//     /// Use for rate limiting, circuit breaking, or when retries would be harmful.
//     /// </summary>
//     public static PickNodeResult Drop(string reason) =>
//         new(PickNodeResultType.Drop, null, new Status(StatusCode.Unavailable, reason));
//
//     /// <summary>
//     /// Drop the RPC immediately with a custom status code.
//     /// </summary>
//     public static PickNodeResult Drop(StatusCode code, string reason) =>
//         new(PickNodeResultType.Drop, null, new Status(code, reason));
//
//     #endregion
//
//     #region Common Failures
//
//     // Pre-allocated common failure results to avoid allocations in hot path
//     static readonly PickNodeResult NoLeaderResult =
//         new(PickNodeResultType.Fail, null, new Status(StatusCode.Unavailable, "No leader available for write operations"));
//
//     static readonly PickNodeResult NoFollowersResult =
//         new(PickNodeResultType.Fail, null, new Status(StatusCode.Unavailable, "No followers available for read operations"));
//
//     static readonly PickNodeResult NoEligibleNodesResult =
//         new(PickNodeResultType.Fail, null, new Status(StatusCode.Unavailable, "No eligible nodes in cluster"));
//
//     static readonly PickNodeResult ClusterNotReadyResult =
//         new(PickNodeResultType.Fail, null, new Status(StatusCode.Unavailable, "Cluster is not ready"));
//
//     static readonly PickNodeResult NoNodesResult =
//         new(PickNodeResultType.Fail, null, new Status(StatusCode.Unavailable, "No nodes available"));
//
//     static readonly PickNodeResult SelectionTimeoutResult =
//         new(PickNodeResultType.Fail, null, new Status(StatusCode.DeadlineExceeded, "Node selection timed out"));
//
//     /// <summary>
//     /// No leader available for write operations.
//     /// </summary>
//     public static PickNodeResult NoLeader() =>
//         NoLeaderResult;
//
//     /// <summary>
//     /// No followers available for read operations.
//     /// </summary>
//     public static PickNodeResult NoFollowers() =>
//         NoFollowersResult;
//
//     /// <summary>
//     /// No eligible nodes in the cluster.
//     /// </summary>
//     public static PickNodeResult NoEligibleNodes() =>
//         NoEligibleNodesResult;
//
//     /// <summary>
//     /// Cluster is not ready to accept requests.
//     /// </summary>
//     public static PickNodeResult ClusterNotReady() =>
//         ClusterNotReadyResult;
//
//     /// <summary>
//     /// No nodes available (empty cluster).
//     /// </summary>
//     public static PickNodeResult NoNodes() =>
//         NoNodesResult;
//
//     /// <summary>
//     /// Node selection timed out.
//     /// </summary>
//     public static PickNodeResult SelectionTimeout() =>
//         SelectionTimeoutResult;
//
//     #endregion
//
//     #region Generic Failures
//
//     /// <summary>
//     /// Fail the RPC with a custom message and Unavailable status code.
//     /// </summary>
//     public static PickNodeResult Fail(string message) =>
//         Fail(StatusCode.Unavailable, message);
//
//     /// <summary>
//     /// Fail the RPC with a custom status code and message.
//     /// </summary>
//     public static PickNodeResult Fail(StatusCode code, string message) =>
//         new(PickNodeResultType.Fail, null, new Status(code, message));
//
//     /// <summary>
//     /// Fail the RPC with a gRPC Status.
//     /// </summary>
//     public static PickNodeResult Fail(Status status) =>
//         new(PickNodeResultType.Fail, null, status);
//
//     #endregion
//
//     #region Internal
//
//     /// <summary>
//     /// Convert to gRPC's PickResult. Used internally by the picker.
//     /// </summary>
//     internal PickResult ToPickResult() =>
//         _type switch {
//             PickNodeResultType.Subchannel => PickResult.ForSubchannel(_subchannel!),
//             PickNodeResultType.Defer      => PickResult.ForQueue(),
//             PickNodeResultType.Drop       => PickResult.ForDrop(_status!.Value),
//             PickNodeResultType.Fail       => PickResult.ForFailure(_status!.Value),
//             _                             => PickResult.ForFailure(new Status(StatusCode.Internal, "Invalid pick result"))
//         };
//
//     #endregion
// }
//
// /// <summary>
// /// Strategy for picking a cluster node for each RPC.
// /// Implement this interface to define custom routing logic.
// /// </summary>
// public interface IClusterNodePickingStrategy {
//     /// <summary>
//     /// Pick a node for the given RPC context.
//     /// Called on every RPC - must be fast and thread-safe.
//     /// </summary>
//     /// <param name="context">The picking context containing available nodes and the HTTP request.</param>
//     /// <returns>The pick result indicating which node to use or why the pick failed.</returns>
//     PickNodeResult Pick(in PickNodeContext context);
// }
//
// #endregion
//
// #region Built-in Strategies
//
// /// <summary>
// /// Routes all requests to the leader node.
// /// Use for strict consistency requirements.
// /// </summary>
// public sealed class LeaderPickingStrategy : IClusterNodePickingStrategy {
//     public static LeaderPickingStrategy Instance { get; } = new();
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public PickNodeResult Pick(in PickNodeContext context) {
//         var nodes = context.Nodes;
//         for (var i = 0; i < nodes.Length; i++) {
//             if (nodes[i].Node.CanAcceptWrites)
//                 return PickNodeResult.ForNode(nodes[i]);
//         }
//         return PickNodeResult.NoLeader();
//     }
// }
//
// /// <summary>
// /// Routes requests to any eligible node using round-robin.
// /// </summary>
// public sealed class RoundRobinPickingStrategy : IClusterNodePickingStrategy {
//     long _index;
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public PickNodeResult Pick(in PickNodeContext context) {
//         var nodes = context.Nodes;
//         if (nodes.Length == 0)
//             return PickNodeResult.NoNodes();
//
//         var index = (int)(Interlocked.Increment(ref _index) % nodes.Length);
//         return PickNodeResult.ForNode(nodes[index]);
//     }
// }
//
// /// <summary>
// /// Routes requests to any node that can serve reads, preferring followers over the leader.
// /// Falls back to leader if no followers are available.
// /// </summary>
// public sealed class PreferFollowersPickingStrategy : IClusterNodePickingStrategy {
//     long _index;
//
//     public PickNodeResult Pick(in PickNodeContext context) {
//         var nodes = context.Nodes;
//         if (nodes.Length == 0)
//             return PickNodeResult.NoNodes();
//
//         // First pass: count followers and find leader
//         var followerCount = 0;
//         var leaderIndex = -1;
//
//         for (var i = 0; i < nodes.Length; i++) {
//             ref readonly var node = ref nodes[i];
//             if (!node.Node.CanServeReads)
//                 continue;
//
//             if (node.Node.Role is NodeRole.Follower or NodeRole.ReadOnly)
//                 followerCount++;
//             else if (node.Node.Role == NodeRole.Leader)
//                 leaderIndex = i;
//         }
//
//         // If we have followers, round-robin among them
//         if (followerCount > 0) {
//             var targetIndex = (int)(Interlocked.Increment(ref _index) % followerCount);
//             var currentIndex = 0;
//
//             for (var i = 0; i < nodes.Length; i++) {
//                 ref readonly var node = ref nodes[i];
//                 if (!node.Node.CanServeReads)
//                     continue;
//
//                 if (node.Node.Role is not (NodeRole.Follower or NodeRole.ReadOnly))
//                     continue;
//
//                 if (currentIndex == targetIndex)
//                     return PickNodeResult.ForNode(node);
//
//                 currentIndex++;
//             }
//         }
//
//         // Fall back to leader
//         return leaderIndex >= 0
//             ? PickNodeResult.ForNode(nodes[leaderIndex])
//             : PickNodeResult.NoEligibleNodes();
//     }
// }
//
// /// <summary>
// /// Picks the first available node. Simple and fast.
// /// </summary>
// public sealed class FirstAvailablePickingStrategy : IClusterNodePickingStrategy {
//     public static FirstAvailablePickingStrategy Instance { get; } = new();
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public PickNodeResult Pick(in PickNodeContext context) {
//         var nodes = context.Nodes;
//         return nodes.Length > 0
//             ? PickNodeResult.ForNode(nodes[0])
//             : PickNodeResult.NoNodes();
//     }
// }
//
// /// <summary>
// /// Picks a random node from the available nodes.
// /// </summary>
// public sealed class RandomPickingStrategy : IClusterNodePickingStrategy {
//     public static RandomPickingStrategy Instance { get; } = new();
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public PickNodeResult Pick(in PickNodeContext context) {
//         var nodes = context.Nodes;
//         if (nodes.Length == 0)
//             return PickNodeResult.NoNodes();
//
//         var index = Random.Shared.Next(nodes.Length);
//         return PickNodeResult.ForNode(nodes[index]);
//     }
// }
//
// /// <summary>
// /// Delegate-based picking strategy for simple custom logic without implementing an interface.
// /// </summary>
// public sealed class DelegatePickingStrategy(PickerDelegate picker) : IClusterNodePickingStrategy {
//     public static DelegatePickingStrategy FromSelector(Func<SubchannelNode[], SubchannelNode?> selector) =>
//         new((in PickNodeContext ctx) => {
//             // Convert span to array for the delegate - unavoidable allocation for this convenience API
//             var array = ctx.Nodes.ToArray();
//             var selected = selector(array);
//             return selected.HasValue
//                 ? PickNodeResult.ForNode(selected.Value)
//                 : PickNodeResult.NoNodes();
//         });
//
//     public static DelegatePickingStrategy FromSelector(Func<SubchannelNode[], HttpRequestMessage, SubchannelNode?> selector) =>
//         new((in PickNodeContext ctx) => {
//             var array = ctx.Nodes.ToArray();
//             var selected = selector(array, ctx.Request);
//             return selected.HasValue
//                 ? PickNodeResult.ForNode(selected.Value)
//                 : PickNodeResult.NoNodes();
//         });
//
//     public PickNodeResult Pick(in PickNodeContext context) =>
//         picker(in context);
// }
//
// /// <summary>
// /// Delegate type for picking strategies.
// /// </summary>
// public delegate PickNodeResult PickerDelegate(in PickNodeContext context);
//
// #endregion
//
// #region Cluster Resolver
//
// /// <summary>
// /// Streaming resolver that receives real-time topology updates from a TopologySource.
// /// Handles retries, seed rotation, and channel management internally.
// /// </summary>
// internal sealed class ClusterResolver(
//     TopologySource source,
//     DnsEndPoint[] seeds,
//     ChannelPool channelPool,
//     ResiliencePipeline resiliencePipeline,
//     ILoggerFactory loggerFactory) : Resolver, IAsyncDisposable {
//
//     readonly ILogger _logger = loggerFactory.CreateLogger<ClusterResolver>();
//     readonly Channel<ClusterTopology> _topologyChannel = Channel.CreateBounded<ClusterTopology>(
//         new BoundedChannelOptions(1) {
//             FullMode = BoundedChannelFullMode.DropOldest,
//             SingleReader = true,
//             SingleWriter = false
//         });
//
//     Action<ResolverResult>? _listener;
//     CancellationTokenSource? _cts;
//     Task? _sourceTask;
//     Task? _readerTask;
//     int _disposed;
//
//     /// <summary>
//     /// Action that can be invoked to request a topology refresh.
//     /// Exposed for use by the interceptor.
//     /// </summary>
//     public Action RefreshAction => RequestRefresh;
//
//     public override void Start(Action<ResolverResult> listener) {
//         _listener = listener;
//         _cts = new CancellationTokenSource();
//
//         _sourceTask = RunSourceLoopAsync(_cts.Token);
//         _readerTask = RunReaderAsync(_cts.Token);
//
//         _logger.LogDebug("Cluster resolver started with {SeedCount} seeds", seeds.Length);
//     }
//
//     public override void Refresh() {
//         RequestRefresh();
//     }
//
//     void RequestRefresh() {
//         _logger.LogDebug("Topology refresh requested");
//         source.RequestRefresh();
//     }
//
//     async Task RunSourceLoopAsync(CancellationToken ct) {
//         var seedIndex = 0;
//
//         while (!ct.IsCancellationRequested) {
//             var endpoint = seeds[seedIndex % seeds.Length];
//
//             try {
//                 await resiliencePipeline.ExecuteAsync(
//                     async token => {
//                         var channel = channelPool.GetChannel(endpoint);
//                         var context = new TopologyContext(channel, endpoint, token);
//
//                         _logger.LogDebug("Starting topology source for {Endpoint}", endpoint);
//
//                         await source.RunAsync(_topologyChannel.Writer, context);
//
//                         _logger.LogDebug("Topology source completed gracefully for {Endpoint}", endpoint);
//                     },
//                     ct);
//             }
//             catch (OperationCanceledException) when (ct.IsCancellationRequested) {
//                 break;
//             }
//             catch (Exception ex) {
//                 _logger.LogWarning(ex, "Topology source failed for {Endpoint}, rotating to next seed", endpoint);
//
//                 channelPool.Invalidate(endpoint);
//                 seedIndex++;
//             }
//         }
//
//         _topologyChannel.Writer.TryComplete();
//     }
//
//     async Task RunReaderAsync(CancellationToken ct) {
//         try {
//             await foreach (var topology in _topologyChannel.Reader.ReadAllAsync(ct)) {
//                 _logger.LogDebug("Topology update received: {Topology}", topology);
//
//                 // Addresses already have ClusterNode attached via BalancerAttributes
//                 _listener?.Invoke(ResolverResult.ForResult(topology.Addresses));
//             }
//         }
//         catch (OperationCanceledException) when (ct.IsCancellationRequested) {
//             _logger.LogDebug("Topology reader cancelled");
//         }
//         catch (Exception ex) {
//             _logger.LogError(ex, "Topology reader failed");
//         }
//     }
//
//     public async ValueTask DisposeAsync() {
//         if (Interlocked.Exchange(ref _disposed, 1) == 1)
//             return;
//
//         _logger.LogDebug("Disposing cluster resolver");
//
//         if (_cts is not null)
//             await _cts.CancelAsync();
//
//         try {
//             var tasks = new List<Task>(2);
//             if (_sourceTask is not null) tasks.Add(_sourceTask);
//             if (_readerTask is not null) tasks.Add(_readerTask);
//
//             if (tasks.Count > 0)
//                 await Task.WhenAll(tasks).ConfigureAwait(false);
//         }
//         catch {
//             // Ignore cancellation exceptions during dispose
//         }
//
//         await source.DisposeAsync().ConfigureAwait(false);
//         _cts?.Dispose();
//     }
//
//     protected override void Dispose(bool disposing) {
//         if (disposing)
//             DisposeAsync().AsTask().GetAwaiter().GetResult();
//
//         base.Dispose(disposing);
//     }
// }
//
// /// <summary>
// /// Factory for creating ClusterResolver instances.
// /// </summary>
// internal sealed class ClusterResolverFactory(
//     TopologySource source,
//     DnsEndPoint[] seeds,
//     ChannelPool channelPool,
//     ResiliencePipeline resiliencePipeline) : ResolverFactory {
//
//     ClusterResolver? _resolver;
//
//     public override string Name =>
//         "cluster";
//
//     /// <summary>
//     /// Gets the refresh action for the created resolver.
//     /// Returns a no-op if resolver hasn't been created yet.
//     /// </summary>
//     public Action RefreshAction =>
//         _resolver?.RefreshAction ?? (() => { });
//
//     public override Resolver Create(ResolverOptions options) {
//         _resolver = new ClusterResolver(source, seeds, channelPool, resiliencePipeline, options.LoggerFactory);
//         return _resolver;
//     }
// }
//
// #endregion
//
// #region Cluster Load Balancer
//
// /// <summary>
// /// Load balancer that extends SubchannelsLoadBalancer and delegates picking to IClusterNodePickingStrategy.
// /// ClusterNode data flows through BalancerAddress.Attributes via UnsafeAccessor.
// /// </summary>
// internal sealed class ClusterLoadBalancer(
//     IChannelControlHelper controller,
//     ILoggerFactory loggerFactory,
//     IClusterNodePickingStrategy strategy) : SubchannelsLoadBalancer(controller, loggerFactory) {
//
//     [SkipLocalsInit]
//     protected override SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels) {
//         var count = readySubchannels.Count;
//
//         // Rent array from pool for temporary storage
//         var rented = ArrayPool<SubchannelNode>.Shared.Rent(count);
//         try {
//             var validCount = 0;
//
//             // Extract ClusterNode from each subchannel's current address attributes
//             for (var i = 0; i < count; i++) {
//                 var subchannel = readySubchannels[i];
//
//                 if (subchannel.CurrentAddress.GetAttribute<ClusterNode>() is { } node)
//                     rented[validCount++] = new SubchannelNode(subchannel, node);
//             }
//
//             // Copy to exact-size array for the picker (picker holds reference long-term)
//             var nodes = new SubchannelNode[validCount];
//             Array.Copy(rented, nodes, validCount);
//
//             return new StrategyPickerAdapter(nodes, strategy);
//         }
//         finally {
//             ArrayPool<SubchannelNode>.Shared.Return(rented);
//         }
//     }
// }
//
// /// <summary>
// /// Factory for creating ClusterLoadBalancer instances.
// /// </summary>
// internal sealed class ClusterLoadBalancerFactory(IClusterNodePickingStrategy strategy) : LoadBalancerFactory {
//     public override string Name =>
//         "cluster";
//
//     public override LoadBalancer Create(LoadBalancerOptions options) =>
//         new ClusterLoadBalancer(options.Controller, options.LoggerFactory, strategy);
// }
//
// #endregion
//
// #region Strategy Picker Adapter
//
// /// <summary>
// /// Adapts IClusterNodePickingStrategy to gRPC's SubchannelPicker.
// /// This is the bridge between our nice API and gRPC's terrible one.
// /// </summary>
// internal sealed class StrategyPickerAdapter(SubchannelNode[] nodes, IClusterNodePickingStrategy strategy) : SubchannelPicker {
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public override PickResult Pick(PickContext context) {
//         var pickContext = new PickNodeContext(nodes, context.Request);
//         var result = strategy.Pick(in pickContext);
//         return result.ToPickResult();
//     }
// }
//
// #endregion
//
// #region Configuration
//
// /// <summary>
// /// Configuration for cluster load balancing.
// /// </summary>
// internal sealed class LoadBalancingConfiguration {
//     public string ClusterName { get; set; } = "cluster";
//     public List<DnsEndPoint> Seeds { get; } = [];
//     public TopologySource? TopologySource { get; set; }
//     public ResiliencePipeline? ResiliencePipeline { get; set; }
//     public IClusterNodePickingStrategy? PickingStrategy { get; set; }
//     public ShouldRefreshTopology? RefreshPredicate { get; set; }
//
//     internal ClusterResolverFactory BuildResolverFactory(ChannelPool channelPool) {
//         if (TopologySource is null)
//             throw new InvalidOperationException("TopologySource must be configured.");
//
//         if (Seeds.Count == 0)
//             throw new InvalidOperationException("At least one seed must be configured.");
//
//         var pipeline = ResiliencePipeline ?? ResiliencePipeline.Empty;
//
//         return new ClusterResolverFactory(TopologySource, [.. Seeds], channelPool, pipeline);
//     }
//
//     internal LoadBalancerFactory BuildLoadBalancerFactory() {
//         if (PickingStrategy is null)
//             throw new InvalidOperationException("PickingStrategy must be configured.");
//
//         return new ClusterLoadBalancerFactory(PickingStrategy);
//     }
//
//     internal TopologyRefreshInterceptor? BuildRefreshInterceptor(Action refreshAction) {
//         if (RefreshPredicate is null)
//             return null;
//
//         return new TopologyRefreshInterceptor(refreshAction, RefreshPredicate);
//     }
// }
//
// /// <summary>
// /// Builder for configuring the TopologySource.
// /// </summary>
// public sealed class TopologySourceBuilder {
//     internal TopologySource? Source { get; set; }
//     internal ResiliencePipeline? ResiliencePipeline { get; set; }
//
//     /// <summary>
//     /// Use the specified topology source instance.
//     /// </summary>
//     public TopologySourceBuilder UseSource(TopologySource source) {
//         Source = source;
//         return this;
//     }
//
//     /// <summary>
//     /// Use a topology source of the specified type.
//     /// </summary>
//     public TopologySourceBuilder UseSource<T>() where T : TopologySource, new() {
//         Source = new T();
//         return this;
//     }
//
//     /// <summary>
//     /// Configure the resilience pipeline for the topology source.
//     /// </summary>
//     public TopologySourceBuilder WithResiliencePipeline(ResiliencePipeline pipeline) {
//         ResiliencePipeline = pipeline;
//         return this;
//     }
//
//     /// <summary>
//     /// Configure resilience using a builder.
//     /// </summary>
//     public TopologySourceBuilder WithResilience(Action<ResiliencePipelineBuilder> configure) {
//         var builder = new ResiliencePipelineBuilder();
//         configure(builder);
//         ResiliencePipeline = builder.Build();
//         return this;
//     }
// }
//
// /// <summary>
// /// Builder for configuring cluster load balancing.
// /// </summary>
// public sealed class LoadBalancingBuilder {
//     readonly LoadBalancingConfiguration _config = new();
//
//     /// <summary>
//     /// Set the cluster name (used in the URI scheme).
//     /// </summary>
//     public LoadBalancingBuilder ForCluster(string name) {
//         _config.ClusterName = name;
//         return this;
//     }
//
//     /// <summary>
//     /// Add seed endpoints for initial cluster discovery.
//     /// </summary>
//     public LoadBalancingBuilder WithSeeds(params string[] seeds) {
//         foreach (var seed in seeds) {
//             var parts = seed.Split(':');
//             var host = parts[0];
//             var port = parts.Length > 1 ? int.Parse(parts[1]) : 2113;
//             _config.Seeds.Add(new DnsEndPoint(host, port));
//         }
//         return this;
//     }
//
//     /// <summary>
//     /// Add seed endpoints for initial cluster discovery.
//     /// </summary>
//     public LoadBalancingBuilder WithSeeds(params DnsEndPoint[] seeds) {
//         _config.Seeds.AddRange(seeds);
//         return this;
//     }
//
//     /// <summary>
//     /// Configure the topology source.
//     /// </summary>
//     public LoadBalancingBuilder WithTopologySource(Action<TopologySourceBuilder> configure) {
//         var builder = new TopologySourceBuilder();
//         configure(builder);
//
//         _config.TopologySource = builder.Source;
//         _config.ResiliencePipeline = builder.ResiliencePipeline;
//
//         return this;
//     }
//
//     /// <summary>
//     /// Configure the topology source with a specific type.
//     /// </summary>
//     public LoadBalancingBuilder WithTopologySource<T>(Action<TopologySourceBuilder>? configure = null)
//         where T : TopologySource, new() {
//         var builder = new TopologySourceBuilder();
//         builder.UseSource<T>();
//         configure?.Invoke(builder);
//
//         _config.TopologySource = builder.Source;
//         _config.ResiliencePipeline = builder.ResiliencePipeline;
//
//         return this;
//     }
//
//     /// <summary>
//     /// Configure the picking strategy.
//     /// </summary>
//     public LoadBalancingBuilder WithPickingStrategy(IClusterNodePickingStrategy strategy) {
//         _config.PickingStrategy = strategy;
//         return this;
//     }
//
//     /// <summary>
//     /// Configure the picking strategy with a specific type.
//     /// </summary>
//     public LoadBalancingBuilder WithPickingStrategy<T>() where T : IClusterNodePickingStrategy, new() {
//         _config.PickingStrategy = new T();
//         return this;
//     }
//
//     /// <summary>
//     /// Configure node picking using a delegate.
//     /// </summary>
//     public LoadBalancingBuilder WithPicker(PickerDelegate picker) {
//         _config.PickingStrategy = new DelegatePickingStrategy(picker);
//         return this;
//     }
//
//     /// <summary>
//     /// Configure node picking using a simple selector delegate.
//     /// </summary>
//     public LoadBalancingBuilder WithNodeSelector(Func<SubchannelNode[], SubchannelNode?> selector) {
//         _config.PickingStrategy = DelegatePickingStrategy.FromSelector(selector);
//         return this;
//     }
//
//     /// <summary>
//     /// Route all operations to the leader only.
//     /// </summary>
//     public LoadBalancingBuilder WithLeaderOnly() {
//         _config.PickingStrategy = LeaderPickingStrategy.Instance;
//         return this;
//     }
//
//     /// <summary>
//     /// Use round-robin across all eligible nodes.
//     /// </summary>
//     public LoadBalancingBuilder WithRoundRobin() {
//         _config.PickingStrategy = new RoundRobinPickingStrategy();
//         return this;
//     }
//
//     /// <summary>
//     /// Prefer followers/read-replicas, fall back to leader.
//     /// </summary>
//     public LoadBalancingBuilder WithPreferFollowers() {
//         _config.PickingStrategy = new PreferFollowersPickingStrategy();
//         return this;
//     }
//
//     /// <summary>
//     /// Pick a random node.
//     /// </summary>
//     public LoadBalancingBuilder WithRandom() {
//         _config.PickingStrategy = RandomPickingStrategy.Instance;
//         return this;
//     }
//
//     /// <summary>
//     /// Configure when to trigger a topology refresh based on RPC failures.
//     /// The interceptor is automatically added to the channel.
//     /// </summary>
//     /// <param name="predicate">Predicate that returns true if the error should trigger a refresh.</param>
//     public LoadBalancingBuilder WithRefreshOn(ShouldRefreshTopology predicate) {
//         _config.RefreshPredicate = predicate;
//         return this;
//     }
//
//     /// <summary>
//     /// Trigger topology refresh on specific status codes.
//     /// </summary>
//     public LoadBalancingBuilder WithRefreshOn(params StatusCode[] statusCodes) {
//         var codes = statusCodes.ToHashSet();
//         _config.RefreshPredicate = (status, _) => codes.Contains(status);
//         return this;
//     }
//
//     internal LoadBalancingConfiguration Build() =>
//         _config;
// }
//
// #endregion
//
// #region Static Factory
//
// /// <summary>
// /// A load-balanced gRPC channel with optional topology refresh support.
// /// </summary>
// public sealed class ClusterChannel : IDisposable {
//     readonly GrpcChannel _channel;
//     readonly TopologyRefreshInterceptor? _interceptor;
//
//     internal ClusterChannel(GrpcChannel channel, TopologyRefreshInterceptor? interceptor) {
//         _channel = channel;
//         _interceptor = interceptor;
//     }
//
//     /// <summary>
//     /// The underlying gRPC channel.
//     /// </summary>
//     public GrpcChannel Channel => _channel;
//
//     /// <summary>
//     /// Creates a CallInvoker that includes the topology refresh interceptor (if configured).
//     /// Use this to create gRPC clients.
//     /// </summary>
//     public CallInvoker CreateCallInvoker() {
//         var invoker = _channel.CreateCallInvoker();
//         return _interceptor is not null
//             ? invoker.Intercept(_interceptor)
//             : invoker;
//     }
//
//     /// <summary>
//     /// Implicit conversion to GrpcChannel for compatibility.
//     /// Note: This bypasses the refresh interceptor.
//     /// </summary>
//     public static implicit operator GrpcChannel(ClusterChannel clusterChannel) {
//         return clusterChannel._channel;
//     }
//
//     public void Dispose() {
//         _channel.Dispose();
//     }
// }
//
// /// <summary>
// /// Factory for creating load-balanced gRPC channels.
// /// </summary>
// public static class GrpcLoadBalancedChannel {
//     /// <summary>
//     /// Create a load-balanced gRPC channel.
//     /// </summary>
//     /// <param name="options">Base channel options (credentials, handlers, etc.)</param>
//     /// <param name="configure">Load balancing configuration.</param>
//     public static ClusterChannel Create(GrpcChannelOptions options, Action<LoadBalancingBuilder> configure) {
//         var builder = new LoadBalancingBuilder();
//         configure(builder);
//
//         var config = builder.Build();
//
//         var channelPool = new ChannelPool(
//             endpoint => GrpcChannel.ForAddress(
//                 BuildEndpointUri(endpoint, options.Credentials),
//                 CloneOptionsForBootstrap(options)));
//
//         var resolverFactory = config.BuildResolverFactory(channelPool);
//         var loadBalancerFactory = config.BuildLoadBalancerFactory();
//
//         var channelServices = new ServiceCollection();
//         channelServices.AddSingleton<ResolverFactory>(resolverFactory);
//         channelServices.AddSingleton<LoadBalancerFactory>(loadBalancerFactory);
//
//         options.ServiceConfig ??= new ServiceConfig();
//         options.ServiceConfig.LoadBalancingConfigs.Clear();
//         options.ServiceConfig.LoadBalancingConfigs.Add(new LoadBalancingConfig("cluster"));
//         options.ServiceProvider = channelServices.BuildServiceProvider();
//
//         var defaultPort = config.Seeds.Count > 0 ? config.Seeds[0].Port : 2113;
//         var address = $"cluster:///{config.ClusterName}:{defaultPort}";
//
//         var channel = GrpcChannel.ForAddress(address, options);
//
//         // Build interceptor with lazy refresh action (resolver created on first call)
//         var interceptor = config.BuildRefreshInterceptor(() => resolverFactory.RefreshAction());
//
//         return new ClusterChannel(channel, interceptor);
//     }
//
//     static string BuildEndpointUri(DnsEndPoint endpoint, ChannelCredentials? credentials) {
//         var scheme = credentials == ChannelCredentials.Insecure ? "http" : "https";
//         return $"{scheme}://{endpoint.Host}:{endpoint.Port}";
//     }
//
//     static GrpcChannelOptions CloneOptionsForBootstrap(GrpcChannelOptions source) {
//         return new() {
//             Credentials                             = source.Credentials,
//             HttpHandler                             = source.HttpHandler,
//             HttpClient                              = source.HttpClient,
//             LoggerFactory                           = source.LoggerFactory,
//             MaxReceiveMessageSize                   = source.MaxReceiveMessageSize,
//             MaxSendMessageSize                      = source.MaxSendMessageSize,
//             MaxRetryAttempts                        = source.MaxRetryAttempts,
//             MaxRetryBufferSize                      = source.MaxRetryBufferSize,
//             MaxRetryBufferPerCallSize               = source.MaxRetryBufferPerCallSize,
//             ThrowOperationCanceledOnCancellation    = source.ThrowOperationCanceledOnCancellation,
//             UnsafeUseInsecureChannelCallCredentials = source.UnsafeUseInsecureChannelCallCredentials,
//             ServiceConfig                           = source.ServiceConfig is not null
//                 ? CloneServiceConfig(source.ServiceConfig)
//                 : null,
//             ServiceProvider = source.ServiceProvider
//         };
//     }
//
//     static ServiceConfig CloneServiceConfig(ServiceConfig source) {
//         var clone = new ServiceConfig();
//
//         foreach (var methodConfig in source.MethodConfigs)
//             clone.MethodConfigs.Add(methodConfig);
//
//         // Don't clone LoadBalancingConfigs - bootstrap channels use direct connection
//
//         return clone;
//     }
// }
//
// #endregion
//
// #region DI Extensions
//
// /// <summary>
// /// Extension methods for integrating cluster load balancing with Microsoft.Extensions.DependencyInjection.
// /// </summary>
// public static class GrpcLoadBalancingServiceCollectionExtensions {
//     /// <summary>
//     /// Adds cluster load balancing to a gRPC client.
//     /// Chain this after ConfigureChannel().
//     /// </summary>
//     public static IHttpClientBuilder WithClusterLoadBalancing(
//         this IHttpClientBuilder builder,
//         Action<LoadBalancingBuilder> configure) {
//         var clientName = builder.Name;
//
//         // Build configuration upfront
//         var lbBuilder = new LoadBalancingBuilder();
//         configure(lbBuilder);
//         var config = lbBuilder.Build();
//
//         // Register channel pool as singleton (shared across resolvers)
//         builder.Services.AddSingleton(sp => {
//             var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<GrpcChannelOptions>>();
//             var options = optionsMonitor.Get(clientName);
//
//             return new ChannelPool(
//                 endpoint => GrpcChannel.ForAddress(
//                     BuildEndpointUri(endpoint, options.Credentials),
//                     CloneOptionsForBootstrap(options)));
//         });
//
//         // Register resolver factory as singleton
//         builder.Services.AddSingleton(sp => {
//             var channelPool = sp.GetRequiredService<ChannelPool>();
//             return config.BuildResolverFactory(channelPool);
//         });
//
//         // Register resolver factory as ResolverFactory (interface registration)
//         builder.Services.AddSingleton<ResolverFactory>(sp => sp.GetRequiredService<ClusterResolverFactory>());
//
//         // Register load balancer factory
//         builder.Services.AddSingleton<LoadBalancerFactory>(_ => config.BuildLoadBalancerFactory());
//
//         // Add interceptor if refresh predicate is configured
//         if (config.RefreshPredicate is not null) {
//             builder.AddInterceptor(sp => {
//                 var resolverFactory = sp.GetRequiredService<ClusterResolverFactory>();
//                 return new TopologyRefreshInterceptor(() => resolverFactory.RefreshAction(), config.RefreshPredicate);
//             });
//         }
//
//         // PostConfigure to set up ServiceConfig and ServiceProvider
//         builder.Services.AddOptions<GrpcChannelOptions>(clientName)
//             .PostConfigure<IServiceProvider>((options, sp) => {
//                 options.ServiceConfig ??= new ServiceConfig();
//                 options.ServiceConfig.LoadBalancingConfigs.Clear();
//                 options.ServiceConfig.LoadBalancingConfigs.Add(new LoadBalancingConfig("cluster"));
//                 options.ServiceProvider = sp;
//             });
//
//         return builder;
//     }
//
//     static string BuildEndpointUri(DnsEndPoint endpoint, ChannelCredentials? credentials) {
//         var scheme = credentials == ChannelCredentials.Insecure ? "http" : "https";
//         return $"{scheme}://{endpoint.Host}:{endpoint.Port}";
//     }
//
//     static GrpcChannelOptions CloneOptionsForBootstrap(GrpcChannelOptions source) {
//         return new() {
//             Credentials                             = source.Credentials,
//             HttpHandler                             = source.HttpHandler,
//             HttpClient                              = source.HttpClient,
//             LoggerFactory                           = source.LoggerFactory,
//             MaxReceiveMessageSize                   = source.MaxReceiveMessageSize,
//             MaxSendMessageSize                      = source.MaxSendMessageSize,
//             MaxRetryAttempts                        = source.MaxRetryAttempts,
//             MaxRetryBufferSize                      = source.MaxRetryBufferSize,
//             MaxRetryBufferPerCallSize               = source.MaxRetryBufferPerCallSize,
//             ThrowOperationCanceledOnCancellation    = source.ThrowOperationCanceledOnCancellation,
//             UnsafeUseInsecureChannelCallCredentials = source.UnsafeUseInsecureChannelCallCredentials,
//             ServiceConfig                           = source.ServiceConfig is not null
//                 ? CloneServiceConfig(source.ServiceConfig)
//                 : null,
//             ServiceProvider = source.ServiceProvider
//         };
//     }
//
//     static ServiceConfig CloneServiceConfig(ServiceConfig source) {
//         var clone = new ServiceConfig();
//
//         foreach (var methodConfig in source.MethodConfigs)
//             clone.MethodConfigs.Add(methodConfig);
//
//         return clone;
//     }
// }
//
// #endregion
//
// #region Usage Examples
//
// /*
//  * =============================================================================
//  * IMPLEMENTING A TOPOLOGY SOURCE
//  * =============================================================================
//  *
//  * public class GossipTopologySource : TopologySource {
//  *     public override async Task RunAsync(
//  *         ChannelWriter<ClusterTopology> writer,
//  *         TopologyContext ctx) {
//  *         var client = new Gossip.GossipClient(ctx.Channel);
//  *
//  *         using var stream = client.Subscribe(new SubscribeRequest());
//  *
//  *         await foreach (var update in stream.ResponseStream.ReadAllAsync(ctx.CancellationToken)) {
//  *             var nodes = update.Members
//  *                 .Select(m => new ClusterNode {
//  *                     NodeId   = Guid.Parse(m.InstanceId),
//  *                     EndPoint = new DnsEndPoint(m.HttpEndPoint.Host, m.HttpEndPoint.Port),
//  *                     Role     = MapRole(m.State),
//  *                     Status   = MapStatus(m.State)
//  *                 })
//  *                 .ToArray();
//  *
//  *             await writer.WriteAsync(new ClusterTopology(nodes), ctx.CancellationToken);
//  *         }
//  *     }
//  *
//  *     static NodeRole MapRole(MemberState state) =>
//  *         state switch {
//  *             MemberState.Leader          => NodeRole.Leader,
//  *             MemberState.Follower        => NodeRole.Follower,
//  *             MemberState.ReadOnlyReplica => NodeRole.ReadOnly,
//  *             _                           => NodeRole.None
//  *         };
//  *
//  *     static NodeStatus MapStatus(MemberState state) =>
//  *         state switch {
//  *             MemberState.Leader or MemberState.Follower => NodeStatus.Ready,
//  *             MemberState.CatchingUp                     => NodeStatus.Syncing,
//  *             MemberState.ResigningLeader                => NodeStatus.Stopping,
//  *             _                                          => NodeStatus.Starting
//  *         };
//  * }
//  *
//  * =============================================================================
//  * STATIC FACTORY USAGE
//  * =============================================================================
//  *
//  * var clusterChannel = GrpcLoadBalancedChannel.Create(
//  *     new GrpcChannelOptions {
//  *         Credentials = ChannelCredentials.SecureSsl,
//  *         HttpHandler = myHandler
//  *     },
//  *     lb => lb
//  *         .ForCluster("kurrent-db")
//  *         .WithSeeds("node1:2113", "node2:2113", "node3:2113")
//  *         .WithTopologySource<GossipTopologySource>(ts => ts
//  *             .WithResilience(r => r
//  *                 .AddRetry(new RetryStrategyOptions {
//  *                     MaxRetryAttempts = 3,
//  *                     Delay = TimeSpan.FromSeconds(1),
//  *                     BackoffType = DelayBackoffType.Exponential
//  *                 })))
//  *         .WithRoundRobin()
//  *         .WithRefreshOn(StatusCode.Unavailable, StatusCode.NotFound));
//  *
//  * // Use CreateCallInvoker() to get the interceptor for refresh support
//  * var client = new Streams.StreamsClient(clusterChannel.CreateCallInvoker());
//  *
//  * // Or use the channel directly (bypasses refresh interceptor)
//  * var client2 = new Streams.StreamsClient(clusterChannel.Channel);
//  *
//  * =============================================================================
//  * DEPENDENCY INJECTION USAGE
//  * =============================================================================
//  *
//  * builder.Services
//  *     .AddGrpcClient<Streams.StreamsClient>(o => {
//  *         o.Address = new Uri("cluster:///kurrent-db");
//  *     })
//  *     .ConfigureChannel(o => {
//  *         o.Credentials = ChannelCredentials.SecureSsl;
//  *         o.HttpHandler = myHandler;
//  *     })
//  *     .WithClusterLoadBalancing(lb => lb
//  *         .ForCluster("kurrent-db")
//  *         .WithSeeds("node1:2113", "node2:2113", "node3:2113")
//  *         .WithTopologySource<GossipTopologySource>(ts => ts
//  *             .WithResilience(r => r
//  *                 .AddRetry(new RetryStrategyOptions {
//  *                     MaxRetryAttempts = 3,
//  *                     Delay = TimeSpan.FromSeconds(1)
//  *                 })))
//  *         .WithPickingStrategy<LeaderPickingStrategy>()
//  *         .WithRefreshOn((status, trailers) =>
//  *             status == StatusCode.Unavailable ||
//  *             trailers.Any(t => t.Key == "not-leader")));
//  *
//  * =============================================================================
//  * POLLING TOPOLOGY SOURCE
//  * =============================================================================
//  *
//  * public class PollingGossipSource : PollingTopologySource {
//  *     public PollingGossipSource() : base(TimeSpan.FromSeconds(30)) { }
//  *
//  *     protected override async Task<ClusterTopology> FetchTopologyAsync(TopologyContext ctx) {
//  *         var client = new Gossip.GossipClient(ctx.Channel);
//  *         var response = await client.ReadAsync(new ReadRequest(), cancellationToken: ctx.CancellationToken);
//  *
//  *         var nodes = response.Members
//  *             .Select(m => new ClusterNode {
//  *                 NodeId   = Guid.Parse(m.InstanceId),
//  *                 EndPoint = new DnsEndPoint(m.HttpEndPoint.Host, m.HttpEndPoint.Port),
//  *                 Role     = MapRole(m.State),
//  *                 Status   = MapStatus(m.State)
//  *             })
//  *             .ToArray();
//  *
//  *         return new ClusterTopology(nodes);
//  *     }
//  * }
//  *
//  * // When using a polling source with WithRefreshOn, errors trigger immediate refresh
//  * // instead of waiting for the next poll interval
//  *
//  * =============================================================================
//  * CUSTOM PICKING STRATEGY
//  * =============================================================================
//  *
//  * public class ReadWriteSplitStrategy : IClusterNodePickingStrategy {
//  *     long _readIndex;
//  *
//  *     public PickNodeResult Pick(in PickNodeContext context) {
//  *         var isWrite = context.Request.RequestUri?.AbsolutePath.Contains("/Append") ?? false;
//  *
//  *         if (isWrite) {
//  *             var nodes = context.Nodes;
//  *             for (var i = 0; i < nodes.Length; i++) {
//  *                 if (nodes[i].Node.CanAcceptWrites)
//  *                     return PickNodeResult.ForNode(nodes[i]);
//  *             }
//  *             return PickNodeResult.NoLeader();
//  *         }
//  *
//  *         // Count readers first
//  *         var readerCount = 0;
//  *         for (var i = 0; i < context.Nodes.Length; i++) {
//  *             if (context.Nodes[i].Node.CanServeReads)
//  *                 readerCount++;
//  *         }
//  *
//  *         if (readerCount == 0)
//  *             return PickNodeResult.NoEligibleNodes();
//  *
//  *         var targetIndex = (int)(Interlocked.Increment(ref _readIndex) % readerCount);
//  *         var currentIndex = 0;
//  *
//  *         for (var i = 0; i < context.Nodes.Length; i++) {
//  *             if (!context.Nodes[i].Node.CanServeReads)
//  *                 continue;
//  *
//  *             if (currentIndex == targetIndex)
//  *                 return PickNodeResult.ForNode(context.Nodes[i]);
//  *
//  *             currentIndex++;
//  *         }
//  *
//  *         return PickNodeResult.NoEligibleNodes();
//  *     }
//  * }
//  *
//  * =============================================================================
//  * PICK RESULT OPTIONS
//  * =============================================================================
//  *
//  * // Success
//  * PickNodeResult.ForSubchannel(subchannel)
//  * PickNodeResult.ForNode(subchannelNode)
//  *
//  * // Defer (wait for cluster to become ready)
//  * PickNodeResult.Defer()
//  * PickNodeResult.WaitForReady()
//  *
//  * // Drop (fail immediately, bypass retries)
//  * PickNodeResult.Drop("Rate limited")
//  * PickNodeResult.Drop(StatusCode.ResourceExhausted, "Too many requests")
//  *
//  * // Common failures (pre-allocated, zero-allocation)
//  * PickNodeResult.NoLeader()
//  * PickNodeResult.NoFollowers()
//  * PickNodeResult.NoEligibleNodes()
//  * PickNodeResult.ClusterNotReady()
//  * PickNodeResult.NoNodes()
//  * PickNodeResult.SelectionTimeout()
//  *
//  * // Custom failures
//  * PickNodeResult.Fail("Custom error message")
//  * PickNodeResult.Fail(StatusCode.PermissionDenied, "Not authorized")
//  *
//  */
//
// #endregion
