using System.Collections.Immutable;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

[PublicAPI]
public class AppendStreamRequestBuilder {
    ExpectedStreamState       _expectedState   = ExpectedStreamState.Any;
    List<AppendRecordBuilder> _messageBuilders = [];

    string _stream = "";

    public AppendStreamRequestBuilder ForStream(string stream) {
        _stream = stream;
        return this;
    }

    public AppendStreamRequestBuilder ExpectingRevision(StreamRevision expectedRevision) {
        _expectedState = expectedRevision;
        return this;
    }

    public AppendStreamRequestBuilder ExpectingRevision(ExpectedStreamState expectedState) {
        _expectedState = expectedState;
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(AppendRecord record) =>
        WithRecord(builder => builder.FromRecord(record));

    public AppendStreamRequestBuilder WithRecords(IEnumerable<AppendRecord> records) {
        foreach (var record in records)
            WithRecord(record);

        return this;
    }

    public AppendStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> configureBuilder) {
        var messageBuilder = new AppendRecordBuilder();
        configureBuilder(messageBuilder);
        _messageBuilders.Add(messageBuilder);
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(AppendRecordBuilder appendRecordBuilder) {
        _messageBuilders.Add(appendRecordBuilder);
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json, Metadata? metadata = null) =>
        WithRecord(builder => builder.WithValue(value).WithDataFormat(dataFormat).WithMetadata(metadata ?? new Metadata()));

    public int MessageCount => _messageBuilders.Count;

    public AppendStreamRequest Build() {
        var messages = _messageBuilders.Select(x => x.ForStream(_stream).Build());
        var request  = new AppendStreamRequest(_stream, _expectedState, messages);
        return request;
    }
}

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

    public AppendRecordBuilder WithRecordId(Guid recordId) {
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

    public AppendRecord Build() => _appendRecord.EnsureValid();
}

/// <summary>
/// Builds an <see cref="AppendRecordsRequest"/> for appending multiple records
/// across one or more streams in a single operation.
/// </summary>
[PublicAPI]
public class AppendRecordsRequestBuilder {
    readonly List<ConsistencyCheck> _checks  = [];
    readonly List<AppendRecord>     _records = [];

    SchemaDataFormat _dataFormat = SchemaDataFormat.Unspecified;
    StreamName       _stream     = StreamName.None;

    bool _streamFallback;

    internal AppendRecordsRequestBuilder() { }

    /// <summary>
    /// Overrides the stream for all records, regardless of their individual stream assignments.
    /// </summary>
    /// <param name="stream">The stream to assign to all records.</param>
    public AppendRecordsRequestBuilder WithStream(StreamName stream) {
        _stream         = stream;
        _streamFallback = false;
        return this;
    }

    /// <summary>
    /// Sets a fallback stream for records that do not have a stream assigned.
    /// Records with an existing stream assignment are not affected.
    /// </summary>
    /// <param name="stream">The fallback stream for unassigned records.</param>
    public AppendRecordsRequestBuilder WithStreamFallback(StreamName stream) {
        _stream         = stream;
        _streamFallback = true;
        return this;
    }

    /// <summary>
    /// Overrides the data format for all records, regardless of their individual format settings.
    /// </summary>
    /// <param name="dataFormat">The data format to assign to all records.</param>
    public AppendRecordsRequestBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _dataFormat = dataFormat;
        return this;
    }

    /// <summary>
    /// Adds a pre-built record to the request.
    /// </summary>
    /// <param name="record">The record to append.</param>
    public AppendRecordsRequestBuilder WithRecord(AppendRecord record) {
        _records.Add(record);
        return this;
    }

    /// <summary>
    /// Adds a record built from the specified builder.
    /// </summary>
    /// <param name="builder">The builder used to construct the record.</param>
    public AppendRecordsRequestBuilder WithRecord(AppendRecordBuilder builder) => WithRecord(builder.Build());

    /// <summary>
    /// Adds a record configured via the specified action.
    /// </summary>
    /// <param name="configure">An action to configure the record builder.</param>
    public AppendRecordsRequestBuilder WithRecord(Action<AppendRecordBuilder> configure) => WithRecord(new AppendRecordBuilder().With(configure).Build());

    /// <summary>
    /// Adds a record with the specified value and optional data format.
    /// </summary>
    /// <param name="value">The message payload.</param>
    /// <param name="dataFormat">The schema data format. Defaults to <see cref="SchemaDataFormat.Json"/>.</param>
    public AppendRecordsRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) =>
        WithRecord(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat).Build());

    /// <summary>
    /// Adds multiple pre-built records to the request.
    /// </summary>
    /// <param name="records">The records to append.</param>
    public AppendRecordsRequestBuilder WithRecords(params AppendRecord[] records) {
        _records.AddRange(records);
        return this;
    }

    /// <summary>
    /// Adds consistency checks to be evaluated during the append operation.
    /// </summary>
    /// <param name="checks">The consistency checks to include.</param>
    public AppendRecordsRequestBuilder WithChecks(params ConsistencyCheck[] checks) {
        foreach (var check in checks)
            _checks.Add(check);

        return this;
    }

    /// <summary>
    /// Adds a revision consistency check for the specified stream.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <param name="revision">The expected revision of the stream.</param>
    public AppendRecordsRequestBuilder WithRevisionCheck(StreamName stream, ExpectedStreamState revision) =>
        WithChecks(new RevisionConsistencyCheck(stream, revision));

    /// <summary>
    /// Builds the <see cref="AppendRecordsRequest"/>, applying stream and data format
    /// resolution and validating all records.
    /// </summary>
    /// <returns>An immutable append records request.</returns>
    public AppendRecordsRequest Build() {
        if (_records.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateBuilder<AppendRecord>(_records.Count);

        foreach (var record in _records) {
            var stream     = ResolveStream(record);
            var dataFormat = ResolveDataFormat(record);

            var resolved = stream == record.Stream && dataFormat == record.DataFormat
                ? record : record with { Stream = stream, DataFormat = dataFormat };

            resolved.EnsureValid();

            records.Add(resolved);
        }

        return new AppendRecordsRequest(records.MoveToImmutable(), [.._checks]);

        StreamName ResolveStream(AppendRecord record) {
            if (_stream == StreamName.None) return record.Stream;
            if (!_streamFallback || record.Stream == StreamName.None) return _stream;
            return record.Stream;
        }

        SchemaDataFormat ResolveDataFormat(AppendRecord record) =>
            _dataFormat != SchemaDataFormat.Unspecified ? _dataFormat : record.DataFormat;
    }
}
