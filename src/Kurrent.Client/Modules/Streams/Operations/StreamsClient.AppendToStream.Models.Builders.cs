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
    readonly StreamName                  _stream;
    readonly List<AppendRecordBuilder>   _builders          = [];
    readonly List<ConsistencyCheck>      _checks            = [];
    readonly Dictionary<string, object?> _metadataOverrides = [];
    SchemaDataFormat?                    _dataFormatOverride;
    ExpectedStreamState?                 _expectedState;

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

    public AppendToStreamRequestBuilder WithMetadata(string key, object? value = null) {
        _metadataOverrides[key] = value;
        return this;
    }

    public AppendToStreamRequestBuilder WithMetadata<T>(KeyValuePair<string, T> entry) {
        _metadataOverrides[entry.Key] = entry.Value;
        return this;
    }

    public AppendToStreamRequestBuilder WithMetadata<T>(string key, T? value = default) {
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

public static partial class AppendToStreamRequestBuilderExtensions {
    extension(AppendToStreamRequestBuilder builder) {
        public ValueTask<AppendToStreamResponse> ExecuteAsync(StreamsClient client, CancellationToken cancellationToken = default) =>
            client.AppendToStream(builder.Build(), cancellationToken);
    }
}
