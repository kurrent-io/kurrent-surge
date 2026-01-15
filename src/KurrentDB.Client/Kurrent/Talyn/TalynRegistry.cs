using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace Talyn;

#region Receiver Entries

/// <summary>
/// Wrapper for a message receiver with disconnect detection and execution tracking.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
/// <remarks>
/// <para>
/// The <see cref="ExecutionLock"/> semaphore ensures that <see cref="ITalynBus.DisconnectAsync{T}"/>
/// can wait for any in-flight execution to complete before returning.
/// </para>
/// <para>
/// The <see cref="IsDisconnected"/> flag is set atomically when disconnect is called,
/// allowing the consumer to skip the receiver even if it was captured in a snapshot.
/// </para>
/// </remarks>
internal sealed class ReceiverEntry<T> {
    /// <summary>
    /// Flag indicating the receiver has been disconnected and should be skipped.
    /// </summary>
    public volatile bool IsDisconnected;

    /// <summary>
    /// Creates a new receiver entry.
    /// </summary>
    /// <param name="receiver">The receiver to wrap.</param>
    public ReceiverEntry(IReceiver<T> receiver) => Receiver = receiver;

    /// <summary>
    /// The wrapped receiver instance.
    /// </summary>
    public IReceiver<T> Receiver { get; }

    /// <summary>
    /// Semaphore used to track active execution for <see cref="ITalynBus.DisconnectAsync{T}"/>.
    /// </summary>
    public SemaphoreSlim ExecutionLock { get; } = new(1, 1);
}

/// <summary>
/// Wrapper for a request/response receiver with disconnect detection and execution tracking.
/// </summary>
/// <typeparam name="T">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// The <see cref="ExecutionLock"/> semaphore ensures that <see cref="ITalynBus.DisconnectAsync{T,TResponse}"/>
/// can wait for any in-flight execution to complete before returning.
/// </para>
/// <para>
/// The <see cref="IsDisconnected"/> flag is set atomically when disconnect is called,
/// allowing the consumer to skip the receiver even if it was captured in a snapshot.
/// </para>
/// </remarks>
internal sealed class ReceiverEntry<T, TResponse> {
    /// <summary>
    /// Flag indicating the receiver has been disconnected and should be skipped.
    /// </summary>
    public volatile bool IsDisconnected;

    /// <summary>
    /// Creates a new receiver entry.
    /// </summary>
    /// <param name="receiver">The receiver to wrap.</param>
    public ReceiverEntry(IReceiver<T, TResponse> receiver) => Receiver = receiver;

    /// <summary>
    /// The wrapped receiver instance.
    /// </summary>
    public IReceiver<T, TResponse> Receiver { get; }

    /// <summary>
    /// Semaphore used to track active execution for <see cref="ITalynBus.DisconnectAsync{T,TResponse}"/>.
    /// </summary>
    public SemaphoreSlim ExecutionLock { get; } = new(1, 1);
}

#endregion

#region Registry Interfaces

/// <summary>
/// Read-only interface for resolving receiver entries from a source.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to provide additional receiver sources beyond DI and runtime connections.
/// The <see cref="TalynRegistry"/> accepts multiple resolvers and combines their results.
/// </para>
/// </remarks>
internal interface IReceiverResolver {
    /// <summary>
    /// Gets receiver entries for the specified message type.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <returns>Receiver entries from this source.</returns>
    ReceiverEntry<T>[] GetReceivers<T>();

    /// <summary>
    /// Gets receiver entries for the specified request/response types.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>Receiver entries from this source.</returns>
    ReceiverEntry<T, TResponse>[] GetReceivers<T, TResponse>();
}

/// <summary>
/// Read-only interface for resolving behavior entries from a source.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to provide additional behavior sources beyond runtime connections.
/// Behaviors are type-safe middleware that execute in registration order.
/// </para>
/// </remarks>
internal interface IBehaviorResolver {
    /// <summary>
    /// Gets behavior entries for fire-and-forget messages.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <returns>Behavior entries from this source.</returns>
    BehaviorEntry<T>[] GetBehaviors<T>();

