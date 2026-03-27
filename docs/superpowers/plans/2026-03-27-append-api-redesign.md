# Append API Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the unified append API into two independent paths --- `AppendToStream` (single-stream) and `AppendRecords` (multi-stream) --- with dedicated types, builders, validation, and client methods for each.

**Architecture:** Two independent request/response pairs that share no base type or interface. Both map to the same gRPC `AppendRecords` RPC via separate `ToContract` overloads. Purpose-built collector builders (not `OptionsBuilder`) accumulate records and checks, then produce immutable requests. `AppendRecord` validation drops the contextual `validateStream` flag; stream validation moves to builders and client methods.

**Tech Stack:** C# 13 / .NET 10, TUnit, gRPC (Google.Protobuf), ImmutableArray, FluentDocker

**Spec:** `docs/superpowers/specs/2026-03-27-append-api-redesign.md`

---

## File Structure

### New files

| File | Responsibility |
|---|---|
| `src/.../Operations/StreamsClient.AppendToStream.cs` | `StreamsClient.AppendToStream` method |
| `src/.../Operations/StreamsClient.AppendToStream.Models.cs` | `AppendToStreamRequest`, `AppendToStreamResponse` |
| `src/.../Operations/StreamsClient.AppendToStream.Models.Builders.cs` | `AppendToStreamRequestBuilder` |
| `src/.../Operations/StreamsClient.AppendToStream.Extensions.cs` | `ExecuteAsync` extension for single-stream builder |
| `test/.../Unit/Modules/Streams/AppendToStreamRequestBuilderTests.cs` | Unit tests for single-stream builder |

(`src/.../` = `src/Kurrent.Client/Modules/Streams/`, `test/.../` = `test/Kurrent.Client.Tests/`)

### Modified files

| File | Changes |
|---|---|
| `src/.../Models/AppendRecord.cs` | Remove `validateStream` parameter from `Validate()` / `EnsureValid()` |
| `src/.../Operations/StreamsClient.Append.Models.cs` | Add `AppendRecordsResponse`; remove obsolete types; clean up `AppendRecordsRequest` factory methods |
| `src/.../Operations/StreamsClient.Append.Models.Builders.cs` | Replace `SingleStreamAppendBuilder` + `MultiStreamAppendBuilder` with `AppendRecordsRequestBuilder` |
| `src/.../Operations/StreamsClient.Append.cs` | Update `AppendRecords` to return `AppendRecordsResponse`; add stream validation; remove old overload |
| `src/.../Operations/StreamsClient.Append.Extensions.cs` | Replace old extensions with `ExecuteAsync` for multi-stream builder |
| `src/.../Operations/StreamsClient.Append.Mappers.cs` | Add `ToContract(AppendToStreamRequest)` overload; add two `ToModel` return paths; remove `StreamsClientV2Mapper` |
| `src/.../Operations/StreamsClient.Append.Appender.cs` | Update to use `AppendRecordsRequestBuilder` and `AppendRecordsResponse` |
| `test/.../Unit/Modules/Streams/AppendRecordTests.cs` | Remove stream-validation tests; update `Validate()` calls |
| `test/.../Unit/Modules/Streams/AppendRecordBuilderTests.cs` | Remove stream-validation tests |
| `test/.../Unit/Modules/Streams/SingleStreamAppendBuilderTests.cs` | Delete (replaced) |
| `test/.../Unit/Modules/Streams/MultiStreamAppendBuilderTests.cs` | Rewrite as `AppendRecordsRequestBuilderTests.cs` |
| `test/.../Integration/Streams/Append/AppendTests.cs` | Update to use new API types |

### Conventions

- K&R braces (opening brace same line)
- Fields: `_camelCase`
- `ConfigureAwait(false)` on ALL awaits in library code
- Tests: `[Test]`, `snake_case` method names, `ValueTask` return, `Assert.That(...)` only
- Partial class naming: `StreamsClient.AppendToStream.{suffix}.cs`
- Namespace: `Kurrent.Client.Streams`

### Test commands

```sh
# Unit tests for the builders
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendToStreamRequestBuilderTests/*" \
  --output Detailed --disable-logo

# Unit tests for AppendRecord
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendRecordTests/*" \
  --output Detailed --disable-logo

# All Streams unit tests
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/*/*[Category=Streams]" \
  --output Detailed --disable-logo

# Integration tests
dotnet test -c Release --framework net10.0 --ignore-exit-code 8 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendTests/*" \
  --output Detailed --disable-logo --fail-fast
```

---

## Task 1: Remove `validateStream` from AppendRecord

**Files:**
- Modify: `src/Kurrent.Client/Modules/Streams/Models/AppendRecord.cs:62-89`
- Modify: `test/Kurrent.Client.Tests/Unit/Modules/Streams/AppendRecordTests.cs`
- Modify: `test/Kurrent.Client.Tests/Unit/Modules/Streams/AppendRecordBuilderTests.cs`

- [ ] **Step 1: Update `Validate()` and `EnsureValid()` on `AppendRecord`**

Remove the `bool validateStream = false` parameter and the stream-validation block from `Validate()`.

In `src/Kurrent.Client/Modules/Streams/Models/AppendRecord.cs`, change:

```csharp
public ValidationResult Validate(bool validateStream = false) {
    var problems = new List<ValidationProblem>();

    if (RecordId == Guid.Empty)
        problems.Add(
            ValidationProblem.CreateForField(nameof(RecordId), ErrorMessages.AppendRecordRecordIdRequired, RecordId));

    if (Value is null)
        problems.Add(
            ValidationProblem.CreateForField(nameof(Value), ErrorMessages.AppendRecordValueRequired));

    if (DataFormat == SchemaDataFormat.Unspecified)
        problems.Add(
            ValidationProblem.CreateForField(nameof(DataFormat), ErrorMessages.AppendRecordDataFormatRequired, DataFormat));

    if (Value is not null && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
        problems.Add(
            ValidationProblem.CreateForField(nameof(DataFormat),
                ErrorMessages.AppendRecordByteValueRequiresBytesFormat, DataFormat));

    if (validateStream && Stream == StreamName.Unset)
        problems.Add(
            ValidationProblem.CreateForField(nameof(Stream), ErrorMessages.AppendRecordStreamRequired, Stream));

    return problems.Count > 0
        ? ValidationError.Create(ErrorCode.FromType<AppendRecord>(), [.. problems])
        : Results.Success;
}

public AppendRecord EnsureValid(bool validateStream = false) {
    Validate(validateStream).Unwrap();
    return this;
}
```

To:

```csharp
public ValidationResult Validate() {
    var problems = new List<ValidationProblem>();

    if (RecordId == Guid.Empty)
        problems.Add(
            ValidationProblem.CreateForField(nameof(RecordId), ErrorMessages.AppendRecordRecordIdRequired, RecordId));

    if (Value is null)
        problems.Add(
            ValidationProblem.CreateForField(nameof(Value), ErrorMessages.AppendRecordValueRequired));

    if (DataFormat == SchemaDataFormat.Unspecified)
        problems.Add(
            ValidationProblem.CreateForField(nameof(DataFormat), ErrorMessages.AppendRecordDataFormatRequired, DataFormat));

    if (Value is not null && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
        problems.Add(
            ValidationProblem.CreateForField(nameof(DataFormat),
                ErrorMessages.AppendRecordByteValueRequiresBytesFormat, DataFormat));

    return problems.Count > 0
        ? ValidationError.Create(ErrorCode.FromType<AppendRecord>(), [.. problems])
        : Results.Success;
}

public AppendRecord EnsureValid() {
    Validate().Unwrap();
    return this;
}
```

- [ ] **Step 2: Update unit tests for AppendRecord**

In `AppendRecordTests.cs`, remove the test that checks stream validation (the one calling `Validate(validateStream: true)` or testing `EnsureValid` with stream = `StreamName.Unset`). The test named something like `ensure_valid_throws_when_stream_is_none` should be deleted --- stream validation is no longer the record's responsibility.

Update any remaining tests that pass `validateStream` to `Validate()` or `EnsureValid()` --- just remove the argument.

- [ ] **Step 3: Update AppendRecordBuilderTests**

In `AppendRecordBuilderTests.cs`, remove any test that validates stream checking on `Build()`. The builder's `Build()` calls `EnsureValid()` which no longer checks streams.

- [ ] **Step 4: Build and run unit tests**

```sh
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendRecordTests/*|/*/*/AppendRecordBuilderTests/*" \
  --output Detailed --disable-logo
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```sh
git add -A && git commit -m "refactor: remove validateStream from AppendRecord.Validate()

Stream validation is now the responsibility of request builders
and client methods, not the record itself. AppendRecord.Validate()
checks only record-level invariants: RecordId, Value, DataFormat."
```

---

## Task 2: Create `AppendToStreamRequest` and `AppendToStreamResponse`

**Files:**
- Create: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.Models.cs`

- [ ] **Step 1: Create the models file**

Create `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.Models.cs`:

```csharp
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to append records to a single stream.
/// All records are assigned to the specified stream regardless of their individual Stream property.
/// </summary>
[PublicAPI]
public record AppendToStreamRequest() {
    /// <summary>
    /// Creates a builder for appending records to the specified stream.
    /// </summary>
    public static AppendToStreamRequestBuilder For(StreamName stream) => new(stream);

    [method: SetsRequiredMembers]
    public AppendToStreamRequest(
        StreamName stream,
        ImmutableArray<AppendRecord> records,
        ImmutableArray<ConsistencyCheck> checks
    ) : this() {
        Stream  = stream;
        Records = records;
        Checks  = checks;
    }

    /// <summary>
    /// The target stream for all records.
    /// </summary>
    public required StreamName Stream { get; init; }

    /// <summary>
    /// The records to append. All will be assigned to <see cref="Stream"/>.
    /// </summary>
    public required ImmutableArray<AppendRecord> Records { get; init; }

    /// <summary>
    /// Consistency checks to enforce before appending.
    /// </summary>
    public required ImmutableArray<ConsistencyCheck> Checks { get; init; }
}

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendToStreamResponse(StreamName Stream, Revision Revision, LogPosition Position) {
    public required StreamName Stream { get; init; } = Stream;
    public required Revision Revision { get; init; } = Revision;
    public required LogPosition Position { get; init; } = Position;
}
```

- [ ] **Step 2: Verify it compiles**

```sh
dotnet build src/Kurrent.Client/ -c Release --framework net10.0
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```sh
git add -A && git commit -m "feat: add AppendToStreamRequest and AppendToStreamResponse types

New request/response pair for single-stream appends. AppendToStreamRequest
carries the target stream, records, and consistency checks. The response
returns the stream revision and log position."
```

---

## Task 3: Add `AppendRecordsResponse` and clean up `AppendRecordsRequest`

**Files:**
- Modify: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.Models.cs`

