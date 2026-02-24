namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Result<LogPosition, DeleteStreamError>> Delete(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Delete(stream, Revision.Unset, cancellationToken);

        public ValueTask<Result<LogPosition, TombstoneStreamError>> Tombstone(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Tombstone(stream, Revision.Unset, cancellationToken);

        public ValueTask<Result<Revision, SetStreamMetadataError>> SetStreamMetadata(StreamName stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
            client.SetStreamMetadata(stream, metadata, Revision.Unset, cancellationToken);

        public ValueTask<Result<Revision, SetStreamMetadataError>> ConfigureStreamDataRetention(StreamName stream, StreamDataRetentionOptions options, CancellationToken cancellationToken) =>
            client.ConfigureStreamDataRetention(stream, options, Revision.Unset, cancellationToken);
    }
}

public static partial class StreamsClientIdiomaticExtensions {
    extension(StreamsClient client) {
        public ValueTask<LogPosition> DeleteAsync(StreamName stream, Revision revision, CancellationToken cancellationToken = default) =>
            client.Delete(stream, revision, cancellationToken).UnwrapAsync();

        public ValueTask<LogPosition> TombstoneAsync(StreamName stream, Revision revision, CancellationToken cancellationToken = default) =>
            client.Tombstone(stream, revision, cancellationToken).UnwrapAsync();

        public ValueTask<LogPosition> DeleteAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            DeleteAsync(client, stream, Revision.Unset, cancellationToken);

        public ValueTask<LogPosition> TombstoneAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            TombstoneAsync(client, stream, Revision.Unset, cancellationToken);

        public ValueTask<Revision> SetStreamMetadataAsync(StreamName stream, StreamMetadata metadata, Revision expectedRevision, CancellationToken cancellationToken = default) =>
            client.SetStreamMetadata(stream, metadata, expectedRevision, cancellationToken).UnwrapAsync();

        public ValueTask<Revision> SetStreamMetadataAsync(StreamName stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
            SetStreamMetadataAsync(client, stream, metadata, Revision.Unset, cancellationToken);

        public ValueTask<Revision> ConfigureStreamDataRetentionAsync(StreamName stream, StreamDataRetentionOptions options, Revision metaRevision, CancellationToken cancellationToken) =>
            client.ConfigureStreamDataRetention(stream, options, metaRevision, cancellationToken).UnwrapAsync();

        public ValueTask<Revision> ConfigureStreamDataRetentionAsync(StreamName stream, StreamDataRetentionOptions options, CancellationToken cancellationToken) =>
            ConfigureStreamDataRetentionAsync(client, stream, options, Revision.Unset, cancellationToken);
    }
}
