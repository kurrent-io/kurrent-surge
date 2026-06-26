namespace Kurrent.Client.Streams;

public interface IStreamReader {
    IAsyncEnumerable<SubscribeMessage> Subscribe(SubscribeOptions options, CancellationToken ct = default);

    IAsyncEnumerable<ReadMessage> ReadRecords(ReadRecordsOptions options, CancellationToken ct = default);

    IAsyncEnumerable<ReadMessage> ReadStream(StreamName stream, ReadStreamOptions options, CancellationToken ct = default);

    ValueTask<Record> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken ct = default);
}

public interface IStreamAppender {
    ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        AppendRecordsOptions? options,
        CancellationToken cancellationToken = default
    );

    ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        IAsyncEnumerable<AppendRecord> records,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default
    );
}

public interface IStreamManager {
    ValueTask<StreamInfo> GetStreamInfo(StreamName stream, CancellationToken cancellationToken = default);

    ValueTask<LogPosition> Delete(StreamName stream, Revision revision, CancellationToken cancellationToken = default);

    ValueTask<LogPosition> Tombstone(StreamName stream, Revision revision, CancellationToken cancellationToken = default);

    ValueTask<Success> Truncate(StreamName stream, Revision truncateRevision, CancellationToken cancellationToken = default);

    ValueTask<Revision> SetCustomMetadata(
        StreamName stream,
        Metadata metadata,
        Revision revision,
        CancellationToken cancellationToken = default
    );

    ValueTask<Revision> ConfigureDataRetention(
        StreamName stream,
        StreamRetentionOptions options,
        Revision revision,
        CancellationToken cancellationToken = default
    );
}

public interface IStreamsClient : IStreamReader, IStreamAppender, IStreamManager;
