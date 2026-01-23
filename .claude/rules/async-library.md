---
paths:
  - "src/**/*.cs"
---

# Async Patterns for Library Code

## ConfigureAwait(false) - REQUIRED ON ALL AWAITS

```csharp
// CORRECT
var result = await _service.HandleAsync(order, ct).ConfigureAwait(false);

// WRONG - missing ConfigureAwait
var result = await _service.HandleAsync(order, ct);
```

## CancellationToken

- Propagate through ALL async chains
- Always the last parameter

## ValueTask

- Use `ValueTask<T>` for frequently synchronous completion paths
