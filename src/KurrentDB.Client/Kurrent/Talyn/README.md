# Talyn

A high-performance, zero-allocation (after warmup) in-memory message bus for .NET.

Named after the hybrid Leviathan gunship from Farscape — fast, powerful, and a bit unconventional.

## Why Talyn?

Most message buses make you choose: either simple in-memory messaging with MediatR-style patterns, or full-blown distributed messaging with MassTransit/NServiceBus complexity. Talyn takes a different approach.

### What Makes Talyn Different

**Live Receiver Management**

Unlike traditional mediators where handlers are fixed at startup, Talyn lets you connect and disconnect receivers at runtime:

```csharp
var registry = services.GetRequiredService<ITalynRegistry>();

// Connect a receiver dynamically
var handler = new PriceUpdateHandler();
registry.Connect<PriceChanged>(handler);

// Disconnect when no longer needed
registry.Disconnect<PriceChanged>(handler);
```

This enables scenarios like:
- Feature flags that add/remove handlers without restarts
- Plugin systems where modules register their own handlers
- Testing with mock receivers injected mid-test
- Graceful degradation by disconnecting failing receivers

**Bounded Channel with Backpressure**

Messages flow through a bounded `System.Threading.Channels` queue. When the queue fills up, producers wait — no dropped messages, no unbounded memory growth:

```
Producer ──► [Bounded Channel] ──► Consumer ──► Receivers
              (backpressure)       (ordered)
```

**Attributed Responses**

When broadcasting to multiple receivers, you get more than just results — you get metadata about each receiver's performance:

```csharp
var replies = await bus.BroadcastAsync<ValidateOrder, ValidationResult>(order);

// Each reply tells you: WHO responded, WHAT they returned, HOW LONG it took
// ReceiverReply { ReceiverType, Result, Duration }
```

**Zero Allocation After Warmup**

Talyn uses object pooling and the TState pattern to eliminate allocations in the hot path:
- Work items are pooled and reused
- Static lambdas with state tuples avoid closures
- `ValueTask` throughout for sync-completing paths

**Dual Interface Design**

Need to bypass the queue for ultra-low-latency or testing? Use `ITalynMediator` for direct execution. Need production resilience with backpressure? Use `ITalynBus` for queued execution. Same receivers, same registration — different execution paths.

### Talyn vs Others

| Feature | MediatR | Wolverine | Talyn |
|---------|---------|-----------|-------|
| Runtime connect/disconnect | No | No | Yes |
| Bounded backpressure | No | Yes | Yes |
| Response attribution | No | No | Yes |
| Zero-alloc hot path | No | Partial | Yes |
| Direct + Queued modes | No | No | Yes |
| Type-safe behaviors | Yes | Yes | Yes |
| Distributed messaging | No | Yes | No |

Talyn is intentionally **not** a distributed message bus. It's designed for high-performance in-process messaging where you need more control than MediatR offers, but don't need the complexity of distributed messaging.

## Quick Start

### 1. Define a message and receiver

```csharp
// A simple request/response pair
public record GetOrder(Guid Id);
public record Order(Guid Id, string Name);

// A receiver handles the request
public class OrderReceiver : IReceiver<GetOrder, Order> {
    public ValueTask<Order> HandleAsync(GetOrder request, CancellationToken ct) =>
        ValueTask.FromResult(new Order(request.Id, "Sample Order"));
}
```

### 2. Register with DI

```csharp
services.AddTalyn(builder => builder.WithReceiver<OrderReceiver>());
```

### 3. Send messages

```csharp
public class OrderController(ITalynBus bus) {
    public async Task<Order> GetOrder(Guid id) {
        return await bus.TransmitAsync<GetOrder, Order>(new GetOrder(id));
    }
}
```

## How It Works

### Request/Response Flow