- [ ] **Step 1: Add `AppendRecordsResponse` and update `AppendRecordsRequest`**

In `StreamsClient.Append.Models.cs`:

1. Remove the `ToStream` factory method from `AppendRecordsRequest` (it pointed to `SingleStreamAppendBuilder` which is being replaced). Keep `New()` but point it to the new `AppendRecordsRequestBuilder`.

2. Add `AppendRecordsResponse` to replace `AppendRecordsSuccess`.

3. Mark `AppendRecordsSuccess` as `[Obsolete]` (will be removed in cleanup task).

4. Remove the already-obsolete `AppendStreamRequest`, `AppendStreamSuccess`, and `AppendSessionSuccess`.

The updated `AppendRecordsRequest`:

```csharp
[PublicAPI]
public record AppendRecordsRequest() {
    /// <summary>
    /// Creates a builder for appending records across multiple streams.
    /// </summary>
    public static AppendRecordsRequestBuilder New() => new();

    [method: SetsRequiredMembers]
    public AppendRecordsRequest(
        ImmutableArray<AppendRecord> records, ImmutableArray<ConsistencyCheck> checks
    ) : this() {
        Records = records;
        Checks  = checks;
    }

    public required ImmutableArray<AppendRecord> Records { get; init; }
    public required ImmutableArray<ConsistencyCheck> Checks { get; init; }
}
```

The new `AppendRecordsResponse`:

```csharp
/// <summary>
/// The result of appending records across multiple streams.
/// </summary>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsResponse(StreamRevision[] Revisions, LogPosition Position) {
    public required StreamRevision[] Revisions { get; init; } = Revisions;
    public required LogPosition Position { get; init; } = Position;
}
```

Keep `AppendRecordsSuccess` temporarily with `[Obsolete]` so the codebase still compiles during migration:

```csharp
[Obsolete("Use AppendRecordsResponse instead.")]
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsSuccess(StreamRevision[] Revisions, LogPosition Position) {
    public required StreamRevision[] Revisions { get; init; } = Revisions;
    public required LogPosition Position { get; init; } = Position;
}
```

Remove the three already-obsolete types: `AppendStreamRequest`, `AppendStreamSuccess`, `AppendSessionSuccess`.

- [ ] **Step 2: Verify it compiles**

The build will break because `AppendRecordsRequest.New()` now references `AppendRecordsRequestBuilder` which doesn't exist yet. Temporarily comment out the `New()` method body or add a stub. Alternatively, do this task after Task 5 (builder creation). If doing it now, add a temporary stub:

```csharp
// Temporary stub - replaced in Task 5
public static MultiStreamAppendBuilder New() => new();
```

Keep the old factory method signature until the builder is created.

```sh
dotnet build src/Kurrent.Client/ -c Release --framework net10.0
```

- [ ] **Step 3: Commit**

```sh
git add -A && git commit -m "feat: add AppendRecordsResponse; remove obsolete append types

AppendRecordsResponse replaces AppendRecordsSuccess as the return type
for multi-stream appends. Removed AppendStreamRequest, AppendStreamSuccess,
and AppendSessionSuccess (previously marked obsolete)."
```

---

## Task 4: Create `AppendToStreamRequestBuilder`

**Files:**
- Create: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.Models.Builders.cs`
- Create: `test/Kurrent.Client.Tests/Unit/Modules/Streams/AppendToStreamRequestBuilderTests.cs`

- [ ] **Step 1: Write failing tests for the builder**

Create `test/Kurrent.Client.Tests/Unit/Modules/Streams/AppendToStreamRequestBuilderTests.cs`:

```csharp
using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

[Category("Streams")]
public class AppendToStreamRequestBuilderTests {
    static readonly StreamName TestStream = $"test-stream-{Guid.NewGuid():N}";

    [Test]
    public async ValueTask build_throws_when_no_records_added() {
        var builder = AppendToStreamRequest.For(TestStream);
        var act = () => builder.Build();
        await Assert.That(act).ThrowsException().OfAnyType();
    }

    [Test]
    public async ValueTask build_stamps_stream_on_all_records() {
        var record1 = AppendRecord.New.WithValue("event-1").Build();
        var record2 = AppendRecord.ToStream("wrong-stream").WithValue("event-2").Build();

        var request = AppendToStreamRequest.For(TestStream)
            .WithRecord(record1)
            .WithRecord(record2)
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(2);
        await Assert.That(request.Records[0].Stream).IsEqualTo(TestStream);
        await Assert.That(request.Records[1].Stream).IsEqualTo(TestStream);
    }

    [Test]
    public async ValueTask build_produces_stream_state_check_from_expecting_state() {
        var request = AppendToStreamRequest.For(TestStream)
            .ExpectingState(ExpectedStreamCondition.NoStream)
            .WithRecord(AppendRecord.New.WithValue("event").Build())
            .Build();

        await Assert.That(request.Checks).HasCount().EqualTo(1);

        var check = request.Checks[0];
        await Assert.That(check.IsStreamStateCheck).IsTrue();

        var stateCheck = (StreamStateCheck)check.Value;
        await Assert.That(stateCheck.Stream).IsEqualTo(TestStream);
        await Assert.That(stateCheck.ExpectedState).IsEqualTo((ExpectedStreamState)ExpectedStreamCondition.NoStream);
    }

