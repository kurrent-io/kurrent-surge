---
order: 5
---

# Metadata

<CopyMarkdownButton />

Every record carries a `Metadata` instance: a case-insensitive dictionary of `string` to `object?`. The SDK populates a small set of system keys (schema name, schema id, format), and you can attach your own keys when appending.

```csharp
var metadata = Metadata.New
    .With("tenant", "acme")
    .With("requestId", Guid.NewGuid());

metadata.TryGetValue<string>("tenant", out var tenant);
```

## Create metadata

Build instances fluently:

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
| `metadata[key] = value` | Direct indexer access. |

## Read values

Three accessors with different contracts:

| Method | Behavior on missing key |
|---|---|
| `TryGetValue<T>(key, out value)` | Returns `false`. |
| `GetValueOrDefault<T>(key, fallback?)` | Returns the supplied default (or `default(T)`). |
| `GetRequiredValue<T>(key)` | Throws `KeyNotFoundException`. |

```csharp
metadata.TryGetValue<string>("tenant", out var tenant);
metadata.TryGetValue<int>("attempts", out var attempts);
metadata.TryGetValue<Guid>("requestId", out var requestId);
metadata.TryGetValue<DateTimeOffset>("createdAt", out var createdAt);
```

Conversion handles strings into primitives, enums, `Guid`, `DateTime`, `DateTimeOffset`, and any type with a `TypeConverter`. Conversion failures return `false` from `TryGetValue` rather than throwing.

## Modify server-returned metadata

Server-returned metadata is locked to prevent accidental mutation of read records. Direct writes throw `InvalidOperationException`.

```csharp
record.Metadata["tenant"] = "other";  // throws, record metadata is locked
```

To modify, copy first:

```csharp
var modified = record.Metadata
    .CreateUnlockedCopy()
    .With("tenant", "other")
    .Lock();
```

`CreateUnlockedCopy()` always returns a new mutable instance, even when the source is already unlocked.

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

## Schema keys

Three system keys describe the record's schema. Convenience extensions read them as strongly-typed values:

| Method | Returns |
|---|---|
| `metadata.GetSchemaName()` | `SchemaName` (or `SchemaName.Unset`). |
| `metadata.GetSchemaFormat()` | `SchemaDataFormat` (or `Unspecified`). |
| `metadata.GetSchemaId()` | `SchemaVersionId` (or `Unset`). |
| `metadata.ExtractSchemaInfo()` | Returns a `RecordSchemaInfo` and removes the three keys from the metadata in place. |

`AddSchemaNameIfMissing` and `AddSchemaFormatIfMissing` only set the value when no schema key is already present:

```csharp
metadata.AddSchemaNameIfMissing("OrderPlaced");
metadata.AddSchemaFormatIfMissing(SchemaDataFormat.Json);
```

## Render values as strings

`Stringify()` converts every value to its string representation using invariant-culture formatting and ISO-8601 dates. Useful when writing metadata into systems that only accept strings, for example tracing headers or downstream queues.

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

## Iterate and compare

`Metadata` implements `IEnumerable<KeyValuePair<string, object?>>`. Order is insertion order, backed by a dictionary with `OrdinalIgnoreCase` comparison. Use `Keys`, `Values`, and `Count` for direct collection access.

```csharp
foreach (var (key, value) in record.Metadata)
    Console.WriteLine("{0} -> {1}", key, value ?? "<null>");
```

Two instances compare equal when they contain the same keys (case-insensitively) and equal values. The hash is stable across instances with the same content. Locked state is not part of the comparison.

```csharp
var a = Metadata.New.With("tenant", "acme");
var b = Metadata.New.With("Tenant", "acme");

Console.WriteLine(a == b);  // True
```