    /// <summary>
    /// Gets behavior entries for request/response messages.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>Behavior entries from this source.</returns>
    BehaviorEntry<T, TResponse>[] GetBehaviors<T, TResponse>();

    /// <summary>
    /// Gets broadcast behavior entries with full response attribution.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>Broadcast behavior entries from this source.</returns>
    BroadcastBehaviorEntry<T, TResponse>[] GetBroadcastBehaviors<T, TResponse>();
}

/// <summary>
/// Registry for message and request receivers with support for multiple resolvers and runtime connections.
/// </summary>
internal interface ITalynRegistry : IAsyncDisposable {
    /// <summary>
    /// Gets all receiver entries for the specified message type.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <returns>All receiver entries (from all resolvers and runtime connected).</returns>
    ReceiverEntry<T>[] GetReceivers<T>();

    /// <summary>
    /// Gets all receiver entries for the specified request/response types.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>All receiver entries (from all resolvers and runtime connected).</returns>
    ReceiverEntry<T, TResponse>[] GetReceivers<T, TResponse>();

    /// <summary>
    /// Gets all behavior entries for fire-and-forget messages.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <returns>All behavior entries (from all resolvers and runtime connected).</returns>
    BehaviorEntry<T>[] GetBehaviors<T>();

    /// <summary>
    /// Gets all behavior entries for request/response messages.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>All behavior entries (from all resolvers and runtime connected).</returns>
    BehaviorEntry<T, TResponse>[] GetBehaviors<T, TResponse>();

    /// <summary>
    /// Gets all broadcast behavior entries with full response attribution.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>All broadcast behavior entries (from all resolvers and runtime connected).</returns>
    BroadcastBehaviorEntry<T, TResponse>[] GetBroadcastBehaviors<T, TResponse>();

    /// <summary>
    /// Connects a message receiver for runtime handling.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="receiver">The receiver to connect.</param>
    void Connect<T>(IReceiver<T> receiver);

    /// <summary>
    /// Disconnects a previously connected message receiver.
    /// Sets the disconnect flag and removes from the registry.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="receiver">The receiver to disconnect.</param>
    /// <returns>The entry that was disconnected, or null if not found.</returns>
    ReceiverEntry<T>? Disconnect<T>(IReceiver<T> receiver);

    /// <summary>
    /// Connects a request receiver for runtime handling.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="receiver">The receiver to connect.</param>
    void Connect<T, TResponse>(IReceiver<T, TResponse> receiver);

    /// <summary>
    /// Disconnects a previously connected request receiver.
    /// Sets the disconnect flag and removes from the registry.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="receiver">The receiver to disconnect.</param>
    /// <returns>The entry that was disconnected, or null if not found.</returns>
    ReceiverEntry<T, TResponse>? Disconnect<T, TResponse>(IReceiver<T, TResponse> receiver);

    /// <summary>
    /// Connects a fire-and-forget behavior for runtime handling.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="behavior">The behavior to connect.</param>
    void Connect<T>(ITalynBehavior<T> behavior);

    /// <summary>
    /// Connects a request/response behavior for runtime handling.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="behavior">The behavior to connect.</param>
    void Connect<T, TResponse>(ITalynBehavior<T, TResponse> behavior);

    /// <summary>
    /// Connects a broadcast behavior for runtime handling.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="behavior">The behavior to connect.</param>
    void ConnectBroadcast<T, TResponse>(ITalynBroadcastBehavior<T, TResponse> behavior);
}

#endregion

#region Registry Implementations

/// <summary>
/// Resolver for DI-registered receivers with permanent caching.
/// </summary>
/// <remarks>
/// <para>
/// Resolves receivers via <see cref="IServiceProvider.GetServices{T}"/> and caches them forever
/// (DI registrations don't change at runtime). Entries are wrapped in <see cref="ReceiverEntry{T}"/>
/// with <see cref="ReceiverEntry{T}.IsDisconnected"/> permanently false since DI receivers
/// cannot be disconnected.
/// </para>
/// </remarks>
internal sealed class TalynContainerResolver : IReceiverResolver {
    readonly ConcurrentDictionary<Type, object> _cache = new();
    readonly IServiceProvider                   _provider;