    [Test]
    public async ValueTask build_includes_additional_checks_alongside_expected_state() {
        var otherStream = (StreamName)$"other-{Guid.NewGuid():N}";

        var request = AppendToStreamRequest.For(TestStream)
            .ExpectingState(ExpectedStreamCondition.NoStream)
            .WithCheck(ConsistencyCheck.Stream(otherStream).Exists())
            .WithRecord(AppendRecord.New.WithValue("event").Build())
            .Build();

        await Assert.That(request.Checks).HasCount().EqualTo(2);
    }

    [Test]
    public async ValueTask build_without_expecting_state_has_no_state_checks() {
        var request = AppendToStreamRequest.For(TestStream)
            .WithRecord(AppendRecord.New.WithValue("event").Build())
            .Build();

        await Assert.That(request.Checks).IsEmpty();
    }

    [Test]
    public async ValueTask with_data_format_overrides_all_records() {
        var request = AppendToStreamRequest.For(TestStream)
            .WithDataFormat(SchemaDataFormat.Protobuf)
            .WithRecord(AppendRecord.New.WithValue("event-1").Build())
            .WithRecord(AppendRecord.New.WithValue("event-2").WithDataFormat(SchemaDataFormat.Json).Build())
            .Build();

        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
        await Assert.That(request.Records[1].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask preserves_record_data_format_when_no_override() {
        var request = AppendToStreamRequest.For(TestStream)
            .WithRecord(AppendRecord.New.WithValue("event").WithDataFormat(SchemaDataFormat.Protobuf).Build())
            .Build();

        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask with_metadata_upserts_on_all_records() {
        var record = AppendRecord.New
            .WithValue("event")
            .WithMetadata("existing-key", "existing-value")
            .Build();

        var request = AppendToStreamRequest.For(TestStream)
            .WithMetadata("batch-key", "batch-value")
            .WithRecord(record)
            .Build();

        await Assert.That(request.Records[0].Metadata.ContainsKey("existing-key")).IsTrue();
        await Assert.That(request.Records[0].Metadata.ContainsKey("batch-key")).IsTrue();
        await Assert.That(request.Records[0].Metadata.GetRequiredValue<string>("batch-key"))
            .IsEqualTo("batch-value");
    }

    [Test]
    public async ValueTask with_metadata_overrides_existing_key() {
        var record = AppendRecord.New
            .WithValue("event")
            .WithMetadata("shared-key", "original")
            .Build();

        var request = AppendToStreamRequest.For(TestStream)
            .WithMetadata("shared-key", "overridden")
            .WithRecord(record)
            .Build();

        await Assert.That(request.Records[0].Metadata.GetRequiredValue<string>("shared-key"))
            .IsEqualTo("overridden");
    }

    [Test]
    public async ValueTask with_record_action_overload() {
        var request = AppendToStreamRequest.For(TestStream)
            .WithRecord(b => b.WithValue("event"))
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("event");
    }

    [Test]
    public async ValueTask with_record_value_overload() {
        var request = AppendToStreamRequest.For(TestStream)
            .WithRecord("event")
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("event");
    }

    [Test]
    public async ValueTask with_records_params_overload() {
        var records = new[] {
            AppendRecord.New.WithValue("event-1").Build(),
            AppendRecord.New.WithValue("event-2").Build()
        };

        var request = AppendToStreamRequest.For(TestStream)
            .WithRecords(records)
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(2);
    }

    [Test]
    public async ValueTask with_records_enumerable_overload() {
        IEnumerable<AppendRecord> records = [
            AppendRecord.New.WithValue("event-1").Build(),
            AppendRecord.New.WithValue("event-2").Build()
        ];

        var request = AppendToStreamRequest.For(TestStream)
            .WithRecords(records)
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(2);
    }

    [Test]
    public async ValueTask request_stream_matches_builder_stream() {
        var request = AppendToStreamRequest.For(TestStream)
            .WithRecord(AppendRecord.New.WithValue("event").Build())
            .Build();

        await Assert.That(request.Stream).IsEqualTo(TestStream);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```sh
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendToStreamRequestBuilderTests/*" \
  --output Detailed --disable-logo
```

Expected: compilation errors because `AppendToStreamRequestBuilder` doesn't exist.

- [ ] **Step 3: Implement `AppendToStreamRequestBuilder`**

Create `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.Models.Builders.cs`:

```csharp
using System.Collections.Immutable;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

/// <summary>
/// Builds an <see cref="AppendToStreamRequest"/> where all records target a single stream.
/// Created via <see cref="AppendToStreamRequest.For"/>.
/// </summary>
[PublicAPI]
public class AppendToStreamRequestBuilder {
    readonly StreamName                              _stream;
    readonly List<AppendRecordBuilder>               _builders = [];
    readonly List<ConsistencyCheck>                  _checks   = [];
    readonly Dictionary<string, object>              _metadataOverrides = [];
    SchemaDataFormat?                                _dataFormatOverride;
    ExpectedStreamState?                             _expectedState;

    internal AppendToStreamRequestBuilder(StreamName stream) => _stream = stream;

    public int RecordsCount => _builders.Count;

    public AppendToStreamRequestBuilder ExpectingState(ExpectedStreamState expectedState) {
        _expectedState = expectedState;
        return this;
    }

    #region Consistency Checks

    public AppendToStreamRequestBuilder WithCheck(ConsistencyCheck check) {
        _checks.Add(check);
        return this;
    }

    public AppendToStreamRequestBuilder WithChecks(params ConsistencyCheck[] checks) {
        foreach (var check in checks)
            _checks.Add(check);
        return this;
    }

    #endregion

    #region Records

    public AppendToStreamRequestBuilder WithRecord(AppendRecord record) {
        _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    public AppendToStreamRequestBuilder WithRecord(AppendRecordBuilder builder) {
        _builders.Add(builder);
        return this;
    }

    public AppendToStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> configure) {
        var builder = new AppendRecordBuilder();
        configure(builder);
        _builders.Add(builder);
        return this;
    }

    public AppendToStreamRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
        _builders.Add(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    public AppendToStreamRequestBuilder WithRecords(params AppendRecord[] records) {
        foreach (var record in records)
            _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    public AppendToStreamRequestBuilder WithRecords(IEnumerable<AppendRecord> records) {
        foreach (var record in records)
            _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    #endregion

    #region Batch Overrides

    public AppendToStreamRequestBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _dataFormatOverride = dataFormat;
        return this;
    }

    public AppendToStreamRequestBuilder WithMetadata(string key, object value) {
        _metadataOverrides[key] = value;
        return this;
    }

    #endregion

    public AppendToStreamRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendToStreamRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateBuilder<AppendRecord>(_builders.Count);

        foreach (var builder in _builders) {
            builder.ForStream(_stream);

            if (_dataFormatOverride.HasValue)
                builder.WithDataFormat(_dataFormatOverride.Value);

            foreach (var (key, value) in _metadataOverrides)
                builder.WithMetadata(key, value);

            records.Add(builder.Build());
        }

        var checks = ImmutableArray.CreateBuilder<ConsistencyCheck>();

        if (_expectedState.HasValue)
            checks.Add(new StreamStateCheck(_stream, _expectedState.Value));

        checks.AddRange(_checks);

        return new AppendToStreamRequest(_stream, records.MoveToImmutable(), checks.ToImmutable());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```sh
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendToStreamRequestBuilderTests/*" \
  --output Detailed --disable-logo
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```sh
git add -A && git commit -m "feat: add AppendToStreamRequestBuilder with unit tests

Purpose-built collector for single-stream append requests. Stamps
target stream on all records at build time. Converts ExpectingState
to a StreamStateCheck. Supports batch-level WithDataFormat and
WithMetadata overrides."
```

---

## Task 5: Create `AppendRecordsRequestBuilder`

**Files:**
- Modify: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.Models.Builders.cs`
- Delete: `test/Kurrent.Client.Tests/Unit/Modules/Streams/SingleStreamAppendBuilderTests.cs`
- Rename/rewrite: `test/Kurrent.Client.Tests/Unit/Modules/Streams/MultiStreamAppendBuilderTests.cs` to `AppendRecordsRequestBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Create (or rewrite) `test/Kurrent.Client.Tests/Unit/Modules/Streams/AppendRecordsRequestBuilderTests.cs`:

```csharp
using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

[Category("Streams")]
public class AppendRecordsRequestBuilderTests {
    [Test]
    public async ValueTask build_throws_when_no_records_added() {
        var builder = AppendRecordsRequest.New();
        var act = () => builder.Build();
        await Assert.That(act).ThrowsException().OfAnyType();
    }

    [Test]
    public async ValueTask build_validates_all_records_have_streams() {
        var builder = AppendRecordsRequest.New()
            .WithRecord(AppendRecord.New.WithValue("event").Build());

        var act = () => builder.Build();
        await Assert.That(act).ThrowsException().OfAnyType();
    }

    [Test]
    public async ValueTask build_succeeds_when_all_records_have_streams() {
        var request = AppendRecordsRequest.New()
            .WithRecord(AppendRecord.ToStream("stream-a").WithValue("event-1").Build())
            .WithRecord(AppendRecord.ToStream("stream-b").WithValue("event-2").Build())
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(2);
    }

    [Test]
    public async ValueTask with_check_adds_consistency_check() {
        var check = ConsistencyCheck.Stream("some-stream").Exists();

        var request = AppendRecordsRequest.New()
            .WithCheck(check)
            .WithRecord(AppendRecord.ToStream("stream-a").WithValue("event").Build())
            .Build();

        await Assert.That(request.Checks).HasCount().EqualTo(1);
    }

    [Test]
    public async ValueTask with_checks_adds_multiple_consistency_checks() {
        var request = AppendRecordsRequest.New()
            .WithChecks(
                ConsistencyCheck.Stream("stream-a").Exists(),
                ConsistencyCheck.Stream("stream-b").DoesNotExist()
            )
            .WithRecord(AppendRecord.ToStream("stream-a").WithValue("event").Build())
            .Build();

        await Assert.That(request.Checks).HasCount().EqualTo(2);
    }

    [Test]
    public async ValueTask with_data_format_overrides_all_records() {
        var request = AppendRecordsRequest.New()
            .WithDataFormat(SchemaDataFormat.Protobuf)
            .WithRecord(AppendRecord.ToStream("stream-a").WithValue("event-1").Build())
            .WithRecord(AppendRecord.ToStream("stream-b").WithValue("event-2").WithDataFormat(SchemaDataFormat.Json).Build())
            .Build();

        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
        await Assert.That(request.Records[1].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask with_metadata_upserts_on_all_records() {
        var record = AppendRecord.ToStream("stream-a")
            .WithValue("event")
            .WithMetadata("existing-key", "existing-value")
            .Build();

        var request = AppendRecordsRequest.New()
            .WithMetadata("batch-key", "batch-value")
            .WithRecord(record)
            .Build();

        await Assert.That(request.Records[0].Metadata.ContainsKey("existing-key")).IsTrue();
        await Assert.That(request.Records[0].Metadata.ContainsKey("batch-key")).IsTrue();
    }

    [Test]
    public async ValueTask with_record_action_overload() {
        var request = AppendRecordsRequest.New()
            .WithRecord(b => b.ForStream("stream-a").WithValue("event"))
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(1);
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"stream-a");
    }

    [Test]
    public async ValueTask with_records_params_overload() {
        var records = new[] {
            AppendRecord.ToStream("stream-a").WithValue("event-1").Build(),
            AppendRecord.ToStream("stream-b").WithValue("event-2").Build()
        };

        var request = AppendRecordsRequest.New()
            .WithRecords(records)
            .Build();

        await Assert.That(request.Records).HasCount().EqualTo(2);
    }

    [Test]
    public async ValueTask records_count_tracks_added_records() {
        var builder = AppendRecordsRequest.New()
            .WithRecord(AppendRecord.ToStream("stream-a").WithValue("event-1").Build())
            .WithRecord(AppendRecord.ToStream("stream-b").WithValue("event-2").Build());

        await Assert.That(builder.RecordsCount).IsEqualTo(2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```sh
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendRecordsRequestBuilderTests/*" \
  --output Detailed --disable-logo
```

Expected: compilation errors because `AppendRecordsRequestBuilder` doesn't exist yet.

- [ ] **Step 3: Implement `AppendRecordsRequestBuilder`**

Replace the contents of `StreamsClient.Append.Models.Builders.cs`. Keep `AppendRecordBuilder` unchanged. Remove `SingleStreamAppendBuilder` and `MultiStreamAppendBuilder`. Add `AppendRecordsRequestBuilder`:

```csharp
using System.Collections.Immutable;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

[PublicAPI]
public class AppendRecordBuilder {
    // ... unchanged from current implementation ...
}

/// <summary>
/// Builds an <see cref="AppendRecordsRequest"/> where each record carries its own stream assignment.
/// Created via <see cref="AppendRecordsRequest.New"/>.
/// </summary>
[PublicAPI]
public class AppendRecordsRequestBuilder {
    readonly List<AppendRecordBuilder>  _builders = [];
    readonly List<ConsistencyCheck>     _checks   = [];
    readonly Dictionary<string, object> _metadataOverrides = [];
    SchemaDataFormat?                   _dataFormatOverride;

    internal AppendRecordsRequestBuilder() { }

    public int RecordsCount => _builders.Count;

    #region Consistency Checks

    public AppendRecordsRequestBuilder WithCheck(ConsistencyCheck check) {
        _checks.Add(check);
        return this;
    }

    public AppendRecordsRequestBuilder WithChecks(params ConsistencyCheck[] checks) {
        foreach (var check in checks)
            _checks.Add(check);
        return this;
    }

    #endregion

    #region Records

    public AppendRecordsRequestBuilder WithRecord(AppendRecord record) {
        _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(AppendRecordBuilder builder) {
        _builders.Add(builder);
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(Action<AppendRecordBuilder> configure) {
        var builder = new AppendRecordBuilder();
        configure(builder);
        _builders.Add(builder);
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
        _builders.Add(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    public AppendRecordsRequestBuilder WithRecords(params AppendRecord[] records) {
        foreach (var record in records)
            _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    #endregion

    #region Batch Overrides

    public AppendRecordsRequestBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _dataFormatOverride = dataFormat;
        return this;
    }

    public AppendRecordsRequestBuilder WithMetadata(string key, object value) {
        _metadataOverrides[key] = value;
        return this;
    }

    #endregion

    public AppendRecordsRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateBuilder<AppendRecord>(_builders.Count);

        foreach (var builder in _builders) {
            if (_dataFormatOverride.HasValue)
                builder.WithDataFormat(_dataFormatOverride.Value);

            foreach (var (key, value) in _metadataOverrides)
                builder.WithMetadata(key, value);

            var record = builder.Build();

            if (record.Stream == StreamName.Unset)
                throw ValidationError.FromProblem(
                    ErrorCode.FromType<AppendRecordsRequest>(),
                    ErrorMessages.AppendRecordStreamRequired
                ).GetOrCreateException();

            records.Add(record);
        }

        return new AppendRecordsRequest(records.MoveToImmutable(), [.._checks]);
    }
}
```

Also update `AppendRecordsRequest.New()` in `StreamsClient.Append.Models.cs` to return `AppendRecordsRequestBuilder`:

```csharp
public static AppendRecordsRequestBuilder New() => new();
```

- [ ] **Step 4: Delete old test files**

Delete `test/Kurrent.Client.Tests/Unit/Modules/Streams/SingleStreamAppendBuilderTests.cs` and
`test/Kurrent.Client.Tests/Unit/Modules/Streams/MultiStreamAppendBuilderTests.cs`.

- [ ] **Step 5: Run tests**

```sh
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendRecordsRequestBuilderTests/*|/*/*/AppendToStreamRequestBuilderTests/*" \
  --output Detailed --disable-logo
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```sh
git add -A && git commit -m "feat: add AppendRecordsRequestBuilder; remove old builders

Replaces SingleStreamAppendBuilder and MultiStreamAppendBuilder.
Validates every record has Stream != Unset at build time.
Supports batch-level WithDataFormat and WithMetadata overrides."
```

---

## Task 6: Update mapper for both paths

**Files:**
- Modify: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.Mappers.cs`

- [ ] **Step 1: Add `ToContract(AppendToStreamRequest)` overload and response mapping**

In `StreamsClient.Append.Mappers.cs`, add alongside existing methods:

```csharp
public async ValueTask<Contracts.AppendRecordsRequest> ToContract(AppendToStreamRequest source, CancellationToken ct) {
    var result = new Contracts.AppendRecordsRequest {
        Records = { Capacity = source.Records.Length }
    };

    foreach (var record in source.Records) {
        // Belt-and-suspenders: stamp stream onto record before mapping
        var stamped = record with { Stream = source.Stream };
        result.Records.Add(await ToContract(stamped, ct).ConfigureAwait(false));
    }

    foreach (var check in source.Checks)
        if (check != ConsistencyCheck.None)
            result.Checks.Add(ToContract(check));

    return result;
}

public AppendToStreamResponse ToAppendToStreamResponse(Contracts.AppendRecordsResponse source, StreamName stream) {
    var revision = source.Revisions
        .Where(r => (StreamName)r.Stream == stream)
        .Select(r => (Revision)r.Revision)
        .LastOrDefault(Revision.Unset);

    return new AppendToStreamResponse(stream, revision, source.Position);
}

public AppendRecordsResponse ToAppendRecordsResponse(Contracts.AppendRecordsResponse source) {
    var revisions = source.Revisions
        .Select(x => new StreamRevision(x.Stream, x.Revision))
        .ToArray();

    return new AppendRecordsResponse(revisions, source.Position);
}
```

Remove `StreamsClientV2Mapper` (the old static mapper class at the bottom of the file). It references the deleted obsolete types.

- [ ] **Step 2: Verify it compiles**

```sh
dotnet build src/Kurrent.Client/ -c Release --framework net10.0
```

- [ ] **Step 3: Commit**

```sh
git add -A && git commit -m "feat: add mapper overloads for split append paths

ToContract(AppendToStreamRequest) stamps the target stream on all
records before mapping. Two ToModel paths: ToAppendToStreamResponse
extracts the single stream's revision, ToAppendRecordsResponse
returns the full revision array. Removed obsolete StreamsClientV2Mapper."
```

---

## Task 7: Add `AppendToStream` client method

**Files:**
- Create: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.cs`

- [ ] **Step 1: Implement the client method**

Create `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.cs`:

```csharp
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            // Belt-and-suspenders: stamp stream on all records
            request = request with {
                Records = [..request.Records.Select(r => r with { Stream = request.Stream })],
                Checks = [
                    ..request.Checks
                        .Where(c => c.IsStreamStateCheck)
                        .Where(c => c.Value is StreamStateCheck check && check.ExpectedState != ExpectedStreamState.Any)
                ]
            };

            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var response = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToAppendToStreamResponse(response, request.Stream);
        }
        catch (AppendConsistencyViolationException ex) when (ex.Violations is [{ Value: StreamStateViolation v }]) {
            throw v.ActualState.Match<Exception>(
                condition => condition switch {
                    ActualStreamCondition.Tombstoned => new StreamTombstonedException(v.Stream),
                    ActualStreamCondition.NoStream   => new StreamNotFoundException(v.Stream),
                    _                                => ex
                },
                _ => ex);
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

```sh
dotnet build src/Kurrent.Client/ -c Release --framework net10.0
```

- [ ] **Step 3: Commit**

```sh
git add -A && git commit -m "feat: add StreamsClient.AppendToStream method

Stamps request.Stream onto all records before sending. Unwraps
single-stream consistency violations into specific exceptions
(StreamTombstonedException, StreamNotFoundException)."
```

---

## Task 8: Update `AppendRecords` client method

**Files:**
- Modify: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.cs`

- [ ] **Step 1: Update `AppendRecords` to return `AppendRecordsResponse` and add stream validation**

Replace the contents of `StreamsClient.Append.cs`:

```csharp
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        // Belt-and-suspenders: validate every record has a stream
        var missingStream = request.Records.FirstOrDefault(r => r.Stream == StreamName.Unset);
        if (missingStream is not null)
            throw new InvalidOperationException(
                $"Record '{missingStream.RecordId}' has no stream assigned. Set the stream on each record or use AppendToStream for single-stream appends.");

        try {
            request = request with {
                Checks = [
                    ..request.Checks
                        .Where(c => c.IsStreamStateCheck)
                        .Where(c => c.Value is StreamStateCheck check && check.ExpectedState != ExpectedStreamState.Any)
                ]
            };

            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var response = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToAppendRecordsResponse(response);
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
```

The old convenience overload `AppendRecords(StreamName, ExpectedStreamState, IEnumerable<AppendRecord>, CancellationToken)` is removed --- users should use `AppendToStream` instead.

- [ ] **Step 2: Update `StreamsAppender`**

In `StreamsClient.Append.Appender.cs`, update return types from `AppendRecordsSuccess` to `AppendRecordsResponse` and `MultiStreamAppendBuilder` to `AppendRecordsRequestBuilder`:

```csharp
public AppendRecordsRequestBuilder NewRequestBuilder() => new();

public async ValueTask<AppendRecordsResponse> AppendAsync(AppendRecordsRequest request, CancellationToken ct) {
    // ... same validation and dispatch logic, but call Mapper.ToAppendRecordsResponse(response) ...
}

public async ValueTask<AppendRecordsResponse> AppendAsync(Action<AppendRecordsRequestBuilder> configure, CancellationToken ct) {
    var builder = NewRequestBuilder();
    configure(builder);
    return await AppendAsync(builder.Build(), ct).ConfigureAwait(false);
}
```

- [ ] **Step 3: Verify it compiles**

```sh
dotnet build src/Kurrent.Client/ -c Release --framework net10.0
```

- [ ] **Step 4: Commit**

```sh
git add -A && git commit -m "refactor: update AppendRecords to return AppendRecordsResponse

Adds belt-and-suspenders stream validation. Returns AppendRecordsResponse
instead of AppendRecordsSuccess. Removes old convenience overload
(replaced by AppendToStream). Updates StreamsAppender."
```

---

## Task 9: Add `ExecuteAsync` extensions

**Files:**
- Create: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.AppendToStream.Extensions.cs`
- Modify: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.Extensions.cs`

- [ ] **Step 1: Create single-stream `ExecuteAsync` extension**

Create `StreamsClient.AppendToStream.Extensions.cs`:

```csharp
namespace Kurrent.Client.Streams;

[PublicAPI]
public static partial class StreamsClientExtensions {
    extension(AppendToStreamRequestBuilder builder) {
        public async ValueTask<AppendToStreamResponse> ExecuteAsync(
            StreamsClient client,
            CancellationToken cancellationToken = default
        ) => await client.AppendToStream(builder.Build(), cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Replace multi-stream extensions**

Replace the contents of `StreamsClient.Append.Extensions.cs`:

```csharp
namespace Kurrent.Client.Streams;

[PublicAPI]
public static partial class StreamsClientExtensions {
    extension(AppendRecordsRequestBuilder builder) {
        public async ValueTask<AppendRecordsResponse> ExecuteAsync(
            StreamsClient client,
            CancellationToken cancellationToken = default
        ) => await client.AppendRecords(builder.Build(), cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Verify it compiles**

```sh
dotnet build src/Kurrent.Client/ -c Release --framework net10.0
```

- [ ] **Step 4: Commit**

```sh
git add -A && git commit -m "feat: add ExecuteAsync extensions for both builders

Build-and-dispatch in one call. Extension methods on the builder
types keep them decoupled from StreamsClient."
```

---

## Task 10: Update integration tests

**Files:**
- Modify: `test/Kurrent.Client.Tests/Integration/Streams/Append/AppendTests.cs`
- Modify: `test/Kurrent.Client.Tests/Integration/Streams/Append/Consistency/AppendConsistencyTestBase.cs`

- [ ] **Step 1: Update `AppendTests.cs`**

Replace all calls to `Fixture.Streams.AppendRecords(stream, expectedState, records)` with the new builder API:

```csharp
// Old:
var result = await Fixture.Streams.AppendRecords(simulation.Stream, ExpectedStreamState.Any, simulation.Records);

// New:
var result = await Fixture.Streams.AppendToStream(
    AppendToStreamRequest.For(simulation.Stream)
        .ExpectingState(ExpectedStreamState.Any)
        .WithRecords(simulation.Records)
        .Build()
);
```

Update all assertion types from `AppendRecordsSuccess` to `AppendToStreamResponse`. The response now has `.Revision` (single) instead of `.Revisions` (array).

For the multi-stream test `creates_multiple_streams_transactionally`, use the `AppendRecords` path:

```csharp
var result = await Fixture.Streams.AppendRecords(
    AppendRecordsRequest.New()
        .WithRecord(AppendRecord.ToStream(streamA).WithValue(eventA).Build())
        .WithRecord(AppendRecord.ToStream(streamB).WithValue(eventB).Build())
        .Build()
);
```

- [ ] **Step 2: Update `AppendConsistencyTestBase.cs`**

Update `BuildAppendRequest` and other helpers to use the new API types.

- [ ] **Step 3: Run unit tests**

```sh
dotnet test -c Release --framework net10.0 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/*/*[Category=Streams]" \
  --output Detailed --disable-logo
```

Expected: all unit tests pass.

- [ ] **Step 4: Run integration tests**

```sh
dotnet test -c Release --framework net10.0 --ignore-exit-code 8 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/AppendTests/*" \
  --output Detailed --disable-logo --fail-fast
```

Expected: all integration tests pass.

- [ ] **Step 5: Commit**

```sh
git add -A && git commit -m "test: update append tests for split API

Integration tests use AppendToStream for single-stream and
AppendRecords for multi-stream. Assertions updated for new
response types (Revision instead of Revisions array)."
```

---

## Task 11: Final cleanup

**Files:**
- Modify: `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.Models.cs`
- Delete: `test/Kurrent.Client.Tests/Integration/Streams/Append/AppendLoadTests.cs` (already dead code)

- [ ] **Step 1: Remove `AppendRecordsSuccess`**

In `StreamsClient.Append.Models.cs`, delete the `AppendRecordsSuccess` record entirely (it was marked `[Obsolete]` in Task 3).

- [ ] **Step 2: Full build and test**

```sh
dotnet build -c Release --framework net10.0
```

```sh
dotnet test -c Release --framework net10.0 --ignore-exit-code 8 --results-directory ./artifacts/test-results \
  -- --treenode-filter "/*/*/*/*[Category=Streams]" \
  --output Detailed --disable-logo
```

Expected: clean build, all tests pass.

- [ ] **Step 3: Review git diff for unintended changes**

```sh
git diff --stat HEAD~10
```

Verify only the expected files are touched.

- [ ] **Step 4: Commit**

```sh
git add -A && git commit -m "chore: remove AppendRecordsSuccess and dead test code

Final cleanup after append API split. All callers migrated to
AppendToStreamResponse and AppendRecordsResponse."
```
