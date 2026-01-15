# Tech Spec: Talyn Generic Behaviors & Streaming Pipeline

> **Artifact**: `_bmad-output/implementation-artifacts/tech-spec-talyn-behaviors.md`
> **Status**: Ready for implementation (Phase A)
> **Prerequisite**: Basic `ITalynMiddleware` already implemented

## Overview

This spec defines the "ultimate middleware pipeline" for Talyn - adding type-safe generic behaviors, open generic DI registration, and `IAsyncEnumerable` streaming support to match/exceed MediatR/DispatchR/Wolverine capabilities.

---

## Current State vs. Industry Standard

| Capability | MediatR/DispatchR | Talyn (Current) |
|------------|-------------------|-----------------|
| Type-safe middleware | ✅ `IPipelineBehavior<T, TResponse>` | ❌ Non-generic |
| Response interception | ✅ `Task<TResponse>` from `next()` | ❌ `ValueTask` (void) |
| Open generic DI | ✅ `typeof(IPipelineBehavior<,>)` | ❌ Explicit registration |
| Per-message-type | ✅ Constraint-based | ❌ Global only |
| Streaming pipeline | ✅ `IStreamPipelineBehavior` | ❌ Not supported |
| IAsyncEnumerable | ✅ `IStreamRequest<T>` | ❌ Not supported |

---

## Proposed Architecture

### Layer 1: Keep Non-Generic (Simple Global Middleware)
```csharp
// KEEP AS-IS - for simple cross-cutting concerns
public interface ITalynMiddleware {
    ValueTask InvokeAsync(TalynMessageContext context, TalynMiddlewareDelegate next);
}
```

### Layer 2: Add Generic Middleware (Type-Safe)
```csharp
// NEW - for type-specific middleware with response access
public interface ITalynBehavior<TMessage, TResponse> {
    ValueTask<TResponse> HandleAsync(
        TMessage message,
        BehaviorDelegate<TResponse> next,
        CancellationToken ct
    );
}

public delegate ValueTask<TResponse> BehaviorDelegate<TResponse>(CancellationToken ct);

// Fire-and-forget variant (no response)
public interface ITalynBehavior<TMessage> {
    ValueTask HandleAsync(
        TMessage message,
        BehaviorDelegate next,
        CancellationToken ct
    );
}

public delegate ValueTask BehaviorDelegate(CancellationToken ct);
```

### Layer 3: Add Streaming Support
```csharp
// NEW - streaming receiver
public interface IStreamReceiver<in TMessage, out TResponse> {
    IAsyncEnumerable<TResponse> HandleAsync(TMessage message, CancellationToken ct);
}

// NEW - streaming middleware
public interface IStreamBehavior<TMessage, TResponse> {
    IAsyncEnumerable<TResponse> HandleAsync(
        TMessage message,
        StreamBehaviorDelegate<TResponse> next,
        CancellationToken ct
    );
}

public delegate IAsyncEnumerable<TResponse> StreamBehaviorDelegate<TResponse>(CancellationToken ct);
```

---

## Middleware Execution Model

