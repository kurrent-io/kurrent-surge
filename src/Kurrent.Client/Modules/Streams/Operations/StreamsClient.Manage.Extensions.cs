namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<LogPosition> Delete(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Delete(stream, Revision.Unset, cancellationToken);

        public ValueTask<LogPosition> Tombstone(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Tombstone(stream, Revision.Unset, cancellationToken);

        public ValueTask<Revision> SetStreamMetadata(StreamName stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
            client.SetStreamMetadata(stream, metadata, Revision.Unset, cancellationToken);

        public ValueTask<Revision> ConfigureStreamDataRetention(StreamName stream, StreamDataRetentionOptions options, CancellationToken cancellationToken) =>
            client.ConfigureStreamDataRetention(stream, options, Revision.Unset, cancellationToken);

        // public ValueTask<Result<Revision, SetStreamMetadataError>> SetDataRetention(StreamName stream, int? maxCount, TimeSpan? maxAge, CancellationToken cancellationToken) =>
        //     ConfigureStreamDataRetention(stream, maxCount, maxAge, Revision.Unset, cancellationToken);
        //
        // public ValueTask<Result<Revision, SetStreamMetadataError>> SetDataRetention(StreamName stream, int maxCount, CancellationToken cancellationToken) {
        //     ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        //     ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCount, int.MaxValue);
        //     return SetDataRetention(stream, maxCount, null, Revision.Unset, cancellationToken);
        // }
        //
        // public ValueTask<Result<Revision, SetStreamMetadataError>> SetDataRetention(StreamName stream, TimeSpan maxAge, CancellationToken cancellationToken) {
        //     ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);
        //     ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxAge, TimeSpan.MaxValue);
        //     return SetDataRetention(stream, null, maxAge, Revision.Unset, cancellationToken);
        // }
    }
}
