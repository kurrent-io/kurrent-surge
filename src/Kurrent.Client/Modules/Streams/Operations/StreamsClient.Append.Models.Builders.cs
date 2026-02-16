namespace Kurrent.Client.Streams;

[PublicAPI]
public class AppendStreamRequestBuilder {
    ExpectedStreamState  _expectedState   = ExpectedStreamState.Any;
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
        var messages = _messageBuilders.Select(x => x.Build());
        var request  = new AppendStreamRequest(_stream, _expectedState, messages);
        return request;
    }
}

[PublicAPI]
public class AppendRecordBuilder {
    AppendRecord _appendRecord = new();

    public AppendRecordBuilder FromRecord(AppendRecord record) {
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
            Metadata   = metadata
        };

        return this;
    }

	public AppendRecordBuilder WithMetadata<T>(string key, T value) =>
		WithMetadata(_appendRecord.Metadata.With(key, value));

    public AppendRecord Build() => _appendRecord.EnsureValid();
}

[PublicAPI]
public class AppendRecordsRequestBuilder {
    readonly List<ConsistencyCheck> _checks  = [];
    readonly List<AppendRecord>     _records = [];

    StreamName _defaultStream = StreamName.None;

    internal AppendRecordsRequestBuilder() { }

    public AppendRecordsRequestBuilder ForStream(StreamName stream) {
        _defaultStream = stream;
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(AppendRecord record) {
        _records.Add(record);
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(Action<AppendRecordBuilder> configure) {
        _records.Add(new AppendRecordBuilder().With(configure).Build());
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(AppendRecordBuilder builder) {
        _records.Add(builder.Build());
        return this;
    }

    public AppendRecordsRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json, Metadata? metadata = null) =>
        WithRecord(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat).WithMetadata(metadata ?? new Metadata()).Build());

    public AppendRecordsRequestBuilder WithRecords(params AppendRecord[] records) {
        _records.AddRange(records);
        return this;
    }

    public AppendRecordsRequestBuilder WithChecks(params ConsistencyCheck[] checks) {
        foreach (var check in checks)
            _checks.Add(check);

        return this;
    }

    public AppendRecordsRequestBuilder WithRevisionCheck(StreamName stream, StreamRevision revision) =>
        WithChecks(new RevisionConsistencyCheck(stream, revision));

    public AppendRecordsRequest Build() {
        var records = _defaultStream != StreamName.None
            ? _records.Select(r => r.Stream == StreamName.None ? r with { Stream = _defaultStream } : r).ToArray()
            : _records.ToArray();

        foreach (var record in records)
            record.EnsureValid();

        return new AppendRecordsRequest(records, _checks.ToArray());
    }
}