```
┌─────────────────────────────────────────────────────────────┐
│                    ITalynMiddleware[]                        │
│  (Global, non-generic, wraps everything)                    │
│                                                             │
│   ┌─────────────────────────────────────────────────────┐   │
│   │           ITalynBehavior<T, TResponse>[]            │   │
│   │  (Per-message-type, generic, response access)       │   │
│   │                                                     │   │
│   │   ┌─────────────────────────────────────────────┐   │   │
│   │   │              Receivers                      │   │   │
│   │   │  IReceiver<T> or IReceiver<T, TResponse>   │   │   │
│   │   └─────────────────────────────────────────────┘   │   │
│   │                                                     │   │
│   └─────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Execution Order:**
1. Global middleware (ITalynMiddleware) - Before
2. Type-specific behaviors (ITalynBehavior<T,R>) - Before
3. Receivers execute
4. Type-specific behaviors - After (with response)
5. Global middleware - After

---

## Key Design Decisions

### Decision 1: Response in Delegate
```csharp
// MediatR pattern - response flows through pipeline
public async ValueTask<TResponse> HandleAsync(
    TMessage message,
    BehaviorDelegate<TResponse> next,
    CancellationToken ct
) {
    // PRE
    Log($"Handling {typeof(TMessage).Name}");

    var response = await next(ct);  // ← Response captured

    // POST - can transform response!
    return Enrich(response);
}
```

### Decision 2: Open Generic Registration
```csharp
// Enable MediatR-style registration
services.AddTalyn(bus => bus
    // Open generic - applies to ALL message types
    .UseBehavior(typeof(ITalynBehavior<,>), typeof(LoggingBehavior<,>))

    // Constrained generic - applies to messages implementing IValidatable
    .UseBehavior(typeof(ITalynBehavior<,>), typeof(ValidationBehavior<,>))

    // Explicit type - only for OrderCommand
    .UseBehavior<OrderCommand, OrderResult, AuditBehavior>()
);
```

### Decision 3: Streaming as First-Class
```csharp
// New dispatch method on ITalynBus
IAsyncEnumerable<TResponse> StreamAsync<TMessage, TResponse>(
    TMessage message,
    CancellationToken ct = default
);

// Usage
await foreach (var item in bus.StreamAsync<DataRequest, DataChunk>(request, ct)) {
    Process(item);
}
```

---

## Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `TalynBehaviors.cs` | **CREATE** | Generic behavior interfaces |
| `TalynStreaming.cs` | **CREATE** | Streaming receiver & behavior interfaces |
| `TalynBus.cs` | **MODIFY** | Add behavior pipeline, streaming dispatch |
| `TalynRegistry.cs` | **MODIFY** | Register streaming receivers, behaviors |
| `TalynDependencyInjection.cs` | **MODIFY** | Open generic registration support |

---

## Implementation Phases

### Phase A: Generic Behaviors (Type-Safe Middleware)
1. Create `ITalynBehavior<T>` and `ITalynBehavior<T, TResponse>`
2. Add behavior resolution to registry
3. Build per-message-type behavior pipelines (cached)
4. Integrate with existing dispatch methods

### Phase B: Open Generic DI Registration
1. Add `UseBehavior(Type openGeneric, Type implementation)` to builder
2. Implement behavior factory resolution at dispatch time
3. Cache resolved behavior chains per message type

### Phase C: Streaming Support
1. Create `IStreamReceiver<T, TResponse>` interface
2. Create `IStreamBehavior<T, TResponse>` interface
3. Add `StreamAsync<T, TResponse>()` to ITalynBus
4. Implement streaming pipeline with yield return semantics

---

## Usage Examples

### Generic Behavior (Type-Safe)
```csharp
public class ValidationBehavior<TMessage, TResponse> : ITalynBehavior<TMessage, TResponse>
    where TMessage : IValidatable
{
    public async ValueTask<TResponse> HandleAsync(
        TMessage message,
        BehaviorDelegate<TResponse> next,
        CancellationToken ct
    ) {
        message.Validate();  // Compile-time type safety!
        return await next(ct);
    }
}

// Registration - applies to ALL IValidatable messages
services.AddTalyn(bus => bus
    .UseBehavior(typeof(ITalynBehavior<,>), typeof(ValidationBehavior<,>))
);
```

### Streaming
```csharp
// Streaming receiver
public class DataStreamReceiver : IStreamReceiver<DataRequest, DataChunk> {
    public async IAsyncEnumerable<DataChunk> HandleAsync(
        DataRequest request,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        await foreach (var chunk in database.StreamAsync(request.Query, ct))
            yield return chunk;
    }
}

