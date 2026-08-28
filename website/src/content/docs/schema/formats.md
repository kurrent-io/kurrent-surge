---
title: Formats
description: JSON and Protobuf are built in. Avro is an opt-in package.
---

| Format | Package | Encode and decode | Schema for the registry | Transcode to JSON |
|---|---|---|---|---|
| JSON | built in | yes | from the CLR type | not necessary — the payload is JSON |
| Protobuf | built in | yes | from the CLR type, or from `.proto` text | yes, from `.proto` text |
| Avro | `Kurrent.Surge.Codecs.Avro` | yes | from the CLR type, or from `.avsc` text | yes, from `.avsc` text |
| Bytes | built in | passthrough | none | no |

## Schema versus transcoding

Every format has a schema, and the registry validates a payload against it. The SDK derives that
schema from the CLR type, so every row above has one.

Transcoding is a different job. It reads a stored payload and writes JSON **without a .NET type**,
which means it must read the schema as text. Protobuf has `.proto` and Avro has `.avsc`. A format
with no schema language has no text to read, so it cannot transcode.

Transcoding matters more in a server than in a client. A server reads a payload its process has no
type for; a client has the type, and the codec gives it the value.

## Avro

Avro is schema-first: the message type takes its shape from a schema definition rather than from a
class. That is what separates it from the built-in codecs, which derive their schema from the CLR
type.

```shell
dotnet add package Kurrent.Surge.Codecs.Avro
```

The package ships its own source generator, which produces the types and codecs from your `.avsc`
files at compile time.

## One codec per format

The codec registry holds one implementation per data format. Registering two for the same format
throws — the SDK will not silently pick a winner.
