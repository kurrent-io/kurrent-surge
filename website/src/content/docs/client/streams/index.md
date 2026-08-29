---
title: Streams
description: Everything you do with streams. Write to them, read from them, and manage them.
---

A stream is a named, ordered sequence of records. Almost everything you do with KurrentDB happens
here, and it divides into three:

| | What you do there |
|---|---|
| [Append](/client/streams/append/) | Write records to a stream, and decide what happens when another writer races you |
| [Read](/client/streams/read/) | Read one stream, read everything, fetch a single record, or follow a live tail |
| [Manage](/client/streams/manage/) | Inspect a stream, remove it or its events, and set retention and metadata |

All three come off the same `client.Streams`:

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

await client.Streams.AppendStream(record);
await client.Streams.ReadStream("order-123");
await client.Streams.GetStreamInfo("order-123");
```

If you are new to this, read in that order. Appending shows how records get in and how concurrency
works; reading shows how they come back; managing is what you reach for later, when data has a
lifecycle.