// Streaming behavior
public class StreamMetricsBehavior<T, R> : IStreamBehavior<T, R> {
    public async IAsyncEnumerable<R> HandleAsync(
        T message,
        StreamBehaviorDelegate<R> next,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var count = 0;
        await foreach (var item in next(ct)) {
            count++;
            yield return item;
        }
        metrics.RecordStreamCount(typeof(T), count);
    }
}

// Usage
await foreach (var chunk in bus.StreamAsync<DataRequest, DataChunk>(request)) {
    ProcessChunk(chunk);
}
```

### Combined Pipeline
```csharp
services.AddTalyn(bus => bus
    // Layer 1: Global middleware (non-generic, all messages)
    .UseMiddleware<TracingMiddleware>()
    .UseMiddleware<MetricsMiddleware>()

    // Layer 2: Generic behaviors (type-safe, per-message-type)
    .UseBehavior(typeof(ITalynBehavior<,>), typeof(LoggingBehavior<,>))
    .UseBehavior(typeof(ITalynBehavior<,>), typeof(ValidationBehavior<,>))

    // Layer 2: Explicit type behavior
    .UseBehavior<OrderCommand, OrderResult, OrderAuditBehavior>()

    // Streaming behaviors
    .UseStreamBehavior(typeof(IStreamBehavior<,>), typeof(StreamMetricsBehavior<,>))
);
```

---

## Verification Plan

1. **Unit Tests**
   - Generic behavior pipeline execution
   - Open generic DI resolution
   - Response transformation in behaviors
   - Streaming receiver enumeration
   - Streaming behavior interception

2. **Integration Tests**
   - Combined global + generic middleware
   - Constraint-based behavior filtering (IValidatable)
   - Streaming with cancellation

3. **Performance Tests**
   - Behavior pipeline caching (no per-call resolution)
   - Streaming backpressure handling
   - Zero middleware fast path preserved

---

## Design Decisions (Confirmed)

1. **Naming**: `ITalynBehavior<T, R>` (follows MediatR pattern)
2. **Scope**: Phase A first, detailed specs for B and C for future
3. **Fire-and-forget**: Support both patterns (unlike MediatR which excludes INotification)
   - `ITalynBehavior<T>` for Transmit (no response)
   - `ITalynBehavior<T, TResponse>` for Broadcast (with response)

---

## Detailed Implementation Specifications

### PHASE A: Generic Behaviors (Type-Safe Middleware)

#### A.1 Create `TalynBehaviors.cs`

```csharp
namespace Talyn;

#region Delegates

/// <summary>
/// Delegate to invoke the next behavior or terminal handler (fire-and-forget).
/// </summary>
public delegate ValueTask BehaviorDelegate(CancellationToken ct);

/// <summary>
/// Delegate to invoke the next behavior or terminal handler (request/response).
/// </summary>
public delegate ValueTask<TResponse> BehaviorDelegate<TResponse>(CancellationToken ct);

#endregion

#region Behavior Interfaces

/// <summary>
/// Type-safe behavior for fire-and-forget messages (Transmit pattern).
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <remarks>
/// Unlike <see cref="ITalynMiddleware"/> which operates on untyped context,
/// this interface provides compile-time type safety and can be constrained
/// (e.g., <c>where TMessage : IValidatable</c>).
/// </remarks>
public interface ITalynBehavior<in TMessage> {
    /// <summary>
    /// Handles the message, optionally calling the next behavior in the pipeline.
    /// </summary>
    /// <param name="message">The strongly-typed message.</param>
    /// <param name="next">Delegate to invoke the next behavior or receivers.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask HandleAsync(TMessage message, BehaviorDelegate next, CancellationToken ct);
}

