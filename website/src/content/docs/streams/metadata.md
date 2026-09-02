---
title: Record metadata
description: Attach your own keys to a record, and read them back with the type you wrote.
---

Every record carries a metadata bag alongside its payload. It travels with the record, survives a
round trip, and is addressable from a [filter](/kurrent-surge/streams/filters/).

Use it for context about the record rather than the fact itself: a correlation id, the user who
caused it, a tenant, a trace id. The payload is what happened; the metadata is what surrounded it.

```csharp
var record = new AppendRecord {
    Stream   = "order-123",
    Value    = new OrderPlaced("order-123"),
    Metadata = Metadata.New
        .With("correlation-id", correlationId)
        .With("tenant", "acme")
        .With("caused-by", userId)
};
```

The bag always exists. A record you never gave metadata to has an empty one, not a null one.

## Writing

`With` returns the metadata so calls chain. `WithMany` takes a sequence.

```csharp
Metadata.New
    .With("tenant", "acme")
    .With("retry-count", 3)
    .WithMany(headersFromUpstream);
```

`WithIf` sets a key only when a condition holds, which keeps a conditional out of the chain.

```csharp
Metadata.New
    .With("tenant", tenant)
    .WithIf("replayed-from", sourcePosition, isReplay);
```

## Reading

Read with the type you wrote, not as strings.

| Method | Behaviour |
|---|---|
| `TryGetValue<T>(key, out value)` | `false` when the key is absent |
| `GetValueOrDefault<T>(key, default)` | The default when the key is absent |
| `GetRequiredValue<T>(key)` | Throws when the key is absent |
| `TryGetParsable<T>(key, out value)` | For a `T` implementing `IParsable<T>`, parsed from its string form |
| `ContainsKey(key)` | Presence only |

```csharp
if (record.Metadata.TryGetValue<string>("tenant", out var tenant))
    _log.LogInformation("Record for tenant {Tenant}", tenant);

var retries = record.Metadata.GetValueOrDefault("retry-count", 0);
var trace   = record.Metadata.GetRequiredValue<string>("trace-id");
```

`GetRequiredValue` is the one to reach for when absence is a bug rather than a case to handle. It
fails at the record that is wrong, instead of substituting a default that travels somewhere else
before causing a problem.

## Locking

`Lock()` makes a bag immutable, and `CreateUnlockedCopy()` gives a mutable one back. A locked bag
shared across records cannot be modified by whoever receives it.

```csharp
static readonly Metadata Common = Metadata.New
    .With("service", "orders-api")
    .Lock();

var metadata = Common.CreateUnlockedCopy().With("correlation-id", correlationId);
```

Locking a template is what makes sharing one safe. Without it, a bag reused across appends
accumulates whatever each caller added.

## Evolving a value

`EvolveValue` reads a key, transforms it, and writes the result back in one step.

```csharp
metadata.EvolveValue<int, int>("retry-count", current => current + 1);
```

## Filtering on metadata

Metadata is addressable in an expression filter under `metadata`, `properties` or `props`.

```csharp
RecordFilter.ByExpression("props.tenant = 'acme'")
```

A bare `metadata` reference is never SQL `NULL`, because the bag always exists. It corresponds to an
empty object rather than a missing column.

## Stream metadata is a different thing

This page is about metadata on a record. Metadata on a *stream* covers retention, access control, and
your own key-value data attached to the stream itself. That lives on
[Manage](/kurrent-surge/streams/manage/).
