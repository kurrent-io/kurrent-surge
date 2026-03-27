using System.Collections.Immutable;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

[PublicAPI]
public class AppendRecordBuilder {
    AppendRecord _appendRecord = new();

    internal AppendRecordBuilder FromRecord(AppendRecord record) {
        _appendRecord = record;
        return this;
    }

    public AppendRecordBuilder ForStream(StreamName stream) {
        _appendRecord = _appendRecord with { Stream = stream };
        return this;
    }

    public AppendRecordBuilder WithId(Guid recordId) {
        _appendRecord = _appendRecord with { RecordId = recordId };
        return this;
    }

    public AppendRecordBuilder WithValue(object recordValue) {
        _appendRecord = _appendRecord with { Value = recordValue };
        return this;
    }

    public AppendRecordBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _appendRecord = _appendRecord with { DataFormat = dataFormat };
        return this;
    }

    public AppendRecordBuilder WithMetadata(Metadata metadata) {
        _appendRecord = _appendRecord with {
            Metadata = metadata
        };

        return this;
    }

    public AppendRecordBuilder WithMetadata<T>(string key, T value) =>
        WithMetadata(_appendRecord.Metadata.With(key, value));

    public AppendRecordBuilder WithTimestamp(DateTimeOffset timestamp) {
        _appendRecord = _appendRecord with { Timestamp = timestamp };
        return this;
    }

    public AppendRecordBuilder WithTimestamp(long timestamp) =>
        WithTimestamp(DateTimeOffset.FromUnixTimeMilliseconds(timestamp));

    public AppendRecord Build() => _appendRecord.EnsureValid();
}

/// <summary>
/// Builds an <see cref="AppendRecordsRequest"/> where all records target a single stream.
/// Created via <see cref="AppendRecordsRequest.ToStream"/>.
/// </summary>
[PublicAPI]
public class SingleStreamAppendBuilder {
    readonly StreamName                _stream;
    readonly List<AppendRecordBuilder> _builders = [];

    ExpectedStreamState? _expectedState;
    SchemaDataFormat     _dataFormat = SchemaDataFormat.Unspecified;

    internal SingleStreamAppendBuilder(StreamName stream) => _stream = stream;

    public int RecordsCount => _builders.Count;

    /// <summary>
    /// Sets the expected stream state for a consistency check on this stream.
    /// </summary>
    /// <param name="expectedState">The expected state of the stream.</param>
    public SingleStreamAppendBuilder ExpectingState(ExpectedStreamState expectedState) {
        _expectedState = expectedState;
        return this;
    }

    /// <summary>
    /// Overrides the data format for all records.
    /// </summary>
    /// <param name="dataFormat">The data format to assign to all records.</param>
    public SingleStreamAppendBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _dataFormat = dataFormat;
        return this;
    }

    /// <summary>
    /// Adds a pre-built record to the request.
    /// </summary>
    /// <param name="record">The record to append.</param>
    public SingleStreamAppendBuilder WithRecord(AppendRecord record) {
        _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    /// <summary>
    /// Adds a record built from the specified builder.
    /// </summary>
    /// <param name="builder">The builder used to construct the record.</param>
    public SingleStreamAppendBuilder WithRecord(AppendRecordBuilder builder) {
        _builders.Add(builder);
        return this;
    }

    /// <summary>
    /// Adds a record configured via the specified action.
    /// </summary>
    /// <param name="configure">An action to configure the record builder.</param>
    public SingleStreamAppendBuilder WithRecord(Action<AppendRecordBuilder> configure) {
        var builder = new AppendRecordBuilder();
        configure(builder);
        _builders.Add(builder);
        return this;
    }

    /// <summary>
    /// Adds a record with the specified value and optional data format.
    /// </summary>
    /// <param name="value">The message payload.</param>
    /// <param name="dataFormat">The schema data format. Defaults to <see cref="SchemaDataFormat.Json"/>.</param>
    public SingleStreamAppendBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
        _builders.Add(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    /// <summary>
    /// Adds multiple pre-built records to the request.
    /// </summary>
    /// <param name="records">The records to append.</param>
    public SingleStreamAppendBuilder WithRecords(params AppendRecord[] records) {
        foreach (var record in records)
            _builders.Add(new AppendRecordBuilder().FromRecord(record));

        return this;
    }

    /// <summary>
    /// Adds multiple pre-built records to the request.
    /// </summary>
    /// <param name="records">The records to append.</param>
    public SingleStreamAppendBuilder WithRecords(IEnumerable<AppendRecord> records) {
        foreach (var record in records)
            _builders.Add(new AppendRecordBuilder().FromRecord(record));

        return this;
    }

    /// <summary>
    /// Builds the <see cref="AppendRecordsRequest"/>, forcing the stream on all records
    /// and emitting a consistency check if <see cref="ExpectingState"/> was called.
    /// </summary>
    /// <returns>An immutable append records request.</returns>
    public AppendRecordsRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateBuilder<AppendRecord>(_builders.Count);

        foreach (var builder in _builders) {
            builder.ForStream(_stream);

            if (_dataFormat != SchemaDataFormat.Unspecified)
                builder.WithDataFormat(_dataFormat);

            records.Add(builder.Build());
        }

        ImmutableArray<ConsistencyCheck> checks = _expectedState.HasValue
            ? [new StreamStateCheck(_stream, _expectedState.Value)]
            : [];

        return new AppendRecordsRequest(records.MoveToImmutable(), checks);
    }
}