/// <summary>
/// Type-safe behavior for request/response messages (Broadcast pattern).
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// The response flows through the pipeline, enabling transformation,
/// enrichment, or caching of responses by behaviors.
/// </remarks>
public interface ITalynBehavior<in TMessage, TResponse> {
    /// <summary>
    /// Handles the message and returns the (potentially transformed) response.
    /// </summary>
    /// <param name="message">The strongly-typed message.</param>
    /// <param name="next">Delegate to invoke the next behavior or receivers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response, potentially transformed by this behavior.</returns>
    ValueTask<TResponse> HandleAsync(TMessage message, BehaviorDelegate<TResponse> next, CancellationToken ct);
}

#endregion

#region Behavior Entry (Internal)

/// <summary>
/// Internal wrapper for behavior instances with type metadata.
/// </summary>
internal readonly struct BehaviorEntry<TMessage> {
    public required ITalynBehavior<TMessage> Behavior { get; init; }
    public required Type BehaviorType { get; init; }
}

internal readonly struct BehaviorEntry<TMessage, TResponse> {
    public required ITalynBehavior<TMessage, TResponse> Behavior { get; init; }
    public required Type BehaviorType { get; init; }
}

#endregion
```

#### A.2 Modify `TalynRegistry.cs`

Add behavior storage and resolution:

```csharp
// New fields
readonly ConcurrentDictionary<Type, object> _behaviorCache = new();

// New methods
public BehaviorEntry<T>[] GetBehaviors<T>() {
    var key = typeof(T);
    if (_behaviorCache.TryGetValue(key, out var cached))
        return (BehaviorEntry<T>[])cached;

    var behaviors = ResolveBehaviors<T>();
    _behaviorCache.TryAdd(key, behaviors);
    return behaviors;
}

public BehaviorEntry<T, TResponse>[] GetBehaviors<T, TResponse>() {
    var key = (typeof(T), typeof(TResponse));
    // Similar caching logic...
}
```

#### A.3 Modify `TalynBus.cs`

Add behavior pipeline execution:

```csharp
#region Behavior Pipeline

BehaviorDelegate BuildBehaviorPipeline<T>(
    BehaviorEntry<T>[] behaviors,
    ReceiverEntry<T>[] receivers,
    T message,
    ExecutionMode mode
) {
    // Terminal: invoke receivers
    BehaviorDelegate terminal = async ct => {
        await InvokeEntriesAsync(receivers, message, mode, ct).ConfigureAwait(false);
    };

    // No behaviors? Return terminal directly
    if (behaviors.Length == 0)
        return terminal;

    // Build pipeline from last to first
    var next = terminal;
    for (var i = behaviors.Length - 1; i >= 0; i--) {
        var behavior = behaviors[i].Behavior;
        var current = next;
        next = ct => behavior.HandleAsync(message, current, ct);
    }

    return next;
}

BehaviorDelegate<ReceiverReply<TResponse>[]> BuildBehaviorPipeline<T, TResponse>(
    BehaviorEntry<T, TResponse>[] behaviors,
    ReceiverEntry<T, TResponse>[] receivers,
    T message,
    ExecutionMode mode
) {
    // Terminal: invoke receivers, collect responses
    BehaviorDelegate<ReceiverReply<TResponse>[]> terminal = async ct => {
        return await ExecuteEntriesAsync(receivers, message, mode, ct).ConfigureAwait(false);
    };

    if (behaviors.Length == 0)
        return terminal;

    var next = terminal;
    for (var i = behaviors.Length - 1; i >= 0; i--) {
        var behavior = behaviors[i].Behavior;
        var current = next;
        // Note: Behavior returns single TResponse, but we aggregate ReceiverReply[]
        // This requires adapter logic - see Decision Point below
        next = ct => behavior.HandleAsync(message, /* adapter */, ct);
    }

    return next;
}

