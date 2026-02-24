namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        #region . Inspect .

        public async ValueTask<Record> InspectRecordAsync(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) =>
            await client.InspectRecord(position, skipDecoding, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region . Read Log .

        public async ValueTask<RecordStream> ReadRecordsAsync(ReadOptions options) =>
            await client.ReadRecords(options).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<RecordStream> ReadRecordsAsync(LogPosition start, CancellationToken cancellationToken = default) =>
            await client.ReadRecords(start, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region . Read Streams .

        public async ValueTask<RecordStream> ReadStreamRecordsAsync(ReadStreamOptions options) =>
            await client.ReadRecords(options).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<RecordStream> ReadStreamRecordsAsync(StreamName stream, Revision start, CancellationToken cancellationToken = default) =>
            await client.ReadStreamRecords(stream, start, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<RecordStream> ReadStreamRecordsAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            await client.ReadStreamRecords(stream, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Record> ReadFirstStreamRecordAsync(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            await client.ReadStreamEdge(stream, ReadDirection.Forwards, skipDecoding, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Record> ReadLastStreamRecordAsync(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            await client.ReadStreamEdge(stream, ReadDirection.Backwards, skipDecoding, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Record> ReadFirstStreamRecordAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            await client.ReadFirstStreamRecordAsync(stream, false, cancellationToken).ConfigureAwait(false);

        public async ValueTask<Record> ReadLastStreamRecordAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            await client.ReadLastStreamRecordAsync(stream, false, cancellationToken).ConfigureAwait(false);

        #endregion
    }
}
