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
    readonly StreamName                _stream;
    readonly List<AppendRecordBuilder> _builders = [];

    ExpectedStreamState _expectedState = ExpectedStreamState.Any;

    internal AppendStreamRequestBuilder(StreamName stream) => _stream = stream;

    public int RecordsCount => _builders.Count;

    public AppendStreamRequestBuilder ExpectingState(ExpectedStreamState expectedState) {
        if (expectedState.Value is Revision revision && revision == Revision.Unset)
            return this;

        if (expectedState == ExpectedStreamState.Any)
            return this;

        _expectedState = expectedState;
        return this;
    }

    #region ->> Records <<-

    public AppendStreamRequestBuilder WithRecord(AppendRecord record) {
        _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> build) {
        var builder = AppendRecord.New;
        build(builder);
        _builders.Add(builder.ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord<T>(T value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) where T : notnull {
        _builders.Add(AppendRecord.New.ForStream(_stream).WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord<T>(T value, Metadata metadata, SchemaDataFormat dataFormat = SchemaDataFormat.Json) where T : notnull {
        _builders.Add(AppendRecord.New.ForStream(_stream).WithValue(value).WithMetadata(metadata).WithDataFormat(dataFormat));
        return this;
    }

    public AppendStreamRequestBuilder WithRecords(IEnumerable<AppendRecord> records) {
        foreach (var record in records)
            _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    #endregion

    public AppendStreamRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendStreamRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateRange(_builders.Select(b => b.Build()));

        return new AppendStreamRequest(_stream, records, _expectedState);
    }
}

public static partial class AppendStreamRequestBuilderExtensions {
    extension(AppendStreamRequestBuilder builder) {
        public ValueTask<AppendStreamResponse> ExecuteAsync(StreamsClient client, CancellationToken cancellationToken = default) =>
            client.AppendStream(builder.Build(), cancellationToken);
    }
}