#endregion
```

#### A.4 Decision: Both Behavior Interfaces (CONFIRMED)

**Problem:** Receivers return `ReceiverReply<TResponse>[]` (multiple attributed responses), but MediatR-style behaviors expect single `TResponse`.

**Solution:** Provide both interfaces:

```csharp
/// <summary>
/// Simple behavior - works with first successful response.
/// Use when you don't need per-receiver attribution.
/// </summary>
public interface ITalynBehavior<in TMessage, TResponse> {
    ValueTask<TResponse> HandleAsync(
        TMessage message,
        BehaviorDelegate<TResponse> next,
        CancellationToken ct
    );
}

/// <summary>
/// Broadcast behavior - full access to all receiver responses with attribution.
/// Use when you need timing data, error handling per-receiver, or response aggregation.
/// </summary>
public interface ITalynBroadcastBehavior<in TMessage, TResponse> {
    ValueTask<ReceiverReply<TResponse>[]> HandleAsync(
        TMessage message,
        BehaviorDelegate<ReceiverReply<TResponse>[]> next,
        CancellationToken ct
    );
}
```

**Adapter logic in TalynBus:**
```csharp
// When ITalynBehavior<T,R> is used with Broadcast:
// 1. Execute receivers → ReceiverReply<R>[]
// 2. Extract first success → R (or throw if all failed)
// 3. Pass R to behavior chain
// 4. Wrap result back into ReceiverReply<R>[]

// When ITalynBroadcastBehavior<T,R> is used:
// Pass ReceiverReply<R>[] directly through chain
```

---

### PHASE B: Open Generic DI Registration (Future)

#### B.1 Modify `TalynBuilder` Registration

```csharp
// New fields
readonly List<(Type InterfaceType, Type ImplementationType)> _openGenericBehaviors = new();

// New method
public TalynBuilder UseBehavior(Type behaviorInterface, Type behaviorImplementation) {
    // Validate: must be open generic ITalynBehavior<,> or ITalynBehavior<>
    ValidateOpenGeneric(behaviorInterface, behaviorImplementation);
    _openGenericBehaviors.Add((behaviorInterface, behaviorImplementation));
    return this;
}

// Explicit registration
public TalynBuilder UseBehavior<TMessage, TResponse, TBehavior>()
    where TBehavior : class, ITalynBehavior<TMessage, TResponse>
{
    Services.TryAddSingleton<TBehavior>();
    Services.AddSingleton<ITalynBehavior<TMessage, TResponse>>(
        sp => sp.GetRequiredService<TBehavior>()
    );
    return this;
}
```

#### B.2 Runtime Resolution with Caching

```csharp
// In TalynRegistry or dedicated BehaviorResolver
public BehaviorEntry<T, TResponse>[] ResolveBehaviors<T, TResponse>() {
    var messageType = typeof(T);
    var responseType = typeof(TResponse);

    var behaviors = new List<BehaviorEntry<T, TResponse>>();

    foreach (var (interfaceType, implType) in _openGenericBehaviors) {
        // Check if implementation satisfies constraints
        var closedInterface = interfaceType.MakeGenericType(messageType, responseType);
        var closedImpl = implType.MakeGenericType(messageType, responseType);

        if (ConstraintsSatisfied(closedImpl, messageType, responseType)) {
            var instance = _serviceProvider.GetService(closedImpl)
                ?? ActivatorUtilities.CreateInstance(_serviceProvider, closedImpl);

            behaviors.Add(new BehaviorEntry<T, TResponse> {
                Behavior = (ITalynBehavior<T, TResponse>)instance,
                BehaviorType = closedImpl
            });
        }
    }

    return behaviors.ToArray();
}

bool ConstraintsSatisfied(Type closedImpl, Type messageType, Type responseType) {
    // Check generic constraints like "where TMessage : IValidatable"
    try {
        // If MakeGenericType succeeded without exception, constraints are met
        return true;
    }
    catch (ArgumentException) {
        return false; // Constraints not satisfied
    }
}
```

---

### PHASE C: Streaming Support (Future)

#### C.1 Create `TalynStreaming.cs`

```csharp
namespace Talyn;

#region Streaming Receiver

