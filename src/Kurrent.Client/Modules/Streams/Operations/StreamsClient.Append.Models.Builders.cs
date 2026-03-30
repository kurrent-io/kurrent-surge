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
/// Builds an <see cref="AppendRecordsRequest"/> where each record carries its own stream assignment.
/// Created via <see cref="AppendRecordsRequest.New"/>.
/// </summary>
[PublicAPI]
public class AppendRecordsRequestBuilder {
    readonly List<AppendRecordBuilder>  _builders          = [];
    readonly List<ConsistencyCheck>     _checks            = [];
    readonly Dictionary<string, object> _metadataOverrides = [];

    SchemaDataFormat? _dataFormatOverride;

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

    public AppendRecordsRequestBuilder WithRecords(IEnumerable<AppendRecord> records) {
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
