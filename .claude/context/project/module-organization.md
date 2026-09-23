# Module Organization

> File layout, naming, and mapping conventions for client modules. One package, `Kurrent.Surge`, ships
> from two projects, and a module is split across **both projects**. Get that split right first.

## The split

Every module has a contract half in the `Kurrent.Surge.Core` project and an implementation half in the
`Kurrent.Surge` project. `Kurrent.Surge` bundles the Core DLL, so both halves ship in the one package.
The folder name is the same on both sides.

| Project | Path | Holds |
|---|---|---|
| `Kurrent.Surge.Core` | `Client/Modules/{Module}/` | The interface, the request, response, and options types, the shared domain types, most convenience extension members, and the exceptions |
| `Kurrent.Surge` | `Client/Modules/{Module}/` | The concrete client, its RPC glue, the mapping between wire and model, and more extension members, public and internal |

Both projects hold extension members. Core holds most public convenience overloads. The implementation
holds the mapping and exception-matcher extensions. It also declares a second public
`StreamsClientExtensions` class, with the `FoldAsync` members in `StreamsClient.Folding.cs` and read
overloads in `StreamsClient.Read.cs`.

Streams shows the weight. Core holds roughly twice as many of its files as the implementation does.

**A model type never goes in the implementation project.** If a consumer can name the type, it belongs
in Core.

## Core side: `Kurrent.Surge.Core/Client/Modules/{Module}/`

```text
I{Module}Client.cs                     the interface, which is the module's public contract
{Module}Client.{Op}.Models.cs          request, response, and options for one operation
{Module}Client.Extensions.cs           convenience overloads as C# 14 extension members
{Module}Client.{Area}.Extensions.cs    a second extension file when one area earns it
Exceptions.cs                          module-scoped exception types
Models/                                shared domain types, one per file
{Subsystem}/                           a self-contained subsystem, such as Metadata/ or Filters/
```

Streams, as the largest:

```text
Core/Client/Modules/Streams/
├── IStreamsClient.cs
├── Exceptions.cs
├── StreamsClient.Append.Models.cs
├── StreamsClient.Read.Models.cs
├── StreamsClient.Folding.Models.cs
├── StreamsClient.Extensions.cs
├── StreamsClient.Records.Extensions.cs
├── Models/          LogPosition, Record, RecordPosition, StreamName, Revision, StreamInfo,
│                    StreamState, Partition, RecordId, SequenceId, ReadDirection, LinkTo, …
├── Metadata/        Metadata, MetadataDecoder, MetadataJsonConverter, SystemMetadataKeys, …
└── Filters/         RecordFilter, ExpressionFilter, StreamFilter, IndexFilter, …
```

The `Models/`, `Metadata/`, and `Filters/` lists are partial.

## Implementation side: `Kurrent.Surge/Client/Modules/{Module}/`

```text
{Module}Client.cs                      the base partial, holding the constructor, service clients, fields
{Module}Client.{Op}.cs                 the operation methods
{Module}Client.{Op}.Mapping.cs         stateless mapping for that operation
{Module}ClientMapping.cs               shared stateless mapping, at the module root
{Subsystem}/                           subsystems that only the implementation needs
```

Streams:

```text
Kurrent.Surge/Client/Modules/Streams/
├── StreamsClient.cs
├── StreamsClient.Append.cs          with .Append.Mapping.cs
├── StreamsClient.Read.cs            with .Read.Mapping.cs
├── StreamsClient.Subscribe.cs       with .Subscribe.Mapping.cs
├── StreamsClient.Manage.cs          with .Manage.Mapping.cs
├── StreamsClient.Resolve.cs         start-position routing
├── StreamsClient.Folding.cs         StreamsClientExtensions members (FoldAsync), no operation methods
├── StreamsClientMapping.cs
├── ConsistencyViolation.Mapping.cs
├── ExceptionMatchers.cs
├── StreamIdentifier.cs
├── Models/     RedactionExtensions, RecordBatchStream (the file is all comments)
├── Metadata/   MetadataJsonExtensions
└── Pipeline/   ReadRecordPipeline, ReadRecordSteps, WriteRecordPipeline, WriteRecordSteps
```

Not every operation needs every slot. A simple operation is one `.cs` file. Add a `.Models.cs` or a
`.Mapping.cs` only when there is enough content to justify a file.

`{Op}` is the **verb**, not the wire operation. When one verb has scope variants, such as
`AppendRecords` and `AppendStream`, they share one file per slot. The single-stream variant is sugar
over the protocol-native core, so the split mirrors the domain rather than the wire shapes: one verb,
two scopes, one pipeline.

