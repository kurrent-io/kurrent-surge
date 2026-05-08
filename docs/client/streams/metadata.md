---
order: 5
---

# Metadata

<CopyMarkdownButton />

Every record carries a `Metadata` instance. The SDK populates a small set of system keys (schema name, schema id, format), and you can attach your own keys when appending. This page covers reading, writing, and converting metadata.

```csharp
var record = await client.Streams.ReadFirstStreamRecord("order-123");

if (record.Metadata.TryGetValue<string>("tenant", out var tenant))
    Console.WriteLine("Tenant: {0}", tenant);
```

## Building metadata

`Metadata` is a case-insensitive dictionary of `string` to `object?`. Build instances fluently:

```csharp
var metadata = Metadata.New
    .With("tenant", "acme")
    .With("requestId", Guid.NewGuid())
    .With("source", "web");
```

| Method | Description |
|---|---|
| `Metadata.New` | A fresh, empty, mutable instance. |
| `Metadata.Create(dictionary)` | Wraps an existing dictionary. |
| `With(key, value)` | Adds or replaces a key. Returns `this` for chaining. |
| `WithMany(items)` | Adds or replaces a batch of pairs. |
| `Reset(items)` | Replaces all current entries with the given pairs. |
| `Remove(key)` | Removes a key. Returns `true` if the key existed. |

Direct indexer access also works: `metadata["tenant"] = "acme"`.

## Reading values

`TryGetValue<T>` is the safest accessor. It returns the value as `T`, performing string-to-target conversion through `TypeDescriptor` when needed.

```csharp
metadata.TryGetValue<string>("tenant", out var tenant);
metadata.TryGetValue<int>("attempts", out var attempts);
metadata.TryGetValue<Guid>("requestId", out var requestId);
metadata.TryGetValue<DateTimeOffset>("createdAt", out var createdAt);
```

Three accessors with different contracts:

| Method | Behavior on missing key |
|---|---|
| `TryGetValue<T>(key, out value)` | Returns `false`. |
| `GetValueOrDefault<T>(key, fallback?)` | Returns the supplied default (or `default(T)`). |
| `GetRequiredValue<T>(key)` | Throws `KeyNotFoundException`. |

Conversion handles strings into primitives, enums, `Guid`, `DateTime`, `DateTimeOffset`, and any type with a `TypeConverter`. Conversion failures return `false` from `TryGetValue` rather than throwing.

## Locking

Server-returned metadata is locked to prevent accidental mutation of read records. Direct writes throw `InvalidOperationException`.

```csharp
record.Metadata["tenant"] = "other";  // throws — record metadata is locked
```

To modify, copy first:

```csharp
var modified = record.Metadata
    .CreateUnlockedCopy()
    .With("tenant", "other")
    .Lock();
```

`Lock()` returns the same instance for chaining. `CreateUnlockedCopy()` always returns a new mutable instance regardless of the source's lock state.

## Conditional writes

`WithIf` writes only when a condition holds. Three overloads cover common patterns:

```csharp
metadata.WithIf("retry", retryCount, retryCount > 0);

metadata.WithIf("traceId", () => Activity.Current?.Id, condition: tracingEnabled);

metadata.WithIf(
    "fallbackTenant",
    () => "default",
    predicate: m => !m.ContainsKey("tenant")
);
```

The factory overloads only invoke the value factory when the condition is satisfied, useful when computing the value is non-trivial.

## Schema metadata

Three system keys describe the record's schema. Convenience extensions read them as strongly-typed values:

| Method | Returns |
|---|---|
| `metadata.GetSchemaName()` | `SchemaName` (or `SchemaName.Unset`). |
| `metadata.GetSchemaFormat()` | `SchemaDataFormat` (or `Unspecified`). |
| `metadata.GetSchemaId()` | `SchemaVersionId` (or `Unset`). |
| `metadata.ExtractSchemaInfo()` | Returns a `RecordSchemaInfo` and removes the three keys from the metadata in place. |

`AddSchemaNameIfMissing` and `AddSchemaFormatIfMissing` set the value only if no schema info is already present. This is useful when forwarding records between contexts where the inner code may already have stamped a schema.

```csharp
metadata.AddSchemaNameIfMissing("OrderPlaced");
metadata.AddSchemaFormatIfMissing(SchemaDataFormat.Json);
```

## Stringify

`Stringify()` converts every value to its string representation using invariant-culture formatting and ISO-8601 dates. Useful when writing metadata into systems that only accept strings (e.g. tracing headers, downstream queues).

```csharp
var stringified = metadata.Stringify();

foreach (var (key, value) in stringified)
    Console.WriteLine("{0} = {1}", key, value);
```

| Type | Format |
|---|---|
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` | ISO-8601 round-trip (`O`). |
| `TimeSpan` | constant (`c`). |
| `Guid` | dashed (`D`). |
| numeric types | invariant culture. |
| `byte[]`, `Memory<byte>`, `ReadOnlyMemory<byte>` | base64. |
| `IEnumerable` | comma-separated, recursively stringified. |
| anything else | `value.ToString()`. |

## Iterating metadata

`Metadata` implements `IEnumerable<KeyValuePair<string, object?>>`:

```csharp
foreach (var (key, value) in record.Metadata)
    Console.WriteLine("{0} -> {1}", key, value ?? "<null>");
```

Use `Keys`, `Values`, and `Count` for direct collection access. Order is insertion order (backed by a `Dictionary` with `OrdinalIgnoreCase` comparison).

## Equality

Two `Metadata` instances compare equal when they contain the same keys (case-insensitively) and equal values. The hash is stable across instances with the same content. Locked state is not part of the comparison.

```csharp
var a = Metadata.New.With("tenant", "acme");
var b = Metadata.New.With("Tenant", "acme");

Console.WriteLine(a == b);  // True
```