    /// <summary>
    /// Creates a container registry with DI integration.
    /// </summary>
    /// <param name="provider">The service provider for resolving DI-registered receivers.</param>
    public TalynContainerResolver(IServiceProvider provider) => _provider = provider;

    /// <inheritdoc />
    public ReceiverEntry<T>[] GetReceivers<T>() =>
        (ReceiverEntry<T>[])_cache.GetOrAdd(
            typeof(IReceiver<T>),
            static (_, sp) => sp.GetServices<IReceiver<T>>()
                .Select(static r => new ReceiverEntry<T>(r))
                .ToArray(),
            _provider
        );

    /// <inheritdoc />
    public ReceiverEntry<T, TResponse>[] GetReceivers<T, TResponse>() =>
        (ReceiverEntry<T, TResponse>[])_cache.GetOrAdd(
            typeof(IReceiver<T, TResponse>),
            static (_, sp) => sp.GetServices<IReceiver<T, TResponse>>()
                .Select(static r => new ReceiverEntry<T, TResponse>(r))
                .ToArray(),
            _provider
        );
}

/// <summary>
/// Resolver for DI-registered behaviors with permanent caching.
/// </summary>
/// <remarks>
/// <para>
/// Resolves behaviors via <see cref="IServiceProvider.GetServices{T}"/> and caches them forever
/// (DI registrations don't change at runtime). Entries are wrapped in <see cref="BehaviorEntry{T}"/>
/// with type metadata for diagnostics.
/// </para>
/// </remarks>
internal sealed class TalynContainerBehaviorResolver : IBehaviorResolver {
    readonly ConcurrentDictionary<Type, object> _cache = new();
    readonly IServiceProvider                   _provider;

    /// <summary>
    /// Creates a container behavior resolver with DI integration.
    /// </summary>
    /// <param name="provider">The service provider for resolving DI-registered behaviors.</param>
    public TalynContainerBehaviorResolver(IServiceProvider provider) => _provider = provider;

    /// <inheritdoc />
    public BehaviorEntry<T>[] GetBehaviors<T>() =>
        (BehaviorEntry<T>[])_cache.GetOrAdd(
            typeof(ITalynBehavior<T>),
            static (_, sp) => sp.GetServices<ITalynBehavior<T>>()
                .Select(static b => new BehaviorEntry<T> { Behavior = b, BehaviorType = b.GetType() })
                .ToArray(),
            _provider
        );

    /// <inheritdoc />
    public BehaviorEntry<T, TResponse>[] GetBehaviors<T, TResponse>() =>
        (BehaviorEntry<T, TResponse>[])_cache.GetOrAdd(
            typeof(ITalynBehavior<T, TResponse>),
            static (_, sp) => sp.GetServices<ITalynBehavior<T, TResponse>>()
                .Select(static b => new BehaviorEntry<T, TResponse> { Behavior = b, BehaviorType = b.GetType() })
                .ToArray(),
            _provider
        );

    /// <inheritdoc />
    public BroadcastBehaviorEntry<T, TResponse>[] GetBroadcastBehaviors<T, TResponse>() =>
        (BroadcastBehaviorEntry<T, TResponse>[])_cache.GetOrAdd(
            typeof(ITalynBroadcastBehavior<T, TResponse>),
            static (_, sp) => sp.GetServices<ITalynBroadcastBehavior<T, TResponse>>()
                .Select(static b => new BroadcastBehaviorEntry<T, TResponse> { Behavior = b, BehaviorType = b.GetType() })
                .ToArray(),
            _provider
        );
}