```
┌──────────┐    TransmitAsync     ┌─────────┐     dequeue      ┌──────────┐    HandleAsync    ┌──────────┐
│ Producer │ ──────────────────►  │ Channel │ ───────────────► │ Consumer │ ────────────────► │ Receiver │
└──────────┘                      └─────────┘                  └──────────┘                   └──────────┘
     │                                 │                            │                              │
     │                                 │  bounded queue             │  single-threaded             │
     │                                 │  with backpressure         │  FIFO processing             │
     │                                                                                             │
     └─────────────────────────────────────────────────────────────────────────────────────────────┘
                                              response returned
```

### Fire-and-Forget Flow

```
┌──────────┐    TransmitAsync     ┌─────────┐
│ Producer │ ──────────────────►  │ Channel │ ──► (continues immediately, doesn't wait)
└──────────┘                      └─────────┘
                                       │
                                       ▼
                                  ┌──────────┐
                                  │ Consumer │ ──► Receiver A ──► Receiver B ──► ...
                                  └──────────┘
```

### Broadcast with Attribution

```
┌──────────┐   BroadcastAsync    ┌─────────┐    ┌──────────┐    ┌─────────────────┐
│ Producer │ ─────────────────►  │ Channel │ ─► │ Consumer │ ─► │ Validator A     │ ─► Reply { Type, Result, 12ms }
└──────────┘                     └─────────┘    └──────────┘    ├─────────────────┤
     ▲                                               │          │ Validator B     │ ─► Reply { Type, Result, 45ms }
     │                                               │          ├─────────────────┤
     │                                               └────────► │ Validator C     │ ─► Reply { Type, Result, 8ms }
     │                                                          └─────────────────┘
     │                                                                   │
     └───────────────────────────────────────────────────────────────────┘
                              ReceiverReply[] (all responses with metadata)
```

### Direct Execution (Bypass Channel)

```
┌────────┐    InvokeAsync     ┌──────────┐    HandleAsync    ┌──────────┐
│ Caller │ ─────────────────► │ Mediator │ ────────────────► │ Receiver │
└────────┘                    └──────────┘                   └──────────┘
     │                                                            │
     └────────────────────────────────────────────────────────────┘
                    immediate response (no channel)
```

## Usage Examples

### Request/Response (Single Receiver)

The most common pattern — send a request, get a response:

```csharp
public class OrderService(ITalynBus bus) {
    public async Task<Order> GetOrder(Guid id) {
        return await bus.TransmitAsync<GetOrder, Order>(new GetOrder(id));
    }
}
```

### Fire-and-Forget Notifications

Notify receivers without waiting for a response:

```csharp
// Simple notification — returns immediately after queuing
await bus.TransmitAsync(new OrderPlaced(orderId));

// With completion callback (still fire-and-forget, but you get notified when done)
await bus.TransmitAsync(new OrderPlaced(orderId), outcome => {
    if (!outcome.IsSuccess)
        logger.LogError(outcome.Error, "Notification failed");
    return ValueTask.CompletedTask;
});
```

### Broadcast to Multiple Receivers

Get attributed responses from all registered receivers:

```csharp
public record ValidateOrder(Order Order);
public record ValidationResult(bool IsValid, string? Message);

// Multiple validators registered
public class InventoryValidator : IReceiver<ValidateOrder, ValidationResult> { ... }
public class PaymentValidator : IReceiver<ValidateOrder, ValidationResult> { ... }
public class FraudDetector : IReceiver<ValidateOrder, ValidationResult> { ... }

// Broadcast and get all responses with metadata
var replies = await bus.BroadcastAsync<ValidateOrder, ValidationResult>(
    new ValidateOrder(order));

foreach (var reply in replies) {
    var result = reply.Result.IsSuccess ? reply.Result.Response : null;
    Console.WriteLine($"{reply.ReceiverType.Name}: {result?.IsValid} ({reply.Duration.TotalMilliseconds}ms)");
}
// Output:
// InventoryValidator: True (12ms)
// PaymentValidator: False (45ms)
// FraudDetector: True (120ms)
```

### Dynamic Receiver Management

Connect and disconnect receivers at runtime:

```csharp
var registry = serviceProvider.GetRequiredService<ITalynRegistry>();

// Connect a new receiver dynamically
var promotionHandler = new HolidayPromotionHandler();
registry.Connect<OrderPlaced>(promotionHandler);

// Messages now flow to this handler too
await bus.TransmitAsync(new OrderPlaced(orderId));

// Disconnect when the promotion ends
registry.Disconnect<OrderPlaced>(promotionHandler);
```

### Execution Modes for Multiple Receivers

Control how receivers execute when broadcasting:

```csharp
// Sequential (default) — ordered, one at a time
var replies = await bus.BroadcastAsync<ValidateOrder, ValidationResult>(
    request, ExecutionMode.Sequential);

// Concurrent — start all async operations, await together
var replies = await bus.BroadcastAsync<ValidateOrder, ValidationResult>(
    request, ExecutionMode.Concurrent);

// Parallel — true thread parallelism via Parallel.ForEachAsync
var replies = await bus.BroadcastAsync<ValidateOrder, ValidationResult>(
    request, ExecutionMode.Parallel);
```

| Mode | Behavior | Use When |
|------|----------|----------|
| `Sequential` | One-by-one with `foreach + await` | Order matters, debugging |
| `Concurrent` | Start all, await with `WhenAll` pattern | I/O-bound receivers |
| `Parallel` | Thread parallelism via `Parallel.ForEachAsync` | CPU-bound receivers |

### Direct Execution for Testing

Bypass the channel for unit tests or low-latency scenarios:

```csharp
public class OrderServiceTests(ITalynMediator mediator) {
    public async Task<Order> GetOrderDirect(Guid id) {
        // Bypasses channel, executes immediately
        return await mediator.InvokeAsync<GetOrder, Order>(new GetOrder(id));
    }
}
```

## Behaviors (Type-Safe Middleware)

Behaviors provide MediatR-style pipeline middleware with full type safety and response access. Unlike global middleware that operates on untyped context, behaviors give you compile-time type checking and the ability to intercept and transform responses.

### Middleware vs Behaviors

| Aspect | `ITalynMiddleware` | `ITalynBehavior<T>` |
|--------|-------------------|---------------------|
| Type safety | Untyped `TalynMessageContext` | Strongly-typed `TMessage` |
| Response access | None (void) | Full response transformation |
| Scope | Global (all messages) | Per-message-type |
| Constraints | None | Generic constraints (e.g., `where T : IValidatable`) |
| Use case | Logging, tracing, metrics | Validation, caching, response enrichment |

### Execution Order

```
┌─────────────────────────────────────────────────────────────┐
│                    ITalynMiddleware[]                       │
│  (Global, non-generic, wraps everything)                    │
│                                                             │
│   ┌─────────────────────────────────────────────────────┐   │
│   │           ITalynBehavior<T, TResponse>[]            │   │
│   │  (Per-message-type, generic, response access)       │   │
│   │                                                     │   │
│   │   ┌─────────────────────────────────────────────┐   │   │
│   │   │              Receivers                      │   │   │
│   │   │  IReceiver<T> or IReceiver<T, TResponse>    │   │   │
│   │   └─────────────────────────────────────────────┘   │   │
│   │                                                     │   │
│   └─────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

1. Global middleware (ITalynMiddleware) - Before
2. Type-specific behaviors (ITalynBehavior<T,R>) - Before
3. Receivers execute
4. Type-specific behaviors - After (with response)
5. Global middleware - After

### Behavior Interfaces

```csharp
// Delegates used by behaviors
public delegate ValueTask BehaviorDelegate(CancellationToken ct);
public delegate ValueTask<TResponse> BehaviorDelegate<TResponse>(CancellationToken ct);

// Fire-and-forget behavior (for TransmitAsync, InvokeAsync)
public interface ITalynBehavior<in TMessage> {
    ValueTask HandleAsync(TMessage message, BehaviorDelegate next, CancellationToken ct);
}

