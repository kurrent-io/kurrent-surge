namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Record> InspectRecord(LogPosition position, CancellationToken cancellationToken = default) =>
            client.InspectRecord(position, true, cancellationToken);

        #region ->> Read Log <--

        public ValueTask<RecordStream> ReadRecords(LogPosition start, CancellationToken cancellationToken = default) =>
            client.ReadRecords(
                ReadLog.From(start)
                    .WithCancellationToken(cancellationToken)
                    .Build()
            );

        #endregion

        #region ->> Read Streams <<-

        public ValueTask<RecordStream> ReadStreamRecords(StreamName stream, Revision start, CancellationToken cancellationToken = default) =>
            client.ReadRecords(
                ReadStream.From(stream)
                    .FromRevision(start)
                    .WithCancellationToken(cancellationToken)
                    .Build()
            );

        public ValueTask<RecordStream> ReadStreamRecords(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadRecords(
                ReadStream.From(stream)
                    .WithCancellationToken(cancellationToken)
                    .Build()
            );

        public ValueTask<Record> ReadFirstStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Forwards, skipDecoding, cancellationToken);

        public ValueTask<Record> ReadLastStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Backwards, skipDecoding, cancellationToken);

        public ValueTask<Record> ReadFirstStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadFirstStreamRecord(stream, false, cancellationToken);

        public ValueTask<Record> ReadLastStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadLastStreamRecord(stream, false, cancellationToken);

        #endregion
    }
}
