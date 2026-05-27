# Module Organization

> File layout, naming conventions, and mapping patterns for client modules under `src/Kurrent.Client/Modules/`.

## Folder Structure

Each module lives in its own folder under `Modules/`. The layout is flat — operation files sit at the
module root alongside the client base partial. Subfolders are reserved for shared domain types and
self-contained subsystems.

```text
Modules/{Module}/
├── {Module}Client.cs                          # base partial — constructor, service clients, fields
├── {Module}Client.{Op}.cs                     # operation methods (one partial per operation)
├── {Module}Client.{Op}.Models.cs              # operation-specific types: request, response, options
├── {Module}Client.{Op}.Mapping.cs             # stateless mapping extensions for that operation
├── {Module}Client.Extensions.cs               # convenience overloads (C# 14 extension members)
├── Exceptions.cs                              # module-scoped exception types
├── Models/                                    # shared domain types used across operations
│   ├── Record.cs
│   ├── StreamName.cs
│   └── ...
└── {Subsystem}/                               # self-contained subsystem (e.g. Filters/, Metadata/)
```

Not every operation needs all file slots. A simple operation may only have the `.cs` file. Only create
`.Models.cs` and `.Mapping.cs` when the operation has types or mapping logic that warrant a separate file.

## File Placement Rules

| What you have | Where it goes |
|---|---|
| Client methods for an operation | `{Module}Client.{Op}.cs` |
| Request, response, options for an operation | `{Module}Client.{Op}.Models.cs` |
| Stateless mapping extensions for an operation | `{Module}Client.{Op}.Mapping.cs` |
| Convenience overloads (C# 14 `extension(...)`) | `{Module}Client.Extensions.cs` |
| Domain types shared across operations | `Models/` subfolder, one type per file |
| Shared stateless mapping extensions | `{Module}Mapping.cs` at module root |
| Module exceptions | `Exceptions.cs` at module root |

## Naming: Mapping vs Mapper

Two suffixes, each encoding a design constraint:

| Suffix | What it is | Statefulness | Example |
|---|---|---|---|
| `*Mapping` | Stateless extension methods — pure type conversions | None; receives explicit `MappingContext` | `StreamsClient.Append.Mapping.cs`, `ProtocolMapping.cs` |
| `*Mapper` | Stateful class with injected dependencies | Holds serializers, decoders, policies | Last resort — prefer `MappingContext` pattern |

Default to `*Mapping`. Only introduce a `*Mapper` class when stateless extensions with a context
parameter genuinely do not work.

## MappingContext Pattern

Operations that need dependencies for mapping (serializers, registry policies) use a context struct
instead of a stateful mapper class. The context bundles dependencies and the cancellation token into
a single value, created once per operation call.

```csharp
readonly record struct MappingContext(
    ISchemaSerializerProvider Serializer,
    SchemaRegistryPolicy Policy,
    CancellationToken CancellationToken
);
```

Mapping extensions receive the context explicitly:

```csharp
static class AppendRecordsMapping {
    extension(AppendRecordsRequest source) {
        public async ValueTask<Contracts.AppendRecordsRequest> ToContract(MappingContext ctx) {
            // uses ctx.Serializer, ctx.Policy, ctx.CancellationToken
        }
    }

    extension(Contracts.AppendRecordsResponse source) {
        public AppendRecordsResponse ToResponse() {
            // pure conversion, no context needed
        }
    }
}
```

Call site in the client method:

```csharp
var ctx = new MappingContext(SerializerProvider, RegistryPolicy, cancellationToken);
var serviceRequest = await request.ToContract(ctx).ConfigureAwait(false);
var rsp = await ServiceClient.AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken).ConfigureAwait(false);
return rsp.ToResponse();
```

Benefits:

- **Explicit** — every dependency visible at the call site, no hidden state
- **Testable** — create a context with test doubles, call the extension directly
- **Composable** — `request.ToContract(ctx)` internally calls `record.ToContract(ctx)`
- **Zero allocation** — `readonly record struct` lives on the stack

## Streams Module Example

Applying the convention to the Streams module (the largest and most complex):

```text
Modules/Streams/
├── StreamsClient.cs
├── StreamsClient.Append.cs
├── StreamsClient.Append.Models.cs
├── StreamsClient.Append.Mapping.cs
├── StreamsClient.Read.cs
├── StreamsClient.Subscribe.cs
├── StreamsClient.Subscribe.Resilient.cs
├── StreamsClient.Subscribe.Models.cs
├── StreamsClient.Manage.cs
├── StreamsClient.Query.cs
├── StreamsClient.Extensions.cs
├── StreamsMapping.cs                         # shared stateless mapping extensions
├── Exceptions.cs
├── Models/
│   ├── AppendRecord.cs
│   ├── ConsistencyViolation.cs
│   ├── LinkTo.cs
│   ├── LogPosition.cs
│   ├── Partition.cs
│   ├── ReadDirection.cs
│   ├── ReadRecordsRequest.cs
│   ├── ReadStream.cs
│   ├── Record.cs
│   ├── RecordBatchStream.cs
│   ├── RecordPosition.cs
│   ├── RecordSchemaInfo.cs
│   ├── RecordStream.cs
│   ├── Revision.cs
│   ├── SchemaDataFormat.cs
│   ├── SequenceId.cs
│   ├── StreamInfo.cs
│   ├── StreamName.cs
│   ├── StreamPosition.cs
│   └── StreamState.cs
├── Filters/
└── Metadata/
```

## What This Convention Eliminates

- **`Operations/` subfolder** — operation partials go flat at the module root
- **`Models/Maps/` subfolder** — mapping extensions fold into `*.Mapping.cs` files
- **`Models/NotSureYet/`** — resolve into `Models/` or remove
- **Stateful mapper classes** — replaced by `MappingContext` + stateless extensions
- **Ambiguity about file placement** — the naming convention answers "where does X go?"