/// <summary>
/// Main registry combining runtime connections with optional external resolvers.
/// </summary>
/// <remarks>
/// <para>
/// This registry serves as both the runtime storage for <see cref="Connect{T}"/>/<see cref="Disconnect{T}"/>
/// operations and the aggregator for external <see cref="IReceiverResolver"/> sources.
/// </para>
/// <para>
/// <b>Runtime receivers:</b> Stored internally with disconnect detection and execution tracking
/// via <see cref="ReceiverEntry{T}"/>. Cache is invalidated on Connect/Disconnect.
/// </para>
/// <para>
/// <b>External resolvers:</b> Combined with runtime receivers. Resolver results are cached
/// per-resolver and combined lazily.
/// </para>
/// </remarks>
internal sealed class TalynRegistry : ITalynRegistry {
    readonly ConcurrentDictionary<Type, object>                 _combinedCache = new();
    readonly ConcurrentDictionary<Type, object>                 _behaviorCache = new();
    readonly IReceiverResolver[]                                _resolvers;
    readonly IBehaviorResolver[]                                _behaviorResolvers;
    readonly ConcurrentDictionary<Type, ImmutableArray<object>> _runtimeEntries = new();
    readonly ConcurrentDictionary<Type, ImmutableArray<object>> _runtimeBehaviors = new();

    /// <summary>
    /// Creates a standalone registry without external resolvers.
    /// </summary>
    public TalynRegistry() : this([], []) { }

    /// <summary>
    /// Creates a registry with external receiver resolvers.
    /// </summary>
    /// <param name="resolvers">External receiver resolvers (e.g., DI container).</param>
    public TalynRegistry(params IReceiverResolver[] resolvers) : this(resolvers, []) { }

    /// <summary>
    /// Creates a registry with external receiver and behavior resolvers.
    /// </summary>
    /// <param name="resolvers">External receiver resolvers (e.g., DI container).</param>
    /// <param name="behaviorResolvers">External behavior resolvers (e.g., DI container).</param>
    public TalynRegistry(IReceiverResolver[] resolvers, IBehaviorResolver[] behaviorResolvers) {
        _resolvers = resolvers;
        _behaviorResolvers = behaviorResolvers;
    }

    /// <inheritdoc />
    public ReceiverEntry<T>[] GetReceivers<T>() {
        var key = typeof(IReceiver<T>);

        // Return cached combined result if available
        if (_combinedCache.TryGetValue(key, out var cached) && cached is ReceiverEntry<T>[] arr)
            return arr;

        // Build combined result
        var result = BuildCombinedResult<T>();
        _combinedCache[key] = result;
        return result;
    }

    /// <inheritdoc />
    public ReceiverEntry<T, TResponse>[] GetReceivers<T, TResponse>() {
        var key = typeof(IReceiver<T, TResponse>);

        // Return cached combined result if available
        if (_combinedCache.TryGetValue(key, out var cached) && cached is ReceiverEntry<T, TResponse>[] arr)
            return arr;

        // Build combined result
        var result = BuildCombinedResult<T, TResponse>();
        _combinedCache[key] = result;
        return result;
    }

    /// <inheritdoc />
    public void Connect<T>(IReceiver<T> receiver) {
        var key   = typeof(IReceiver<T>);
        var entry = new ReceiverEntry<T>(receiver);

        _combinedCache.TryRemove(key, out _); // Invalidate cache

        _runtimeEntries.AddOrUpdate(
            key,
            _ => [entry],
            (_, existing) => existing.Add(entry)
        );
    }

    /// <inheritdoc />
    public ReceiverEntry<T>? Disconnect<T>(IReceiver<T> receiver) {
        var               key               = typeof(IReceiver<T>);
        ReceiverEntry<T>? disconnectedEntry = null;

        _combinedCache.TryRemove(key, out _); // Invalidate cache

        _runtimeEntries.AddOrUpdate(
            key,
            _ => ImmutableArray<object>.Empty,
            (_, existing) => {
                // Find and mark the entry as disconnected
                foreach (var obj in existing)
                    if (obj is ReceiverEntry<T> entry && ReferenceEquals(entry.Receiver, receiver)) {
                        entry.IsDisconnected = true; // Signal to consumer to skip
                        disconnectedEntry    = entry;
                        break;
                    }

                // Remove from the array
                return existing.RemoveAll(obj =>
                                              obj is ReceiverEntry<T> e && ReferenceEquals(e.Receiver, receiver)
                );
            }
        );

        return disconnectedEntry;
    }