/// <summary>
/// Receiver that yields multiple responses as an async stream.
/// </summary>
public interface IStreamReceiver<in TMessage, out TResponse> {
    IAsyncEnumerable<TResponse> HandleAsync(TMessage message, CancellationToken ct);
}

#endregion

#region Streaming Behavior

/// <summary>
/// Delegate to invoke the next streaming behavior or receiver.
/// </summary>
public delegate IAsyncEnumerable<TResponse> StreamBehaviorDelegate<out TResponse>(CancellationToken ct);

/// <summary>
/// Behavior that intercepts streaming operations.
/// </summary>
public interface IStreamBehavior<in TMessage, TResponse> {
    IAsyncEnumerable<TResponse> HandleAsync(
        TMessage message,
        StreamBehaviorDelegate<TResponse> next,
        CancellationToken ct
    );
}

#endregion
```

#### C.2 Add to `ITalynBus`

```csharp
public interface ITalynBus : ITalynMediator, IAsyncDisposable {
    // Existing methods...

    /// <summary>
    /// Streams responses from a streaming receiver.
    /// </summary>
    IAsyncEnumerable<TResponse> StreamAsync<TMessage, TResponse>(
        TMessage message,
        CancellationToken ct = default
    );
}
```

#### C.3 Implement in `TalynBus`

```csharp
public async IAsyncEnumerable<TResponse> StreamAsync<TMessage, TResponse>(
    TMessage message,
    [EnumeratorCancellation] CancellationToken ct = default
) {
    var receivers = _registry.GetStreamReceivers<TMessage, TResponse>();
    var behaviors = _registry.GetStreamBehaviors<TMessage, TResponse>();

    if (receivers.Length == 0)
        yield break;

    // Build streaming pipeline
    StreamBehaviorDelegate<TResponse> terminal = ct2 => StreamFromReceivers(receivers, message, ct2);

    var pipeline = terminal;
    for (var i = behaviors.Length - 1; i >= 0; i--) {
        var behavior = behaviors[i];
        var current = pipeline;
        pipeline = ct2 => behavior.HandleAsync(message, current, ct2);
    }

    // Execute and yield
    await foreach (var item in pipeline(ct).WithCancellation(ct).ConfigureAwait(false)) {
        yield return item;
    }
}