## Operation naming: the target-scope axis

A suffix encodes the **target scope**. It never encodes a resource split.

| Suffix | Target | Examples |
|---|---|---|
| `*Records` | the log, or several streams. This is the protocol-native form. | `ReadRecords`, `AppendRecords` |
| `*Stream` | a single stream. This is the convenience form. | `ReadStream`, `AppendStream` |

Edge queries such as `FirstRecord` and `LastRecord` take a `StreamName` target instead. The overloads
without a stream parameter, such as `FirstRecord(CancellationToken)`, target the log.

## Result-type suffixes

| Suffix | Meaning | Examples |
|---|---|---|
| `*Response` | one per call, for a unary outcome | `AppendRecordsResponse`, `FoldResponse<TState>` |
| `*Message` | one per stream element, for a server-streamed outcome | `ReadMessage`, `SubscribeMessage` |

Never introduce a `*Result` type. It is always one of the two above.

## Mapping compared with Mapper

| Suffix | What it is | State | Example |
|---|---|---|---|
| `*Mapping` | Stateless extension members that do pure conversions | none. It takes an explicit `MappingContext`. | `StreamsClient.Append.Mapping.cs` |
| `*Mapper` | A stateful class with injected dependencies | holds serializers, decoders, policies | last resort |

Default to `*Mapping`. Introduce a `*Mapper` class only when stateless extensions with a context
parameter genuinely do not work.

### The MappingContext pattern

A mapping that needs dependencies gets them, and the cancellation token, in one value.
`StreamsClientMapping.cs` declares the type, and `StreamsClient.CreateMappingContext(ct)` creates it:

```csharp
readonly record struct MappingContext(
    IMetadataDecoder MetadataDecoder,
    IRecordDecoder Decoder,
    CancellationToken CancellationToken
);

internal MappingContext CreateMappingContext(CancellationToken ct) =>
    new(MetadataDecoder, RecordDecoder, ct);
```

Mapping extensions take that context explicitly. The shared record mapping in `StreamsClientMapping.cs`
is the example:

```csharp
static partial class StreamsClientMapping {
    public static async ValueTask<Record> MapToRecordAsync(
        this Contracts.ReadResp.Types.ReadEvent readEvent,
        SequenceId sequenceId,
        MappingContext ctx,
        bool skipDecoding = false,
        bool throwOnUnresolved = false
    ) { … }
}
```

A pure conversion needs no context. Append builds the `Contracts.AppendRecordsRequest` inline in
`StreamsClient.Append.cs`. Its mapping file converts only the consistency checks and the response:

```csharp
static class AppendRecordsMapping {
    extension(ConsistencyCheck source) {
        public Contracts.ConsistencyCheck ToContract() => …
    }

    extension(Contracts.AppendRecordsResponse source) {
        public AppendRecordsResponse ToResponse() { … }
    }
}
```

Read, subscribe, and manage call `CreateMappingContext(ct)` where they map a record:

```csharp
var record = await rsp.Event
    .MapToRecordAsync(sequencer.FetchNext(), CreateMappingContext(ct), skipDecoding, throwOnUnresolved)
    .ConfigureAwait(false);
```

Every dependency is visible at the call site. A test can build a context from doubles and call the
extension directly. The `readonly record struct` does not allocate.

## Where does it go?

| What you have | Where it goes |
|---|---|
| The module interface | `Core/…/I{Module}Client.cs` |
| Request, response, or options for an operation | `Core/…/{Module}Client.{Op}.Models.cs` |
| A domain type used across operations | `Core/…/Models/`, one type per file |
| Convenience overloads | `Core/…/{Module}Client.Extensions.cs` |
| Module exceptions | `Core/…/Exceptions.cs` |
| Client methods for an operation | `Kurrent.Surge/…/{Module}Client.{Op}.cs` |
| Stateless mapping for that operation | `Kurrent.Surge/…/{Module}Client.{Op}.Mapping.cs` |
| Shared stateless mapping | `Kurrent.Surge/…/{Module}ClientMapping.cs` |
| A self-contained subsystem | its own subfolder, on whichever side owns it |

## What this convention rules out

- **An `Operations/` subfolder.** Operation partials sit flat at the module root.
- **A `Models/Maps/` subfolder.** Mapping folds into the `*.Mapping.cs` files.
- **Placeholder folders** such as `Models/NotSureYet/`. Resolve them into `Models/` or delete them.
- **Stateful mapper classes.** Use `MappingContext` and stateless extensions instead.
- **Model types in the implementation project.** They go in Core.
