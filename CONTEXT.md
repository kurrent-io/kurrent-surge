# Context

> Ubiquitous language for the Kurrent .NET SDK. One entry per domain term: its canonical name, what it means
> here, and the synonyms to avoid. The engineering skills read this before naming a domain concept.
> `/domain-modeling` fills it lazily as terms get resolved — leave it thin until a term actually needs pinning.

## Glossary

### Streaming pipeline

**Record**:
One event read from a KurrentDB stream — a payload plus a position. The wire/storage unit.
_Avoid_: event, message (those belong to the schema / catalog layer).

**Position**:
Where a record sits in the log — a single monotonic, ever-increasing `long`. Used as "how far have we got".
_Avoid_: offset, commit/prepare pair (KurrentDB has exactly one position per record, never a pair).

**Source**:
The origin of a pipeline's data. Yields batches of messages plus an opaque cursor marking how far it has
been read. KurrentDB is the first source.
_Avoid_: feed, input, producer, subscription.

**Cursor**:
A source's opaque resume token. The engine stores and hands it back blind; only the source that issued it
decodes it. For a KurrentDB source the cursor is a Position.
_Avoid_: offset, bookmark.

**Processor**:
The stage between source and sinks. Transforms each source message into zero or more output messages.
Filtering is emitting nothing.
_Avoid_: handler, transformer, mapper. (Collides with the legacy `RecordProcessor` — canonical name still open.)

**Sink**:
A terminal that delivers transformed output to one external system under a fixed schema. It is
position-unaware — the engine owns the source cursor — so sinks compose in fan-out (many sinks, one source).
Reaches exactly-once by idempotency (keyed on Position) or by a two-phase committer.
_Avoid_: output, target, writer, consumer.

**Projection**:
A terminal that folds the source into a materialized read model in a transactional database, writing to it
directly and committing the source Position inside its own transaction (co-location). It owns its position,
uses no engine checkpoint store, and cannot share a pipeline with other terminals. A Projection is **not** a Sink.
_Avoid_: calling it a sink; view-builder.

**Engine**:
The runner that reads from the source, drives the processor, fans output to the sinks, and persists one
checkpoint per barrier. The single coordinator of the source cursor.
_Avoid_: host, runner. (Pipeline = the whole assembly, not the runner; the current implementation is the `Drone`.)

**Barrier**:
The moment a checkpoint is taken. The engine makes sinks durable (flush / prepare) just before it.

**Checkpoint**:
What the engine persists at a barrier: the source cursor, plus — only for two-phase sinks — their committables.
The whole pipeline advances atomically or not at all.
_Avoid_: offset-save, snapshot.

**Committable**:
A two-phase sink's opaque handle to in-flight work, persisted in the checkpoint and re-fed to the sink's
commit on recovery. Only sinks that genuinely need 2PC produce one; idempotent and co-locating terminals do not.
_Avoid_: commit token, transaction handle.
