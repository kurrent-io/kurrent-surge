---
order: 1
title: Append
index: false
---

# Append

<CopyMarkdownButton />

Streams hold an ordered sequence of records. There are two write surfaces, and the right one depends on whether the transaction needs to touch more than one stream.

## Pick the right operation

| Operation                                | When to use it                                                                            |
|------------------------------------------|-------------------------------------------------------------------------------------------|
| [Append to one stream](to-one-stream.md) | The common case. All records go to one stream, concurrency is guarded by `ExpectedStreamState`, failures surface as typed exceptions. |
| [Append across streams](across-streams.md) | An invariant spans more than one stream. Each record targets its own stream, pre-commit `ConsistencyCheck`s guard any affected stream, the whole batch commits atomically. |

A single-stream append is the more ergonomic API and what most code paths want. The cross-stream surface trades that ergonomics for atomicity over several streams and a richer set of pre-commit checks, at the cost of a flatter error model: violations come back as a list rather than as one typed exception.

Under the hood both flow through the same V2 `AppendRecords` RPC. `AppendStream` is sugar that pins every record to one stream and unwraps the single violation into a typed exception.
