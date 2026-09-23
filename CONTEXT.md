# Context

> Ubiquitous language for the Kurrent .NET SDK. One entry per domain term: its canonical name, what it means
> here, and the synonyms to avoid. The engineering skills read this before naming a domain concept.
> `/domain-modeling` fills it lazily as terms get resolved — leave it thin until a term actually needs pinning.

## Glossary

### Product

**Kurrent Surge**:
The product, written as two words with no dot — in prose, headings, docs, commit messages, and issue
text. It is the .NET SDK for the Kurrent Platform and KurrentDB, but "SDK" is what it *is*, not what
it is *called*: use the descriptor to explain it once, then refer to it by name.
_Avoid_: Surge on its own, the SDK, the .NET SDK, the client (as a name for the whole product —
`KurrentClient` is one type inside it).

**`Kurrent.Surge`**:
The NuGet package and the root namespace — dotted, and only ever in code, a package reference, or a
`using`. The dotted form is never the product's name in prose. "Install `Kurrent.Surge`" is right;
"Kurrent.Surge is the SDK" is not — that is Kurrent Surge.

### Streaming pipeline

**Stream**:
A named, ordered sequence of records in KurrentDB, addressed by its stream name.
_Avoid_: topic, queue.

**Record**:
One event read from a KurrentDB stream — a payload plus a position. The wire/storage unit.
_Avoid_: event, message (those belong to the schema / catalog layer).

**Position**:
Where a record sits in the log — a single monotonic, ever-increasing `long`. Used as "how far have we got".
_Avoid_: offset, commit/prepare pair (KurrentDB has exactly one position per record, never a pair).

**Source**:
The origin of a processor's records. The code type is `IDataSource`. KurrentDB is the only source.
_Avoid_: feed, input, producer, subscription.

**Processor**:
A type that handles the records a source delivers. It implements `IProcessor`, usually through
`IRecordProcessor` or `IRecordBatchProcessor`.
_Avoid_: handler, transformer, mapper.

**Checkpoint**:
The position a processor persists so that it can resume after a restart. KurrentDB stores it in the
`$processors/{id}/checkpoints` stream.
_Avoid_: offset-save, snapshot.

### Client

**Connection string**:
The `kurrentdb://` string that configures a client connection.
_Avoid_: URI, address (when referring to `kurrentdb://` strings).
