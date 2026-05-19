# Serialization docs verification — follow-up tasks

Findings from running every C# snippet under `docs/client/serialization/` against the local KurrentDB on `:2113`. 27 of 32 snippet checks passed. Three issues need attention, plus one already filed.

Related Linear issue already filed: [DEV-1731 — MessageTypeMapper throws KeyNotFoundException on reverse-collision](https://linear.app/kurrent/issue/DEV-1731/messagetypemapper-throws-keynotfoundexception-on-reverse-collision)

---

## 1. Bytes append is broken end to end

**Severity**: High. The entire `formats/bytes.md` page documents a path that does not work against the server today.

**File**: `docs/client/serialization/formats/bytes.md` (Append bytes section, lines 17-29)

**What the doc says**

```csharp
byte[] payload = Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.New()
    .Stream("blobs")
    .Value(payload)
    .DataFormat(SchemaDataFormat.Bytes)
    .Build();

await client.Streams.AppendStream(record);
```

**What actually happens**

Server rejects the append:

```
InvalidArgumentException: The 'AppendRequest' is invalid:
 -- Schema name must not be empty
 -- Schema name can only contain unicode letters, digits, underscores, dashes,
    periods, colons, and dollar signs. Attempted Value:
```

**Root cause**

`BytesPassthroughSerializer.Serialize` at `src/Kurrent.Client/Schema/Serialization/Bytes/BytesPassthroughSerializer.cs:19-39` sets `SchemaFormat = Bytes` on the outgoing context but never sets `SchemaName`. It implements `ISchemaSerializer` directly instead of inheriting from `SchemaSerializerBase`, which is where the mapper/strategy lookup happens (`src/Kurrent.Client/Schema/Serialization/SchemaSerializerBase.cs:29-67`). The result is an empty schema name on the wire, which the server refuses.

**Workaround attempted, did not work**

Pre-registering the type in the mapper (`opts.Schema.Mapper.Map<byte[]>("custom.payload")`) makes no difference. The bytes path bypasses the mapper entirely. There is no user-facing way to make this work today.

**Why nobody noticed**

No integration test exercises the bytes append path. A repo-wide grep for `SchemaDataFormat.Bytes` in `test/Kurrent.Client.Tests/Integration` returns nothing.

**Recommended actions**

- File a Linear issue under DevEx / Kurrent SDK .NET. Title suggestion: "BytesPassthroughSerializer does not propagate a schema name, breaking bytes appends".
- Fix in the SDK: have the bytes serializer go through `SchemaManager` like the other formats, or populate the schema name from the type mapper / naming strategy directly inside `BytesPassthroughSerializer.Serialize`.
- Add an integration test that appends a bytes record and reads it back.
- Until fixed, the docs page is selling a feature that does not work. Either land the SDK fix first, or temporarily mark the page as "preview / not yet wired up".

---

## 2. JSON customization snippet does not compile

**Severity**: Medium. Compile-time failure on a copy-paste snippet.

**File**: `docs/client/serialization/formats/json.md` (Customize the serializer, lines 30-36)

**What the doc says**

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Json = JsonSchemaSerializerOptions.Default with {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    });
```

**Reality**

`JsonSchemaSerializerOptions` at `src/Kurrent.Client/Schema/Serialization/Json/JsonSchemaSerializerOptions.cs:10-57` only exposes:

- `UserJsonContexts`
- `UseProtobufFormatter`
- (inherited) `SchemaRegistration`
- (inherited) `ConsumeMappedOnly`

`PropertyNamingPolicy`, `DictionaryKeyPolicy`, `PropertyNameCaseInsensitive`, `DefaultIgnoreCondition`, `UnmappedMemberHandling`, `UnknownTypeHandling`, `NumberHandling`, and the TimeSpan/NodaTime converter setup are all hard-wired inside the private `BuildOptions(...)` method. The `with { PropertyNamingPolicy = ... }` snippet fails to compile.

**Recommended actions**

Two options, pick one:

- **Doc-only fix**: rewrite the Customize section around the knobs that are actually settable. The defaults table further down is still accurate and worth keeping. Use `UserJsonContexts` for the AOT example (already done in the doc) and either drop the runtime-policy snippet or describe the policy as a planned extension.
- **SDK fix + doc fix**: add real, settable properties to `JsonSchemaSerializerOptions` and have `BuildOptions` honor them. Then keep the doc as-is.

The cheaper move is the doc-only fix. SDK surface should only grow if there is a real customer ask.

---

## 3. Avro removed from docs (resolved)

**Status**: Done. Avro is not supported, so the page has been removed entirely. Affected files:

- Deleted `docs/client/serialization/formats/avro.md`
- Removed the Avro row from `docs/client/serialization/formats/README.md`
- Dropped `Avro` from the `SchemaFormat` value list in `docs/client/serialization/decoding-records.md`
- Removed Avro from the section index in `docs/client/serialization/README.md`

The `SchemaDataFormat.Avro` enum value still exists in the SDK. Not exposing it through docs is enough for now. If Avro support is ever planned, reintroduce the page then.

---

## 4. Everything else verified clean

For the record, the following all behave as documented:

- `docs/client/serialization/README.md` — quick example, overrides snippet
- `docs/client/serialization/schema-names.md` — all four strategies, all four output formats, every entry in every example table
- `docs/client/serialization/type-mapping.md` — auto-mapping, cold-consumer mapper, DI registration
- `docs/client/serialization/metadata.md` — attach, read, `NestedMetadataDecoder`, client install, DI registration, `ReadStreamRecords` extension
- `docs/client/serialization/decoding-records.md` — eager decode, `SkipDecoding` + `TryDecode`, `Record.As<T>`, every documented property on `Record` and `Record.Schema`
- `docs/client/serialization/schema-evolution.md` — add-field default, rename-type pin, V1/V2 side by side, adopt-new-strategy (after recent rewrite in `ec78ab9`)
- `docs/client/serialization/formats/json.md` — append, AOT `UserJsonContexts`
- `docs/client/serialization/formats/protobuf.md` — append-builder surface

---

## Suggested execution order

1. File the Linear issue for the bytes bug (item 1). It is the only one that ships a broken feature.
2. Fix item 2 (JSON customization snippet) in the docs.
3. Land the bytes SDK fix and bytes integration test under the Linear issue.
4. After the SDK fix lands, the bytes doc page is correct as-written and needs no further changes.