    /// <inheritdoc />
    public void Connect<T, TResponse>(IReceiver<T, TResponse> receiver) {
        var key   = typeof(IReceiver<T, TResponse>);
        var entry = new ReceiverEntry<T, TResponse>(receiver);

        _combinedCache.TryRemove(key, out _); // Invalidate cache

        _runtimeEntries.AddOrUpdate(
            key,
            _ => ImmutableArray.Create<object>(entry),
            (_, existing) => existing.Add(entry)
        );
    }

    /// <inheritdoc />
    public ReceiverEntry<T, TResponse>? Disconnect<T, TResponse>(IReceiver<T, TResponse> receiver) {
        var                          key               = typeof(IReceiver<T, TResponse>);
        ReceiverEntry<T, TResponse>? disconnectedEntry = null;

        _combinedCache.TryRemove(key, out _); // Invalidate cache

        _runtimeEntries.AddOrUpdate(
            key,
            _ => ImmutableArray<object>.Empty,
            (_, existing) => {
                // Find and mark the entry as disconnected
                foreach (var obj in existing)
                    if (obj is ReceiverEntry<T, TResponse> entry && ReferenceEquals(entry.Receiver, receiver)) {
                        entry.IsDisconnected = true; // Signal to consumer to skip
                        disconnectedEntry    = entry;
                        break;
                    }

                // Remove from the array
                return existing.RemoveAll(obj =>
                                              obj is ReceiverEntry<T, TResponse> e && ReferenceEquals(e.Receiver, receiver)
                );
            }
        );

        return disconnectedEntry;
    }

    /// <inheritdoc />
    public BehaviorEntry<T>[] GetBehaviors<T>() {
        var key = typeof(ITalynBehavior<T>);

        // Return cached combined result if available
        if (_behaviorCache.TryGetValue(key, out var cached) && cached is BehaviorEntry<T>[] arr)
            return arr;

        // Build combined result
        var result = BuildCombinedBehaviors<T>();
        _behaviorCache[key] = result;
        return result;
    }

    /// <inheritdoc />
    public BehaviorEntry<T, TResponse>[] GetBehaviors<T, TResponse>() {
        var key = typeof(ITalynBehavior<T, TResponse>);

        // Return cached combined result if available
        if (_behaviorCache.TryGetValue(key, out var cached) && cached is BehaviorEntry<T, TResponse>[] arr)
            return arr;

        // Build combined result
        var result = BuildCombinedBehaviors<T, TResponse>();
        _behaviorCache[key] = result;
        return result;
    }

    /// <inheritdoc />
    public BroadcastBehaviorEntry<T, TResponse>[] GetBroadcastBehaviors<T, TResponse>() {
        var key = typeof(ITalynBroadcastBehavior<T, TResponse>);

        // Return cached combined result if available
        if (_behaviorCache.TryGetValue(key, out var cached) && cached is BroadcastBehaviorEntry<T, TResponse>[] arr)
            return arr;

        // Build combined result
        var result = BuildCombinedBroadcastBehaviors<T, TResponse>();
        _behaviorCache[key] = result;
        return result;
    }

    /// <inheritdoc />
    public void Connect<T>(ITalynBehavior<T> behavior) {
        var key   = typeof(ITalynBehavior<T>);
        var entry = new BehaviorEntry<T> { Behavior = behavior, BehaviorType = behavior.GetType() };

        _behaviorCache.TryRemove(key, out _); // Invalidate cache

        _runtimeBehaviors.AddOrUpdate(
            key,
            _ => [entry],
            (_, existing) => existing.Add(entry)
        );
    }

