# Read Start-Position Resolution

> How `ReadRecords` accepts, rejects, and resolves a `RecordPosition` start. The executable contract of
> `StreamsClient.ReadRecords` (`ReadRecordsCore`), grounded in the code as it ships, plus the server
> guarantees that the contract depends on and the subscription counterpart.

## Start here (plain version)

A start position answers **"where do I begin reading?"** It can be written in **two languages**:

- **Stream language.** A stream name + a *revision* (that stream's own `0, 1, 2…` counter).
- **Log language.** One *log position*, a single global address across the whole database.
- Plus two shortcuts that work everywhere: **Beginning** and **End**.

**What you read decides the language the server needs:**

- Reading **one stream** → needs a **revision**.
- Reading the **whole log** (any filter that is not one specific stream) → needs a **log position**.

**The main question: did you give it the language it wants?**

| You're reading… | You gave… | Result |
|---|---|---|
| either | **nothing (no position)** | Free. Starts at the **beginning** reading forwards, at the **end** reading backwards |
| one stream | that stream's revision | Free |
| one stream | Beginning / End | Free |
| one stream | a log position (with or without *another* stream's name and revision) | Works. **1 extra server call** translates it into this stream's revision |
| one stream | *another* stream's name, no revision, no log position | Same as no position |
| one stream | a revision with no log position, and the stream name is not this stream | **Error** |
| the log | a log position | Free |
| the log | Beginning / End | Free |
| the log | a stream name, with or without a revision, no log position | Works. **1 extra call** translates it into a log position. No revision means that stream's first record forwards, its last record backwards |
| the log | a revision with no stream name and no log position | **Error** |

**Right language → free. Wrong language → one translation call, in either direction, if the start
carries something to translate. A start with nothing to translate throws `ArgumentException`.**

Three things that surprise people:

- **No position defaults to the direction's edge, not a fixed end.** Forwards → the beginning.
  Backwards → the end. A fixed default of "the end" gives a forwards read with no records.
- **"From the End, going forwards" returns nothing, and that is fine, not an error.** There is nothing
  past the end. (Both stream and log.)
- **A stream name works as a whole-log start too.** It is translated into a log position first (one
  extra call), so `ReadRecords` and `ReadStream` behave identically. (Anchoring a multi-stream log read
  at one stream's position skips earlier records of other streams. That is intended: you asked to start
  there.)

**About "sentinel alignment".** *Beginning* and *End* are labels, not addresses. Before a stream read,
the SDK replaces each label with the matching revision (the first revision or the end of the stream), so
the server gets a value it accepts. You do not do this step.

The sections below are the precise contract behind this summary.

## Model

A `RecordPosition` start carries two halves plus sentinels:

- **LOG half.** `LogPosition` (a concrete value, or the `Earliest` / `Latest` sentinel).
- **STREAM half.** `Stream` + `Revision`.

`ReadRecords` routes by **filter** to a **target**, and the wire needs the start in the target's currency:

| Filter | Target | Wire wants |
|--------|--------|-----------|
| `StreamNameFilter`, or a pure-equality expression such as `stream = 'S'` | **stream** (S) | a `Revision` |
| regex / prefix / other expression / index / `EmptyFilter` | **log** (`$all`) | a `LogPosition` |

`ResolveStartPosition` routes on `filter.UnwrapFilter()`. For an `ExpressionFilter`, that is its
`ServerFilter`. A single case-sensitive equality on `stream` extracts a `StreamNameFilter`, so the read
targets that stream.

**Resolution** = a server round-trip that translates the start into the target's currency, driven by the
single entry point `ResolveStartPosition(filter, start, direction)` (which delegates the I/O to the
`ResolveReadStart` leaf). It is the exception, not the rule. See [When to resolve](#when-to-resolve).

### Three universal rules

1. **Each target has one address, and a foreign stream half is never trusted.** On a stream target, a
   stream half that names the target and carries a revision is used directly. Any other start uses its
   log half. On a log target, a log half is used directly and the stream half is ignored. With no log
   half, a named stream is the anchor.
2. **Resolution goes whichever way the target needs.** A STREAM target with a start that is not native
   and has a log half resolves **log → stream**. A LOG target with no log half and a stream name resolves
   **stream → log**. A missing revision becomes `Revision.Min` forwards and `Revision.Max` backwards.
   `ReadRecords` and `ReadStream` converge, because both run through `ResolveStartPosition`. (A log read
   anchored at a stream's position does skip earlier-in-log matches of other streams. That is intended.)
   A start that neither target rule can address throws `ArgumentException`. See the throw rows in the
   two matrices.
3. **"From the end, forwards" is an empty success, not an error.** `Max`+Forwards (stream) and
   `Latest`+Forwards (log) read nothing and return zero records. (Server-verified: a stream `End`+Forwards
   wire call *errors*, so the SDK short-circuits it client-side. `$all` `End`+Forwards returns empty
   natively.)

## Matrix: STREAM target (`StreamNameFilter` → stream S)

Sentinel alignment runs first. `ResolveStartPosition` does it inline, not in a named method. It maps
`Earliest → (S, Revision.Min)` and `Latest → (S, Revision.Max)`. "Native" means
`start.Stream == S` **and** `Revision` set.

| Start shape | Forwards | Backwards | Resolve? | RPCs |
|-------------|----------|-----------|----------|------|
| `Earliest` | full read from first revision | at most revision 0 (degenerate) | no, wire-native after align | 0 |
| `Latest` | **empty** (`Max`+Fwd short-circuit) | from last revision backward | no, wire-native after align | 0 |
| native (`Stream == S`, revision set) | inclusive from revision | inclusive from revision | no, used directly | 0 |
| log half, stream half not native (unset, `S` without a revision, or a foreign stream) | first S-record at `position ≥ X` | last S-record at `position < X` | **yes, log→stream** (foreign stream half discarded) | 1 |
| revision set, no log half, `Stream ≠ S` (unset or foreign) | **throw** `ArgumentException`, cannot address S | **throw** | no | 0 |
| no log half, no revision (any stream name, foreign included) | from first revision (`Min`) | from last revision (`Max`) | no, substituted to the direction edge | 0 |

- A full position whose stream **matches** S is **native**: the revision is used directly and the log half
  is ignored (0 RPCs). Only a **non-matching** or revision-unset start triggers the log→stream resolve.
- "No position" (no log half **and** no revision) substitutes the direction edge **before** the native
  check. The substitution names S as the stream, so a foreign stream name without a revision is dropped
  and the start lands on the native path (0 RPCs).
- Resolve outcome `Unset` (no S-record qualifies from X) → **empty** read, not an error.

## Matrix: LOG target (regex / prefix / index / empty / expression that is not a single stream equality)

No sentinel alignment here. The sentinels map at the wire (`Earliest → AllOptions.Start`,
`Latest → AllOptions.End`, in `StreamsClientMapping.ConvertToAllOptions`). `ResolveStartPosition` uses a
log half directly when one is present. `ReadRecordsCore` reads from the resolved start and returns no
records when the resolved start is `Unset`.

| Start shape | Forwards | Backwards | Resolve? | RPCs |
|-------------|----------|-----------|----------|------|
| `Earliest` | full log from start | **empty** (see the note below the table) | no | 0 |
| `Latest` | **empty** (head of log, nothing ahead) | from end backward | no | 0 |
| log half present (log-only **or** full) | INCLUSIVE, the record AT the position | EXCLUSIVE on the plain `$all` route, the record before the position (server quirk). INCLUSIVE on the secondary-index route | no, log half used directly and stream half ignored | 0 |
| stream `S` + revision, no log half | resolved to the log position of S's record at the revision, then read from there | same. The first record is S's record at the revision (the anchor) | **yes, stream→log** | 1 |
| stream name `S`, no revision, no log half | resolved from S's first record (`Min`) | resolved from S's last record (`Max`). The first record is S's last record (the anchor) | **yes, stream→log** | 1 |
| revision with no stream name, no log half | **throw** `ArgumentException` | **throw** | no | 0 |
| **`Unset` / no position** | full log from start (`Earliest`) | from end backward (`Latest`) | no, substituted to the direction edge | 0 |

`Earliest` + Backwards sends `AllOptions.Start` with a backwards direction. The server maps
`AllOptions.Start` to log position 0 (`ReadReq.Types.Options.Types.AllOptions.ToPosition`).
`AllReader.ReadAllEventsBackwardInternal` then calls `TFChunkReader.TryReadPrev`, which fails at a
cursor position of 0 or less, so the read returns no records. This result comes from the server
source. No test pins it.

The stream→log rows do one single-record stream read at the revision, in the read direction. If the
stream is missing, deleted, or tombstoned, the resolve returns `Unset` and the read is empty.

The stream read does not resolve links. The record it returns is the row in S at the revision, the
same row that a log read gives at that position. On a link stream such as `$ce-orders`, the row is
the `$>` link, not its target.

### Anchor delivery

A read that goes backwards from a stream anchor must start with the anchor record. The plain `$all`
backwards read excludes its start position, so the server never sends the anchor.

The resolve read already has the anchor record, so `ResolveStartPosition` returns it with the start
position. `ReadRecordsCore` then delivers the anchor before the `$all` read starts. There is no extra
RPC.

`ReadRecordsCore` applies to the anchor the checks that `ReadCore` applies to each log record:

1. Decode the anchor, unless `SkipDecoding` is set. An unknown schema throws
   `MessageTypeResolutionException` when `ThrowOnUnresolved` is set.
2. Drop the anchor if it is a system record and the filter is not `RecordFilter.None`.
3. Drop the anchor if `filter.IsMatch` rejects it. The filter always runs on the client here,
   because no server filter saw the anchor.
4. Count a delivered anchor against `Limit`.

The secondary-index route (an `IndexFilter`, for example `RecordFilter.ByCategory`) reads backwards
INCLUSIVE of its start position. The server sends the anchor, so `ReadRecordsCore` does not deliver
it a second time. Forwards reads are inclusive on both routes and need no anchor delivery.

## When to resolve

> **Resolve (1 RPC) iff the start is in the wrong currency for its target:** a STREAM target with a
> start that is not native and has a log half → **log→stream**; a LOG target with no log half and a
> stream name (with or without a revision) → **stream→log**.

Every other *allowed* case is **0 RPCs**: sentinels map at the wire, native revisions pass straight
through, a log target with a log half uses it directly, and "no position" becomes the direction edge.
Both directions live in one entry point, `ResolveStartPosition(filter, start, direction)`, which
delegates the I/O to the `ResolveReadStart` leaf. `ReadStream` is a thin passthrough. It builds a
`ReadRecordsOptions` and calls `ReadRecords`.

## Why one filtered `$all` read resolves log → stream

`ResolveReadStart` resolves a log half X to a record of stream S with one `$all` read. The read starts at
X, goes in the read direction, and has a `StreamRegexFilter` of `^S$` (S escaped with `Regex.Escape`).
The SDK takes the first record and ends the call. The wire count is unbounded. The server applies the
direction contract:

- Forwards: the first record of S at `position ≥ X`. The `$all` forwards read is inclusive, and a
  scavenged X moves forward to the next surviving record.
- Backwards: the last record of S at `position < X`. The `$all` backwards read is exclusive.

That one call covers every case:

| Case | Forwards | Backwards |
|------|----------|-----------|
| the record at X belongs to S | that record | the previous record of S |
| X is before S's first record | S's first record | none → empty read |
| X is after S's last record | none → empty read | S's last record |
| X is inside S's range, at a record of another stream | first S-record at `position ≥ X` | last S-record at `position < X` |
| scavenge removed the record at X | same as the rows above. The scan skips the gap | same |

A stream read from the resolved revision (inclusive) then gives the same records as a `$all` read from X
filtered to S. Revision and log position are co-monotonic within a stream, so the two ranges are equal.

**Performance.** The server scans the log from X to the first matching record. It examines at most
`FilterMaxSearchWindow` records for each page (4096 by default, `StreamDefaults.DefaultMaxSearchWindow`).
For the intended uses, such as a checkpoint resume or a read anchor, X is recent and the scan is short.
For a sparse stream and an X far from its records, the scan is long. Its cost grows with the log
distance, not with the length of the stream.

**Alternative not used: binary search over revisions.** Co-monotonicity makes the boundary searchable
with single-record stream reads, in about log₂(n) reads (about 20 for a stream of one million records).
That cost is fixed by the stream length and does not depend on log density. It costs many round trips
and more code where the scan costs one call. The scan stays the choice unless it is slow in practice.

## Boundary flavors

Reads and subscriptions need adjacent boundary records, because reads are inclusive and a subscription
resumes after its start. Two methods in `StreamsClient.Resolve.cs` give the three boundary flavors:

| Flavor | Used by | Boundary | RPCs |
|--------|---------|----------|------|
| AtOrAfter(X) | read forwards (`ResolveReadStart`) | first S-record with `position ≥ X` | 1 |
| Before(X) | read backwards (`ResolveReadStart`) | last S-record with `position < X` | 1 |
| AtOrBefore(X) | subscribe (`ResolveSubscriptionStart`) | last S-record with `position ≤ X` | 1 or 2 |

`ResolveSubscriptionStart` cannot use one backwards read, because the backwards read excludes the record
at X. It first reads forwards from X to get the first S-record. If the position of that record is X,
that record is the boundary. If not, a
backwards read from X gives the last S-record before X.

`Subscribe` uses the boundary differently from a read. Its start rules also differ:

- Stream target: `Subscribe` does its own sentinel alignment, inline at its call site in
  `StreamsClient.Subscribe.cs`. A native start is used directly. Any other start must have a log half,
  or `Subscribe` throws `ArgumentException`. This includes "no position" and a foreign stream name
  without a revision. `Subscribe` does not substitute a direction edge.
- The server resumes a stream subscription after the boundary revision (exclusive). A resolve outcome
  of `Unset` means no record of S is at or before X. `Subscribe` then starts from `Revision.Min` and
  delivers the whole stream. The subscription is not empty.
- Log target: the start must have a log half, or `Subscribe` throws `ArgumentException`. A stream half
  is never translated into a log position.

## Start inclusivity (server contract)

| Path | Semantics |
|------|-----------|
| stream read forwards | INCLUSIVE (record at revision) |
| stream read backwards | INCLUSIVE |
| `$all` read forwards | INCLUSIVE (record AT position) |
| `$all` read backwards, plain route | EXCLUSIVE (server quirk) |
| `$all` read backwards, secondary-index route | INCLUSIVE |
| stream subscribe | EXCLUSIVE (after the revision) |
| `$all` subscribe | EXCLUSIVE (after the position) |

The log→stream resolver mirrors the read rows. Forwards → first S-record `≥ X` (AtOrAfter). Backwards →
last S-record `< X` (Before).

## Server guarantees

The resolution design depends on these KurrentDB server behaviors:

| Guarantee | Server source | Effect on resolution |
|---|---|---|
| Replication keeps log positions. A follower appends the leader's records at the same positions. | `ClusterStorageWriterService` handles `DataChunkBulk` and `RawChunkBulk`. It logs an error and drops a bulk that arrives at a different position | A position is valid on every node |
| Scavenge keeps the positions of the records that survive. | `ScavengedChunkWriter` writes each survivor through `TFChunkScavenger.WriteRecord`, which records a `PosMap` from the logical position to the new physical offset | A read from a surviving record's position finds that record |
| An exact lookup at a scavenged position fails. | `TFChunk` scavenged read side: `TryReadAt` returns `RecordReadResult.Failure` when `TranslateExactPosition` finds no entry | No resolve path uses an exact lookup |
| A `$all` forwards scan skips scavenged gaps. | `TFChunkReader.TryReadNext` calls `TryReadClosestForward`, and `AllReader` reads through it | A `$all` read from a scavenged position starts at the next survivor |
| The client protocol has no read from a log position into a stream revision. The stream index is keyed by stream and revision, and gives a log position. | `TableIndex.GetRange` and `TableIndex.TryGetOneValue`. `TableIndex.TryGetLatestEntry` with a `beforePosition` exists, but only the scavenge calculator (`IndexReaderForCalculator`) calls it | The SDK does the log→stream resolve |
| A stream read from a gap revision returns the next survivor. | `IndexReader.ReadStreamEventsForwardInternal` and `ReadStreamEventsBackwardInternal` read a revision range through `TableIndex.GetRange` | A gap after scavenge does not break a stream read or a stream→log resolve |
| Tombstones survive scavenge. | `EventCalculator.DecideEvent` keeps the last event of each stream. `ChunkExecutor` removes a tombstone only when `UnsafeIgnoreHardDeletes` is set | A tombstoned stream stays tombstoned |
| Revision and log position are co-monotonic within a stream. | `StorageWriterService` gives a stream's new events the revisions after its current version and appends them at the end of the log | A boundary in one currency has one match in the other |

### Scavenge tolerance

Only an exact position lookup is fragile after a scavenge. Every scan and range path skips the gap.

| Path | Start | Result after scavenge |
|------|-------|-----------------------|
| `$all` read or subscription | scavenged log position | Tolerant. The scan starts at the next survivor |
| `InspectRecord` | scavenged log position | Returns the next surviving record, not the one at the position. It sends a `$all` forwards read with a count of 1 |
| stream read | gap revision | Tolerant. The index range skips the gap |
| stream subscription | gap revision | Tolerant. The same index range path |

## Sources

- Code: `src/Kurrent.Surge/Client/Modules/Streams/StreamsClient.Resolve.cs` holds `ResolveStartPosition`, the
  single routing entry. It delegates the I/O to the `ResolveReadStart` leaf. The same file holds
  `ResolveSubscriptionStart`. `StreamsClient.Read.cs` holds `ReadRecords` and the `ReadStream` passthrough.
  `StreamsClient.Subscribe.cs` holds `Subscribe`. `StreamsClientMapping.cs` holds `ConvertToAllOptions`
  and `ConvertToStreamOptions`.
- Sentinel alignment: `ResolveStartPosition` does it inline for reads. `Subscribe` has its own inline copy.
- Tests: `test/Kurrent.Surge.Tests/Integration/Streams/Read/`. `StartSemanticsTests` (inclusivity),
  `StartRoutingTests` (routing + `log_targeted_read_resolves_stream_revision_start_to_log_position`),
  `ResolveStartTests` (the `ResolveReadStart` leaf), `ReadTests.reading_forwards_from_the_end_of_a_stream_returns_no_records`
  (`Max`+Forwards → empty). The `StartRoutingTests.log_targeted_read_backwards_*` tests pin anchor
  delivery: with no position, from a revision, from `Revision.Min`, with the default filter, with a
  filter that rejects the anchor, on the index route, with `Limit = 1`, and on a `$ce-` link stream.
- Server-verified (KurrentDB source):
  - Stream `End`+Forwards is rejected (`IndexReader.ReadStreamEventsForwardInternal` `Ensure.Nonnegative`).
  - `$all` `End`+Forwards returns empty. `StorageReaderWorker` resolves `TFPos.HeadOfTf` to the writer
    checkpoint for both directions.
  - A stream read backwards from `Revision.Min` reads the range `[0, 0]`
    (`IndexReader.ReadStreamEventsBackwardInternal`).
  - Subscriptions are exclusive. `Enumerator.AllSubscription` skips `position <= checkpoint`.
    `Enumerator.StreamSubscription` reads from `checkpoint + 1`.
  - A plain `$all` read backwards excludes its start position. `AllReader.ReadAllEventsBackwardInternal`
    keeps a record only when its post-position is at or before the start. A start inside a record
    fails with `LogPositionNotFoundException`.
  - A secondary-index read backwards includes its start position. `Enumerator.ReadIndex` reads the
    first page with `excludeStart = false`, and the index SQL uses `log_position<=$2`.
