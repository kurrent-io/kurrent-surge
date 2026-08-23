# Protobuf to JSON, guided by schema text

Rewrites a protobuf payload as canonical JSON from `.proto` text alone. No generated message types,
no `protoc`, nothing read from disk.

```csharp
var schema = ProtoSchemaSet.Build(new ProtoFile("orders/v1/order.proto", protoText));
var writer = new ProtobufJsonWriter(schema, "orders.v1.Order");

string json = writer.Write(payload);          // or Write(payload, utf8JsonWriter)
```

A schema arrives as text, so a registry can hand one over without a build step. Imports resolve
against the other files in the set; `google/protobuf/*.proto` resolves to the descriptors compiled
into Google.Protobuf.

## Performance

Apple M2 Max, .NET 10.0.9 Arm64, BenchmarkDotNet 0.15.8 at default warmup and iteration counts.
`Lines` is the number of nested messages in the payload, so it scales both size and depth. Standard
deviation stays under 1% of the mean on every row.

| Lines | Implementation | Mean | Ratio | Allocated | Alloc ratio |
|------:|----------------|-----:|------:|----------:|------------:|
| 0 | **`ProtobufJsonWriter`** | **1.532 μs** | **0.60** | **64 B** | **0.007** |
| 0 | Google `JsonFormatter`, compiled types | 2.387 μs | 0.94 | 3 496 B | 0.38 |
| 0 | `DynamicMessage` baseline | 2.538 μs | 1.00 | 9 112 B | 1.00 |
| 8 | **`ProtobufJsonWriter`** | **3.547 μs** | **0.64** | **576 B** | **0.03** |
| 8 | Google `JsonFormatter`, compiled types | 5.264 μs | 0.95 | 5 864 B | 0.32 |
| 8 | `DynamicMessage` baseline | 5.546 μs | 1.00 | 18 376 B | 1.00 |
| 64 | **`ProtobufJsonWriter`** | **17.719 μs** | **0.66** | **4 160 B** | **0.05** |
| 64 | Google `JsonFormatter`, compiled types | 24.977 μs | 0.93 | 22 512 B | 0.27 |
| 64 | `DynamicMessage` baseline | 27.009 μs | 1.00 | 82 960 B | 1.00 |

The writer is 29–36% faster than Google's own formatter and allocates a twentieth to a fiftieth of
what it does — while resolving the schema at run time, where the formatter is handed types generated
at build time.

Two notes so the figures survive scrutiny. `JsonFormatter` emits UTF-16 and offers no UTF-8
destination, so its allocation is not strictly like-for-like; it is charged for the output form its
API produces. Benchmarks run on BenchmarkDotNet's in-process toolchain, which trades per-case process
isolation for a build this repository's code generation can complete.

## How it works

```
.proto text ─▶ ProtoLexer ─▶ ProtoParser ─▶ FileDescriptorProto
                                                    │
                                       FileDescriptor.BuildFromByteStrings
                                                    │
 wire bytes ─▶ ProtoWireReader ─────────────▶ MessageDescriptor ─▶ Utf8JsonWriter
```

The parser emits Google's own `FileDescriptorProto` and hands it to
`FileDescriptor.BuildFromByteStrings`. Type linking, name resolution and edition feature merging
therefore come from the reference implementation rather than from a second reading of the
specification. Google.Protobuf ships no `.proto` reader at any visibility, so the front-end has no
substitute — `protoc` is a separate native binary and cannot run in-process.

Nothing is materialised on the decode path. A scalar is never boxed, a repeated field never builds a
list, and a string field writes the wire bytes it already holds. The wire format forces two passes
per message: fields arrive in any order, a repeated field's elements can be split across non-adjacent
runs, and a repeated singular field takes its last value.

## Coverage

proto2, proto3 and editions 2023. Scalars, nested messages, repeated and packed fields, maps, oneofs,
open and closed enums, groups, extensions, proto2 required and explicit defaults, and the well-known
types — `Timestamp`, `Duration`, `Struct`, `Value`, `ListValue`, `FieldMask`, `Empty`, `Any` and the
wrappers.

Correctness is diffed against Google's `JsonFormatter` on schemas whose types are also generated, so
every case has a typed oracle.

## Options

| Option | Default | Effect |
|--------|---------|--------|
| `UseProtoFieldNames` | off | emits `order_id` rather than `orderId` |
| `EmitDefaults` | off | emits absent implicit-presence fields, repeated fields and maps |
| `EnumsAsIntegers` | off | emits enum numbers rather than names |
| `MaxDepth` | 100 | rejects a payload nesting deeper than this |

`MaxDepth` is not a tuning knob. Nesting is decoded by recursion, a recursive message type lets a few
bytes ask for thousands of levels, and a stack overflow cannot be caught. The default matches the
limit the reference implementations enforce.