/// <summary>
/// Builds an <see cref="AppendRecordsRequest"/> where records carry their own stream assignments
/// and zero or more explicit consistency checks can be added.
/// Created via <see cref="AppendRecordsRequest.New"/>.
/// </summary>
[PublicAPI]
public class MultiStreamAppendBuilder {
    readonly List<ConsistencyCheck>    _checks   = [];
    readonly List<AppendRecordBuilder> _builders = [];

    SchemaDataFormat _dataFormat = SchemaDataFormat.Unspecified;

    internal MultiStreamAppendBuilder() { }

    public int RecordsCount => _builders.Count;

    /// <summary>
    /// Overrides the data format for all records.
    /// </summary>
    /// <param name="dataFormat">The data format to assign to all records.</param>
    public MultiStreamAppendBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _dataFormat = dataFormat;
        return this;
    }

    /// <summary>
    /// Adds a consistency check to be evaluated during the append operation.
    /// </summary>
    /// <param name="check">The consistency check to include.</param>
    public MultiStreamAppendBuilder WithCheck(ConsistencyCheck check) {
        _checks.Add(check);
        return this;
    }

    /// <summary>
    /// Adds consistency checks to be evaluated during the append operation.
    /// </summary>
    /// <param name="checks">The consistency checks to include.</param>
    public MultiStreamAppendBuilder WithChecks(params ConsistencyCheck[] checks) {
        foreach (var check in checks)
            _checks.Add(check);

        return this;
    }

    /// <summary>
    /// Adds a pre-built record to the request.
    /// </summary>
    /// <param name="record">The record to append.</param>
    public MultiStreamAppendBuilder WithRecord(AppendRecord record) {
        _builders.Add(new AppendRecordBuilder().FromRecord(record));
        return this;
    }

    /// <summary>
    /// Adds a record built from the specified builder.
    /// </summary>
    /// <param name="builder">The builder used to construct the record.</param>
    public MultiStreamAppendBuilder WithRecord(AppendRecordBuilder builder) {
        _builders.Add(builder);
        return this;
    }

    /// <summary>
    /// Adds a record configured via the specified action.
    /// </summary>
    /// <param name="configure">An action to configure the record builder.</param>
    public MultiStreamAppendBuilder WithRecord(Action<AppendRecordBuilder> configure) {
        var builder = new AppendRecordBuilder();
        configure(builder);
        _builders.Add(builder);
        return this;
    }

    /// <summary>
    /// Adds a record with the specified value and optional data format.
    /// </summary>
    /// <param name="value">The message payload.</param>
    /// <param name="dataFormat">The schema data format. Defaults to <see cref="SchemaDataFormat.Json"/>.</param>
    public MultiStreamAppendBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
        _builders.Add(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    /// <summary>
    /// Adds multiple pre-built records to the request.
    /// </summary>
    /// <param name="records">The records to append.</param>
    public MultiStreamAppendBuilder WithRecords(params AppendRecord[] records) {
        foreach (var record in records)
            _builders.Add(new AppendRecordBuilder().FromRecord(record));

        return this;
    }

    /// <summary>
    /// Builds the <see cref="AppendRecordsRequest"/>, applying data format resolution
    /// and validating all records.
    /// </summary>
    /// <returns>An immutable append records request.</returns>
    public AppendRecordsRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateBuilder<AppendRecord>(_builders.Count);

        foreach (var builder in _builders) {
            if (_dataFormat != SchemaDataFormat.Unspecified)
                builder.WithDataFormat(_dataFormat);

            records.Add(builder.Build());
        }

        return new AppendRecordsRequest(records.MoveToImmutable(), [.._checks]);
    }
}
