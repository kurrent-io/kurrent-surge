namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Result<Record, InspectRecordError>> InspectRecord(LogPosition position, CancellationToken cancellationToken = default) =>
            client.InspectRecord(position, true, cancellationToken);

        #region . Read Log .

        public ValueTask<Result<Messages, ReadError>> Read(Func<ReadOptions, ReadOptions>? configure = null) {
            var options = new ReadOptions();
            options = configure?.Invoke(options) ?? options;
            return client.Read(options);
        }

        public ValueTask<Result<Messages, ReadError>> Read(LogPosition start, CancellationToken cancellationToken) =>
            client.Read(options => options with { Start = start, CancellationToken = cancellationToken });

        #endregion

        #region . Read Streams .

        public ValueTask<Result<Messages, ReadError>> ReadStream(Func<ReadStreamOptions, ReadStreamOptions>? configure = null) {
            var options = new ReadStreamOptions();
            options = configure?.Invoke(options) ?? options;
            return client.Read(options);
        }

        public ValueTask<Result<Messages, ReadError>> ReadStream(StreamName stream, StreamRevision start, CancellationToken cancellationToken) =>
            client.ReadStream(options => options with { Stream = stream, Start = start, CancellationToken = cancellationToken });

        public ValueTask<Result<Messages, ReadError>> ReadStream(StreamName stream, CancellationToken cancellationToken) =>
            client.ReadStream(options => options with { Stream = stream, CancellationToken = cancellationToken });

        public ValueTask<Result<Record, ReadError>> ReadFirstStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Forwards, skipDecoding, cancellationToken);

        public ValueTask<Result<Record, ReadError>> ReadLastStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Backwards, skipDecoding, cancellationToken);

        public ValueTask<Result<Record, ReadError>> ReadFirstStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            ReadFirstStreamRecord(client, stream, true, cancellationToken);

        public ValueTask<Result<Record, ReadError>> ReadLastStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            ReadLastStreamRecord(client, stream, true, cancellationToken);

        #endregion
    }
}
