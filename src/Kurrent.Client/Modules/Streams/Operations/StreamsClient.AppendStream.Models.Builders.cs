using System.Collections.Immutable;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

/// <summary>
/// Builds an <see cref="AppendStreamRequest"/> where all records target a single stream.
/// Created via <see cref="AppendStreamRequest.For"/>.
/// </summary>
[PublicAPI]
public class AppendStreamRequestBuilder {
    readonly StreamName                  _stream;
    readonly List<AppendRecordBuilder>   _builders          = [];
    // readonly List<ConsistencyCheck>      _checks            = [];
    // readonly Dictionary<string, object?> _metadataOverrides = [];

    // SchemaDataFormat?    _dataFormatOverride;
    ExpectedStreamState _expectedState = ExpectedStreamState.Any;

    internal AppendStreamRequestBuilder(StreamName stream) => _stream = stream;

    public int RecordsCount => _builders.Count;

    public AppendStreamRequestBuilder ExpectingState(ExpectedStreamState expectedState) {
        _expectedState = expectedState;
        return this;
    }

    #region ->> Consistency Checks <<-

    //
    // public AppendStreamRequestBuilder WithCheck(ConsistencyCheck check) {
    //     _checks.Add(check);
    //     return this;
    // }
    //
    // public AppendStreamRequestBuilder WithChecks(params ConsistencyCheck[] checks) {
    //     foreach (var check in checks)
    //         _checks.Add(check);
    //     return this;
    // }

    #endregion

    #region ->> Records <<-

    public AppendStreamRequestBuilder WithRecord(AppendRecord record) {
        _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(AppendRecordBuilder builder) {
        _builders.Add(builder.ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> configure) {
        var builder = AppendRecord.New;
        configure(builder);
        _builders.Add(builder.ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
        _builders.Add(AppendRecord.New.ForStream(_stream).WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    public AppendStreamRequestBuilder WithRecords(params AppendRecord[] records) {
        foreach (var record in records)
            _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecords(IEnumerable<AppendRecord> records) {
        foreach (var record in records)
            _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    #endregion

    // #region ->> Batch Overrides <<-
    //
    // public AppendStreamRequestBuilder WithDataFormat(SchemaDataFormat dataFormat) {
    //     _dataFormatOverride = dataFormat;
    //     return this;
    // }
    //
    // public AppendStreamRequestBuilder WithMetadata(string key, object? value = null) {
    //     _metadataOverrides[key] = value;
    //     return this;
    // }
    //
    // public AppendStreamRequestBuilder WithMetadata<T>(KeyValuePair<string, T> entry) {
    //     _metadataOverrides[entry.Key] = entry.Value;
    //     return this;
    // }
    //
    // public AppendStreamRequestBuilder WithMetadata<T>(string key, T? value = default) {
    //     _metadataOverrides[key] = value;
    //     return this;
    // }
    //
    // #endregion

    public AppendStreamRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendStreamRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();


        var records = ImmutableArray.CreateRange(_builders.Select(b => b.Build()));
        // var records = ImmutableArray.CreateBuilder<AppendRecord>(_builders.Count);
        //
        //
        // ImmutableArray.CreateRange()
        // foreach (var builder in _builders) {
        //     // builder.ForStream(_stream);
        //
        //     // if (_dataFormatOverride.HasValue)
        //     //     builder.WithDataFormat(_dataFormatOverride.Value);
        //     //
        //     // foreach (var (key, value) in _metadataOverrides)
        //     //     builder.WithMetadata(key, value);
        //
        //     records.Add(builder.Build());
        // }

        // var checks = ImmutableArray.CreateBuilder<ConsistencyCheck>();
        //
        // if (_expectedState.HasValue)
        //     checks.Add(new StreamStateCheck(_stream, _expectedState.Value));

        // checks.AddRange(_checks);

        // return new AppendStreamRequest(_stream, records.MoveToImmutable(), checks.ToImmutable());

        return new AppendStreamRequest(_stream, records, _expectedState);
    }
}

public static partial class AppendStreamRequestBuilderExtensions {
    extension(AppendStreamRequestBuilder builder) {
        public ValueTask<AppendStreamResponse> ExecuteAsync(StreamsClient client, CancellationToken cancellationToken = default) =>
            client.AppendStream(builder.Build(), cancellationToken);
    }
}