// Request/response behavior (works with first successful response)
public interface ITalynBehavior<in TMessage, TResponse> {
    ValueTask<TResponse> HandleAsync(TMessage message, BehaviorDelegate<TResponse> next, CancellationToken ct);
}

// Broadcast behavior (full access to all receiver responses with attribution)
public interface ITalynBroadcastBehavior<in TMessage, TResponse> {
    ValueTask<ReceiverReply<TResponse>[]> HandleAsync(
        TMessage message,
        BehaviorDelegate<ReceiverReply<TResponse>[]> next,
        CancellationToken ct
    );
}
```

### When to Use Each Behavior Type

| Scenario | Behavior Type | Why |
|----------|---------------|-----|
| Logging, tracing, metrics | `ITalynBehavior<T>` | Fire-and-forget, no response needed |
| Validation, authorization | `ITalynBehavior<T, R>` | Need to short-circuit or transform response |
| Caching, response enrichment | `ITalynBehavior<T, R>` | Need to modify response before returning |
| Per-receiver timing, error aggregation | `ITalynBroadcastBehavior<T, R>` | Need full attribution for each receiver |
| Health checks across validators | `ITalynBroadcastBehavior<T, R>` | Need to know which receiver failed |

### Behavior Examples

#### Fire-and-Forget Logging Behavior

```csharp
public class LoggingBehavior<TMessage> : ITalynBehavior<TMessage> {
    readonly ILogger _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TMessage>> logger) => _logger = logger;

    public async ValueTask HandleAsync(TMessage message, BehaviorDelegate next, CancellationToken ct) {
        _logger.LogInformation("Handling {MessageType}", typeof(TMessage).Name);
        var sw = Stopwatch.StartNew();

        try {
            await next(ct);
            _logger.LogInformation("Handled {MessageType} in {Elapsed}ms", typeof(TMessage).Name, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed {MessageType} after {Elapsed}ms", typeof(TMessage).Name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

#### Request/Response Validation Behavior

```csharp
public interface IValidatable {
    void Validate();
}

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
```

#### Response Transformation Behavior

```csharp
public class EnrichmentBehavior<TMessage, TResponse> : ITalynBehavior<TMessage, TResponse>
    where TResponse : IEnrichable
{
    public async ValueTask<TResponse> HandleAsync(
        TMessage message,
        BehaviorDelegate<TResponse> next,
        CancellationToken ct
    ) {
        var response = await next(ct);
        response.EnrichedAt = DateTime.UtcNow;
        response.RequestId = Activity.Current?.TraceId.ToString();
        return response;
    }
}
```

#### Caching Behavior

```csharp
public class CachingBehavior<TMessage, TResponse> : ITalynBehavior<TMessage, TResponse>
    where TMessage : ICacheable
{
    readonly IMemoryCache _cache;

    public CachingBehavior(IMemoryCache cache) => _cache = cache;

    public async ValueTask<TResponse> HandleAsync(
        TMessage message,
        BehaviorDelegate<TResponse> next,
        CancellationToken ct
    ) {
        var cacheKey = message.CacheKey;

        if (_cache.TryGetValue<TResponse>(cacheKey, out var cached))
            return cached!;

        var response = await next(ct);
        _cache.Set(cacheKey, response, message.CacheDuration);
        return response;
    }
}
```

#### Broadcast Metrics Behavior

```csharp
public class MetricsBehavior<TMessage, TResponse> : ITalynBroadcastBehavior<TMessage, TResponse> {
    readonly IMetrics _metrics;

    public MetricsBehavior(IMetrics metrics) => _metrics = metrics;

    public async ValueTask<ReceiverReply<TResponse>[]> HandleAsync(
        TMessage message,
        BehaviorDelegate<ReceiverReply<TResponse>[]> next,
        CancellationToken ct
    ) {
        var results = await next(ct);

        foreach (var reply in results) {
            _metrics.RecordLatency(reply.ReceiverType.Name, reply.Duration);

            if (!reply.Result.IsSuccess)
                _metrics.RecordFailure(reply.ReceiverType.Name);
        }

        return results;
    }
}
```

### Registering Behaviors

#### With Dependency Injection

```csharp
services.AddTalyn(builder => builder
    // Global middleware (non-generic)
    .UseMiddleware<TracingMiddleware>()

    // Fire-and-forget behaviors
    .UseBehavior<OrderPlaced, AuditBehavior<OrderPlaced>>()

    // Request/response behaviors
    .UseBehavior<GetOrder, Order, CachingBehavior<GetOrder, Order>>()
    .UseBehavior<CreateOrder, OrderResult, ValidationBehavior<CreateOrder, OrderResult>>()

    // Broadcast behaviors (with full attribution access)
    .UseBroadcastBehavior<ValidateOrder, ValidationResult, MetricsBehavior<ValidateOrder, ValidationResult>>()

    // Receivers
    .WithReceiver<OrderReceiver>()
);
```

#### Standalone (Without DI)

```csharp
var bus = TalynBus.Create()
    // Instance-based registration
    .UseBehavior(new LoggingBehavior<OrderPlaced>(logger))
    .UseBehavior(new CachingBehavior<GetOrder, Order>(cache))
    .UseBroadcastBehavior(new MetricsBehavior<ValidateOrder, ValidationResult>(metrics))

    // Lambda behaviors for quick inline behaviors
    .UseBehavior<OrderPlaced>(async (msg, next, ct) => {
        Console.WriteLine($"Before: {msg.OrderId}");
        await next(ct);
        Console.WriteLine($"After: {msg.OrderId}");
    })

    .UseBehavior<GetOrder, Order>(async (msg, next, ct) => {
        var response = await next(ct);
        return response with { CachedAt = DateTime.UtcNow };
    })

    .WithReceiver<OrderPlaced>(msg => Console.WriteLine($"Received: {msg.OrderId}"))
    .Build();
```

### Runtime Behavior Management

Like receivers, behaviors can be connected and disconnected at runtime:

```csharp
var registry = services.GetRequiredService<ITalynRegistry>();

// Connect a behavior dynamically
var cachingBehavior = new CachingBehavior<GetOrder, Order>(cache);
registry.Connect(cachingBehavior);

// Disconnect when no longer needed
registry.Disconnect(cachingBehavior);
```

### Short-Circuiting

Behaviors can skip downstream processing by not calling `next()`:

```csharp
public class AuthorizationBehavior<TMessage, TResponse> : ITalynBehavior<TMessage, TResponse>
    where TMessage : IAuthorizedRequest
{
    public async ValueTask<TResponse> HandleAsync(
        TMessage message,
        BehaviorDelegate<TResponse> next,
        CancellationToken ct
    ) {
        if (!message.User.HasPermission(message.RequiredPermission))
            throw new UnauthorizedException();  // Short-circuit — next() never called

        return await next(ct);
    }
}
```

## Receiver Interfaces

```csharp
// Message receiver (fire-and-forget)
public interface IReceiver<in T> {
    ValueTask HandleAsync(T message, CancellationToken ct);
}

// Request/Response receiver
public interface IReceiver<in T, TResponse> {
    ValueTask<TResponse> HandleAsync(T message, CancellationToken ct);
}
```

## Configuration

```csharp
services.AddTalyn(builder => {
    builder.Options = new TalynOptions {
        ChannelCapacity = Environment.ProcessorCount * 2,
        FailFast = true,
        DrainOnDispose = true,
        DefaultExecutionMode = ExecutionMode.Sequential
    };

    builder.WithReceiver<OrderReceiver>();
    builder.WithReceiver<InventoryValidator>();
});
```

| Option | Default | Description |
|--------|---------|-------------|
| `ChannelCapacity` | `ProcessorCount * 2` | Max queued messages before backpressure kicks in |
| `FailFast` | `true` | Crash on errors vs log and continue |
| `DrainOnDispose` | `true` | Complete queued work vs cancel on shutdown |
| `DefaultExecutionMode` | `Sequential` | Default mode for multi-receiver operations |

## API Reference

### ITalynBus (Queued Operations)

Production interface with backpressure and ordered processing.

| Method | Description | Returns |
|--------|-------------|---------|
| `TransmitAsync<T>` | Fire-and-forget to all `IReceiver<T>` | `ValueTask` |
| `TransmitAsync<T>` + callback | Fire-and-forget with completion notification | `ValueTask` |
| `TransmitAsync<T, TResponse>` | Single receiver request/response | `TResponse` |
| `BroadcastAsync<T, TResponse>` | All receivers with attribution | `ReceiverReply<TResponse>[]` |
| `BroadcastAsync<T, TResponse>` + callback | Fire-and-forget broadcast | `ValueTask` |

### ITalynMediator (Direct Operations)

Low-latency interface that bypasses the channel.

| Method | Description | Returns |
|--------|-------------|---------|
| `InvokeAsync<T>` | Fire-and-forget to all `IReceiver<T>` | `ValueTask` |
| `InvokeAsync<T, TResponse>` | Single receiver request/response | `TResponse` |
| `ScatterAsync<T, TResponse>` | All receivers with attribution | `ReceiverReply<TResponse>[]` |

### ITalynRegistry (Receiver & Behavior Management)

Runtime receiver and behavior connection and disconnection.

**Receiver Methods**

| Method | Description |
|--------|-------------|
| `Connect<T>(IReceiver<T>)` | Add a message receiver at runtime |
| `Connect<T, TResponse>(IReceiver<T, TResponse>)` | Add a request/response receiver at runtime |
| `Disconnect<T>(IReceiver<T>)` | Remove a message receiver |
| `Disconnect<T, TResponse>(IReceiver<T, TResponse>)` | Remove a request/response receiver |

**Behavior Methods**

| Method | Description |
|--------|-------------|
| `Connect<T>(ITalynBehavior<T>)` | Add a fire-and-forget behavior at runtime |
| `Connect<T, TResponse>(ITalynBehavior<T, TResponse>)` | Add a request/response behavior at runtime |
| `ConnectBroadcast<T, TResponse>(ITalynBroadcastBehavior<T, TResponse>)` | Add a broadcast behavior at runtime |
| `Disconnect<T>(ITalynBehavior<T>)` | Remove a fire-and-forget behavior |
| `Disconnect<T, TResponse>(ITalynBehavior<T, TResponse>)` | Remove a request/response behavior |
| `DisconnectBroadcast<T, TResponse>(ITalynBroadcastBehavior<T, TResponse>)` | Remove a broadcast behavior |

### ReceiverReply&lt;TResponse&gt;

Attributed response with metadata:

```csharp
public readonly record struct ReceiverReply<TResponse>(
    Type ReceiverType,         // Which receiver responded
    Outcome<TResponse> Result, // Success/Failure with value or exception
    TimeSpan Duration          // How long it took
);
```

## Performance

### Zero Allocation Pattern

Talyn uses the TState pattern to avoid closure allocations:

```csharp
// Zero allocation — static lambda + state tuple
var item = Directive.Create(
    (message, receiver),
    static (state, ct) => state.receiver.HandleAsync(state.message, ct));
```

### Pooled Directives

Work items are pooled using `Microsoft.Extensions.ObjectPool`:

- Rented from pool on publish
- Returned automatically when result is consumed
- Finalizer catches leaks (with DEBUG diagnostics)

### ValueTask Optimization

All operations use `ValueTask` for synchronous completion without allocation:

```csharp
var writeTask = _channel.Writer.WriteAsync(item, ct);

if (writeTask.IsCompletedSuccessfully)
    return item.Task;  // no await, no state machine

return AwaitWriteAndReturn(writeTask, item);
```

## Thread Safety

| Component | Safety |
|-----------|--------|
| `TalynBus` | Thread-safe, multiple producers supported |
| `TalynRegistry` | Thread-safe for Connect/Disconnect |
| Consumer | Single-threaded, FIFO order guaranteed |

## License

MIT

---

*"I am not a warship. I am Talyn."*