    /// <inheritdoc />
    public void Connect<T, TResponse>(ITalynBehavior<T, TResponse> behavior) {
        var key   = typeof(ITalynBehavior<T, TResponse>);
        var entry = new BehaviorEntry<T, TResponse> { Behavior = behavior, BehaviorType = behavior.GetType() };

        _behaviorCache.TryRemove(key, out _); // Invalidate cache

        _runtimeBehaviors.AddOrUpdate(
            key,
            _ => ImmutableArray.Create<object>(entry),
            (_, existing) => existing.Add(entry)
        );
    }

    /// <inheritdoc />
    public void ConnectBroadcast<T, TResponse>(ITalynBroadcastBehavior<T, TResponse> behavior) {
        var key   = typeof(ITalynBroadcastBehavior<T, TResponse>);
        var entry = new BroadcastBehaviorEntry<T, TResponse> { Behavior = behavior, BehaviorType = behavior.GetType() };

        _behaviorCache.TryRemove(key, out _); // Invalidate cache

        _runtimeBehaviors.AddOrUpdate(
            key,
            _ => ImmutableArray.Create<object>(entry),
            (_, existing) => existing.Add(entry)
        );
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        Clear();
        return ValueTask.CompletedTask;
    }

    ReceiverEntry<T>[] BuildCombinedResult<T>() {
        var key = typeof(IReceiver<T>);

        // Get runtime entries
        var runtimeEntries = _runtimeEntries.TryGetValue(key, out var list)
            ? list.OfType<ReceiverEntry<T>>().ToArray()
            : [];

        // If no resolvers, return runtime only
        if (_resolvers.Length == 0)
            return runtimeEntries;

        // Combine resolver results
        var totalCount = runtimeEntries.Length;

        foreach (var resolver in _resolvers)
            totalCount += resolver.GetReceivers<T>().Length;

        if (totalCount == 0)
            return [];

        if (totalCount == runtimeEntries.Length)
            return runtimeEntries;

        var combined = new ReceiverEntry<T>[totalCount];
        var offset   = 0;

        // Add resolver entries first (they're typically DI singletons)
        foreach (var resolver in _resolvers) {
            var resolverEntries = resolver.GetReceivers<T>();
            resolverEntries.CopyTo(combined, offset);
            offset += resolverEntries.Length;
        }

        // Add runtime entries last
        runtimeEntries.CopyTo(combined, offset);

        return combined;
    }

    ReceiverEntry<T, TResponse>[] BuildCombinedResult<T, TResponse>() {
        var key = typeof(IReceiver<T, TResponse>);

        // Get runtime entries
        var runtimeEntries = _runtimeEntries.TryGetValue(key, out var list)
            ? list.OfType<ReceiverEntry<T, TResponse>>().ToArray()
            : [];

        // If no resolvers, return runtime only
        if (_resolvers.Length == 0)
            return runtimeEntries;

        // Combine resolver results
        var totalCount = runtimeEntries.Length;
        foreach (var resolver in _resolvers)
            totalCount += resolver.GetReceivers<T, TResponse>().Length;

        if (totalCount == 0)
            return [];

        if (totalCount == runtimeEntries.Length)
            return runtimeEntries;

        var combined = new ReceiverEntry<T, TResponse>[totalCount];
        var offset   = 0;

        // Add resolver entries first (they're typically DI singletons)
        foreach (var resolver in _resolvers) {
            var resolverEntries = resolver.GetReceivers<T, TResponse>();
            resolverEntries.CopyTo(combined, offset);
            offset += resolverEntries.Length;
        }

        // Add runtime entries last
        runtimeEntries.CopyTo(combined, offset);

        return combined;
    }