private async IAsyncEnumerable<TResponse> StreamFromReceivers<TMessage, TResponse>(
    IStreamReceiver<TMessage, TResponse>[] receivers,
    TMessage message,
    [EnumeratorCancellation] CancellationToken ct
) {
    // Merge streams from all receivers (interleaved or sequential based on config)
    foreach (var receiver in receivers) {
        await foreach (var item in receiver.HandleAsync(message, ct).ConfigureAwait(false)) {
            yield return item;
        }
    }
}
```

---

## Summary: What Each Phase Delivers

| Phase | Capability | Effort |
|-------|------------|--------|
| **A** | Type-safe behaviors, response transformation, per-message-type middleware | Medium |
| **B** | Open generic DI, constraint-based filtering (`where T : IValidatable`) | Medium |
| **C** | `IAsyncEnumerable` streaming with streaming behaviors | High |

---

## Implementation Checklist (Phase A)

### Step 0: Save Tech Spec
- [x] Copy this plan to `_bmad-output/implementation-artifacts/tech-spec-talyn-behaviors.md`

### Step 1: Create `TalynBehaviors.cs`
- [ ] `BehaviorDelegate` and `BehaviorDelegate<TResponse>` delegates
- [ ] `ITalynBehavior<TMessage>` interface (fire-and-forget)
- [ ] `ITalynBehavior<TMessage, TResponse>` interface (simple response)
- [ ] `ITalynBroadcastBehavior<TMessage, TResponse>` interface (full attribution)
- [ ] `BehaviorEntry<T>` and `BehaviorEntry<T, TResponse>` structs

### Step 2: Modify `TalynRegistry.cs`
- [ ] Add behavior cache (`ConcurrentDictionary`)
- [ ] Add `GetBehaviors<T>()` method
- [ ] Add `GetBehaviors<T, TResponse>()` method
- [ ] Add `GetBroadcastBehaviors<T, TResponse>()` method

### Step 3: Modify `TalynBus.cs`
- [ ] Add `BuildBehaviorPipeline<T>()` for fire-and-forget
- [ ] Add `BuildBehaviorPipeline<T, TResponse>()` for request/response
- [ ] Add adapter logic for `ITalynBehavior<T,R>` → `ReceiverReply<R>[]`
- [ ] Modify `TransmitAsync` to run behaviors
- [ ] Modify `BroadcastAsync` to run behaviors
- [ ] Modify `InvokeAsync` to run behaviors
- [ ] Modify `ScatterAsync` to run behaviors

### Step 4: Modify `TalynDependencyInjection.cs`
- [ ] Add `UseBehavior<T, TBehavior>()` method
- [ ] Add `UseBehavior<T, TResponse, TBehavior>()` method
- [ ] Add `UseBroadcastBehavior<T, TResponse, TBehavior>()` method

### Step 5: Modify `TalynBus.Builder`
- [ ] Add `UseBehavior()` methods for standalone usage

### Step 6: Tests
- [ ] Create `TalynBehaviorTests.cs`
- [ ] Test type safety (compile-time constraints)
- [ ] Test response transformation
- [ ] Test pipeline execution order
- [ ] Test combined middleware + behaviors

---

## Verification Plan

### Phase A Tests
```csharp
[Test]
public async Task Behavior_receives_typed_message() {
    var behavior = new CapturingBehavior<TestMessage, TestResponse>();
    var bus = TalynBus.Create()
        .UseBehavior(behavior)
        .WithReceiver<TestMessage, TestResponse>(msg => new TestResponse())
        .Build();

    await bus.BroadcastAsync<TestMessage, TestResponse>(new TestMessage { Value = 42 });

    Assert.That(behavior.CapturedMessage.Value, Is.EqualTo(42));
}

[Test]
public async Task Behavior_can_transform_response() {
    var bus = TalynBus.Create()
        .UseBehavior(new EnrichingBehavior<TestMessage, TestResponse>())
        .WithReceiver<TestMessage, TestResponse>(msg => new TestResponse { Data = "original" })
        .Build();

    var responses = await bus.BroadcastAsync<TestMessage, TestResponse>(new TestMessage());

    Assert.That(responses[0].Result.Response.Data, Is.EqualTo("original+enriched"));
}
```

### Phase B Tests
```csharp
[Test]
public async Task Open_generic_behavior_applies_to_matching_types() {
    services.AddTalyn(bus => bus
        .UseBehavior(typeof(ITalynBehavior<,>), typeof(ValidationBehavior<,>))
    );

    var bus = provider.GetRequiredService<ITalynBus>();

    // ValidationBehavior only applies where TMessage : IValidatable
    await bus.BroadcastAsync<ValidatableCommand, Result>(new ValidatableCommand());
    // Validation ran ✓

    await bus.BroadcastAsync<NonValidatableCommand, Result>(new NonValidatableCommand());
    // Validation skipped (constraints not met) ✓
}
```

### Phase C Tests
```csharp
[Test]
public async Task Streaming_yields_items_through_behavior() {
    var bus = TalynBus.Create()
        .UseStreamBehavior(new CountingStreamBehavior<DataRequest, DataChunk>())
        .WithStreamReceiver<DataRequest, DataChunk>(new TestDataStreamer())
        .Build();

    var items = new List<DataChunk>();
    await foreach (var chunk in bus.StreamAsync<DataRequest, DataChunk>(new DataRequest())) {
        items.Add(chunk);
    }

    Assert.That(items, Has.Count.EqualTo(10));
}
```
