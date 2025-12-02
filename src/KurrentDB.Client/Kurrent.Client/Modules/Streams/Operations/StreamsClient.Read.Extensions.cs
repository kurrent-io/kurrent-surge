namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Result<Record, InspectRecordError>> InspectRecord(LogPosition position, CancellationToken cancellationToken = default) =>
            client.InspectRecord(position, true, cancellationToken);

        #region . Read Log .

        public ValueTask<Result<RecordStream, ReadError>> ReadRecords(Func<ReadOptions, ReadOptions>? configure = null) {
            var options = new ReadOptions();
            options = configure?.Invoke(options) ?? options;
            return client.ReadRecords(options);
        }

        public ValueTask<Result<RecordStream, ReadError>> ReadRecords(LogPosition start, CancellationToken cancellationToken) =>
            client.ReadRecords(options => options with { Start = start, CancellationToken = cancellationToken });

        #endregion

        #region . Read Streams .

        public ValueTask<Result<RecordStream, ReadError>> ReadStreamRecords(Func<ReadStreamOptions, ReadStreamOptions>? configure = null) {
            var options = new ReadStreamOptions();
            options = configure?.Invoke(options) ?? options;
            return client.ReadRecords(options);
        }

        public ValueTask<Result<RecordStream, ReadError>> ReadStreamRecords(StreamName stream, StreamRevision start, CancellationToken cancellationToken) =>
            client.ReadStreamRecords(options => options with { Stream = stream, Start = start, CancellationToken = cancellationToken });

        public ValueTask<Result<RecordStream, ReadError>> ReadStreamRecords(StreamName stream, CancellationToken cancellationToken) =>
            client.ReadStreamRecords(options => options with { Stream = stream, CancellationToken = cancellationToken });

        public ValueTask<Result<Record, ReadError>> ReadFirstStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Forwards, skipDecoding, cancellationToken);

        public ValueTask<Result<Record, ReadError>> ReadLastStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Backwards, skipDecoding, cancellationToken);

        public ValueTask<Result<Record, ReadError>> ReadFirstStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            ReadFirstStreamRecord(client, stream, false, cancellationToken);

        public ValueTask<Result<Record, ReadError>> ReadLastStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            ReadLastStreamRecord(client, stream, false, cancellationToken);

        #endregion
    }
}
