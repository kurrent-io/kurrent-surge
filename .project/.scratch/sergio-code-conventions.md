# Sergio's C# Code Conventions

Personal coding style. Documents patterns that deviate from or extend mainstream C# conventions. Read alongside
`.editorconfig` and [project-code-conventions.md](../project-code-conventions.md) for the full picture.

## Contents

- [Formatting](#formatting)
- [Type design](#type-design)
- [Immutability](#immutability)
- [Collections](#collections)
- [Null handling](#null-handling)
- [Async](#async)
- [Error handling](#error-handling)
- [Pattern matching](#pattern-matching)
- [Extension methods](#extension-methods)
- [Builders](#builders)
- [Testing](#testing)
- [What this style is not](#what-this-style-is-not)

## Formatting

### K&R braces

Opening brace on the same line. This is the single most visible divergence from mainstream C# (Allman style).

```csharp
if (position == LogPosition.Unset) {
    throw new ArgumentOutOfRangeException(nameof(position));
}

public class StreamsClient : ClientModuleBase {
    // ...
}
```

### `var` everywhere

`var` for all local declarations, including built-in types. No exceptions. Enforced at warning level.

```csharp
var count    = 10;           // not: int count = 10;
var name     = "stream-1";   // not: string name = "stream-1";
var position = LogPosition.Earliest;
```

### Column-aligned declarations

Related declarations are column-aligned for scanability. Applies to fields, local variables, and multi-property
initializers.

```csharp
var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
var stoppingToken = cancellator.Token;

public required ImmutableArray<AppendRecord>     Records { get; init; }
public required ImmutableArray<ConsistencyCheck> Checks  { get; init; }
```

### Parameters on separate lines

Multi-parameter methods place each parameter on its own line. Types are column-aligned when practical.

```csharp
public async ValueTask<AppendRecordsSuccess> AppendRecords(
    StreamName                stream,
    ExpectedStreamState       expectedState,
    IEnumerable<AppendRecord> records,
    CancellationToken         cancellationToken = default
) {
    // ...
}
```

### Omit default access modifiers

If the member's visibility matches the language default, the modifier is omitted.

```csharp
class InternalHelper { }                          // not: internal class InternalHelper
readonly ConcurrentDictionary<TKey, TValue> _fwd; // not: private readonly ...
```

### Expression-bodied members for simple operations

Single-expression members use `=>`. Multi-statement logic uses block bodies. No in-between.

```csharp
public int Count => Results.Count;

internal RegistryClient(KurrentClient client) : base(client) =>
    ServiceClient = new(client.LegacyCallInvoker);
```

## Type design

### Records as the default for data

Records are the default choice for data types. Classes are reserved for stateful services and base types.

```csharp
public record AppendRecordsRequest() { ... }
public record AppendRecordsSuccess(StreamRevision[] Revisions, LogPosition Position) { ... }
public record KurrentConnectionString { ... }
```

### `readonly record struct` for value types

Lightweight immutable value types use `readonly record struct`, not `class` or plain `struct`.

```csharp
public readonly record struct StreamStateCheck(StreamName Stream, ExpectedStreamState ExpectedState);
public readonly record struct RetryInfo { ... }
public readonly record struct QueryPredicateCheck(string Expression) { ... }
```

### `IVariant<T1, T2>` discriminated unions

Type-safe variant types replace class hierarchies or enums for sum types.

```csharp
public readonly partial record struct ConsistencyCheck : IVariant<StreamStateCheck, QueryPredicateCheck> {
    public static readonly ConsistencyCheck None = new();
}

public readonly partial record struct StartPosition : IVariant<LogPosition, Revision> {
    public static readonly StartPosition Unset = new(LogPosition.Unset);
}
```

### Static sentinels over null

Domain types expose static readonly sentinels (`None`, `Empty`, `Unset`) instead of relying on null.

```csharp
public static readonly Revision    Unset    = new((long)-1000);
public static readonly LogPosition Earliest = new(0);
public static readonly LogPosition Latest   = new(long.MaxValue);
public static readonly AppendRecord None    = new() { /* defaults */ };
```

### Private constructors + `From()` factory

Value types with validation use private constructors and static factory methods with switch-expression validation.

```csharp
public record Revision : IComparable<Revision> {
    Revision(long value) => Value = value;
    public long Value { get; private init; }

    public static Revision From(long value) =>
        value switch {
            -1000 => Unset,
            >= 0  => new Revision(value),
            _     => throw new InvalidRevision(value)
        };
}
```

### Implicit conversions on domain value types

Domain primitives define implicit operators for ergonomic use at API boundaries.

```csharp
public static implicit operator long(Revision revision) => revision.Value;
public static implicit operator Revision(long revision) => From(revision);
```

### `[DebuggerDisplay]` on domain types

Value types and frequently inspected types carry `[DebuggerDisplay]` for IDE tooling.

```csharp
[DebuggerDisplay("{ToDebugString()}")]
public record Revision : IComparable<Revision>, IComparable { ... }
```

## Immutability

### `required` + `init` as the standard property pattern

Public data properties use `required` for mandatory initialization and `init` for immutability.

```csharp
public required ImmutableArray<AppendRecord> Records { get; init; }
public required LogPosition                  Position { get; init; }
```

### `[method: SetsRequiredMembers]` on primary constructors

When a record has both a primary constructor and `required` properties, the constructor is annotated to satisfy the
compiler.

```csharp
[method: SetsRequiredMembers]
public record AppendRecordsSuccess(StreamRevision[] Revisions, LogPosition Position) {
    public required StreamRevision[] Revisions { get; init; } = Revisions;
    public required LogPosition      Position  { get; init; } = Position;
}
```

### `with` expressions for immutable updates

Record mutation uses `with` expressions; never mutate in place.

```csharp
position = defaultPos with { Partition = partition };

message = record with {
    Position = record.Position with {
        Partition = Partition.From(partition)
    }
};
```

## Collections

### `ImmutableArray<T>` for public API collections

Public-facing collection properties use `ImmutableArray<T>`, not `List<T>` or `IReadOnlyList<T>`.

```csharp
public required ImmutableArray<AppendRecord>     Records { get; init; }
public required ImmutableArray<ConsistencyCheck> Checks  { get; init; }
```

### Arrays for simple return types

When immutability guarantees are unnecessary (e.g., freshly allocated results), plain arrays are acceptable.

```csharp
public required StreamRevision[] Revisions { get; init; }
```

### Collection expressions for initialization

```csharp
public DnsEndPoint[] Endpoints { get; init; } = [new DnsEndPoint("localhost", 2113)];
ImmutableArray<ConsistencyCheck> checks = _expectedState.HasValue
    ? [new StreamStateCheck(_stream, _expectedState.Value)]
    : [];
```

## Null handling

### Pattern matching over null checks

`is not null`, `is { }`, and `is Type name` replace `!= null`, `as`, and explicit casts.

```csharp
if (status.GetDetail<ResourceInfo>() is { } resourceInfo)
    details.ResourceInfo = ResourceInfo.From(resourceInfo.ResourceType, resourceInfo.ResourceName);

if (record.Value is not Record rec)
    throw new LogPositionNotFoundException(position);
```

### Null-coalescing assignment

```csharp
status ??= new Status {
    Code    = (int)rex.StatusCode,
    Message = rex.Message
};
```

### Guard clauses with early return/throw

Validation at method entry, not nested conditionals.

```csharp
if (position == LogPosition.Unset)
    throw new ArgumentOutOfRangeException(nameof(position));

if (stream == StreamName.None || stream == StreamName.All)
    throw new ArgumentException("...", nameof(stream));
```

## Async

### `ValueTask<T>` over `Task<T>`

All public async methods return `ValueTask` or `ValueTask<T>`. No `Task<T>` in library code.

### `ConfigureAwait(false)` on every await

No exceptions in library code (`src/`). Includes `await foreach`.

```csharp
await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false)) {
    // ...
}

var message = await messages
    .FirstOrDefaultAsync(cancellationToken)
    .ConfigureAwait(false);
```

### `CancellationToken` always last, always defaulted

```csharp
public async ValueTask<RecordStream> ReadRecords(
    ReadOptions       options,
    CancellationToken cancellationToken = default
) { ... }
```

Internally abbreviated to `ct` in implementation code. Externally always `cancellationToken`.

### Linked cancellation pattern

```csharp
var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
var stoppingToken = cancellator.Token;
```

### `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]`

Hot-path local async functions use the pooling builder to reduce allocations.

```csharp
[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
async ValueTask PumpMessages() { ... }
```

## Error handling

### Typed exception hierarchy

A single base exception (`KurrentException`) with specific subtypes. No `catch (Exception)` in library code.

### Exception mapping via extension parsers

RPC exceptions are translated to domain exceptions through parser chains.

```csharp
return parsers
    .Concat(ServerParsers)
    .Concat(GoogleParsers)
    .Select(parse => parse(status, errorInfo))
    .SkipWhile(result => result is null)
    .DefaultIfEmpty(ex)
    .Select(ex => (Exception)ex!)
    .First();
```

### `when` guards on catch clauses

Catch blocks use `when` for precise exception filtering instead of re-throwing.

```csharp
catch (RpcException rex) when (rex.StatusCode == StatusCode.FailedPrecondition &&
                               rex.Trailers.GetValue("exception") == "stream-deleted") {
    throw new StreamTombstonedException(stream);
}
```

## Pattern matching

### Switch expressions as default

Switch expressions replace if/else chains and switch statements for mapping operations.

```csharp
throw info.State switch {
    StreamState.Active     => new StreamRevisionConflictException(stream, revision, info.LastRevision),
    StreamState.Deleted    => new StreamDeletedException(stream),
    StreamState.Tombstoned => new StreamTombstonedException(stream),
    StreamState.Missing    => new StreamNotFoundException(stream),
    _                      => rex
};
```

### `is not` over negated conditions

```csharp
if (ex is not RpcException rex)
    return ex;

if (record.Value is not ProcessorCheckpointState state || state.Partitions.IsEmpty) {
    return ProcessorCheckpoint.Empty;
}
```

## Extension methods

### C# 14 `extension` keyword

Extension methods use the new `extension()` syntax, not `static class` with `this` parameter.

```csharp
extension(StreamsClient client) {
    public ValueTask<AppendRecordsSuccess> AppendRecords(
        ImmutableArray<AppendRecord>     records,
        ImmutableArray<ConsistencyCheck> checks,
        CancellationToken                cancellationToken = default
    ) => client.AppendRecords(new AppendRecordsRequest(records, checks), cancellationToken);
}
```

### Organized in `*.Extensions.cs` partial files

Extensions live in partial class files named `{Type}.Extensions.cs` alongside the type they extend.

### `using static` for enum-like constants

Selectively applied to reduce noise in pattern matching and mapping code.

```csharp
using static System.StringComparison;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;
```

## Builders

### Static factory entry points on the target type

Builder creation is accessed through static methods on the type being built.

```csharp
public record AppendRecordsRequest() {
    public static SingleStreamAppendBuilder ToStream(StreamName stream) => new(stream);
    public static MultiStreamAppendBuilder New() => new();
}
```

### Fluent chaining with `return this`

```csharp
var request = AppendRecordsRequest.ToStream(_stream)
    .ExpectingState(expectedRevision)
    .WithRecord(rec => rec
        .WithValue(merged)
        .WithMetadata("processor_id", ProcessorId))
    .Build();
```

### Struct builders for lightweight construction

Simple builders use `readonly struct` to avoid heap allocation.

```csharp
public readonly struct StreamStateCheckBuilder(StreamName stream) {
    public StreamStateCheck AtRevision(Revision revision) => new(stream, revision);
    public StreamStateCheck Exists() => new(stream, ExpectedStreamCondition.Exists);
    public StreamStateCheck DoesNotExist() => new(stream, ExpectedStreamCondition.NoStream);
}
```

## Testing

### `snake_case` test method names

```csharp
public async ValueTask appends_to_single_stream(CancellationToken ct) { }
public async ValueTask creates_stream(CancellationToken ct) { }
public async ValueTask append_to_stream_with_maxcount_retention(CancellationToken ct) { }
```

### TUnit with `Assert.That(...)` for new tests

Shouldly is legacy. New assertions use TUnit's built-in `Assert.That`.

```csharp
await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
await Assert.That(info.State).IsEqualTo(StreamState.Active);
```

### `ValueTask` test return types

All async test methods return `ValueTask`, not `Task`.

### Nested `TestCaseGenerator<T>` for parameterized tests

```csharp
public class AppendToStreamInAnyState : TestCaseGenerator<ExpectedStreamState> {
    protected override IEnumerable<ExpectedStreamState> Data() => [
        ExpectedStreamCondition.NoStream,
        ExpectedStreamCondition.Exists
    ];
}

[Test, AppendToStreamInAnyState]
public async ValueTask appends_to_stream_in_any_state(ExpectedStreamState expectedStreamState, CancellationToken ct) {
    // ...
}
```

### `#region` markers for grouping related tests

```csharp
#region . Retention Policies .

[Test]
public async ValueTask append_to_stream_with_maxcount_retention(CancellationToken ct) { ... }

#endregion
```

## What this style is not

- Not Allman braces. K&R only.
- Not `List<T>` in public APIs. `ImmutableArray<T>`.
- Not `Task<T>` in library code. `ValueTask<T>`.
- Not explicit types for locals. `var` everywhere.
- Not xUnit or NUnit. TUnit only.
- Not Shouldly for new tests. `Assert.That`.
- Not class hierarchies for sum types. `IVariant<T1, T2>`.
- Not null for "absence". Static sentinels.
- Not `static class` extension methods. C# 14 `extension()` keyword.