    BehaviorEntry<T>[] BuildCombinedBehaviors<T>() {
        var key = typeof(ITalynBehavior<T>);

        // Get runtime entries
        var runtimeEntries = _runtimeBehaviors.TryGetValue(key, out var list)
            ? list.OfType<BehaviorEntry<T>>().ToArray()
            : [];

        // If no resolvers, return runtime only
        if (_behaviorResolvers.Length == 0)
            return runtimeEntries;

        // Combine resolver results
        var totalCount = runtimeEntries.Length;
        foreach (var resolver in _behaviorResolvers)
            totalCount += resolver.GetBehaviors<T>().Length;

        if (totalCount == 0)
            return [];

        if (totalCount == runtimeEntries.Length)
            return runtimeEntries;

        var combined = new BehaviorEntry<T>[totalCount];
        var offset   = 0;

        // Add resolver entries first (they're typically DI singletons)
        foreach (var resolver in _behaviorResolvers) {
            var resolverEntries = resolver.GetBehaviors<T>();
            resolverEntries.CopyTo(combined, offset);
            offset += resolverEntries.Length;
        }

        // Add runtime entries last
        runtimeEntries.CopyTo(combined, offset);

        return combined;
    }

    BehaviorEntry<T, TResponse>[] BuildCombinedBehaviors<T, TResponse>() {
        var key = typeof(ITalynBehavior<T, TResponse>);

        // Get runtime entries
        var runtimeEntries = _runtimeBehaviors.TryGetValue(key, out var list)
            ? list.OfType<BehaviorEntry<T, TResponse>>().ToArray()
            : [];

        // If no resolvers, return runtime only
        if (_behaviorResolvers.Length == 0)
            return runtimeEntries;

        // Combine resolver results
        var totalCount = runtimeEntries.Length;
        foreach (var resolver in _behaviorResolvers)
            totalCount += resolver.GetBehaviors<T, TResponse>().Length;

        if (totalCount == 0)
            return [];

        if (totalCount == runtimeEntries.Length)
            return runtimeEntries;

        var combined = new BehaviorEntry<T, TResponse>[totalCount];
        var offset   = 0;

        // Add resolver entries first (they're typically DI singletons)
        foreach (var resolver in _behaviorResolvers) {
            var resolverEntries = resolver.GetBehaviors<T, TResponse>();
            resolverEntries.CopyTo(combined, offset);
            offset += resolverEntries.Length;
        }

        // Add runtime entries last
        runtimeEntries.CopyTo(combined, offset);

        return combined;
    }

    BroadcastBehaviorEntry<T, TResponse>[] BuildCombinedBroadcastBehaviors<T, TResponse>() {
        var key = typeof(ITalynBroadcastBehavior<T, TResponse>);

        // Get runtime entries
        var runtimeEntries = _runtimeBehaviors.TryGetValue(key, out var list)
            ? list.OfType<BroadcastBehaviorEntry<T, TResponse>>().ToArray()
            : [];

        // If no resolvers, return runtime only
        if (_behaviorResolvers.Length == 0)
            return runtimeEntries;

        // Combine resolver results
        var totalCount = runtimeEntries.Length;
        foreach (var resolver in _behaviorResolvers)
            totalCount += resolver.GetBroadcastBehaviors<T, TResponse>().Length;

        if (totalCount == 0)
            return [];

        if (totalCount == runtimeEntries.Length)
            return runtimeEntries;

        var combined = new BroadcastBehaviorEntry<T, TResponse>[totalCount];
        var offset   = 0;

        // Add resolver entries first (they're typically DI singletons)
        foreach (var resolver in _behaviorResolvers) {
            var resolverEntries = resolver.GetBroadcastBehaviors<T, TResponse>();
            resolverEntries.CopyTo(combined, offset);
            offset += resolverEntries.Length;
        }

        // Add runtime entries last
        runtimeEntries.CopyTo(combined, offset);

        return combined;
    }

    /// <summary>
    /// Clears all runtime connections.
    /// </summary>
    public void Clear() {
        _runtimeEntries.Clear();
        _combinedCache.Clear();
        _runtimeBehaviors.Clear();
        _behaviorCache.Clear();
    }
}

#endregion
